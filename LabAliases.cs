using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LokrModAPI.Serialization;

namespace LokrCharacterLoader
{
	/// <summary>Per-folder aliases.json map and <c>$alias</c> expand used by Lab loaders and the Lab editor.</summary>
	/// <remarks>
	/// Expansion is <c>$key</c> only, and only from that same folder's file. Bare KV values that
	/// happen to equal a key (InheritsFrom "Hero") stay untouched. See
	/// docs/roadmaps/completed/human-readable-ids.md.
	/// </remarks>
	public static class LabAliases
	{
		/// <summary>On-disk filename inside a character or ability folder.</summary>
		public const string FileName = "aliases.json";

		private static readonly Regex PairPattern = new Regex("\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
		private static readonly Regex AliasRefPattern = new Regex(@"\$([A-Za-z][A-Za-z0-9_]*)");

		/// <summary>Reads aliases.json from folder, or an empty map when the file is missing.</summary>
		public static Dictionary<string, string> Load(string folder)
		{
			Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(folder))
			{
				return map;
			}

			string path = Path.Combine(folder, FileName);
			if (!File.Exists(path))
			{
				return map;
			}

			foreach (Match match in PairPattern.Matches(File.ReadAllText(path)))
			{
				string key = match.Groups[1].Value;
				if (string.IsNullOrEmpty(key) || string.Equals(key, FileName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				map[key] = match.Groups[2].Value ?? string.Empty;
			}

			return map;
		}

		/// <summary>Writes aliases.json as a flat string-to-string object.</summary>
		public static void Save(string folder, Dictionary<string, string> map)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return;
			}

			Directory.CreateDirectory(folder);
			StringBuilder json = new StringBuilder();
			json.Append("{\n");
			if (map != null)
			{
				List<string> keys = new List<string>(map.Keys);
				keys.Sort(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < keys.Count; i++)
				{
					string key = keys[i];
					if (string.IsNullOrEmpty(key))
					{
						continue;
					}

					json.Append("  \"").Append(TextEscaping.JsonEscape(key)).Append("\": \"")
						.Append(TextEscaping.JsonEscape(map[key] ?? string.Empty)).Append('"');
					if (i + 1 < keys.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}
			}

			json.Append("}\n");
			File.WriteAllText(Path.Combine(folder, FileName), json.ToString());
		}

		/// <summary>Replaces <c>$key</c> in text using that folder's aliases.json. Unknown <c>$word</c> stays.</summary>
		public static string ExpandInFolder(string folder, string text)
		{
			return Expand(text, Load(folder));
		}

		/// <summary>Replaces each <c>$key</c> whose key is in the map. Leaves every other token alone.</summary>
		/// <remarks>
		/// Loc keys are written as UNIT_$alias_NAME_0001. The greedy <c>$word</c> pattern would
		/// capture assassin_NAME_0001, miss the alias, and leave the roster looking up
		/// UNIT_ASSASSIN_Z7V9V1_NAME. When the full capture is not an alias, the longest map key
		/// that is a prefix of the capture (next char '_') wins and the suffix is kept.
		/// </remarks>
		public static string Expand(string text, IDictionary<string, string> map)
		{
			if (string.IsNullOrEmpty(text) || map == null || map.Count == 0)
			{
				return text;
			}

			return AliasRefPattern.Replace(text, match =>
			{
				string captured = match.Groups[1].Value;
				if (map.TryGetValue(captured, out string exact) && !string.IsNullOrEmpty(exact))
				{
					return exact;
				}

				string bestKey = null;
				foreach (KeyValuePair<string, string> pair in map)
				{
					if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value))
					{
						continue;
					}

					if (captured.Length > pair.Key.Length
						&& captured.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)
						&& captured[pair.Key.Length] == '_')
					{
						if (bestKey == null || pair.Key.Length > bestKey.Length)
						{
							bestKey = pair.Key;
						}
					}
				}

				if (bestKey != null && map.TryGetValue(bestKey, out string value) && !string.IsNullOrEmpty(value))
				{
					return value + captured.Substring(bestKey.Length);
				}

				return match.Value;
			});
		}

		/// <summary>Resolves a <c>$alias</c> or returns the input when it is already a unique id.</summary>
		public static string ResolveRef(string folder, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return value;
			}

			string trimmed = value.Trim();
			if (trimmed.Length > 1 && trimmed[0] == '$')
			{
				Dictionary<string, string> map = Load(folder);
				if (map.TryGetValue(trimmed.Substring(1), out string resolved) && !string.IsNullOrEmpty(resolved))
				{
					return resolved;
				}
			}

			return trimmed;
		}

		/// <summary>First alias key that maps to uniqueId, or null.</summary>
		public static string FindKeyForId(IDictionary<string, string> map, string uniqueId)
		{
			if (map == null || string.IsNullOrEmpty(uniqueId))
			{
				return null;
			}

			foreach (KeyValuePair<string, string> pair in map)
			{
				if (string.Equals(pair.Value, uniqueId, StringComparison.OrdinalIgnoreCase))
				{
					return pair.Key;
				}
			}

			return null;
		}

		/// <summary><c>$key</c> when uniqueId has an alias in this folder; otherwise the unique id.</summary>
		public static string ToAuthoredRef(string folder, string uniqueId)
		{
			if (string.IsNullOrEmpty(uniqueId))
			{
				return uniqueId;
			}

			if (uniqueId[0] == '$')
			{
				return uniqueId;
			}

			string key = FindKeyForId(Load(folder), uniqueId);
			return key != null ? "$" + key : uniqueId;
		}

		/// <summary>Writes alias key → uniqueId, keeping any other rows already in the file.</summary>
		public static void SeedSelf(string folder, string alias, string uniqueId)
		{
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(uniqueId))
			{
				return;
			}

			Dictionary<string, string> map = Load(folder);
			map[alias] = uniqueId;
			Save(folder, map);
		}

		/// <summary>Copies the source folder's alias for uniqueId into the destination map.</summary>
		/// <returns>The alias key written, or null when uniqueId is empty.</returns>
		public static string CopyAlias(string sourceFolder, string destFolder, string uniqueId, string fallbackKey)
		{
			if (string.IsNullOrEmpty(destFolder) || string.IsNullOrEmpty(uniqueId))
			{
				return null;
			}

			string key = FindKeyForId(Load(sourceFolder), uniqueId);
			if (string.IsNullOrEmpty(key))
			{
				key = fallbackKey;
			}

			if (string.IsNullOrEmpty(key))
			{
				return null;
			}

			Dictionary<string, string> dest = Load(destFolder);
			dest[key] = uniqueId;
			Save(destFolder, dest);
			return key;
		}
	}
}
