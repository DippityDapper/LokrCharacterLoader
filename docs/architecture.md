# LokrCharacterLoader — Architecture

## The resolver-chain pattern

A private generic helper inside `CharacterAPI` backs the three sprite/
audio/rig resolver kinds (full detail in
[`character-api.md`](character-api.md)):

```csharp
private sealed class ResolverChain<TResolver> where TResolver : class
{
    public void Register(TResolver resolver, int priority);
    public TResult Resolve<TResult>(Func<TResolver, TResult> invoke) where TResult : class;
}
```

Entries are `(priority, order, resolver)` tuples, re-sorted on every
`Register` call: higher `priority` first, ties broken by a monotonically
increasing registration counter (**first-registered wins on a tie**).
`Resolve` walks the sorted list and calls each resolver in turn — the
**first non-null result wins** and short-circuits the rest. `priority`
defaults to `0` on every public `Register*` method, so an unmodified
caller lands in registration order among other default-priority callers.

This is deliberately different from `ContributingLocalization`, which
merges **all** non-null contributions instead of stopping at the first —
see [`character-api.md`](character-api.md) for that event.

## `CharacterAPI` is dogfooded, not special-cased

`CharacterAPI`'s header comment frames it explicitly as the extension
point for *other* plugins — this plugin's own default file-convention
logic (scanning `Mods/*/<Category>` folders) is registered through the
exact same surface any third-party plugin would use, as an ordinary,
lowest-priority participant. There is no separate fast path or "default
content" registry.

## `DefaultContentSources.cs` — the wiring

```csharp
internal static void RegisterAll()
```

Called once from `LokrCharacterLoaderPlugin.Awake()`, **before**
Harmony patches — every default content source must be wired up
before any patched game method can fire, since a patch firing early with
no registered resolver would just fall through to vanilla behavior with
nothing to override it later. `Awake` applies each patch class on its
own (`CreateClassProcessor`) so one bad signature cannot abort the rest
of the plugin (1.1.10's `PlaySound` Type[] miss did exactly that).

Calls each patch class's own `RegisterDefaults()` in order:

1. `PortraitPatches.RegisterDefaults()`
2. `SoundPatches.RegisterDefaults()`
3. `HeroRosterManagerPatches.RegisterDefaults()`
4. `Ironhide.Legends.Model.Game.Units.UnityDefinitionsParserPatches.RegisterDefaults()`
5. `Ironhide.Legends.Model.Game.Units.Abilities.AbilitiesDefinitionsPatches.RegisterDefaults()`
6. `LocalizationManagerPatches.RegisterDefaults()`
7. `IronhideScriptLoaderPatches.RegisterDefaults()`
8. `InvisibilityPatches.RegisterDefaults()`
9. `CustomRigLoader.RegisterDefaults()`
10. `CharacterLabContentLoader.RegisterDefaults()` (expands `$alias` from that folder's aliases.json)
11. `AbilityLabContentLoader.RegisterDefaults()` (same `$alias` expand per ability folder)
12. `CustomFxLoader.RegisterDefaults()`

The two referenced by full name physically live in the game's own
namespace — see [`conventions.md`](conventions.md) for why, and
[`patches.md`](patches.md) for what each `RegisterDefaults()` actually
registers.

## Full-method-replacement vs. narrow pre/postfix

Where a base-game method is too intertwined with private fields/local
closures to safely Prefix/Postfix around, patches in this plugin use a
`[HarmonyPrefix]` that fully reimplements the method body and returns
`false` to skip the original — private-field/method access in these
reimplementations goes through HarmonyLib's `Traverse` API or reflected
`MethodInfo`/`AccessTools.Method`. See [`patches.md`](patches.md) for
which specific patches use this vs. a narrow, original-method-preserving
pre/postfix.
