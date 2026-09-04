# Contributing to Klovnstation 14

Coding conventions for this repo. Written for coding agents first, humans second — precise over chatty, examples over prose.

**Quick reference:**
- New code → `Content.<project>/_KS14/<Feature>/...` (see §2).
- Editing or adding a file *outside* `_KS14/` → mark it with `// KS14:` / `# KS14:` (see §3).
- Otherwise follow upstream SS14 conventions, plus the local rules in §4.

## 1. Project lineage

Klovnstation 14 is a fork of SS14:

- [space-wizards/space-station-14](https://github.com/space-wizards/space-station-14) — upstream, vanilla SS14.
- **Klovnstation 14** — this repo, a modded downstream.

We merge from upstream regularly. PRs mix upstream ports with Klovnstation 14-specific work; the conventions below keep the two easy to tell apart during those merges.

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

Forms, ordered so that they take precedence over those before them:

- **Specific change** — `/* KS14: concise statement */` right after the change:
  ```csharp
  internal /* KS14: public -> internal */ sealed partial /* KS14: made partial */ class OldClass
  {
      public void Main(int nuParam /* KS14: added param */, int oldParam)
      {
          PredictedSpawn/* KS14: made predicted */(entityId);
      }
  }
  ```
- **C# value swap** — `// KS14: OLD -> NEW, reason (optional)` (use the specific-change form instead if the value isn't at the end of the line, excluding the semicolon):
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

**One exception to upstream**: `codebase-organization` says game-code folders live directly under `Content.Client/Shared/Server`. We override this for **new fork code only** — new code goes under `_KS14/` per §2. Upstream files edited in place keep their upstream layout and carry `// KS14:` markers per §3. Don't touch existing code just to bring it into convention unless you're already changing it for another reason.

### Local rules on top of upstream

**Casting/coercion (C#)** — always cast explicitly with the target type in parentheses:
```csharp
var myFloat = (float)GetMyInt();       // do this
float myFloat = GetMyInt();            // not this
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
```
This earns its keep when one thing exists in several forms in the same scope — `projectileUid` sitting next to `Entity<LagCompensatingProjectileComponent> projectile` reads unambiguously.

Exceptions, all conventional: the primary subject of a handler or method may stay bare — `entity`, `ent`, `uid` — and a locally-built event is just `ev`. As soon as a second thing of the same kind enters scope, go back to suffixes.

**`Ks` prefix (IDs & type names)** — when a new prototype ID or type name could plausibly collide with an upstream name (present or future), prefix it with `Ks`: `KsCCVars` (a fork-only cvars class, deliberately not inheriting upstream `CCVars`), `KsBlack`, `KsCatwalkIron` (colors and structure variants — generic vocabulary upstream already uses or could use). Skip the prefix when the name is already distinctive enough not to collide — `Anchorless`, `ArcFlash`, `ComplexShove` — the `_KS14/` folder already marks provenance there. This is a judgment call, not a mechanical rule: ask "would upstream plausibly ship something under this exact name?" If yes, prefix it.

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
