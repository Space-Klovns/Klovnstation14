# Contributing to Klovnstation 14

Coding conventions for this repo. Written for coding agents first, humans second — precise over chatty, examples over prose.

**Quick reference:**
- New code → `Content.<project>/_KS14/<Feature>/...` (see §2).
- Fork-owned files (`_KS14/`, `.Klovn`/`.Ks` filenames, or `Ks`/`KS14` namespaces/types) need no provenance headers (see §3).
- Editing existing upstream code *outside* `_KS14/` → mark it with `// KS14:` / `# KS14:` (see §3).
- **Marking a change on a single line → `/* KS14: ... */` sitting at the change itself**, not a trailing `//` at the end of the line (see §3).
- Otherwise follow upstream SS14 conventions, plus the local rules in §4.
- Before you ship: the traps in §6 that fail *silently* — no build error, no log, no failing test.
- Client-culture popups and prototype names/descriptions → use the KS localisation APIs in §7; defer popup arguments until the recipient formats them.

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

Where possible, give each feature its own subfolder rather than leaving its files loose in a shared one (`Entities/Effects/SupplyPod/`, not `Effects/podglow.yml`), and mirror it in `Resources/Locale/`.

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

When you edit an **existing upstream file outside** `_KS14/` (in upstream SS14 / other forks / `_Manifest` / `_sin` / etc.), mark each logical change inline:

- **Edits to an existing upstream file** — mark every logical change inline (forms below).
- **Adding methods/members to an existing class** — make the class partial (comment why), and add the members in a same-folder file named `*.Klovn.Feature.cs`.
- **Fork-owned files need no provenance headers.** This includes files under `_KS14/`, files named `*.Klovn.*` or `*.Ks.*` (case-insensitive), and files whose names or declared namespaces/types use the fork's `Ks`/`KS14` prefix. Their location, name, or namespace already identifies them. Do not add generic fork-origin comments to these files. New files outside `_KS14/` also need no blanket provenance header; prefer `_KS14/` unless extending an existing upstream class or tree is the appropriate home.

Inline markers in existing upstream files make our changes easy to spot on the next upstream merge. Always preserve the original upstream value in the comment: swap `100 -> 50` today, and a later change to that same line becomes `100 -> 30` (not `50 -> 30`). Swap `KS14` for another fork's tag (e.g. `Goobstation`) when porting from that fork instead of writing net-new code.

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
- **Referencing a type from a new namespace only once** — don't add a `using` for a single reference; write the type fully qualified at the use site instead, with the change marked there. One edit at the point of use is one fewer line to conflict on during upstream merges. Add the `using` (per the form above) only once the namespace is referenced more than once in that file:
  ```csharp
  // do this
  if (Content.Client._KS14.Portal.KsPortalRenderSystem.PassActive /* KS14: added */)
      return false;

  // not this - a 'using' line for one reference
  using Content.Client._KS14.Portal; // KS14
  // ...
  if (KsPortalRenderSystem.PassActive /* KS14: added */)
      return false;
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

### Partial prototypes: change an upstream prototype without editing its file

A partial prototype has the same kind and id as an existing one, and is merged into it instead of failing the load as `Duplicate ID`. It's the way to add to, or take from, an upstream prototype (a sound collection, a loadout group, an entity's components) from a fork-owned file, with no `# KS14:` edit to the upstream file and so nothing to conflict with on the next merge.

**A file is only partial if it's registered.** Anywhere else, a second `LobbyMusic` throws `PrototypeLoadException: Duplicate ID` and the load fails. Register the file, or a whole directory, in `PartialPrototypePaths` in `Content.Shared/_KS14/Entry/KsEntryPoint.cs`, which runs before prototypes load on the client, server and YAML linter alike, and which the packaged localization cache reads too:

```csharp
public static readonly ResPath[] PartialPrototypePaths = [
    new("/Prototypes/_KsModule/Lobby/soundcollection.yml"),   // a file
    new("/Prototypes/_KS14/Partials/"),                       // or a directory, with no extension
];
```

Keep the registrations narrow: inside a partial path, an id that *accidentally* matches an existing prototype silently merges into it rather than erroring. Don't register whole trees like `_KS14/` or `_KsModule/`.

Then write only the fields you're changing. Sequences are appended to, mappings are combined key by key, and plain values are replaced:

```yaml
# adds a track to upstream's collection; every upstream track stays
- type: !PartialOnly soundCollection
  id: LobbyMusic
  files:
  - /Audio/_KsModule/LobbyMusic/melcom-cyberpunk.ogg
```

- **`!PartialOnly`** on the `type` — if the original doesn't exist (upstream renamed or removed it), do nothing, instead of quietly creating a new prototype from the fragment. Use it whenever you're modifying something, which is almost always.
- **`- !Remove X`** in a sequence removes `X`; **`key: !Remove`** removes a mapping key, and **`key: !Remove 1`** removes it only if its value is `1`. **`- !Remove type: SomeComponent`** removes a component from an entity.
- **`field: !Clear`** empties a sequence or mapping before your entries are added to it.
- **`- !Index:0 X`** inserts at a position (negative counts from the end, and it's clamped to the list). Combine it with `!Remove` to reorder.
- **`- !CombineIndex:0 key: value`** merges into the element *already at* that index, rather than adding a new one.

The full list of examples is on `IPrototypeManager.PartialDirectory` (`RobustToolbox/Robust.Shared/Prototypes/IPrototypeManager.cs`). Partials apply after every non-partial file, so it doesn't matter where the original lives. Prefer a partial to a `_KsModule_ReplacedPrototypes` replacement whenever you're changing part of a prototype, since a replacement restates the whole thing and drifts from upstream. Cover a partial with a test that the merged prototype has your change *and* still has upstream's data (`KsLobbyMusicPartialTest`), since a partial that silently stopped applying looks exactly like the unmodified upstream prototype.

**One exception to upstream**: `codebase-organization` says game-code folders live directly under `Content.Client/Shared/Server`. We override this for **new fork code only** — new code goes under `_KS14/` per §2. Upstream files edited in place keep their upstream layout and carry `// KS14:` markers per §3. Don't relocate or reformat **upstream** code just to bring it into convention unless you're already changing it for another reason — every such move is a merge conflict waiting for the next upstream pull, paid for nothing. This is about upstream files specifically, not a general licence to leave things alone: fork code under `_KS14/` is ours, merges cleanly, and is fair game to tidy whenever it has drifted from the rules below.

### Local rules on top of upstream

**Casting/coercion (C#)** — always cast explicitly with the target type in parentheses:
```csharp
var myFloat = (float)GetMyInt();       // do this
float myFloat = GetMyInt();            // not this
```

**Convert to and from `FixedPoint2` with its own conversions (C#)** — reagent quantities are `FixedPoint2`, stored to 0.01u. When a `float` has to become one, or come back, use `FixedPoint2.New(float)` and the explicit `(float)` cast. Don't write a helper that rounds a float to solution precision by hand:
```csharp
// do this - FixedPoint2.New truncates to the 0.01u the solution stores
var used = (float)FixedPoint2.New(MathF.Min(availableFoam, foamPerTile));

// not this - a hand-rolled copy of what FixedPoint2 already does
private static float FloorToSolutionUnits(float units) => MathF.Floor(units * 100f) / 100f;
var used = FloorToSolutionUnits(MathF.Min(availableFoam, foamPerTile));
```
The same applies in the other direction: `(float)quantity`, not `quantity.Float()`. A copy of the rounding drifts from `FixedPoint2`'s own. `FixedPoint2.New` applies a float epsilon before truncating, and the hand-rolled version doesn't. So the amount taken and the amount that actually lands in the solution can disagree by 0.01u.

**Name every optional argument (C#)** — an optional parameter is always passed with its name, so the call site says what the value *means* instead of making the reader go read the signature. Required parameters stay positional:
```csharp
// do this
_entityLookupSystem.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true);
dependencyCollection.InjectDependencies(overlay, oneOff: true);

// not this - what is 'true'?
_entityLookupSystem.FindGridsIntersecting(mapId, bounds, ref _grids, true);
dependencyCollection.InjectDependencies(overlay, true);
```

**No string literals as prototype ids (C#)** — everywhere, integration tests included. The `RA0033` analyzer rejects a literal passed to `Index`/`TryIndex` and similar id parameters. Declare a typed id once and use that:
```csharp
private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";   // do this
var water = protoMan.Index(WaterReagent);

var water = protoMan.Index<ReagentPrototype>("Water");                      // not this
```
For an id that exists only in a test's `[TestPrototypes]` YAML, make that field non-`static` (`private readonly ProtoId<T> _id = "..."`): the YAML linter validates only static id fields against the real prototypes, and an instance field still satisfies `RA0033`.

Entity ids use `EntProtoId` rather than `ProtoId<EntityPrototype>`. `EntProtoId<T>` also requires the entity to have a `T`: as a `[DataField]`, the YAML linter rejects a prototype without one (`public EntProtoId<InstantActionComponent> Action;`).

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

**`Entity<T>`, not a uid and a component side by side (C#)** — an entity and its component travel together as one `Entity<T>`, never as an `EntityUid` parameter next to a component parameter. The pair can't drift apart (a component handed in with the wrong uid), and the call site reads as one thing:
```csharp
// do this
[SubscribeLocalEvent]
private void OnShutdown(Entity<NpcHolderComponent> entity, ref ComponentShutdown args) { }

public bool TryStartAttack(Entity<NpcHolderComponent?> entity, string attackId)
{
    if (!_holderQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false))
        return false;
    // ...entity.Comp from here on
}

// not this
private void OnShutdown(EntityUid uid, NpcHolderComponent component, ComponentShutdown args) { }
public bool TryStartAttack(EntityUid uid, string attackId, NpcHolderComponent? component = null) { }
```
- **A component the caller may not have** is `Entity<T?>`, resolved inside. A bare `EntityUid` converts to it implicitly, so callers that only have the uid still just pass it.
- **Several components** are `Entity<T1, T2>` (up to the arity the engine provides), not one in the entity and the rest as extra parameters.
- **A method that never touches a component** takes a plain `EntityUid`: an `Entity<T>` there promises a dependency that isn't real.
- **Handlers for by-value events** (a `class` event, not a `[ByRefEvent]` struct, e.g. `DamageChangedEvent`) are the
  exception: the engine only has an `Entity<T>` handler for `ref` events, and the generator rejects anything else
  (`RA0054`). Keep `(EntityUid uid, TComp component, TEvent args)` there, say why in a comment, and pass the pair on
  as an `Entity<T>` from the first call.
- **Upstream signatures** stay as they are unless you're already changing them (see the note on relocating upstream code in §4).

Its members, so you don't need to go and read `RobustToolbox/Robust.Shared/GameObjects/Entity.cs`. Every arity, from
`Entity<T>` to `Entity<T1, …, T8>`, is a mutable `record struct`:

| Member | `Entity<T>` | `Entity<T1, T2, …>` |
| --- | --- | --- |
| The uid | `.Owner` | `.Owner` |
| A component | `.Comp` | `.Comp1`, `.Comp2`, … (no `.Comp`) |
| Deconstruct | `var (uid, component) = entity;` | `var (uid, component1, component2) = entity;` |
| Build one | `(uid, component)`, or `new Entity<T>(uid, component)` | `(uid, component1, component2)` |
| As `T?` | `entity.AsNullable()` | `entity.AsNullable()`, which makes every component nullable |

Conversions are implicit:
- **To `EntityUid`**, so an `Entity<T>` passes straight to anything that takes a uid. Write `entity.Owner` anyway
  where the call would otherwise read ambiguously.
- **To each component type**: `TComp component = entity;` works, but `entity.Comp` says what you mean.
- **From `EntityUid` to the all-nullable form** (`Entity<T?>`, `Entity<T1?, T2?>`), with the components left null for
  the callee to resolve.
- **Between arities:** `Entity<T1, T2>` narrows to `Entity<T1>`, dropping the rest, and `Entity<T1>` widens to
  `Entity<T1, T2?>`, with the added component null.

Resolve a nullable one in place. An `EntityQuery<T>` takes the whole entity by `ref`; `EntitySystem.Resolve` has no
such overload, so pass the fields, which are fields and so take `ref`:
```csharp
if (!_holderQuery.Resolve(ref entity, logMissing: false))   // same as Resolve(entity.Owner, ref entity.Comp, ...)
    return false;

if (!Resolve(entity.Owner, ref entity.Comp1, ref entity.Comp2))
    return false;
```

Because it's a struct, a copy of it is independent. Assigning `.Comp` on a copy changes nothing for the caller,
though the component it points to is still the same object.

**Finding engine code:** everything under `Robust.{Shared,Client,Server}/GameObjects/`, subfolders included, is in
the flat namespace `Robust.{Shared,Client,Server}.GameObjects` (see the `!NO_FOLDER_NAMESPACES` file there). So a
type's namespace says nothing about its folder: search by type name, not path.

**Engine helpers whose behaviour isn't in their name** — each of these took reading the engine to find out:
- **`Resolve` logs by default.** `logMissing` defaults to `true`, and a miss logs an *error* with a stack trace
  (`EntitySystem.Resolve.cs`). An integration test fails on an error log, so pass `logMissing: false` wherever the
  component is legitimately optional. On `EntityQuery<T>`, `CompOrNull(uid)` is the null-returning form, and
  `Comp(uid)` throws.
- **`EntityQueryEnumerator` skips paused entities; `AllEntityQueryEnumerator` doesn't.** Anything on a paused map
  (mapping, a test map not yet initialised) is invisible to the first. Pick by whether a paused entity should be
  touched, not by which name you saw last.
- **Read a prototype's component without spawning it** with
  `ProtoMan.Index(id).TryComp<TComp>(out var component, Factory)`. The instance is the prototype's own, shared by
  every reader, so treat it as read-only.
- **A `TimeSpan` `[DataField]` reads a plain number as seconds** (`cooldown: 2.5`), or one number with a single
  unit suffix: `s`, `m`, `h` (`1.5m`). There is no `ms`, and no combining (`1h30m` fails); use `0.25` instead.
  See `TimeSpanExt.TryTimeSpan`.
- **`[AutoPausedField]`** (with `[AutoGenerateComponentPause]` on the component) shifts the field forward by however
  long the entity was paused. It takes `TimeSpan`, `TimeSpan?` and `Dictionary<TKey, TimeSpan>`, and on a field
  that's also `[AutoNetworkedField]` it dirties the component for you. Use it on every absolute end time, or a
  paused entity's timers expire the moment it unpauses.
- **`ValueList<T>`** (`Robust.Shared.Collections`) is a `List<T>` held in a struct, for scratch lists that live in a
  method. Two traps follow from it being a struct: a copy shares the backing array but not the count, so pass it by
  `ref`; and it doesn't throw if changed while being enumerated, unlike `List<T>`.

**`Ks` prefix (IDs & type names)** — when a new prototype ID or type name could plausibly collide with an upstream name (present or future), prefix it with `Ks`: `KsCCVars` (a fork-only cvars class, deliberately not inheriting upstream `CCVars`), `KsBlack`, `KsCatwalkIron` (colors and structure variants — generic vocabulary upstream already uses or could use). Skip the prefix when the name is already distinctive enough not to collide — `Anchorless`, `ArcFlash`, `ComplexShove` — the `_KS14/` folder already marks provenance there. This is a judgment call, not a mechanical rule: ask "would upstream plausibly ship something under this exact name?" If yes, prefix it.

**No `ks`/`Ks` prefix on path-referenced files inside `_KS14/`.** A file found by its path, such as a shader, texture, audio file or font, is already namespaced by the `_KS14/` folder in that path. A prefix on its filename only repeats it:
```yaml
path: "/Textures/_KS14/Shaders/lobby_contrast.swsl"     # do this
path: "/Textures/_KS14/Shaders/ks_lobby_contrast.swsl"  # not this - _KS14/ already says it's ours
```
This covers resource files referenced by path and nothing else. Prototype IDs, C# type names (and the `.cs` files named after them), locale keys and style classes still follow the `Ks` prefix rule above, because they share one global namespace with upstream whatever folder they sit in.

**`"metaAtlas": false` for large sprites (RSI)** — by default every `.rsi` is packed into the sprite atlas at startup and so held in VRAM for the whole session. `"metaAtlas": false` in its `meta.json` leaves it out and loads it on demand instead. Use it for large-resolution sprites.

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

**Act when a value changes, not every tick in case it did (C#)** — whatever has to follow a value (a fixture sized to a width, a cache, a sprite state) is updated by the code that changes the value: a setter, an event handler, `AfterAutoHandleState`, a cvar subscription. Polling for the change in `Update` costs every tick for every entity, forever, and still reacts a tick late.

```csharp
// do this - the sensor is resized once, when the width changes
public void SetWidth(Entity<KsPortalViewComponent?> entity, float width)
{
    // ...resolve, set Width, Dirty...
    RefreshSensor((entity.Owner, entity.Comp));
}

// not this - every portal's sensor is compared against its width on every tick
public override void Update(float frameTime)
{
    var query = EntityQueryEnumerator<KsPortalViewComponent>();
    while (query.MoveNext(out var uid, out var viewComponent))
    {
        if (!SensorMatches(uid, viewComponent.Width))
            RefreshSensor((uid, viewComponent));
    }
}
```

This only holds while the setter is the *only* way in: `[Access]` the field to the system (above), and close off VV, the one writer `[Access]` can't stop (below).

**Cheapest rejection first (C#)** — in a filter loop, order the checks so the ones that reject most, cheaply, run first: a field on a component you already have before a `HasComp`, and a `HasComp` before a fixture or layer loop. A broad lookup returns mostly things you skip (decals, cables, pipes), and every check ahead of the one that rejects them runs for nothing.

**ViewVariables (VV) (C#)** — the engine's live inspector. Admins and developers open any entity, component or system in the VV window, or use `vv`/`vvread`/`vvwrite`, to read and change its fields mid-round. Every `[DataField]` and `[ViewVariables]` member shows up there, writable by default, so VV is a writer of your component that your code does not control. It writes by reflection, so `[Access]` does not stop it, and the server is told nothing beyond the component being dirtied. When a field must only change through a setter:

1. `[ViewVariables(VVAccess.ReadOnly)]` limits the VV window to viewing it. The window writes server components by reflection and ignores custom paths, so read-only is the only way to keep it out.
2. Register a VV path with the field's name whose setter calls yours. `vvread`/`vvwrite` resolve a registered path before falling back to the field, so the command still works, through your setter.

```csharp
// The component.
[DataField, AutoNetworkedField, Access(typeof(KsPortalTravelSystem)), ViewVariables(VVAccess.ReadOnly)]
public float Width = 1f;

// The system. The path is removed again in Shutdown: the VV manager outlives the system, and its lambdas would keep it alive.
[Dependency] private IViewVariablesManager _viewVariablesManager = default!;

_viewVariablesManager.GetTypeHandler<KsPortalViewComponent>().AddPath(
    nameof(KsPortalViewComponent.Width),
    (_, component) => component.Width,                             // getter: (uid, component)
    (uid, width, component) => SetWidth((uid, component), width)); // setter: (uid, value, component?) - hence Entity<T?> on SetWidth

_viewVariablesManager.GetTypeHandler<KsPortalViewComponent>().RemovePath(nameof(KsPortalViewComponent.Width));
```

The command is `vvwrite /entity/<uid>/KsPortalView/Width 3`: the local `EntityUid` number, the component's name without its `Component` suffix, then the path. Upstream registers a path the same way for `EventHorizonComponent.TargetConsumePeriod`, but leaves the field writable in the window, which still bypasses it. Test it by running the command through `IConsoleHost` and asserting on what it keeps in step (`KsPortalTravelTest.TestVvWriteOfWidthResizesTheSensor`).

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
//      (likewise BoundUserInterface -> EntMan, Overlay -> OverlayManager, resolved in its constructor)
public sealed partial class KsSomeCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entityManager = default!;
    // ...then _entityManager.HasComponent<MapGridComponent>(uid)
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

**The same goes for the queries `EntityManager` already holds.** `EntityManager` (which every `EntitySystem` has, as above) keeps public `TransformQuery` and `MetaQuery` fields. Use those; don't inject or fetch your own `EntityQuery<TransformComponent>` or `EntityQuery<MetaDataComponent>`:

```csharp
// do this
var transformComponent = EntityManager.TransformQuery.GetComponent(uid);

// not this - a second query for the one EntityManager already keeps
[Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;
var transformComponent = _transformQuery.GetComponent(uid);
```

`Transform(uid)` and `MetaData(uid)` remain fine where you only need the component; reach for the query when you want `TryComp`, `Resolve` or `HasComp`.

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

**Per-entity state is a component, not a system dictionary (C#)** — anything a system remembers *about* an entity belongs on that entity, in a component read through an injected `EntityQuery<T>`. A `Dictionary<EntityUid, T>` held by the system is a parallel entity store: nothing removes an entry when its entity is deleted, so it leaks or needs its own cleanup; VV cannot see it; and it has to be cleared, epoch-stamped or pruned by hand. As a component, the data dies with its entity and shows up in VV. `[Access]` the component to the system that owns it (see above).

```csharp
// not this - a cache of light levels keyed by target, cleared every tick
private readonly Dictionary<EntityUid, float> _lightLevels = new();

// this - the cache lives on the target; the tick it was computed on says whether it is still good
[RegisterComponent, Access(typeof(NpcLightDetectionSystem))]
public sealed partial class NpcLightLevelCacheComponent : Component
{
    [ViewVariables] public float Level;
    [ViewVariables] public GameTick ComputedTick;
}

[Dependency] private EntityQuery<NpcLightLevelCacheComponent> _lightLevelCacheQuery = default!;

if (_lightLevelCacheQuery.TryComp(targetUid, out var cacheComponent) && cacheComponent.ComputedTick == currentTick)
    return cacheComponent.Level;
// ...compute, then EnsureComp<NpcLightLevelCacheComponent>(targetUid) and store it.
```

**No `static` mutable state on systems (C#)** — a system is per server (or client), but a `static` field is per process, and integration tests run several pooled servers in one process, each with its own clock. A static timer set by one server holds back every other server's updates. `PuddleSystem`'s `_nextTileEffectUpdate` was static, so in tests a fuel puddle heated past its ignition point never caught fire through the update loop. The existing tests only lit fuel that was spilled already hot, and that path doesn't use the timer, so they passed anyway. `static readonly` constants and shapes are fine. Anything that changes at runtime is an instance field.

This is about state *about* an entity. Two things that look similar are fine as they are:
- **Transient work lists** — a set of uids queued this tick and drained in `Update`, like the deferred-check set in §6's reparenting entry. It is a to-do list, not state.
- **Collections on a component** — a `Dictionary<EntityUid, T>` *inside* a component (an NPC's sightings keyed by target) is data belonging to the entity that owns the component, which is what components are for.

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

The generator only runs in projects that import it. `Content.Client`, `Content.Server`, `Content.Shared` and `Content.IntegrationTests` each carry `<Import Project="..\RobustToolbox\MSBuild\Robust.EntitySystemSubscriptionsGenerator.targets" />` for exactly this reason — without it the attribute still compiles, nothing is generated, and **every converted subscription silently stops firing** with no build error to point at it. Any other project that wants attribute subscriptions needs the same import, and `Robust.Shared.Analyzers` imported globally as well (`<Using Include="Robust.Shared.Analyzers" />`, or a `GlobalUsings.cs`): the attributes live there, and the code the generator emits uses `[MustCallBase]` unqualified, so without it that generated file fails to build.

**Popups predict themselves (C#)** — in shared code, call `PopupEntity`, `PopupCoordinates` or `PopupCursor` directly. The predicting client shows the popup the moment it runs, and the server's copy is matched against it rather than shown a second time; everyone else gets the server's. There is no separate "predicted" call to reach for — `PopupPredicted`, `PopupPredictedCursor`, `PopupPredictedCoordinates` and `PopupClient` are `[Obsolete]` wrappers kept for old callers.

The trap is the `recipient` argument, which is **a filter, not a prediction hint**: whoever it names is the only player who sees the popup.

```csharp
// everyone who can see the dodger sees it, predicted for whoever caused it
_popupSystem.PopupEntity(message, dodgerUid, type: PopupType.Small);

// only the shooter sees it - the dodger and every bystander see nothing
_popupSystem.PopupEntity(message, dodgerUid, shooterUid, type: PopupType.Small);
```

So when replacing an obsolete call, keep the audience it had: `PopupPredicted(message, uid, recipient)` showed the popup to everyone and becomes `PopupEntity(message, uid)`, while `PopupClient(message, uid, recipient)` showed it to the recipient alone and becomes `PopupEntity(message, uid, recipient)`.

**Area guides** — NPC behaviour, ranged combat, overlays, lighting, rendering, sprites, shaders, client UI and integration testing each
have an agent skill under `.claude/skills/` (agents see the list automatically). Read the matching one before working
in that area. To port an SS13 sprite, run the `/dmi-to-rsi` command: it converts a `.dmi` into an `.rsi`, copyright line included. To port SS13 code or read DM, use the `dreammaker` skill.

**Engine version** — this fork tracks a pinned `RobustToolbox` submodule, currently v291.0.1. When bumping it, read [RELEASE-NOTES.md](https://github.com/space-wizards/RobustToolbox/blob/master/RELEASE-NOTES.md) for every intervening version and check whether upstream SS14 already shipped the content-side fix — porting their commit is cheaper and keeps future merges clean. A bump is also one of the main ways new debug assertions arrive, so run the tests in `Debug` afterwards as well as building `Release` (§5).

## 5. Build configurations, and what each one catches

`Release` and `Debug` fail on **disjoint** sets of problems. Neither is a superset of the other, so a change is only green when both are.

| | `Release` | `Debug` / `DebugOpt` |
| --- | --- | --- |
| `TreatWarningsAsErrors` | **on** (`MSBuild/Content.props`) | off |
| `DebugTools.Assert`, `DebugTools.AssertNotNull` | **compiled out** | live |

- **Build `-c Release`.** It is the only configuration that reproduces the warnings-as-errors failures CI reports; `Debug` will happily build code that fails CI.
- **Read build output by `error CS` and the `Passed!`/`Failed!` line**, not by grepping `error`: obsolete-API warnings contain it.
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

### Integration-test compile errors CI catches

- **`CS8632`: nullable annotations without `#nullable enable`.** `Content.IntegrationTests` has nullable *disabled* by default, so `GasMixture? air` or `Entity<TransformComponent?>` in a test is a warning, and therefore a CI error. Either drop the `?` (it means nothing there anyway) or put `#nullable enable` at the top of the file and fix whatever new warnings that surfaces.
- **`RA0033`: literal prototype ids.** Tests are not exempt from the no-literal-ids rule in §4 (*Local rules on top of upstream*); ids defined only in `[TestPrototypes]` go in non-static `ProtoId` fields so the YAML linter skips them.

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

**Only one test will tell you** (see `SandboxTest` below): everything else loads content without the check
and passes. So a violation doesn't break a test run in any way you'd notice. It breaks a real client, at
connect, with the error above.

A related shape: when a run reports a hundred-odd failures across unrelated features, all of them
`SetUp : System.InvalidOperationException : Pool manager has not been initialized`, something failed while the
integration pool was being set up, and it has **one** cause that isn't in any of the tests named. Find the single
result that failed with something else. Running with `--logger "trx;LogFileName=..."` and grouping the results by
message is the quick way to see that shape; `-v q` prints only the total and hides it entirely.

The offenders are mostly the low-level performance conveniences: `CollectionsMarshal`, `Unsafe`,
`MemoryMarshal`, most of `System.Runtime.InteropServices`, reflection that writes, and anything
touching the filesystem or process directly. Plain `Dictionary`, `Span`, `MathF` and `System.Numerics`'
`Vector2`/`Vector3`/`Vector4` are all fine. If you are reaching for something to avoid a dictionary lookup or a
struct copy in rendering code, the copy was almost certainly cheaper than finding this out:

```csharp
// not this - compiles everywhere, refused at load
ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_map, key, out _);
entry.Value += 1;

// this - a struct copy, and one the sandbox allows
_map.TryGetValue(key, out var entry);
entry.Value += 1;
_map[key] = entry;
```

**`SandboxTest` is the check; an ordinary integration test is not.** Pooled test pairs load content through
`TestingModLoader`, whose `SetEnableSandboxing` does nothing, so every other test passes with a violation in the
build. A `stackalloc` planted in `Content.Shared` leaves a normal pooled test green. Only
`Content.IntegrationTests/Tests/Utility/SandboxTest.cs` runs the real checker: it starts its own client and calls
`CheckSandboxed` on `Content.Client` and `Content.Shared`, which applies the whitelist (`Sandbox.yml`) *and*
ILVerify. It takes about 20 seconds, so run it whenever you add an API you haven't seen content use before:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~SandboxTest"
```

A failure names each problem directly: `Sandbox violation: Access to type not allowed: ...` for the whitelist, and
`ILVerify: Instruction cannot be verified., method: ..., Offset IL_...` for IL. The whitelist is
`RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml`, and it's worth a search before you rely on a BCL type.

The rules come from two places, so nothing about a type's namespace predicts them:

- **ILVerify** rejects `stackalloc` (even into a `Span<T>`), `unsafe` code and pointers. Content already notes
  this (`AtmosphereSystem.Gases.cs`). Use `new T[n]` instead.
- **The whitelist** is per type and often per member. Besides the ones above, these came up vendoring Concentus
  (`Content.Klovn.Concentus/README.md`), and none of them warns at build time:
  - `System.Buffer` (`BlockCopy`): use `Array.Copy`, which counts elements, not bytes.
  - `System.Diagnostics.Debug` and `ConditionalAttribute`: use `DebugTools.Assert`, as `Solution.cs` notes.
  - `System.Numerics.Vector<T>`.
  - `System.Tuple`: value tuples are fine.
  - `ArgumentNullException`: `ArgumentException` is fine.
  - A *field* of type `MethodImplOptions`. `[MethodImpl(MethodImplOptions.AggressiveInlining)]` itself is fine,
    since it compiles to method flags rather than a type reference.

Two more are made by the compiler, so the source never names what it refuses. Both came up in the TTS Opus code
(`KsTtsOpus.cs`):

- **`"..."u8` literals** fail ILVerify. They compile to a pointer into the assembly's static data (`Found address of
  '<PrivateImplementationDetails>...', Expected Native Int`). Build the bytes once instead, e.g.
  `static readonly byte[] Magic = Encoding.ASCII.GetBytes("OggS")`.
- **A non-empty collection expression that targets `List<T>`** (`Method([item])`, where the parameter is a
  `List<T>`) compiles to `CollectionsMarshal.SetCount`, which is not whitelisted. Use `new List<T> { item }`.
  Empty ones (`= []`) and ones that target arrays are fine.

### Client-sent floats: the server flushes NaN, but not everywhere, and never Infinity

`Content.Server/Entry/EntryPoint.cs` sets `IRobustSerializer.FloatFlags = SerializerFloatFlags.RemoveReadNan`.
From then on the server reads every `float`, `double` and `Half` **field** of an incoming network message (BUI
messages, network events, anything through `NetSerializer`) with NaN replaced by `0`
(`RobustToolbox/Robust.Shared/Serialization/SafePrimitives.cs`). So a handler for
`SetTemperatureMessage(float Temperature)` never sees NaN, and a NaN guard there is dead code. A test that sends NaN
through the real path proves nothing either: the value arrives as `0`.

What it does **not** cover:

| Not covered | Why | What to do |
| --- | --- | --- |
| `±Infinity`, and huge finite values | Only NaN is flushed. | `Math.Clamp` handles both. Clamp a *delta* too (`ControlRodInsertion + args.Change`), or one message jumps the whole range. |
| `Vector2`/`3`/`4`, `Quaternion`, `Matrix3x2`, `Matrix4x4` | `NetMathSerializer.ReadFloatObject` reads them raw, before the NaN flush. | NaN arrives intact. |
| ...so `EntityCoordinates`, `MapCoordinates`, `NetCoordinates` | Their position is a `Vector2`. | `SharedTransformSystem.IsValid(coordinates)` rejects non-finite positions. |
| Fields typed `UnsafeFloat`/`UnsafeHalf`/`UnsafeDouble` | Opt out by design. | Validate them yourself. |
| NaN made on the server | `0 / 0` from a zero-length direction, for example. | Guard the arithmetic (a throw at your own position has no direction). |
| Integer conversions | `(int)1e30` and `(int)double.PositiveInfinity` are out of range for any array. | Bounds-check the floating value before casting it to an index. |
| The client side | The flag is only set on the server. | A server-sent NaN reaches the client unchanged. |

`Vector2d` is two `double` fields and is serialized field by field, so it *is* flushed.

None of this checks *meaning*. A finite value can still be out of range, an index can be past the end of a list,
and a list the client sends can omit what it should count. The server derives those from its own state. The same
applies to amounts, cooldowns and limits: enforce them where the server reads the message, never only in the UI that
sends it.

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

### Clean up on `ComponentShutdown`, not `EntityTerminatingEvent`

`EntityTerminatingEvent` is raised only when the entity is deleted. When just the component is removed, it never
fires. `ComponentShutdown` covers both cases, because deleting an entity shuts down each of its components in turn.
So cleanup that undoes what a component set up goes on its shutdown. If part of that cleanup is pointless when the
whole entity is going, skip that part with `Terminating(uid)`, the `EntitySystem` proxy for the entity's life stage.

```csharp
// not this - removing PuddleComponent alone leaves every toucher still holding the puddle
[SubscribeLocalEvent]
private void OnPuddleTerminating(Entity<PuddleComponent> entity, ref EntityTerminatingEvent args)

// this - runs on removal and on deletion alike
[SubscribeLocalEvent]
private void OnPuddleShutdown(Entity<PuddleComponent> entity, ref ComponentShutdown args)
```

If another system already has that component's shutdown, call into yours from its handler (see above), as
`PuddleSystem.OnPuddleCacheShutdown` does for `KsHotPuddleSystem`.

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

The opposite also happens: a test that *did* fail can be reported as not having run. In a `dotnet test` run with
several tests, a failed `GameTest` can show as `Skipped` or `NotExecuted`, with only `Test was dirty-disposed.` in the
trx and the console. Treat that as a failure. Rerun that test alone with `--logger "console;verbosity=detailed"` to see
the assertion that actually failed.

### A test bound to today's tuning breaks on the next balance pass

A test that copies a prototype's numbers into itself (a loop count, a wait, a threshold) is checking those numbers, not
the mechanic. When someone retunes the prototype, it fails with no bug present, or, worse, it keeps passing while no
longer exercising what it is named for. One balance pass on the foam blanket (`maxFoam` 1.5 → 3.5, `latentHeat` 12000
→ 45000) and a shorter shot lifetime broke six tests this way:

- `for (var i = 0; i < 30; i++) Deploy(...)` filled a 1.5u blanket and no longer filled a 3.5u one.
- `WaitRunTicks(TicksPerSecond * 4)` was enough for a fire to boil a full blanket off, until it took 17 seconds.
- "Boils at least half its `PuddleBoilRate`" assumed the rate was the limit; with more latent heat, the puddle's
  spare heat ran out first.

Derive what the test depends on from the prototype it spawns, and state the mechanism as the bound:

```csharp
// do this - fills the blanket whatever it holds
for (var i = 0; i < 1000 && blanketSystem.Deploy(BlanketPrototype, tileRef, 1f) > 0f; i++)
{
}

// and this - waits as long as the fire's heat takes, from the prototypes' own values
ProtoMan.Index(BlanketPrototype).TryGetComponent<KsFoamBlanketComponent>(out var blanketPrototype, Factory);
var boilSeconds = blanketPrototype.MaxFoam * energyPerUnit / (firePrototype.HeatPower * firePrototype.FoamHeatFraction);
await server.WaitRunTicks((int)MathF.Ceiling(boilSeconds * 1.5f + 1f) * TicksPerSecond);

// not this - true for one tuning
for (var i = 0; i < 30; i++)
    blanketSystem.Deploy(BlanketPrototype, tileRef, 1f);
await server.WaitRunTicks(TicksPerSecond * 4);
```

- **Bound by every limit the code applies, not only the obvious one.** If the code takes the minimum of a rate and an
  energy budget, so does the expected value.
- **Tolerances come from the mechanism too:** a value that changes every update and reaches the client a state later is
  compared within one update's change (`DecayRate * UpdateInterval`), not `0.0001f`.
- **Assert the design limit separately** where it matters ("boiling a full blanket off takes under 30 s"), so a tuning
  that breaks the *gameplay* still fails, with a message saying so, rather than the test silently stretching to fit.
- **A hard-coded number is fine** when it's the test's own setup (a `[TestPrototypes]` entity, a spilled volume), not a
  copy of a production prototype's value.

When a test does fail after a balance change, first check whether the change made the test's own arithmetic wrong.
Only then go looking for a bug, and update the test to derive the value rather than to match the new constant.

### Sleeping bodies get no contact updates, so a teleport doesn't end a collision

Physics only updates contacts for awake bodies. Move a sleeping entity with `SetCoordinates` and it keeps every contact
it had: no `EndCollideEvent` for what it left, and no `StartCollideEvent` for what it's now on top of, until something
wakes it. Walking wakes a body, so in game this rarely shows. In a test that teleports an entity to check collision
tracking (the foam's `KsInFoamComponent`, say), it looks exactly like the tracking being broken. Wake the body after
moving it:

```csharp
transformSystem.SetCoordinates(mobUid, outsideCoordinates);
physicsSystem.WakeBody(mobUid);
```

### An item lying on a puddle isn't touching it

An item's own fixture has mask `ItemMask` and no layer. A puddle's `slipFixture` is on `SlipLayer` with mask `ItemMask`.
Neither one's mask matches the other's layer, so a dropped item never makes a contact with the puddle under it, and its
`StartCollideEvent` never comes. A mob does touch it, which makes a test on a mob look like proof that items work.

When a feature needs items to touch puddles, put the fixture on the side the feature is about. For a puddle feature,
that's a non-hard fixture on the puddle whose layer matches what items collide with, not a fixture added to every item
that might matter. Keep it separate from `slipFixture` and filter your collide handlers on `args.OurFixtureId`, so each
toucher is counted once.

Items have no layer, so the fixture can't pick them out by mask. Whatever layer it uses is also hit by anything else that
masks that layer, and `FlyingMobMask` is exactly `ItemMask`. Step triggers and speed modifiers don't check *which* of the
puddle's fixtures was touched, only that the other fixture is hard. So without a filter, bats and carp start slipping on
puddles and getting slowed by sticky ones. `KsPuddleTouchSystem` cancels `PreventCollideEvent` between the touch
fixture and a mob's hard fixture. A mob still touches the puddle through its non-hard `flammable` fixture, which is all a
hot-thing check needs.

Items do reach the puddle's step trigger, but `SlipperySystem.CanSlip` rejects anything that can't be stunned, so
they're never counted. Non-hard fixtures also don't block a tile (`TurfSystem.IsTileBlocked`), and neither ray queries
nor projectile hits count them.

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

### Map saves: only `[DataField]`s persist, and `UninitializedSaveTest` checks it

A map save writes a component's `[DataField]`s and nothing else. A field that is only `[ViewVariables]`, or plain,
comes back at its default after a save and load: fine for caches and derived state, a silent bug for state that
must survive (an owner, a charge, a timer). Two `[DataField]` parameters change this:

| Parameter | Effect | Use for |
| --- | --- | --- |
| `readOnly: true` | Read from YAML, **never written**, so never saved. | Setup-only input consumed at init, e.g. `AccessComponent.Groups`, merged into `Tags` at `MapInit`. |
| `serverOnly: true` | The client neither reads nor writes it (generated code is wrapped in `IsServer`); it stays at its default there. | Server-only types inside shared prototypes or components, e.g. `ReactionPrototype.Impact`. |

Mobs get leeway: by default they are never written into map saves. Still keep mob data as serializable as
reasonably possible, but compromises are acceptable there.

`PrototypeSaveTest.UninitializedSaveTest` spawns every map-savable prototype on an **uninitialised** map (no
`MapInit`), saves each component and diffs it against the prototype. Nothing else in the suite checks this, so it is
the one test people forget to run. Run it after adding or changing a component or prototype:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~UninitializedSaveTest"
```

Failure reads `modifies component on spawn: <Comp>. Modified yaml: ...`. Causes and fixes:
- A `[DataField]` written in `ComponentInit`/`ComponentStartup`/a constructor, or defaulting to something computed
  (`_random`, `CurTime`): set it in `MapInit` instead, or compute it lazily.
- Runtime state or a cache as a `[DataField]` that need not survive a save: make it `[ViewVariables]` only.
- A non-nullable `EntityUid` field written before `MapInit`: make it `EntityUid?`, null until `MapInit`.
- A component added or removed on spawn: put it in the prototype, or do it in `MapInit`.

The test's own exclusions (`Metadata`/`Transform`/`Fixtures`, test prototypes, `DEBUG` suffix) are not a fix.

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

`PuddleSystem`'s one-puddle-per-tile invariant is enforced this way: a puddle that starts up or moves onto an
occupied tile is queued, and merged into the cached puddle (then deleted) in `Update`, never in the callback.

### Two kinds of predicted spawn, and which copy each one keeps

A predicted spawn makes two copies of an entity: one on the predicting client, and the server's. The two spawn APIs differ in **which copy the predicting client ends up showing**. Pick by that, not by name.

| | Engine `PredictedSpawn*` (`EntityManager`/`EntitySystem`) | `KsSharedPredictedSpawnSystem` with a `user` |
| --- | --- | --- |
| Client's copy | A stand-in. Deleted on every applied server state (`ClientGameStateManager.ResetPredictedEntities`), re-created only while its tick is still being re-predicted, so gone about one round trip after spawning. | Kept for its whole life, on that user's client only. |
| Server's copy | Replaces the stand-in for everyone. | Hidden and inert on the user's client (no sprite, light, collisions or predicted physics); shown to everyone else. |
| Use for | Anything long-lived or that gameplay reads: fires, foam blankets. | Short-lived movers the user caused: their own bullets, sparks and vapors. |

Without a `user`, the KS system is the engine spawn plus predicted physics, which matters only for something that moves (a spark). Anything anchored or static uses the engine's spawn directly.

**A one-shot animated effect (an EMP pulse, a flash) is neither.** Each re-creation of an engine stand-in starts its animation again, so it replays on every applied state for a round trip. Spawn it on the server only, as `SharedEmpSystem` does.

User mode exists because the engine's handover is a visible jump for fast movers. A lag-compensated bullet's server copy isn't where the client's copy is, so swapping one for the other teleports it. Showing both is a desync.

User mode has rules nothing enforces:

- **Pass `user` only on that user's own predicted path**: the code their client also runs for their own input, such as a shot request. A server-only path with a `user` hides the server's copy from a client that never made one, and the user sees nothing at all. Teleports, damage-driven effects, triggers and spells pass no user. Guns don't pass one per call either. `SharedGunSystem.OnShootRequest` opens a `BeginUserPrediction` scope, and only projectiles spawned inside it are marked. This is because `PlayerShotProjectileEvent` also fires for server-only shots, such as spells and autofire. Sprays pass one only from their click and activate-in-world handlers (`SharedSpraySystem.Klovn.Predicted.cs`). A verb, an action or a test sprays the server's vapors alone.
- **The kept copy never hears from the server again.** It can't follow anything the server does to the real one, which is why gameplay-relevant entities don't use this mode.
- **Gate a kept copy's one-off effects on `KsSharedPredictedSpawnSystem.IsFirstTimePredicted(uid)`, not `IGameTiming.IsFirstTimePredicted`.** Its physics steps only on first-time ticks, but its contacts are found by the next pass, usually a re-predicted tick, so a timing gate drops its impact effect.

Three things the client system handles, each of which looks like a working kept copy until it doesn't:
- Removing `PredictedSpawnComponent`, so the state reset leaves it alone.
- Blocking its physics on re-predicted ticks, so it isn't stepped several times per real tick. Nothing rolls a client-only entity back.
- Restoring its simulated position before each step. The client's transform lerp writes the rendered position over the real one between ticks, and only networked entities get that overwritten by the next reset.

`KsPredictedSpawnTest.KeptCopySurvivesStatesAndMovesAtItsOwnSpeed` fails if any of the three is missing.

**A kept copy that deletes itself from a collision handler uses `PredictedQueueDelDeferrable`, not `PredictedQueueDel`.**

- **Where the events come from.** The state reset raises `StartCollideEvent`/`EndCollideEvent` from a contact list it gathered beforehand (`PhysicsSystem.ResetContacts` → `UpdateIsTouching`, `RobustToolbox/Robust.Client/Physics/PhysicsSystem.Predict.cs`). It never checks whether an earlier event destroyed a later entry.
- **Why deleting there is fatal.** Queue-deleting a client-side entity destroys its contacts immediately (`SharedPhysicsSystem.OnContactEntityQueueDel`). That returns them to the pool, which clears their entities to `EntityUid.Invalid`. The next entry then throws `KeyNotFoundException: Entity 0 does not have a component of type TransformComponent` from `ResetPredictedEntities`.
- **Why only kept copies hit it.** Engine predicted spawns are deleted before `ResetContacts` runs, so only a kept copy can be in that list.
- **What the deferrable form does.** It flags the copy during state application and deletes it before the next physics step.

The same applies to anything else a collision handler does that destroys contacts, such as `SetCanCollide(false)`. `KsPredictedSpawnTest.KeptProjectileHitByTheStateReset` reproduces the crash.

**A kept copy's side effects are not re-predicted either.** The engine re-creates its predicted spawns only by
re-running the code that spawned them, and nothing re-runs a kept copy. So anything a kept copy spawns with the engine's
`PredictedSpawn*` is deleted by the next state reset and never comes back: the extinguisher shot's foam blankets
vanished from every tile but the first this way. Anything a kept copy spawns that has to last until the server's own copy
arrives is a plain client-side entity, together with a handover:

- **Mark it.** Spawn it with `KsTilePlaceholderSystem.Spawn` (client), which gives it a `KsTilePlaceholderComponent` and
  an expiry time. It is the real entity on that client until it is handed over: a placeholder blanket keeps chemfires
  out, and a placeholder puddle is slippery.
- **Hand over.** When the server's copy for the tile arrives, call `KsTilePlaceholderSystem.Supersede`. That hides the
  placeholder at once, so the tile never shows two, and deletes it in `FrameUpdate`, outside state application, for the
  contact reason above. Each feature decides what "arrived" means: the blanket cache taking the server's blanket, or a
  networked puddle starting up on the tile (`KsPredictedSpillSystem`).
- **Time it out.** If the server's copy never comes (the server's shot hit something sooner), the placeholder deletes
  itself when it expires.

**Never change a networked entity's state from a kept copy.** The next state puts it back, and the server's change
arrives a state or so later, so it flickers between the two. A kept vapor spilling into a puddle the server already sent
leaves the puddle's solution alone. It adds a client-only `KsPredictedPuddleTopUpComponent`, which only changes the volume the
puddle is drawn at, until a state shows the server's spill has landed.

`KsPredictedFoamShotTest.FiredShotLaysBlanketsThatLastUntilTheServersArrive` and
`KsPredictedSpillTest.SprayLaysPuddlesThatLastUntilTheServersArrive` fail if engine predicted spawns are used, or if the
handover is missing.

**Vapors are shared code** (`SharedVaporSystem`): a player's own spray (`SharedSpraySystem`) or solution gun shot is a
kept copy, and their client runs its tile walk. The server simulates every vapor; a client only its own kept copies.

- **What the client predicts.** A reagent's `tileReactions` reach the client through `KsTileReactionListSerializer`,
  which keeps only the types the client has. A reaction is predicted by adding a client-side class with exactly the
  server's class name (`Content.Client/_KS14/Chemistry/TileReactions/`), or by moving a fork-owned one to Shared
  (`KsDeployFoamBlanketTileReaction`). Everything else (extinguishing, flammability, CO2 vaporising) stays server-only,
  with no YAML change. `KsFirefightingTest.TestClientReadsOnlyThePredictedTileReactions` checks every reagent.
- **Gate each client reaction on `KsPredictedTileReactionSystem.ShouldReact`.** Only a kept vapor's walk opens the scope
  that makes it true. Other shared code runs tile reactions on the client too: a mop's absorbent, re-run on every
  re-predicted tick. Without the gate, each re-prediction would spawn another puddle. Checked by
  `KsPredictedSpillTest.ReactionsOutsideAKeptVaporDoNothingOnTheClient`.
- **Order before physics.** A vapor reacts with the tile the last tick left it on, so a tile it enters on the tick it
  despawns is never reacted with on the server. A client that reacts after physics lays one tile too many. That tile's
  placeholder waits for a server copy that never comes, and vanishes when it expires.

**Objects held on prototypes outlive a client's systems.** A client makes its entity systems anew on every connection,
but keeps its prototypes, and the tile effects and reactions on them. One injected once, at load, still holds the old
systems after a reconnect: it compiles, it runs, and it acts on nothing. Before the client had any tile reactions this
never showed. Re-check whenever you use one: `KsTileEffectSystem.EnsureInitialized` re-injects an effect that was
injected by an older `KsTileEffectSystem` (`KsTileEffect.IsInitializedFor`). In tests it shows up only when a pooled
client pair reconnected earlier in the same run, so a test of it fails in a batch and passes alone.

Neither kind of hiding works by sending the client a network event naming the entity: it can arrive before the entity's state does. Put the information on a networked component, as `KsPredictedSpawnComponent.PredictingUser` does. Also, `PredictedDel` of a networked entity on the client is only a predicted detach, which the next state restores, so don't use it to hide one.

### `IntersectRay` with `returnOnFirstHit: true` is not the nearest hit

The physics ray query's `returnOnFirstHit` stops at whichever fixture the broadphase tree happens to visit first.
That is often, but not always, the closest one, so a beam, sight line or "how far can I see" length ends beyond a
nearer obstacle only sometimes, which reads as flicker or a rare overshoot. Nothing errors. Use
`returnOnFirstHit: false` (the results come back sorted by distance) and take the minimum when you need the nearest:

```csharp
foreach (var result in _physicsSystem.IntersectRay(mapId, ray, length, ignoredEnt: uid, returnOnFirstHit: false))
    length = MathF.Min(length, result.Distance);
```

### `Equals` on an enum boxes it

An enum's `Equals` is inherited from `System.Enum`, a class, and takes an `object`. Calling it boxes both
sides: two heap allocations, for what `==` does as a single integer compare. It reads identically, compiles
clean, and returns the right answer, so nothing points at it; it only shows up as garbage.

```csharp
// not this - two boxes per call
return Flags.Equals(other.Flags);

// this - an integer compare, no allocation
return Flags == other.Flags;
```

It matters wherever the comparison runs in bulk. `PathfindingData.Equals` did this, and it sits under every
`PathPoly` dictionary lookup, so a single NPC tactical position search allocated about 230 KB of boxed
`PathfindingBreadcrumbFlag`s - roughly 60% of everything it allocated, on a path every holding NPC runs each
replan. The same goes for any `IEquatable<T>.Equals` or `GetHashCode` on a struct used as a dictionary key: it
runs on every lookup, so an allocation in it multiplies by the size of the search.

`==`, `!=` and bitwise tests (`(flags & Flag) != 0`) never box. Nor does `HasFlag` on current .NET, which the
JIT turns into a bitwise test, but `&` says the same thing without relying on that.

To find allocations like this, measure by type rather than guessing: `GC.GetTotalAllocatedBytes(precise: true)`
around the work says *how much*, and an `EventListener` on `Microsoft-Windows-DotNETRuntime` (GC keyword `0x1`,
`Verbose`) receives `GCAllocationTick` events naming the type being allocated, which says *what*. Both work in
an integration test, which is not sandboxed.

### A window's `MinSize` does not protect contents that wrap

`BaseWindow` clamps a drag to `MinSize` and nothing else. It never asks the contents what they need. A fixed
`MinSize` is fine for contents whose size doesn't depend on the window's, but wrapping text (`RichTextLabel`, or
anything word-wrapped) gets *taller* as the window gets narrower. So a window can meet its minimum and still push
whatever sits below the text out of the bottom. It isn't clipped visibly: those controls are laid out at zero height and
simply vanish. The voice link window lost its buttons this way, at a size that looked like a sensible minimum when it
was picked.

Make the minimum follow the contents. The rows that can't wrap set the minimum width, and the minimum height is what
the contents need *at the current width*. Recompute it on every resize and whenever a row's text changes:

```csharp
protected override void Resized()
{
    base.Resized();
    UpdateMinimumSize();
}

private void UpdateMinimumSize()
{
    if (RootBox.Size.X <= 0f)
        return; // not laid out yet

    var marginSize = new Vector2(RootBox.Margin.SumHorizontal, RootBox.Margin.SumVertical);
    var frameSize = Size - RootBox.Size - marginSize; // title bar and borders

    var widestRow = 0f;
    foreach (var row in _unwrappableRows) // buttons, single-line labels
    {
        row.Measure(Vector2Helpers.Infinity);
        widestRow = MathF.Max(widestRow, row.DesiredSize.X);
    }

    // DesiredSize includes the margin.
    RootBox.Measure(new Vector2(RootBox.Size.X + marginSize.X, float.PositiveInfinity));

    MinSize = new Vector2(frameSize.X + marginSize.X + widestRow, frameSize.Y + RootBox.DesiredSize.Y);
}
```

A `MinSize` bigger than the current `SetSize` wins at measure time, so the window grows back to fit by itself. Also set
`SetSize` to match, so the next drag starts from the size actually shown. See `KsVoiceLinkWindow`.

**Testing it has its own trap.** A squeezed control is laid out *and measured* at the squeezed size, so its `Size` and
its `DesiredSize` agree with each other at zero. "Is it inside the window?" passes too, because a zero-height box sits
inside anything. A test built on those passes with the bug present. Instead, compare each control against what it needs
with room to spare: `Measure` it with unlimited height (and unlimited width, for anything that can't wrap), then check
its shown size against that. `KsVoiceLinkWindowTests` does this. Layout runs in the headless client with real font
metrics, so this works in an ordinary integration test.

### World-space overlays draw square to the map, not the grid

A world handle with the identity transform draws in map space, so every coordinate you hand it is measured along the
map's axes. That covers far more than `Box2`s. It applies to any shape with corners or a direction: a `Box2` or
`DrawRect`, a texture drawn with `DrawTextureRect`, `DrawPrimitives` vertices, lines whose ends are built from offsets
(`position + new Vector2(0, size)`), and anything placed next to something else by such an offset, text labels
included. On a turned grid, all of these sit at a slant to the grid they mark, and anything placed beside them lands
in the wrong spot. Only shapes that look the same at any rotation are safe without a transform: a circle, a lone point,
or a line between two points that were both already worked out in map space. Screen-space text glyphs are always
upright, but *where* the text starts still has to be worked out in the grid's frame.

Draw in the grid's frame instead. Call `SetTransform` with the grid's world matrix for tiles, or with
`Matrix3Helpers.CreateTransform(position, gridRotation)` for a marker drawn around the origin. Set
`Matrix3x2.Identity` back afterwards. Clyde applies the transform to vertices as it queues them, so setting it per
marker is cheap. `SquadDebugOverlay` does both (`GetMarkerTransform`), including a label positioned through the
marker's frame, and `KsLockOnIndicatorOverlay` draws its brackets the same way.

### Occluder ray queries along a tile edge report walls that are not there

The occluder tree's ray queries (`OccluderSystem.IntersectRay`, and everything built on them) test each box with
`Ray.Intersects`. For a ray parallel to an axis lying exactly on a box's edge, as one along a tile edge does, that
returns a NaN or zero distance for every box on the line, including those behind the ray's start and past its end.
A NaN never compares greater than the maximum length, so every wall in line with the ray counts as in the way. It
only happens for exact edges, so it hides well: a random-position test almost never lands on one.

Throw out hits the segment cannot reach before trusting them, with `KsSegmentBounds.Touches` against the occluder's
world bounds:

```csharp
// as ExamineSystemShared.Klovn.Occlusion.cs does - only ever removes impossible hits, so normal rays are unchanged
if (!KsSegmentBounds.Touches(start, end, worldBounds.Enlarged(0.01f)))
    continue;
```

`ExamineSystemShared.InRangeUnOccluded`, `NpcLineOfSightSystem` and `KsLosSensorSystem` all do this. The engine's own
`OccluderSystem.InRangeUnoccluded` does not, so use the examine one.

## 7. KS client localisation: popups and prototype metadata

The server normally runs in `en-US`, while each client selects its own culture through
the KS14 settings tab (`klovn.client_locale`). **Formatting a string on the server does
not make it follow the recipient's language.** Two content systems handle this:
`KsPopupMessage` carries unformatted popup messages to clients, and
`ContentLocalizationManager` resolves prototype names and descriptions in the active
localisation culture. Use both when a popup contains a prototype name.

Keep fixes in content; do not modify `RobustToolbox` to work around the engine's
metadata cache. Further culture, guidebook, and radio setup details are in
[client_localization.md](Klovn/Docs/client_localization.md).

### Popups: carry the message key and its arguments

Import `Content.Shared._KS14.PopupLocalization`, inject `SharedPopupSystem`, and pass
`KsPopupMessage.Create(...)` to the ordinary popup methods. The argument names must
match the Fluent variables. Add the Fluent message to English and the supported
translated locales, following the feature's existing file layout.

```csharp
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;

[Dependency] private SharedPopupSystem _popupSystem = default!;

// Only the user receives this message.
_popupSystem.PopupEntity(
    KsPopupMessage.Create("anchorless-convert-begin-message"),
    entityUid, userUid, type: PopupType.Small);

// Entity arguments retain the information needed by Fluent's entity functions.
_popupSystem.PopupEntity(
    KsPopupMessage.Create("gun-ballistic-transfer-invalid",
        ("ammoEntity", ammoUid), ("targetEntity", targetUid)),
    targetUid, userUid, type: PopupType.Small);
```

Do **not** pass `Loc.GetString(...)` to a popup intended to follow the client's
culture. That selects the legacy string overload, which sends the already formatted
text. String overloads remain appropriate for literal text and player-authored text.
Likewise, do not call `message.Format(...)` before handing the message to the popup
system. Keep custom events, helpers, and intermediate variables typed as
`KsPopupMessage` if they carry a translated popup. A helper taking `string` cannot
recover the original key or arguments after formatting.

The complete argument tree must remain deferred, including the values inserted into
an otherwise localised outer message:

```csharp
// A nested Fluent label.
_popupSystem.PopupEntity(
    KsPopupMessage.Create("gun-selected-mode",
        ("mode", KsPopupMessage.Create("gun-SemiAuto"))),
    weaponUid, userUid);

// A prototype name, such as the energy shotgun's selected projectile.
_popupSystem.PopupEntity(
    KsPopupMessage.Create("gun-set-fire-mode-popup",
        ("mode", new KsPopupPrototypeName(projectilePrototype.ID))),
    weaponUid, userUid);
```

`KsPopupPrototypeName` resolves the ID through `ContentLocalizationManager` **at the
recipient**, for both local prediction and authoritative network messages. It uses
the prototype name table and its inheritance rules. An unknown prototype ID displays
the ID as a fallback. Do not replace it with `prototype.Name`, an eagerly formatted
`GetLocalizedPrototypeName(...)`, or `KsPopupMessage.Create("ent-" + prototype.ID)`:
the first two freeze the sender's language; the last bypasses prototype inheritance
and custom `localizationId` handling.

Use these argument forms:

| Value needed by the message | Argument to pass |
| --- | --- |
| Live entity, including Fluent entity grammar | `EntityUid` or `Entity<T>`; avoid preformatted `Name(...)` |
| Prototype name without a live entity | `new KsPopupPrototypeName(prototype.ID)` |
| Another translated label | `KsPopupMessage.Create(labelKey, ...)` |
| Several translated labels joined into one argument | `new KsPopupMessageList(separator, messages)`; each element is a deferred `KsPopupMessage` |
| Number, boolean, time, literal/player text | The original value; let Fluent format numbers |
| Colour or enum | `Color` or enum; the payload converts them to hex or lowercase invariant text |

The payload also unwraps `LocValueString` and `LocValueNumber`. Other arbitrary
objects fall back to `ToString()` when sent, so do not pass components, prototypes,
custom localisation values, or custom objects expecting their behaviour to survive
the network. Add an explicit supported argument representation and tests if a new
kind is needed. Live entity arguments must be available to the receiving client;
they are transported as `NetEntity`, not as a snapshot of their name.

`KsPopupMessage.ToLower()` defers lowercasing until after formatting. It currently
uses ordinary `string.ToLower()` and therefore the thread's casing rules; do not
assume it implements the selected game culture's grammatical inflections. Prefer
appropriately written Fluent messages when casing or inflection matters.

### Popup audience, prediction, and network flow

The KS overloads preserve the ordinary popup API's audience rules:

| Call | Audience |
| --- | --- |
| `PopupEntity(message, entityUid)` | Players in the entity's PVS |
| `PopupEntity(message, entityUid, recipientUid)` | That recipient only |
| `PopupEntity(message, entityUid, recipientSession)` | That session only |
| `PopupEntity(message, entityUid, filter, recordReplay)` | The supplied filter |
| `PopupCoordinates(message, coordinates)` | Players in the coordinates' PVS |
| `PopupCursor(message, recipientUid)` | That recipient only |

Entity and coordinate methods also accept explicit filters and sessions. The two
message `PopupEntity` overload sends one message to the recipient and another to
nearby observers. Both translated messages must be `KsPopupMessage`; the mixed
overload permits a literal observer message or `null` to suppress it. Preserve the
existing filter, popup type, replay recording choice, coordinates, and prediction
key when migrating a call.

The KS `PopupClient` / `PopupPredicted` aliases exist for old call sites; new code uses
`PopupEntity`, `PopupCoordinates` and `PopupCursor`. See §4 (*Popups predict themselves*) for
the `recipient` trap and how to migrate an obsolete call without turning a private
`PopupClient` into a PVS broadcast.

On the server, `PopupSystem.Klovn.Localization.cs` serialises a `KsPopupPayload`
inside `KsLocalizedPopupEvent`, including its location, popup type, game tick, and
coordinate prediction key. It does not format the text or send separate copies for
each language. On the client, `PopupSystem.Klovn.Localization.cs` formats the payload
in the client's current culture, then feeds the ordinary popup event handlers.
Shared prediction formats the original arguments locally on the first predicted
execution, without a network conversion round trip.

Before formatting a network popup, the client checks that its entity or coordinate
anchor is available. `KsPopupPayload.TryFormat` also rejects missing or terminating
entity arguments, including those in nested messages and lists. Such stale popups
are discarded; never pass `EntityUid.Invalid` to Fluent entity grammar functions.

Formatting before the ordinary prediction match allows the predicted and confirmed
text to agree even when the server uses English and the client uses another language.
**Use the same key, argument values, audience, and prediction identity on both paths.**
This preserves the engine's existing duplicate suppression; it does not deduplicate
arbitrary messages with different ticks, locations, keys, or arguments. Do not fix
duplicates by adding a second popup send or a separate language-specific broadcast.

### Prototype names and descriptions: use the content manager

Import `Content.Shared.Localizations` and inject `ContentLocalizationManager` in a
system that needs prototype metadata for the active culture:

```csharp
[Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;

var prototype = ProtoMan.Index<EntityPrototype>(prototypeId);
var localisedName = _contentLocalizationManager.GetLocalizedPrototypeName(prototype);
var localisedDescription = _contentLocalizationManager.GetLocalizedPrototypeDescription(prototype);

// Preserve a live item's custom name rather than replacing it with the prototype's name.
var metadataComponent = MetaData(itemUid);
var displayName = _contentLocalizationManager.GetLocalizedEntityName(
    itemUid, EntityManager, metadataComponent.EntityName);

// An examine response is formatted now, inside the request's existing culture scope.
args.PushMarkup(Loc.GetString("gun-set-fire-mode-examine", ("mode", localisedName)));
```

These methods use the **active** localisation culture, not an entity's owner or an
implicitly inferred recipient. Client calls use the selected client culture. Server
examine and verb request handlers already enter a validated synchronous
`BeginCultureScope(requestedCulture)` for that request. Code adding another
authoritative response path must carry and validate the client's requested culture
and scope its formatting with the same manager:

```csharp
using (_contentLocalizationManager.BeginCultureScope(requestedCulture))
{
    // Format only this synchronous response here.
}
```

The scope temporarily changes the localisation manager's default culture and restores
it on disposal. Never retain it across `await`, background work, queued callbacks,
or an unrelated gameplay action. Verb actions execute after their validation scope
has been disposed. Popups use deferred messages instead of a server culture scope.

The engine's entity metadata localisation cache is warmed in the normal server
culture at startup. Do not rely on `prototype.Name`, `prototype.Description`, or
`Loc.GetEntityData(...)` switching with each client's request, and do not clear or
rewrite global engine metadata to translate one response. That causes mixed-language
or Russian-then-English examine results and can affect other players. The server's
base examine description uses `GetLocalizedPrototypeDescription` only while the
live description still equals its prototype description; deliberate runtime changes
are preserved. Follow that distinction when adding another description path.

### Translation files and inheritance

Prototype translations normally use `ent-<ExactPrototypeID>` for the name and its
`.desc` attribute for the description. A prototype's `localizationId`, including an
inherited one, overrides that key. Keep prototype IDs case-sensitive. Put fork
translations under `Resources/Locale/<culture>/_KS14/` and mirror the corresponding
prototype paths; do not collect an entire module's prototypes in one giant FTL file.
Follow existing translated upstream trees when extending their translations.
A new prototype needs its `ent-` entry in both `en-US` and `ru-RU`, with the prototype's path as a comment above it.

```ftl
ent-KsExampleDevice = пример устройства
    .desc = Описание устройства.
```

Names and descriptions resolve independently. `KsPrototypeLocalizationResolver`
shares the following policy between runtime and packaging:

1. Check the prototype's native translated field, then traverse parents breadth
   first in their declared order, including abstract parents.
2. A concrete prototype's distinct literal field stops inheritance when reached.
   A child with its own English name therefore needs its own translation; do not
   expect an unrelated translated parent name to replace it.
3. A native inherited translation takes precedence over a fallback English Fluent
   field when no distinct concrete literal stops that traversal.
4. If nothing suitable is found, use the prototype's English Fluent field, its
   resolved literal field, or an empty string. An explicitly empty translated
   description (`.desc = { "" }`) is a valid override, not a missing translation.

The English name API retains the engine's ordinary `prototype.Name` behaviour.
Do not implement another parent walk or use fallback-aware `Loc.HasString(...)`
as a test for a *native* translation: an English fallback can hide a missing native
field. `KsFluentMessageIndex` records native message/attribute presence for the shared
resolver. Prototype metadata is resolved as static text; put per-use variables and
state in a separate Fluent examine/popup message rather than a cached name or `.desc`.

### Cache generation, cultures, reloads, and performance

`MSBuild/KsLocalizationCache.targets` runs the packaging compiler after normal client
and server builds. It produces `Resources/Localization/_KS14/prototype-names.bin`
with per-culture name and description tables and the registered culture list. The
artifact and input fingerprint are Git-ignored; do not hand-edit or commit them.
Unchanged inputs skip regeneration. `-p:SkipKsLocalizationCache=true` skips this build
hook for tooling; it is not a replacement for shipping a current cache.

Packaging recompiles from the package's exact prototype and locale inputs, including
prototype replacements, and includes the result in client/server resource packages
and automatic ACZ client packages. Clients receive it with the ordinary resource
download. There is no separate cache transfer during gameplay.

`KsEntryPoint.PostInit()` loads the artifact and initialises registered cultures after
prototype replacement. The compiled tables use frozen dictionaries: ordinary
lookups do not enumerate Fluent files, walk inheritance, or rebuild tables. Language
switching retains loaded compiled tables. Keep the expensive work in compilation,
initialisation, or explicit reload paths; do not add resource scans, whole-prototype
loops, culture switches, or per-recipient cache rebuilding to popup/examine lookups.
Popup formatting still performs Fluent work and allocates arguments when needed;
it is not an allocation-free API. Keep argument trees small and send only to the
intended audience.

Without an artifact, or for a runtime culture absent from it, the manager builds a
runtime table on demand and retains it. Abstract prototype requests outside the
concrete tables use memoised runtime translations. Runtime and packaging must keep
the same resolver and native-presence rules; changing one side alone can make a
development run and a packaged build translate differently.

Add future cultures under `Resources/Locale/<canonical-culture>/` and translate
`_KS14/Localization/options.ftl` there to register them as selectable game languages.
Use `GetAvailableCultures()` / `ResolveCulture(...)`, not a hardcoded English/Russian
branch. English is the fallback for unknown culture requests and missing messages.
Both client and server need the resources for authoritative responses. All registered
cultures and their content formatting functions load at startup.

For a resource pack mounted at runtime, call `RefreshAvailableCultures()` and reopen
settings to refresh the choices. Prototype reloads invalidate the prototype tables
automatically. Content language refreshes also invalidate them. If code calls the
engine's `ReloadLocalizations()` directly, it must also call
`InvalidatePrototypeNameCache()` because the engine exposes no reload notification
for this cache. Rebuild or repackage to update the persistent artifact. Do not
invalidate the cache on every ordinary lookup or every popup.

### Localisation verification

Use the tests under `Content.IntegrationTests/Tests/_KS14/Localization/`:

- `KsPopupLocalizationTests`: recipient cultures, energy shotgun's nested prototype
  labels, prediction confirmation, and payload formatting/serialization measurements.
- `KsPrototypeNameCacheTests`: packaged/runtime parity and resolver behaviour.
- `KsClientLocalizationTests`: culture discovery/switching, prototype metadata,
  nested examine text, and other client localisation regressions.
- `KsVerbLocalizationTests`: authoritative verb labels and action culture boundaries.
- `Content.IntegrationTests.Tests.Utility.SandboxTest`: shared/client API legality,
  required when adding new argument types or cache APIs.

For a changed message, check an English server with clients in two different
cultures, including a predicted action and its authoritative confirmation. Verify
the nested label, private/broadcast audience, custom item names/descriptions, and
fallback for a missing translation. Check packaged resources as well as the runtime
fallback when changing cache resolution. Do not infer whole-round CPU or network overhead from the
popup microbenchmarks; they measure individual components and serialized payloads.
