using System;
using System.Collections.Generic;
using System.Linq;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Model.Game.Units.Abilities;
using Ironhide.Localization;
using UnityEngine;

namespace LokrCharacterLoader
{
	/// <summary>Extension point for other plugins to add or override character content via resolver chains and events.</summary>
	/// <remarks>
	/// Public extension-point facade for other plugins -- see docs/modapi-plan.md §5.
	/// LokrCharacterLoader owns the actual Harmony patches on the base game; other plugins that
	/// want to add or override character content call into CharacterAPI instead of patching those
	/// same methods themselves. LokrCharacterLoader's own file-convention logic
	/// (Mods/*/Portraits, Mods/*/Sounds, etc.) is registered through this exact same surface as an
	/// ordinary, lowest-priority participant -- it gets no special treatment.
	/// </remarks>
	public static class CharacterAPI
	{
		/// <summary>Which runtime caches <see cref="ReloadLabContent"/> should rebuild.</summary>
		[Flags]
		public enum ReloadScope
		{
			None = 0,
			UnitDefinitions = 1 << 0,
			HeroRoster = 1 << 1,
			Localization = 1 << 2,
			Rigs = 1 << 3,
			Abilities = 1 << 4,
			/// <summary>Typical Character Lab reload: units, roster, localization, rigs.</summary>
			LabCharacterDefaults = UnitDefinitions | HeroRoster | Localization | Rigs,
			/// <summary>Custom sprite FXMega / projectile prefabs built from disk folders.</summary>
			Visuals = 1 << 5,
			All = LabCharacterDefaults | Abilities | Visuals,
		}

		/// <summary>Outcome of a <see cref="ReloadLabContent"/> call.</summary>
		public struct ReloadResult
		{
			public bool Success;
			public ReloadScope Completed;
			public string ErrorMessage;
			public double ElapsedMs;
		}

		/// <summary>
		/// Re-reads mod/Lab character content from disk into runtime caches.
		/// Does not reset save-game progress. See docs/roadmaps/started/live-reload.md.
		/// </summary>
		public static ReloadResult ReloadLabContent(ReloadScope scope = ReloadScope.LabCharacterDefaults) =>
			ContentReloader.Reload(scope);

		/// <summary>Generic priority-ordered resolver chain: higher priority tried first, first non-null result wins.</summary>
		/// <remarks>
		/// Ties are broken by registration order (first-registered first). Shared by all three
		/// resolver kinds in this class instead of triplicating the sort/lookup logic (§5.1).
		/// </remarks>
		private sealed class ResolverChain<TResolver> where TResolver : class
		{
			private readonly List<(int priority, int order, TResolver resolver)> entries = new List<(int, int, TResolver)>();

			/// <summary>Adds a resolver at the given priority, re-sorting the chain.</summary>
			public void Register(TResolver resolver, int priority)
			{
				entries.Add((priority, registrationCounter++, resolver));
				entries.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : a.order.CompareTo(b.order));
			}

			/// <summary>Invokes each resolver in priority order and returns the first non-null result, or null if none match.</summary>
			public TResult Resolve<TResult>(Func<TResolver, TResult> invoke) where TResult : class
			{
				for (int i = 0; i < entries.Count; i++)
				{
					TResult result = invoke(entries[i].resolver);
					if (result != null)
					{
						return result;
					}
				}
				return null;
			}
		}

		private static int registrationCounter;

		/// <summary>Resolves a hero's portrait sprite for a given slot ("MINI"/"BIG"/"BANNER"/"MAP"/"MAPMINI"/"CHALLENGE").</summary>
		public delegate Sprite PortraitResolver(string heroId, string slot);
		/// <summary>Resolves an ability icon sprite by name.</summary>
		public delegate Sprite AbilityIconResolver(string iconName);
		/// <summary>Resolves a combat/UI sound clip for a unit/event.</summary>
		public delegate AudioClip SoundResolver(string unitId, string eventName);
		/// <summary>Resolves a full-rig ExoSkeleton replacement for the given metaExo name.</summary>
		/// <remarks>metaExoName is UnitDefinition.metaExo -- the same string vanilla passes to AssetBundleManager.LoadAsset&lt;ExoSkeletonDataAsset&gt;("units", metaExo).</remarks>
		public delegate ExoSkeletonDataAsset ExoSkeletonResolver(string metaExoName);
		/// <summary>Resolves a runtime-built FXMega prefab by the name Ability KV stores in CastFXId / EffectName / ModifierFXName.</summary>
		public delegate GameObject FxMegaResolver(string fxName);
		/// <summary>Resolves a runtime-built projectile prefab by the name Ability KV stores in TrackingProjectile Model.</summary>
		public delegate GameObject ProjectileResolver(string modelName);

		private static readonly ResolverChain<PortraitResolver> portraitResolvers = new ResolverChain<PortraitResolver>();
		private static readonly ResolverChain<AbilityIconResolver> abilityIconResolvers = new ResolverChain<AbilityIconResolver>();
		private static readonly ResolverChain<SoundResolver> soundResolvers = new ResolverChain<SoundResolver>();
		private static readonly ResolverChain<ExoSkeletonResolver> exoSkeletonResolvers = new ResolverChain<ExoSkeletonResolver>();
		private static readonly ResolverChain<FxMegaResolver> fxMegaResolvers = new ResolverChain<FxMegaResolver>();
		private static readonly ResolverChain<ProjectileResolver> projectileResolvers = new ResolverChain<ProjectileResolver>();

		/// <summary>Registers a portrait resolver for one of the six portrait slots; higher priority is tried first.</summary>
		/// <remarks>slot is one of "MINI"/"BIG"/"BANNER"/"MAP"/"MAPMINI"/"CHALLENGE" (§5.3).</remarks>
		public static void RegisterPortraitResolver(PortraitResolver resolver, int priority = 0) =>
			portraitResolvers.Register(resolver, priority);

		/// <summary>Registers an ability icon resolver; higher priority is tried first.</summary>
		/// <remarks>Not one of the six portrait slots (an ability icon isn't tied to a hero) but the same override-chain shape, so it gets the same treatment.</remarks>
		public static void RegisterAbilityIconResolver(AbilityIconResolver resolver, int priority = 0) =>
			abilityIconResolvers.Register(resolver, priority);

		/// <summary>Registers a combat/UI sound resolver; higher priority is tried first.</summary>
		/// <remarks>unitId/eventName cover combat sound events, "promote", and "selectHero" (§5.4).</remarks>
		public static void RegisterSoundResolver(SoundResolver resolver, int priority = 0) =>
			soundResolvers.Register(resolver, priority);

		/// <summary>Registers a full-rig ExoSkeleton replacement resolver; higher priority is tried first.</summary>
		/// <remarks>See Patches/HeroExoSkeletonPatches.cs for the concrete replacement of a hero's ExoSkeletonDataAsset.</remarks>
		public static void RegisterExoSkeletonResolver(ExoSkeletonResolver resolver, int priority = 0) =>
			exoSkeletonResolvers.Register(resolver, priority);

		/// <summary>Registers an FXMega prefab resolver; higher priority is tried first.</summary>
		/// <remarks>The returned GameObject must have FXMegaComponent (and FXMegaController for Hit / modifier attach). File-convention sprite FX register through this same chain.</remarks>
		public static void RegisterFxMegaResolver(FxMegaResolver resolver, int priority = 0) =>
			fxMegaResolvers.Register(resolver, priority);

		/// <summary>Registers a projectile prefab resolver; higher priority is tried first.</summary>
		/// <remarks>The returned GameObject must have ProjectileViewComponent plus ProjectileMovement / ProjectileVisibilityControl implementations.</remarks>
		public static void RegisterProjectileResolver(ProjectileResolver resolver, int priority = 0) =>
			projectileResolvers.Register(resolver, priority);

		/// <summary>Resolves a hero's portrait for a slot via the registered resolver chain.</summary>
		internal static Sprite ResolvePortrait(string heroId, string slot) =>
			portraitResolvers.Resolve(r => r(heroId, slot));

		/// <summary>Resolves an ability icon via the registered resolver chain.</summary>
		internal static Sprite ResolveAbilityIcon(string iconName) =>
			abilityIconResolvers.Resolve(r => r(iconName));

		/// <summary>Resolves a full-rig ExoSkeleton replacement via the registered resolver chain.</summary>
		internal static ExoSkeletonDataAsset ResolveExoSkeleton(string metaExoName) =>
			exoSkeletonResolvers.Resolve(r => r(metaExoName));

		/// <summary>Resolves the first non-null AudioClip from the registered resolver chain, or null if none match.</summary>
		/// <remarks>Playback itself (once a clip is resolved) goes through ModAPI.Audio.PlayClip -- see SoundPatches.cs -- so code-based plugins don't need any notion of a "mod folder" to participate, they just need to produce a clip however they like.</remarks>
		internal static AudioClip ResolveSound(string unitId, string eventName) =>
			soundResolvers.Resolve(r => r(unitId, eventName));

		/// <summary>Resolves a custom FXMega prefab via the registered resolver chain.</summary>
		internal static GameObject ResolveFxMega(string fxName) =>
			string.IsNullOrEmpty(fxName) ? null : fxMegaResolvers.Resolve(r => r(fxName));

		/// <summary>Resolves a custom projectile prefab via the registered resolver chain.</summary>
		internal static GameObject ResolveProjectile(string modelName) =>
			string.IsNullOrEmpty(modelName) ? null : projectileResolvers.Resolve(r => r(modelName));

		/// <summary>Folder-convention FXMega names currently built from disk (not vanilla FXMegaList).</summary>
		public static IReadOnlyCollection<string> KnownCustomFxNames => CustomRigs.CustomFxLoader.FxNames;

		/// <summary>Folder-convention projectile Model names currently built from disk.</summary>
		public static IReadOnlyCollection<string> KnownCustomProjectileNames => CustomRigs.CustomFxLoader.ProjectileNames;

		/// <summary>Clip names scraped from Character Lab <c>rig/rig.json</c> files (strings only; no Character Lab DLL).</summary>
		public static IReadOnlyCollection<string> KnownCustomClipNames => CustomRigs.CustomFxLoader.ClipNames;

		/// <summary>Re-reads custom FX / projectile folders and Character Lab clip names, then injects into FXManager if it has already Preloaded.</summary>
		public static void RefreshCustomVisuals() => CustomRigs.CustomFxLoader.Refresh();

		/// <summary>Collects legend/companion JSON fragments to merge into the hero roster (§5.2).</summary>
		public sealed class RosterBuilder
		{
			/// <summary>Legend JSON fragments collected via AddLegend, to be merged into the hero roster.</summary>
			internal readonly List<string> LegendFragments = new List<string>();
			/// <summary>Companion JSON fragments collected via AddCompanion, to be merged into the hero roster.</summary>
			internal readonly List<string> CompanionFragments = new List<string>();

			/// <summary>Adds a legend's JSON fragment to the roster.</summary>
			public void AddLegend(string json) => LegendFragments.Add(json);
			/// <summary>Adds a companion's JSON fragment to the roster.</summary>
			public void AddCompanion(string json) => CompanionFragments.Add(json);
		}

		/// <summary>Collects hero/enemy KV-text fragments to merge into unit definitions.</summary>
		public sealed class UnitDefinitionsBuilder
		{
			/// <summary>Hero definition KV-text fragments collected via AddHeroDefinition, to be merged into unit definitions.</summary>
			internal readonly List<string> RLHeroesFragments = new List<string>();
			/// <summary>Enemy definition KV-text fragments collected via AddEnemyDefinition, to be merged into unit definitions.</summary>
			internal readonly List<string> EnemiesDefinitionsFragments = new List<string>();

			/// <summary>Adds a hero definition's KV-text fragment.</summary>
			public void AddHeroDefinition(string kvText) => RLHeroesFragments.Add(kvText);
			/// <summary>Adds an enemy definition's KV-text fragment.</summary>
			public void AddEnemyDefinition(string kvText) => EnemiesDefinitionsFragments.Add(kvText);
		}

		/// <summary>Collects ability KV-text fragments to merge into ability definitions.</summary>
		public sealed class AbilitiesBuilder
		{
			/// <summary>Ability KV-text fragments collected via AddAbilityText, to be merged into ability definitions.</summary>
			internal readonly List<string> KvTextFragments = new List<string>();
			/// <summary>Source labels parallel to KvTextFragments, used when a fragment fails to parse.</summary>
			internal readonly List<string> KvTextSources = new List<string>();

			/// <summary>Adds an ability's KV-text fragment.</summary>
			public void AddAbilityText(string kvText) => AddAbilityText(kvText, null);

			/// <summary>Adds an ability's KV-text fragment, tagging it with a source name for parse-error logs.</summary>
			public void AddAbilityText(string kvText, string sourceName)
			{
				KvTextFragments.Add(kvText);
				KvTextSources.Add(string.IsNullOrEmpty(sourceName)
					? "mod-ability-" + KvTextFragments.Count
					: sourceName);
			}
		}

		/// <summary>Fired while building the hero roster; add legend/companion JSON fragments via the builder.</summary>
		public static event Action<RosterBuilder> BuildingHeroRoster;
		/// <summary>Fired while building unit definitions; add hero/enemy KV-text fragments via the builder.</summary>
		public static event Action<UnitDefinitionsBuilder> BuildingUnitDefinitions;
		/// <summary>Fired once per UnitDefinition as it's parsed at game boot.</summary>
		public static event Action<UnitDefinition> UnitDefinitionLoaded;
		/// <summary>Fired while building abilities; add ability KV-text fragments via the builder.</summary>
		public static event Action<AbilitiesBuilder> BuildingAbilities;

		private static readonly List<Ability> registeredAbilities = new List<Ability>();

		/// <summary>Registers a programmatically-built Ability, applied alongside file/KV-text-based abilities.</summary>
		/// <remarks>For plugins that build Ability objects directly instead of hand-writing KV text. Applied once, the first time AbilitiesDefinitions.Load() runs.</remarks>
		public static void RegisterAbility(Ability ability) => registeredAbilities.Add(ability);

		/// <summary>Builds a RosterBuilder and fires BuildingHeroRoster.</summary>
		internal static RosterBuilder RaiseBuildingHeroRoster()
		{
			RosterBuilder builder = new RosterBuilder();
			BuildingHeroRoster?.Invoke(builder);
			return builder;
		}

		/// <summary>Builds a UnitDefinitionsBuilder and fires BuildingUnitDefinitions.</summary>
		internal static UnitDefinitionsBuilder RaiseBuildingUnitDefinitions()
		{
			UnitDefinitionsBuilder builder = new UnitDefinitionsBuilder();
			BuildingUnitDefinitions?.Invoke(builder);
			return builder;
		}

		private static readonly Dictionary<string, UnitDefinition> knownUnitDefinitionsById = new Dictionary<string, UnitDefinition>();
		/// <summary>Every UnitDefinition seen via UnitDefinitionLoaded so far, keyed by id.</summary>
		/// <remarks>
		/// Accumulates for the process lifetime. UnitDefinitionLoaded fires at parse time
		/// (boot and reload) on add and on last-wins replace, not per-frame. Exposed publicly so tooling (e.g.
		/// LokrCharacterLab's rig editor picking a metaExo id from currently-loaded characters) can
		/// list every known unit without needing its own separate subscription/bookkeeping.
		/// </remarks>
		public static IReadOnlyDictionary<string, UnitDefinition> KnownUnitDefinitions => knownUnitDefinitionsById;

		/// <summary>Records the UnitDefinition by id and fires UnitDefinitionLoaded.</summary>
		internal static void RaiseUnitDefinitionLoaded(UnitDefinition definition)
		{
			if (definition != null && !string.IsNullOrEmpty(definition.id))
			{
				knownUnitDefinitionsById[definition.id] = definition;
			}
			UnitDefinitionLoaded?.Invoke(definition);
		}

		/// <summary>Clears the KnownUnitDefinitions index before a unit-definition reload.</summary>
		internal static void ClearKnownUnitDefinitions()
		{
			knownUnitDefinitionsById.Clear();
		}

		/// <summary>Builds an AbilitiesBuilder and fires BuildingAbilities.</summary>
		internal static AbilitiesBuilder RaiseBuildingAbilities()
		{
			AbilitiesBuilder builder = new AbilitiesBuilder();
			BuildingAbilities?.Invoke(builder);
			return builder;
		}

		/// <summary>Every Ability registered via RegisterAbility so far.</summary>
		internal static IReadOnlyList<Ability> GetRegisteredAbilities() => registeredAbilities;

		/// <summary>Fired to collect localization strings for a language; return null/empty if you have nothing for it.</summary>
		public static event Func<LocalizationManager.LanguageCode, IDictionary<string, string>> ContributingLocalization;

		/// <summary>Fired to resolve Lua script source by name; return null to fall through to the next contributor.</summary>
		public static event Func<string, string> ResolvingScript;

		/// <summary>Collects every contributor's localization strings for a language.</summary>
		internal static IEnumerable<IDictionary<string, string>> RaiseContributingLocalization(LocalizationManager.LanguageCode language)
		{
			if (ContributingLocalization == null)
			{
				yield break;
			}
			foreach (Func<LocalizationManager.LanguageCode, IDictionary<string, string>> handler in ContributingLocalization.GetInvocationList())
			{
				IDictionary<string, string> result = handler(language);
				if (result != null)
				{
					yield return result;
				}
			}
		}

		/// <summary>Asks each contributor in turn for scriptName's source and returns the first non-null result, or null.</summary>
		internal static string RaiseResolvingScript(string scriptName)
		{
			if (ResolvingScript == null)
			{
				return null;
			}
			foreach (Func<string, string> handler in ResolvingScript.GetInvocationList())
			{
				string result = handler(scriptName);
				if (result != null)
				{
					return result;
				}
			}
			return null;
		}

		private static readonly Dictionary<string, List<Action<Unit, bool>>> stateVisualEffects = new Dictionary<string, List<Action<Unit, bool>>>();

		/// <summary>Registers a visual-effect callback fired whenever a named unit state is entered or exited.</summary>
		/// <remarks>action(unit, isEntering) -- isEntering=true when the state was just applied, false when a per-turn check determines it should no longer be considered active.</remarks>
		public static void RegisterStateVisualEffect(string stateName, Action<Unit, bool> action)
		{
			if (!stateVisualEffects.TryGetValue(stateName, out List<Action<Unit, bool>> list))
			{
				list = new List<Action<Unit, bool>>();
				stateVisualEffects[stateName] = list;
			}
			list.Add(action);
		}

		/// <summary>Fires every registered visual-effect callback for a state's entry/exit.</summary>
		internal static void RaiseStateVisualEffect(string stateName, Unit unit, bool isEntering)
		{
			if (stateVisualEffects.TryGetValue(stateName, out List<Action<Unit, bool>> list))
			{
				for (int i = 0; i < list.Count; i++)
				{
					list[i](unit, isEntering);
				}
			}
		}
	}
}
