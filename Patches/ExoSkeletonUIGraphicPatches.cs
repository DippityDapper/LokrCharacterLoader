using ExoSkeleton.Code;
using HarmonyLib;
using LokrModAPI;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Applies a mod-provided exoskeleton body-skin texture before ExoSkeletonUIGraphic.OnPopulateMesh() runs.</summary>
	/// <remarks>
	/// Row 9 in docs/bepinex-migration-plan.md §6 (exoskeleton body skin, UI rendering). Internal
	/// plumbing, same reasoning as ExoSkeletonRendererPatches (texture-name-keyed, not
	/// hero+slot-keyed, so it stays on ModAPI.Files/Assets directly rather than CharacterAPI).
	/// Mutates exoSkeletonData.asset.partSprites[0] from a Prefix so the *next* read of the
	/// `mainTexture` property (including the one at the top of the original OnPopulateMesh, which
	/// still runs afterward) picks up the mod texture immediately -- no one-frame lag.
	/// </remarks>
	[HarmonyPatch(typeof(ExoSkeletonUIGraphic), "OnPopulateMesh")]
	internal static class ExoSkeletonUIGraphicPatches
	{
		/// <summary>Applies a mod-provided texture for this hero's exoskeleton part sprite, if one exists and none has been applied yet.</summary>
		[HarmonyPrefix]
		private static void Prefix(ExoSkeletonUIGraphic __instance)
		{
			if (ExoSkeletonModData.IsTextureLoaded(__instance))
			{
				return;
			}
			string heroId = ExoSkeletonModData.GetHeroId(__instance.exoSkeletonData);
			if (heroId == null)
			{
				return;
			}
			if (__instance.exoSkeletonData.asset == null || __instance.exoSkeletonData.asset.partSprites == null || __instance.exoSkeletonData.asset.partSprites.Count == 0)
			{
				return;
			}

			string textureName = __instance.exoSkeletonData.asset.partSprites[0].texture.name;
			if (ModAPI.Files.TryFindFile("Exoskeletons", textureName + ".png", out string path))
			{
				__instance.exoSkeletonData.asset.partSprites[0] = ModAPI.Assets.LoadSprite(path);
				ExoSkeletonModData.MarkTextureLoaded(__instance);
			}
		}
	}
}
