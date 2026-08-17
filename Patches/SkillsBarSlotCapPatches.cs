using HarmonyLib;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.View.Game.Units;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Keeps SkillsBar from indexing past its five hex slots in campaign fights as well as Lab.</summary>
	/// <remarks>
	/// Vanilla instantiates exactly five scenario/Skill transforms, then copies every IsInteractive
	/// skill into MatchSkillsBarUnit.skills. A sixth interactive skill throws in SetSelectedUnit,
	/// GetSelectedSkillIcon, SetSelectedSkill, and NotDefaultSkillSelected. Lab already trims when
	/// EmbeddedFightHost.IsActive; this campaign-wide cap has no such gate and does not add hexes.
	/// </remarks>
	internal static class SkillsBarSlotCapPatches
	{
		/// <summary>Drops extra MatchSkillsBarUnit entries that have no hex slot.</summary>
		internal static void Trim(SkillsBar bar, UnitViewComponent unitView, bool logDrop)
		{
			if (bar == null || bar.skillPerUnits == null || unitView == null || bar.skillsList == null)
			{
				return;
			}

			MatchSkillsBarUnit match;
			if (!bar.skillPerUnits.TryGetValue(unitView, out match) || match == null || match.skills == null)
			{
				return;
			}

			int cap = bar.skillsList.Count;
			int before = match.skills.Count;
			ContentRules.TrimListToCap(match.skills, cap);
			if (logDrop && before > match.skills.Count)
			{
				int dropped = before - match.skills.Count;
				Unit unit = unitView.GetUnit();
				string id = "?";
				if (unit != null && unit.unitDefinition != null && !string.IsNullOrEmpty(unit.unitDefinition.uniqueId))
				{
					id = unit.unitDefinition.uniqueId;
				}
				else if (unit != null)
				{
					id = unit.CodeName;
				}
				LokrCharacterLoaderPlugin.Log.LogWarning(string.Format(
					"SkillsBar: unit '{0}' has {1} extra interactive skill(s) beyond {2} hex slots; extras omitted.",
					id, dropped, cap));
			}
		}

		/// <summary>Trims the bar after vanilla copies every interactive skill onto it.</summary>
		[HarmonyPatch(typeof(SkillsBar), nameof(SkillsBar.AddSkillsBar))]
		private static class AddSkillsBar_Patch
		{
			[HarmonyPostfix]
			private static void Postfix(SkillsBar __instance, UnitViewComponent unitView)
			{
				Trim(__instance, unitView, true);
			}
		}

		/// <summary>Trims before vanilla indexes skillsList by skill count.</summary>
		[HarmonyPatch(typeof(SkillsBar), "SetSelectedUnit", new[] { typeof(UnitViewComponent) })]
		private static class SetSelectedUnit_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(SkillsBar __instance, UnitViewComponent unitView)
			{
				Trim(__instance, unitView, false);
				return true;
			}
		}

		/// <summary>Trims before SetSelectedSkill walks match.skills against skillsList.</summary>
		[HarmonyPatch(typeof(SkillsBar), nameof(SkillsBar.SetSelectedSkill))]
		private static class SetSelectedSkill_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(SkillsBar __instance, UnitViewComponent unitView)
			{
				Trim(__instance, unitView, false);
				return true;
			}
		}

		/// <summary>Trims before GetSelectedSkillIcon indexes skillsList by skill count.</summary>
		[HarmonyPatch(typeof(SkillsBar), nameof(SkillsBar.GetSelectedSkillIcon))]
		private static class GetSelectedSkillIcon_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(SkillsBar __instance, UnitViewComponent unitView)
			{
				Trim(__instance, unitView, false);
				return true;
			}
		}

		/// <summary>Trims before NotDefaultSkillSelected indexes skillsList by skill count.</summary>
		[HarmonyPatch(typeof(SkillsBar), nameof(SkillsBar.NotDefaultSkillSelected))]
		private static class NotDefaultSkillSelected_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(SkillsBar __instance, Unit unit)
			{
				if (unit != null)
				{
					Trim(__instance, unit.unitView, false);
				}
				return true;
			}
		}
	}
}
