# LokrCharacterLoader — Overview

The core "add/override custom characters, heroes, and abilities" facade.
Owns every Harmony patch that touches base-game character content
(portraits, sounds, ability icons, unit/hero/ability definitions,
ExoSkeleton rigs, custom sprite FX / projectiles, localization, Lua
scripts) and exposes one static class,
`CharacterAPI`, as the sanctioned extension point — so other plugins never
need to re-patch the same base-game methods themselves. `LokrCharacterLab`
(the Animator workstation, see `../../LokrLab/docs/character/overview.md`)
depends on this plugin specifically for `CustomRigLoader` and, eventually,
the same hero/unit-definition data this system manages.

## In this folder

- [`layout.md`](layout.md) — file structure
- [`architecture.md`](architecture.md) — the resolver-chain pattern, `RegisterDefaults()` wiring, and full-method-replacement philosophy
- [`character-api.md`](character-api.md) — `CharacterAPI.cs` in full detail, the most load-bearing file in this plugin
- [`custom-rig-loader.md`](custom-rig-loader.md) — `CustomRigs/CustomRigLoader.cs`
- [`patches.md`](patches.md) — every file under `Patches/`, one section each
- [`conventions.md`](conventions.md) — naming, duplicate-ID handling, patch style
- [`cross-references.md`](cross-references.md) — base-game constraints and documented past bugs/fixes

## Key architectural features

- **Resolver-chain pattern**: Core extension mechanism used throughout `CharacterAPI` — higher-priority resolvers tried first, first non-null result wins. See [`architecture.md`](architecture.md) for details. Applied to portraits (6 UI slots), sounds, state-visual-effects, custom rigs (`CustomRigLoader`), and Phase 5 sprite FXMega / projectiles (`CustomFxLoader`).
- **Live reload**: `CharacterAPI.ReloadLabContent(scope)` re-reads Lab-authored content from disk via `ContentReloader` and refreshes loaded heroes via `MetagameHeroReloader`. See [`../../docs/roadmaps/started/live-reload.md`](../../docs/roadmaps/started/live-reload.md).
- **Full-method-replacement patches**: Where a base-game method is too tightly coupled with private fields/closures to safely Harmony Prefix/Postfix around, patches reimplement the method fully and skip the original. See [`architecture.md`](architecture.md) and [`patches.md`](patches.md) for which patterns use which approach.
- **CharacterAPI events**: Plugins extending character content (e.g. adding new heroes) don't patch the same methods twice — instead, they subscribe to `CharacterAPI` events (`BuildingHeroRoster`, `BuildingUnitDefinitions`, `BuildingAbilities`, `ContributingLocalization`, etc.). This plugin registers its own file-convention logic as the default subscriber, and every resolver chain is seeded before patches fire.
- **Dogfooded design**: `CharacterAPI`'s own default file-based content source (scanning `Mods/*/` folders) is registered through the exact same public resolver/event API any other plugin would use — there is no privileged "built-in" path. This ensures the extension points are genuinely usable.

## Plugin metadata

`LokrCharacterLoaderPlugin.cs`: `Guid = "com.lokrmodding.characterloader"`,
`Name = "LoKR Character Loader"`, `Version = "1.1.17"`,
`[BepInDependency(LokrModAPIPlugin.Guid)]`. `Awake()` calls
`DefaultContentSources.RegisterAll()` **before** Harmony patches, so
every default content source is wired up before any patched game method
can fire — see [`architecture.md`](architecture.md). Each patch class is
applied on its own so one bad signature cannot abort the rest.
