# Contributing to Klovnstation 14

Coding conventions for this repo. Written for coding agents first, humans second — precise over chatty, examples over prose.

**Quick reference:**
- New code → `Content.<project>/_KS14/<Feature>/...` (see §2).
- Editing or adding a file *outside* `_KS14/` → mark it with `// KS14:` / `# KS14:` (see §3).
- **Marking a change on a single line → `/* KS14: ... */` sitting at the change itself**, not a trailing `//` at the end of the line (see §3).
- Otherwise follow upstream SS14 conventions, plus the local rules in §4.
- Before you ship: the traps in §6 that fail *silently* — no build error, no log, no failing test.

## 1. Project lineage

Klovnstation 14 is a fork of SS14:

- [space-wizards/space-station-14](https://github.com/space-wizards/space-station-14) — upstream, vanilla SS14.
- **Klovnstation 14** — this repo, a modded downstream.

We merge from upstream regularly. PRs mix upstream ports with Klovnstation 14-specific work; the conventions below keep the two easy to tell apart during those merges.

### Submodule access

The main repo is public. Every `_KsModule*` submodule (`_KsModule`, `_KsModule_ReplacedPrototypes`, and any future one matching that prefix) is private by default, but all of them share the exact same access list — there is no tier between them. Treat access as binary:

1. Either you have access to all `_KsModule*` submodules, or
2. You have access to none of them.

Don't assume partial access (e.g. "the audio submodule but not the prototypes one") is a real state to design around — it isn't a thing that exists, and code or docs shouldn't imply otherwise.

## 2. The `_KS14/` rule

**New Klovnstation 14 code lives under a `_KS14/` folder**, in every project tree that has one:

- `Content.Server/_KS14/`
- `Content.Client/_KS14/`
- `Content.Shared/_KS14/`
- `Content.IntegrationTests/Tests/_KS14/`
- `Resources/Prototypes/_KS14/`
- `Resources/Maps/_KS14/`
- `Resources/Locale/en-US/_KS14/`
- `Resources/Textures/_KS14/`
- `Resources/Audio/_KS14/`
- `Resources/ServerInfo/_KS14/`
- `Resources/ConfigPresets/_KS14/`

Inside `_KS14/`, mirror upstream's feature-driven layout (`_KS14/Atmos/Components/...`, `_KS14/Cargo/Systems/...`) — group by feature, not by type.

### File structure examples

Treat `*` as a placeholder.

**C#** — a new system for announcements:
- Upstream would put it at `Content.Server/Announcements/*`.
- Here it's `Content.Server/_KS14/Announcements/*`.
- Deviation when possible: skip the `*/Components`, `*/Systems`, `*/[Category]` split for small additions — that's for large, full-scope pieces of work only.

**YAML** — a new species:
- Upstream would put it at `Resources/Prototypes/Body/Species/*`.
- Here it's `Resources/Prototypes/_KS14/Body/Species/*`.
- Deviation when possible: feature-scoped, not type-scoped files. Split a generic file into a folder of specific ones, e.g. `shaders.yml` → `Shaders/misc.yml`.

**Notable exception**: the dev map lives at `Maps/Test/_KS14/klovndev.yml`, not `Maps/_KS14/Test/klovndev.yml`, because that gives it more leeway in integration tests. Exceptions like this — that reduce codebase divergence and avoid tweaking integration tests — are allowed.

### Namespace (C#)

A file at `Content.<project>/_KS14/<Feature>/<Sub>/File.cs` declares:

```csharp
namespace Content.<project>._KS14.<Feature>.<Sub>;
```

## 3. Upstream edits: the `// KS14:` marker

When you edit **or add** a file **outside** `_KS14/` (anywhere in upstream SS14 / other forks / `_Manifest` / `_sin` / etc.), mark Klovnstation 14's provenance inline:

- **Edits to an existing upstream file** — mark every logical change inline (forms below).
- **Adding methods/members to an existing class** — make the class partial (comment why), and add the members in a same-folder file named `*.Klovn.Feature.cs`.
- **New files added OUTSIDE `_KS14/`** — first-line header `// KS14: added in this fork` (`# KS14: added in this fork` for YAML/FTL/shell). Prefer `_KS14/`; only do this when extending an upstream tree is genuinely the right home (e.g. filling translation gaps in `Resources/Locale/en-US/_Goobstation/`).

Both forms make our changes easy to spot on the next upstream merge. Always preserve the original upstream value in the comment: swap `100 -> 50` today, and a later change to that same line becomes `100 -> 30` (not `50 -> 30`). Swap `KS14` for another fork's tag (e.g. `Goobstation`) when porting from that fork instead of writing net-new code.

**Put the marker where the change is.** `/* KS14: ... */` is the default for anything that happens on a single line, because it points at the exact token that moved. A trailing `// KS14:` says only "something on this line changed" — on a line with several things, the next person merging upstream has to diff to find out which. Reach for a trailing `//` only when the marker genuinely cannot sit at the change site: a whole added line, or a value swap where the marker would land mid-expression and wreck the line.

```csharp
// do this - the marker is attached to what actually changed
IgnoredCategories = ["Spawner", "Debug", "KsTrail" /* KS14: added */];
handle.Draw(texture, bounds, alpha/* KS14: added arg */);

// not this - which part of the line is ours?
IgnoredCategories = ["Spawner", "Debug", "KsTrail"]; // KS14: added KsTrail
```

Forms, ordered so that they take precedence over those before them:

- **Specific change (the default for single-line edits)** — `/* KS14: concise statement */` right after the change:
  ```csharp
  internal /* KS14: public -> internal */ sealed partial /* KS14: made partial */ class OldClass
  {
      public void Main(int nuParam /* KS14: added param */, int oldParam)
      {
          PredictedSpawn/* KS14: made predicted */(entityId);
      }
  }
  ```
- **C# value swap** — `// KS14: OLD -> NEW, reason (optional)`, but only when the swapped value is the whole, unambiguous tail of the line (excluding the semicolon). If it's one part of something bigger — an element of a collection literal, one argument of several — the specific-change form wins:
  ```csharp
  public const int MaxPlayers = 50; // KS14: 100 -> 50, too high
  ```
- **YAML value swap** — same, with `#`:
  ```yaml
  myValue: 50 # KS14: 100 -> 50, too high
  ```
- **Adding/changing a multi-line block** — `// KS14 start: reason` ... `// KS14 end`:
  ```csharp
  // KS14 start: check if we should return early
  if (ShouldReturnEarlyNow())
      return;
  // KS14 end
  ```
- **Removing a multi-line block** — `// KS14: reason` before the commented-out block:
  ```csharp
  // KS14: unnecessary
  /*
  doThing();
  doOtherThing();
  doMoreThings();
  */
  ```
- **Adding a line, or several changes on one line** — trailing `// KS14: short reason`:
  ```csharp
  public bool Inverted; // KS14: if true, Species list is a blacklist
  ```
- **Removing a single line** — comment it out, reason after:
  ```csharp
  /* public bool Inverted; */ // KS14: removed, if true Species list was a blacklist
  ```
- **Added `using`** — trailing `// KS14`:
  ```csharp
  using Content.Shared._KS14.NewFeature; // KS14
  ```

### YAML and Fluent (`.ftl`) edits

Same rules, `#` comments: `# KS14: ...`.

```yaml
- type: entity
  id: SomeUpstreamEntity
  components:
  - type: HealthAnalyzer
    scanDelay: 0.8 # KS14: 1.2 -> 0.8
```

## 4. Code style and upstream SS14 standards

Klovnstation 14 follows upstream Space Wizards' Den coding standards. Read and apply before any PR touching C# or YAML:

- [Codebase info](https://docs.spacestation14.com/en/general-development/codebase-info.html) — landing page for the full conventions tree.
- [Conventions](https://docs.spacestation14.com/en/general-development/codebase-info/conventions.html) — naming, comments, ECS rules (components hold *only* data; systems hold logic; events are struct `[ByRefEvent]`s named `...Event` with `OnXEvent` handlers), XAML/UI, performance, `TimeSpan`/field-deltas, YAML, localization, in-/out-of-simulation split. Primary document.
- [Codebase organization](https://docs.spacestation14.com/en/general-development/codebase-info/codebase-organization.html) — project split (Client/Shared/Server), file layout, prototype organization (`base.yml` + per-type files, no `misc/` folders).
- [Pull-request guidelines](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html) — separate PRs per feature/bug fix/refactor, test in-game, no web edits, no force-push after reviews.
- [Style guide](https://docs.spacestation14.com/en/general-development/codebase-info/style-guide.html) — C# formatting.

### YAML prototype essentials

Summarised from the upstream conventions doc — that page stays the authority; this is the part you'll reach for constantly.

**Field order** in an entity prototype: `type` → `abstract` → `parent` → `id` → `categories` → `name` → `suffix` → `description` → `components`, then the rest.

```yaml
- type: entity
  abstract: true            # omit entirely when not abstract
  parent: BaseStructure
  id: KsCatwalkIron
  categories: [ HideSpawnMenu ]
  name: catwalk
  suffix: Iron
  description: A metal walkway.
  components:
  - type: Sprite
    sprite: _KS14/Structures/catwalk.rsi
  - type: KsCatwalkIconsmoother
```

- **Casing** — prototype IDs and component names are `PascalCase`; every other field, and prototype *type* names, are `camelCase`. Never use `prefix.Something` as an ID. Locale IDs are `kebab-case`, no capitals, specific enough not to clash (`antag-traitor-user-was-traitor-message`).
- **Components** — `- type:` entries take no extra indent under `components:`, and no blank lines between them. Generalized/engine components near the top, specific ones near the bottom.
- **Spacing** — exactly one blank line between prototypes.
- **Lists** — inline (`[ A, B ]`) for `categories` and multi-`parent`; block lists for everything else.
- **Text** — no quotes on `name`/`description` unless punctuation demands it, then single quotes. Every player-facing string is localized.
- **Abstract prototypes** — no textures in them. Use `suffix` to separate spawn-menu variants instead of baking the distinction into `name`.

**One exception to upstream**: `codebase-organization` says game-code folders live directly under `Content.Client/Shared/Server`. We override this for **new fork code only** — new code goes under `_KS14/` per §2. Upstream files edited in place keep their upstream layout and carry `// KS14:` markers per §3. Don't relocate or reformat **upstream** code just to bring it into convention unless you're already changing it for another reason — every such move is a merge conflict waiting for the next upstream pull, paid for nothing. This is about upstream files specifically, not a general licence to leave things alone: fork code under `_KS14/` is ours, merges cleanly, and is fair game to tidy whenever it has drifted from the rules below.

### Local rules on top of upstream

**Casting/coercion (C#)** — always cast explicitly with the target type in parentheses:
```csharp
var myFloat = (float)GetMyInt();       // do this
float myFloat = GetMyInt();            // not this
```

**Name every optional argument (C#)** — an optional parameter is always passed with its name, so the call site says what the value *means* instead of making the reader go read the signature. Required parameters stay positional:
```csharp
// do this
_entityLookupSystem.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true);
dependencyCollection.InjectDependencies(overlay, oneOff: true);

// not this - what is 'true'?
_entityLookupSystem.FindGridsIntersecting(mapId, bounds, ref _grids, true);
dependencyCollection.InjectDependencies(overlay, true);
```

**Verbosity (C#)** — use verbose names, even where existing code is archaic (`xform` → `transform`):
```csharp
[Dependency] TransformSystem _transformSystem;   // not '_xform'
PhysicsComponent physicsComponent;               // not 'body'
SpriteComponent spriteComponent;                 // not 'sprite'
```
Members with `[DataField]` get some leeway (`Prototype` → `Proto` is fine).

**Names imply type (C#)** — a descriptively-named variable, parameter or member carries its type in its suffix:
```csharp
EntityUid targetUid;                          // EntityUid            → '...Uid'
Entity<StickyComponent> stuckEntity;          // Entity<T>            → '...Entity'
NetEntity massDriverNetEntity;                // NetEntity            → '...NetEntity'
TransformComponent userTransformComponent;    // a component          → '...Component'
EntityQuery<SpriteComponent> _spriteQuery;    // EntityQuery<T>       → '...Query'
```
This earns its keep when one thing exists in several forms in the same scope — `projectileUid` sitting next to `Entity<LagCompensatingProjectileComponent> projectile` reads unambiguously.

Exceptions, all conventional: the primary subject of a handler or method may stay bare — `entity`, `ent`, `uid` — and a locally-built event is just `ev`. As soon as a second thing of the same kind enters scope, go back to suffixes.

**`Ks` prefix (IDs & type names)** — when a new prototype ID or type name could plausibly collide with an upstream name (present or future), prefix it with `Ks`: `KsCCVars` (a fork-only cvars class, deliberately not inheriting upstream `CCVars`), `KsBlack`, `KsCatwalkIron` (colors and structure variants — generic vocabulary upstream already uses or could use). Skip the prefix when the name is already distinctive enough not to collide — `Anchorless`, `ArcFlash`, `ComplexShove` — the `_KS14/` folder already marks provenance there. This is a judgment call, not a mechanical rule: ask "would upstream plausibly ship something under this exact name?" If yes, prefix it.

**`TryGet`/`Resolve`/`Ensure` naming (C#)** — `TryGet...` implies a pure lookup: it either finds the thing or it doesn't, with no side effects either way. If a "`TryGet`" actually creates the thing when it's missing, name it `Resolve...` or `Ensure...` instead — whichever reads better for the case — not `TryGet...`:
```csharp
// misleading - this can create a new entity as a side effect, which "TryGet" doesn't promise
private bool TryGetTemplateFire(EntProtoId prototypeId, out EntityUid templateUid)

// do this instead
private bool ResolveTemplateFire(EntProtoId prototypeId, out EntityUid templateUid)
```

**`[Access]`, not a doc comment (C#)** — when a member may only be written through a particular system, enforce it with `[Access]` instead of asking in prose. A comment saying "set this through `SetFoo`" is advice the compiler cannot hold anyone to; `[Access]` is the same statement as an analyzer error (`RA0002`):

```csharp
// do this - the analyzer rejects any other type writing to these
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(IvDripSystem))]
public sealed partial class IvDripComponent : Component
{
    /// <summary>
    ///     Whether the pump is currently running.
    /// </summary>
    /// <seealso cref="IvDripSystem.SetInjectionEnabled"/>
    [DataField, AutoNetworkedField]
    public bool InjectionEnabled;
}

// not this - nothing stops the next caller, and nothing tells them they broke an invariant
/// <summary>
///     Whether the pump is running. Set this through <see cref="IvDripSystem.SetInjectionEnabled"/>
///         rather than directly, so the action and the window stay in step with it.
/// </summary>
public bool InjectionEnabled;
```

The defaults are `Self`/`Friend` = `ReadWriteExecute` and `Other` = `Read`, so the attribute alone leaves the member readable everywhere and writable only by the named types — which is what a component whose invariants live in one system wants. Tighten it with `Other = AccessPermissions.None` when even reading a member should go through the system, and put `[Access]` on the individual member instead of the class when only part of a component is restricted.

Reach for this whenever a system method exists precisely to keep two things in step — a networked field and an action's toggled state, a list and the index into it, a cached value and the thing it caches. Note that it guards the *member*, not what the member points at: `Other = Read` on a `List<T>` field still lets anyone call `Add` on the list they read.

**Source-gen `[Dependency]` fields (C#)** — on current engine versions, injected `[Dependency]` fields on `EntitySystem` (and the few other injectable types) must be writable, and their owning class must be `partial`:
```csharp
// old
public sealed class MySystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;
}

// current
public sealed partial class MySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
}
```

**Don't re-declare a dependency your base class already injects (C#)** — several engine base types arrive with dependencies already resolved. Declaring your own is a second injected field pointing at the same object: it compiles, it works, nothing warns, and it reads as though the two might differ. Use the inherited member.

```csharp
// old
[Dependency] private IPrototypeManager _prototypeManager = default!;   // then _prototypeManager.Index(...)

// current
ProtoMan.Index(...);                                                   // no declaration at all
```

What each base already gives you, and the name to use:

| Base type | Inherited member | Type |
| --- | --- | --- |
| `EntitySystem` (incl. `GameRuleSystem<T>`) | `EntityManager` | `EntityManager` |
| | `ProtoMan` | `IPrototypeManager` |
| | `Factory` | `IComponentFactory` (forwards to `EntityManager.ComponentFactory`) |
| | `LogManager` / `Log` | `ILogManager` / `ISawmill` |
| | `Loc` | `ILocalizationManager` |
| `BoundUserInterface` | `EntMan` | `IEntityManager` |
| | `PlayerManager` | `ISharedPlayerManager` |
| | `UiSystem` | `SharedUserInterfaceSystem` |
| `Overlay` | `OverlayManager` | `IOverlayManager` (resolved in its constructor) |
| `LocalizedCommands` | `LocalizationManager`, or `Loc` | `ILocalizationManager` |
| `LocalizedEntityCommands` | the above, plus `EntityManager` | `EntityManager` |
| `ToolshedCommand` | `Toolshed`, `Loc` | `ToolshedManager`, `ILocalizationManager` |

```csharp
// not this - LocalizedEntityCommands already injects EntityManager
public sealed partial class KsSomeCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entityManager = default!;
    // ...then _entityManager.HasComponent<MapGridComponent>(uid)
}

// not this either - BoundUserInterface already injects EntMan
public sealed class KsSomeBoundUserInterface : BoundUserInterface
{
    [Dependency] private IEntityManager _entityManager = default!;
}

// nor this - Overlay's constructor resolves OverlayManager itself
public sealed class KsSomeOverlay : Overlay
{
    [Dependency] private IOverlayManager _overlayManager = default!;
}
```

**The inherited names break the verbosity rule above, and that is fine.** `EntMan`, `ProtoMan` and `Factory` are the engine's names, not ours; wanting `_entityManager` instead is not a reason to declare a second field. Rename nothing, declare nothing, just use them.

**Not everything on a base is inherited.** `EntitySystem` injects `ISharedPlayerManager` and `IReplayRecordingManager` as **private** fields, so a subclass genuinely cannot see them and does declare its own:

```csharp
// correct - EntitySystem's own player manager is private, so this is not shadowing
[Dependency] private ISharedPlayerManager _playerManager = default!;
```

And the table is per base, not universal: only `EntitySystem` has a `ProtoMan`. An overlay, a
`BoundUserInterface`, a window, a manager or an HTN operator that needs prototypes declares its own
`IPrototypeManager`, exactly as before.

The rule is "check the base before you declare", not "assume it is there". Nothing in the toolchain catches either mistake — C# allows a derived field to hide a base one of a different name without a whisper, so this is a review-time thing.

**Inject `EntityQuery<T>`, don't `GetEntityQuery<T>()` (C#)** — the collection that injects into `EntitySystem`s (`IEntitySystemManager.DependencyCollection`) resolves `EntityQuery<T>` and `EntitySystem` as well, unlike the default `IoCManager` one. So declare queries as dependencies:
```csharp
[Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;   // not GetEntityQuery<SpriteComponent>() in Initialize
```
Fall back to `GetEntityQuery<T>()` only where injection genuinely isn't available.

Classes that aren't systems (overlays, UI, managers) can opt into the same collection through `SystemCollectionHookManager` — it hands you a collection that already has every loaded system and query:
```csharp
[Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

// ...then, once the collection exists:
_systemCollectionHookManager.HookAction(dependencyCollection =>
    dependencyCollection.InjectDependencies(overlay, oneOff: true));
```

**Subscribe with `[SubscribeLocalEvent]`, not a call in `Initialize` (C#)** — the engine generates the subscription from an attribute on the handler, inferring the event (and component) from the handler's signature. The class must be `partial`, because the generator emits an `AutoSubscriptions()` override into it:
```csharp
// old
public override void Initialize()
{
    base.Initialize();

    SubscribeLocalEvent<KsTrailComponent, ComponentStartup>(OnStartup);
}

private void OnStartup(Entity<KsTrailComponent> entity, ref ComponentStartup args) { }

// current
[SubscribeLocalEvent]
private void OnStartup(Entity<KsTrailComponent> entity, ref ComponentStartup args) { }
```
`[SubscribeNetworkEvent]` and `[EventSubscription]` (the latter for `SubscribeAllEvent`) work the same way. Ordering goes in the attribute rather than in a separate call: `[SubscribeLocalEvent(after: [typeof(SharedGunSystem)])]`.

Once every subscription in an `Initialize` has moved to an attribute, delete the override — an `Initialize` left holding nothing but `base.Initialize();` is dead code.

The attribute cannot express every subscription. Keep the explicit call in `Initialize` when:
- the subscription is conditional — inside an `if`, a loop, or an `#if`;
- the handler is a lambda rather than a named method;
- the handler is generic, `virtual` or `abstract`, or a type argument comes from the containing class's own type parameters (`BaseHierarchySystem` subscribes on `THierarchyComp`, so it stays as-is);
- one handler serves several subscriptions — a method has one signature, so it gets one attribute and one event type (`KsSensorSystem.OnEmitterAddedOrRemoved` covers both `ComponentStartup` and `ComponentShutdown`);
- the handler's parameter is a *base* of the event actually subscribed to, since the attribute would subscribe to the base type instead.

`RA0058` flags every call that can be converted and ships a code fixer, so bulk conversion is a tooling job, not a manual one:
```sh
# RA0058 is Info severity, so raise it in .editorconfig first, then put .editorconfig back -
# leaving it raised turns the not-yet-converted calls elsewhere in the repo into Release build errors.
dotnet format analyzers Content.Shared/Content.Shared.csproj --diagnostics RA0058 --severity info --include Content.Shared/_KS14/
```
`RA0056` is the error you get when the containing class isn't `partial`, and `RA0054` when the handler's signature doesn't match a subscribable delegate.

The generator only runs in projects that import it. `Content.Client`, `Content.Server` and `Content.Shared` each carry `<Import Project="..\RobustToolbox\MSBuild\Robust.EntitySystemSubscriptionsGenerator.targets" />` for exactly this reason — without it the attribute still compiles, nothing is generated, and **every converted subscription silently stops firing** with no build error to point at it. Any other project that wants attribute subscriptions needs the same import.

**Engine version** — this fork tracks a pinned `RobustToolbox` submodule, currently v289.0.3. When bumping it, read [RELEASE-NOTES.md](https://github.com/space-wizards/RobustToolbox/blob/master/RELEASE-NOTES.md) for every intervening version and check whether upstream SS14 already shipped the content-side fix — porting their commit is cheaper and keeps future merges clean. A bump is also one of the main ways new debug assertions arrive, so run the tests in `Debug` afterwards as well as building `Release` (§5).

## 5. Build configurations, and what each one catches

`Release` and `Debug` fail on **disjoint** sets of problems. Neither is a superset of the other, so a change is only green when both are.

| | `Release` | `Debug` / `DebugOpt` |
| --- | --- | --- |
| `TreatWarningsAsErrors` | **on** (`MSBuild/Content.props`) | off |
| `DebugTools.Assert`, `DebugTools.AssertNotNull` | **compiled out** | live |

- **Build `-c Release`.** It is the only configuration that reproduces the warnings-as-errors failures CI reports; `Debug` will happily build code that fails CI.
- **Run tests `-c Debug` as well.** Every `DebugTools.Assert` in the engine and in content is a no-op under `Release`, so a Release-only test run silently skips the lot — and CI runs the tests in `Debug`. A suite that is green in `Release` tells you nothing about assertions.

```sh
# warnings-as-errors
dotnet build Content.Shared/Content.Shared.csproj -c Release

# debug assertions
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~_KS14"
```

This bites hardest in integration-test prototypes, where a wrong component combination is legal C#, legal YAML, and only a debug assert objects:

```yaml
# SharedMoverController.HandleMobMovement asserts that anything with an InputMover is a
# KinematicController. Dynamic is for thrown objects and debris, not mobs.
# Passes every Release test run. Fails every Debug one.
- type: entity
  id: KsSomeTestMob
  components:
  - type: Physics
    bodyType: Dynamic
  - type: InputMover
```

The same asymmetry applies to anything a debug assert guards: stack invariants, `Resolve` calls with `logMissing`, and the engine's own transform and physics checks. If a test only ever runs in `Release`, treat its coverage of those as zero.

## 6. Cross-codebase pitfalls

Every entry here is something that produced working, compiling, apparently-tested code that was wrong
anyway. They share one shape: **nothing tells you**. No build error, no exception, no log line, and
more than once a test that passed whether the bug was present or not. They are collected here because
none of them belong to a single feature — each one is waiting in whatever you touch next.

### CVars: unsubscribe from anything that does not live as long as the process

`IConfigurationManager`'s subscriber list is rooted for the life of the process. A handler that closes
over `this` therefore keeps `this` alive forever, along with everything it references — for a UI control
that means its render targets, its buffers, and every object hanging off them, for every instance ever
created.

An `EntitySystem` may ignore this, but only because it is handed a mechanism that does it for you.
`Subs.CVar` calls `RegisterUnsubscription`, and `ShutdownSubscriptions` runs it when the system shuts
down:

```csharp
// EntitySystem - fine, and the only place that is. Subs.CVar unsubscribes itself at shutdown.
public override void Initialize()
{
    base.Initialize();

    Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitGravity, value => _transitGravity = value, true);
}
```

Everything else — `Control`s and viewports, `Overlay`s, `BoundUserInterface`s, windows, anything
constructed and thrown away during a round — has no `Subs` and no shutdown hook, so the unsubscribe is
yours to write. Keep the handler in a field, because a fresh lambda is a different delegate and
`UnsubValueChanged` will not match it:

```csharp
// do this - the handler is held, so it can be taken back off again
private Action<bool>? _drawGapLevelsHandler;

private void Initialise()
{
    _drawGapLevelsHandler = value => _drawGapLevels = value;
    _configurationManager.OnValueChanged(KsCCVars.ZLevelDrawGapLevels, _drawGapLevelsHandler, invokeImmediately: true);
}

protected override void Dispose(bool disposing)
{
    if (_drawGapLevelsHandler != null)
    {
        _configurationManager.UnsubValueChanged(KsCCVars.ZLevelDrawGapLevels, _drawGapLevelsHandler);
        _drawGapLevelsHandler = null;
    }

    base.Dispose(disposing);
}

// not this - the lambda is unreachable, so this control can never be collected
_configurationManager.OnValueChanged(KsCCVars.ZLevelDrawGapLevels, value => _drawGapLevels = value, invokeImmediately: true);
```

The same reasoning covers every other process-lifetime registry a short-lived object can put itself
into: `IPlayerManager` and `INetManager` events, `IOverlayManager`, `IUserInterfaceManager` handlers,
and any static or manager-held list. Ask "what owns the thing I just handed my `this` to, and does it
outlive me?" If yes, the teardown is yours.

### CVars: `CLIENTONLY` is not "a client-side setting"

`CLIENTONLY` and `SERVERONLY` mean *"skip registering this cvar on the other side entirely"*, not "only
this side cares about it". So the moment shared code touches one, the side it was skipped on throws:

```
System.Collections.Generic.KeyNotFoundException : The given key 'klovn.zlevel.parallax_strength'
    was not present in the dictionary.
  at Robust.Shared.Configuration.ConfigurationManager.OnValueChanged[T](...)
  at Content.Shared._KS14.ZLevel.KsZLevelSystem.Initialize()
```

From `Initialize` that is fatal — the server does not start, and every integration test fails at SetUp,
which looks nothing like "a cvar has the wrong flag".

Pick by **who reads it**, not by who it is for:

| Read from | Changed by | Flags |
| --- | --- | --- |
| Client code only | the player | `CLIENTONLY \| ARCHIVE` |
| Server code only | the server | `SERVERONLY` |
| **Shared code** | the player | `CLIENT \| ARCHIVE` — registered both sides, only the client may set it |
| Shared code, must agree | the server | `SERVER \| REPLICATED` |

The third row is the one that catches people. A rendering knob read through a shared helper is still
read on the server, where it sits at its default and is never used — that costs nothing, and it is the
only way the shared call compiles and runs on both sides.

Two consequences worth knowing when testing one: a `CVar.CLIENT` var cannot be set server-side at all,
so `OverrideCVar(Side.Server, ...)` on one is silently ignored and the assertion reads as the feature
being broken. And a *disconnected* pooled client has never started its entity systems, so resolving one
to read the value throws `UnregisteredTypeException` — such a test needs `Connected = true`.

### The sandbox rejects APIs no build will warn you about

Content assemblies are type-checked against a whitelist when they load, not when they compile. Reach for
something outside it and both configurations build clean, the IDE is happy, and the failure arrives at
assembly load:

```
Robust.Shared.ContentPack.TypeCheckFailedException
Sandbox violation: Access to method not allowed:
    System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
        System.Collections.Generic.Dictionary`2<!!0, !!1>, !!0, bool&)
```

**The cascade is what makes this expensive to diagnose.** One violation takes the assembly down, which
takes the integration pool down with it, and every unrelated test then fails with:

```
SetUp : System.InvalidOperationException : Pool manager has not been initialized
```

A run that reports a hundred-odd failures across unrelated features, all of them `Pool manager has not
been initialized`, has **one** cause, and it is not in any of the tests named. Find the single result
that failed with something else - `TypeCheckFailedException` - and fix that. Running with
`--logger "trx;LogFileName=..."` and grouping the results by message is the quick way to see that shape;
`-v q` prints only the total and hides it entirely.

The offenders are mostly the low-level performance conveniences: `CollectionsMarshal`, `Unsafe`,
`MemoryMarshal`, most of `System.Runtime.InteropServices`, reflection that writes, and anything
touching the filesystem or process directly. Plain `Dictionary`, `Span`, `System.Numerics` and
`MathF` are all fine. If you are reaching for something to avoid a dictionary lookup or a struct copy
in rendering code, the copy was almost certainly cheaper than finding this out:

```csharp
// not this - compiles everywhere, refused at load
ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_map, key, out _);
entry.Value += 1;

// this - a struct copy, and one the sandbox allows
_map.TryGetValue(key, out var entry);
entry.Value += 1;
_map[key] = entry;
```

Content.IntegrationTests is the cheapest way to find out, because loading the assemblies is the first
thing it does - a single test from any fixture is enough to prove the sandbox accepted the build.

### Only one system may subscribe to a given component and event pair

The event bus throws
`InvalidOperationException: Duplicate Subscriptions for comp=<Component>, event=<Event>` when a second
subscription for the same pair is registered. It throws at **startup**, so it does not break the one
feature that collided — it breaks every integration test in the suite at SetUp, which reads as
"everything is broken" rather than "two handlers want the same event".

So a partial system split across several files cannot have two files subscribing to the same pair. When
a second file needs to react, call into it from the existing handler rather than adding a subscription:

```csharp
// Interaction.cs - the one subscription for this pair
[SubscribeLocalEvent]
private void OnElevatorStopped(Entity<ZLevelElevatorComponent> entity, ref ZLevelElevatorStoppedEvent args)
{
    // Called rather than subscribed separately: only one system may take a given component and event
    //      pair, so everything that reacts to a stop goes through here.
    StopMovementAudio(entity);

    InvokeSignals(entity.Owner, stopped: true);
}
```

Where several *unrelated* systems genuinely need the same moment, the component's owner re-broadcasts it
as an event of its own — see `KsZLevelTransitEvents.cs`, which exists because `KsZLevelPhysicsSystem`
had already taken `KsZLevelTransitComponent`'s `ComponentStartup` and `ComponentShutdown`.

### A test that never fails is worse than no test

Two separate bugs in one feature were each "covered" by a test that passed with the bug present. The
test is then actively harmful: it is the reason nobody looks again.

**So prove the test fails.** Break the thing it covers — comment the fix out, unsubscribe the handler,
revert the line — rebuild, and watch it go red. A test you have only ever seen pass is a test you have
not checked. This is cheap and it is the only thing that actually catches the cases below.

Two ways to end up here that have nothing to do with carelessness:

- **The assertion is satisfied by something other than the code under test.** A test named for landing
  on a crossing platform passed with the obstruction event unsubscribed entirely, because the ordinary
  slice-crossing path put the faller on that map anyway. It asserted a true fact about the wrong
  mechanism. When a subsystem has two routes to the same observable outcome, name which one the test
  pins and write a second test for the other.
- **The environment is more permissive than production.** See the PVS entry below — pooled pairs run
  with filtering off, so the visibility assertion cannot fail.

When you find a test like this, fix the test in the same change as the bug. Deleting it is better than
leaving it.

### Verifying an attribute subscription actually generated

Related, and the honest way to answer "is this handler wired up?" rather than guessing from the
signature: ask the generator. `EmitCompilerGeneratedFiles` writes the `AutoSubscriptions()` override it
produced to disk, and it either contains your handler or it does not.

```sh
# --no-incremental matters: an up-to-date build emits nothing, which reads exactly like "the generator
# refused my handler" and is the reason to check twice before concluding anything.
dotnet build Content.Shared/Content.Shared.csproj -c Release --no-incremental \
  -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=/tmp/gen

cat /tmp/gen/Robust.Shared.EntitySystemSubscriptionsGenerator/*/Content.Shared.<Namespace>.<System>.g.cs
```

Worth knowing what the generator *does* accept, because the signature rules are easy to guess wrong:
a broadcast `ref` handler (`void OnFoo(ref SomeByRefEvent args)`) is fine, on an `abstract partial`
system as much as a sealed one — the emitted `SubscribeLocalEvent<T>(OnFoo, null, null)` binds to the
`EntityEventRefHandler<T>` overload. The genuine exclusions are the ones listed in §4, and a project
missing the generator import.

### "Invisible" is usually PVS, and PVS tests are vacuous by default

When something is on the server, in the right place, with the right components, and the client cannot
see it, the cause is far more often that it was never *sent* than that it was drawn wrong. Two traps
follow from that, and both make a PVS test pass while the bug is live:

1. **Pooled test pairs run with `net.pvs` off**, which sends every entity to every client. A test that
   does not turn it back on proves nothing whatsoever about visibility:
   ```csharp
   await OverrideCVar(Side.Server, CVars.NetPVS, true);
   ```
2. **Leaving PVS detaches an entity on the client, it does not delete it** (`MetaDataFlags.Detached`).
   So `TryGetEntity` keeps answering `true` forever for anything the client was *ever* told about.
   Assert on an entity spawned **after** the state under test began — one that can only have arrived if
   it is genuinely being sent — or check the detached flag explicitly.

Include a control entity that must *not* arrive, too. Without one, "everything reached the client" and
"PVS is not filtering at all" are the same green test.

### Removing or deleting while enumerating: use the deferred forms

Removing a component from inside an enumeration of that component, or deleting an entity the enumeration
can still reach, mutates the storage being walked. The engine ships deferred forms for exactly this. Use
them rather than gathering uids into a scratch list first:

| Immediate | Deferred | What is deferred |
| --- | --- | --- |
| `RemComp` | `RemCompDeferred` | Only the removal from storage. `ComponentShutdown` runs **now**. |
| `Del` | `QueueDel` | **Everything.** Nothing happens until the queue is processed. |
| `PredictedDel` | `PredictedQueueDel` | As `QueueDel`, for predicted shared code. |

Both queues drain at the end of the tick (`EntityManager.TickUpdate` → `ProcessQueueudDeletions`, then
`CullRemovedComponents`), outside any system's loop. Calling either twice on the same target is harmless.

```csharp
// do this - safe mid-enumeration, and no scratch list
var warpedEnumerator = AllEntityQuery<KsPitchWarpedAudioComponent>();
while (warpedEnumerator.MoveNext(out var audioUid, out var warpedAudioComponent))
    RemCompDeferred(audioUid, warpedAudioComponent);

// not this - mutates the component storage the enumerator is walking
while (warpedEnumerator.MoveNext(out var audioUid, out _))
    RemComp<KsPitchWarpedAudioComponent>(audioUid);
```

The two deferred forms leave different things behind until the end of the tick, and each is a silent trap:

- **A `RemCompDeferred`'d component is shut down, but still stored.** Enumerations and `HasComp` keep
  finding it. Anything that acts on it has to skip it, or it undoes what its shutdown handler just
  cleaned up:
  ```csharp
  if (warpedAudioComponent.LifeStage > ComponentLifeStage.Running)
      continue;
  ```
- **A `QueueDel`'d entity is entirely alive.** It isn't terminating, so `TerminatingOrDeleted` says
  `false` and every query still returns it. To ask whether it is on its way out, use
  `EntityManager.IsQueuedForDeletion(uid)`.

Reach for the immediate forms only when nothing up the call stack is iterating what you are removing,
*and* the caller needs the thing gone before it continues.

### Reparenting and map work inside engine callbacks

Some engine events are raised mid-operation, with the engine's own iteration, broadphase or chunk
structures still in flight. Reparenting an entity, moving a grid or deleting a map from inside one is
reentrant mutation of the thing that called you, and it does not throw something catchable — it takes
the server down.

`GridFixtureSystem`'s split is the known one: it creates grid entities and reparents everything off the
old grid, and editing a lot of tiles at once is the ordinary way to reach it. `KsZLevelPhysicsSystem`
carries a deferred-check set for exactly this reason, and the pattern generalises — queue the work into
a set, drain it in `Update` clear of the callback:

```csharp
// Queue from the callback...
[SubscribeLocalEvent]
private void OnPhysicsLand(Entity<PhysicsComponent> entity, ref LandEvent args)
{
    _pendingTransitChecks.Add(entity.Owner);
}

// ...and act on it in Update, draining into a scratch list first, because acting on one entry can
//      raise the very events that queue into the set.
_drainedTransitChecks.Clear();
_drainedTransitChecks.AddRange(_pendingTransitChecks);
_pendingTransitChecks.Clear();
```

Draining into a second list is not optional: a `foreach` over a set that the loop body can add to
throws straight out of `Update`.
