using HarmonyLib;
using Ironhide.Legends.Model.Game.Units.Projectiles;
using Ironhide.Legends.View.Game.FX;
using LokrCharacterLoader.CustomRigs;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Injects CharacterAPI custom FXMega / projectile prefabs into the vanilla load paths.</summary>
	/// <remarks>
	/// FXManager.Preload fills fxMegaPrefabs once from the scenario bundle, then no-ops. A postfix
	/// adds runtime-built sprite FX. LoadFXMega is private and throws on a miss — a prefix returns
	/// a resolver hit so names added after Preload still work. Projectiles use DataHelper.LoadProjectile
	/// (scenario bundle, not fxMegaPrefabs).
	/// </remarks>
	internal static class FxPatches
	{
		[HarmonyPatch(typeof(FXManager), "Preload")]
		private static class FXManager_Preload_Patch
		{
			[HarmonyPostfix]
			private static void Postfix()
			{
				CustomFxLoader.Refresh();
			}
		}

		[HarmonyPatch(typeof(FXManager), "LoadFXMega")]
		private static class FXManager_LoadFXMega_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(string key, ref GameObject __result)
			{
				GameObject custom = CharacterAPI.ResolveFxMega(key);
				if (custom == null)
				{
					return true;
				}

				__result = custom;
				return false;
			}
		}

		[HarmonyPatch(typeof(DataHelper), "LoadProjectile")]
		private static class DataHelper_LoadProjectile_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(string prefabName, ref GameObject __result)
			{
				GameObject custom = CharacterAPI.ResolveProjectile(prefabName);
				if (custom == null)
				{
					return true;
				}

				__result = custom;
				return false;
			}
		}

		/// <summary>A custom projectile missing view wiring NREs every frame in Projectile.Update and freezes combat. Finish the shot instead.</summary>
		[HarmonyPatch(typeof(Projectile), "Update")]
		private static class Projectile_Update_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(Projectile __instance)
			{
				if (__instance.view != null && __instance.view.projectileTransform != null)
				{
					return true;
				}

				try
				{
					__instance.DestinationReached();
				}
				catch (System.Exception ex)
				{
					LokrCharacterLoaderPlugin.Log.LogError("Custom projectile view was incomplete; combat would have frozen. " + ex.Message);
				}

				return false;
			}
		}
	}
}
