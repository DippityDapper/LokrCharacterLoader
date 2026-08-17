# LokrCharacterLoader — `Patches/`

One section per file. See [`architecture.md`](architecture.md) for the
full-method-replacement-vs-narrow-patch distinction referenced throughout.

## `AbilitiesDefinitionsPatches.cs`

Lives in namespace `Ironhide.Legends.Model.Game.Units.Abilities` (direct
access to `Ability`/`Modifier`/`AbilityParser`; no effect on Harmony
targeting — see [`conventions.md`](conventions.md)). Full-method-
replacement `[HarmonyPrefix]` on `AbilitiesDefinitions.Load`: load-once
guard (`abilities.Count > 0` early-out), loads all ability `TextAsset`s,
splices in `BuildingAbilities` KV-text fragments as named synthetic
assets (source path when the contributor passed one), parses everything
with `AbilityParser` (parse failures log the exception message and a
short preview, not stack-only), then applies every
`GetRegisteredAbilities()` entry directly (code-registered abilities load
after, so they can override file-based ones sharing an `abilityId`).
See [`ability-kv-parse-empty-filename.md`](../../docs/issues/resolved/ability-kv-parse-empty-filename.md)
for the unnamed-fragment parse error.
`RegisterDefaults()` (this class's own) wires the flat, legacy
`Mods/*/NewAbilities/*.txt` scan directly. The nested `Ability Lab`
layout (`Mods/*/Abilities/<id>/ability.txt`) is a separate registrant on
the same `BuildingAbilities` event — `CustomRigs/AbilityLabContentLoader.cs`
(moved here from `LokrAbilityLab` 2026-08-12 so that content loads without
`LokrAbilityLab` installed — see
`../../docs/roadmaps/started/editor-redesign.md` §2.7), the sibling of
`CustomRigs/CharacterLabContentLoader.cs`.

## `DialogViewManagerMapPatches.cs`

Full-method-replacement `[HarmonyPrefix]` on
`DialogViewManagerMap.RefreshText` (too intertwined with private state
for a smaller patch). Reimplements dialog balloon/response/challenge UI;
for each response's and dice-challenge participant's hero, resolves
`CharacterAPI.ResolvePortrait(heroId, "CHALLENGE")` and, if found,
replaces the rigged exoskeleton with a flat `Image` via
`PortraitPatches.ReplaceWithFlatImage`. Heavy `Traverse`/`AccessTools`
use for several private `DialogViewManager`/`DialogViewManagerMap` members.

## `ExoSkeletonDataPatches.cs`

`[HarmonyPrefix]` on `ExoSkeletonData.ReplacePart(string, string)`.
Handles the map party-token mini portrait (MAPMINI, historically
`_MAPMINI.png`). Reads the hero ID stashed by `ExoSkeletonModData` (set by
`PartyTokenComponentPatches` — `ReplacePart`'s own 2-arg signature has no
room for a hero-id parameter); if none, or if
`CharacterAPI.ResolvePortrait(id, "MAPMINI")` returns null, falls through
to vanilla. Otherwise applies the resolved texture via
`ExoSkeletonModData.ApplyTextureToRenderer` and manually rebuilds
replacement `Part` mesh data (quad vertices/UVs/triangles) for both the
target part and `"Asst_Shadow"`, then skips the original. If
`FindPartIndex` returns `-1` for either name, that write is skipped and
logged; `partsVersion`/`renderVersion` still bump and vanilla
`ReplacePart` still does not run (it would NRE on the same miss).
Tracked: [`find-part-index-unvalidated.md`](../../docs/issues/unresolved-tested/find-part-index-unvalidated.md).

## `ExoSkeletonModData.cs` — shared state, not a patch

Uses `LokrModAPI.ExtensionData.AttachedData<TOwner, TValue>` instead of
hand-rolled `ConditionalWeakTable` boilerplate. Two slots:
`AttachedData<ExoSkeletonData, string> HeroId` (written by
`PartyTokenComponentPatches`, read by `ExoSkeletonDataPatches`/
`ExoSkeletonUIGraphicPatches`) and
`AttachedData<ExoSkeletonUIGraphic, bool> TextureLoaded`. Also
`ApplyTextureToRenderer(renderer, texture)`: builds a
`MaterialPropertyBlock` (`_MainTex`, plus `_AlphaTex`/
`_AlphaSplitEnabled` if the sprite has an associated alpha-split
texture), applies it, and sets the renderer's private `loadedTexture`
field via `Traverse` — the same flag `ExoSkeletonRendererPatches` checks
to avoid redoing vanilla texture loading.

## `ExoSkeletonRendererPatches.cs`

`[HarmonyPrefix]` (does not skip the original) on
`ExoSkeletonRenderer.LateUpdate`. Handles the exoskeleton **body skin**
for world rendering — keyed by texture *name*, not hero+slot, so it
doesn't fit the portrait resolver chain and goes through `ModAPI.Files`/
`ModAPI.Assets` directly instead. No-ops if `loadedTexture` is already
true or required fields are null/empty; otherwise looks up
`Mods/*/Exoskeletons/<textureName>.png` and applies it via
`ExoSkeletonModData.ApplyTextureToRenderer` (which sets `loadedTexture`,
so vanilla's own texture-loading block naturally no-ops afterward — no
need to skip the original).

## `ExoSkeletonUIGraphicPatches.cs`

Same body-skin concept, for **UI** rendering. `[HarmonyPrefix]` on
`ExoSkeletonUIGraphic.OnPopulateMesh`. Checks its own
`ExoSkeletonModData.IsTextureLoaded` flag; requires a hero ID to already
be tagged (only applies to party-token UI instances); if found, directly
mutates `exoSkeletonData.asset.partSprites[0]` in place via
`ModAPI.Assets.LoadSprite`. Mutating from a Prefix (not Postfix) ensures
the very next read of `mainTexture` — including inside the original
method's own body, which still runs afterward — picks up the mod texture
immediately, avoiding a one-frame lag.

## `FxPatches.cs`

Narrow patches for Phase 5 custom visuals. `CustomFxLoader` (not this
file) scans `fx/<name>/` and `projectiles/<name>/` folders and builds
prefabs; this file only hooks the vanilla load paths.

- `FXManager.Preload` postfix — copies built FXMega prefabs into the
  public `fxMegaPrefabs` dictionary after the scenario-bundle fill.
  If the cache is empty (prefabs lost across a scene), it re-scans
  disk first.
- `FXManager.LoadFXMega` prefix — returns `CharacterAPI.ResolveFxMega`
  so a name added after Preload still resolves instead of throwing.
  A miss rebuilds that one folder from disk.
- `DataHelper.LoadProjectile` prefix — same for projectile Model names
  (`ResolveProjectile`). Projectiles are not FXMega names. Custom
  projectiles clone `SimpleArrowProjectile` and swap the sprite so
  `Projectile.Update` has a real view.
- `Projectile.Update` prefix — if `view` / `projectileTransform` is
  missing, calls `DestinationReached` instead of null-reffing every
  frame (that freeze is what a thin custom projectile used to cause).

`RegisterDefaults()` lives on `CustomFxLoader`, called from
`DefaultContentSources`. `ReloadScope.Visuals` calls
`CustomFxLoader.Refresh()`.

## `HeroExoSkeletonPatches.cs`

`[HarmonyPrefix]` on the `Hero.exoSkeletonDataAsset` getter, using
Harmony's `____exoSkeletonDataAsset` field injection. This seeds the
metagame / roster / UI rig. Combat views use `UnitViewExoSkeletonPatches`
(`UnitViewManager.InstantiateUnitView`) so a modded hero can use a fully
custom rig in combat, not just the Lab test scene. If the private backing field is
currently null, seeds it via
`CharacterAPI.ResolveExoSkeleton(unitDefinition.metaExo)` before the
original getter runs. Deliberately seed-then-let-original-run rather than
skip-and-reimplement: the getter already lazily caches into that field,
so seeding it lets the original getter's own cache-check see it already
populated — same caching semantics as vanilla, and `ResolveExoSkeleton`
(which rebuilds an atlas texture) never re-runs on every property access.
If `unitDefinition` is null the prefix returns null and skips the original
(vanilla would NRE on `metaExo`). A null `metaExo` with a present
definition still falls through to vanilla `LoadAsset`.
Tracked: [`exo-skeleton-null-unitdefinition.md`](../../docs/issues/unresolved-tested/exo-skeleton-null-unitdefinition.md).

## `UnitViewExoSkeletonPatches.cs`

Postfix on `UnitViewManager.InstantiateUnitView`. After the combat view
exists, resolves `CharacterAPI.ResolveExoSkeleton` and applies it so
custom rigs appear in fight, not only on the hero-room getter.
Returns immediately when `unit`, `unit.unitDefinition`, or the instantiated
view is null; a null `metaExo` still goes through `ResolveExoSkeleton`
(already returns null) and does not skip `UpdateAsset` when a custom
asset is present.
Tracked: [`exo-skeleton-null-unitdefinition.md`](../../docs/issues/unresolved-tested/exo-skeleton-null-unitdefinition.md).

## `HeroRosterManagerPatches.cs`

Full-method-replacement `[HarmonyPrefix]` on `HeroRosterManager.Init`.
Loads `Balance/HeroRoster/HeroRoster`, raises `BuildingHeroRoster`, string-
splices any contributed legend/companion JSON fragments into the
`"legends"`/`"companions"` arrays (locating marker strings and matching
brackets). Same `id` last-wins (Lab/mod row replaces the vanilla object);
new ids append. Replace-in-place avoids a second Gerald object that would
throw in `HeroRosterConfig.Parse` and leave metagame stuck instantiating.
See [vanilla-character-edit.md](../../docs/roadmaps/started/vanilla-character-edit.md)
Phase 2. Parses the result, rebuilds `XP_BRACKETS` as vanilla does.
`RegisterDefaults()`: files under `Mods/*/HeroRoster` whose name contains
`"legend_"` become legend fragments, `"companion_"` become companion
fragments (independent checks, not mutually exclusive).

## `InvisibilityPatches.cs`

Prefix+postfix pairs on `Unit.AddModifier` and `Unit.TurnEnded` capture
whether `INVISIBLE` was already on (`__state`) and raise
`RaiseStateVisualEffect` only on a true edge: false→true on enter,
true→false on exit. `TurnEnded` does not fire exit for every
non-invisible unit. Neither patch hardcodes hero-specific logic — that
lives entirely in `RegisterDefaults()`, which registers a handler that
only acts for `unit.isHero && uniqueId == "Assassin"`, tinting
`"Graphic"` `ExoSkeletonRenderer`s under `unit.unitView` (not a global
`FindObjectsOfType`) to alpha `0.5f` on entry, `1f` on exit. Also the
sole current registrant of the generic `RegisterStateVisualEffect` hook.
Tracked: [`invisibility-exit-fires-every-turn.md`](../../docs/issues/unresolved-tested/invisibility-exit-fires-every-turn.md).

## `IronhideScriptLoaderPatches.cs`

Full-method-replacement `[HarmonyPrefix]` on
`IronhideScriptLoader.LoadScripts(string path)`. Skips (with a warning) if
the path's folder was already loaded. Otherwise, per `TextAsset`: raises
`RaiseResolvingScript(name)`; if a non-null override comes back, builds a
replacement `TextAsset` with that source under the same name. Duplicate
script names are skipped with a warning. `RegisterDefaults()`: looks for
`Mods/*/Lua/<scriptName>.lua`.

See [`cross-references.md`](cross-references.md) for a documented
incidental fix baked into this file.

## `LocalizationManagerPatches.cs`

Full-method-replacement `[HarmonyPrefix]` on
`LocalizationManager.Load(LanguageCode)`. Reimplements the full pipeline:
`LanguageNames`, English reference (merged for non-EN), `AUTO_`-prefixed
auto-localization (if enabled), the main language file, `QUEST_`-prefixed
file, and an optional build-flavor override path — merged via
`Dictionary.MergeWith(newData, true, true)`, later sources win. Every load
goes through a local `LoadKVTextWithMods` helper that merges in every
dictionary yielded by `RaiseContributingLocalization(currLang)` on top of
the file-loaded base. `RegisterDefaults()`: maps `LanguageCode` to a
filename suffix (16 languages, e.g. `EN`→`"en_US"`, `JA`→`"ja"`), scans
`Mods/*/Localization/*_<suffix>.txt`, merges all matches.

See [`cross-references.md`](cross-references.md) for a documented
deliberate bug fix baked into this file.

## `PartyTokenComponentPatches.cs`

Full-method-replacement `[HarmonyPrefix]` on
`PartyTokenComponent.UpdateHeroes`. Reimplements per-hero map-token
instantiation; for each hero with a non-empty `unitOnMap`, stashes the
hero's unique ID via `ExoSkeletonModData.SetHeroId` **before** calling the
existing `ReplacePart(unitOnMap, "Asst_Party_Base")` — the write side of
the out-of-band channel `ExoSkeletonDataPatches` reads. Skips
`ReplacePart` when `FindPartIndex` misses `unitOnMap` or
`Asst_Party_Base`. A missing `Asst_Party_Banner` logs and appends an
empty vertex array so `partVertices` stays aligned with `unitsOnMap`.
A second prefix on `SetFlagVisibility` skips that unit when the banner
part is missing (vanilla would index `parts[-1]`).
Tracked: [`find-part-index-unvalidated.md`](../../docs/issues/unresolved-tested/find-part-index-unvalidated.md).

## `PortraitPatches.cs`

Consolidates what were four separate patch sets onto
`RegisterPortraitResolver`/`RegisterAbilityIconResolver`.

- `DataHelper.LoadMiniPortrait`/`LoadBigPortrait`/`LoadHeroBanner`
  (postfixes, MINI/BIG/BANNER): shared `ResolveOrFallback` helper — tries
  the resolver chain first, falls back to a bundled
  `Mods/Resources/DEFAULT_<slot>.png` only if both the resolver and
  vanilla's own result were null.
- `DataHelper.LoadSkillIcon` (postfix): resolver first, bundled
  `DEFAULTICON.png` fallback.
- `MapHeroBarPortraitComponent.SetHero` (postfix, MAP): swaps to a flat
  image via `ReplaceWithFlatImage` if resolved, targeting
  `__instance.portraitData` (skip-and-log when that field is null).
- `RewardViewComponent.SetTargetPortrait` (full-replacement prefix,
  CHALLENGE): sets up the normal rigged portrait, then swaps to flat if
  resolved.
- `UIBuffStoreItem.SetItem` (postfix, CHALLENGE): same swap pattern,
  after a bounds check on `GetAllHeroes()[heroPosition]`.
- `ReplaceWithFlatImage(GameObject, Sprite, anchorMin, anchorMax)` —
  shared `internal` helper (also used by `DialogViewManagerMapPatches`):
  destroys `ExoSkeletonUIGraphic`/`ExoSkeletonData`, reconfigures the
  `RectTransform`, adds a plain `Image`. A resolver just returns a
  `Sprite`; it doesn't need to know how that sprite ends up on screen.
  Does not reparent the transform (the old self-parent was a Unity no-op).
  Tracked: [`portrait-patches-self-parent.md`](../../docs/issues/unresolved/portrait-patches-self-parent.md),
  [`portrait-patches-hardcoded-hierarchy.md`](../../docs/issues/unresolved/portrait-patches-hardcoded-hierarchy.md),
  [`portrait-patches-buff-store-index.md`](../../docs/issues/unresolved-tested/portrait-patches-buff-store-index.md).
- `RegisterDefaults()`: one `PortraitResolver` covering all six slots via
  `Mods/*/Characters/<heroId>/portraits/` first, then
  `Mods/*/Portraits/<heroId>/<heroId>_<slot>.png` (`RGBA64` for MAP,
  `ARGB32` otherwise); empty `heroId` returns null before `Path.Combine`.
  One `AbilityIconResolver` that checks
  `Mods/*/Abilities/<id>/icons/<iconName>.png` then
  `Mods/*/AbilityIcons/<iconName>.png`.

## `SkillsBarSlotCapPatches.cs`

Campaign-wide cap of `MatchSkillsBarUnit.skills` to `skillsList.Count`
(always five hexes). Postfix on `SkillsBar.AddSkillsBar` trims extras
and logs the unit id plus dropped count. Prefixes on the private
`SetSelectedUnit(UnitViewComponent)`, `SetSelectedSkill`,
`GetSelectedSkillIcon`, and `NotDefaultSkillSelected` trim then let the
original run so a later skill grant cannot grow past the hex list. No
`EmbeddedFightHost` gate and no extra `scenario/Skill` slots. Lab's own
trim patches stay in place (idempotent).
Tracked: [`skills-bar-five-slot-cap.md`](../../docs/issues/unresolved-tested/skills-bar-five-slot-cap.md).

## `SoundPatches.cs`

Consolidates three prior patch sets onto `RegisterSoundResolver` +
`ModAPI.Audio.PlayClip`.

- `Unit.PlaySound` (prefix): resolver first, falls through to vanilla if
  unresolved.
- `MasterAudio.PlaySound` (prefix): if the clip's sound group is not
  registered, load the matching vanilla `DynamicSoundGroup*` from the
  `sounds` bundle (Assassin reusing Asra/Cleaver FXMega). Targeted by
  method name, not a baked `Type[]` — 1.1.10 used `string` for the sixth
  argument (`double?` in the real signature) and HarmonyX aborted
  PatchAll. See
  [`fxmega-sounds-need-source-hero-group.md`](../../docs/issues/resolved/fxmega-sounds-need-source-hero-group.md).
- `UIHeroManage.PromoteHero` (still a **full-method replacement** — the
  sound check lives inside a compiler-generated lambda Harmony can't
  target by name): tries the resolver inside that lambda, falls back to
  vanilla's own sound config only if unresolved.
- `UIHeroRoom.PlayHeroSelectedSound` (prefix): resolver-or-fall-through.
- `RegisterDefaults()`: per-path `AudioClip` cache (repeatedly-triggered
  combat sounds don't re-decode from disk every time); scans
  `Mods/*/Sounds/<unitId>/` for filenames containing the event name, picks
  **randomly** among multiple matches per call (only the chosen file's
  clip is cached, not the choice itself).

## `UnityDefinitionsParserPatches.cs`

Lives in namespace `Ironhide.Legends.Model.Game.Units` (same reasoning as
`AbilitiesDefinitionsPatches` — direct access to `UnitDefinition`,
`ParseDebugContext`, etc.; no effect on Harmony targeting).

- `LoadData` (full-replacement prefix): loads all `Balance/UnitDefinitions`
  assets, raises `BuildingUnitDefinitions`, appends each
  `RLHeroesFragments` / `EnemiesDefinitionsFragments` entry as its own
  wrapped `units` TextAsset, then parses via `ParseText`. Per parsed
  `(key, UnitDefinition)`: last-wins on the block key (Lab/mod fragment
  after vanilla); fires `UnitDefinitionLoaded` on add and replace.
  `RegisterDefaults()`: `Mods/*/RLHeroes` →
  hero fragments, `Mods/*/EnemiesDefinitions` → enemy fragments. Lab
  folders contribute `definition/rlheroes.txt` the same way (including
  EnemySummon). Leftover `c`-prefixed Lab block keys are stripped back
  to the folder id. The UniqueId index is built first (last level-1
  block wins on a duplicate UniqueId); UniqueId is then registered as a
  Definitions lookup key pointing at that winning row. See
  [vanilla-character-edit.md](../../docs/roadmaps/started/vanilla-character-edit.md)
  Phase 2.
- `GetDefinition` prefix: strips a leading `#`, then resolves Lab
  generated ids under both the folder spelling and the `c`-prefixed
  block key. A miss that looks like a generated id is logged; vanilla
  still falls back to `MissingUnitDefinition`.
- `ParseText` (full-replacement prefix): faithfully reproduces vanilla's
  KV→`UnitDefinition` field mapping, including the `SKILLVARIANT-HACK`
  migration block and the lvl4-archetype-disable block (both carried over
  unmodified vanilla compat logic). See
  [`cross-references.md`](cross-references.md) for the within-file
  last-wins that `LoadData` now also uses across files.
