using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Model.Game.Units.Abilities;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Localization;
using LokrCharacterLoader.CustomRigs;

namespace LokrCharacterLoader
{
	/// <summary>Re-reads Lab-authored mod content from disk into the running game's caches.</summary>
	internal static class ContentReloader
	{
		private static readonly MethodInfo UnityDefinitionsLoadData =
			AccessTools.Method(typeof(UnityDefinitionsParser), "LoadData");

		internal static CharacterAPI.ReloadResult Reload(CharacterAPI.ReloadScope scope)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			CharacterAPI.ReloadScope completed = CharacterAPI.ReloadScope.None;
			try
			{
				if ((scope & CharacterAPI.ReloadScope.UnitDefinitions) != 0)
				{
					ReloadUnitDefinitions();
					completed |= CharacterAPI.ReloadScope.UnitDefinitions;
				}
				if ((scope & CharacterAPI.ReloadScope.HeroRoster) != 0)
				{
					ReloadHeroRoster();
					completed |= CharacterAPI.ReloadScope.HeroRoster;
				}
				if ((scope & CharacterAPI.ReloadScope.Localization) != 0)
				{
					ReloadLocalization();
					completed |= CharacterAPI.ReloadScope.Localization;
				}
				if ((scope & CharacterAPI.ReloadScope.Rigs) != 0)
				{
					CustomRigLoader.ClearCaches();
					completed |= CharacterAPI.ReloadScope.Rigs;
				}
				if ((scope & CharacterAPI.ReloadScope.Abilities) != 0)
				{
					AbilitiesDefinitionsPatches.ForceReload(AbilitiesDefinitions.instance);
					completed |= CharacterAPI.ReloadScope.Abilities;
				}
				if ((scope & CharacterAPI.ReloadScope.Visuals) != 0)
				{
					CustomFxLoader.Refresh();
					completed |= CharacterAPI.ReloadScope.Visuals;
				}

				int heroesRefreshed = 0;
				if ((completed & (CharacterAPI.ReloadScope.UnitDefinitions | CharacterAPI.ReloadScope.Rigs)) != 0)
				{
					heroesRefreshed = MetagameHeroReloader.RefreshLoadedHeroes();
				}

				stopwatch.Stop();
				LokrCharacterLoaderPlugin.Log.LogInfo(string.Format(
					"ContentReloader: reloaded {0} in {1:F0} ms ({2} heroes refreshed).",
					completed,
					stopwatch.Elapsed.TotalMilliseconds,
					heroesRefreshed));

				return new CharacterAPI.ReloadResult
				{
					Success = true,
					Completed = completed,
					ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
				};
			}
			catch (Exception ex)
			{
				stopwatch.Stop();
				LokrCharacterLoaderPlugin.Log.LogError("ContentReloader: reload failed — " + ex);
				return new CharacterAPI.ReloadResult
				{
					Success = false,
					Completed = completed,
					ErrorMessage = ex.Message,
					ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
				};
			}
		}

		private static void ReloadUnitDefinitions()
		{
			CharacterAPI.ClearKnownUnitDefinitions();
			UnityDefinitionsLoadData.Invoke(UnityDefinitionsParser.instance, null);
		}

		private static void ReloadHeroRoster()
		{
			if (!MetagameManager.IsInstanceValid)
			{
				LokrCharacterLoaderPlugin.Log.LogWarning(
					"ContentReloader: metagame not initialized — skipped hero roster reload.");
				return;
			}

			HeroRosterManager rosterManager =
				Traverse.Create(MetagameManager.instanceNoLoad).Field<HeroRosterManager>("heroRosterManager").Value;
			rosterManager.Init();
		}

		private static void ReloadLocalization()
		{
			LocalizationManager localization = LocalizationManager.instance;
			localization.Load(localization.currLanguageCode);
		}
	}
}
