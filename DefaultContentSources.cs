using LokrCharacterLoader.CustomRigs;
using LokrCharacterLoader.Patches;

namespace LokrCharacterLoader
{
	/// <summary>Wires this plugin's own file-convention content sources through CharacterAPI's public surface.</summary>
	/// <remarks>
	/// Registers LokrCharacterLoader's own Mods/*/&lt;Category&gt; file-convention scanning as
	/// ordinary, lowest-priority participants in CharacterAPI's resolver chains/events -- see
	/// docs/modapi-plan.md §5.1. Each patch class that owns a CharacterAPI extension point exposes
	/// its own internal RegisterDefaults(); this just calls all of them from one place, once, in
	/// LokrCharacterLoaderPlugin.Awake() before Harmony patches.
	///
	/// UnityDefinitionsParserPatches and AbilitiesDefinitionsPatches live in the game's own
	/// namespaces rather than LokrCharacterLoader.Patches (see those files for why), so they're
	/// referenced by full name here instead of via the `using` above.
	/// </remarks>
	internal static class DefaultContentSources
	{
		/// <summary>Calls RegisterDefaults() on every content-source patch class, once at plugin startup.</summary>
		internal static void RegisterAll()
		{
			PortraitPatches.RegisterDefaults();
			SoundPatches.RegisterDefaults();
			HeroRosterManagerPatches.RegisterDefaults();
			Ironhide.Legends.Model.Game.Units.UnityDefinitionsParserPatches.RegisterDefaults();
			Ironhide.Legends.Model.Game.Units.Abilities.AbilitiesDefinitionsPatches.RegisterDefaults();
			LocalizationManagerPatches.RegisterDefaults();
			IronhideScriptLoaderPatches.RegisterDefaults();
			InvisibilityPatches.RegisterDefaults();
			CustomRigLoader.RegisterDefaults();
			CharacterLabContentLoader.RegisterDefaults();
			AbilityLabContentLoader.RegisterDefaults();
			CustomFxLoader.RegisterDefaults();
		}
	}
}
