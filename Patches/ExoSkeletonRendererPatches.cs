using HarmonyLib;
using Ironhide.ExoSkeleton;
using LokrModAPI;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Applies a mod-provided exoskeleton body-skin texture before ExoSkeletonRenderer.LateUpdate() runs.</summary>
	/// <remarks>
	/// Row 8 in docs/bepinex-migration-plan.md §6 (exoskeleton body skin, world rendering).
	/// Internal plumbing (docs/modapi-plan.md §7) -- keyed by texture name rather than hero+slot,
	/// so it doesn't fit CharacterAPI's portrait resolver chain; uses ModAPI.Files / ModAPI.Assets
	/// directly instead. If a mod texture is found, ExoSkeletonModData.ApplyTextureToRenderer
	/// already sets the renderer's private `loadedTexture` flag, so the original method's own
	/// vanilla texture-loading block naturally no-ops afterward -- no need to skip the original method.
	/// </remarks>
	[HarmonyPatch(typeof(ExoSkeletonRenderer), "LateUpdate")]
	internal static class ExoSkeletonRendererPatches
	{
		/// <summary>Applies a mod-provided texture for this renderer's exoskeleton part, if one exists and none has been applied yet.</summary>
		[HarmonyPrefix]
		private static void Prefix(ExoSkeletonRenderer __instance)
		{
			if (Traverse.Create(__instance).Field<bool>("loadedTexture").Value)
			{
				return;
			}
			if (__instance.exoSkeletonData == null || __instance.exoSkeletonData.parts == null || __instance.exoSkeletonData.parts.Count == 0)
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
				Texture2D texture2D = ModAPI.Assets.LoadTexture(path);
				ExoSkeletonModData.ApplyTextureToRenderer(__instance, texture2D);
			}
		}
	}
}
