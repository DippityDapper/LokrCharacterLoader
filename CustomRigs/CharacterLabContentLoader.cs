using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Ironhide.Localization;
using LokrModAPI;

namespace LokrCharacterLoader.CustomRigs
{
	/// <summary>Feeds LokrCharacterLab's General-workstation-created characters into CharacterAPI's content-building events.</summary>
	/// <remarks>
	/// Reads from CharactersRoot/&lt;Id&gt;/ (see LokrCharacterLab's own CharacterLabPaths.cs)
	/// into CharacterAPI's existing BuildingUnitDefinitions/BuildingHeroRoster/
	/// ContributingLocalization resolver-chain events -- the same proven infrastructure
	/// CustomRigLoader already uses for the rig itself, zero changes needed to CharacterAPI.cs or
	/// the base-game patches. Additive alongside the existing flat-folder default scanning in
	/// UnityDefinitionsParserPatches/HeroRosterManagerPatches/LocalizationManagerPatches (still
	/// needed for other/legacy hand-authored mods) -- this is one more participant in the same
	/// chain, not a replacement of it.
	///
	/// Scans LokrCharacterLab/&lt;Id&gt;/ (and leftover Characters/ folders that still have
	/// character.json or project.json), reading the sibling files
	/// General itself writes (definition/rlheroes.txt, roster.json, localization_en_US.txt,
	/// character.json) -- see LokrCharacterLab's own General/RLHeroesGenerator.cs for exactly what
	/// it writes and why.
	/// </remarks>
	internal static class CharacterLabContentLoader
	{
		private const string Category = "LokrCharacterLab";
		private const string LegacyCategory = "Characters";

		/// <summary>Subscribes to CharacterAPI's content-building events.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.BuildingUnitDefinitions += OnBuildingUnitDefinitions;
			CharacterAPI.BuildingHeroRoster += OnBuildingHeroRoster;
			CharacterAPI.ContributingLocalization += OnContributingLocalization;
		}

		/// <summary>Adds each Lab-authored character's rlheroes.txt as an RLHeroes fragment (heroes and EnemySummon share that format).</summary>
		/// <remarks>
		/// Leftover c-prefixed block keys from 0.12.4 are stripped back to the folder id.
		/// <c>$alias</c> in this folder's aliases.json is expanded first. SpawnUnit #word
		/// literals are rewritten separately. See LabAliases, LabExpressionIds, and
		/// docs/issues/resolved/sandbox-summon-missing-unit-view.md.
		/// </remarks>
		private static void OnBuildingUnitDefinitions(CharacterAPI.UnitDefinitionsBuilder builder)
		{
			foreach (string characterFolder in EnumerateCharacterFolders())
			{
				string path = Path.Combine(characterFolder, "definition", "rlheroes.txt");
				if (!File.Exists(path))
				{
					continue;
				}
				builder.AddHeroDefinition(LabExpressionIds.NormalizeDefinitionKeys(
					LabAliases.ExpandInFolder(characterFolder, File.ReadAllText(path))));
			}
		}

		/// <summary>Adds each Lab-authored character's roster.json as a legend or companion fragment, based on its tier.</summary>
		private static void OnBuildingHeroRoster(CharacterAPI.RosterBuilder builder)
		{
			foreach (string characterFolder in EnumerateCharacterFolders())
			{
				string path = Path.Combine(characterFolder, "roster.json");
				if (!File.Exists(path))
				{
					continue;
				}
				string content = LabAliases.ExpandInFolder(characterFolder, File.ReadAllText(path));
				if (ReadTier(characterFolder) == "Legend")
				{
					builder.AddLegend(content);
				}
				else
				{
					builder.AddCompanion(content);
				}
			}
		}

		/// <summary>Merges each Lab-authored character's localization_&lt;suffix&gt;.txt into the localization strings for the requested language, for every language the base game supports -- not just English.</summary>
		/// <remarks>A character with no Lab-authored translation for a given language just has none yet (RLHeroesGenerator only ever writes English plus whatever locales that character's own CharacterProfile.Localizations tracks), same as any other mod-contributed content.</remarks>
		private static IDictionary<string, string> OnContributingLocalization(LocalizationManager.LanguageCode language)
		{
			if (!LocaleFileSuffixes.Map.TryGetValue(language, out string suffix))
			{
				return null;
			}

			Dictionary<string, string> merged = null;
			Regex linePattern = new Regex("^\\s*\"(.*)\"\\s*=\\s*\"(.*)\"\\s*$");
			foreach (string characterFolder in EnumerateCharacterFolders())
			{
				string path = Path.Combine(characterFolder, "localization_" + suffix + ".txt");
				if (!File.Exists(path))
				{
					continue;
				}
				foreach (string rawLine in File.ReadAllLines(path))
				{
					Match match = linePattern.Match(LabAliases.ExpandInFolder(characterFolder, rawLine));
					if (!match.Success)
					{
						continue;
					}
					merged ??= new Dictionary<string, string>();
					merged[match.Groups[1].Value] = match.Groups[2].Value.Replace("\\\"", "\"");
				}
			}
			return merged;
		}

		/// <summary>Reads character.json's "tier" field via plain regex, defaulting to "Companion" if missing.</summary>
		/// <remarks>Deliberately not a real JSON parse (this project has no existing SimpleJSON usage to build on, and a single string field doesn't need one), same pragmatic choice LokrCharacterLab's own LegacyModImporter makes for the equivalent lookup on the authoring side.</remarks>
		private static string ReadTier(string characterFolder)
		{
			string path = Path.Combine(characterFolder, "character.json");
			if (!File.Exists(path))
			{
				return "Companion";
			}
			Match match = Regex.Match(File.ReadAllText(path), "\"tier\"\\s*:\\s*\"([^\"]*)\"");
			return match.Success ? match.Groups[1].Value : "Companion";
		}

		/// <summary>Reads character.json's "entityType" field via plain regex, defaulting to "Hero" if missing (matches CharacterProfile.EntityType's own default, and every character.json written before this field existed).</summary>
		private static string ReadEntityType(string characterFolder)
		{
			string path = Path.Combine(characterFolder, "character.json");
			if (!File.Exists(path))
			{
				return "Hero";
			}
			Match match = Regex.Match(File.ReadAllText(path), "\"entityType\"\\s*:\\s*\"([^\"]*)\"");
			return match.Success ? match.Groups[1].Value : "Hero";
		}

		/// <summary>Every Lab-authored character folder, across all installed mods.</summary>
		/// <remarks>
		/// Dedupe is by folder name, not character.json / roster id. Two Gerald overrides with
		/// different slug_token folders both contribute; unit-def and roster last-wins at the
		/// parser. Same folder name in LokrCharacterLab/ and leftover Characters/ keeps the first.
		/// </remarks>
		private static IEnumerable<string> EnumerateCharacterFolders()
		{
			HashSet<string> seenIds = new HashSet<string>();
			foreach ((string _, string characterFolder) in ModAPI.Files.EnumerateCategorySubfolders(Category))
			{
				if (TryTakeCharacterFolder(characterFolder, seenIds))
				{
					yield return characterFolder;
				}
			}

			foreach ((string _, string characterFolder) in ModAPI.Files.EnumerateCategorySubfolders(LegacyCategory))
			{
				if (!File.Exists(Path.Combine(characterFolder, "character.json"))
					&& !File.Exists(Path.Combine(characterFolder, "project.json")))
				{
					continue;
				}

				if (TryTakeCharacterFolder(characterFolder, seenIds))
				{
					yield return characterFolder;
				}
			}
		}

		/// <summary>True when this folder name has not already been loaded from another scan root.</summary>
		private static bool TryTakeCharacterFolder(string characterFolder, HashSet<string> seenNames)
		{
			string name = Path.GetFileName(
				characterFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrEmpty(name) || seenNames.Add(name))
			{
				return true;
			}

			LokrCharacterLoaderPlugin.Log.LogWarning(
				"CharacterLabContentLoader: skipping '" + characterFolder
				+ "' because folder name '" + name + "' was already loaded.");
			return false;
		}
	}
}
