using System.Collections.Generic;
using Ironhide.Localization;

namespace LokrCharacterLoader.CustomRigs
{
	/// <summary>Maps each base-game LanguageCode to the localization_&lt;suffix&gt;.txt filename convention every per-folder content source in this file (Lab-authored characters, Ability Lab-authored abilities) writes one of per language.</summary>
	/// <remarks>Mirrors LocalizationManager.fileNamesMapping (Ironhide.Localization, decompiled source) -- duplicated here rather than reflected into, the same pragmatic choice this class's own callers already make for a single-field read. Shared by CharacterLabContentLoader and AbilityLabContentLoader now that both live in this assembly; LokrCharacterLab's own LocaleCodes.AllNonEnglish (a differently-shaped list, EN excluded, used for the write side) is a separate, pre-existing, not-yet-consolidated copy -- see docs/roadmaps/started/character-lab-loader-pre-redesign-audit.md's M-02 for that broader cleanup, out of scope for this file's own consolidation.</remarks>
	internal static class LocaleFileSuffixes
	{
		internal static readonly IReadOnlyDictionary<LocalizationManager.LanguageCode, string> Map = new Dictionary<LocalizationManager.LanguageCode, string>
		{
			{ LocalizationManager.LanguageCode.EN, "en_US" },
			{ LocalizationManager.LanguageCode.EN_GB, "en-gb" },
			{ LocalizationManager.LanguageCode.ES, "es" },
			{ LocalizationManager.LanguageCode.DE, "de" },
			{ LocalizationManager.LanguageCode.RU, "ru" },
			{ LocalizationManager.LanguageCode.FR, "fr" },
			{ LocalizationManager.LanguageCode.FR_CA, "fr-ca" },
			{ LocalizationManager.LanguageCode.IT, "it" },
			{ LocalizationManager.LanguageCode.TR, "tr" },
			{ LocalizationManager.LanguageCode.ZH_HANS, "zh-Hans" },
			{ LocalizationManager.LanguageCode.ZH_HANT, "zh-Hant" },
			{ LocalizationManager.LanguageCode.AR, "ar" },
			{ LocalizationManager.LanguageCode.PT, "pt" },
			{ LocalizationManager.LanguageCode.JA, "ja" },
			{ LocalizationManager.LanguageCode.NL, "nl" },
			{ LocalizationManager.LanguageCode.KO, "ko" },
		};
	}
}
