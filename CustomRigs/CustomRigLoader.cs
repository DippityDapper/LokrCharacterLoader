using System;
using System.Collections.Generic;
using System.IO;
using Ironhide.ExoSkeleton;
using LokrModAPI;
using SimpleJSON;
using UnityEngine;

namespace LokrCharacterLoader.CustomRigs
{
	/// <summary>Loads mod-provided custom skeleton/animation rigs from a rig.json + PNGs folder.</summary>
	/// <remarks>
	/// Loads custom ExoSkeleton rigs from Mods/*/Characters/&lt;RigId&gt;/ -- a character folder
	/// holding &lt;RigId&gt;/rig/rig.json (the exact schema ExoSkeletonDataAsset.ReloadData
	/// expects) plus &lt;RigId&gt;/sprites/*.png, one per part named to match a "name" entry in
	/// rig.json's "parts" list. Folders with no rig/rig.json are skipped silently -- an
	/// EnemySummon that uses a vanilla Model (no custom art) is still a valid Characters/&lt;Id&gt;/
	/// unit-definition folder, just not a custom rig. That schema and the multi-part atlas-packing requirement
	/// (ExoSkeletonRenderer only reads partSprites[0].texture for the whole mesh -- see
	/// ModAPI.Assets.PackSprites) were worked out and proven against real game art in
	/// LokrCharacterLab's RealCharacterRig/PosedRealCharacterRig; this reuses both directly rather
	/// than inventing a separate mod format.
	///
	/// RigId (the folder name) is the same string a hero's UnitDefinition.metaExo would reference
	/// -- matches how AssetBundleManager resolves the vanilla equivalent, so no extra "which hero"
	/// field is needed in rig.json.
	///
	/// Public surface is deliberately narrow: LokrCharacterLab's rig editor calls BuildFromFolder
	/// directly for its Preview feature. Everything else here stays internal-in-effect
	/// (RegisterDefaults/Resolve are only ever invoked through the CharacterAPI resolver chain,
	/// not called directly by other plugins).
	/// </remarks>
	public static class CustomRigLoader
	{
		private const string Category = "Characters";

		/// <summary>Animation names every hero rig must have, or the adventure map/hero bar/buff store/reward screen/dialog views will crash when displaying that hero.</summary>
		/// <remarks>Traced every call site that reads Hero.exoSkeletonDataAsset across the base game -- they all hardcode one of these animation names and throw a C# exception if it's missing, silently, deep in game code, with a stack trace that gives no hint it's about a missing animation. A rig missing these will build fine and work in the Lab scene (which never touches Hero) but break the adventure map the moment it's actually assigned to a hero.</remarks>
		private static readonly string[] RequiredAnimationNames = { "Stand", "Portrait", "StandStatic" };

		/// <summary>Cinematic clips the adventure-map intro plays on the unit. Missing Speak logs "Animation Speak doesn't exist" and skips the talk pose; we alias them to Stand.</summary>
		private static readonly string[] CinematicAliasNames = { "Speak" };

		private static readonly Dictionary<string, string> rigFoldersById = new Dictionary<string, string>();
		private static readonly Dictionary<string, ExoSkeletonDataAsset> builtRigsById = new Dictionary<string, ExoSkeletonDataAsset>();
		private static bool indexed;

		/// <summary>Registers this loader as the ExoSkeleton resolver.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.RegisterExoSkeletonResolver(Resolve);
		}

		/// <summary>Finds rig folders and checks rig.json exists, once.</summary>
		/// <remarks>Cheap: the expensive part (loading every PNG, packing an atlas, calling ReloadData) is deferred to Resolve() and only ever happens for rigs a hero actually asks for, once, then cached like vanilla does.</remarks>
		private static void EnsureIndexed()
		{
			if (indexed)
			{
				return;
			}
			indexed = true;

			IndexCategory(Category);
			IndexCategory("LokrCharacterLab");
		}

		private static void IndexCategory(string category)
		{
			foreach ((string _, string rigFolder) in ModAPI.Files.EnumerateCategorySubfolders(category))
			{
				string rigId = Path.GetFileName(rigFolder);
				if (!File.Exists(Path.Combine(rigFolder, "rig", "rig.json")))
				{
					continue;
				}
				if (rigFoldersById.ContainsKey(rigId))
				{
					LokrCharacterLoaderPlugin.Log.LogWarning("CustomRigLoader: duplicate rig id '" + rigId + "' — keeping the first one found.");
					continue;
				}
				rigFoldersById[rigId] = rigFolder;
			}
		}

		/// <summary>Resolves and caches a hero's ExoSkeleton rig by metaExo name, building it from its mod folder on first use.</summary>
		/// <remarks>A failed build (no matching parts) is not stored in builtRigsById, so a later live reload can retry.</remarks>
		private static ExoSkeletonDataAsset Resolve(string metaExoName)
		{
			EnsureIndexed();
			if (metaExoName == null || !rigFoldersById.TryGetValue(metaExoName, out string rigFolder))
			{
				return null;
			}
			if (builtRigsById.TryGetValue(metaExoName, out ExoSkeletonDataAsset cached))
			{
				return cached;
			}

			ExoSkeletonDataAsset asset = Build(metaExoName, rigFolder);
			if (asset == null)
			{
				return null;
			}
			builtRigsById[metaExoName] = asset;
			return asset;
		}

		/// <summary>Builds a rig from any character folder on demand, without the mod-folder indexing/caching Resolve() uses.</summary>
		/// <remarks>
		/// Exposed for LokrCharacterLab's rig editor "Preview" feature -- takes a
		/// &lt;folder&gt;/rig/rig.json plus &lt;folder&gt;/sprites/*.png, same layout as a real
		/// Mods/*/Characters/&lt;RigId&gt;/ folder. Callers own the returned asset; nothing here
		/// caches it. Returns null when every JSON part name misses the packed atlas (build failed).
		/// </remarks>
		public static ExoSkeletonDataAsset BuildFromFolder(string rigId, string folderPath) => Build(rigId, folderPath);

		/// <summary>Loads a rig's textures and JSON from its folder, packs an atlas, and builds the ExoSkeletonDataAsset.</summary>
		/// <remarks>Pre-filters rig.json so vanilla ReloadData never reads sprite.vertices on a FindSprite miss (that path logs then NREs). A fully unmatched parts list fails the build and is not cached.</remarks>
		private static ExoSkeletonDataAsset Build(string rigId, string rigFolder)
		{
			string jsonText = File.ReadAllText(Path.Combine(rigFolder, "rig", "rig.json"));

			Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
			foreach (string pngPath in Directory.GetFiles(Path.Combine(rigFolder, "sprites"), "*.png"))
			{
				string partName = Path.GetFileNameWithoutExtension(pngPath);
				textures[partName] = ModAPI.Assets.LoadTexture(pngPath);
			}

			Dictionary<string, Sprite> sprites = ModAPI.Assets.PackSprites(textures);

			string filteredJson = FilterJsonForReload(jsonText, sprites, rigId);
			if (filteredJson == null)
			{
				return null;
			}

			ExoSkeletonDataAsset asset = ScriptableObject.CreateInstance<ExoSkeletonDataAsset>();
			asset.pixelsToUnits = 100f;
			asset.ReloadData(filteredJson, new List<Sprite>(sprites.Values));
			EnsureCinematicAliases(rigId, asset);

			int partCount = asset.parts != null ? asset.parts.Count : 0;
			int animCount = asset.animations != null ? asset.animations.Length : 0;
			LokrCharacterLoaderPlugin.Log.LogInfo(string.Format(
				"CustomRigLoader: built '{0}' from '{1}' ({2} parts, {3} animations).",
				rigId, rigFolder, partCount, animCount));
			WarnIfMissingRequiredAnimations(rigId, asset);
			return asset;
		}

		/// <summary>Omits rig.json parts (and frame references to them) whose names do not match a packed sprite, matching LoadParts' silent skip.</summary>
		/// <remarks>Vanilla ReloadData logs "Cant find sprite named" then immediately reads sprite.vertices. This project's SimpleJSON ToString() returns the literal NOT, so the filtered tree is serialized with ToJSON(0).</remarks>
		private static string FilterJsonForReload(string jsonText, Dictionary<string, Sprite> sprites, string rigId)
		{
			JSONNode root = JSON.Parse(jsonText);
			if (root == null)
			{
				LokrCharacterLoaderPlugin.Log.LogError("CustomRigLoader: rig '" + rigId + "' rig.json failed to parse.");
				return null;
			}

			JSONArray keptParts = new JSONArray();
			HashSet<string> keptNames = new HashSet<string>();
			foreach (JSONNode partNode in root["parts"].Children)
			{
				string name = partNode["name"].Value;
				if (HasPackedSprite(sprites, name))
				{
					keptParts.Add(partNode);
					keptNames.Add(name);
				}
				else
				{
					LokrCharacterLoaderPlugin.Log.LogWarning("Cant find sprite named: " + name);
				}
			}

			if (keptNames.Count == 0)
			{
				LokrCharacterLoaderPlugin.Log.LogError(string.Format(
					"CustomRigLoader: rig '{0}' has no parts matching packed sprites — build failed.",
					rigId));
				return null;
			}

			root["parts"] = keptParts;

			foreach (JSONNode animationNode in root["animations"].Children)
			{
				foreach (JSONNode frameNode in animationNode["frames"].Children)
				{
					JSONArray keptFrameParts = new JSONArray();
					foreach (JSONNode framePart in frameNode["parts"].Children)
					{
						if (keptNames.Contains(framePart["name"].Value))
						{
							keptFrameParts.Add(framePart);
						}
					}
					frameNode["parts"] = keptFrameParts;
				}
			}

			return root.ToJSON(0);
		}

		/// <summary>True when a packed sprite matches the part name using ExoSkeletonDataAsset.FindSprite rules.</summary>
		/// <remarks>Case-insensitive; a '#' suffix is stripped from the sprite name only, matching vanilla FindSprite.</remarks>
		private static bool HasPackedSprite(Dictionary<string, Sprite> sprites, string partName)
		{
			if (string.IsNullOrEmpty(partName) || sprites == null)
			{
				return false;
			}

			foreach (Sprite sprite in sprites.Values)
			{
				if (sprite == null)
				{
					continue;
				}

				if (ContentRules.PackedSpriteNameMatchesPart(sprite.name, partName))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Clears the rig folder index and built-asset caches so the next resolve re-reads disk.</summary>
		internal static void ClearCaches()
		{
			foreach (ExoSkeletonDataAsset asset in builtRigsById.Values)
			{
				if (asset != null)
				{
					UnityEngine.Object.Destroy(asset);
				}
			}
			builtRigsById.Clear();
			rigFoldersById.Clear();
			indexed = false;
		}

		/// <summary>Copies Stand onto missing cinematic clip names so map intros do not log "Animation Speak doesn't exist".</summary>
		private static void EnsureCinematicAliases(string rigId, ExoSkeletonDataAsset asset)
		{
			if (asset.animations == null || asset.animations.Length == 0)
			{
				return;
			}

			Ironhide.ExoSkeleton.Animation stand = FindAnimation(asset, "Stand");
			if (stand == null)
			{
				return;
			}

			List<string> added = new List<string>();
			foreach (string name in CinematicAliasNames)
			{
				if (FindAnimation(asset, name) != null)
				{
					continue;
				}

				Ironhide.ExoSkeleton.Animation alias = new Ironhide.ExoSkeleton.Animation
				{
					name = name,
					frames = stand.frames,
					loopsByDefault = stand.loopsByDefault,
					duration = stand.duration,
					moveCurve = stand.moveCurve,
				};
				Ironhide.ExoSkeleton.Animation[] next = new Ironhide.ExoSkeleton.Animation[asset.animations.Length + 1];
				Array.Copy(asset.animations, next, asset.animations.Length);
				next[next.Length - 1] = alias;
				asset.animations = next;
				added.Add(name);
			}

			if (added.Count > 0)
			{
				LokrCharacterLoaderPlugin.Log.LogInfo(string.Format(
					"CustomRigLoader: rig '{0}' had no {1} — aliased to Stand so cinematics can play.",
					rigId, string.Join("/", added.ToArray())));
			}
		}

		private static Ironhide.ExoSkeleton.Animation FindAnimation(ExoSkeletonDataAsset asset, string name)
		{
			foreach (Ironhide.ExoSkeleton.Animation animation in asset.animations)
			{
				if (animation != null && animation.name == name)
				{
					return animation;
				}
			}

			return null;
		}

		/// <summary>Logs a warning if the rig is missing "Stand" or "Portrait"/"StandStatic" -- see RequiredAnimationNames.</summary>
		private static void WarnIfMissingRequiredAnimations(string rigId, ExoSkeletonDataAsset asset)
		{
			bool hasStand = false;
			bool hasPortraitOrStandStatic = false;
			foreach (Animation animation in asset.animations)
			{
				if (animation.name == "Stand")
				{
					hasStand = true;
				}
				else if (animation.name == "Portrait" || animation.name == "StandStatic")
				{
					hasPortraitOrStandStatic = true;
				}
			}
			if (!hasStand || !hasPortraitOrStandStatic)
			{
				LokrCharacterLoaderPlugin.Log.LogWarning(string.Format(
					"CustomRigLoader: rig '{0}' is missing required animation(s) ({1}{2}) — assigning this rig to a real hero WILL crash the adventure map, hero bar, buff store, reward screen, or dialog views the moment they try to display that hero. This rig is only safe to use in LokrCharacterLab's test scene until fixed.",
					rigId,
					hasStand ? "" : "\"Stand\"",
					hasPortraitOrStandStatic ? "" : (hasStand ? "\"Portrait\" or \"StandStatic\"" : ", \"Portrait\" or \"StandStatic\"")));
			}
		}
	}
}
