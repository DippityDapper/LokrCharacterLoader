# LokrCharacterLoader — Layout

```
LokrCharacterLoader/
├── LokrCharacterLoaderPlugin.cs
├── CharacterAPI.cs                        (the facade — see character-api.md)
├── DefaultContentSources.cs               (see architecture.md)
├── LabExpressionIds.cs                    (UnitName #word + c-prefix for generated SpawnUnit keys)
├── LabAliases.cs                          (per-folder aliases.json + $alias expand)
├── ContentRules.cs                        (Unity-free skip/clamp/merge helpers for patches + xUnit)
├── ContentReloader.cs                     (live reload into running game caches)
├── MetagameHeroReloader.cs                (refresh loaded Hero instances after reload)
├── CustomRigs/
│   ├── CustomRigLoader.cs                 (see custom-rig-loader.md)
│   ├── CharacterLabContentLoader.cs       (LokrCharacterLab/<Id>/; legacy Characters/)
│   ├── AbilityLabContentLoader.cs         (Ability Lab-authored Abilities/* folders — moved here 2026-08-12)
│   ├── CustomFxLoader.cs                  (sprite FXMega / projectile folders + Character Lab clip-name scrape)
│   └── LocaleFileSuffixes.cs              (LanguageCode -> localization_<suffix>.txt map, shared by the two loaders above)
└── Patches/                               (see patches.md for all of these)
    ├── AbilitiesDefinitionsPatches.cs
    ├── DialogViewManagerMapPatches.cs
    ├── ExoSkeletonDataPatches.cs
    ├── ExoSkeletonModData.cs              (shared state, not a patch)
    ├── ExoSkeletonRendererPatches.cs
    ├── ExoSkeletonUIGraphicPatches.cs
    ├── FxPatches.cs                       (FXManager.Preload / LoadFXMega + DataHelper.LoadProjectile)
    ├── HeroExoSkeletonPatches.cs
    ├── HeroRosterManagerPatches.cs
    ├── InvisibilityPatches.cs
    ├── IronhideScriptLoaderPatches.cs
    ├── LocalizationManagerPatches.cs
    ├── PartyTokenComponentPatches.cs
    ├── PortraitPatches.cs
    ├── SkillsBarSlotCapPatches.cs         (campaign-wide five-hex skills-bar cap)
    ├── SoundPatches.cs
    ├── UnityDefinitionsParserPatches.cs
    └── UnitViewExoSkeletonPatches.cs      (combat view custom-rig swap)
```
