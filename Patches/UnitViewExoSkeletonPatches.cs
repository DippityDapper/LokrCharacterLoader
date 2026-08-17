using HarmonyLib;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.View.Game.Units;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Lets a modded hero/unit use a custom-built ExoSkeleton rig in actual combat, not just the map/roster/portrait contexts HeroExoSkeletonPatches already covers.</summary>
	/// <remarks>
	/// Combat spawns a unit's view from a real, baked vanilla prefab
	/// (UnitViewManager.FindPrefab(unit.kind), resolved from AssetBundleManager against the game's
	/// own "units" bundle) -- there's no equivalent of CharacterAPI.RegisterExoSkeletonResolver at
	/// that resolution point, and can't be: a made-up "kind" has no prefab to find. So the vanilla
	/// prefab (whatever CharacterProfile.Model names, defaulting to "HumanArcher") is instantiated
	/// as-is, then this postfix swaps the ExoSkeletonAnimator's data asset to the same custom rig
	/// HeroExoSkeletonPatches already resolves for the map -- ExoSkeletonData.UpdateAsset is a real,
	/// purpose-built base-game method for exactly this swap, not a workaround. Every
	/// ExoSkeletonUnitAnimationController on the view caches its own animationId (and angled-variant
	/// ids) against whichever asset was active when it last ran PreloadAnimationIds, so each one
	/// needs re-preloading after the swap or it keeps pointing at indices into the old (template)
	/// asset.
	/// After the swap, attach points and frame events come from the custom asset's current pose
	/// (AttachPointContainerExoSkeleton reads exoSkeletonData.attachPoints; AbilityMeleeActivity
	/// waits on exo-skeleton events AbilityAction/AbilityEnd). The GameObject name stays the
	/// Model prefab's (e.g. UNIT-Nightshade-ObeliskLvl4) even when the mesh is the custom rig.
	/// </remarks>
	[HarmonyPatch(typeof(UnitViewManager), nameof(UnitViewManager.InstantiateUnitView))]
	internal static class UnitViewExoSkeletonPatches
	{
		/// <summary>Swaps in the unit's own custom rig (if any) after its view is instantiated from the vanilla template prefab.</summary>
		[HarmonyPostfix]
		private static void Postfix(Unit unit, GameObject __result)
		{
			if (unit == null || unit.unitDefinition == null || __result == null)
			{
				return;
			}

			ExoSkeletonDataAsset customAsset = CharacterAPI.ResolveExoSkeleton(unit.unitDefinition.metaExo);
			if (customAsset == null)
			{
				return;
			}

			ExoSkeletonAnimator animator = __result.GetComponentInChildren<ExoSkeletonAnimator>(true);
			if (animator == null || animator.data == null)
			{
				return;
			}

			animator.data.UpdateAsset(customAsset);
			foreach (ExoSkeletonUnitAnimationController controller in __result.GetComponentsInChildren<ExoSkeletonUnitAnimationController>(true))
			{
				controller.PreloadAnimationIds();
			}
		}
	}
}
