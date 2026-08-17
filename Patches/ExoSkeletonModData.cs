using ExoSkeleton.Code;
using HarmonyLib;
using Ironhide.ExoSkeleton;
using LokrModAPI.ExtensionData;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Side-table data attached to ExoSkeletonData/ExoSkeletonUIGraphic instances the mod doesn't own.</summary>
	/// <remarks>Uses ModAPI.ExtensionData.AttachedData&lt;,&gt; (docs/modapi-plan.md §4.5) instead of hand-rolled ConditionalWeakTable boilerplate -- the generalized version of the same "add a field to a type I don't own" workaround the original migration built ad hoc.</remarks>
	internal static class ExoSkeletonModData
	{
		private static readonly AttachedData<ExoSkeletonData, string> HeroId = new AttachedData<ExoSkeletonData, string>();

		/// <summary>Gets the mod-assigned hero unique ID attached to an ExoSkeletonData, or null.</summary>
		/// <remarks>Written by PartyTokenComponentPatches, read by ExoSkeletonDataPatches and ExoSkeletonUIGraphicPatches.</remarks>
		internal static string GetHeroId(ExoSkeletonData data)
		{
			return data != null && HeroId.TryGet(data, out string value) ? value : null;
		}

		/// <summary>Attaches a mod-assigned hero unique ID to an ExoSkeletonData.</summary>
		internal static void SetHeroId(ExoSkeletonData data, string heroId)
		{
			HeroId.Set(data, heroId);
		}

		private static readonly AttachedData<ExoSkeletonUIGraphic, bool> TextureLoaded = new AttachedData<ExoSkeletonUIGraphic, bool>();

		/// <summary>Whether a mod texture has already been applied to this ExoSkeletonUIGraphic.</summary>
		internal static bool IsTextureLoaded(ExoSkeletonUIGraphic graphic)
		{
			return TextureLoaded.TryGet(graphic, out bool value) && value;
		}

		/// <summary>Marks that a mod texture has been applied to this ExoSkeletonUIGraphic.</summary>
		internal static void MarkTextureLoaded(ExoSkeletonUIGraphic graphic)
		{
			TextureLoaded.Set(graphic, true);
		}

		/// <summary>Applies a mod-provided texture to an ExoSkeletonRenderer.</summary>
		/// <remarks>Reimplements the same behavior the recompiled mod's (also-new) ExoSkeletonRenderer.LoadTexture(Texture2D) method had, via Traverse instead of adding a real method to ExoSkeletonRenderer.</remarks>
		internal static void ApplyTextureToRenderer(ExoSkeletonRenderer renderer, Texture2D texture)
		{
			if (renderer == null)
			{
				return;
			}
			Traverse traverse = Traverse.Create(renderer);
			MeshRenderer meshRenderer = traverse.Field<MeshRenderer>("meshRenderer").Value;

			MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
			materialPropertyBlock.SetTexture("_MainTex", texture);
			if (renderer.exoSkeletonData.asset.partSprites[0].associatedAlphaSplitTexture != null)
			{
				materialPropertyBlock.SetTexture("_AlphaTex", renderer.exoSkeletonData.asset.partSprites[0].associatedAlphaSplitTexture);
				materialPropertyBlock.SetFloat("_AlphaSplitEnabled", 1f);
			}
			meshRenderer.SetPropertyBlock(materialPropertyBlock);
			traverse.Field<bool>("loadedTexture").Value = true;
		}
	}
}
