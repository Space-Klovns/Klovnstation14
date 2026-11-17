# Contributing to Klovnstation 14

Coding conventions for this repo. Written for coding agents first, humans second — precise over chatty, examples over prose.

**Quick reference:**
- New code → `Content.<project>/_KS14/<Feature>/...` (see §2).
- Fork-owned files (`_KS14/`, `.Klovn`/`.Ks` filenames, or `Ks`/`KS14` namespaces/types) need no provenance headers (see §3).
- Editing existing upstream code *outside* `_KS14/` → mark it with `// KS14:` / `# KS14:` (see §3).
- **Marking a change on a single line → `/* KS14: ... */` sitting at the change itself**, not a trailing `//` at the end of the line (see §3).
- Otherwise follow upstream SS14 conventions, plus the local rules in §4.
- Before you ship: the traps in §6 that fail *silently* — no build error, no log, no failing test.
- Popups, prototype names and anything else a player reads must follow *their* language, not the server's (see §7).

## 1. Project lineage

Klovnstation 14 is a fork of SS14:

- [space-wizards/space-station-14](https://github.com/space-wizards/space-station-14) — upstream, vanilla SS14.
- **Klovnstation 14** — this repo, a modded downstream.

We merge from upstream regularly. PRs mix upstream ports with Klovnstation 14-specific work; the conventions below keep the two easy to tell apart during those merges.

### Submodule access

The main repo is public. Every `_KsModule*` submodule (`_KsModule`, `_KsModule_ReplacedPrototypes`, and any future one matching that prefix) is private, and all of them share one access list: either you have access to all of them or to none. Don't design around partial access; it isn't a state that exists.

Public files must build and run without the submodules, so they never reference an id that only a `_KsModule*` defines. Private additions to public prototypes go through `Partials` (§4).

## 2. The `_KS14/` rule

**New Klovnstation 14 code lives under a `_KS14/` folder**, in every project tree that has one:

- `Content.Server/_KS14/`, `Content.Client/_KS14/`, `Content.Shared/_KS14/`
- `Content.IntegrationTests/Tests/_KS14/`
- `Resources/Prototypes/_KS14/`, `Resources/Maps/_KS14/`, `Resources/Locale/en-US/_KS14/`, `Resources/Textures/_KS14/`, `Resources/Audio/_KS14/`, `Resources/ServerInfo/_KS14/`, `Resources/ConfigPresets/_KS14/`

Inside `_KS14/`, mirror upstream's feature-driven layout (`_KS14/Atmos/Components/...`, `_KS14/Cargo/Systems/...`): group by feature, not by type. Give each feature its own subfolder rather than leaving files loose in a shared one (`Entities/Effects/SupplyPod/`, not `Effects/podglow.yml`), and mirror it in `Resources/Locale/`.

- **C#**: a new announcements system goes in `Content.Server/_KS14/Announcements/*`, where upstream would use `Content.Server/Announcements/*`. Skip the `Components`/`Systems` split for small additions; it's for large pieces of work.
- **YAML**: a new species goes in `Resources/Prototypes/_KS14/Body/Species/*`. Prefer feature-scoped files to type-scoped ones, and split a generic file into a folder of specific ones (`shaders.yml` → `Shaders/misc.yml`).
- **Exception**: the dev map is at `Maps/Test/_KS14/klovndev.yml`, not `Maps/_KS14/Test/klovndev.yml`, because that gives it more leeway in integration tests. Exceptions that reduce divergence and avoid tweaking tests are allowed.

A file at `Content.<project>/_KS14/<Feature>/<Sub>/File.cs` declares `namespace Content.<project>._KS14.<Feature>.<Sub>;`.

## 3. Upstream edits: the `// KS14:` marker

Mark each logical change to an **existing upstream file outside** `_KS14/` (upstream SS14, other forks, `_Manifest`, `_sin`, etc.) inline, so the next upstream merge can find it:

- **Adding members to an existing class**: make the class partial (comment why), and add them in a same-folder `*.Klovn.Feature.cs` file.
- **Fork-owned files need no provenance headers**: files under `_KS14/`, files named `*.Klovn.*` or `*.Ks.*` (any case), and files whose names or namespaces/types use the `Ks`/`KS14` prefix. New files outside `_KS14/` need no blanket header either; prefer `_KS14/` unless extending an upstream class or tree is the right home.
- **Always preserve the original upstream value in the comment**: swap `100 -> 50` today, and a later change becomes `100 -> 30`, not `50 -> 30`.
- Use another fork's tag (e.g. `Goobstation`) instead of `KS14` when porting from that fork.

**Put the marker where the change is.** `/* KS14: ... */` is the default for anything on a single line, because it points at the exact token that moved. A trailing `// KS14:` only says "something on this line changed". Use it only when the marker can't sit at the change: a whole added line, or a value swap where the marker would land mid-expression.

```csharp
// do this - the marker is attached to what actually changed
IgnoredCategories = ["Spawner", "Debug", "KsTrail" /* KS14: added */];
handle.Draw(texture, bounds, alpha/* KS14: added arg */);

// not this - which part of the line is ours?
IgnoredCategories = ["Spawner", "Debug", "KsTrail"]; // KS14: added KsTrail
```

Forms, each taking precedence over those before it:

- **Specific change (the default for single-line edits)**: `/* KS14: concise statement */` right after the change:
  ```csharp
  internal /* KS14: public -> internal */ sealed partial /* KS14: made partial */ class OldClass
  {
      public void Main(int nuParam /* KS14: added param */, int oldParam)
      {
          PredictedSpawn/* KS14: made predicted */(entityId);
      }
  }
  ```
- **C# value swap**: `// KS14: OLD -> NEW, reason (optional)`, only when the value is the whole tail of the line (bar the semicolon). One element of a collection or one argument of several takes the specific form instead.
  ```csharp
  public const int MaxPlayers = 50; // KS14: 100 -> 50, too high
  ```
- **YAML value swap**: the same, with `#`: `myValue: 50 # KS14: 100 -> 50, too high`.
- **Adding or changing a multi-line block**: `// KS14 start: reason` ... `// KS14 end`.
- **Removing a multi-line block**: `// KS14: reason` before the commented-out block.
- **Adding a line, or several changes on one line**: trailing `// KS14: short reason`.
- **Removing a single line**: comment it out, reason after: `/* public bool Inverted; */ // KS14: removed, ...`.
- **Added `using`**: trailing `// KS14`.
- **A type from a new namespace used only once**: write it fully qualified at the use site, marked there, instead of adding a `using`; one edit at the point of use is one fewer line to conflict. Add the `using` (marked) once the namespace is used more than once in the file.
  ```csharp
  if (Content.Client._KS14.Portal.KsPortalRenderSystem.PassActive /* KS14: added */)
      return false;
  ```

YAML and Fluent (`.ftl`) edits follow the same rules with `#` comments:

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
- [Codebase organization](https://docs.spacestation14.com/en/general-development/codebase-info/codebase-organization.html) — project split, file layout, prototype organization (`base.yml` + per-type files, no `misc/` folders).
- [Pull-request guidelines](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html) — separate PRs per feature/bug fix/refactor, test in-game, no web edits, no force-push after reviews.
- [Style guide](https://docs.spacestation14.com/en/general-development/codebase-info/style-guide.html) — C# formatting.

**One exception to upstream**: `codebase-organization` puts game code directly under `Content.Client/Shared/Server`. New fork code goes under `_KS14/` instead (§2). Upstream files edited in place keep their layout and carry markers (§3). Don't relocate or reformat **upstream** code unless you're already changing it: every move is a merge conflict paid for nothing. Fork code under `_KS14/` merges cleanly and is fair game to tidy whenever it has drifted from these rules.

Area guides are agent skills under `.claude/skills/`. Read the matching one before working in that area. To port an SS13 sprite, run `/dmi-to-rsi`; to port SS13 code or read DM, use the `dreammaker` skill.

### YAML prototypes

Summarised from the upstream conventions doc, which stays the authority.

**Field order**: `type` → `abstract` → `parent` → `id` → `categories` → `name` → `suffix` → `description` → `components`, then the rest.

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

- **Casing**: prototype ids and component names `PascalCase`; every other field, and prototype *type* names, `camelCase`. Never `prefix.Something` as an id. Locale ids are `kebab-case`, no capitals, specific enough not to clash (`antag-traitor-user-was-traitor-message`).
- **Components**: `- type:` entries take no extra indent under `components:`, with no blank lines between them. Generalised and engine components near the top, specific ones near the bottom.
- **Spacing**: exactly one blank line between prototypes.
- **Lists**: inline (`[ A, B ]`) for `categories` and multi-`parent`; block lists everywhere else.
- **Text**: no quotes on `name`/`description` unless punctuation demands them, then single quotes. Every player-facing string is localised.
- **Variants**: separate spawn-menu variants with `suffix`, not by baking the distinction into `name`.
- **Shared parts go in a parent, not a YAML anchor.**

### Assets from another repository

Attribution (an RSI's `copyright`, an audio `attributions.yml`) gives a source URL with at least the **full hash of the commit** the asset was taken at. "Taken from tgstation" alone makes the original very hard to trace.

- One file: point at its exact path, `Taken from tgstation at https://github.com/tgstation/tgstation/blob/<full commit hash>/icons/mob/telegraphing/telegraph.dmi`, the form `/dmi-to-rsi` writes.
- Several files from the same repository and commit: one entry for all of them, pointing at their shared parent folder, or at the repository at that hash and nothing more. An `attributions.yml` entry already lists many files under one licence, copyright and source; don't split them into an entry each.

### Partial prototypes: change an upstream prototype without editing its file

A partial prototype has the same kind and id as an existing one and is merged into it, instead of failing the load as `Duplicate ID`. Use it to add to or take from an upstream prototype from a fork-owned file, with nothing to conflict on the next merge.

**Put it in a folder named `Partials`**, anywhere below `Resources/Prototypes/`, subfolders included: `Prototypes/_KS14/Reagents/Partials/explosive.yml`. Nothing else to register; `KsEntryPoint` finds these folders before prototypes load on client, server and linter, and the packaged localisation cache uses the same rule (`KsEntryPoint.IsInPartialsFolder`). Keep each `Partials` folder next to the feature it changes, holding only partials: inside one, an id that *accidentally* matches an existing prototype silently merges into it. A file that can't move into one can be listed in `KsEntryPoint.PartialPrototypePaths`, as an exception.

Write only the fields you change. Sequences are appended to, mappings combined key by key, plain values replaced:

```yaml
# adds a track to upstream's collection; every upstream track stays
- type: !PartialOnly soundCollection
  id: LobbyMusic
  files:
  - /Audio/_KsModule/LobbyMusic/melcom-cyberpunk.ogg
```

- **`!PartialOnly`** on the `type`: if the original doesn't exist (upstream renamed or removed it), do nothing rather than quietly create a prototype from the fragment. Use it whenever you're modifying something, which is almost always.
- **`- !Remove X`** removes `X` from a sequence; **`key: !Remove`** removes a mapping key, **`key: !Remove 1`** only if its value is `1`; **`- !Remove type: SomeComponent`** removes a component.
- **`field: !Clear`** empties a sequence or mapping before your entries go in.
- **`- !Index:0 X`** inserts at a position (negative counts from the end, clamped); combine with `!Remove` to reorder. **`- !CombineIndex:0 key: value`** merges into the element *already at* that index.

Every example is on `IPrototypeManager.PartialDirectory` (`RobustToolbox/Robust.Shared/Prototypes/IPrototypeManager.cs`). Partials apply after every non-partial file. Prefer one to a `_KsModule_ReplacedPrototypes` replacement, which restates the whole prototype and drifts from upstream. Cover a partial with a test that the merged prototype has your change *and* upstream's data (`KsLobbyMusicPartialTest`): one that stopped applying looks exactly like the unmodified prototype.

### Types, numbers and naming (C#)

- **Cast explicitly**: `var myFloat = (float)GetMyInt();`, not `float myFloat = GetMyInt();`.
- **`FixedPoint2` has its own conversions**: `FixedPoint2.New(float)` in, the `(float)` cast out (not `quantity.Float()`), never a hand-rolled rounding helper: `var used = (float)FixedPoint2.New(MathF.Min(availableFoam, foamPerTile));`. `New` applies an epsilon before truncating to 0.01u, so a copy of it can disagree with what lands in the solution by 0.01u.
- **Name every optional argument**: `FindGridsIntersecting(mapId, bounds, ref _grids, approx: true)`, not `..., true)`. Required parameters stay positional.
- **No string literals as prototype ids**, tests included: `RA0033` rejects a literal passed to `Index`, `TryIndex` and similar. Declare a typed id once: `private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";`. An id that only exists in a test's `[TestPrototypes]` goes in a non-`static` field (`private readonly ProtoId<T> _id = "..."`): the YAML linter validates only static ones, and an instance field still satisfies `RA0033`. Entity ids use `EntProtoId`; `EntProtoId<T>` also requires the entity to have a `T`, which the linter checks on a `[DataField]` (`public EntProtoId<InstantActionComponent> Action;`).
- **Angles are `Angle`, not `float`**: bearings, turns, arcs and rotations are an `Angle` (`Robust.Shared.Maths`), made with `Angle.FromDegrees(25)`, `new Angle(radians)` or `vector.ToAngle()`, turned back with `.ToVec()`, compared with `Angle.ShortestDistance(a, b)` (which wraps; `bearing - other` on floats gets 359° against 1° wrong). Read `.Theta` or `.Degrees` only at the edge. As a `[DataField]`, YAML gives degrees (`flankArc: 70`), or radians with a `rad` suffix. A `float` only where `Angle`'s 64-bit `double` is specifically unwanted: a measurably hot loop over many angles, or a buffer for the GPU or the network.
- **Locale string ids are `LocId`, not `string`**: `LocId` (`Robust.Shared.Localization`) is just a wrapper around a `string` marking it as the id of a locale (Fluent) string, converting to and from `string` implicitly. As a `[DataField]`, dictionary keys included, the YAML linter checks it against the locale files (`LocIdSerializer`), so a mistyped id fails the linter rather than showing in game as the raw key: `[DataField] public LocId Name = "ks-thing-name";`, `Dictionary<LocId, SoundSpecifier>`.
- **Verbose names**, even where existing code is terse: `_transformSystem`, `physicsComponent`, `spriteComponent`, not `_xform`, `body`, `sprite`. `[DataField]` members get some leeway.
- **Names imply type**: `...Uid` (`EntityUid`), `...Entity` (`Entity<T>`), `...NetEntity`, `...Component`, `...Query`. The primary subject of a handler may stay bare (`entity`, `ent`, `uid`) and a local event is `ev`; once a second thing of the same kind is in scope, use suffixes.
- **`Ks` prefix** on a new prototype id or type name that could plausibly collide with upstream's, now or later (`KsCCVars`, `KsBlack`, `KsCatwalkIron`). Skip it when the name is distinctive enough already (`Anchorless`, `ArcFlash`). Ask "would upstream plausibly ship this exact name?".
- **No `Ks` prefix on path-referenced files inside `_KS14/`** (shaders, textures, audio, fonts): `/Textures/_KS14/Shaders/lobby_contrast.swsl`, not `ks_lobby_contrast.swsl`. Prototype ids, type names (and their `.cs` files), locale keys and style classes still follow the prefix rule, as they share one namespace with upstream.
- **`TryGet...` is a pure lookup.** If it creates the thing when missing, call it `Resolve...` or `Ensure...`.
- **`"metaAtlas": false`** in a large sprite's `meta.json` keeps it out of the startup atlas (VRAM for the whole session) and loads it on demand.

### Entities and components (C#)

**An entity and its component travel together as `Entity<T>`**, never an `EntityUid` parameter beside a component parameter, so the pair can't drift apart (a component handed in with the wrong uid). Its source is `RobustToolbox/Robust.Shared/GameObjects/Entity.cs`:

```csharp
[SubscribeLocalEvent]
private void OnShutdown(Entity<NpcHolderComponent> entity, ref ComponentShutdown args) { }

public bool TryStartAttack(Entity<NpcHolderComponent?> entity, string attackId)
{
    if (!_holderQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false))
        return false;
    // ...entity.Comp from here on
}
```

- A component the caller may not have is `Entity<T?>`, resolved inside; a bare `EntityUid` converts to it implicitly.
- Several components are `Entity<T1, T2>`, not one in the entity and the rest as extra parameters.
- A method that never touches a component takes a plain `EntityUid`.
- Handlers for by-value `class` events (`DamageChangedEvent`) are the exception: the engine has `Entity<T>` handlers only for `ref` events (`RA0054`). Keep `(EntityUid uid, TComp component, TEvent args)`, say why in a comment, and pass the pair on as an `Entity<T>`.
- Upstream signatures stay as they are unless you're already changing them.

| Member | `Entity<T>` | `Entity<T1, T2, …>` |
| --- | --- | --- |
| The uid | `.Owner` | `.Owner` |
| A component | `.Comp` | `.Comp1`, `.Comp2`, … |
| Deconstruct | `var (uid, component) = entity;` | `var (uid, component1, component2) = entity;` |
| Build one | `(uid, component)` | `(uid, component1, component2)` |
| As `T?` | `entity.AsNullable()` | `entity.AsNullable()` (every component nullable) |

Conversions are implicit: to `EntityUid` (write `.Owner` anyway where it reads ambiguously), to each component type, from `EntityUid` to the all-nullable form, and between arities (`Entity<T1, T2>` narrows to `Entity<T1>`; `Entity<T1>` widens to `Entity<T1, T2?>`). An `EntityQuery<T>` resolves the whole entity by `ref` (`_holderQuery.Resolve(ref entity, logMissing: false)`); `EntitySystem.Resolve` takes the fields: `Resolve(entity.Owner, ref entity.Comp1, ref entity.Comp2)`. It's a mutable `record struct`, so assigning `.Comp` on a copy changes nothing for the caller.

**Per-entity state is a component, not a system dictionary.** A `Dictionary<EntityUid, T>` on a system leaks entries for deleted entities, is invisible to VV, and needs clearing by hand. Put the data on the entity, `[Access]` it to the owning system, and read it through an injected query:

```csharp
[RegisterComponent, Access(typeof(NpcLightDetectionSystem))]
public sealed partial class NpcLightLevelCacheComponent : Component
{
    [ViewVariables] public float Level;
    [ViewVariables] public GameTick ComputedTick;
}
```

Fine as they are: transient work lists (a set of uids queued this tick and drained in `Update`), and a collection *inside* a component (an NPC's sightings keyed by target), which belongs to the entity that owns it.

**No `static` mutable state on systems.** Integration tests run several pooled servers in one process, each with its own clock, and a static field is shared by all of them: `PuddleSystem`'s static `_nextTileEffectUpdate` stopped a heated fuel puddle from ever igniting in tests, and the existing tests passed anyway because they only lit fuel spilled already hot, which skips the timer. `static readonly` constants and shapes are fine.

**Act when a value changes, not every tick in case it did.** Whatever follows a value (a fixture sized to a width, a cache, a sprite state) is updated by the code that changes it: a setter, an event handler, `AfterAutoHandleState`, a cvar subscription. Polling in `Update` costs every tick for every entity and still reacts a tick late. This only holds while the setter is the only way in, so `[Access]` the field and close off VV (below).

**Cheapest rejection first** in a filter loop: a field on a component you already have before a `HasComp`, a `HasComp` before a fixture or layer loop. Broad lookups return mostly things you skip.

### `[Access]` and ViewVariables (C#)

**Enforce "only written through this system" with `[Access]`, not a doc comment.** A comment is advice; `[Access]` is an analyzer error (`RA0002`):

```csharp
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(IvDripSystem))]
public sealed partial class IvDripComponent : Component
{
    /// <seealso cref="IvDripSystem.SetInjectionEnabled"/>
    [DataField, AutoNetworkedField]
    public bool InjectionEnabled;
}
```

Defaults are `Self`/`Friend` = `ReadWriteExecute` and `Other` = `Read`; tighten with `Other = AccessPermissions.None`, or put it on individual members. It guards the *member*, not what it points at: `Other = Read` on a `List<T>` still lets anyone `Add`. Reach for it whenever a system method exists to keep two things in step (a networked field and an action's state, a list and an index into it, a cache and its source).

**VV (ViewVariables) writes past `[Access]`.** Every `[DataField]` and `[ViewVariables]` member is writable in the VV window and by `vvwrite`, by reflection, and the server hears only that the component was dirtied. When a field must change only through a setter:

1. `[ViewVariables(VVAccess.ReadOnly)]`: the window writes by reflection and ignores custom paths, so read-only is the only way to keep it out.
2. Register a VV path named after the field whose setter calls yours; `vvread`/`vvwrite` resolve a registered path first. Remove it in `Shutdown`: the VV manager outlives the system.

```csharp
[DataField, AutoNetworkedField, Access(typeof(KsPortalTravelSystem)), ViewVariables(VVAccess.ReadOnly)]
public float Width = 1f;

_viewVariablesManager.GetTypeHandler<KsPortalViewComponent>().AddPath(
    nameof(KsPortalViewComponent.Width),
    (_, component) => component.Width,                             // getter: (uid, component)
    (uid, width, component) => SetWidth((uid, component), width)); // setter: (uid, value, component?)

_viewVariablesManager.GetTypeHandler<KsPortalViewComponent>().RemovePath(nameof(KsPortalViewComponent.Width));
```

The command is `vvwrite /entity/<uid>/KsPortalView/Width 3`: the local `EntityUid` number, the component's name without `Component`, then the path. Upstream registers a path this way for `EventHorizonComponent.TargetConsumePeriod` but leaves the field writable in the window, which still bypasses it. Test it through `IConsoleHost` and assert on what it keeps in step (`KsPortalTravelTest.TestVvWriteOfWidthResizesTheSensor`).

### Dependencies and queries (C#)

- **`[Dependency]` fields are writable, and their class `partial`** (source-generated injection): `[Dependency] private EntityLookupSystem _entityLookupSystem = default!;`.
- **Don't re-declare what the base already injects.** A second field pointing at the same object compiles, works, and reads as if the two might differ; nothing warns. The inherited names break the verbosity rule, and that's fine.

| Base type | Inherited member | Type |
| --- | --- | --- |
| `EntitySystem` (incl. `GameRuleSystem<T>`) | `EntityManager`, `ProtoMan`, `Factory`, `LogManager` / `Log`, `Loc` | `EntityManager`, `IPrototypeManager`, `IComponentFactory`, `ILogManager` / `ISawmill`, `ILocalizationManager` |
| `BoundUserInterface` | `EntMan`, `PlayerManager`, `UiSystem` | `IEntityManager`, `ISharedPlayerManager`, `SharedUserInterfaceSystem` |
| `Overlay` | `OverlayManager` | `IOverlayManager` (resolved in its constructor) |
| `LocalizedCommands` | `LocalizationManager`, or `Loc` | `ILocalizationManager` |
| `LocalizedEntityCommands` | the above, plus `EntityManager` | `EntityManager` |
| `ToolshedCommand` | `Toolshed`, `Loc` | `ToolshedManager`, `ILocalizationManager` |

- **Not everything on a base is visible**: `EntitySystem` injects `ISharedPlayerManager` and `IReplayRecordingManager` *privately*, so declaring your own is correct. And only `EntitySystem` has `ProtoMan`: an overlay, BUI, window, manager or HTN operator declares its own `IPrototypeManager`. Check the base before you declare.
- **Use `EntityManager.TransformQuery` and `MetaQuery`**, not your own `EntityQuery<TransformComponent>` or `<MetaDataComponent>`. `Transform(uid)` and `MetaData(uid)` are fine when you only need the component.
- **Inject `EntityQuery<T>`** (`[Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;`) rather than calling `GetEntityQuery<T>()` in `Initialize`: the systems' dependency collection (`IEntitySystemManager.DependencyCollection`) resolves queries and systems, unlike the default `IoCManager` one. Fall back to `GetEntityQuery<T>()` only where injection isn't available. Non-systems (overlays, UI, managers) can use `SystemCollectionHookManager.HookAction(collection => collection.InjectDependencies(overlay, oneOff: true))`.

- **Every entity with all of up to four components**: `var query = EntityQueryEnumerator<TComp1, TComp2>(); while (query.MoveNext(out var uid, out var comp1, out var comp2)) { ... }` (`EntityManager.EntityQueryEnumerator` outside a system; `MoveNext(out comp1, out comp2)` without the uid). The components come back non-null; paused entities are skipped (below, *Engine helpers*). Removing an enumerated component, or deleting an entity, inside the loop needs the deferred forms (§6).
- **Put the rarest component first.** It walks every entity with `TComp1` and looks each of the rest up by uid, skipping those without: the cost is how many have `TComp1`, whatever the others are. So `EntityQueryEnumerator<NpcSuppressiveFireComponent, TransformComponent>()`, never `<TransformComponent, ...>`, which visits every entity there is.

### Subscriptions (C#)

**Subscribe with `[SubscribeLocalEvent]`, not a call in `Initialize`.** The generator infers the event and component from the handler's signature and emits an `AutoSubscriptions()` override, so the class must be `partial` (`RA0056`). Ordering goes in the attribute: `[SubscribeLocalEvent(after: [typeof(SharedGunSystem)])]`. `[SubscribeNetworkEvent]` and `[EventSubscription]` (for `SubscribeAllEvent`) work the same. Delete an `Initialize` left holding only `base.Initialize();`.

Keep the explicit call when the subscription is conditional (an `if`, loop or `#if`), the handler is a lambda, generic, `virtual` or `abstract`, a type argument comes from the class's own type parameters (`BaseHierarchySystem`), one handler serves several subscriptions (`KsSensorSystem.OnEmitterAddedOrRemoved`), or the handler's parameter is a *base* of the subscribed event.

`RA0058` flags convertible calls and has a fixer. It's Info severity, so raise it in `.editorconfig` for the run and put it back afterwards, or the unconverted calls elsewhere become Release errors:

```sh
dotnet format analyzers Content.Shared/Content.Shared.csproj --diagnostics RA0058 --severity info --include Content.Shared/_KS14/
```

The generator only runs in projects that import `RobustToolbox/MSBuild/Robust.EntitySystemSubscriptionsGenerator.targets` (`Content.Client`, `.Server`, `.Shared`, `.IntegrationTests` do). Without it the attribute compiles and **every converted subscription silently stops firing**. A new project also needs `Robust.Shared.Analyzers` imported globally (`<Using Include="Robust.Shared.Analyzers" />` or a `GlobalUsings.cs`), since the generated code uses `[MustCallBase]` unqualified. A handler signature the generator can't bind is `RA0054`. To check a handler really generated, emit the generated files (`--no-incremental` matters: an up-to-date build emits nothing, which looks exactly like a refused handler):

```sh
dotnet build Content.Shared/Content.Shared.csproj -c Release --no-incremental \
  -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=/tmp/gen
cat /tmp/gen/Robust.Shared.EntitySystemSubscriptionsGenerator/*/Content.Shared.<Namespace>.<System>.g.cs
```

A broadcast `ref` handler (`void OnFoo(ref SomeByRefEvent args)`) is accepted, on an `abstract partial` system as well as a sealed one.

### Popups (C#)

In shared code, call `PopupEntity`, `PopupCoordinates` or `PopupCursor` directly: they predict themselves, and the server's copy is matched to the client's rather than shown twice. `PopupPredicted*` and `PopupClient` are `[Obsolete]` wrappers. **`recipient` is a filter, not a prediction hint**: `PopupEntity(message, uid, recipientUid)` shows the popup to that player only. So `PopupPredicted(message, uid, recipient)` becomes `PopupEntity(message, uid)`, and `PopupClient(message, uid, recipient)` becomes `PopupEntity(message, uid, recipient)`. The message itself is a `KsPopupMessage` (§7).

### Randomness in shared code (C#)

Shared code runs on the client too, predicted, and runs again on every re-prediction. `IRobustRandom` rolls differently on each side and each run, so a roll the client predicts shows one result, then snaps to the server's. Where a roll in shared code decides something predicted (a dodge, a disarm, which limb, where blood lands), seed a `System.Random` from what both sides agree on, with `KsSharedRandomExtensions` (`Content.Shared/_KS14/Random/Helpers/`):

```csharp
var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
    (int) _gameTiming.CurTick.Value,
    KsSharedRandomExtensions.GetNetId(bodyEntity.Owner, EntityManager),
    (int) partType); // what tells this roll from another one this tick
var dodged = predictedRandom.NextDouble() < (double) dodgeChance;
```

- **Same inputs, same rolls**: two rolls in one tick for one entity need an input that differs (`partType` above, the other entity's net id in a disarm). Two systems meant to agree on one roll seed it alike (`GunDodgerSystem.RollDodge`, for both the dodge and whether a hitscan shot is let through, the same tick).
- **Seed once per action** and pass the `Random` down to what rolls after, rather than seeding again in each helper.
- **Side effects still once**: the roll is the same on every re-prediction, but a popup or a sound off it is gated on `IsFirstTimePredicted` as ever .
- **Extensions on `System.Random`**: `NextVector2(maxMagnitude)`, `NextUnitVector2`, `NextVector2Box`, besides `Next`/`NextDouble` and Robust's `Pick`/`Prob`.
- **Each seed allocates a `Random`**: fine for an action, not in a per-tick loop over many entities. Server-only code keeps `IRobustRandom`.

### Engine helpers whose behaviour isn't in their name

Everything under `Robust.{Shared,Client,Server}/GameObjects/`, subfolders included, is in the flat namespace `Robust.{Shared,Client,Server}.GameObjects` (`!NO_FOLDER_NAMESPACES`), so search engine types by name, not path.

- **`Resolve` logs an error by default** (`logMissing` defaults to `true`, with a stack trace), and an integration test fails on an error log. Pass `logMissing: false` where the component is legitimately optional. `EntityQuery<T>.CompOrNull(uid)` returns null; `Comp(uid)` throws.
- **`EntityQueryEnumerator` skips paused entities; `AllEntityQueryEnumerator` doesn't.** Anything on a paused or uninitialised map is invisible to the first.
- **Read a prototype's component without spawning**: `ProtoMan.Index(id).TryComp<TComp>(out var component, Factory)`. The instance is shared by every reader: read-only.
- **A `TimeSpan` `[DataField]`** reads a plain number as seconds (`2.5`), or one number with one unit suffix (`s`, `m`, `h`: `1.5m`). No `ms`, no `1h30m` (`TimeSpanExt.TryTimeSpan`).
- **`[AutoPausedField]`** (with `[AutoGenerateComponentPause]`) shifts a `TimeSpan`, `TimeSpan?` or `Dictionary<TKey, TimeSpan>` forward by however long the entity was paused, and dirties it if it's also `[AutoNetworkedField]`. Use it on every absolute end time, or paused timers expire the moment they unpause.
- **`ValueList<T>`** (`Robust.Shared.Collections`) is a `List<T>` in a struct, for method-local scratch lists. A copy shares the array but not the count, so pass it by `ref`; it doesn't throw if changed while enumerated.
- **An enum's `Equals` boxes both sides** (it's `System.Enum`'s, taking `object`): two allocations where `==` is one integer compare. It matters in bulk: `PathfindingData.Equals` boxed `PathfindingBreadcrumbFlag`s under every `PathPoly` lookup, about 230 KB and 60% of an NPC tactical search's allocations. Use `==`, `!=` or `(flags & Flag) != 0`, above all in any `IEquatable<T>.Equals` or `GetHashCode` of a dictionary key, which runs on every lookup. `HasFlag` doesn't box on current .NET, but `&` doesn't rely on the JIT. To find allocations, `GC.GetTotalAllocatedBytes(precise: true)` says how much and an `EventListener` on `Microsoft-Windows-DotNETRuntime` (GC keyword `0x1`, `Verbose`) gets `GCAllocationTick` events naming the type; both work in integration tests.

**Engine version**: the `RobustToolbox` submodule is pinned, currently v295.0.1. When bumping it, read [RELEASE-NOTES.md](https://github.com/space-wizards/RobustToolbox/blob/master/RELEASE-NOTES.md) for every version in between and check whether upstream SS14 already shipped the content-side fix (porting it is cheaper and keeps merges clean). Bumps bring new debug assertions, so run the tests in Debug as well as building Release (§5).

### Events

A system that owns state raises; any system that cares subscribes. A new behaviour adds a subscription and changes nothing in the raiser.

- **Event or call**: call a system when the callee is known. Raise an event when subscribers the raiser doesn't know must be able to observe or change the outcome. Dependencies point one way: lower-level systems raise, features subscribe (`NpcSquadSystem` raises `NpcSquadMemberDownedEvent`, `NpcMeterOnSquadLossSystem` subscribes). When callers need the result, wrap the raise in a public method (`SharedFlashSystem.CanFlash` wraps `KsCanFlashEvent`).
- **Raising**: new events are `[ByRefEvent] record struct`s raised with `ref` (no allocation); `class` events are older and by-value. `RaiseLocalEvent(uid, ref ev)` reaches that entity's components; `RaiseLocalEvent(ref ev)` broadcasts, for events with no owning entity (`ChemicalFireTileChangedEvent`); `broadcast: true` does both. One subscription per component and event pair (§6), so unrelated systems that need the same moment get a re-broadcast from the component's owner.
- **Combining results**: several handlers write to the event in no set order, so writes must commute (`args.CanMove |= ...`, `args.Duration *= 2`, `ModifySpeed`). `Cancelled` only ever goes to `true`. Where order is inherent, declare it with `before:`/`after:` (`DisposalSignallerSystem` on `GetDisposalsNextDirectionEvent`).
- **Relays**: a directed event stops at the entity's own components, and a missing relay fails silently, so test it.

| To reach | The event needs | Handlers subscribe to |
| --- | --- | --- |
| Worn items | `IInventoryRelayEvent` with `TargetSlots`, and a line in `InventorySystem.Relay.cs` | `InventoryRelayedEvent<T>` |
| Held items | A line in `SharedHandsSystem.Relay.cs` | `HeldRelayedEvent<T>` |
| Status effects | A line in `StatusEffectSystem.KlovnRelay.cs` | `StatusEffectRelayedEvent<T>` |
| Organs | A subscription on `BodyComponent` to `BodySystem.RelayEvent` | `BodyRelayedEvent<T>` |

Handlers read and write the original through `args.Args`. `Subs.SubscribeWithRelay` subscribes one handler to the plain, worn and held forms at once.

**Event names are a contract** about when an event is raised and what its handlers may change. **Pure** means a handler changes only the event: no component writes, `Dirty`, popups, audio, damage or RNG rolls.

| Pattern | Asks | Raised | Handlers |
| --- | --- | --- | --- |
| `Can...Event` | May this happen? | Any number of times: per tick, by a UI, while an NPC plans | Pure |
| `Get...Event` | What is this value? | On demand; the raiser returns the result | Pure |
| `Modify...Event` | What does this value become? | Mid-calculation, on a value about to be applied; the raiser reads it back | Pure |
| `Refresh...Event` | What should the cached value be? | By a `Refresh...()` method when an input changes, which stores the result on a component | Pure |
| `...AttemptEvent` | This is happening: stop it or respond | Once per real try, right before the act | May have side effects |

- **Name a new event for how it is raised**: a pure question is a Can or a Get.
- **To ask, raise a Can, never an Attempt**: Attempt handlers run side effects for a try that never happened (`ClumsySystem.OnCatchAttempt` rolls RNG and deals damage). A `Silent` or `ShowPopup` flag (`StorageOpenAttemptEvent`, `BasePickupAttemptEvent`) doesn't make an Attempt pure: it only quiets the handlers that read it.
- **An action that needs both raises both**: `SharedFlashSystem.Flash` raises `KsCanFlashEvent`, then `FlashAttemptEvent` for a real flash; `CanDropTargetEvent` and `DragDropTargetEvent` split the same way.
- **Modify isn't a notification**: it doesn't mean the value was applied. `DamageableSystem.ChangeDamage`: `BeforeDamageChangedEvent` can cancel, `DamageModifyEvent` transforms (skipped for `ignoreResistances`, and fired at full strength even if armour absorbs it all), `DamageDealtEvent` carries the final amount. React to `DamageDealtEvent` (`IvDripSpillageSystem`).
- **Get or Modify**: a Get builds from a default and anything can ask (`GetPryTimeModifierEvent`); a Modify passes through a value the raiser computed and is about to use (`ModifyUncuffDurationEvent`). A handler of one may raise the other (`SlowOnDamageSystem` raises `ModifySlowOnDamageSpeedEvent` while handling `RefreshMovementSpeedModifiersEvent`).
- **Refresh results are cached and can go stale**: everything reads the component the result is stored on, not the event, so a contributing system calls `Refresh...()` whenever its contribution changes, or the cached value stays wrong silently. `SlowOnDamageSystem` calls `RefreshMovementSpeedModifiers()` on equip, unequip, its component's startup and shutdown, and `DamageChangedEvent`.
- **Existing code isn't proof**: `CanSeeAttemptEvent` and `NpcPushableAttemptEvent` are pure queries named as Attempts; `BlockingSystem.OnUserDamageModified` and `SharedFirelockSystem.OnDoorGetPryTimeModifier` have side effects on events that must be pure. Before raising an event more often than today, read every subscriber, relays included, and move side effects out.

## 5. Build configurations, and what each one catches

`Release` and `Debug` fail on **disjoint** sets of problems, so a change is green only when both are.

| | `Release` | `Debug` / `DebugOpt` |
| --- | --- | --- |
| `TreatWarningsAsErrors` | **on** (`MSBuild/Content.props`) | off |
| `DebugTools.Assert`, `DebugTools.AssertNotNull` | **compiled out** | live |

- **Build `-c Release`**: the only configuration that reproduces CI's warnings-as-errors failures.
- **Read build output by `error CS` and the `Passed!`/`Failed!` line**, not by grepping `error`: obsolete-API warnings contain it.
- **Run tests `-c Debug`**, as CI does. A Release-only run skips every assertion; treat its coverage of them as zero.

```sh
dotnet build Content.Shared/Content.Shared.csproj -c Release                       # warnings-as-errors
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~_KS14"   # assertions
```

It bites hardest in test prototypes, where a wrong component combination is legal C# and YAML and only a debug assert objects: `SharedMoverController.HandleMobMovement` asserts that anything with an `InputMover` is a `KinematicController`, so a test mob with `bodyType: Dynamic` passes every Release run and fails every Debug one. The same goes for stack invariants, `Resolve` with `logMissing`, and the engine's transform and physics checks.

Integration-test compile errors CI catches:

- **`CS8632`**: nullable annotations without `#nullable enable`. `Content.IntegrationTests` has nullable *off*, so `GasMixture? air` in a test is a warning, and in CI an error. Drop the `?` or add `#nullable enable` and fix what it surfaces.
- **`RA0033`**: literal prototype ids (§4); test-only ids go in non-static fields.

## 6. Cross-codebase pitfalls

Each of these produced working, compiling, apparently-tested code that was wrong anyway, and none belongs to one feature. Area-specific traps live in the area's skill.

### CVars: unsubscribe from anything that doesn't live as long as the process

`IConfigurationManager`'s subscriber list lives for the whole process, so a handler closing over `this` keeps `this` alive forever, with everything it references. An `EntitySystem` may ignore this only because `Subs.CVar` unsubscribes at shutdown. Everything else (`Control`s, viewports, `Overlay`s, `BoundUserInterface`s, windows, anything made and dropped during a round) unsubscribes itself, holding the handler in a field, because a fresh lambda won't match:

```csharp
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
```

The same applies to every other process-lifetime registry: `IPlayerManager` and `INetManager` events, `IOverlayManager`, `IUserInterfaceManager` handlers, any static or manager-held list. Ask what owns what you handed `this` to, and whether it outlives you.

### CVars: `CLIENTONLY` is not "a client-side setting"

`CLIENTONLY`/`SERVERONLY` mean "don't register this on the other side at all". Shared code touching one throws there (`KeyNotFoundException: The given key 'klovn.zlevel.parallax_strength' was not present`), and from `Initialize` that stops the server and fails every test at SetUp. Pick by **who reads it**:

| Read from | Changed by | Flags |
| --- | --- | --- |
| Client code only | the player | `CLIENTONLY \| ARCHIVE` |
| Server code only | the server | `SERVERONLY` |
| **Shared code** | the player | `CLIENT \| ARCHIVE` (registered both sides, only the client sets it) |
| Shared code, must agree | the server | `SERVER \| REPLICATED` |

In tests, `OverrideCVar(Side.Server, ...)` on a `CLIENT` cvar is silently ignored, and a *disconnected* pooled client has never started its systems, so resolving one throws `UnregisteredTypeException`: such a test needs `Connected = true`.

### The sandbox rejects APIs no build warns about

Content assemblies are type-checked against a whitelist at load, not at compile time. Both configurations build clean, and a real client fails at connect with `Sandbox violation: Access to method not allowed: ...`. **Only `SandboxTest` checks it** (`Content.IntegrationTests/Tests/Utility/SandboxTest.cs`, about 20 s): it starts its own client and calls `CheckSandboxed` on `Content.Client` and `Content.Shared`, which applies the whitelist *and* ILVerify. Pooled pairs load content through `TestingModLoader`, whose `SetEnableSandboxing` does nothing, so a `stackalloc` planted in `Content.Shared` leaves every other test green. Run it whenever you use an API content hasn't used before:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~SandboxTest"
```

It reports `Sandbox violation: Access to type not allowed: ...` for the whitelist (`RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml`, worth a search first) and `ILVerify: Instruction cannot be verified., method: ...` for IL. When the sandbox forces a worse implementation, leave `// TODO SANDBOX: <what you'd use, and why>`.

- **Refused by the whitelist**: `CollectionsMarshal`, `Unsafe`, `MemoryMarshal`, most of `System.Runtime.InteropServices`, reflection that writes, direct filesystem or process access. Also, from vendoring Concentus (`Content.Klovn.Concentus/README.md`): `System.Buffer` (use `Array.Copy`, which counts elements, not bytes); `System.Diagnostics.Debug` and `ConditionalAttribute` (use `DebugTools.Assert`, as `Solution.cs` notes); `System.Numerics.Vector<T>`; `System.Tuple` (value tuples are fine); `ArgumentNullException` (`ArgumentException` is fine); `string.Create(IFormatProvider, $"...")` (use `value.ToString(format, provider)`); a *field* of type `MethodImplOptions` (the attribute itself is fine, compiling to method flags).
- **ILVerify refuses** `stackalloc` (even into a `Span<T>`), `unsafe` code and pointers (use `new T[n]`, as `AtmosphereSystem.Gases.cs` notes), and a method that *returns* a `Span<T>`/`ReadOnlySpan<T>` (taking or slicing one locally is fine; return a count and hand out the array, as `KsTguiHazardStyleBox.StripeTriangles` does).
- **The compiler makes these, so the source never names them** (both hit in `KsTtsOpus.cs`): `"..."u8` literals, a pointer into static data (build the bytes once: `static readonly byte[] Magic = Encoding.ASCII.GetBytes("OggS")`); and a non-empty collection expression targeting `List<T>` (`Method([item])` compiles to `CollectionsMarshal.SetCount`; use `new List<T> { item }`; empty ones and array targets are fine). A `new()` generic constraint too, as `new T()` compiles to `Activator.CreateInstance<T>()`: have the subclass make the instance (an abstract `CreateOverlay()`).
- **Fine**: plain `Dictionary`, `Span`, `MathF`, and `System.Numerics`' `Vector2`/`3`/`4`. A struct copy is almost always cheaper than finding this out.

A run with a hundred-odd failures across unrelated features, all `SetUp : InvalidOperationException : Pool manager has not been initialized`, has **one** cause in pool setup, not in any test named: find the single result that failed with something else (`--logger "trx;LogFileName=..."` and group by message; `-v q` hides it).

### Client-sent floats: the server flushes NaN, but not everywhere, and never Infinity

`Content.Server/Entry/EntryPoint.cs` sets `SerializerFloatFlags.RemoveReadNan`, so every `float`, `double` and `Half` **field** of an incoming message reads NaN as `0` (`SafePrimitives.cs`). A NaN guard there is dead code, and a test sending NaN through the real path proves nothing.

| Not covered | Why | Do |
| --- | --- | --- |
| `±Infinity`, huge finite values | Only NaN is flushed | `Math.Clamp`, deltas too (`ControlRodInsertion + args.Change`) |
| `Vector2`/`3`/`4`, `Quaternion`, `Matrix3x2`, `Matrix4x4` | `NetMathSerializer.ReadFloatObject` reads them raw | NaN arrives intact |
| …so `EntityCoordinates`, `MapCoordinates`, `NetCoordinates` | Their position is a `Vector2` | `SharedTransformSystem.IsValid(coordinates)` |
| `UnsafeFloat`/`UnsafeHalf`/`UnsafeDouble` fields | Opt out by design | Validate them yourself |
| NaN made on the server | `0 / 0` from a zero-length direction | Guard the arithmetic |
| Integer conversions | `(int)1e30` and `(int)double.PositiveInfinity` are out of range | Bounds-check before casting to an index |
| The client | The flag is server-only | A server-sent NaN reaches the client |

`Vector2d` is two `double` fields, serialised one by one, so it *is* flushed. None of this checks meaning: an index can be past the end of a list, and a list can omit what it should count. Derive those, and enforce amounts, cooldowns and limits, where the server reads the message, never only in the UI that sends it.

### Only one system may subscribe to a given component and event pair

A second subscription for the same pair throws `Duplicate Subscriptions for comp=<Component>, event=<Event>` at **startup**, which breaks every test at SetUp and reads as "everything is broken". A partial system split over files can't subscribe to one pair twice: call into the second file from the one handler. Where unrelated systems need the same moment, the component's owner re-broadcasts it as an event of its own (`KsZLevelTransitEvents.cs`).

### Clean up on `ComponentShutdown`, not `EntityTerminatingEvent`

`EntityTerminatingEvent` fires only when the entity is deleted; `ComponentShutdown` also covers the component being removed, since deleting an entity shuts down each component. Undo a component's setup on its shutdown, and skip the parts that are pointless when the whole entity is going with `Terminating(uid)`. If another system already has that pair, call into yours from its handler (`PuddleSystem.OnPuddleCacheShutdown` for `KsHotPuddleSystem`).

### Tests

The `integration-testing` skill has the detail and the mechanics behind these; read it whenever you're unsure.

- **Prove each test fails.** Break what it covers (comment the fix out, unsubscribe the handler), rebuild, watch it go red with *your* message, restore. A test only ever seen passing is unchecked, and it is the reason nobody looks again.
- **Mechanism tests use their own `[TestPrototypes]` data.** YAML is where balance changes, often, by people who shouldn't have to think about tests. Use live prototypes only to test that content itself is set up right, and then derive every number from them.
- **Never copy a prototype's tuning into a test** (a loop count, a wait, a threshold). Derive it from the prototype, or loop until the thing happens, up to a generous cap.
- **Assert behaviour, not how a prototype is written**: "has this component" fails only on purpose. The YAML linter and `UninitializedSaveTest` already catch malformed prototypes.
- **PVS is off in pooled pairs**: a visibility test needs `OverrideCVar(Side.Server, CVars.NetPVS, true)`, an entity spawned after the state under test began, and a control that must *not* arrive.
- **A failed test in a batch can report as `Skipped`/`NotExecuted`** with only `Test was dirty-disposed.`: treat it as a failure and rerun it alone.

### Map saves: only `[DataField]`s persist

A map save writes a component's `[DataField]`s and nothing else, so a `[ViewVariables]`-only or plain field comes back at its default: fine for caches, a silent bug for an owner, a charge or a timer. Mobs are never written to map saves by default, so mob data gets leeway, but keep it as serialisable as is reasonable.

| Parameter | Effect | Use for |
| --- | --- | --- |
| `readOnly: true` | Read from YAML, **never written**, so never saved | Setup-only input consumed at init (`AccessComponent.Groups`, merged into `Tags` at `MapInit`) |
| `serverOnly: true` | The client neither reads nor writes it, and keeps the default | Server-only types inside shared prototypes or components (`ReactionPrototype.Impact`) |

**`PrototypeSaveTest.UninitializedSaveTest`** spawns every map-savable prototype on an **uninitialised** map, saves each component and diffs it against the prototype; nothing else checks this. Run it after adding or changing a component or prototype:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~UninitializedSaveTest"
```

It fails with `modifies component on spawn: <Comp>. Modified yaml: ...`. Fixes:

- A `[DataField]` written in `ComponentInit`/`ComponentStartup`/a constructor, or defaulting to something computed (`_random`, `CurTime`): set it in `MapInit`, or compute it lazily.
- Runtime state as a `[DataField]` that needn't survive a save: make it `[ViewVariables]` only.
- A non-nullable `EntityUid` written before `MapInit`: make it `EntityUid?`, null until then.
- A component added, removed or changed on spawn (anchoring at spawn makes `Physics` static, for one): put it in the prototype as it ends up, or do it in `MapInit`.

The test's own exclusions (`Metadata`/`Transform`/`Fixtures`, test prototypes, the `DEBUG` suffix) are not a fix.

### Removing or deleting while enumerating: use the deferred forms

Removing a component inside an enumeration of it, or deleting an entity the enumeration can reach, mutates the storage being walked. Use the deferred forms rather than gathering uids first:

| Immediate | Deferred | What waits |
| --- | --- | --- |
| `RemComp` | `RemCompDeferred` | Only removal from storage; `ComponentShutdown` runs **now** |
| `Del` | `QueueDel` | **Everything** |
| `PredictedDel` | `PredictedQueueDel` | As `QueueDel`, for predicted shared code |

Both queues drain at the end of the tick (`EntityManager.TickUpdate`), and calling either twice is harmless. Each leaves a trap until then:

- **A `RemCompDeferred`'d component is shut down but still stored**: enumerations and `HasComp` still find it, so skip it (`if (component.LifeStage > ComponentLifeStage.Running) continue;`) or you undo what its shutdown cleaned up.
- **A `QueueDel`'d entity is fully alive**: `TerminatingOrDeleted` says `false` and queries return it. Ask `EntityManager.IsQueuedForDeletion(uid)`.

Use the immediate forms only when nothing up the call stack is iterating what you remove *and* the caller needs it gone before continuing.

### Reparenting and map work inside engine callbacks

Some engine events are raised mid-operation, with the engine's iteration, broadphase or chunk structures in flight. Reparenting, moving a grid or deleting a map from one is reentrant mutation of the caller, and it takes the server down rather than throwing something catchable. `GridFixtureSystem`'s split, reached by editing many tiles at once, is the known case. Queue the work into a set from the callback and drain it in `Update`, through a second list, because acting on an entry can raise the very events that queue into the set (`KsZLevelPhysicsSystem`'s deferred transit checks; `PuddleSystem`'s one-puddle-per-tile merge).

## 7. Localisation

The server runs in `en-US`; each client picks its own culture. **Text formatted on the server doesn't follow the recipient's language.** So:

- Popups carry a **`KsPopupMessage`** with deferred arguments, never `Loc.GetString(...)` (which sends formatted text) or a pre-formatted string.
- Prototype names and descriptions come from **`ContentLocalizationManager`**, not `prototype.Name`.
- Every new prototype gets an **`ent-` entry in both `en-US` and `ru-RU`**, under `Resources/Locale/<culture>/_KS14/`, with the prototype's path as a comment above it.
- Keep fixes in content; never modify `RobustToolbox` to work around the engine's metadata cache.
