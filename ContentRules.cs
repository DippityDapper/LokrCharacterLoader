using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LokrCharacterLoader
{
	/// <summary>Unity-free skip/clamp/merge rules used by Character Loader patches and the xUnit suite.</summary>
	/// <remarks>
	/// Harmony prefixes stay thin: they read vanilla fields, call these helpers, then skip or write.
	/// Tests compile this file as linked source so they never load player UnityEngine.
	/// </remarks>
	internal static class ContentRules
	{
		/// <summary>Drops list entries past <paramref name="cap"/> so SkillsBar never indexes a missing hex slot.</summary>
		internal static void TrimListToCap<T>(List<T> list, int cap)
		{
			if (list == null || cap <= 0 || list.Count <= cap)
			{
				return;
			}

			list.RemoveRange(cap, list.Count - cap);
		}

		/// <summary>True when INVISIBLE went from off to on (AddModifier edge).</summary>
		internal static bool ShouldRaiseInvisibilityEnter(bool wasOn, bool isOn)
		{
			return !wasOn && isOn;
		}

		/// <summary>True when INVISIBLE went from on to off (TurnEnded edge), not for units that were never invisible.</summary>
		internal static bool ShouldRaiseInvisibilityExit(bool wasOn, bool isOn)
		{
			return wasOn && !isOn;
		}

		/// <summary>True when FindPartIndex returned a writable index (not -1).</summary>
		internal static bool ShouldWritePartAtIndex(int index)
		{
			return index >= 0;
		}

		/// <summary>True when Hero.exoSkeletonDataAsset must skip vanilla because <c>unitDefinition</c> is null.</summary>
		internal static bool ShouldSkipExoResolveForNullDefinition(bool unitDefinitionIsNull)
		{
			return unitDefinitionIsNull;
		}

		/// <summary>True when a packed atlas sprite name matches a rig.json part, using vanilla FindSprite rules.</summary>
		/// <remarks>Case-insensitive; a '#' suffix is stripped from the sprite name only.</remarks>
		internal static bool PackedSpriteNameMatchesPart(string spriteName, string partName)
		{
			if (string.IsNullOrEmpty(partName) || string.IsNullOrEmpty(spriteName))
			{
				return false;
			}

			int hash = spriteName.IndexOf('#');
			string text = hash != -1
				? spriteName.Substring(hash + 1, spriteName.Length - (hash + 1))
				: spriteName;
			return string.Equals(text, partName, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>True when any packed sprite name matches <paramref name="partName"/> so ReloadData will not NRE on a miss.</summary>
		internal static bool AnyPackedSpriteMatchesPart(IEnumerable<string> spriteNames, string partName)
		{
			if (spriteNames == null || string.IsNullOrEmpty(partName))
			{
				return false;
			}

			foreach (string spriteName in spriteNames)
			{
				if (PackedSpriteNameMatchesPart(spriteName, partName))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>True when a buff-store heroPosition can index GetAllHeroes without throwing.</summary>
		internal static bool IsHeroPositionInRange(int heroPosition, int heroCount)
		{
			return heroCount > 0 && heroPosition >= 0 && heroPosition < heroCount;
		}

		/// <summary>True when a portrait resolver must not Path.Combine with this heroId.</summary>
		internal static bool ShouldSkipPortraitResolve(string heroId)
		{
			return string.IsNullOrEmpty(heroId);
		}

		/// <summary>Unions two uniqueId lists in order, skipping null, empty, and duplicates (case-insensitive).</summary>
		internal static List<string> MergeUniqueIds(IEnumerable<string> first, IEnumerable<string> second)
		{
			List<string> result = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			AppendUnique(result, seen, first);
			AppendUnique(result, seen, second);
			return result;
		}

		private static void AppendUnique(List<string> result, HashSet<string> seen, IEnumerable<string> source)
		{
			if (source == null)
			{
				return;
			}

			foreach (string id in source)
			{
				if (string.IsNullOrEmpty(id) || !seen.Add(id))
				{
					continue;
				}

				result.Add(id);
			}
		}

		/// <summary>Last-wins assign. True when <paramref name="key"/> was already present.</summary>
		internal static bool AssignLastWins<T>(IDictionary<string, T> dest, string key, T value)
		{
			if (dest == null || string.IsNullOrEmpty(key))
			{
				return false;
			}

			bool replaced = dest.ContainsKey(key);
			dest[key] = value;
			return replaced;
		}

		/// <summary>Last-wins UniqueId index for level-1 rows. True when this uniqueId already pointed at a different block key.</summary>
		internal static bool AssignLevel1UniqueIdLastWins(
			IDictionary<string, string> uniqueIdToBlockKey,
			string uniqueId,
			string blockKey,
			int level,
			out string previousBlockKey)
		{
			previousBlockKey = null;
			if (uniqueIdToBlockKey == null
				|| string.IsNullOrEmpty(uniqueId)
				|| string.IsNullOrEmpty(blockKey)
				|| level != 1)
			{
				return false;
			}

			bool replaced = uniqueIdToBlockKey.TryGetValue(uniqueId, out previousBlockKey)
				&& !string.Equals(previousBlockKey, blockKey, StringComparison.Ordinal);
			uniqueIdToBlockKey[uniqueId] = blockKey;
			if (!replaced)
			{
				previousBlockKey = null;
			}

			return replaced;
		}

		/// <summary>Reads a roster fragment or array entry's <c>id</c> field.</summary>
		internal static string ReadRosterId(string fragment)
		{
			if (string.IsNullOrEmpty(fragment))
			{
				return null;
			}

			Match match = RosterIdPattern.Match(fragment);
			return match.Success ? match.Groups[1].Value : null;
		}

		/// <summary>Splices roster JSON fragments into a legends or companions array. Same id replaces the existing object; new ids append.</summary>
		internal static string MergeRosterArray(string jsonArray, IEnumerable<string> fragments)
		{
			List<string> objects = ExtractJsonObjects(jsonArray);
			Dictionary<string, int> indexById = new Dictionary<string, int>(StringComparer.Ordinal);
			List<string> ordered = new List<string>();

			for (int i = 0; i < objects.Count; i++)
			{
				TakeRosterObject(objects[i], indexById, ordered);
			}

			if (fragments != null)
			{
				foreach (string fragment in fragments)
				{
					if (string.IsNullOrWhiteSpace(fragment))
					{
						continue;
					}

					TakeRosterObject(fragment.Trim(), indexById, ordered);
				}
			}

			return RebuildRosterArray(ordered);
		}

		/// <summary>Top-level <c>{...}</c> objects in a JSON array, using brace matching that ignores braces inside strings.</summary>
		internal static List<string> ExtractJsonObjects(string json)
		{
			List<string> result = new List<string>();
			if (string.IsNullOrEmpty(json))
			{
				return result;
			}

			int i = 0;
			while (i < json.Length)
			{
				if (json[i] != '{')
				{
					i++;
					continue;
				}

				int start = i;
				int depth = 0;
				bool inString = false;
				bool escape = false;
				for (; i < json.Length; i++)
				{
					char c = json[i];
					if (inString)
					{
						if (escape)
						{
							escape = false;
							continue;
						}

						if (c == '\\')
						{
							escape = true;
							continue;
						}

						if (c == '"')
						{
							inString = false;
						}

						continue;
					}

					if (c == '"')
					{
						inString = true;
						continue;
					}

					if (c == '{')
					{
						depth++;
					}
					else if (c == '}')
					{
						depth--;
						if (depth == 0)
						{
							result.Add(json.Substring(start, i - start + 1));
							i++;
							break;
						}
					}
				}
			}

			return result;
		}

		private static void TakeRosterObject(
			string jsonObject,
			Dictionary<string, int> indexById,
			List<string> ordered)
		{
			string id = ReadRosterId(jsonObject);
			if (string.IsNullOrEmpty(id))
			{
				ordered.Add(jsonObject);
				return;
			}

			if (indexById.TryGetValue(id, out int existing))
			{
				ordered[existing] = jsonObject;
				return;
			}

			indexById[id] = ordered.Count;
			ordered.Add(jsonObject);
		}

		private static string RebuildRosterArray(IReadOnlyList<string> objects)
		{
			if (objects == null || objects.Count == 0)
			{
				return "[ ]";
			}

			StringBuilder text = new StringBuilder();
			text.Append("[\n");
			for (int i = 0; i < objects.Count; i++)
			{
				if (i > 0)
				{
					text.Append(",\n");
				}

				text.Append('\t').Append(objects[i]);
			}

			text.Append("\n]");
			return text.ToString();
		}

		private static readonly Regex RosterIdPattern = new Regex("\"id\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
	}
}
