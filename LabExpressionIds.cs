using System.Text.RegularExpressions;

namespace LokrCharacterLoader
{
	/// <summary>Maps Lab generated folder ids onto keys the ability expression grammar and GetDefinition can both use.</summary>
	/// <remarks>
	/// GenerateNewCharacterId is 18-20 digits. SpawnUnit UnitName is a #word, and word must start
	/// with a letter, so #1529… never evaluates to the block key. Ability literals get a leading c;
	/// the KV block key stays the folder id so sandbox/roster ContainsKey still hits. See
	/// docs/issues/resolved/sandbox-summon-missing-unit-view.md.
	/// </remarks>
	internal static class LabExpressionIds
	{
		private const string Prefix = "c";
		private static readonly Regex GeneratedId = new Regex(@"^\d{18,20}$");
		private static readonly Regex GeneratedIdWithLevel = new Regex(@"^(\d{18,20})(_Lvl\d+)?$");
		private static readonly Regex PrefixedBlockKeyLine = new Regex(@"(?m)^(\s*)""c(\d{18,20}(?:_Lvl\d+)?)""\s*$");
		private static readonly Regex PrefixedInheritanceValue = new Regex(@"(""(?:InheritsFrom|nextLevelArchetype)""\s+"")c(\d{18,20}(?:_Lvl\d+)?)""");
		private static readonly Regex AbilityHashId = new Regex(@"#(\d{18,20})\b");
		private static readonly Regex BareUnitName = new Regex("(\"UnitName\"\\s+\")(?!#)([A-Za-z][A-Za-z0-9_]*)(\")");

		/// <summary>True when id is an 18-20 digit Lab folder id, ignoring a leading c or a _LvlN suffix.</summary>
		internal static bool LooksLikeGeneratedId(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return false;
			}
			if (id[0] == Prefix[0] && id.Length > 1)
			{
				id = id.Substring(1);
			}
			Match match = GeneratedIdWithLevel.Match(id);
			return match.Success && GeneratedId.IsMatch(match.Groups[1].Value);
		}

		/// <summary>Block key / #word form: c plus the generated digits, or the input when it already has a leading letter.</summary>
		internal static string ToExpressionSafeKey(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return id;
			}
			if (id[0] == Prefix[0] && LooksLikeGeneratedId(id))
			{
				return id;
			}
			Match match = GeneratedIdWithLevel.Match(id);
			if (!match.Success)
			{
				return id;
			}
			return Prefix + match.Groups[1].Value + match.Groups[2].Value;
		}

		/// <summary>Folder-id form: strips a leading c from a generated key so GetDefinition can try both spellings.</summary>
		internal static string ToFolderId(string id)
		{
			if (string.IsNullOrEmpty(id) || id[0] != Prefix[0] || !LooksLikeGeneratedId(id))
			{
				return id;
			}
			return id.Substring(1);
		}

		/// <summary>Strips a leftover leading <c>c</c> from generated block keys and InheritsFrom / nextLevelArchetype values.</summary>
		/// <remarks>
		/// Sandbox and roster lookup use the folder id as the Definitions dictionary key. The
		/// 0.12.4 writer briefly prefixed those keys; this puts already-written files back. Ability
		/// #UnitName literals still use the c form via RewriteAbilityText. UniqueId / MetaExo / Name
		/// are left alone.
		/// </remarks>
		internal static string NormalizeDefinitionKeys(string kvText)
		{
			if (string.IsNullOrEmpty(kvText))
			{
				return kvText;
			}
			string rewritten = PrefixedBlockKeyLine.Replace(kvText, "$1\"$2\"");
			return PrefixedInheritanceValue.Replace(rewritten, "$1$2\"");
		}

		/// <summary>Makes SpawnUnit UnitName a legal <c>#word</c>: <c>#1529…</c> becomes <c>#c1529…</c>, and a bare identifier gets a leading <c>#</c>.</summary>
		/// <remarks>
		/// Lab authors UnitName as <c>$alias</c>. Expand yields the unique id with no hash.
		/// A bare <c>slug_token</c> is parsed as a function name
		/// (<c>Function onagro_mine_6htjnq is not defined</c>). See
		/// docs/issues/resolved/alias-unitname-parsed-as-function.md.
		/// </remarks>
		internal static string RewriteAbilityText(string kvText)
		{
			if (string.IsNullOrEmpty(kvText))
			{
				return kvText;
			}

			string hashedDigits = AbilityHashId.Replace(kvText, "#" + Prefix + "$1");
			return BareUnitName.Replace(hashedDigits, "$1#$2$3");
		}
	}
}
