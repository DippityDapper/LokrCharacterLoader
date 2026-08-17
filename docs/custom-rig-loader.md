# LokrCharacterLoader — `CustomRigs/CustomRigLoader.cs`

`public static class` (public specifically so `LokrCharacterLab`'s rig
editor can call `BuildFromFolder` directly for its live Preview — see
`../../LokrLab/docs/character/rig-editor-scene.md`).

## Folder/file convention

Category `"Characters"` → `Mods/*/Characters/<RigId>/`, each
containing `rig/rig.json` in `ExoSkeletonDataAsset.ReloadData`'s exact
schema plus one PNG per part, named to match a `"name"` entry in
`rig.json`'s `"parts"` list. `RigId` (the folder name) is the same string
a hero's `UnitDefinition.metaExo` references. This format was worked out
and proven against real game art in `LokrCharacterLab`'s early rig tests
and is reused here directly.

Folders without `rig/rig.json` are skipped silently during indexing —
`EnemySummon` props that use a vanilla `Model` prefab without custom art
do not need a rig folder entry.

## Indexing / caching

`EnsureIndexed()` runs once, lazily — cheap (finds character folders, checks
`rig/rig.json` exists) — skipping a folder silently if `rig/rig.json` is
missing, and keeping the **first-found** on a duplicate rig ID (warning
logged, no crash).

The expensive part (loading every PNG, packing an atlas via
`ModAPI.Assets.PackSprites`, calling `ReloadData`) is deferred to
`Resolve()` and only happens the first time a hero actually requests that
rig, then cached in `builtRigsById` — mirroring vanilla's own
never-rebuilt caching. `Resolve` is registered as the plugin's only
`ExoSkeletonResolver` via `RegisterDefaults()`. A build that matches
zero JSON part names to packed sprites logs an error, returns null, and
is **not** cached.

Before `ReloadData`, `Build` parses `rig.json` with SimpleJSON and omits
any `parts[]` entry whose `name` does not match a packed sprite
(case-insensitive; `#` suffix stripped on the sprite name only — the
same rules as `ExoSkeletonDataAsset.FindSprite`). Frame `parts` entries
that name a dropped part are omitted so `FindPartIndex` never writes
`-1` into `renderOrder`. Missing parts log vanilla's
`Cant find sprite named: …` warning. This matches `LoadParts`' silent
skip; it does not invent placeholder meshes, and it does not Harmony-
replace `ReloadData`. Tracked:
[`reload-data-missing-sprite-nre.md`](../../docs/issues/unresolved-tested/reload-data-missing-sprite-nre.md).

`BuildFromFolder(rigId, folderPath)` is a public escape hatch that builds
from any folder **without** touching the mod-folder index/cache — the
caller owns the result. This is what `LokrCharacterLab`'s Preview feature
calls directly. It returns null on the same all-parts-miss failure.

## Required animation validation

```csharp
RequiredAnimationNames = { "Stand", "Portrait", "StandStatic" }
```

The actual check requires `"Stand"` **and** (`"Portrait"` OR
`"StandStatic"`). Every base-game call site reading
`Hero.exoSkeletonDataAsset` (adventure map hero bar, party visual, buff
store, reward screen, dialog views) hardcodes one of these names and
throws an uncaught exception deep in game code with no hint the cause is a
missing animation. A rig missing them still builds and works fine in the
Lab preview scene (which never touches `Hero`) but crashes the moment it's
assigned to a real hero — this is a **warning only**, not a hard failure.

Map intros also play `"Speak"`. If that clip is missing, the game logs
`Animation Speak doesn't exist` on the unit (often during cinematic
skip) and the talk pose is skipped. After `ReloadData`, the loader
aliases missing `Speak` to `Stand` so the cinematic can finish.

See `../../LokrLab/docs/character/rig-editor-scene.md` for the
corresponding auto-generation logic (`OnSaveClicked`/`EnsureRequiredClip`)
that guarantees a rig saved from the Animator workstation can never be
missing these.
