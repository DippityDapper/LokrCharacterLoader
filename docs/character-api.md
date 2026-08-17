# LokrCharacterLoader — `CharacterAPI.cs`

Static class, no instance state beyond its own registries. See
[`architecture.md`](architecture.md) for the resolver-chain pattern this
whole file is built around.

## Sprite / audio / rig resolvers

```csharp
public delegate Sprite PortraitResolver(string heroId, string slot);
public delegate Sprite AbilityIconResolver(string iconName);
public delegate AudioClip SoundResolver(string unitId, string eventName);
public delegate ExoSkeletonDataAsset ExoSkeletonResolver(string metaExoName);
public delegate GameObject FxMegaResolver(string fxName);
public delegate GameObject ProjectileResolver(string modelName);
```

| Method | Purpose | Fired from |
|---|---|---|
| `RegisterPortraitResolver(resolver, priority = 0)` | Resolve a hero portrait for one of six slots: `MINI`, `BIG`, `BANNER`, `MAP`, `MAPMINI`, `CHALLENGE`. | `PortraitPatches` (`DataHelper.LoadMiniPortrait`/`LoadBigPortrait`/`LoadHeroBanner`, `MapHeroBarPortraitComponent.SetHero`, `RewardViewComponent.SetTargetPortrait`, `UIBuffStoreItem.SetItem`), `DialogViewManagerMapPatches` (CHALLENGE, dialog/dice), `ExoSkeletonDataPatches` (MAPMINI, `ExoSkeletonData.ReplacePart`) |
| `RegisterAbilityIconResolver(resolver, priority = 0)` | Resolve a custom ability/skill icon by name (not hero-tied). | `PortraitPatches` (`DataHelper.LoadSkillIcon`) |
| `RegisterSoundResolver(resolver, priority = 0)` | Resolve an `AudioClip` for `(unitId, eventName)` — covers combat events plus `"promote"`/`"selectHero"`. | `SoundPatches` (`Unit.PlaySound`, `UIHeroManage.PromoteHero`, `UIHeroRoom.PlayHeroSelectedSound`) |
| `RegisterExoSkeletonResolver(resolver, priority = 0)` | Full-rig replacement: resolve an entire `ExoSkeletonDataAsset` given `metaExoName` (= `UnitDefinition.metaExo`). | `HeroExoSkeletonPatches` (metagame getter); `UnitViewExoSkeletonPatches` (combat `InstantiateUnitView`) |
| `RegisterFxMegaResolver(resolver, priority = 0)` | Runtime-built FXMega prefab (`FXMegaComponent` + `FXMegaController`) for Cast / Hit / modifier names. | `FxPatches` (`FXManager.Preload` postfix inject, `FXManager.LoadFXMega` prefix) |
| `RegisterProjectileResolver(resolver, priority = 0)` | Runtime-built projectile prefab for `TrackingProjectile` Model. | `FxPatches` (`DataHelper.LoadProjectile` prefix) |

Internal "fire" methods (patches only): `ResolvePortrait`,
`ResolveAbilityIcon`, `ResolveExoSkeleton`, `ResolveSound`,
`ResolveFxMega`, `ResolveProjectile`. Resolving a
sound clip is decoupled from playing it — actual playback goes through
`ModAPI.Audio.PlayClip` in the calling patch, so a resolver never needs
mod-folder awareness.

Public name lists for pickers (filled by `CustomFxLoader`):
`KnownCustomFxNames`, `KnownCustomProjectileNames`,
`KnownCustomClipNames` (clip names scraped from Character Lab
`rig/rig.json`, strings only). `RefreshCustomVisuals()` re-reads those
folders and re-injects into `FXManager` if it has already Preloaded.
`ResolveFxMega` / `ResolveProjectile` rebuild a single folder from disk
when the cached prefab is missing or was destroyed across a scene.

## Character/roster/ability content

```csharp
public sealed class RosterBuilder { void AddLegend(string json); void AddCompanion(string json); }
public sealed class UnitDefinitionsBuilder { void AddHeroDefinition(string kvText); void AddEnemyDefinition(string kvText); }
public sealed class AbilitiesBuilder { void AddAbilityText(string kvText); void AddAbilityText(string kvText, string sourceName); }

public static event Action<RosterBuilder> BuildingHeroRoster;
public static event Action<UnitDefinitionsBuilder> BuildingUnitDefinitions;
public static event Action<UnitDefinition> UnitDefinitionLoaded;
public static event Action<AbilitiesBuilder> BuildingAbilities;
public static void RegisterAbility(Ability ability);
```

- `BuildingHeroRoster` — subscribers append legend/companion JSON
  fragments; fired once by `HeroRosterManagerPatches`
  (`HeroRosterManager.Init`), inserted into the `"legends"`/`"companions"`
  JSON arrays of `Balance/HeroRoster/HeroRoster` before parsing.
- `BuildingUnitDefinitions` — subscribers append raw KV-text for new hero
  (`RLHeroes`) or enemy (`EnemiesDefinitions`) definitions. Lab-authored
  characters (heroes and `EnemySummon`) splice into **RLHeroes** via
  `CharacterLabContentLoader`, not `EnemiesDefinitions`. Fired once by
  `UnityDefinitionsParserPatches` (`UnityDefinitionsParser.LoadData`),
  text-spliced into the matching vanilla `TextAsset` (matched by filename
  substring) just before its closing `}`.
- `UnitDefinitionLoaded` — fired **per parsed `UnitDefinition`**, from
  inside `LoadData`'s per-definition loop — subscribers observe every
  loaded unit, including modded ones.
- `BuildingAbilities` — subscribers append raw KV-text ability fragments
  as named synthetic `TextAsset`s (`AddAbilityText(kvText, sourceName)`
  when the source path is known); fired once by `AbilitiesDefinitionsPatches`
  (`AbilitiesDefinitions.Load`).
- `RegisterAbility(Ability)` — for plugins building `Ability` objects
  programmatically. Applied via `GetRegisteredAbilities()` **after**
  KV-text abilities are parsed (on the first, load-once-guarded call to
  `Load()`), so a code-registered ability with the same `abilityId`
  overrides a file-based one.

## Localization & Lua scripts

```csharp
public static event Func<LocalizationManager.LanguageCode, IDictionary<string, string>> ContributingLocalization;
public static event Func<string, string> ResolvingScript;
```

- `ContributingLocalization` — **all** non-null results from **all**
  subscribers are merged (not first-wins): `RaiseContributingLocalization`
  yields every non-null dictionary in `GetInvocationList()` order; fired
  from `LocalizationManagerPatches` (`LocalizationManager.Load`), merged
  on top of the file-loaded base with later contributions overwriting
  earlier keys.
- `ResolvingScript` — first non-null result wins (first-wins, unlike
  localization); fired from `IronhideScriptLoaderPatches`
  (`IronhideScriptLoader.LoadScripts`).

## State-visual-effect hook

```csharp
public static void RegisterStateVisualEffect(string stateName, Action<Unit, bool> action);
```

Backed by `Dictionary<string, List<Action<Unit, bool>>>` — **all**
subscribers for a state fire (not first-wins). `action(unit, isEntering)`:
`true` when the state was just applied, `false` when a per-turn check
determines it's no longer active. Fired from `InvisibilityPatches` for the
`"INVISIBLE"` state (`Unit.AddModifier` on a false→true edge,
`Unit.TurnEnded` on a true→false edge) — `InvisibilityPatches` is also the
sole registrant of this hook in the current codebase. The Assassin
subscriber tints `"Graphic"` renderers under that unit's own view, not
every exo in the scene.

## Live reload (Character Lab)

```csharp
CharacterAPI.ReloadResult result =
    CharacterAPI.ReloadLabContent(CharacterAPI.ReloadScope.LabCharacterDefaults);
```

Re-reads Lab-authored files from disk into runtime caches (unit definitions,
roster config, localization, rig caches, abilities, custom visuals) and
refreshes live `Hero` instances.
Does **not** reset save progress. `ReloadScope.Visuals` rebuilds sprite
FXMega / projectile prefabs. See [`../../docs/roadmaps/started/live-reload.md`](../../docs/roadmaps/started/live-reload.md).

Character Lab calls this automatically on close when `AutoReloadOnLabClose`
is enabled in `com.lokrmodding.lab.cfg`, or manually via Home → **Reload in Game**.
