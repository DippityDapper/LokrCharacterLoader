# LokrCharacterLoader — Conventions

- **Two patch files live in the game's own namespace**
  (`UnityDefinitionsParserPatches` in
  `Ironhide.Legends.Model.Game.Units`, `AbilitiesDefinitionsPatches` in
  `...Units.Abilities`) purely so their large full-method-copy bodies can
  reference internal game types without extra qualification — both are
  still `internal` and targeted only via Harmony attributes, so this has
  no functional effect (confirmed in comments in both files). Every other
  patch file lives in `LokrCharacterLoader.Patches`.
- **`RegisterDefaults()` convention**: every patch class owning a
  `CharacterAPI` extension point exposes `internal static void
  RegisterDefaults()`, wiring this plugin's own file-convention scanning
  into that same extension point at default priority — all invoked once
  from `DefaultContentSources.RegisterAll()`. See
  [`architecture.md`](architecture.md).
- **Full-method-replacement vs. narrow pre/postfix**: used where the
  original method is too intertwined with private fields/closures to
  patch narrowly (`AbilitiesDefinitions.Load`, `HeroRosterManager.Init`,
  `DialogViewManagerMap.RefreshText`, `IronhideScriptLoader.LoadScripts`,
  `LocalizationManager.Load`, `PartyTokenComponent.UpdateHeroes`,
  `PartyTokenComponent.SetFlagVisibility`,
  `UIHeroManage.PromoteHero`, `RewardViewComponent.SetTargetPortrait`,
  `UnityDefinitionsParser.LoadData`/`ParseText`) — these reimplement the
  full body and return `false` to skip the original, using `Traverse`/
  reflected `MethodInfo` for private-member access. Narrow pre/postfixes
  are used everywhere the original can safely keep running. See
  [`patches.md`](patches.md) for which is which.
- **Resolver-chain semantics are the core convention** for sprite/audio/
  rig / FXMega / projectile lookups: highest priority, first-registered-on-tie, first
  non-null-result-wins. Deliberately different from
  `ContributingLocalization`, which merges all non-null contributions
  instead — see [`character-api.md`](character-api.md).
- **Duplicate-ID handling is intentionally inconsistent, matching each
  subsystem's own semantics** — not a bug, a deliberate per-system
  choice:
  - Ability IDs override (code-registered wins over file-based).
  - Custom rig IDs are first-found-wins with a warning.
  - Hero roster ids last-wins in `HeroRosterManagerPatches` (same id
    replaces the JSON object). `CharacterLabContentLoader` still skips
    a second Lab folder with the same `character.json` id so two copies
    do not both contribute fragments.
  - Unit definitions last-wins across files and within a file (Lab/mod
    fragment replaces vanilla). UniqueId index last-wins for level-1.
  - Lua script names keep the first loaded (override happens earlier, via
    content substitution, not a second insertion).
  - Localization keys are last-contributor-wins.
