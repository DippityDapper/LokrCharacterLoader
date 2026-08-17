using HarmonyLib;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Metagame.Heroes;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Lets a modded hero use a custom-built ExoSkeleton rig in real gameplay, not just LokrCharacterLab's test scene.</summary>
	/// <remarks>
	/// Seeds Hero.exoSkeletonDataAsset's private backing field (via CharacterAPI's
	/// RegisterExoSkeletonResolver chain -- e.g. LokrCharacterLoader.CustomRigs.CustomRigLoader)
	/// the first time it's read.
	///
	/// Hero.exoSkeletonDataAsset's getter already lazily caches into a private
	/// _exoSkeletonDataAsset field (`if (this._exoSkeletonDataAsset == null) { ... }`). Rather than
	/// skip the original method (which would re-run CharacterAPI.ResolveExoSkeleton -- rebuilding
	/// the atlas texture -- on every single access), this prefix seeds that same field via
	/// Harmony's ____fieldName injection when a custom rig matches. The original getter then sees
	/// a non-null field and just returns it, exactly as if it had loaded a vanilla asset -- same
	/// caching, same codepath, no duplicate patching of anything downstream that reads this property.
	/// </remarks>
	[HarmonyPatch(typeof(Hero), nameof(Hero.exoSkeletonDataAsset), MethodType.Getter)]
	internal static class HeroExoSkeletonPatches
	{
		/// <summary>Seeds the backing field with a custom rig via CharacterAPI.ResolveExoSkeleton if it hasn't been loaded yet.</summary>
		/// <remarks>Vanilla's getter reads unitDefinition.metaExo with no null check. Skip the original only when unitDefinition is missing; a present definition with a null metaExo still falls through to AssetBundleManager.LoadAsset.</remarks>
		[HarmonyPrefix]
		private static bool Prefix(Hero __instance, ref ExoSkeletonDataAsset ____exoSkeletonDataAsset, ref ExoSkeletonDataAsset __result)
		{
			if (ContentRules.ShouldSkipExoResolveForNullDefinition(__instance.unitDefinition == null))
			{
				__result = null;
				return false;
			}

			if (__instance.unitDefinition.metaExo == null)
			{
				return true;
			}

			if (____exoSkeletonDataAsset == null)
			{
				____exoSkeletonDataAsset = CharacterAPI.ResolveExoSkeleton(__instance.unitDefinition.metaExo);
			}

			return true;
		}
	}
}
