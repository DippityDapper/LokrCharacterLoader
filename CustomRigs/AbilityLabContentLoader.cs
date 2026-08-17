using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Ironhide.Localization;
using LokrModAPI;

namespace LokrCharacterLoader.CustomRigs
{
	/// <summary>Feeds LokrAbilityLab's per-ability folders into CharacterAPI's ability-building and localization events.</summary>
	/// <remarks>
	/// Moved here from LokrAbilityLab (2026-08-12) so ability content authored with LokrAbilityLab
	/// loads for a player who only has LokrCharacterLoader installed -- the same reasoning that
	/// already put CharacterLabContentLoader here for Lab-authored characters, just applied to
	/// abilities for the first time. Before this move, this registration lived inside
	/// LokrAbilityLab itself, so a shared per-ability folder (definition, icons, localization) was
	/// silently ignored by anyone who hadn't installed that plugin, even though the equivalent
	/// character folder already worked without LokrCharacterLab installed. See
	/// docs/roadmaps/started/editor-redesign.md §2.7 for the full reasoning.
	///
	/// Same shape as CharacterLabContentLoader right above it and this assembly's own
	/// AbilitiesDefinitionsPatches.RegisterDefaults() (which scans the flat, legacy
	/// "NewAbilities" category) -- CharacterAPI.BuildingAbilities is a public multi-subscriber
	/// event and ModAPI.Files.EnumerateCategorySubfolders scans across every installed mod folder
	/// already, so this is one more ordinary participant, not a special case. Requires zero
	/// knowledge of LokrAbilityLab.dll -- only the on-disk convention (folder name "Abilities",
	/// nested ability.txt/icons/localization_*.txt per id) it already documents in
	/// docs/mods-folder-structure.md. Ability icon resolution is unaffected by this move --
	/// PortraitPatches already lives in this assembly and already resolves nested icons/ folders
	/// generically, the same way it does for hero portraits.
	/// </remarks>
	internal static class AbilityLabContentLoader
	{
		private const string Category = "LokrAbilityLab";
		private const string LegacyCategory = "Abilities";

		/// <summary>The KV definition filename inside an ability folder -- mirrors LokrAbilityLab's own AbilityLabPaths.DefinitionFileName, duplicated rather than referenced since this assembly has no dependency on LokrAbilityLab.dll.</summary>
		private const string DefinitionFileName = "ability.txt";

		/// <summary>Subscribes to CharacterAPI.BuildingAbilities and ContributingLocalization.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.BuildingAbilities += OnBuildingAbilities;
			CharacterAPI.ContributingLocalization += OnContributingLocalization;
		}

		/// <summary>Adds each Lab ability.txt, expanding that folder's <c>$alias</c> then making UnitName a legal <c>#word</c>.</summary>
		private static void OnBuildingAbilities(CharacterAPI.AbilitiesBuilder builder)
		{
			foreach (string path in EnumerateAbilityDefinitionPaths())
			{
				string folder = Path.GetDirectoryName(path);
				builder.AddAbilityText(LabExpressionIds.RewriteAbilityText(
					LabAliases.ExpandInFolder(folder, File.ReadAllText(path))), path);
			}
		}

		/// <summary>Merges each ability folder's localization_&lt;suffix&gt;.txt into the requested language.</summary>
		private static IDictionary<string, string> OnContributingLocalization(LocalizationManager.LanguageCode language)
		{
			if (!LocaleFileSuffixes.Map.TryGetValue(language, out string suffix))
			{
				return null;
			}

			Dictionary<string, string> merged = null;
			Regex linePattern = new Regex("^\\s*\"(.*)\"\\s*=\\s*\"(.*)\"\\s*$");
			foreach (string abilityFolder in EnumerateAbilityFolders())
			{
				string path = Path.Combine(abilityFolder, "localization_" + suffix + ".txt");
				if (!File.Exists(path))
				{
					continue;
				}
				foreach (string rawLine in File.ReadAllLines(path))
				{
					Match match = linePattern.Match(LabAliases.ExpandInFolder(abilityFolder, rawLine));
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

		private static IEnumerable<string> EnumerateAbilityDefinitionPaths()
		{
			foreach (string folder in EnumerateAbilityFolders())
			{
				string path = Path.Combine(folder, DefinitionFileName);
				if (File.Exists(path))
				{
					yield return path;
				}
			}
		}

		/// <summary>Ability folders inside each LokrAbilityLab library, plus leftover flat Abilities/&lt;id&gt; folders.</summary>
		private static IEnumerable<string> EnumerateAbilityFolders()
		{
			foreach ((string _, string libraryFolder) in ModAPI.Files.EnumerateCategorySubfolders(Category))
			{
				if (!Directory.Exists(libraryFolder))
				{
					continue;
				}

				foreach (string abilityFolder in Directory.GetDirectories(libraryFolder))
				{
					if (File.Exists(Path.Combine(abilityFolder, DefinitionFileName)))
					{
						yield return abilityFolder;
					}
				}
			}

			foreach ((string _, string abilityFolder) in ModAPI.Files.EnumerateCategorySubfolders(LegacyCategory))
			{
				if (File.Exists(Path.Combine(abilityFolder, DefinitionFileName)))
				{
					yield return abilityFolder;
				}
			}
		}
	}
}
