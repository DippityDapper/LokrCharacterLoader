using HarmonyLib;
using Ironhide.ExoSkeleton;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Replaces ExoSkeletonData.ReplacePart to resolve a party-token's map mini portrait via CharacterAPI.</summary>
	/// <remarks>
	/// Row 7 in docs/bepinex-migration-plan.md §6 (map party-token mini portrait, `_MAPMINI.png`).
	/// Resolved through CharacterAPI.ResolvePortrait(uniqueId, "MAPMINI") -- the same resolver
	/// chain PortraitPatches registers its file-convention default into -- instead of an inline
	/// Mods/ scan. ExoSkeletonModData.HeroId (§4.5 AttachedData) still carries the hero identity
	/// from PartyTokenComponentPatches into this Prefix, since ReplacePart's original 2-arg
	/// signature has no room for it.
	/// </remarks>
	[HarmonyPatch(typeof(ExoSkeletonData), "ReplacePart", typeof(string), typeof(string))]
	internal static class ExoSkeletonDataPatches
	{
		/// <summary>Resolves the party-token's map mini portrait via CharacterAPI and swaps in the flat-quad part/shadow, or falls through to vanilla if none is found.</summary>
		[HarmonyPrefix]
		private static bool Prefix(ExoSkeletonData __instance, string newPart, string oldPart)
		{
			string uniqueId = ExoSkeletonModData.GetHeroId(__instance);
			if (uniqueId == null)
			{
				return true;
			}

			Sprite sprite = CharacterAPI.ResolvePortrait(uniqueId, "MAPMINI");
			if (sprite == null)
			{
				return true;
			}

			ExoSkeletonModData.ApplyTextureToRenderer(__instance.GetComponent<ExoSkeletonRenderer>(), sprite.texture);

			int index = __instance.FindPartIndex(oldPart);
			if (!ContentRules.ShouldWritePartAtIndex(index))
			{
				LokrCharacterLoaderPlugin.Log.LogWarning(string.Format(
					"ExoSkeletonData.ReplacePart: part '{0}' not found on hero '{1}' — skip.",
					oldPart, uniqueId));
			}
			else
			{
				__instance.parts[index] = new Part
				{
					color = new Color32(255, 255, 255, 255),
					triangles = new int[] { 0, 1, 2, 2, 1, 3 },
					vertices = new Vector2[]
					{
						new Vector2(-0.35f, 0.7f),
						new Vector2(0.35f, 0.7f),
						new Vector2(-0.35f, 0f),
						new Vector2(0.35f, 0f)
					},
					uvs = new Vector2[]
					{
						new Vector2(0f, 1f),
						new Vector2(1f, 1f),
						new Vector2(0f, 0.2f),
						new Vector2(1f, 0.2f)
					},
					name = "Asst_Party_" + uniqueId
				};
			}

			int index2 = __instance.FindPartIndex("Asst_Shadow");
			if (!ContentRules.ShouldWritePartAtIndex(index2))
			{
				LokrCharacterLoaderPlugin.Log.LogWarning(string.Format(
					"ExoSkeletonData.ReplacePart: part 'Asst_Shadow' not found on hero '{0}' — skip.",
					uniqueId));
			}
			else
			{
				__instance.parts[index2] = new Part
				{
					color = new Color32(255, 255, 255, 255),
					triangles = new int[] { 0, 1, 2, 2, 1, 3 },
					vertices = new Vector2[]
					{
						new Vector2(-0.15f, 0.075f),
						new Vector2(0.15f, 0.075f),
						new Vector2(-0.15f, -0.075f),
						new Vector2(0.15f, -0.075f)
					},
					uvs = new Vector2[]
					{
						new Vector2(0.25f, 0.2f),
						new Vector2(0.75f, 0.2f),
						new Vector2(0.25f, 0f),
						new Vector2(0.75f, 0f)
					},
					name = "Asst_Shadow"
				};
			}

			__instance.partsVersion++;
			__instance.renderVersion++;
			return false;
		}
	}
}
