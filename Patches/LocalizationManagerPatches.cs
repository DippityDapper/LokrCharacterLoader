using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Ironhide.Legends.Builds;
using Ironhide.Legends.Services.Persistence;
using Ironhide.Legends.Utils;
using Ironhide.Localization;
using LokrModAPI;
using UnityEngine;
using File = System.IO.File;
using Path = System.IO.Path;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Full-method replacement of LocalizationManager.Load(LanguageCode), merging in CharacterAPI.ContributingLocalization contributions.</summary>
	/// <remarks>
	/// Row 14 in docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.5.
	///
	/// Bug fix along the way: the previous recompiled-DLL mod hardcoded LanguageCode.JA for five
	/// of its six modded-localization lookups instead of passing the language each call site
	/// actually represents. Fixed here -- each call site now passes its real language (EN for the
	/// English-reference merge, currLanguageCode for the AUTO/QUEST/override-path merges and the
	/// LanguageNames list). Contributions now go through CharacterAPI.ContributingLocalization
	/// (returning parsed key/value pairs merged into the result) rather than raw-text
	/// concatenation before parsing.
	/// </remarks>
	[HarmonyPatch(typeof(LocalizationManager), "Load")]
	internal static class LocalizationManagerPatches
	{
		private static readonly MethodInfo ParseKVTextMethod =
			AccessTools.Method(typeof(LocalizationManager), "ParseKVText");

		/// <summary>Replaces LocalizationManager.Load(languageCode), loading every localization source and merging in CharacterAPI contributions.</summary>
		[HarmonyPrefix]
		private static bool Prefix(LocalizationManager __instance, LocalizationManager.LanguageCode languageCode)
		{
			Traverse traverse = Traverse.Create(__instance);

			UserSettings.Language = languageCode;
			__instance.currLanguageCode = languageCode;

			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();

			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			Dictionary<string, List<string>> dictionary2 = new Dictionary<string, List<string>>();

			Dictionary<string, string> newData = LoadKVTextWithMods(Path.Combine("Localization", "LanguageNames"), __instance.currLanguageCode);
			dictionary.MergeWith(newData, true, true);

			if (__instance.currLanguageCode != LocalizationManager.LanguageCode.EN)
			{
				Dictionary<string, string> newData2 = LoadKVTextWithMods(Path.Combine("Localization", __instance.fileNamesMapping[LocalizationManager.LanguageCode.EN]), LocalizationManager.LanguageCode.EN);
				dictionary.MergeWith(newData2, true, true);
				if (LocalizationManager.USE_AUTO_LOCALIZATION)
				{
					Dictionary<string, string> newData3 = LoadKVTextWithMods(Path.Combine("Localization", "AUTO_" + __instance.fileNamesMapping[__instance.currLanguageCode]), __instance.currLanguageCode);
					dictionary.MergeWith(newData3, true, true);
				}
			}

			Dictionary<string, string> newData4 = LoadKVTextWithMods(Path.Combine("Localization", __instance.fileNamesMapping[__instance.currLanguageCode]), __instance.currLanguageCode);
			dictionary.MergeWith(newData4, true, true);

			Dictionary<string, string> newData5 = LoadKVTextWithMods(Path.Combine("Localization", "QUEST_" + __instance.fileNamesMapping[__instance.currLanguageCode]), __instance.currLanguageCode);
			dictionary.MergeWith(newData5, true, true);

			string text = null;
			BuildFlavorLocalization localization = BuildFlavors.config.localization;
			if (localization != null)
			{
				if (!string.IsNullOrEmpty(localization.overridePath))
				{
					Dictionary<string, string> newData6 = LoadKVTextWithMods(Path.Combine("Localization", localization.overridePath, __instance.fileNamesMapping[__instance.currLanguageCode] + "-" + localization.overridePath), __instance.currLanguageCode);
					dictionary.MergeWith(newData6, true, true);
				}
				if (!string.IsNullOrEmpty(localization.overrideSuffix))
				{
					text = "_" + localization.overrideSuffix;
				}
			}

			List<string> list = new List<string>();
			Regex regex = new Regex("_[0-9]{4}$");
			foreach (KeyValuePair<string, string> keyValuePair in dictionary)
			{
				if (regex.IsMatch(keyValuePair.Key))
				{
					string key = keyValuePair.Key.Substring(0, keyValuePair.Key.Length - 4);
					List<string> valueOrDefaultFunc = dictionary2.GetValueOrDefaultFunc(key, () => new List<string>());
					valueOrDefaultFunc.Add(keyValuePair.Value);
					dictionary2[key] = valueOrDefaultFunc;
				}
				if (text != null && keyValuePair.Key.EndsWith(text))
				{
					list.Add(keyValuePair.Key);
				}
			}
			for (int i = 0; i < list.Count; i++)
			{
				string text2 = list[i];
				string key2 = text2.Substring(0, text2.Length - text.Length);
				if (dictionary.ContainsKey(key2))
				{
					dictionary[key2] = dictionary[text2];
				}
			}

			traverse.Field<Dictionary<string, string>>("database").Value = dictionary;
			traverse.Field<Dictionary<string, List<string>>>("multiValueDatabase").Value = dictionary2;

			double num = stopwatch.Elapsed.TotalMilliseconds / 1000.0;
			UnityEngine.Debug.Log(string.Format("Finished loading localizable strings - Loaded {0} entries in {1} seconds", dictionary.Count, num));

			return false;
		}

		/// <summary>Loads a KV localization resource and merges in every CharacterAPI.ContributingLocalization contributor's strings for the given language.</summary>
		private static Dictionary<string, string> LoadKVTextWithMods(string path, LocalizationManager.LanguageCode currLang)
		{
			TextAsset textAsset = ResourcesWrapper.Load<TextAsset>(path);
			Dictionary<string, string> result = textAsset != null
				? (Dictionary<string, string>)ParseKVTextMethod.Invoke(null, new object[] { textAsset.text })
				: new Dictionary<string, string>();

			foreach (IDictionary<string, string> contribution in CharacterAPI.RaiseContributingLocalization(currLang))
			{
				foreach (KeyValuePair<string, string> kv in contribution)
				{
					result[kv.Key] = kv.Value;
				}
			}
			return result;
		}

		/// <summary>Registers the Mods/*/Localization file-convention contributor.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.ContributingLocalization += currLang =>
			{
				string suffix = LanguageMapping(currLang);
				if (suffix == null)
				{
					return null;
				}
				Dictionary<string, string> merged = null;
				foreach ((string modFolder, string filePath) in ModAPI.Files.EnumerateCategoryFiles("Localization", "*_" + suffix + ".txt"))
				{
					try
					{
						string rawText = File.ReadAllText(filePath);
						Dictionary<string, string> parsed = (Dictionary<string, string>)ParseKVTextMethod.Invoke(null, new object[] { rawText });
						merged ??= new Dictionary<string, string>();
						foreach (KeyValuePair<string, string> kv in parsed)
						{
							merged[kv.Key] = kv.Value;
						}
					}
					catch (Exception ex)
					{
						UnityEngine.Debug.LogError(string.Format("Failed to read localization file '{0}': {1}", filePath, ex));
					}
				}
				return merged;
			};
		}

		/// <summary>Maps a LanguageCode to the file-suffix convention Mods/*/Localization/*_&lt;suffix&gt;.txt uses.</summary>
		private static string LanguageMapping(LocalizationManager.LanguageCode currLang)
		{
			switch (currLang)
			{
				case LocalizationManager.LanguageCode.EN: return "en_US";
				case LocalizationManager.LanguageCode.EN_GB: return "en-gb";
				case LocalizationManager.LanguageCode.ES: return "es";
				case LocalizationManager.LanguageCode.DE: return "de";
				case LocalizationManager.LanguageCode.RU: return "ru";
				case LocalizationManager.LanguageCode.FR: return "fr";
				case LocalizationManager.LanguageCode.FR_CA: return "fr-ca";
				case LocalizationManager.LanguageCode.IT: return "it";
				case LocalizationManager.LanguageCode.TR: return "tr";
				case LocalizationManager.LanguageCode.ZH_HANS: return "zh-Hans";
				case LocalizationManager.LanguageCode.ZH_HANT: return "zh-Hant";
				case LocalizationManager.LanguageCode.AR: return "ar";
				case LocalizationManager.LanguageCode.PT: return "pt";
				case LocalizationManager.LanguageCode.JA: return "ja";
				case LocalizationManager.LanguageCode.NL: return "nl";
				case LocalizationManager.LanguageCode.KO: return "ko";
				default: return null;
			}
		}
	}
}
