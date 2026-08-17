using HarmonyLib;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Game.Units;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Fires CharacterAPI's generic state-visual-effect hook when a unit's INVISIBLE state changes, and registers the Assassin's own color effect as an ordinary subscriber.</summary>
	/// <remarks>
	/// Rows 17, 18 in docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.6. Rebuilt on
	/// CharacterAPI.RegisterStateVisualEffect instead of hardcoding the Assassin unique-ID check
	/// directly in the Unit patches -- the patches here just fire a generic "state X changed on
	/// this unit" event; the Assassin-specific color effect is registered as an ordinary (only,
	/// for now) subscriber via RegisterDefaults(), same "dogfooding" pattern as everywhere else in
	/// this file set. Any other plugin can register its own state-tied visual effect (a different
	/// state name, or a different hero) without touching Unit.AddModifier/TurnEnded itself.
	/// Enter and exit both use a Prefix-captured __state so a second stealth modifier does not
	/// re-raise, and TurnEnded does not fire exit for every non-invisible unit every turn.
	/// Vanilla TurnEnded does not clear INVISIBLE itself; OnEvent("OnTurnFinished") can expire
	/// the modifier before the postfix, which is why the previous value must be captured first.
	/// </remarks>
	internal static class InvisibilityPatches
	{
		/// <summary>Fires the INVISIBLE state-entered event only on a false-to-true edge.</summary>
		[HarmonyPatch(typeof(Unit), "AddModifier")]
		private static class Unit_AddModifier_Patch
		{
			[HarmonyPrefix]
			private static void Prefix(Unit __instance, out bool __state)
			{
				__state = __instance.states != null && __instance.states.IsOn("INVISIBLE");
			}

			[HarmonyPostfix]
			private static void Postfix(Unit __instance, bool __state)
			{
				if (ContentRules.ShouldRaiseInvisibilityEnter(__state, __instance.states != null && __instance.states.IsOn("INVISIBLE")))
				{
					CharacterAPI.RaiseStateVisualEffect("INVISIBLE", __instance, true);
				}
			}
		}

		/// <summary>Fires the INVISIBLE state-exited event only on a true-to-false edge at turn end.</summary>
		[HarmonyPatch(typeof(Unit), "TurnEnded")]
		private static class Unit_TurnEnded_Patch
		{
			[HarmonyPrefix]
			private static void Prefix(Unit __instance, out bool __state)
			{
				__state = __instance.states != null && __instance.states.IsOn("INVISIBLE");
			}

			[HarmonyPostfix]
			private static void Postfix(Unit __instance, bool __state)
			{
				if (__instance.states == null)
				{
					return;
				}

				if (ContentRules.ShouldRaiseInvisibilityExit(__state, __instance.states.IsOn("INVISIBLE")))
				{
					CharacterAPI.RaiseStateVisualEffect("INVISIBLE", __instance, false);
				}
			}
		}

		/// <summary>Registers the Assassin's translucency color effect as an INVISIBLE state-visual-effect subscriber.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.RegisterStateVisualEffect("INVISIBLE", (unit, isEntering) =>
			{
				if (!unit.isHero || unit.unitDefinition.uniqueId != "Assassin")
				{
					return;
				}
				if (unit.unitView == null)
				{
					return;
				}
				ExoSkeletonRenderer[] array = unit.unitView.GetComponentsInChildren<ExoSkeletonRenderer>(true);
				for (int i = 0; i < array.Length; i++)
				{
					if (array[i].name.Equals("Graphic"))
					{
						array[i].color = new Color(1f, 1f, 1f, isEntering ? 0.5f : 1f);
					}
				}
			});
		}
	}
}
