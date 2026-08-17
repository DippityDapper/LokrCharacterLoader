# LokrCharacterLoader — Cross-references

- **`ExoSkeletonRenderer` only reads `partSprites[0].texture` for the
  whole mesh** — drives the same atlas-packing requirement documented in
  `../../LokrModAPI/docs/classes.md` (`TextureAtlasPacker`) and
  `../../LokrLab/docs/character/character-importer.md`.
- **Required-animation-name crash risk** on every base-game read of
  `Hero.exoSkeletonDataAsset` — see
  [`custom-rig-loader.md`](custom-rig-loader.md); this is the same
  constraint LokrLab's `RigEditorScene.OnSaveClicked`
  auto-generates `Stand`/`StandStatic` to guarantee (see
  `../../LokrLab/docs/character/rig-editor-scene.md`).
- **`Hero.exoSkeletonDataAsset`'s private lazy-cache field** — the seed-
  don't-skip patch design in `HeroExoSkeletonPatches` (see
  [`patches.md`](patches.md)) exists specifically to avoid rebuilding an
  atlas texture on every property read.
- **`docs/bepinex-migration-plan.md §6`** and **`docs/modapi-plan.md`**
  (in the solution root `docs/` folder) are cited by row/section number
  as the source design documents behind several of the full-method-
  replacement patches.
- **Documented, deliberate bug fix — localization language hardcoding**
  (`LocalizationManagerPatches.cs`): an earlier version of this mod
  hardcoded `LanguageCode.JA` for five of six modded-localization lookups
  regardless of the actually-selected language; this rewrite passes the
  correct language (`EN` or `currLanguageCode`) at every call site.
- **Documented, deliberate behavior change — duplicate-key guard removal
  in `UnityDefinitionsParser.ParseText`** (`UnityDefinitionsParserPatches.cs`):
  vanilla threw on a duplicate unit-definition key within one parsed
  block; this is removed so a mod-contributed fragment overriding an
  existing unit definition doesn't crash the game (see
  `docs/content-systems.md §2` in the solution root, external to this
  plugin). `LoadData` now last-wins across files as well (1.1.16), so a
  Lab `rlheroes.txt` with a vanilla block key replaces the shipped
  definition instead of logging ERROR and keeping vanilla. See
  [vanilla-character-edit.md](../../docs/roadmaps/started/vanilla-character-edit.md).
- **Documented, incidental (non-deliberate) fix — spurious "Duplicate
  script" warning** (`IronhideScriptLoaderPatches.cs`): the old ad hoc
  control flow re-checked for duplicates after a successful mod override
  and logged a false-positive warning; the clean-resolver rewrite avoids
  this as a side effect, explicitly called out in comments as *not* an
  intentional behavior change.
- **`SKILLVARIANT-HACK` and lvl4-archetype-disable blocks**
  (`UnityDefinitionsParserPatches.ParseText`): carried over unmodified
  from the base game's own runtime migration/compat logic — vanilla
  behaviors being faithfully reproduced in the full-method copy, not
  LokrCharacterLoader-authored changes.
- **Reflection-based access to compiler-generated lambdas**
  (`SoundPatches.cs`, `UIHeroManage.PromoteHero`): kept as a full-method
  replacement specifically because the sound-check code lives inside a
  compiler-generated closure (`skillSelectedAction`) that Harmony cannot
  target by a stable method name/signature.
