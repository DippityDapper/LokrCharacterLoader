using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.View.Metagame.Screens.Logic;
using LokrModAPI;
using UnityEngine;
using File = System.IO.File;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Full-method-replacement patch that fires CharacterAPI.BuildingHeroRoster while loading the hero roster.</summary>
	/// <remarks>Row 2 in docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.2. Replaces HeroRosterManager.Init() entirely -- fires CharacterAPI.BuildingHeroRoster to gather legend/companion JSON contributions instead of scanning Mods/ itself.</remarks>
	[HarmonyPatch(typeof(HeroRosterManager), "Init")]
	internal static class HeroRosterManagerPatches
	{
		/// <summary>Replaces HeroRosterManager.Init(), applying roster contributions before parsing the roster config.</summary>
		[HarmonyPrefix]
		private static bool Prefix(HeroRosterManager __instance)
		{
			TextAsset textAsset = ResourcesWrapper.Load<TextAsset>("Balance/HeroRoster/HeroRoster");
			textAsset = ApplyRosterContributions(textAsset);

			Traverse.Create(__instance).Field<HeroRosterConfig>("heroRosterConfig").Value = HeroRosterConfig.Parse(textAsset.text);

			List<HeroRosterManager.HeroRosterXPBracket> list = new List<HeroRosterManager.HeroRosterXPBracket>();
			int num = 0;
			for (int i = 0; i < HeroRosterManager.XP_PER_LEVEL.Length; i++)
			{
				int num2 = HeroRosterManager.XP_PER_LEVEL[i];
				HeroRosterManager.HeroRosterXPBracket heroRosterXPBracket = new HeroRosterManager.HeroRosterXPBracket();
				heroRosterXPBracket.index = i;
				heroRosterXPBracket.min = num;
				num += num2;
				heroRosterXPBracket.max = num - 1;
				heroRosterXPBracket.width = num2;
				list.Add(heroRosterXPBracket);
			}
			list.Add(new HeroRosterManager.HeroRosterXPBracket
			{
				index = list.Count,
				min = num,
				max = num + 1,
				width = 1
			});
			HeroRosterManager.XP_BRACKETS = list;

			return false;
		}

		/// <summary>Splices any registered legend/companion JSON fragments into the roster TextAsset's "legends"/"companions" arrays.</summary>
		/// <remarks>
		/// Same id last-wins (Lab/mod fragment replaces the vanilla row). New ids append.
		/// <see cref="HeroRosterConfig.Parse"/> still keys by hero id; replace-in-place avoids
		/// a second Gerald object that would throw and leave MetagameManager instantiating.
		/// </remarks>
		private static TextAsset ApplyRosterContributions(TextAsset asset)
		{
			CharacterAPI.RosterBuilder builder = CharacterAPI.RaiseBuildingHeroRoster();
			if (builder.LegendFragments.Count == 0 && builder.CompanionFragments.Count == 0)
			{
				return asset;
			}

			string text = asset.text;
			string legendsMarker = "\"legends\" : [";
			string companionsMarker = "\"companions\" : [";
			int legendsOpen = text.IndexOf(legendsMarker) + legendsMarker.Length - 1;
			int legendsClose = text.IndexOf("]", legendsOpen);
			int companionsOpen = text.IndexOf(companionsMarker) + companionsMarker.Length - 1;
			int companionsClose = text.IndexOf("]", companionsOpen);
			string legendsArray = ContentRules.MergeRosterArray(
				text.Substring(legendsOpen, legendsClose - legendsOpen + 1),
				builder.LegendFragments);
			string companionsArray = ContentRules.MergeRosterArray(
				text.Substring(companionsOpen, companionsClose - companionsOpen + 1),
				builder.CompanionFragments);

			text = string.Concat(
				text.Substring(0, legendsOpen),
				legendsArray,
				text.Substring(legendsClose + 1, companionsOpen - legendsClose - 1),
				companionsArray,
				text.Substring(companionsClose + 1));
			return new TextAsset(text);
		}

		/// <summary>Registers the Mods/*/HeroRoster file-convention scan as a BuildingHeroRoster contributor.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.BuildingHeroRoster += builder =>
			{
				foreach ((string modFolder, string filePath) in ModAPI.Files.EnumerateCategoryFiles("HeroRoster"))
				{
					string content = File.ReadAllText(filePath);
					if (filePath.Contains("legend_"))
					{
						builder.AddLegend(content);
					}
					if (filePath.Contains("companion_"))
					{
						builder.AddCompanion(content);
					}
				}
			};
		}
	}
}
