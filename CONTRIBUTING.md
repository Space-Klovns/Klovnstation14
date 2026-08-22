# Contributing to Klovnstation 14

Follow this guide to know our coding conventions.

## 1. Project lineage

Klovnstation 14 is a fork of SS14. The chain:

- [space-wizards/space-station-14](https://github.com/space-wizards/space-station-14) - upstream, vanilla SS14.
- **Klovnstation 14** - this repo - a modded downstream.

We merge from `space-wizards/space-station-14` regularly. PRs contain both upstream ports and Klovnstation 14-specific work; conventions below make them easy to tell apart.

## 2. The `_KS14/` rule

**New Klovnstation 14 code lives under a `_KS14/` folder.** Applies to every project tree where one exists:

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

Inside `_KS14/`, mirror upstream feature-driven layout (`_KS14/Atmos/Components/...`, `_KS14/Cargo/Systems/...`) rather than grouping by type.

### Namespace (C#)

File at `Content.<project>/_KS14/<Feature>/<Sub>/File.cs` declares:

```csharp
namespace Content.<project>._KS14.<Feature>.<Sub>;
```

## 3. Upstream edits: the `// KS14:` marker

When you edit **or add** a file **outside** `_KS14/` (anywhere in upstream SS14 / other forks / `_Manifest` / `_sin` / etc. trees), mark Klovnstation 14 provenance inline:

- **Edits to existing upstream files** - mark every logical change inline (see forms below).
- **Method/member additions to classes** - make the class partial (with KS14 comment) and add it in a same-folder file with the name template of `*.Klovn.Feature.cs`.
- **New files added outside `_KS14/`** - put `// KS14: added in this fork` (or `# KS14: added in this fork` for YAML / FTL / shell) header on first line. Prefer `_KS14/`; only use this when extending an upstream tree is genuinely the right home (e.g. filling translation gaps in `Resources/Locale/en-US/_Goobstation/`).

Both forms make Klovnstation 14 modifications easy to spot during upstream merges. As a precedent, for example, making a value swap (`KS14: 100 -> 50`) and changing it later on should be, say, `KS14: 100 -> 30`; the original upstream value must be preserved in the comment. These forms can also be modified for other forks (e.g. use `Goobstation` instead of `KS14` when modifying code as part of a port from Gobstation) when necessary.

Forms:

- **Specific change** - `/* KS14: concise statement of the change done */` after the change:
  ```csharp
  internal /* KS14: public -> internal */ sealed partial /* KS14: made partial */ class OldClass
  {
    public void Main(int nuParam /* KS14: added param */, int oldParam)
    {
        PredictedSpawn/* KS14: made predicted */(entityId);
    }
  }
  ```
- **Adding single line or making multiple changes to one** - `// KS14: short reason`:
  ```csharp
  public bool Inverted; // KS14: if true, Species list is a blacklist
  ```
- **Removing single line** - `/* old code wrapped in comment * // KS14: removed: short reason why`:
  ```csharp
  /* public bool Inverted; */ // KS14: if true, Species list is a blacklist
  ```
- **C# Value swap** - `// KS14: OLD -> NEW, reason (optional)` - follow form for a specific change if the value being changed is not at the end of the line (excluding semicolon):
  ```csharp
  public const int MaxPlayers = 50; // KS14: 100 -> 50, too high
  ```
- **YML Value swap** - `# KS14: OLD -> NEW, reason (optional)`:
  ```csharp
  myValue: 50 // KS14: 100 -> 50, too high
  ```
- **Adding/changing multi-line block** - `// KS14 start: reason` opens, `// KS14 end` closes:
  ```csharp
  // KS14 start: check if we should return early
  if (ShouldReturnEarlyNow())
      return;
  // KS14 end
  ```
- **Removing multi-line block** - `// KS14: reason` before a multiline comment-block:
  ```csharp
  // KS14: unnecesssary
  /*
  doThing();
  doOtherThing();
  doMoreThings();
  */
  ```
- **Added `using`** - trailing `// KS14`:
  ```csharp
  using Content.Shared._KS14.NewFeature; // KS14
  ```

### YAML and Fluent (`.ftl`) edits

Same rule with `#` comments: `# KS14:` / `# End KS14`.

```yaml
- type: entity
  id: SomeUpstreamEntity
  components:
  - type: HealthAnalyzer
    scanDelay: 0.8 # KS14: 1.2 -> 0.8
```

### File structure

New additions (be it in Resources, Content, or anything) should generally try to mimic upstream organisation.

Two examples for C# and YAML respectively - treat * as a placeholder:
#### C#
Addition: A new system for announcements.
Upstream: `Content.Server/Announcements/*`
Klovnstation 14: `Content.Server/_KS14/Announcements/*`

Exceptions: Do not follow the `*/Components`, `*/Systems`, `*/[Specified Category]`, etc. pattern for small additions - these are for large or large-in-full-scope pieces of work.

#### YAML
Addition: New species
Upstream: `Resources/Prototypes/Body/Species/*`
Klovnstation 14: `Resources/_KS14/Prototypes/Body/Species/*`

Exceptions: For Klovnstation 14 changes, you are encouraged to split generalised files into a folder with more specialised files, e.g. `shaders.yml` becomes `Shaders/misc.yml`.
Especially notable exception: The KS14-specific dev map is at `Maps/Test/_KS14/klovndev.yml` instead of the existing `Maps/_KS14/Test/klovndev.yml`, because the former gives it more leeway in integration tests. Things like this are allowed as they reduce codebase divergence (you would otherwise need to tweak integration tests)

## 4. Code style and upstream SS14 standards

Klovnstation 14 follows upstream Space Wizards' Den coding standards. Read and apply before any PR touching C# or YAML:

- [SS14 codebase info](https://docs.spacestation14.com/en/general-development/codebase-info.html) - landing page for full conventions tree.
- [SS14 conventions](https://docs.spacestation14.com/en/general-development/codebase-info/conventions.html) - naming, comments, ECS rules (components hold *only* data; systems hold logic; events are struct `[ByRefEvent]`s named `...Event` with `OnXEvent` handlers), XAML/UI, performance, `TimeSpan` / field-deltas, YAML conventions, localization, in-/out-of-simulation split. Primary document.
- [SS14 codebase organization](https://docs.spacestation14.com/en/general-development/codebase-info/codebase-organization.html) - project split (Client / Shared / Server), file layout, prototype organization (`base.yml` + per-type files; no `misc/` folders).
- [SS14 pull-request guidelines](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html) - PR hygiene (separate PRs for features / bug fixes / refactors, test in-game, no web edits, no force-push after reviews).
- [SS14 style guide](https://docs.spacestation14.com/en/general-development/codebase-info/style-guide.html) - C# formatting.

**One exception to upstream.** SS14's `codebase-organization` says "game-code folders live directly under `Content.Client/Shared/Server`." Klovnstation 14 overrides for **new fork code only**: new code goes under `_KS14/` per section 2. Upstream files edited in place still follow upstream layout and carry `// KS14:` markers per section 3. You do not need to modify existing code just to make it follow conventions, ONLY IF modifying the code will not diverge it from its original

Local rules on top of upstream:

### Casting/Coercion and Code Clarity (C#)

When defining a variable and casting the value, always use an explicit cast using the target type wrapped in parentheses.
DO THIS:
```csharp
var myFloat = (float)GetMyInt();
```

DO NOT:
```csharp
float myFloat = GetMyInt();
```

### Verbosity (C#)

Use verbose names for members/variables/dependencies etc., also if existing code uses archaic names - `xform` should be `transform`, etc.. For example:
BAD:
```csharp
[Dependency] TransformSystem _xform
```
GOOD:
```csharp
[Dependency] TransformSystem _transformSystem
```

BAD:
```csharp
PhysicsComponent body
```
GOOD:
```csharp
PhysicsComponent physicsComponent
```

BAD:
```csharp
SpriteComponent sprite
```
GOOD:
```csharp
SpriteComponent spriteComponent
```

Methods and members with [DataField] are given a bit of leeway - e.g., Prototype can shorted to Proto.

### New source-gen code standards (C#)

With new engine versions such as the one this codebase is on, automatically-injected dependencies using the [Dependency] attribute (specifically only those in EntitySystem inheritors and some very few exceptions) must now be writable, and their owner classes must be partial.

OLD ENTITYSYSTEM/ETC.:
```csharp
public sealed class MySystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;
}
```
NEW:
```csharp
public sealed partial class MySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
}
```
