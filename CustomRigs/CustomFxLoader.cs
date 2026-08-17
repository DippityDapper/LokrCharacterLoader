using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Ironhide.AssetBundles;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Game.FX;
using Ironhide.Legends.View.Game.Projectiles;
using LokrModAPI;
using UnityEngine;

namespace LokrCharacterLoader.CustomRigs
{
	/// <summary>Builds sprite FXMega / projectile prefabs from on-disk folders and injects them into the game dictionaries.</summary>
	/// <remarks>
	/// Phase 5 thinner workaround from ability-vfx-animation.html: Ability Lab authors a folder
	/// name + PNG + JSON; this loader (not Ability Lab) instantiates a minimal FXMegaComponent /
	/// projectile graph so LoadFXMega does not throw. Full Unity particle AssetBundles are still
	/// out of scope. Clip names are scraped from Character Lab rig.json files as strings only.
	/// </remarks>
	internal static class CustomFxLoader
	{
		private const string AbilityCategory = "LokrAbilityLab";
		private const string LegacyAbilityCategory = "Abilities";
		private const string CharacterCategory = "LokrCharacterLab";
		private const string LegacyCharacterCategory = "Characters";
		private const string FlatFxCategory = "FXMega";
		private const string FlatProjectileCategory = "Projectiles";

		private static readonly Regex ClipNamePattern = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

		private static readonly Dictionary<string, GameObject> builtFx = new Dictionary<string, GameObject>(StringComparer.Ordinal);
		private static readonly Dictionary<string, GameObject> builtProjectiles = new Dictionary<string, GameObject>(StringComparer.Ordinal);
		private static readonly HashSet<string> fxNames = new HashSet<string>(StringComparer.Ordinal);
		private static readonly HashSet<string> projectileNames = new HashSet<string>(StringComparer.Ordinal);
		private static readonly HashSet<string> clipNames = new HashSet<string>(StringComparer.Ordinal);
		private static Transform hideRoot;
		private static bool resolversRegistered;

		internal static IReadOnlyCollection<string> FxNames => fxNames;
		internal static IReadOnlyCollection<string> ProjectileNames => projectileNames;
		internal static IReadOnlyCollection<string> ClipNames => clipNames;

		/// <summary>Wires the file-convention resolvers and does the first disk scan.</summary>
		internal static void RegisterDefaults()
		{
			if (!resolversRegistered)
			{
				CharacterAPI.RegisterFxMegaResolver(ResolveFx);
				CharacterAPI.RegisterProjectileResolver(ResolveProjectile);
				resolversRegistered = true;
			}

			Refresh();
		}

		/// <summary>Returns a built FXMega prefab, scanning disk for that name if the cache missed or the object was destroyed.</summary>
		internal static GameObject ResolveFx(string name) =>
			ResolveOrBuild(name, builtFx, TryBuildFxSafe);

		/// <summary>Returns a built projectile prefab, scanning disk for that name if the cache missed or the object was destroyed.</summary>
		internal static GameObject ResolveProjectile(string name) =>
			ResolveOrBuild(name, builtProjectiles, TryBuildProjectileSafe);

		/// <summary>Re-reads every convention folder, rebuilds prefabs, and injects into FXManager when it is ready.</summary>
		internal static void Refresh()
		{
			EnsureHideRoot();
			DestroyBuilt(builtFx);
			DestroyBuilt(builtProjectiles);
			fxNames.Clear();
			projectileNames.Clear();
			clipNames.Clear();

			foreach (string folder in EnumerateNamedFolders("fx", FlatFxCategory))
			{
				TryBuildFxSafe(folder);
			}

			foreach (string folder in EnumerateNamedFolders("projectiles", FlatProjectileCategory))
			{
				TryBuildProjectileSafe(folder);
			}

			CollectClipNames();
			InjectIntoFxManager();
			LokrCharacterLoaderPlugin.Log.LogInfo(string.Format(
				"CustomFxLoader: {0} sprite FX, {1} projectiles, {2} clip names.",
				builtFx.Count, builtProjectiles.Count, clipNames.Count));
		}

		/// <summary>Adds built FXMega prefabs to FXManager.fxMegaPrefabs after Preload (or on refresh).</summary>
		internal static void InjectIntoFxManager()
		{
			if (!MonoSingleton<FXManager>.IsInstanceValid)
			{
				return;
			}

			Dictionary<string, GameObject> dict = MonoSingleton<FXManager>.Instance.fxMegaPrefabs;
			if (dict == null)
			{
				return;
			}

			foreach (KeyValuePair<string, GameObject> pair in builtFx)
			{
				if (pair.Value == null)
				{
					continue;
				}

				dict[pair.Key] = pair.Value;
			}
		}

		private static GameObject ResolveOrBuild(string name, Dictionary<string, GameObject> built, Action<string> buildFolder)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			if (built.TryGetValue(name, out GameObject prefab) && prefab != null)
			{
				return prefab;
			}

			EnsureHideRoot();
			foreach (string folder in EnumerateNamedFolders(built == builtFx ? "fx" : "projectiles",
				built == builtFx ? FlatFxCategory : FlatProjectileCategory))
			{
				if (!string.Equals(Path.GetFileName(folder), name, StringComparison.Ordinal))
				{
					continue;
				}

				buildFolder(folder);
				break;
			}

			built.TryGetValue(name, out prefab);
			return prefab != null ? prefab : null;
		}

		private static void TryBuildFxSafe(string folder)
		{
			try
			{
				TryBuildFx(folder);
			}
			catch (Exception ex)
			{
				LokrCharacterLoaderPlugin.Log.LogError("CustomFxLoader: failed FX '" + Path.GetFileName(folder) + "' — " + ex);
			}
		}

		private static void TryBuildProjectileSafe(string folder)
		{
			try
			{
				TryBuildProjectile(folder);
			}
			catch (Exception ex)
			{
				LokrCharacterLoaderPlugin.Log.LogError("CustomFxLoader: failed projectile '" + Path.GetFileName(folder) + "' — " + ex);
			}
		}

		private static void TryBuildFx(string folder)
		{
			string name = Path.GetFileName(folder);
			if (string.IsNullOrEmpty(name) || (builtFx.TryGetValue(name, out GameObject existing) && existing != null))
			{
				return;
			}

			SpriteFxSpec spec = ReadFxSpec(folder);
			Sprite sprite = LoadSprite(folder, spec.pixelsPerUnit);
			GameObject root = NewHiddenPrefab(name);

			FXMegaComponent mega = root.AddComponent<FXMegaComponent>();
			FXMegaController controller = root.AddComponent<FXMegaController>();
			controller.autoStart = false;

			GameObject inner = new GameObject(name + "_sprite");
			inner.transform.SetParent(root.transform, false);
			SpriteRenderer renderer = inner.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			renderer.sortingOrder = 20;

			FXComponentSimple fx = inner.AddComponent<FXComponentSimple>();
			fx.startDuration = spec.duration;
			fx.loops = spec.loops;
			fx.stopDuration = 0f;
			fx.autoKillAfterStop = !spec.loops;
			fx.autoPlay = false;

			List<FXMegaComponentAction> actions = new List<FXMegaComponentAction>();
			AddAction(actions, spec.createEvent, spec, fx);
			if (!string.Equals(spec.castCreateEvent, spec.createEvent, StringComparison.Ordinal))
			{
				AddAction(actions, spec.castCreateEvent, spec, fx);
			}

			mega.actions = actions;
			mega.finishEvent = spec.finishEvent;
			builtFx[name] = root;
			fxNames.Add(name);
		}

		private static void AddAction(List<FXMegaComponentAction> actions, string createEvent, SpriteFxSpec spec, FXComponentSimple fx)
		{
			if (string.IsNullOrEmpty(createEvent))
			{
				return;
			}

			actions.Add(new FXMegaComponentAction
			{
				createEventId = createEvent,
				removeEventId = spec.removeEvent,
				fxPrefab = fx,
				attachPoint = spec.attachPoint,
				detached = spec.detached,
				soundId = spec.soundId ?? string.Empty,
			});
		}

		private static void TryBuildProjectile(string folder)
		{
			string name = Path.GetFileName(folder);
			if (string.IsNullOrEmpty(name) || (builtProjectiles.TryGetValue(name, out GameObject existing) && existing != null))
			{
				return;
			}

			ProjectileSpec spec = ReadProjectileSpec(folder);
			Sprite sprite = LoadSprite(folder, spec.pixelsPerUnit);
			GameObject root = TryCloneVanillaProjectile(name, sprite, spec);
			if (root == null)
			{
				root = BuildThinProjectile(name, sprite, spec);
			}

			builtProjectiles[name] = root;
			projectileNames.Add(name);
		}

		/// <summary>Clones SimpleArrowProjectile from the scenario bundle and swaps sprites so movement/view wiring matches vanilla.</summary>
		private static GameObject TryCloneVanillaProjectile(string name, Sprite sprite, ProjectileSpec spec)
		{
			GameObject vanilla;
			try
			{
				vanilla = AssetBundleManager.LoadAsset<GameObject>("scenario", "SimpleArrowProjectile");
			}
			catch (Exception)
			{
				return null;
			}

			if (vanilla == null)
			{
				return null;
			}

			EnsureHideRoot();
			GameObject root = UnityEngine.Object.Instantiate(vanilla);
			UnityEngine.Object.DontDestroyOnLoad(root);
			root.name = name;
			root.hideFlags = HideFlags.HideAndDontSave;
			if (hideRoot != null)
			{
				root.transform.SetParent(hideRoot, false);
			}

			foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
			{
				renderer.sprite = sprite;
			}

			ProjectileForceMovementComponent movement = root.GetComponent<ProjectileForceMovementComponent>();
			if (movement != null)
			{
				movement.maxSpeed = spec.maxSpeed;
				movement.maxForce = spec.maxForce;
				movement.slowingDistance = spec.slowingDistance;
				movement.forceMultiplier = spec.forceMultiplier;
				movement.keepTrackingTarget = spec.keepTrackingTarget;
			}

			ProjectileViewComponent view = root.GetComponent<ProjectileViewComponent>();
			if (view != null)
			{
				view.ignoresRotation = spec.ignoresRotation;
				if (view.imageTransform == null)
				{
					view.imageTransform = root.transform;
				}

				if (view.projectileTransform == null)
				{
					view.projectileTransform = root.transform;
				}
			}

			return root;
		}

		private static GameObject BuildThinProjectile(string name, Sprite sprite, ProjectileSpec spec)
		{
			GameObject root = NewHiddenPrefab(name);

			SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			renderer.sortingOrder = 20;

			ProjectileForceMovementComponent movement = root.AddComponent<ProjectileForceMovementComponent>();
			movement.maxSpeed = spec.maxSpeed;
			movement.maxForce = spec.maxForce;
			movement.slowingDistance = spec.slowingDistance;
			movement.forceMultiplier = spec.forceMultiplier;
			movement.keepTrackingTarget = spec.keepTrackingTarget;

			root.AddComponent<ProjectileVisiblityControlSpriteAndParticles>();
			ProjectileViewComponent view = root.AddComponent<ProjectileViewComponent>();
			view.imageTransform = root.transform;
			view.projectileTransform = root.transform;
			view.ignoresRotation = spec.ignoresRotation;
			return root;
		}

		private static SpriteFxSpec ReadFxSpec(string folder)
		{
			SpriteFxSpec spec = ReadJson(Path.Combine(folder, "fx.json"), new SpriteFxSpec());
			spec.attachPoint = NormalizeAttachPoint(spec.attachPoint);

			if (string.IsNullOrEmpty(spec.createEvent))
			{
				spec.createEvent = "start";
			}

			if (string.IsNullOrEmpty(spec.castCreateEvent))
			{
				spec.castCreateEvent = "AbilityAction";
			}

			if (string.IsNullOrEmpty(spec.removeEvent))
			{
				spec.removeEvent = "AbilityEnd";
			}

			if (string.IsNullOrEmpty(spec.finishEvent))
			{
				spec.finishEvent = "AbilityEnd";
			}

			if (spec.duration <= 0f)
			{
				spec.duration = 0.6f;
			}

			if (spec.pixelsPerUnit <= 0f)
			{
				spec.pixelsPerUnit = 100f;
			}

			return spec;
		}

		private static ProjectileSpec ReadProjectileSpec(string folder)
		{
			ProjectileSpec spec = ReadJson(Path.Combine(folder, "projectile.json"), new ProjectileSpec());
			if (spec.maxSpeed <= 0f)
			{
				spec.maxSpeed = 8f;
			}

			if (spec.maxForce <= 0f)
			{
				spec.maxForce = 24f;
			}

			if (spec.slowingDistance <= 0f)
			{
				spec.slowingDistance = 0.4f;
			}

			if (spec.forceMultiplier <= 0f)
			{
				spec.forceMultiplier = 1f;
			}

			if (spec.pixelsPerUnit <= 0f)
			{
				spec.pixelsPerUnit = 100f;
			}

			return spec;
		}

		private static T ReadJson<T>(string path, T fallback) where T : class
		{
			if (!File.Exists(path))
			{
				return fallback;
			}

			try
			{
				T parsed = JsonUtility.FromJson<T>(File.ReadAllText(path));
				return parsed ?? fallback;
			}
			catch (Exception ex)
			{
				LokrCharacterLoaderPlugin.Log.LogWarning("CustomFxLoader: could not parse " + path + " — " + ex.Message);
				return fallback;
			}
		}

		private static Sprite LoadSprite(string folder, float pixelsPerUnit)
		{
			string path = FindPng(folder);
			if (path != null)
			{
				Texture2D texture = ModAPI.Assets.LoadTexture(path);
				return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f),
					pixelsPerUnit, 0, SpriteMeshType.FullRect);
			}

			LokrCharacterLoaderPlugin.Log.LogWarning("CustomFxLoader: no PNG in " + folder + " — using a placeholder square.");
			Texture2D fallback = new Texture2D(16, 16, TextureFormat.ARGB32, false);
			Color[] pixels = new Color[256];
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = new Color(1f, 0.85f, 0.2f, 0.9f);
			}

			fallback.SetPixels(pixels);
			fallback.Apply();
			return Sprite.Create(fallback, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f),
				pixelsPerUnit > 0f ? pixelsPerUnit : 100f, 0, SpriteMeshType.FullRect);
		}

		private static string FindPng(string folder)
		{
			string named = Path.Combine(folder, "sprite.png");
			if (File.Exists(named))
			{
				return named;
			}

			string[] files = Directory.GetFiles(folder, "*.png");
			return files.Length > 0 ? files[0] : null;
		}

		private static IEnumerable<string> EnumerateNamedFolders(string nestedName, string flatCategory)
		{
			foreach ((string _, string libraryFolder) in ModAPI.Files.EnumerateCategorySubfolders(AbilityCategory))
			{
				if (!Directory.Exists(libraryFolder))
				{
					continue;
				}

				string shared = Path.Combine(libraryFolder, nestedName);
				if (Directory.Exists(shared))
				{
					foreach (string child in Directory.GetDirectories(shared))
					{
						yield return child;
					}
				}

				foreach (string abilityFolder in Directory.GetDirectories(libraryFolder))
				{
					string nested = Path.Combine(abilityFolder, nestedName);
					if (!Directory.Exists(nested))
					{
						continue;
					}

					foreach (string child in Directory.GetDirectories(nested))
					{
						yield return child;
					}
				}
			}

			foreach ((string _, string abilityFolder) in ModAPI.Files.EnumerateCategorySubfolders(LegacyAbilityCategory))
			{
				string nested = Path.Combine(abilityFolder, nestedName);
				if (!Directory.Exists(nested))
				{
					continue;
				}

				foreach (string child in Directory.GetDirectories(nested))
				{
					yield return child;
				}
			}

			foreach ((string _, string itemFolder) in ModAPI.Files.EnumerateCategorySubfolders(flatCategory))
			{
				yield return itemFolder;
			}
		}

		private static void CollectClipNames()
		{
			foreach (string rigPath in EnumerateRigJsonPaths())
			{
				try
				{
					AddClipNamesFromRig(File.ReadAllText(rigPath));
				}
				catch (IOException)
				{
				}
			}
		}

		private static IEnumerable<string> EnumerateRigJsonPaths()
		{
			foreach (string category in new[] { CharacterCategory, LegacyCharacterCategory })
			{
				foreach ((string _, string characterFolder) in ModAPI.Files.EnumerateCategorySubfolders(category))
				{
					string path = Path.Combine(characterFolder, "rig", "rig.json");
					if (File.Exists(path))
					{
						yield return path;
					}
				}
			}
		}

		private static void AddClipNamesFromRig(string json)
		{
			int key = json.IndexOf("\"animations\"", StringComparison.Ordinal);
			if (key < 0)
			{
				return;
			}

			int open = json.IndexOf('[', key);
			if (open < 0)
			{
				return;
			}

			int close = FindMatchingBracket(json, open);
			if (close < 0)
			{
				return;
			}

			foreach (Match match in ClipNamePattern.Matches(json.Substring(open, close - open)))
			{
				string name = match.Groups[1].Value;
				if (!string.IsNullOrEmpty(name))
				{
					clipNames.Add(name);
				}
			}
		}

		private static int FindMatchingBracket(string text, int open)
		{
			int depth = 0;
			for (int i = open; i < text.Length; i++)
			{
				char c = text[i];
				if (c == '[')
				{
					depth++;
				}
				else if (c == ']')
				{
					depth--;
					if (depth == 0)
					{
						return i;
					}
				}
			}

			return -1;
		}

		/// <summary>FXMega attach points are socket names (Chest), not expression tokens (#Chest).</summary>
		internal static string NormalizeAttachPoint(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "Chest";
			}

			string text = value.Trim();
			if (text.Length > 0 && text[0] == '#')
			{
				text = text.Substring(1);
			}

			return string.IsNullOrEmpty(text) ? "Chest" : text;
		}

		private static GameObject NewHiddenPrefab(string name)
		{
			EnsureHideRoot();
			GameObject root = new GameObject(name);
			UnityEngine.Object.DontDestroyOnLoad(root);
			root.hideFlags = HideFlags.HideAndDontSave;
			if (hideRoot != null)
			{
				root.transform.SetParent(hideRoot, false);
			}

			return root;
		}

		private static void EnsureHideRoot()
		{
			if (hideRoot != null)
			{
				return;
			}

			GameObject root = new GameObject("LokrCustomVisuals");
			UnityEngine.Object.DontDestroyOnLoad(root);
			root.hideFlags = HideFlags.HideAndDontSave;
			root.SetActive(false);
			hideRoot = root.transform;
		}

		private static void DestroyBuilt(Dictionary<string, GameObject> built)
		{
			foreach (GameObject prefab in built.Values)
			{
				if (prefab != null)
				{
					UnityEngine.Object.Destroy(prefab);
				}
			}

			built.Clear();
		}

#pragma warning disable CS0649 // JsonUtility assigns these from fx.json / projectile.json
		[Serializable]
		private sealed class SpriteFxSpec
		{
			public string attachPoint = "Chest";
			public string createEvent = "start";
			public string castCreateEvent = "AbilityAction";
			public string removeEvent = "AbilityEnd";
			public string finishEvent = "AbilityEnd";
			public bool detached;
			public bool loops;
			public float duration = 0.6f;
			public string soundId = string.Empty;
			public float pixelsPerUnit = 100f;
		}

		[Serializable]
		private sealed class ProjectileSpec
		{
			public float maxSpeed = 8f;
			public float maxForce = 24f;
			public float slowingDistance = 0.4f;
			public float forceMultiplier = 1f;
			public bool keepTrackingTarget = true;
			public bool ignoresRotation;
			public float pixelsPerUnit = 100f;
		}
#pragma warning restore CS0649
	}
}
