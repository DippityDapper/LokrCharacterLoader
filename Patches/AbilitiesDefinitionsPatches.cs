using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using KVLib;
using LokrCharacterLoader;
using LokrModAPI;
using UnityEngine;
using File = System.IO.File;

namespace Ironhide.Legends.Model.Game.Units.Abilities
{
	/// <summary>Full-method replacement of AbilitiesDefinitions.Load(), firing CharacterAPI's ability-building events.</summary>
	/// <remarks>
	/// Row 4 in docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.2. Declared in the
	/// game's own namespace for the same reason as UnityDefinitionsParserPatches (direct access to
	/// Ability/Modifier/AbilityParser). Fires CharacterAPI.BuildingAbilities for KV-text
	/// contributions and applies CharacterAPI.RegisterAbility(...) registrations directly,
	/// alongside the existing Mods/*/NewAbilities/*.txt file convention.
	/// </remarks>
	[HarmonyPatch(typeof(AbilitiesDefinitions))]
	internal static class AbilitiesDefinitionsPatches
	{
		/// <summary>Replaces AbilitiesDefinitions.Load(), applying KV-text and programmatic ability contributions.</summary>
		/// <remarks>Programmatic abilities (CharacterAPI.RegisterAbility) are applied after KV-text-based ones, so a code-registered ability can override a file-based one (or vice versa if registered before Load() first runs) by reusing its abilityId.</remarks>
		[HarmonyPatch("Load")]
		[HarmonyPrefix]
		private static bool Load_Prefix(AbilitiesDefinitions __instance)
		{
			if (__instance.abilities.Count > 0)
			{
				return false;
			}
			ExecuteLoad(__instance);
			return false;
		}

		/// <summary>Re-reads ability definitions from disk, bypassing the load-once guard.</summary>
		internal static void ForceReload(AbilitiesDefinitions __instance)
		{
			__instance.abilities.Clear();
			__instance.ability_modifiers.Clear();
			ExecuteLoad(__instance);
		}

		private static void ExecuteLoad(AbilitiesDefinitions __instance)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();

			string abilitiesFolder = Traverse.Create(__instance).Field<string>("abilitiesFolder").Value;
			List<TextAsset> list = new List<TextAsset>();
			foreach (TextAsset item in ResourcesWrapper.LoadAll<TextAsset>(abilitiesFolder))
			{
				list.Add(item);
			}
			HashSet<TextAsset> fromResources = new HashSet<TextAsset>(list);
			ApplyAbilityContributions(list);

			AbilityParser abilityParser = new AbilityParser();
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			stopwatch2.Stop();
			stopwatch2.Reset();

			foreach (TextAsset textAsset in list)
			{
				string text = textAsset.text;
				if (fromResources.Contains(textAsset))
				{
					Resources.UnloadAsset(textAsset);
				}
				try
				{
					KeyValue[] array2 = KVParser.KV1.ParseAll(text);
					stopwatch2.Start();
					foreach (KeyValue keyValue in array2)
					{
						Ability ability = abilityParser.ParseAbility(keyValue);
						if (ability != null)
						{
							__instance.abilities[ability.abilityId] = ability;
							foreach (Modifier modifier in ability.Modifiers)
							{
								__instance.ability_modifiers[modifier.modifierId] = modifier;
							}
						}
						else
						{
							string format = "ERROR PARSING: Could not load ability {0}";
							object[] args = new string[] { keyValue.Key };
							UnityEngine.Debug.LogErrorFormat(format, args);
						}
					}
					stopwatch2.Stop();
				}
				catch (Exception ex)
				{
					string preview = text ?? string.Empty;
					preview = preview.Replace('\r', ' ').Replace('\n', ' ');
					if (preview.Length > 160)
					{
						preview = preview.Substring(0, 160);
					}

					string format2 = "ERROR PARSING: Could not parse kv in file {0} - EXCEPTION\n{1}\nPreview: {2}";
					object[] args2 = new string[]
					{
						string.IsNullOrEmpty(textAsset.name) ? "(unnamed)" : textAsset.name,
						ex.ToString(),
						string.IsNullOrEmpty(preview) ? "(empty)" : preview
					};
					UnityEngine.Debug.LogErrorFormat(format2, args2);
				}
			}

			foreach (Ability ability in CharacterAPI.GetRegisteredAbilities())
			{
				__instance.abilities[ability.abilityId] = ability;
				foreach (Modifier modifier in ability.Modifiers)
				{
					__instance.ability_modifiers[modifier.modifierId] = modifier;
				}
			}

			UnityEngine.Debug.LogFormat("AbilitiesDefinition: Loaded in {0} with {1} of parsing", new object[]
			{
				(float)stopwatch.ElapsedMilliseconds / 1000f,
				(float)stopwatch2.ElapsedMilliseconds / 1000f
			});
		}

		/// <summary>Fires BuildingAbilities and appends the resulting KV-text fragments as TextAssets.</summary>
		/// <remarks>KvTextFragments is `internal` -- accessible here since this patch class lives in the same LokrCharacterLoader assembly, just a different (game-matching) C# namespace.</remarks>
		private static void ApplyAbilityContributions(List<TextAsset> abilitiesList)
		{
			CharacterAPI.AbilitiesBuilder builder = CharacterAPI.RaiseBuildingAbilities();
			for (int i = 0; i < builder.KvTextFragments.Count; i++)
			{
				string kvText = builder.KvTextFragments[i];
				if (string.IsNullOrWhiteSpace(kvText))
				{
					continue;
				}

				TextAsset asset = new TextAsset(kvText);
				asset.name = i < builder.KvTextSources.Count
					? builder.KvTextSources[i]
					: "mod-ability-" + (i + 1);
				abilitiesList.Add(asset);
			}
		}

		/// <summary>Registers the Mods/*/NewAbilities file-convention contributor.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.BuildingAbilities += builder =>
			{
				foreach ((string modFolder, string filePath) in ModAPI.Files.EnumerateCategoryFiles("NewAbilities", "*.txt"))
				{
					builder.AddAbilityText(
						LabExpressionIds.RewriteAbilityText(File.ReadAllText(filePath)),
						filePath);
				}
			};
		}
	}
}
