using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.Model.Metagame.Heroes;
using UnityEngine;

namespace LokrCharacterLoader
{
	/// <summary>Refreshes live <see cref="Hero"/> instances after unit-definition or rig caches reload.</summary>
	internal static class MetagameHeroReloader
	{
		/// <summary>Re-clones each hero's <see cref="Hero.unitDefinition"/> from the parser and clears cached rig assets.</summary>
		/// <remarks>HeroManager.GetAllHeroes() just returns a bare field with no lazy-init -- legitimately null before any save slot has been loaded (e.g. the lab was opened straight from the main menu), so that case is treated the same as no hero manager at all rather than left to throw.</remarks>
		internal static int RefreshLoadedHeroes()
		{
			if (!MetagameManager.IsInstanceValid)
			{
				return 0;
			}

			HeroManager heroManager = Traverse.Create(MetagameManager.instanceNoLoad).Field<HeroManager>("heroManager").Value;
			if (heroManager == null)
			{
				return 0;
			}

			List<Hero> allHeroes = heroManager.GetAllHeroes();
			if (allHeroes == null)
			{
				return 0;
			}

			int refreshed = 0;
			foreach (Hero hero in new List<Hero>(allHeroes))
			{
				if (TryRefreshHero(hero))
				{
					refreshed++;
				}
			}
			return refreshed;
		}

		private static bool TryRefreshHero(Hero hero)
		{
			if (hero?.heroDefinition == null || string.IsNullOrEmpty(hero.heroDefinition.archetype))
			{
				return false;
			}

			UnitDefinition latest = UnityDefinitionsParser.instance.GetDefinition(hero.heroDefinition.archetype);
			if (latest == null)
			{
				LokrCharacterLoaderPlugin.Log.LogWarning(
					"MetagameHeroReloader: no definition for archetype '" + hero.heroDefinition.archetype + "'.");
				return false;
			}

			UnitDefinition oldDefinition = hero.unitDefinition;
			UnitDefinition cloned = latest.Clone();
			cloned.heroDefinition = hero.heroDefinition;
			hero.unitDefinition = cloned;
			hero.exoSkeletonDataAsset = null;

			if (!string.IsNullOrEmpty(cloned.defaultSkill))
			{
				hero.heroDefinition.defaultAttack = cloned.defaultSkill;
			}

			hero.RefreshStats(oldDefinition);
			// Do not call UpdateSkills here — it re-runs level-progression picks and duplicates skill ids
			// already stored on the save. RegenerateFakeUnit (with HeroSkillSanitizer) rebuilds the dummy unit.
			hero.RegenerateFakeUnit();
			return true;
		}
	}
}
