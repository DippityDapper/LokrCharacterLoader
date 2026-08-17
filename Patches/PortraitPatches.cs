using System.Collections.Generic;
using System.IO;
using ExoSkeleton.Code;
using HarmonyLib;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.Model.Metagame.Heroes;
using Ironhide.Legends.View.Map.Screens.Rewards;
using Ironhide.Legends.View.Map.Screens.Store;
using LokrModAPI;
using UnityEngine;
using UnityEngine.UI;
using File = System.IO.File;
using Path = System.IO.Path;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Unifies six base-game portrait call sites onto CharacterAPI.RegisterPortraitResolver.</summary>
	/// <remarks>
	/// Rows 5, 5b, 6, 10 (RewardViewComponent + UIBuffStoreItem halves) in
	/// docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.3. Replaces the old
	/// DataHelperPatches + MapHeroBarPortraitComponentPatches + RewardViewComponentPatches +
	/// UIBuffStoreItemPatches, all unified onto CharacterAPI.RegisterPortraitResolver /
	/// RegisterAbilityIconResolver. (DialogViewManagerMap's two CHALLENGE call sites also use the
	/// same resolver chain but stay in their own file -- see DialogViewManagerMapPatches.cs --
	/// since that method's full-body replacement is large enough on its own to keep separate for
	/// readability; functionally it's the same unification.)
	/// </remarks>
	internal static class PortraitPatches
	{
		/// <summary>Resolves the MINI portrait via CharacterAPI, falling back to the vanilla result.</summary>
		[HarmonyPatch(typeof(DataHelper), "LoadMiniPortrait")]
		private static class LoadMiniPortrait_Patch
		{
			[HarmonyPostfix]
			private static void Postfix(string heroId, ref Sprite __result) => __result = ResolveOrFallback(heroId, "MINI", __result);
		}

		/// <summary>Resolves the BIG portrait via CharacterAPI, falling back to the vanilla result.</summary>
		[HarmonyPatch(typeof(DataHelper), "LoadBigPortrait")]
		private static class LoadBigPortrait_Patch
		{
			[HarmonyPostfix]
			private static void Postfix(string heroId, ref Sprite __result) => __result = ResolveOrFallback(heroId, "BIG", __result);
		}

		/// <summary>Resolves the BANNER portrait via CharacterAPI, falling back to the vanilla result.</summary>
		[HarmonyPatch(typeof(DataHelper), "LoadHeroBanner")]
		private static class LoadHeroBanner_Patch
		{
			[HarmonyPostfix]
			private static void Postfix(string heroId, ref Sprite __result) => __result = ResolveOrFallback(heroId, "BANNER", __result);
		}

		/// <summary>Resolves an ability icon via CharacterAPI, falling back to a bundled default icon if vanilla also came back null.</summary>
		[HarmonyPatch(typeof(DataHelper), "LoadSkillIcon")]
		private static class LoadSkillIcon_Patch
		{
			[HarmonyPostfix]
			private static void Postfix(string icon, ref Sprite __result)
			{
				Sprite resolved = CharacterAPI.ResolveAbilityIcon(icon);
				if (resolved != null)
				{
					__result = resolved;
					return;
				}
				if (__result == null && TryLoadFallback("DEFAULTICON.png", out Sprite fallback))
				{
					__result = fallback;
				}
			}
		}

		/// <summary>Resolves a portrait via the CharacterAPI chain, falling back to a bundled DEFAULT_&lt;slot&gt;.png only if vanilla's own result was also null.</summary>
		/// <remarks>Called as a Postfix, so the vanilla asset-bundle lookup has already run.</remarks>
		private static Sprite ResolveOrFallback(string heroId, string slot, Sprite vanillaResult)
		{
			if (ContentRules.ShouldSkipPortraitResolve(heroId))
			{
				if (TryLoadFallback("DEFAULT_" + slot + ".png", out Sprite emptySlot))
				{
					return emptySlot;
				}

				return vanillaResult;
			}

			Sprite resolved = CharacterAPI.ResolvePortrait(heroId, slot);
			if (resolved != null)
			{
				return resolved;
			}
			if (vanillaResult == null && TryLoadFallback("DEFAULT_" + slot + ".png", out Sprite fallback))
			{
				return fallback;
			}
			return vanillaResult;
		}

		/// <summary>Loads a bundled default sprite from Mods/Resources/&lt;fileName&gt; if it exists.</summary>
		private static bool TryLoadFallback(string fileName, out Sprite sprite)
		{
			string path;
			if (ModAPI.Files.TryFindFile("Resources", fileName, out path)
				|| File.Exists(path = Path.Combine(Application.dataPath, "Mods", "Resources", fileName)))
			{
				sprite = ModAPI.Assets.LoadSprite(path);
				return sprite != null;
			}

			sprite = null;
			return false;
		}

		/// <summary>Resolves the MAP-slot portrait via CharacterAPI, replacing the exoskeleton renderer with a flat image if one is found.</summary>
		[HarmonyPatch(typeof(MapHeroBarPortraitComponent), "SetHero")]
		private static class MapHeroBarPortraitComponent_SetHero_Patch
		{
			[HarmonyPostfix]
			private static void Postfix(MapHeroBarPortraitComponent __instance, Hero hero)
			{
				if (hero == null)
				{
					return;
				}
				Sprite sprite = CharacterAPI.ResolvePortrait(hero.unitDefinition.uniqueId, "MAP");
				if (sprite == null)
				{
					return;
				}
				if (__instance.portraitData == null)
				{
					LokrCharacterLoaderPlugin.Log.LogWarning("MapHeroBarPortraitComponent.SetHero: portraitData is null — skip MAP flat image.");
					return;
				}
				ReplaceWithFlatImage(__instance.portraitData.gameObject, sprite, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
			}
		}

		/// <summary>Replaces RewardViewComponent.SetTargetPortrait, resolving the CHALLENGE-slot portrait via CharacterAPI.</summary>
		[HarmonyPatch(typeof(RewardViewComponent), "SetTargetPortrait", typeof(Hero))]
		private static class RewardViewComponent_SetTargetPortrait_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(RewardViewComponent __instance, Hero hero)
			{
				Traverse traverse = Traverse.Create(__instance);
				ExoSkeletonData targetPortraitExo = traverse.Field<ExoSkeletonData>("targetPortraitExo").Value;
				Image targetPortraitParty = traverse.Field<Image>("targetPortraitParty").Value;
				RectTransform targetPortraitRect = traverse.Field<RectTransform>("targetPortraitRect").Value;

				targetPortraitExo.UpdateAsset(hero.exoSkeletonDataAsset);
				targetPortraitExo.SetAnimFrame("Portrait", "StandStatic", 0);

				Sprite sprite = CharacterAPI.ResolvePortrait(hero.unitDefinition.uniqueId, "CHALLENGE");
				if (sprite != null)
				{
					ReplaceWithFlatImage(targetPortraitExo.gameObject, sprite, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
				}

				targetPortraitParty.gameObject.SetActive(false);
				targetPortraitRect.gameObject.SetActive(true);

				return false;
			}
		}

		/// <summary>Resolves the CHALLENGE-slot portrait via CharacterAPI for the buff store's own hero display.</summary>
		[HarmonyPatch(typeof(UIBuffStoreItem), "SetItem")]
		private static class UIBuffStoreItem_SetItem_Patch
		{
			[HarmonyPostfix]
			private static void Postfix(UIBuffStoreItem __instance)
			{
				BuffStoreModelAdapter.BuffStoreItemBehavior itemBehavior =
					Traverse.Create(__instance).Field<BuffStoreModelAdapter.BuffStoreItemBehavior>("itemBehavior").Value;

				if (MetagameManager.instance == null || MetagameManager.instance.HeroManager == null)
				{
					return;
				}

				List<Hero> heroes = MetagameManager.instance.HeroManager.GetAllHeroes();
				if (heroes == null || !ContentRules.IsHeroPositionInRange(itemBehavior.heroPosition, heroes.Count))
				{
					int count = heroes == null ? -1 : heroes.Count;
					LokrCharacterLoaderPlugin.Log.LogWarning(string.Format(
						"UIBuffStoreItem.SetItem: heroPosition {0} out of range (heroes {1}) — skip CHALLENGE portrait.",
						itemBehavior.heroPosition, count));
					return;
				}

				Hero hero = heroes[itemBehavior.heroPosition];
				if (hero == null || hero.unitDefinition == null)
				{
					return;
				}
				Sprite sprite = CharacterAPI.ResolvePortrait(hero.unitDefinition.uniqueId, "CHALLENGE");
				if (sprite != null)
				{
					ReplaceWithFlatImage(__instance.portraitData.gameObject, sprite, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
				}
			}
		}

		/// <summary>Tears down the rigged-sprite (ExoSkeleton) renderer on gameObject and replaces it with a plain Image showing sprite.</summary>
		/// <remarks>Shared teardown mechanics used by every portrait slot here. A resolver just returns a Sprite; it doesn't need to know how that sprite ends up on screen.</remarks>
		internal static void ReplaceWithFlatImage(GameObject gameObject, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax)
		{
			Object.DestroyImmediate(gameObject.GetComponent<ExoSkeletonUIGraphic>());
			Object.DestroyImmediate(gameObject.GetComponent<ExoSkeletonData>());
			RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
			rectTransform.anchorMin = anchorMin;
			rectTransform.anchorMax = anchorMax;
			rectTransform.pivot = new Vector2(0.5f, 0.5f);
			Image image = gameObject.AddComponent<Image>();
			image.sprite = sprite;
		}

		/// <summary>Registers the Mods/*/Characters/&lt;heroId&gt;/portraits, Mods/*/Portraits, Mods/*/Abilities/&lt;id&gt;/icons, and Mods/*/AbilityIcons file-convention resolvers.</summary>
		/// <remarks>One portrait resolver covers all six slots -- &lt;heroId&gt;_&lt;slot&gt;.png is the same convention for MINI/BIG/BANNER/MAP/MAPMINI/CHALLENGE alike. Checks a Lab character's own nested Characters/&lt;heroId&gt;/portraits/ folder first, falling back to the flat mod-wide Portraits/&lt;heroId&gt;/ convention unchanged -- that flat convention is what hand-authored, non-Lab mods (e.g. the Official Pack) already rely on, so it must keep working exactly as before; the nested lookup is purely additive. Ability icons check each Abilities/&lt;id&gt;/icons/&lt;iconName&gt;.png folder first (Ability Lab's per-ability layout), then the flat Mods/*/AbilityIcons/ convention for hand-authored mods. The resolver only receives the Icon field string, not an ability id, so the nested scan is by filename across every ability folder.</remarks>
		internal static void RegisterDefaults()
		{
			CharacterAPI.RegisterPortraitResolver((heroId, slot) =>
			{
				if (ContentRules.ShouldSkipPortraitResolve(heroId))
				{
					return null;
				}

				string fileName = heroId + "_" + slot + ".png";
				if (!ModAPI.Files.TryFindFile(Path.Combine("LokrCharacterLab", heroId, "portraits"), fileName, out string path)
					&& !ModAPI.Files.TryFindFile(Path.Combine("Characters", heroId, "portraits"), fileName, out path))
				{
					ModAPI.Files.TryFindFile("Portraits", heroId + "/" + fileName, out path);
				}
				if (path != null)
				{
					TextureFormat format = slot == "MAP" ? TextureFormat.RGBA64 : TextureFormat.ARGB32;
					return ModAPI.Assets.LoadSprite(path, format);
				}
				return null;
			});

			CharacterAPI.RegisterAbilityIconResolver(iconName =>
			{
				string fileName = iconName + ".png";
				foreach ((string _, string libraryFolder) in ModAPI.Files.EnumerateCategorySubfolders("LokrAbilityLab"))
				{
					if (!Directory.Exists(libraryFolder))
					{
						continue;
					}

					foreach (string abilityFolder in Directory.GetDirectories(libraryFolder))
					{
						string nested = Path.Combine(abilityFolder, "icons", fileName);
						if (File.Exists(nested))
						{
							return ModAPI.Assets.LoadSprite(nested);
						}
					}
				}

				foreach ((string _, string abilityFolder) in ModAPI.Files.EnumerateCategorySubfolders("Abilities"))
				{
					string nested = Path.Combine(abilityFolder, "icons", fileName);
					if (File.Exists(nested))
					{
						return ModAPI.Assets.LoadSprite(nested);
					}
				}
				if (ModAPI.Files.TryFindFile("AbilityIcons", fileName, out string path))
				{
					return ModAPI.Assets.LoadSprite(path);
				}
				return null;
			});
		}
	}
}
