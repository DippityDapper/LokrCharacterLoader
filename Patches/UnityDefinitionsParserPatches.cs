using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ironhide.Legends.Model.Common.Parsers;
using Ironhide.Legends.Utils;
using KVLib;
using LokrCharacterLoader;
using LokrModAPI;
using UnityEngine;
using Path = System.IO.Path;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace Ironhide.Legends.Model.Game.Units
{
	/// <summary>Full-method replacement of UnityDefinitionsParser.LoadData() and .ParseText(string), firing CharacterAPI's unit-definition events.</summary>
	/// <remarks>
	/// Rows 3, 3b in docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.2. Duplicate-key
	/// guard removed (see docs/content-systems.md §2) so an injected mod override of an existing
	/// unit definition silently replaces it instead of crashing on load. LoadData last-wins
	/// across files (Lab/mod fragment after vanilla) and the UniqueId index last-wins for
	/// level-1 rows, matching ability last-write. See
	/// docs/roadmaps/started/vanilla-character-edit.md Phase 2. LoadData fires
	/// CharacterAPI.BuildingUnitDefinitions to gather RLHeroes/
	/// EnemiesDefinitions text contributions instead of scanning Mods/ itself, and
	/// CharacterAPI.UnitDefinitionLoaded once per parsed definition (again on replace).
	/// Fragments are appended as their own wrapped assets (not spliced into the first
	/// matching vanilla filename).
	///
	/// Declared in the game's own `Ironhide.Legends.Model.Game.Units` namespace (rather than
	/// LokrCharacterLoader.Patches like most other patch classes) purely so ParseText's full-body
	/// copy below can reference UnitDefinition, ParseDebugContext, etc. without a wall of extra
	/// qualification -- has no other effect, Harmony patches by type/method reference regardless
	/// of which namespace the patch class itself lives in.
	/// </remarks>
	[HarmonyPatch(typeof(UnityDefinitionsParser))]
	internal static class UnityDefinitionsParserPatches
	{
		/// <summary>Replaces UnityDefinitionsParser.LoadData(), applying unit-definition contributions before parsing and firing UnitDefinitionLoaded per definition.</summary>
		[HarmonyPatch("LoadData")]
		[HarmonyPrefix]
		private static bool LoadData_Prefix(UnityDefinitionsParser __instance)
		{
			Traverse traverse = Traverse.Create(__instance);
			ParseDebugContext parseDebugContext = traverse.Field<ParseDebugContext>("parseDebugContext").Value;

			try
			{
				Dictionary<string, UnitDefinition> dictionary = new Dictionary<string, UnitDefinition>();

				List<TextAsset> list = new List<TextAsset>();
				foreach (TextAsset item in ResourcesWrapper.LoadAll<TextAsset>("Balance/UnitDefinitions"))
				{
					list.Add(item);
				}
				ApplyUnitDefinitionContributions(list);

				foreach (TextAsset textAsset in list)
				{
					string text = textAsset.text;
					parseDebugContext.Clear();
					parseDebugContext.Enter(textAsset.name);
					foreach (KeyValuePair<string, UnitDefinition> keyValuePair in __instance.ParseText(text))
					{
						bool replaced = ContentRules.AssignLastWins(dictionary, keyValuePair.Key, keyValuePair.Value);
						CharacterAPI.RaiseUnitDefinitionLoaded(keyValuePair.Value);
						if (replaced)
						{
							LokrCharacterLoaderPlugin.Log.LogInfo(
								"LoadData: replaced unit definition '" + keyValuePair.Key
								+ "' from " + textAsset.name + ".");
						}
					}
				}
				parseDebugContext.Clear();
				traverse.Field<IDictionary<string, UnitDefinition>>("definitions").Value = dictionary;
				parseDebugContext.Enter("Inheritance");
				__instance.SolveInheritance();
				parseDebugContext.Exit();
				Dictionary<string, UnitDefinition> byUnique = IndexByUniqueId(dictionary);
				traverse.Field<IDictionary<string, UnitDefinition>>("definitionsByUnique").Value = byUnique;
				AliasUniqueIds(dictionary, byUnique);
			}
			catch (Exception ex)
			{
				string format2 = "ERROR PARSING UNIT DEFINITIONS: {0}\n{1}";
				object[] args = new string[] { parseDebugContext.ToString(), ex.ToString() };
				Debug.LogErrorFormat(format2, args);
				throw;
			}

			return false;
		}

		/// <summary>Fires BuildingUnitDefinitions and appends each RLHeroes/EnemiesDefinitions fragment as its own wrapped TextAsset.</summary>
		/// <remarks>
		/// Earlier this spliced into the first vanilla file whose name contained RLHeroes or
		/// EnemiesDefinitions. Those names are not unique (RLHeroes_new vs SkillUnitDefinitions),
		/// and a missed match dropped Lab summons so GetDefinition fell back to
		/// MissingUnitDefinition. Appending a wrapped asset keeps fragments as top-level block
		/// keys regardless of vanilla file names. See
		/// docs/issues/resolved/sandbox-summon-missing-unit-view.md.
		/// </remarks>
		private static void ApplyUnitDefinitionContributions(List<TextAsset> assetsList)
		{
			CharacterAPI.UnitDefinitionsBuilder builder = CharacterAPI.RaiseBuildingUnitDefinitions();
			AppendDefinitionFragments(assetsList, builder.RLHeroesFragments);
			AppendDefinitionFragments(assetsList, builder.EnemiesDefinitionsFragments);
		}

		/// <summary>Adds each fragment as a ParseText-ready <c>units</c> document, wrapping only when the fragment is not already wrapped.</summary>
		private static void AppendDefinitionFragments(List<TextAsset> assetsList, IReadOnlyList<string> fragments)
		{
			for (int i = 0; i < fragments.Count; i++)
			{
				string fragment = fragments[i];
				if (string.IsNullOrWhiteSpace(fragment))
				{
					continue;
				}
				assetsList.Add(new TextAsset(WrapDefinitionFragment(fragment)));
			}
		}

		/// <summary>Level-1 UniqueId index. Later block wins when two definitions share a UniqueId.</summary>
		/// <remarks>Must run before AliasUniqueIds. That helper copies UniqueId into Definitions as a second key, and ToDictionary(uniqueId) then sees Gerald twice.</remarks>
		private static Dictionary<string, UnitDefinition> IndexByUniqueId(Dictionary<string, UnitDefinition> dictionary)
		{
			Dictionary<string, string> uniqueIdToBlockKey = new Dictionary<string, string>();
			foreach (KeyValuePair<string, UnitDefinition> pair in dictionary)
			{
				int level = Mathf.RoundToInt(pair.Value.stats.GetValueOrDefault("level", -1f));
				if (ContentRules.AssignLevel1UniqueIdLastWins(
					uniqueIdToBlockKey,
					pair.Value.uniqueId,
					pair.Key,
					level,
					out string previousBlockKey))
				{
					LokrCharacterLoaderPlugin.Log.LogInfo(
						"LoadData: UniqueId '" + pair.Value.uniqueId + "' now '" + pair.Key
						+ "' (was '" + previousBlockKey + "').");
				}
			}

			Dictionary<string, UnitDefinition> byUnique = new Dictionary<string, UnitDefinition>();
			foreach (KeyValuePair<string, string> pair in uniqueIdToBlockKey)
			{
				byUnique[pair.Key] = dictionary[pair.Value];
			}

			return byUnique;
		}

		/// <summary>Registers UniqueId as a Definitions key pointing at the winning level-1 row, so sandbox CasterUnitId lookups still hit.</summary>
		private static void AliasUniqueIds(
			Dictionary<string, UnitDefinition> dictionary,
			Dictionary<string, UnitDefinition> byUnique)
		{
			foreach (KeyValuePair<string, UnitDefinition> pair in byUnique)
			{
				dictionary[pair.Key] = pair.Value;
			}

			List<KeyValuePair<string, UnitDefinition>> snapshot = dictionary.ToList();
			for (int i = 0; i < snapshot.Count; i++)
			{
				string uniqueId = snapshot[i].Value.uniqueId;
				if (!string.IsNullOrEmpty(uniqueId) && !dictionary.ContainsKey(uniqueId))
				{
					dictionary.Add(uniqueId, snapshot[i].Value);
				}
			}
		}

		/// <summary>ParseText requires one root whose children are unit blocks; Lab rlheroes.txt files are already those children.</summary>
		private static string WrapDefinitionFragment(string fragment)
		{
			string trimmed = fragment.TrimStart();
			if (trimmed.StartsWith("\"units\"", StringComparison.Ordinal))
			{
				return fragment;
			}
			return "\"units\"\n{\n" + fragment + "\n}\n";
		}

		/// <summary>Registers the Mods/*/RLHeroes and Mods/*/EnemiesDefinitions file-convention contributors.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.BuildingUnitDefinitions += builder =>
			{
				foreach ((string modFolder, string filePath) in ModAPI.Files.EnumerateCategoryFiles("RLHeroes"))
				{
					builder.AddHeroDefinition(LabExpressionIds.NormalizeDefinitionKeys(File.ReadAllText(filePath)));
				}
				foreach ((string modFolder, string filePath) in ModAPI.Files.EnumerateCategoryFiles("EnemiesDefinitions"))
				{
					builder.AddEnemyDefinition(LabExpressionIds.NormalizeDefinitionKeys(File.ReadAllText(filePath)));
				}
			};
		}

		/// <summary>Strips a leading <c>#</c> and resolves Lab generated ids under both the folder spelling and the expression-safe <c>c</c> prefix.</summary>
		/// <remarks>
		/// GetDefinition keys off the KV block name, not UniqueId. EnemySummon blocks have no
		/// UniqueId. A missed key returns MissingUnitDefinition (purple MissingUnitViewComplex)
		/// instead of throwing. See docs/issues/resolved/sandbox-summon-missing-unit-view.md.
		/// </remarks>
		[HarmonyPatch(nameof(UnityDefinitionsParser.GetDefinition))]
		[HarmonyPrefix]
		private static bool GetDefinition_Prefix(UnityDefinitionsParser __instance, ref string id, ref UnitDefinition __result)
		{
			if (string.IsNullOrEmpty(id))
			{
				return true;
			}
			if (id[0] == '#' && id.Length > 1)
			{
				id = id.Substring(1);
			}

			IDictionary<string, UnitDefinition> definitions =
				Traverse.Create(__instance).Field<IDictionary<string, UnitDefinition>>("definitions").Value;
			if (definitions != null && TryResolveDefinition(definitions, id, out UnitDefinition found))
			{
				__result = found;
				return false;
			}

			if (LabExpressionIds.LooksLikeGeneratedId(id))
			{
				LokrCharacterLoaderPlugin.Log.LogWarning(
					"GetDefinition: no unit '" + id + "'; SpawnUnit will use MissingUnitDefinition.");
			}
			return true;
		}

		/// <summary>True when definitions contains id, its <c>c</c>-prefixed form, or the unprefixed folder id.</summary>
		private static bool TryResolveDefinition(
			IDictionary<string, UnitDefinition> definitions,
			string id,
			out UnitDefinition found)
		{
			if (definitions.TryGetValue(id, out found))
			{
				return true;
			}
			string prefixed = LabExpressionIds.ToExpressionSafeKey(id);
			if (prefixed != id && definitions.TryGetValue(prefixed, out found))
			{
				return true;
			}
			string folderId = LabExpressionIds.ToFolderId(id);
			return folderId != id && definitions.TryGetValue(folderId, out found);
		}

		/// <summary>Replaces UnityDefinitionsParser.ParseText, parsing KV unit-definition text into a dictionary keyed by id (duplicate-key guard removed -- see class remarks).</summary>
		[HarmonyPatch("ParseText")]
		[HarmonyPrefix]
		private static bool ParseText_Prefix(UnityDefinitionsParser __instance, string text, ref IDictionary<string, UnitDefinition> __result)
		{
			ParseDebugContext parseDebugContext = Traverse.Create(__instance).Field<ParseDebugContext>("parseDebugContext").Value;

			KeyValue[] array = KVParser.KV1.ParseAll(text);
			if (array.Length != 1)
			{
				throw new Exception("Wrong content of UnitsDefinitions");
			}
			KeyValue keyValue = array[0];
			IDictionary<string, UnitDefinition> dictionary = new Dictionary<string, UnitDefinition>();
			foreach (KeyValue keyValue2 in keyValue.Children)
			{
				parseDebugContext.Enter(keyValue2.Key);
				UnitDefinition unitDefinition = new UnitDefinition();
				unitDefinition.id = keyValue2.Key;
				parseDebugContext.Enter("InheritsFrom");
				KeyValue keyValue3 = keyValue2["InheritsFrom"];
				if (keyValue3 != null)
				{
					unitDefinition.inheritsFrom = keyValue3.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("UniqueId");
				KeyValue keyValue4 = keyValue2["UniqueId"];
				if (keyValue4 != null)
				{
					unitDefinition.uniqueId = keyValue4.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("Name");
				KeyValue keyValue5 = keyValue2["Name"];
				if (keyValue5 != null)
				{
					unitDefinition.name = keyValue5.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("UnitClass");
				KeyValue keyValue6 = keyValue2["UnitClass"];
				if (keyValue6 != null)
				{
					unitDefinition.unitClass = keyValue6.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("Icon");
				KeyValue keyValue7 = keyValue2["Icon"];
				if (keyValue7 != null)
				{
					unitDefinition.icon = keyValue7.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("Background");
				KeyValue keyValue8 = keyValue2["Background"];
				if (keyValue8 != null)
				{
					unitDefinition.background = keyValue8.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("AttackType");
				KeyValue keyValue9 = keyValue2["AttackType"];
				if (keyValue9 != null)
				{
					unitDefinition.attackType = keyValue9.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("MetaExo");
				KeyValue keyValue10 = keyValue2["MetaExo"];
				if (keyValue10 != null)
				{
					unitDefinition.metaExo = keyValue10.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("Model");
				KeyValue keyValue11 = keyValue2["Model"];
				if (keyValue11 != null)
				{
					unitDefinition.kind = keyValue11.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("stats");
				Dictionary<string, float> dictionary2 = new Dictionary<string, float>();
				KeyValue keyValue12 = keyValue2["stats"];
				if (keyValue12 != null)
				{
					foreach (KeyValue keyValue13 in keyValue12.Children)
					{
						parseDebugContext.Enter(keyValue13.Key);
						dictionary2.Add(keyValue13.Key, keyValue13.GetFloat());
						parseDebugContext.Exit();
					}
				}
				unitDefinition.stats = dictionary2;
				parseDebugContext.Exit();
				parseDebugContext.Enter("skills");
				KeyValue keyValue14 = keyValue2["skills"];
				if (keyValue14 != null)
				{
					List<string> list = new List<string>();
					foreach (KeyValue keyValue15 in keyValue14.Children)
					{
						parseDebugContext.Enter(keyValue15.Key);
						list.Add(keyValue15.GetString());
						parseDebugContext.Exit();
					}
					unitDefinition.skills = list;
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("skillPool");
				KeyValue keyValue16 = keyValue2["skillPool"];
				if (keyValue16 != null)
				{
					List<string> list2 = new List<string>();
					foreach (KeyValue keyValue17 in keyValue16.Children)
					{
						parseDebugContext.Enter(keyValue17.Key);
						list2.Add(keyValue17.GetString());
						parseDebugContext.Exit();
					}
					unitDefinition.skillPool = list2;
				}
				else
				{
					unitDefinition.skillPool = new List<string>();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("states");
				Dictionary<string, int> dictionary3 = new Dictionary<string, int>();
				KeyValue keyValue18 = keyValue2["states"];
				if (keyValue18 != null)
				{
					foreach (KeyValue keyValue19 in keyValue18.Children)
					{
						parseDebugContext.Enter(keyValue19.Key);
						dictionary3.Add(keyValue19.Key, keyValue19.GetInt());
						parseDebugContext.Exit();
					}
				}
				unitDefinition.states = dictionary3;
				parseDebugContext.Exit();
				parseDebugContext.Enter("defaultSkill");
				KeyValue keyValue20 = keyValue2["defaultSkill"];
				if (keyValue20 != null)
				{
					unitDefinition.defaultSkill = keyValue20.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("nextLevelArchetype");
				KeyValue keyValue21 = keyValue2["nextLevelArchetype"];
				if (keyValue21 != null)
				{
					unitDefinition.nextLevelArchetype = keyValue21.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("cinematicTags");
				KeyValue keyValue22 = keyValue2["cinematicTags"];
				if (keyValue22 != null)
				{
					unitDefinition.cinematicTags = keyValue22.GetString().Split('|').Select(u => u.Trim()).ToList();
				}
				else
				{
					unitDefinition.cinematicTags = new List<string>();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("soundConfig");
				KeyValue keyValue23 = keyValue2["soundConfig"];
				SoundConfig soundConfig = new SoundConfig();
				Dictionary<string, string> dictionary4 = new Dictionary<string, string>();
				soundConfig.soundClips = dictionary4;
				if (keyValue23 != null)
				{
					KeyValue keyValue24 = keyValue23["assetId"];
					if (keyValue24 != null)
					{
						soundConfig.assetId = keyValue24.GetString();
					}
					foreach (KeyValue keyValue25 in keyValue23["sounds"].Children)
					{
						parseDebugContext.Enter(keyValue25.Key);
						dictionary4[keyValue25.Key] = keyValue25.GetString();
						parseDebugContext.Exit();
					}
				}
				unitDefinition.soundConfig = soundConfig;
				parseDebugContext.Exit();
				parseDebugContext.Enter("configs");
				Dictionary<string, string> dictionary5 = new Dictionary<string, string>();
				KeyValue keyValue26 = keyValue2["configs"];
				if (keyValue26 != null)
				{
					foreach (KeyValue keyValue27 in keyValue26.Children)
					{
						parseDebugContext.Enter(keyValue27.Key);
						dictionary5.Add(keyValue27.Key, keyValue27.GetString());
						parseDebugContext.Exit();
					}
				}
				unitDefinition.configs = dictionary5;
				parseDebugContext.Exit();
				parseDebugContext.Enter("unitOnMap");
				KeyValue keyValue28 = keyValue2["UnitOnMap"];
				if (keyValue28 != null)
				{
					unitDefinition.unitOnMap = keyValue28.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("PortraitBackgroundColor");
				KeyValue keyValue29 = keyValue2["PortraitBackgroundColor"];
				if (keyValue29 != null)
				{
					unitDefinition.portraitBackgroundColor = keyValue29.GetString();
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("skillProgression");
				KeyValue keyValue30 = keyValue2["skillProgression"];
				if (keyValue30 != null)
				{
					Dictionary<int, List<string>> dictionary6 = new Dictionary<int, List<string>>();
					foreach (KeyValue keyValue31 in keyValue30.Children)
					{
						parseDebugContext.Enter(keyValue31.Key);
						int key = int.Parse(keyValue31.Key);
						List<string> list3 = new List<string>();
						foreach (KeyValue keyValue32 in keyValue31.Children)
						{
							parseDebugContext.Enter(keyValue32.Key);
							list3.Add(keyValue32.GetString());
							parseDebugContext.Exit();
						}
						dictionary6.Add(key, list3);
						parseDebugContext.Exit();
					}
					unitDefinition.skillProgression = dictionary6;
				}
				parseDebugContext.Exit();
				parseDebugContext.Enter("SKILLVARIANT-HACK");
				if (unitDefinition.skillProgression == null && !unitDefinition.skillPool.IsEmpty())
				{
					unitDefinition.skillProgression = new Dictionary<int, List<string>>();
					unitDefinition.skills.Add(unitDefinition.skillPool[0]);
					unitDefinition.skillProgression.Add(2, new List<string> { unitDefinition.skillPool[1] });
					unitDefinition.skillProgression.Add(3, new List<string> { unitDefinition.skillPool[2] });
					Debug.LogWarning("UNITDEFINITION: Migrating to skillProgression unit: " + unitDefinition.id);
				}
				if (unitDefinition.nextLevelArchetype != null && unitDefinition.nextLevelArchetype.EndsWith("4"))
				{
					unitDefinition.nextLevelArchetype = null;
					Debug.LogWarning("UNITDEFINITION: Disabling lvl4 unit: " + unitDefinition.id);
				}
				parseDebugContext.Exit();
				parseDebugContext.Exit();
				dictionary[keyValue2.Key] = unitDefinition;
			}

			__result = dictionary;
			return false;
		}
	}
}
