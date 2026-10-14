<!-- KS14: added in this fork -->
# KS14 procedural generation system specification

Status: implementation in progress. This document remains the behavior contract; the detailed
implementation ledger in section 17 records what exists, what has been verified, and what remains.
An unchecked task is not implemented merely because its API or a partial algorithm exists.

Current overall implementation estimate (2026-10-14): **approximately 75%**. This is weighted
capability coverage, not production readiness. Section 17's milestone table and detailed rewrite
ledger record the evidence, uncertainty and remaining exits; earlier percentages are historical
checkpoints. R01-R07 remain in progress. Engine access certification and publication remain gated.

Contract revision 2026-09-30 adds reusable relational furnishing assemblies, connected area-marker
blobs, and selectable entrance connection configurations. These are requirements to implement,
not claims about existing support. Section 17's R01-R07 rewrite checklist takes precedence over
older implementation assumptions and verification notes wherever they conflict.

Audience: an AI implementation agent and its human reviewer. `MUST` denotes a required invariant;
`SHOULD` denotes a preferred outcome with a documented reason for deviation. Complete the tasks in
section 17 in dependency order. Do not implement the entire system as one unreviewable change.

## 1. Scope and required outcomes

Implement a server-authoritative generator that accepts any finite set of grid cells, including
concave shapes, holes, disconnected islands, and a single cell. One cell is the smallest unit; there
is no minimum room module size. A 1x3 request MUST be representable and processed. This does not
imply that an isolated 1x3 footprint can contain a usable, enclosed room.

Support three content modes independently of how the shape is supplied:

| Mode | Content |
| --- | --- |
| `Procedural` | Generate partitions, passages, structure, and optional furnishing without room prefabs. |
| `Prefabs` | Automatically select and fit rooms/layouts from a library to the supplied shape; generate structural seams and required connectors, but no procedural room interiors. Uncovered cells must have an explicit disposition or fail. |
| `Hybrid` | Automatically select and fit rooms/layouts to the shape, then procedurally fill remaining areas. |

Shapes may be supplied directly as a mask, authored using markers, or produced by a shape provider
inside an explicit bounding mask. Once normalized, every mode uses the same cell representation.
Shape providers are optional adapters; a caller never needs to use a rectangle, noise, or a room pack.

**Automatic room selection and shape fitting are the default.** The author supplies a target shape,
theme/library, and optional goals; the generator decides room types, counts, positions, rotations,
and subdivision. It MUST NOT require an authored slot grid, fixed room positions, alignment markers
throughout the target, or a preselected list of rooms. Optional constant regions can force selected
parts of the map to remain exactly authored while generation fits around them. Explicit choice
regions and anchors are advanced overrides, not prerequisites for generation.

Required capabilities:

1. The same region can offer a large-room layout, several medium rooms, or many small rooms.
2. A single output can contain large, medium, and small rooms in different, nonoverlapping regions.
3. Alternatives comprising multiple rooms are selected and rolled back as complete layouts. Three
   small rooms forming an L and four small rooms with different connections cannot be mixed.
4. Authored empty areas can be filled procedurally, including narrow remnants between prefabs.
5. Every operational room entrance has a walkable destination; a reachable dead-end passage is valid.
6. Every pair of operational entrances to one logical room is connected through that room by a
   cardinal walkway at least one tile wide. A marker blob may explicitly contain several separate
   entrance groups; it is a generation domain, not automatically one logical room. Each group's
   required routes and any separation between groups are validated under section 5.4.
7. Spaceproofing, exterior window proportion, and traversal are explicit constraints. Cover,
   sightlines, and defensibility are configurable secondary goals.
8. Impossible requests produce a bounded, explained fallback or a failure without publishing a
   broken layout. No retries continue indefinitely.
9. Low-effort procedural interiors use room themes that select tile, wall, lighting, and entity
   packs. Related entities aggregate into coherent room contents instead of independent uniform
   scatter. Theme goals are specifiable preferences with explicit outcomes and fallbacks.
10. Automatically fit rooms to the available shape, preferring exterior placements whose contours
    closely match the target exterior. Infer interior placements and fill residuals without requiring
    fixed slots. Explicit constant regions remain unchanged across seeds.
11. Paint custom generation areas with tile markers; each cardinally connected component becomes
    a discrete configurable blob. Attach labeled entrance markers and choose among complete
    acceptable entrance connection configurations.
12. Reuse relational assemblies such as a microwave on a table, chairs adjacent to and facing that
    table, or a device on a corner table facing reachable open floor. Relations are explicit data,
    with engine-validated support, orientation, and access.

Version 1 covers generation before players enter the output. It includes preserved authored rooms
inside explicit constant regions of a new generated host. Content mode governs the remaining
generated area; `Procedural` may therefore surround a constant authored landmark without using any
prefabs for its generated rooms. Arbitrary regeneration of an occupied live station, multi-grid routes,
vertical connections, functional power/plumbing, and balanced combat simulation are later extensions.
Air retention is in scope; supplying breathable gas, working power, and job access must be requested
through explicit content/integration profiles rather than inferred from the word "room."

## 2. Existing repository integration

Read [CONTRIBUTING.md](../../CONTRIBUTING.md) before implementation. The accompanying
[procgen guide](procgen.md) describes the current system. Useful source entry points are:

| Existing source | Relevant behavior and limit |
| --- | --- |
| [DungeonRoomPrototype](../../Content.Shared/Procedural/DungeonRoomPrototype.cs) | Atlas rectangle, tags, and an optional ignored tile for irregular copied shapes; no explicit entrance contract or atomic variant identity. |
| [DungeonRoomPackPrototype](../../Content.Shared/Procedural/DungeonRoomPackPrototype.cs) and [DungeonPresetPrototype](../../Content.Shared/Procedural/DungeonPresetPrototype.cs) | Lists of room and pack rectangles; useful migration inputs. |
| [PrefabDunGen](../../Content.Shared/Procedural/DungeonGenerators/PrefabDunGen.cs) | Presets, room whitelist, and a fallback tile. |
| [DungeonSystem.Rooms](../../Content.Server/Procedural/DungeonSystem.Rooms.cs) | Template placement, rotation, tiles, entities, and decals. Its entity loop spawns by prototype ID; do not assume that arbitrary map entity overrides or entity references survive this copy. |
| [DungeonJob](../../Content.Server/Procedural/DungeonJob/DungeonJob.cs) | Layer dispatch, yielding, and generation lifecycle. |
| [DungeonJob.Corridor](../../Content.Server/Procedural/DungeonJob/DungeonJob.Corridor.cs) | Existing entrance collection, spanning-tree connections, and corridor widening. |
| [DungeonJob.ExternalWindow](../../Content.Server/Procedural/DungeonJob/DungeonJob.ExternalWindow.cs) | Existing exterior window placement; the KS percentage definition below needs its own implementation. |
| [DungeonTests](../../Content.IntegrationTests/Tests/Procedural/DungeonTests.cs) | Existing geometry/prototype test conventions. |

Introduce a separate KS planning pipeline. Reuse engine map loading, serialization, collision,
atmosphere, tile, and door services through adapters after checking their actual contracts. Existing
dungeon layers that write directly to a grid MUST NOT execute inside speculative planning.

Suggested homes:

| Path | Responsibility |
| --- | --- |
| `Content.Shared/_KS14/Procedural/` | Serializable requests, prototypes, marker components, result/diagnostic contracts, and pure geometry where shared use warrants it. |
| `Content.Server/_KS14/Procedural/` | `KsProcgenSystem`, planning algorithms, prefab inspection, validation, materialization, and admin commands. |
| `Content.Client/_KS14/Procedural/` | Debug overlay only when needed; clients do not rerun generation to determine authoritative geometry. |
| `Resources/Prototypes/_KS14/Procedural/` | Themes, constraints, room/layout definitions, and marker prototypes. |
| `Resources/Maps/_KS14/Procedural/` | Authored rooms and example layouts. |
| `Content.IntegrationTests/Tests/_KS14/Procedural/` | Geometry, solver, prefab, and engine validation tests. |

Use `Ks` prefixes for new potentially colliding type/prototype names. Keep ECS components as data.
Prefer an explicit KS entry point initially. An upstream `IDunGenLayer` adapter may be added only
after the independent pipeline works; mark any upstream edits according to CONTRIBUTING.md.

## 3. Spatial model and ownership

### 3.1 Coordinates and masks

All planning uses integer tile indices in one grid's local coordinates. `(0, 0)` identifies a tile,
not an entity's center. Entity conversion adds the engine's tile-center offset exactly once. Bounds
are half-open: `[minX, maxX) x [minY, maxY)`. Negative indices are valid.

Cardinal adjacency is exactly `(x+1,y)`, `(x-1,y)`, `(x,y+1)`, `(x,y-1)`. Diagonal contact never
establishes movement, a room connection, or a corridor. Visibility rays may travel diagonally;
visibility and movement are separate graphs.

| Mask | Definition |
| --- | --- |
| `targetMask` (`T`) | Cells the caller asks this operation to classify/fill. |
| `envelopeMask` (`E`) | Additional cells explicitly authorized for structural closure or connector approaches. Empty by default. |
| `writableMask` (`W`) | `T union E`, minus preserved cells. Every generated tile/entity footprint must fit here. |
| `preservedMask` (`P`) | Existing/authored cells and entity footprints that must retain their content. They may participate in traversal. |
| `voidMask` (`V`) | Explicitly excluded cells intended to remain empty/space. They cannot overlap `T`, `E`, or preserved occupied content. |
| `contextMask` | Read-only surrounding cells needed to inspect existing connections, obstacles, and atmosphere boundaries. Reading them grants no write permission. |
| `routeReserve` | Planned walkable cells and doorway clearances protected from later blocking content. |

Preserved cells may lie within `T`; they count as already assigned when computing residual fill.
Envelope cells are not an invitation to expand the requested floor area: each use must be tagged as
closure or an explicitly requested connector. Unused envelope cells remain unchanged.

Every cell of `T` MUST finish with exactly one primary disposition: preserved, prefab-owned,
procedural floor, passage, structure, or an explicitly permitted sealed solid. No implicit holes.
Fixtures, decals, and other entity layers are additional occupancy information, not alternative
primary owners. Multiple entities may coexist only when the collision/placement adapter permits it.

Two geometry interpretations are supported and MUST be explicit:

* `Footprint` (default): `T` includes floors and structure. Boundary walls can consume cells of `T`.
* `InteriorFill`: `T` identifies cells intended to remain usable interior. Required enclosing
  structure must already exist in `P`/context or fit in `E`. Consuming interior cells for walls is
  only legal as an explicitly permitted fallback.

SS14 walls generally occupy tiles. Do not assume infinitely thin edge walls make a 1x3 standalone
interior spaceproof. An abstract boundary-edge model is permitted for validation, but every edge
must resolve to a supported real tile/entity footprint before a plan passes.

### 3.2 Empty space has two different authoring meanings

* `Generate`: leave this portion of a layout unfilled by prefabs and hand it to procedural fill.
* `KeepVoid`: preserve a hole/courtyard/space area. It is excluded from the fill target and receives
  the necessary surrounding hull if the adjoining interior requires spaceproofing.

The default for unassigned target cells in `Hybrid` and `Procedural` is `Generate`. In `Prefabs`, an
uncovered room area is an error unless declared passage, preserved content, or a permitted solid.
Whitespace outside a mask is never treated as a request to generate.

### 3.3 Shape authoring and validation

Support explicit cell lists, rectangle unions/subtractions, and a text-mask authoring adapter.
Normalize all of them to the same mask before solving. Text masks declare their origin; the top
text row maps to the greatest Y. A polygon adapter may be added with an explicit rasterization
rule (tile center inside polygon, declared edge inclusion); it must not silently change cell masks.

Reject contradictory masks, duplicate stable IDs, out-of-range coordinates, invalid transforms,
empty required port spans, and cyclic layout references before search. An empty `T` returns
`NoOp` when there are no required outputs/ports; otherwise it is an invalid request.

Disconnected masks use `connectivityPolicy`:

* `SingleNetwork` (default): all required rooms/ports must share one traversable network. Islands
  require an authorized path through preserved content or `E`; otherwise the request fails.
* `PerIsland`: validate each island independently and report its root. Never imply the islands are
  mutually reachable. Each island still needs its own valid closure when spaceproofing is required.
* `DeclaredNetworks`: explicitly authorize the separate networks named by the selected entrance
  configuration. Validate every network's roots/access separately, including several networks in
  one geometric island. This is opt-in; local entrance groups do not silently override `SingleNetwork`.

### 3.4 Connected area markers

Provide one paintable `KsProcgenAreaCell` marker and a reusable `ksProcgenAreaProfile` containing
mode, geometry interpretation, theme/library references, constraints, budgets, and an optional
entrance connection profile. Marker instance fields are `channel` (default `Default`), `profile`,
and optional `blobId`. Markers are editor metadata, never runtime furniture or solid obstacles.

Normalize markers as follows, before routing or candidate selection:

1. Snapshot markers on one grid and convert their positions to exact tile cells. Require tile
   alignment; reject ambiguous fractional placement rather than snapping it differently per run.
2. Apply explicit `KeepVoid` subtractions, then within each grid/channel form maximal components
   of cardinally adjacent remaining area-marker cells.
   Diagonal contact does not join components. Each component is one blob. Markers paint cells,
   not just an outline: unmarked enclosed holes remain outside the target. No bounding rectangle,
   polygon fill, or noise growth is inferred. A one-cell bridge joins two lobes until removed.
3. Require a single profile and at most one distinct nonempty explicit `blobId` per component.
   Conflicting settings fail; profile changes inside connected paint must not silently split it.
   Identical duplicate paint at a cell may be deduplicated; conflicting duplicates fail. Separate
   channels remain separate domains, but overlapping writable claims still fail ownership checks.
4. Use the explicit blob ID when supplied, otherwise derive one from the stable host key, channel,
   and sorted component cells. Do not use entity UIDs or enumeration order. Reject a reused explicit
   blob ID on two disconnected components. Editing a derived-ID mask may change its seeded choices;
   an explicit blob ID keeps its identity, while geometry still enters the replay hash.
5. Apply the profile to that exact component. Preserved/constants remain claims within the domain and do not erase
   its identity. Snapshot external context as read-only. Markers grant no additional envelope or
   write permission. Publish/discard all blobs in an invocation atomically by default.

A blob is a shape/configuration boundary. It may generate multiple rooms, passages, or isolated
entrance groups; it is not itself a room or a guarantee of connectivity. Disconnected blobs do not
gain paths across unmarked tiles. Shared profiles are reusable, but ports and selected configurations
are scoped to each blob instance. Optional explicit cross-blob links remain ordinary request links.

Example profile (new schema):

```yaml
- type: ksProcgenAreaProfile
  id: KsServiceArea
  mode: Procedural
  geometryMode: InteriorFill
  theme: KsProcgenSimpleOffice
  connectivityPolicy: DeclaredNetworks
  entranceConnections: KsServiceBlobConnections
```

Paint `KsProcgenAreaCell` instances with `profile: KsServiceArea` and `channel: Service` over the
desired cells. Set a consistent optional `blobId` for stable author-assigned identity. Paint another
disconnected patch with the same profile to get another independent instance. `InteriorFill` in
this example requires inspected existing enclosing structure or an explicit writable envelope.

## 4. Data and authoring contracts

The following are proposed contracts, not current engine APIs. Serialized names use camelCase;
IDs use PascalCase. Implementation may split types for clarity but MUST preserve these semantics.

### 4.1 Generation request and profiles

| Field | Contract/default |
| --- | --- |
| `requestId`, `seed`, `generatorVersion` | Required replay identity; version names the algorithms, defaults, and random implementation. |
| `mode` | `Procedural`, `Prefabs`, or `Hybrid`. |
| `shape`, `geometryMode` | Normalized masks from section 3; `Footprint` default. |
| `areaMarkers`, `areaProfiles` | Optional source adapter for exact component masks and per-blob configuration (section 3.4); direct masks remain supported. |
| `theme` | Floors, walls, airtight windows, doors, allowed furnishings, and classification adapters. Required for materialization. |
| `roomThemes` | Weighted room-theme pool or one fixed theme; request theme supplies the default room theme when omitted. |
| `themeGoals` | Typed appearance, lighting, and content goals; override theme defaults within request-hard constraints. |
| `roomLibrary` | Eligible room prototypes, atomic layout families, and tag/weight filters. Required for automatic prefab selection; theme may supply it. Not used for pure procedural rooms. |
| `placementPolicy` | `AutomaticFit` by default; controls exterior matching, preferred prefab coverage, size mix, and residual fragmentation. |
| `exteriorMatchBand`, `matchVoidBoundaries` | Near-match distance cap (default 3 tiles) and whether exterior matching includes space holes/courtyards (default true). |
| `constantRegions` | Explicit immutable map areas or pinned authored layouts with exact masks/transforms/content. Default empty; these are deliberate overrides. |
| `choiceRegions` | Optional authored constraints offering interchangeable layouts at specified locations. Empty by default; automatic generation discovers its own placement opportunities. |
| `anchors`, `externalPorts` | Stable local alignment and connection metadata; default empty. |
| `rootCells` | Traversable access roots; supplied roots are hard requirements. If absent, choose the lexicographically first usable cell per permitted network and report that external station access was not established. |
| `connectivityPolicy` | `SingleNetwork` by default. |
| `entranceConnectionProfiles` | Per-blob or explicit-domain references to atomic alternatives of named entrance groups (section 5.4). Default omitted: ordinary all-connected behavior. |
| `traversalProfile` | Reference actor collision footprint, door access capabilities, and required operational states. Required; theme may supply a default. |
| `constraints` | Named hard conditions and soft targets, with scope and tolerances. |
| `fallbackPolicy` | Ordered, explicitly permitted degradations. Default permits soft-target misses and one-tile sparse fill; it never disables a hard condition. |
| `budgets` | Tile/candidate/search/path/repair/decoration/tactical limits from section 14. |

`constantRegions` controls deliberate immutable overrides only. Do not implement fixed room
positions as the base generation model. Constant regions may contain any authored area, including
several rooms, corridors, or landmarks; their contract is specified in section 7.3.

Profiles may define desired room sizes, room count, corridor width, loops, furniture density, and
window/cover targets. Size classes are theme-defined area bands, not fixed placement modules.
Counts and sizes are soft unless marked required. Hard/soft status MUST be visible in the preview.

An optional `sizeMix` gives target counts or area shares by size class. Define shares against total
room interior area, excluding corridors, hull, and void; normalize positive weights. Required
minimum counts are validated separately. The solver may therefore choose a large room in one
region and small rooms elsewhere without forcing all regions to use the same scale.

### 4.2 Room definition: `ksProcgenRoom`

Each room definition contains:

* Stable ID, atlas/map resource and extraction bounds, exact occupied mask, usable interior mask,
  and any generator-owned seam cells. A bounding rectangle alone is insufficient for collision.
* Actual entity footprints, including shapes extending outside their anchor cell, plus preserved
  authored entity data, containers, references, decals, rotations, and relevant configuration.
* Alignment anchors and ports (section 5).
* Allowed quarter-turn rotations; default `[0]`. Reflection is disabled in version 1. A rotation
  must transform masks, ports, normals, entity offsets, fixture geometry, and decals together.
* Theme/tags, weight, unique-instance limits, exclusions/requires rules, and repair permissions.
* Spaceproof/traversal requirements and optional interior subdivision metadata.
* `exteriorCompatibleEdges`: cardinal boundary spans that can face the target exterior, including
  their actual hull/window support and any required facade orientation. Inspection may infer these
  from supported geometry; unsupported or ambiguous spans require metadata. Ordinary room ports
  cannot face vacuum simply to improve the shape score.

An authored room with mutually disconnected entrances is invalid as one logical room. It must be
repaired by its author or described as multiple logical rooms inside one atomic layout. A metadata
claim that a path is clear never overrides inspection of the actual map entities.

Default repair permissions prohibit deleting walls/furniture, moving doors, or modifying authored
entities. Authors may mark specific cells/entities as replaceable and specific wall spans as
generator-owned seams. The generator may edit only those locations and must report the edits.

### 4.3 Layout and choice region

A library may offer individual rooms, atomic layouts, and layout families without assigning any of
them a map position. Automatic fitting chooses their transforms and creates placement instances.
A layout family groups alternatives such as one large room, two medium rooms, or either small-room
pattern under a shared reservation/external interface. Its origin is discovered by fitting, unless
an optional authored override pins it. Singleton rooms are one-member atomic layout candidates.

`ksProcgenLayout` describes an indivisible package containing:

* A local claim mask and named alignment anchor.
* Room members, nested choice regions, and/or procedural subregions with local transforms.
* Explicit `Generate` and `KeepVoid` masks, passage reservations, seam ownership, and exposed ports.
* Required member-to-member port links and allowed optional links.
* Tags, weight, compatibility rules, and optional scoped count/size goals.

Every claim cell must be assigned to a member, seam/passage, or procedural subregion. `KeepVoid`
holes lie outside the claim's occupied/fill cells but remain protected by the choice region's
reservation. Parent validation checks all child references, transforms, and port endpoints.
Layout `KeepVoid` cells must match the caller's normalized `V`; choosing a layout cannot silently
convert required target cells to void. A region's reservation may include those protected holes,
while its writable claims remain constrained by `W`.

A `choiceRegion` is an instance with an ID, reservation mask, anchor, and alternatives. Its
`selection` is `ExactlyOne` by default. `ZeroOrOne` explicitly permits no prefab package; the
region's declared residual policy then handles the cells. Alternatives for a region share its
external contract (reservation boundary, required exposed port interfaces, and constraints), but
may have different interiors, member counts, and internal links.

Nested choices form an acyclic tree. A region reserves its entire domain against sibling/external
placements, then delegates ownership to its selected members and fills. An unoccupied quadrant of
an L layout is consequently available to that layout's filler, not to another layout's rooms.

### 4.4 Procedural room themes and reusable packs

The main authoring unit for a low-effort interior is `ksProcgenRoomTheme`. It combines surface,
lighting, and furnishing choices for a recognizable kind of room. It contains no fixed room map,
predefined rectangular footprint, or mandatory furniture coordinates. A caller should be able to
provide an arbitrary mask, one theme ID, and a seed to obtain a complete interior.

Keep request-level structural/safety constraints separate from room appearance. A room theme may
request a particular wall, but it cannot substitute a non-airtight wall where the request requires
an airtight boundary. Theme choices become solver candidates subject to the same validation as
other content. The initial station theme must include compatible fallback materials and a simple
working lighting option so a basic author does not need to define every pack personally.

| Room-theme field | Meaning/default |
| --- | --- |
| `id`, optional `parent` | Stable theme ID; single-parent inheritance, validated acyclic. |
| `tags`, `compatibleRegionTags` | Semantic classification and optional eligibility restrictions. |
| `tilePacks` | Weighted floor palettes, including optional passage/border/accent choices. |
| `wallPacks` | Weighted interior/hull wall palettes; palettes can name compatible doors/windows/frames. |
| `lightingPacks` | Weighted lighting plans and fixtures, including placement and supply assumptions. |
| `entityPacks` | Related furniture/items/decor; each reference has weight, count/density targets, and optional hard minima. |
| `goals` | Typed soft targets and boolean/enum preferences described below. |
| `sizePreference` | Soft area/shape suitability; no implicit minimum that rejects a 1x3 request. |
| `fallbackTheme` | Optional compatible simpler theme, with cycle detection and permission to substitute. |

Each pack has an ID, tags, a finite list of weighted candidates, applicability predicates, and
classification data needed by the relevant placement adapter. Zero weight disables a candidate;
negative/nonfinite weights and empty required pools are invalid. Prototype references must resolve.
Cache expansion of references per content version; do not repeatedly resolve the same packs per cell.

* **Tile pack (`ksProcgenTilePack`):** weighted palettes, each with a primary floor and optional
  accents/borders/transitions. Choose one primary palette per logical room by default. Variation
  forms patches, borders, or explicitly requested scatter; do not independently reroll an unrelated
  floor material for every cell. A one-cell fragment may use the primary tile alone.
* **Wall pack (`ksProcgenWallPack`):** compatible wall families plus supported doors, frames, and
  airtight windows. Declare which candidates can fulfill hull versus internal-partition roles.
  Choose one family per room/region by default. A shared seam is assigned one owner/family by the
  parent plan, using request structure rules and then stable owner ID to break equal preferences;
  neighboring themes cannot spawn two competing walls there.
* **Lighting pack (`ksProcgenLightingPack`):** fixture variants, mounting requirements, preferred
  spacing, brightness/color options, placement roles, and an explicit operating/supply profile.
  Roles include ceiling/free-standing lighting, wall mounts, and cluster/task lighting where
  supported by actual prototypes. A fixture requiring unavailable power cannot satisfy a working
  lighting target. Version 1 does not promise to build a station power network; use supported
  self-powered fixtures or a validated caller-provided supply contract.
* **Entity pack (`ksProcgenEntityPack`):** semantically related weighted entity entries and optional
  small assemblies, with instance-count/density targets and placement relations. Examples are
  office workstations, medical treatment, workshop tools, storage, and lounge furniture. This is
  reusable content for a procedural room, not a premade room map.

Theme inheritance resolves parent first. Scalar values override; a supplied pack-reference list
replaces the inherited list in full; omitted fields inherit. Goals merge by stable goal ID.
Request overrides then apply, but may not weaken request-hard constraints. Within a theme's pack
list, weighted entries are alternatives for a palette or compatible candidate content as defined
by the pack type; they are not all required simultaneously. For entity packs, the theme chooses a
dominant activity pack and may add compatible supporting packs. Explicit per-pack minima make a
pack mandatory. The resolved theme, palette, and pack choices are retained in the plan/report.
Entity packs also declare `activityTags`, compatible supporting tags, and optional room-scoped
exclusions. The theme admits all eligible mandatory packs first, chooses a dominant pack among
eligible activity candidates by weight, and adds only support packs compatible with the dominant
pack and each other. Contradictory mandatory packs reject that theme. Counts use inclusive integer
ranges; densities use distinct occupied cells, and hard minima take precedence over a soft density
target. A theme with no entity packs is valid and produces an unfurnished interior.

### 4.5 Specifiable theme goals

Expose a typed `goals` record rather than an unchecked collection of free-form flags. Initial
goals include the following; they are soft unless specifically made hard by the author:

| Goal | Meaning |
| --- | --- |
| `coherentContents` | Default true: cluster related entries and prefer a dominant room activity. |
| `preferWallPlacement` | Prefer eligible furnishing/lighting against usable walls. |
| `keepCenterOpen` | Penalize optional obstacles in the central usable area; mandatory routes remain protected. |
| `furnishingDensity` | Occupied furnishing-footprint cells / usable room cells before furnishing, in `[0,1]`; count overlapping legal layers once. |
| `clusterCount` | Preferred number/range of activity clusters, subject to available area/clearance. |
| `lightingCoverage` | Fraction of usable cell centers meeting a declared light-sampling threshold in the nominated operating state. |
| `maximumDarkRun` | Preferred maximum consecutive poorly lit cells along required routes. |
| `paletteConsistency` | Default room-wide primary palette; alternatives are region-wide or explicitly mixed. |
| `allowLooseItems` | Default true; items need valid floor/support/container placement and count caps. |
| `preferCover`, `preferAlternateRoutes` | Feed section 12's optional metrics; never override traversal. |

Every goal has a stable ID, typed target, scope, severity, and tolerance where applicable. Behavior
switches such as `allowLooseItems: false` are enforced eligibility filters; they cannot be ignored
as a soft miss. Other booleans describe scoring preferences. Validate
ranges and conflicting goals. A disabled preference does not disable an invariant: for example,
`keepCenterOpen: false` still cannot obstruct a required walkway. Pack filters such as allowed tags
are eligibility rules, not excuses to choose incompatible entities as a fallback.

Lighting coverage is evaluated using a documented adapter for the engine's light model, including
occlusion and actual fixture state. A provisional distance/occlusion estimate may guide planning,
but must be labeled estimated. If authoritative coverage cannot be evaluated, report `Unverified`;
a hard lighting target cannot pass on an unlabeled approximation. Report predicted and validated
coverage separately. An intentionally dark theme can explicitly set a low/zero target.

### 4.6 Entity packs and coherent activity clusters

An entity entry declares a prototype/table reference, weight, semantic role, footprint, allowed
orientations, placement constraints, and optional supporting-surface/container requirements. Roles
include primary furniture, seat, storage, equipment, task light, consumable, and decoration.
Prototype tables must expand to inspectable candidates before collision/fit validation; do not
spawn a random unknown footprint and hope that it fits afterward.

An optional reusable `ksProcgenAssembly` prototype specifies named members and their placement
relations. Entity packs reference assemblies with concrete entity bindings, weights, and count
goals. A pair is simply a two-member assembly; larger assemblies reuse the same relation model.
The complete schema and solver contract below replace role-order or nearest-entity guesses for
authored relationships. Assemblies define arrangements inside discovered rooms, not fixed rooms.

Distinguish mandatory members of an assembly from optional additions. Place the mandatory core
atomically; if its chair cannot fit, do not leave an incomplete desk-and-chair assembly that claims
to be functional. Optional papers or a lamp may be omitted with a soft-target report. Assemblies
may declare smaller valid variants (desk + chair, then standing workbench), selected atomically.
If a plain entity list has no authored assembly, use each entry as a singleton core and group
compatible entries near the chosen pack's anchor. This provides low-effort defaults without
requiring authors to hand-script every arrangement.

Coherence works at both room and cluster scale:

1. Choose a room theme using assignments, region eligibility, preferred size, and seeded weights.
   Pick a dominant activity pack before individual entities. Compatible support packs can add
   storage, lighting, and decoration.
2. Select feasible cluster anchors outside route/port reservations. Score placements for pack
   proximity, wall/center preferences, usable approaches, and theme goals. Stable seeded tie-breaks
   provide variation. Room geometry and real fit remain decisive.
3. Build one cluster's mandatory core, then its optional related contents. Keep a cluster within one
   logical room; do not scatter a desk's chair into an adjoining corridor or another room.
4. Fill remaining eligible areas with more clusters from the same or compatible packs, favoring
   spatial grouping. Default grouping cost is the sum of cardinal feasible distances to that
   cluster's anchor, with a penalty outside its declared radius (default 4 tiles). No available
   path/interaction approach makes a functional member infeasible, even when Euclidean distance is
   small. Add incompatible activities only when the theme explicitly permits mixed use.
5. Revalidate routes, required interaction approaches, hull, mounting, and operational lighting.
   Roll back a failed cluster's entire core and all its dependent children/support reservations.

Pack aggregation is a preference by default; collision, access, and mandated assembly relations are
hard for any accepted assembly. If a pack has an explicitly required minimum of one workstation,
the generator must place a valid workstation or fail/perform an authorized relaxation. It cannot
meet that minimum with a loose desk item or silently discard the pack. Optional packs may be
reduced/omitted on tiny masks, with requested/achieved counts in the report.

#### Reusable relational assemblies

An assembly has a stable ID, named `members`, an `anchorMember`, `relations`, optional ordered
`variants`, and placement preferences. Each member references either an entity or a required
binding supplied by its entity-pack reference. Bindings resolve to finite inspectable prototypes
before solving. A hypothetical future device can use the same assembly when bound to a supported
prototype; an unresolved binding is an input error, never an instruction to invent an entity.

Member fields include `id`, `entity` or `binding`, `minimumCount` (default 1), `maximumCount`
(default 1), `allowedQuarterTurns` (all four, filtered by prototype capability), `rotationMode`
(`AssemblyRelative` default or `Independent`), `localQuarterTurns` (default 0), and required placement
capabilities. `AssemblyRelative` carries the declared local rotation rigidly with the assembly;
`Independent` solves the member rotation from its allowed turns after choosing the assembly pose.
An optional `approachLanding` is an unrotated tile offset from the member origin, carried with
the selected member rotation. For normally blocking/vault-required furniture it must be outside
and cardinally beside the declared footprint; clear members may also nominate an occupied clear
tile. Bound each offset coordinate to +/-9. The selected landing must remain clean and reachable
from the room's permitted root; preserve its route through subsequent furnishing additions.
Minimum counts form the
mandatory core. Instances above the minimum are optional additions with reported shortfalls.
Expand repeated members to stable IDs such as `Seat/0` and `Seat/1`. An omitted optional member
removes its incident relations; any mandatory dependent requires its target to exist. Reject
contradictory counts or missing targets. A selected variant defines a complete valid core; never
combine mandatory members or relations from competing variants.

Each relation has an ID, a `subject` member, a relation `kind`, a `target` member where applicable,
typed parameters, and a severity (`Required` by default). For a repeated subject, the relation
applies to every accepted instance. A target must resolve to one named instance; implicit pairing
of two repeated sets is forbidden. Add explicit instance bindings if that use case is needed.

| Relation | Required semantics |
| --- | --- |
| `OnSurface` | Place subject in a supported surface slot on target. Reserve capacity, size/footprint, mounting constraints and actual engine placement mode. Same XY tile is allowed only through this validated support relationship. It does not make the supporting tile walkable. |
| `InContainer` | Resolve a named compatible container on target, capacity and insertion rules. Preserve the parent reference during staging. Contained contents do not count as floor occupants or usable exposed machines. |
| `AdjacentTo` | Subject and target footprints share a cardinal boundary at the declared distance (default 1 tile between nearest occupied cell centers). Corner-only contact fails; never infer adjacency from anchor distance for wide furniture. |
| `FacingTarget` | Subject's nominated face points toward a reachable use-facing edge/slot of target, with cardinal alignment and bounded interaction distance. A chair may face a blocking table without requiring a walking path onto that table. Its own seating approach must remain clean. |
| `UsesSeat` | Interactive subject explicitly associates its usable approach with one target seat instance; require seat reachability, compatible device reach, and the seat facing that device. Generic proximity cannot substitute for this relation. |
| `Near` | Compare the shortest cardinal clean path between selected interaction approaches, otherwise declared `approachLanding` tiles, otherwise the origin of a `Clear` member. Blocking/vault-required furniture without a selected approach or declared landing has no implicit endpoint. Require the shortest distance in `[minimumDistance, maximumDistance]`; both defaults remain 1 and 4. A shared landing can be distance 0 when explicitly permitted. Walls and all normally blocking/vault-required footprints are excluded. A wall-separated Euclidean neighbor does not qualify. |
| `AtCorner` | Unary context relation: targetless subject has backing support on two perpendicular sides from inspected/planned room walls. An arbitrary concave outline or another item's occupancy is not automatically a supported wall. `Preferred` permits other placements; `Required` demands a corner. |
| `FacingOpenSpace` | Unary context relation: subject faces a reachable permitted approach outside its occupied/supporting obstruction. Among legal faces score empty traversible floor or explicitly associated usable seating, then bounded forward clearance. A visually empty but disconnected pocket fails. |

All relations transform with the assembly; use the documented clockwise authoring turns. Member
rotation is relative to the chosen assembly transform unless explicitly allowed to solve independently.
For example a table can use a corner orientation while its supported device independently picks
the open side. `FacingTarget` and `FacingOpenSpace` may coexist only if one orientation satisfies
both. Wall, collision, support, and interaction capability adapters determine actual usable faces.
Authoring a relation cannot grant a prototype unsupported anchoring, surface stacking or interaction.

The support/container dependency graph MUST be acyclic and is materialized parent before child.
Do not reject every cycle in the general constraint graph: `AdjacentTo` plus `FacingTarget` between
the same pair is valid. Solve spatial relations together with bounded backtracking; reject a
proven contradiction, and report budget exhaustion distinctly from infeasibility.

Reusable prototype and entity-pack binding (R01 implements schema loading; relational placement
remains subject to R02/R06 and prototype capability validation):

```yaml
- type: ksProcgenAssembly
  id: KsTableWithDeviceAndSeats
  anchorMember: Table
  members:
  - id: Table
    binding: TableEntity
  - id: Device
    binding: DeviceEntity
    rotationMode: Independent
  - id: Seat
    binding: ChairEntity
    minimumCount: 1
    maximumCount: 2
    rotationMode: Independent
  relations:
  - id: DeviceSupport
    subject: Device
    kind: OnSurface
    target: Table
  - id: DeviceFront
    subject: Device
    kind: FacingOpenSpace
    approachPolicy: EmptyOrAssociatedSeat
  - id: SeatPosition
    subject: Seat
    kind: AdjacentTo
    target: Table
  - id: SeatDirection
    subject: Seat
    kind: FacingTarget
    target: Table
  - id: CornerPreference
    subject: Table
    kind: AtCorner
    severity: Preferred

- type: ksProcgenEntityPack
  id: KsBreakroomFurniture
  assemblies:
  - id: BreakroomCluster
    assembly: KsTableWithDeviceAndSeats
    bindings:
      TableEntity: Table
      DeviceEntity: Microwave
      ChairEntity: ChairOfficeLight
    weight: 1
```

The example bindings are candidate IDs to validate against actual content/capabilities, not a claim
that a particular microwave currently supports table mounting. A table/microwave pair omits the
seat member and its relations; a dining arrangement omits the device. Authors can require the
corner by changing its severity. Another pack may bind a future tabletop device to `DeviceEntity`
without changing the spatial rules. A declared seated-use relation must explicitly associate the
device with a seat instance; merely choosing `EmptyOrAssociatedSeat` does not invent that link.

Placement uses separate ground-blocking, surface-slot, container, and approach reservations. Record
assembly/variant/member IDs, support parent and slot, transforms, and resolved relation witnesses
in proposals. A blanket "all entity footprints must be disjoint" check MUST be replaced with
adapter-approved layered occupancy; arbitrary overlaps remain forbidden. A surface-mounted machine
derives its usable front approach from its actual supported pose and the supporting object's
exposed edge. A deep table may make the device unreachable even when stacking fits. Validate real
interaction reach and clean walking to the approach; never count vaulting over the table as access.

Reserve required approaches before optional members. Validate all earlier members after each
addition; retry other transforms, anchors, or complete declared variants when any mandatory
relation fails. Roll back the failed core together with support slots, child entities and route
reservations. Plain-list packs compile to grouped singleton cores; failure of an optional singleton
must not discard unrelated valid singletons. Mandatory pack counts refer to complete valid cores,
not arbitrary entity totals. T15's current whole-list atomic treatment requires R02 below.

#### Machine facing and clean access to seats

Interactible machines, including computers, MUST present their interaction face toward a reachable
usable approach and away from an adjacent backing wall. Default unrotated facing is down/south.
Use this explicit authoring convention for quarter-turn facing angles:

| Facing rotation | Interaction face | Suitable backing wall |
| --- | --- | --- |
| 0 degrees | Down/south | Above/north |
| 90 degrees | Left/west | Right/east |
| 180 degrees | Up/north | Below/south |
| 270 degrees | Right/east | Left/west |

Thus a computer placed next to a right wall rotates by 90 degrees to face left into the room.
These are clockwise authoring turns from the down-facing baseline. The placement adapter MUST
translate them to engine rotation and any prototype-specific sprite/interaction offset; do not
assume a raw engine angle has the same sign or that every sprite uses the same baseline. Transform
the machine's footprint, interaction face, approach cells, and assembly relations together.

At a corner, consider both inward-facing orientations. For example, at an upper-right corner,
down and left are candidates. Filter out any orientation whose front approach is blocked or has
no clean route to the room's passage network. Among the remaining candidates, prefer a face
opening onto empty traversible floor or a usable chair tile. An explicitly associated workstation
chair facing the machine takes priority for a seated assembly; otherwise prefer clear empty floor,
then compatible traversible seat tiles, with available approach clearance and stable seeded
tie-breaking. Never prefer an apparently empty tile that is an inaccessible pocket.

An entity entry may declare `interactionFacing`, `interactionApproachCells`, and
`requiresSeatedUse`; defaults use the down-facing baseline and at least one clear tile immediately
in front of the footprint. Approaches for larger machines must be derived from their actual front
edge rather than just their anchor tile. A supported multi-sided interaction model may declare
additional approaches, but cannot excuse placing the nominated primary face into a wall.

Every generated usable chair MUST have a clean cardinal passage from the room's accessible network
to a valid position for reaching, sitting in, and leaving that chair. Reserve that approach before
adding surrounding furniture. A chair can satisfy a machine's front approach only if the actor can
actually reach and use it; a chair trapped behind a desk does not make the desk/computer accessible.
Its tile counts as a through-route only when the collision/seat adapter confirms ordinary traversal;
an accessible seating endpoint is not automatically a corridor. For a seated workstation, validate
both the chair-to-machine facing relation and the route to the chair.

**Anything that normally blocks movement and that you need to vault over is disruptive to clean
passage, including tables and desks.** Treat its occupied collision footprint as blocked in all
required clean-passage searches, even when the reference actor can vault it or interact with
something across it. Classify obstacles by their movement behavior, not by a table-specific
prototype list. The same applies to obstacles requiring climbing, crawling, or pushing aside.
Surface support for an item does not make the supporting object a walkable route. Clean access may
go around the obstacle; it cannot rely on crossing it.

If no legal facing/access arrangement fits, try another allowed rotation, position, or assembly
variant; omit an optional machine/chair or reject a required assembly. Do not retain an unusable
machine or trapped chair and claim its functional content goal was met. Preserved prefabs are
validated under the same declared functional requirements, but rotating/moving authored entities
still requires their explicit repair permission.

### 4.7 Minimal authoring example

The desired end-user workflow is a mask plus a reusable room theme. The initial office content in
`Resources/Prototypes/_KS14/Procedural/office.yml` now defines the IDs below. It is a validated
selection example; spawning, working-light checks, and final furnishing placement are still pending:

```yaml
- type: ksProcgenRoomTheme
  id: KsProcgenSimpleOffice
  tilePacks:
  - pack: KsProcgenOfficeTiles
    weight: 1
  wallPacks:
  - pack: KsProcgenOfficeWalls
    weight: 1
  lightingPacks:
  - pack: KsProcgenOfficeLights
    weight: 1
  entityPacks:
  - pack: KsProcgenOfficeWorkstations
    weight: 4
  - pack: KsProcgenOfficeStorage
    weight: 1
  goals:
    coherentContents: true
    keepCenterOpen: true
    furnishingDensity: 0.2
    lightingCoverage: 0.85
```

This abbreviated `goals` form expands to soft goals with IDs equal to field names and documented
default tolerances; support an expanded form for severity/scope/tolerance. Initial density and light
coverage tolerance is 0.05 absolute fraction. Booleans guide scoring and are reported with measured
violations, not treated as unmeasurable success claims. Hard goals need an explicit predicate in the
expanded form. Unknown shorthand keys fail validation.

`KsOfficeWorkstations` could supply a desk/chair core with optional papers/task lighting;
`KsOfficeStorage` supplies shelving/cabinets with compatible stored items. One small room might
receive one workstation, a larger room several grouped workstations plus storage. A narrow 1x3 fill
may receive only coherent flooring and a supported nonblocking light, with omitted furnishing
reported. A theme referencing existing packs is sufficient for routine authors; creating new room
maps or assembly definitions is optional.

## 5. Alignment markers and entrance ports

### 5.1 Alignment

Author marker entities such as `KsProcgenAnchor`, `KsProcgenPort`, `KsProcgenRegion`, and
`KsProcgenVoid`, backed by data-only components. They are editor-visible metadata and MUST be
consumed/removed from the published output. Prototype metadata and markers normalize to one
representation; conflicting duplicate definitions fail validation.

An anchor has a stable instance-local name, integer cell position, optional cardinal orientation,
and compatibility tags. For room anchor `a`, target anchor `b`, and allowed quarter-turn `R`, use:

```text
transformedCell(p) = b + R * (p - a)
transformedNormal(n) = R * n
```

Transform entity centers in the corresponding continuous tile coordinate frame; do not rotate
about an implicit bounding-box center or round fractions independently. Matching two or more
anchors requires one transform satisfying all of them. Ambiguous transforms are separate solver
candidates in stable order. An anchor aligns content; it does not establish connectivity.

### 5.2 Ports

A port is an explicit traversable opening, not an inferred point on a room bounding box. Fields:

| Field | Meaning |
| --- | --- |
| `id`, `ownerRoomId` | Stable identity. |
| `thresholdCells` | Contiguous cardinal span occupied by the doorway/opening. |
| `outwardNormal` | Cardinal direction from the room toward the connection. |
| `insideApproach`, `outsideApproach` | Required clear cells on each side, at least one tile deep. |
| `width`, `minimumClearWidth` | Geometric opening and required movement clearance; minimum 1. |
| `connectionClass` | Compatible passage, ordinary door, or external airlock/docking interface. |
| `requirement` | `Required` default; `OptionalSealable` only by explicit authoring. |
| `destinationPolicy` | `NetworkPreferredAllowStub` default, `NetworkRequired`, or `FixedPort`. |
| `accessProfile`, `pressureBoundary` | Traversal capabilities and atmosphere behavior. |
| `repairPolicy` | Specific permission to move/replace/seal; absent means forbidden. |

Every real doorway/opening in a prefab boundary MUST be declared or classified as intentionally
sealed. Validate this from geometry; missing markers cannot excuse an unconnected door.

Directly matched ports require opposite normals, compatible classes, sufficient coincident or
adjacent spans, and valid approach cells. With walls occupying tiles, prefer a generator-owned
shared seam containing a single doorway. Adjacent fully authored walls may instead produce a
two-door vestibule if both approaches fit. Never spawn duplicate walls/doors in one seam cell.
Partial overlap of a wide entrance is insufficient unless its declared clear-width contract and
remaining sealed spans are explicitly satisfied.

An exposed layout port maps to a member port or a declared passage terminal. Every exposed required
port exists in every alternative. Alternatives may change internal connections freely as long as
their external contract and section 8 invariants remain satisfiable.

### 5.3 Entrance markers attached to generated blobs

Provide a generic `KsProcgenEntrance` editor marker with `portId`, `channel`, optional `blobId`,
threshold span, and a cardinal direction into the generated area. `portId` is a string local to
that blob; numeric labels such as `1` are valid. Ten convenience prototypes labeled 1 through 10
may preset this field, but ten component types or a hard ten-entrance limit are not required.

Place it at an authored threshold next to the painted blob and point its inside approach into
the blob. Normalize it to the section 5.2 port contract, including a real external landing and
door/access state. An existing threshold outside the blob is read-only unless explicitly claimed
by the request. It receives no write permission merely because a marker sits there.

Infer ownership only when the directed inside approach identifies exactly one blob in the channel;
otherwise require an explicit blob ID or report an ambiguous/orphan marker. For a wide opening,
all inside-approach cells must belong to the same blob. Duplicate port IDs in one blob fail; the
same labels in different blobs are valid. Missing real doorways are still errors under section 5.2.
Marker removal happens only in generated/published copies; do not delete the source authoring map.

### 5.4 Entrance connection profiles and atomic configurations

`ksProcgenEntranceConnections` contains weighted, named `configurations`. One configuration is
selected for a blob instance; selection is part of the same bounded search as room fitting and
partitioning. Each configuration defines groups of port IDs and a `mode`:

* `ExactComponents` (default when a profile is authored): every group's entrances MUST share a
  clean route within the domain, and entrances in different groups MUST NOT share such a route
  within it. Internal walls or another valid permanent separation must realize this. An ordinary
  operable door across groups connects them and violates the contract.
* `RequiredConnections`: each group must be connected, but additional routes between groups are
  allowed. Use this for minimum connectivity wishes when isolation is not intended.

Example canonical data for the user's `1-3-4;9-8` configuration and another acceptable choice:

```yaml
- type: ksProcgenEntranceConnections
  id: KsServiceBlobConnections
  configurations:
  - id: ServiceAndStorage
    weight: 1
    mode: ExactComponents
    groups:
    - id: Service
      ports:
      - '1'
      - '3'
      - '4'
    - id: Storage
      ports:
      - '9'
      - '8'
  - id: CrossService
    weight: 1
    mode: ExactComponents
    groups:
    - id: Service
      ports:
      - '1'
      - '8'
    - id: Storage
      ports:
      - '3'
      - '4'
      - '9'
```

Only those five entrances exist in this example. If ten markers are actually placed, every one
must appear exactly once in every configuration or have an explicit permitted sealed disposition
in that configuration. Unknown IDs, duplicate membership, missing operational ports, empty groups,
negative/nonfinite weights, and empty eligible configuration sets fail validation. A singleton group
requires a real reachable interior landing/destination; it does not mean "discard this entrance."
Sealing requires the port's `OptionalSealable` permission and cannot erase an unrelated hard link.

The editor may accept one shorthand alternative per line, with an explicit profile-level mode:

```text
1-3-4;9-8
1-8;3-4-9
```

Here hyphens mean membership in one connected group, not a demanded route order, direct adjacency,
or an exhaustive list of graph edges. Semicolons separate groups; lines are alternatives, never
instructions to union their links. Shorthand labels are alphanumeric/underscore only; use canonical
YAML for labels containing delimiters. Whitespace is ignored, empty segments fail, and the editor
assigns persisted configuration/group IDs when importing. Numeric and named labels use identical
semantics. Explicit ordered corridors or mandatory direct links continue to use separate port-link
constraints; do not overload this shorthand with that meaning. References from area profiles use
`entranceConnections`; normalized requests scope the same reference under `entranceConnectionProfiles`.

The default `isolationScope` is `DomainLocal`: paths may use the blob's generated/approved preserved
cells and explicitly assigned seams, but cannot leave the blob and re-enter to satisfy a group.
External host corridors may connect different groups outside the domain. If the author requests
`isolationScope: RequestNetwork`, check absence of cross-group paths through all inspected authorized
host context as well; incomplete context leaves isolation unverified and blocks publication.

Section 3's global policy remains explicit. `SingleNetwork` can coexist with locally separate groups
when outside host paths connect them, but cannot coexist with multiple globally isolated groups.
Use `DeclaredNetworks` to authorize the latter and assign access roots or verified host entrances
per group. Without real host attachment, an automatically chosen local root establishes only local
traversability and must be reported as such. Reject contradictory global/local contracts before
search when provable. Do not silently rewrite the request to `PerIsland` or `DeclaredNetworks`.

Plan group membership before carving required routes. Each accepted walkable cell is assigned to
one permitted component; structures separate groups where needed. Unassigned residual floor joins
an allowed group or receives an explicitly permitted nonwalkable disposition. A blob with multiple
exact groups is not treated as one logical room; generated rooms cannot straddle their separation.
Preserved rooms/prefabs with ports spanning forbidden groups are incompatible unless the inspected
source already separates those ports into distinct logical rooms. Do not rewrite an immutable room
or invent disconnected-room metadata to make it pass.

Required and optional routes, corridor widening, open-plan merging, furnishings, lights, fallback,
and materialization MUST preserve both required reachability and forbidden connections. A failed
configuration rolls back its routes, rooms, seams and reservations before another is tried. Never
mix edges from different configurations. Seeded weighting orders feasible alternatives; geometry
can force a less-preferred configuration. No feasible configuration means a reported failure, not
permission to connect everything. Store the chosen configuration and actual port-component matrix
for replay and validation. Source format/order changes alone must not change normalized membership.

## 6. Atomic alternatives and blacklist rules

### 6.1 Choose complete packages

The primary solver variable is a complete room/layout candidate at a discovered position and
rotation. For a family instance, select one complete alternative, not independent members of its
competing patterns. Selecting an alternative atomically claims its reservation, member IDs, connections,
exclusions, and residual masks. Failure of any mandatory member or link rejects the entire choice.
Backtracking restores all of its reservations and generated consequences before another choice.

For candidates `c`, let `x[c]` be a binary selection variable. Enforce:

```text
ExactlyOne region r:  sum(x[c] for c in alternatives(r)) = 1
ZeroOrOne region r:   sum(x[c] for c in alternatives(r)) <= 1
Conflicting c,d:      x[c] + x[d] <= 1
c requires d:        x[c] <= x[d]
Every required room/member/link belongs to its selected package.
No cell has two physical owners, except an explicitly unified seam.
```

These equations define behavior; version 1 can implement bounded constraint propagation and
backtracking without an external SAT/ILP library. A geometric overlap check alone does not prevent
mixing disjoint members of competing layouts; region identity is mandatory.
For automatic placement, accepting a family candidate creates that region identity and reservation;
rejecting the candidate creates no region. `ExactlyOne` applies to an accepted family instance or
an explicitly required authored choice region, not to every potential placement in the library.
Overlapping alternatives at different origins still conflict through their whole reservations.

### 6.2 Blacklist/exclusion semantics

Support typed `excludes` and `requires` selectors over prototype IDs, instance IDs, and tags, with
an explicit scope: `Region`, `AncestorRegion`, `Adjacent`, or `Request`. Default to `Region`.
`Adjacent` means occupied/claim boundaries share at least one cardinal edge after transformation;
diagonal touch does not count. A request-wide unique count is a separate explicit constraint.

An exclusion is symmetric incompatibility even if authored on one side. `requires` is directional;
resolve the referenced instance or a satisfying selector count before accepting a complete plan.
Self-exclusion or impossible requirements reject the definition. Tags alone are never used as a
substitute for layout membership. Port compatibility, spatial ownership, and semantic exclusions
are separate checks with separate diagnostics.

Avoid manually maintaining "A1 excludes B1/B2/B3/B4" on every small room. One selected alternative
ID for that region excludes all siblings automatically. Scope instance IDs so selecting an L
layout in region West does not prevent selecting a four-room layout in region East.

### 6.3 Required authoring example

This is a logical sketch of one reusable layout family with four possible subareas. Its position
is normally discovered by automatic fitting. Letters represent
room footprints; actual walls, approaches, seams, and corridor cells must be included in the
authored masks. It is not a one-character-per-tile playable map.

```text
One large       Two medium      Three small, L       Four small
+---------+     +----+----+      +----+----+           +----+----+
|         |     | M1 | M2 |      | A1 | A2 |           | B1 | B2 |
|    L    |     |    |    |      +----+----+           +----+----+
|         |     |    |    |      | A3 | G  |           | B3 | B4 |
+---------+     +----+----+      +----+----+           +----+----+
```

`G` is a generated residual area owned by the three-room alternative. Example required internal
links: L-pattern `A1-A2`, `A1-A3`, `A2-G`, `A3-G`; four-room pattern `B1-B2`, `B2-B4`, `B4-B3`.
Both expose the same region-level required entry interfaces, mapped to suitable members. The
four-room pattern may have completely different door positions internally.

Selecting the L-pattern creates A1, A2, A3, and G together. It can never insert B4 into G. Selecting
the four-room pattern creates B1 through B4 together. A failed G connection rolls back A1-A3 before
trying B1-B4. A larger parent layout can allocate three separate such regions and select different
size classes in each; physical overlap of a large room and small rooms is never allowed.

Optional pinned-choice authoring syntax (new schema to implement; not loadable by the current
engine). The normal workflow instead references this family from `roomLibrary` with no map anchor:

```yaml
choiceRegions:
- id: WestBlock
  mask: WestBlockMask
  anchor: WestBlockOrigin
  selection: ExactlyOne
  alternatives:
  - layout: KsWestLarge
    weight: 1
  - layout: KsWestMediumPair
    weight: 2
  - layout: KsWestSmallL
    weight: 3
  - layout: KsWestSmallFour
    weight: 3
```

`KsWestSmallL` declares A1-A3, their links, and a `Generate` subregion using the fourth-area mask.
`KsWestSmallFour` declares B1-B4 and their links. Each declaration includes exact cell geometry and
external port mappings; those details cannot be inferred from the IDs or from this sketch.

## 7. Planning pipeline

Plan against an immutable input snapshot. A plan contains cell dispositions, room/layout instances,
port assignments, reserved routes, typed spawn/edit intentions, and a validation report. It does
not contain already-spawned speculative entities.

```text
Normalize input -> inspect templates/context -> construct candidates
    -> choose atomic layouts -> plan residual structure and routes
    -> validate required geometry/connectivity -> place windows/furnishings
    -> validate final plan -> materialize privately -> engine validation -> publish
```

Routing and closure feasibility feed back into selection: this is not a greedy place-everything
pass followed by an attempt to repair an arbitrarily blocked map.

1. Normalize masks, markers, references, profiles, and budgets. Snapshot preserved geometry and
   port/door states. Hash the relevant content for replay and cache invalidation.
2. Inspect room maps into immutable template records. Resolve actual footprints and preserve map
   data. Reject illegal rotations and rooms whose mandatory paths are already blocked.
3. Apply constant-region reservations, then discover transformed room/layout placements from the
   library against the actual shape using section 7.1. Filter by bounds, explicit anchor overrides,
   required port reachability potential, seams, and exclusions. Reserve closure/approach space early.
4. Select whole entrance configurations and search jointly over room choice, placement, rotation,
   per-group routing/separation, and residual fill. Backtracking a configuration restores all its
   dependent claims. Prioritize viable
   exterior matches and constrained gaps; authored required choice regions also participate.
   Rank candidates by section 7.2, using seeded weighted order only within equal fit-score tiers.
   Zero-weight candidates are disabled; an unsatisfiable required choice region fails.
5. Tentatively claim an entire alternative. Propagate occupancy, count, port, and exclusion
   constraints. Use optimistic reachability through carveable cells to prune impossible branches.
   Optimistic reachability is a pruning aid, never proof that the final layout works.
6. For each complete candidate assignment, derive residual masks, choose room partitions, reserve
   paths, and construct a valid enclosing structure. A failure returns a conflict to the solver.
7. Resolve room themes and structural pack compatibility during step 6, including required assembly
   and lighting feasibility. Validate and retain candidate plans satisfying these hard obligations.
   Select the best structural plan under the search budget, preferring fewer required relaxations,
   then the exterior/structural score from section 7.2, then a stable tie-break.
   Freeze that selection before optional furnishing; aesthetic random draws do not rank layouts.
8. Complete the selected plan with optional loops, windows, lighting, cover, and coherent entity
   clusters under separate stage budgets. Required window counts and other hard content obligations
   were reserved/proven feasible in step 7. Revalidate collision, sightlines, and gas edges after
   relevant changes; revert optional additions locally when they break an invariant. Validate the
   full plan again. Optional decoration can optimize its own soft goals, never reroll structural
   alternatives; newly discovered hard infeasibility can trigger a recorded bounded backtrack.
9. Materialize and inspect actual engine behavior as described in section 13. Report the chosen
   layout IDs, fallbacks, and all constraint outcomes even when generation succeeds.

Each branch uses an undo journal or persistent plan data. Track the dependency from generated
routes, wall seams, and fill to the selected package, so rollback cannot leave A's corridor inside
B's layout. Cache keys include transforms, template revisions, and relevant policy/geometry state.

### 7.1 Automatic candidate discovery and room fitting

Treat placement as bounded packing of complete, irregular cell masks with connectivity constraints.
The solver discovers room boundaries; it does not first impose a fixed lattice and ask for a room
of each lattice size. In `Procedural`, section 9 generates shape-fitting room partitions directly
from room themes. In `Prefabs`/`Hybrid`, the following baseline selects library content automatically:

1. Subtract constant ownership and reserve required roots, approaches, and hull support. Extract
   target boundary edges, cardinal runs, convex/concave corners, and connected available areas.
   Keep outer-map exterior, explicit void boundaries, and internal constant-region boundaries
   separately labeled. All boundary processing uses the exact cell mask.
2. Index inspected room/layout masks by area, bounds, allowed rotations, ports, and compatible
   exterior spans/corners. These indexes prune candidates; rectangular bounds never establish fit.
3. Propose placements by aligning compatible room facade edges/corners to target edges/corners.
   Derive the integer transform from paired cells/directions. Alignment is computational: target
   anchor markers are unnecessary. Deduplicate by prototype/family ID, transform, and content version.
4. Also enumerate interior placements at integer origins in compatible available bounds, including
   boundary placements missed by the fast proposals. Check every actual footprint/reservation cell,
   approach, and closure support. Enumeration is lazy and charged to budgets; incomplete enumeration
   must be reported. Do not sample only module-sized offsets and thereby miss a valid one-tile shift.
5. Select an unresolved boundary/gap cell with the fewest feasible candidates, prioritizing required
   obligations and exterior runs, with stable coordinate tie-breaks. Branch over candidates covering
   that cell and, where the mode/policy allows it, assignment of the cell to procedural/passage fill.
   A residual assignment prevents subsequent candidates from claiming that cell within the branch;
   backtracking may revise it. If no prefab fits, the cell is still an obligation to fill/classify.
6. Accept a room/layout only as a complete candidate, including the reserved gap of an L-pattern.
   Propagate conflicts and optimistic route/hull feasibility after each placement. Recompute local
   opportunities as available space changes. Do not freeze exterior placements before checking
   that the interior and all required ports remain solvable.
7. Validate residuals with the actual connector/procedural planner. Penalize avoidable slivers and
   pockets, but allow tiny spaces when that is what the requested mask provides. Backtrack complete
   candidates on hard failures; keep the best valid plan within the shared operation budget.

No scaling, cropping, wall removal, or stretching of a prefab is allowed unless the template exposes
a specific supported repair/seam operation. If a curve/stair-step/concavity has no suitable prefab,
hybrid generation fits a procedural boundary strip or whole residual room to it. The requested
outline remains authoritative. `Prefabs` mode reports uncovered incompatible areas rather than
silently changing their shape or introducing procedural room interiors.

Automatic candidate instance IDs derive from the source ID and transform, with stable child paths
for family members. Search order must not determine region IDs or random streams. A required room
type/count can constrain the library selection without specifying where those rooms go. A room's
own internal authored furniture is preserved when placed; that does not pin its map position.

### 7.2 Exterior matching preference

Prefer exterior rooms whose facade follows the requested outline, including bends and concave
features, over rooms that leave avoidable strips of filler or protrude toward forbidden space.
Containment, constants, hull integrity, and accessible entrances always take precedence. Exterior
matching is a soft objective by default and cannot override any of those constraints.

Construct a fixed reference boundary before evaluating candidates. In `Footprint`, compare the
room/layout's compatible exterior-facing occupied boundary against the target's occupied boundary.
In `InteriorFill`, compare its usable interior boundary against the target interior boundary and
separately verify that its hull fits the existing structure or authorized envelope. Do not enlarge
or redefine the reference boundary to improve a candidate's score. Preserved exterior segments
remain visible in the report but are excluded from the variable score.

Compare directed cardinal edges `(cell, outwardNormal)`, not just bounding boxes or distance between
centers. An exact match shares the reference location and normal under the chosen geometry mode.
Concave corners require the corresponding pair of directed spans. For near matches, measure
Manhattan distance to a reference edge with the same normal, capped at `exteriorMatchBand` (default
3 tiles); edges beyond that band receive the maximum mismatch penalty. Count only spans explicitly
eligible to face that exterior. Ordinary doors aimed into space never count as matched facade.

Outer perimeter gets the exterior preference by default. Request field `matchVoidBoundaries`
(default true) extends it to explicit space holes/courtyards; constant-room interior interfaces
instead use port/seam compatibility. A hole is never mistaken for spare fill space.

After hard feasibility and the number/severity of permitted relaxations, compare complete plans
lexicographically using this initial structural objective:

1. Maximize the number of distinct eligible target exterior edges matched by selected prefab
   facades (or by generated room shell boundaries in pure procedural mode).
2. Minimize capped facade mismatch distance for boundary placements.
3. Minimize avoidable residual fragments too narrow to meet the requested corridor/room goals.
   Determine width by the clearance model; do not count intended passages as defects.
4. Minimize deviation from requested prefab coverage, room size mix/counts, and preferred connections.
   Default hybrid prefab coverage target is 70% of nonconstant usable room area, soft; caller/theme
   can override it. Required type/count minima always remain hard. Prefab mode's required coverage
   is dictated by its ban on unassigned/procedural interiors, not by this percentage.
5. Apply seeded prototype weights and a stable final tie-break among equally scored plans.

Deduplicate matched target edges so many small rooms cannot gain credit repeatedly for the same
segment. Report score terms separately, along with unmatched exterior spans and their reasons.
The score ranks feasible plans; it does not promise globally optimal packing under finite budgets.
For pure procgen, boundary adherence will commonly tie because exact closure is already required;
the remaining structural goals then guide subdivision. No prefab library is needed in that mode.

### 7.3 Constant regions: explicit author overrides

A `constantRegion` declares a stable ID, exact mask and transform, and either an authored map/layout
resource or an immutable snapshot of already authored host content. It is instantiated into staging
at that exact transform and contributes its full occupied/support footprints to `P`. Existing host
snapshots retain their actual positions. Do not randomize, rotate, refurnish, retile, or replace any
content in the constant mask across seeds. Resolve any random spawners/markers within constant
content to pinned outcomes or reject them as nonconstant; ordinary post-publication gameplay is
outside this generation-time guarantee.

Constants may cover a landmark, hull section, entrance hall, room cluster, or other subarea. They
need not be rectangular. An author may explicitly declare `Generate` holes inside a surrounding
constant area; those holes are outside its immutable mask. Reserve the entire actual footprint of
constant entities so neighboring placements cannot overlap a fixture whose anchor is inside it.

Ports and clean traversal through constant content are inspected exactly as for other authored
rooms. External connection/interface markers are useful here, but automatic candidate fitting does
not require target-wide markers. If generators may fill a doorway seam, define that seam outside
the immutable mask and identify its allowed operations explicitly. Constants never implicitly grant
permission to remove a wall or door. Conflicting constants, blocked mandatory constant entrances,
or constants incompatible with the target/closure requirements produce a diagnostic and failure;
fallbacks may change surrounding generation but cannot alter the constants.

The default workflow has zero constants. A request with only constants and no remaining target
still validates their requested accessibility/closure and reports zero generated area. Constant
content is excluded from variable room-coverage/size targets unless the goal explicitly includes
it. Its fingerprint must remain identical across seeds and permitted fallback paths.

Conceptual minimal requests, with each symbolic mask/library/theme supplied by a resource or caller:

```yaml
mode: Hybrid
shape: KsIrregularStationMask
roomLibrary: KsStationRoomLibrary
theme: KsStationInterior
seed: 12345
```

Optionally add a constant landmark without specifying any generated room positions:

```yaml
constantRegions:
- id: ArrivalHall
  source: KsAuthoredArrivalHall
  mask: KsArrivalHallMask
  origin: 12, 8
  rotation: 0
```

`origin` and `rotation` express the constant's exact transform; the mask is local to that transform.
The resulting world-local footprint must fit the declared target/context ownership. Referenced
constant assets include validated ports/structure. All remaining room transforms are solver outputs.

## 8. Traversal, entrance destinations, and residual connections

### 8.1 Two traversal graphs

Build a cell graph from the final collision plan: nodes are cells usable by the reference actor;
edges are valid cardinal steps with sufficient physical clearance. A tile labeled "floor" is not
automatically traversable: fixtures, furniture, door frames, and neighboring overhangs count.
Door transitions require that the actor can actually open/pass the door in the requested operational
state. Merely ignoring all closed doors is not an acceptable access test.

Required walkways and furniture/seat approaches use a **clean-passage graph**: ordinary cardinal
walking and permitted door operation only, with no vaulting, climbing, crawling, or moving obstacles.
Anything that normally blocks movement and requires vaulting, including tables/desks, explicitly
blocks this graph. An actor profile's ability to vault cannot relax that rule. Reachable machine
approaches and chair access positions are
mandatory terminals for every accepted functional furnishing assembly, just like room entrances.

Maintain a separate room/port graph for author intent and diagnostics. Validate both graphs:

* **Room-local invariant:** within each logical room, all operational inside approaches and
  thresholds connect to each other through that room's usable cells. Do not satisfy this by exiting
  the room and walking around through a hallway. A room with one port must have a reachable interior
  landing; a zero-port decorative room cannot be counted as a required accessible room.
  A marker blob with multiple exact groups contains separate logical rooms/networks; never pass
  all of its entrance markers to an unconditional all-room-ports connection check.
* **Entrance-configuration invariant:** section 5.4's selected configuration must match the actual
  port connectivity matrix, including forbidden cross-group reachability in its declared scope.
* **Global invariant:** every required room, operational port, and mandatory generated accessible
  area is reachable from the root of its permitted network. Default policy is `SingleNetwork`.
* **Port destination invariant:** each operational threshold connects its inside approach to a
  valid outside approach and destination: another port, generated passage/room, declared external
  host path, or an allowed stub. Doors into a wall, unapproved vacuum, or an unreachable pocket fail.
* **Width invariant:** every required route maintains `minimumClearWidth >= 1`, including bends,
  thresholds, and approaches. Desired widths greater than one may degrade only when allowed.

In a fully generated accessible region, all walkable floor cells should join its network. An
intentional inaccessible service cavity must be explicitly classified and cannot contain an
operational port, mandatory item, spawn, or required room. Do not conceal routing bugs by relabeling
disconnected floor as decorative during validation.

The reference actor profile defines collision dimensions and door capabilities. Version 1 requires
one nominated gameplay profile per request; additional profiles can be checked independently. A
geometrically connected locked or unpowered door is reported as inaccessible unless the profile
provides a real permitted way through it. Abstract plans may pass preliminary checks but cannot be
published until the materialized operational state passes.

### 8.2 Route construction

Use cardinal path search over writable procedural/seam cells, existing valid passages, and approved
room routes. Protected walls, void, out-of-bounds cells, and unauthorized prefab interiors are
impassable. Costs are positive integers: favor reusing reserved passage, penalize new excavation,
and use deterministic tie-breaking. Never use a diagonal shortcut to join two corners.

For a generated room with several ports, reserve a connected route tree through all inside
approaches first. A simple initial implementation incrementally routes each terminal to the
existing tree with Dijkstra or A*. A* must use an admissible lower bound, such as Manhattan distance
times the minimum step cost; Dijkstra avoids that requirement. Reserve paths before placing walls
or furniture. For a multi-group blob, build a tree per required group and reserve real separation
before allowing the groups to grow. Optional additional paths create loops only within allowed
connections after required connectivity succeeds.

For global routing, begin with components already connected through rooms and existing passages.
Search for feasible paths joining different components, commit the least-cost viable candidate,
recompute components, and repeat until each permitted required network is connected. Filter all
candidates against exact-group isolation, including external bypasses when that scope is selected.
Include required explicit
port-to-port links even if another route already joins their rooms. A spanning tree over Euclidean
port distances alone is insufficient because those edges may cross forbidden cells.

Widen paths only into authorized free cells. A requested width of two or more requires validation
of actual width at turns using a profile-defined clearance stencil, not just dilation of a
centerline that can cross protected walls. Failure to widen keeps a one-tile route only when the
policy permits that degradation; the one-tile lower bound is never relaxed for an operational route.

### 8.3 Filling gaps near rooms

Compute residual cells after chosen layouts and structural reservations:

```text
residual = target cells assigned Generate
           minus preserved/prefab/structural cells already assigned
```

Process connected components, while routing across components remains possible through authorized
rooms, seams, and other passages. Collect every selected room port whose outside approach borders
or can reach these cells. Mandatory unconnected ports remain obligations even when they exceed a
proximity search radius.

For each obligation, attempt a network connection through the residual mask. Rank nearby compatible
entrances by feasible path cost, including obstacles, rather than straight-line distance. The
optional `nearbyPortRadius` (default 12 tiles, Manhattan broad-phase distance) limits searches for
additional convenient links only. Budget permitting, add those links to improve flow and cycles.
Record which nearby optional links were attempted and why any were rejected.
An entrance's connection-group assignment is a hard eligibility filter for these attempts. Proximity,
dead-end fallback, and residual reuse cannot connect groups forbidden by the chosen configuration.

If a direct connection between two rooms cannot fit, a residual area may branch from one room and
terminate at a dead end. `NetworkPreferredAllowStub` permits this only when:

1. The entrance's owning room is already reachable from the network through some valid route.
2. The stub has at least one clear cell beyond the threshold, enough approach clearance for the
   actor, and a real enclosed end where spaceproofing applies.
3. The stub stays inside authorized geometry and does not pretend to satisfy a `FixedPort` link or
   `NetworkRequired` destination contract.

A one-tile landing is the smallest permitted stub. Returning to the room is a valid way out. A door
opening directly onto a blocking wall is not a stub. Record `PortUsesStub` as a soft-target miss when
network connection was preferred. A room whose only port faces an isolated stub fails global
accessibility unless an explicitly supplied root really enters that component.

Required ports cannot be sealed to make validation pass. `OptionalSealable` ports may be replaced by
an approved airtight wall only under explicit policy, then removed from the operational port list
with a diagnostic. Validate accessibility of the room again; sealing its only entrance cannot leave
a required room inaccessible.

## 9. Pure procedural fill at tile resolution

### 9.1 Minimum complete algorithm

The baseline MUST work without prefabs, a fixed cell-block size, or a minimum room area:

1. Classify the exact target mask, reserve required closure and approach cells, and determine the
   remaining usable mask. In `InteriorFill`, do not erode the requested interior without permission.
2. Build and reserve routes for each permitted network/group in that usable mask, including
   separation structure required by exact entrance configurations. Select a deterministic usable
   root per authorized network if none is supplied; report absent external access. A narrow target
   may consist entirely of passage when its connection configuration permits this.
3. Propose room partitions using connected seeded region growth. Select seed cells in stable seeded
   order; expand each region through cardinal unassigned neighbors until its target area or boundary
     is reached. Absorb remaining fragments into compatible neighbors or treat them as small spaces.
   Every cell is handled; fragments are not discarded because they miss a preferred room size.
4. Propose partition walls on available cells between regions, with door openings where required.
   Tile-thick partitions must fit around reserved routes. Reject a split when its walls would destroy
   an interior landing, mandatory route, or requested minimum area. Merge regions only when their
   connection groups permit it. Failure to separate exact groups rejects this candidate/configuration.
5. Derive ports for the accepted partition doors and validate local and global connectivity. The
   initial route network remains protected throughout this stage.
6. Add theme content only in available cells using section 9.3, then perform the final checks.

Large rectangular areas may later use BSP or another partition proposal algorithm; every proposal
still operates within the exact mask and passes the same constraints. No partition algorithm can
weaken the one-cell resolution guarantee. Partitions express desired variety; a single unpartitioned
open space is a valid fallback when room-count targets are soft and the selected entrance
configuration permits those connections. Geometric narrowness cannot relax exact separation.

### 9.2 Tiny and degenerate shapes

| Request | Required behavior |
| --- | --- |
| `InteriorFill`, 1x3, already sealed host, valid end approaches | Produce three connected cells; omit furniture and extra partitions. Verify the host closure and actual approach paths. |
| Isolated 1x3 `Footprint`, spaceproofing disabled | Produce the requested three-cell floor/path if other hard constraints allow it; explicitly report unsealed output. |
| Isolated 1x3 `Footprint`, required usable space and required spaceproofing, no envelope | Return `NoFeasiblePlan` or an explicitly permitted constraint relaxation. Never report a usable sealed room. |
| Same footprint, `AllowSolidFill`, no hard usable-area/port requirement | May produce a sealed structural strip with `Degraded` status and zero usable room area. It does not count as a room. |
| 1x3 interior with a writable surrounding envelope | May construct a surrounding hull if the theme's real wall/door footprints fit and connect to valid context. |
| One cell | Apply the same rules, with no artificial minimum dimension. |
| Two cells touching only diagonally | Two components; apply `SingleNetwork`/`PerIsland` policy. |
| Thin ring around a hole | Preserve the hole, reserve its required hull, and validate remaining cardinal routes; fail/degrade if no usable route survives. |

The generator must be able to represent and return a diagnostic for every finite accepted mask. It
does not promise a functional room for geometrically contradictory requests. Falling back to sparse
floor never silently bypasses a required hull, route, root, or port.

### 9.3 Theme-driven interior generation

Assign a room theme as soon as a procedural partition is proposed so its usable size, required
assemblies, palette, and lighting support can influence feasibility. Shared corridors inherit the
parent passage theme unless explicitly assigned another. Entire masks can use one theme, or each
room can choose from a weighted compatible pool. Themes are not tied to rectangular room sizes.

Choose floor/wall families per room and reconcile shared seams before final structural validation.
Reserve placement/interaction space for required assembly cores and fixtures with hard lighting
requirements before optional decoration. Plan general lighting to cover the usable room/routes,
then add coherent activity clusters and supported task lighting. Re-evaluate coverage after tall
furniture or walls change occlusion. A powered fixture placed on a disconnected wire is not working
lighting; respect the explicit supply profile.

Default placement order is structure/palette, required routes, required assembly cores and their
clear approaches, general lighting, optional cluster members, supporting clusters, loose items,
and decoration. Structure and routes remain protected at every stage; if a required assembly cannot
fit, revise the procedural partition or reject that theme candidate instead of shrinking a walkway.
Ordinary loose-item placement must not make a passage unusable under the declared traversal model.

Bounded room-content fallback order: try another position/orientation; try a smaller declared
assembly; reduce optional cluster count; remove optional members/supporting packs; use an explicitly
allowed compatible fallback theme; leave a sparsely furnished interior. Retain chosen primary
materials and as much working lighting as fits. A required assembly or lighting minimum remains a
hard condition unless its exact relaxation is authorized. Sparse output is useful for tiny inputs
but is reported as missing the richer theme goals.

Theme selection and per-room pack placement use their own deterministic streams. Merely adding a
decorative item to a pack should not reroll structural room alternatives. A substantive new required
assembly may legitimately make a previously selected layout infeasible; that constraint-driven
change is recorded rather than hidden as random variation.

## 10. Spaceproofing and structural closure

### 10.1 Contract

`spaceproof` defaults to `Required` for station-interior themes and can explicitly be `Disabled` for
outdoor/ruin themes. Spaceproof means no gas-transfer path from a protected interior to a space/vacuum
sink in the nominated resting door state. It does not mean indestructible, permanently pressurized,
or immune to a player opening an exterior door.

Each protected room/network declares whether its shell is independently sealed when its own doors
are closed or may rely on the enclosing host hull. Default generated station rooms use the latter;
an independently sealed room requires a separate room-scoped leak check. For host-dependent closure,
all gas-reachable host context must be known or supplied by a validated enclosing-hull contract.
Reaching an unknown context edge is `UnverifiedBoundary`, never assumed airtight.

### 10.2 Construction and checking

1. Determine protected gas-bearing floor cells, explicit void/space sinks, and real gas adjacency.
   Movement blocking and gas blocking are separate properties: furniture and ordinary floor do not
   automatically seal gas. Interior holes marked space count as vacuum sinks even if disconnected
   from the outer map edge in a simple geometric flood fill.
2. Propose hull walls/windows/doors in writable structural cells or rely on verified preserved
   boundaries. Include tile/floor support and every exposed side, including concave corners and
   holes. Evaluate actual directional airtight behavior through an engine adapter.
3. Flood the gas graph under the nominated resting door states. Any protected cell reaching a space
   sink fails. An unknown boundary fails verification unless the request explicitly accepts a
   reported unverified result as a degradation; it must never be called spaceproof.
4. Repeat with materialized engine entities after tile/airtight updates have completed. A static
   metadata flood is preliminary evidence only. Include an atmosphere integration fixture that
   detects a removed hull segment or non-airtight window.

Default resting states close ordinary doors and airlocks. A direct opening/door to vacuum is not an
ordinary room entrance destination. An exterior access request needs a declared airlock/docking
contract with a sealed landing on the appropriate side. Guaranteeing containment during an opening
cycle requires a validated interlock/operating-state contract; closing all doors for a static check
does not establish it. Always report which door states were tested.

Route and gas validation deliberately use different states: an openable closed interior door can
be a valid movement transition and a closed gas barrier. An unusable locked exterior door cannot
be treated as both a convenient route and a permanently sealed boundary without a valid profile.

Never repair a leak by overwriting preserved content, extending outside `W`, or blocking an
operational port. Try a permitted structural alternative, backtrack the layout, or fail/degrade.

## 11. Exterior windows

Use `exteriorWindowFraction` in `[0, 1]`, default `0.25` for the initial station theme. The fraction is
measured over **eligible exterior boundary cells**, not the room's area or all walls. Eligibility
requires an interior-facing side, an exterior-facing side, and a supported airtight window footprint.
Exclude doorway spans/approaches, unsupported corners, internal room walls, and required structural
elements. Exterior includes declared space courtyards/holes; authors may disable windows on chosen
boundary segments. One cell counts once even if it touches multiple exterior edges.

Compute eligibility from structure before window selection or decoration, independent of the desired
fraction. Preserved eligible walls/windows remain in the denominator so the generator cannot hide
uneditable content. Let `N` be the eligible cell count, `F` the number of fixed eligible window cells,
and `U` the set of editable eligible cells:

```text
desiredWindowCells = floor(exteriorWindowFraction * N + 0.5)
actualWindowCells = F + chosenWindowCellsInU
```

Choose editable cells in seeded stable order, optionally grouped into short runs. Existing fixed
windows cannot be removed without permission. Clamp a soft target to the feasible count; record
requested/achieved counts, fraction, denominator, and exclusions. With `N = 0`, report `NotApplicable`
and a zero count, not a divide-by-zero value or a claim that the percentage was achieved. A hard
positive window-count requirement is infeasible in that case.

Default `windowToleranceCells` is 1. A hard fraction constraint requires the actual count to be
within that tolerance of the rounded target; an explicit minimum/maximum count is checked separately.
Scope may be the whole request or named rooms/boundary segments; overlapping hard scopes must all
hold. When a window module spans multiple cells, count its eligible occupied cells and report
granularity shortfalls. Preview also reports windows versus total exterior wall cells to make the
eligible-cell denominator visible to authors.

Window replacement must retain pressure closure, avoid route obstruction, and update sightlines.
Decorative glass lacking the necessary airtight behavior is rejected by the theme validator.

## 12. Cover and defensibility

These are optional soft objectives in version 1. Do not claim that a single numeric score guarantees
balanced combat. First ship useful measurements; then use them to guide bounded furnishing choices.

Keep separate representations for movement, vision occlusion, and projectile obstruction. An
opaque curtain, transparent solid window, and chest-high object can differ across all three. A
theme/profile supplies supported classifications; unknown properties are reported and excluded
from optimization instead of guessed from sprites.

Initial measurements:

| Metric | Defined computation |
| --- | --- |
| Exposure | For sampled usable cells, count visible sampled cells within a configured radius through the vision mask. Report visible fraction and longest sampled clear ray. |
| Cover availability | For a nominated threat origin/direction, test projectile rays to a sample point and nearby legal positions. A position is protected when the selected projectile model is blocked before reaching it. Report directional protection, with actual stance/height only when the engine adapter supports it. |
| Choke candidates | Find articulation cells and bridge edges in the cardinal walk graph: temporarily removing one disconnects it. Report the sizes and required terminals of separated sides. Also report narrow clearance runs; multi-tile chokes need later cut analysis. |
| Alternate routes | Count whether important room/port pairs still connect when a nominated choke is unavailable; use bounded checks on nominated pairs. |
| Route redundancy | For nominated terminal pairs, count internally cell-disjoint clean cardinal routes up to a configured cap. A count below the cap is exact for the supplied graph; reaching the cap is a lower bound. This finds two-cell bottlenecks that single-cell articulation cannot describe. |
| Firing positions | Identify reachable protected positions with a clear sampled shot toward a designated doorway/choke. Report the assumed threat side and projectile model. |

Use a specified tile-ray convention: a ray touches every cell its segment crosses; corner ties
inspect both incident cells to avoid seeing through a pair of diagonally touching opaque blockers.
For engine validation, use the chosen vision/projectile APIs and record any mismatch with the
planning approximation. A visibility ray does not create a movement edge.

Initial optimization: propose one optional cover object at a time outside protected routes and
approaches; recompute affected samples and all relevant traversal/pressure checks; accept only if
hard constraints remain true and the weighted soft objective improves. Cap proposals and samples.
Allow targets such as maximum exposed corridor length, minimum covered approach positions, and
preferred number of alternate routes. Do not maximize choke count indiscriminately: a layout may
explicitly prefer openness or two ways out of a room.

Diagnostics identify whether a choke is unavoidable because of the input mask, authored into a
prefab, or introduced by generated furnishing. Optional tactical analysis may be skipped on tiny
areas or budget exhaustion with `TacticalAnalysisSkipped`; this never skips route validation.

## 13. Fallbacks, results, and publication

### 13.1 Constraint precedence

Classify every condition as an immutable invariant, a request-hard requirement, or a soft target.

* Immutable: write bounds, preserved ownership, package exclusivity, valid prototypes/transforms,
  finite execution, no diagonal movement edges, and minimum one-tile width for any claimed route.
* Request-hard: required ports/links/roots, connectivity policy, selected entrance-group reachability
  and isolation, required spaceproofing, minimum usable area/counts, access profile, and any explicitly
  hard window/size/theme constraint, including mandatory functional assemblies and lighting coverage.
* Soft: preferred sizes/counts, extra loops, nearby optional links, desired corridor width above the
  hard minimum, window fraction by default, furnishings, and tactical goals.

A soft failure lowers plan quality and appears in the report. A hard failure rejects a branch.
Changing a request-hard requirement is allowed only by a named relaxation already enabled in
`fallbackPolicy`. The result remains `Degraded` and records original and effective requirements;
it must not claim to satisfy the original contract. Immutable invariants cannot be relaxed.

### 13.2 Ordered fallback policy

Within each bounded search, first try ordinary alternatives satisfying the original constraints.
The default progression, skipping steps without permission, is:

1. Remove/reposition optional generated obstacles; reroute required paths or choose other allowed
   seams/partitions. Preserve authored content unless its repair metadata permits a specific edit.
2. Reduce optional furnishings, cover objectives, room-count/size targets, extra loops, and desired
   width down to the hard minimum. Miss the soft window target if necessary.
3. Use valid stubs for ports whose destination policy permits them. Seal only explicitly sealable
   optional ports under their authoring/policy contract.
4. Backtrack the complete offending layout and try other alternatives. All branch-local fallback
   effects disappear with that layout. Try different origins/rotations, room sizes, or family
   alternatives as needed; do not keep a bad exterior placement permanently pinned. Constant
   regions remain immutable, and required room-type/count constraints remain required.
5. Replace an automatically proposed optional placement with procedural fill when the mode/policy
   allows it. An explicitly authored choice region additionally needs a declared procedural fallback.
   Required prefabs cannot disappear silently; exterior fit and prefab-coverage target misses are
   reported when no suitable room matches a boundary.
6. Merge procedural subdivisions into one sparse connected interior, keeping required routes and
   closure, only within permitted connection groups. Hard room-count/minimum-area requirements
   and exact-group separation still apply; try another complete configuration or fail when needed.
7. Apply individually enabled terminal relaxations: for example `AllowSolidFill`,
   `AllowUnsealedOutput`, or `AllowSeparateIslands`. They are disabled by default. A relaxation
   identifies the precise hard condition it may replace and can never invalidate an unrelated
   hard port/access requirement.
8. Return failure with the smallest useful discovered conflict and an uncommitted preview.

This is a bounded preference order, not an instruction to restart unlimited searches at every step.
One request-wide work budget spans retries and fallback passes. Reserve a defined part of that
budget for the sparse fallback so aesthetic exploration cannot consume every chance to return it.
Failure after bounded search means "no plan found within this search," not proof that no solution
exists. Distinguish a directly proven contradiction from budget exhaustion.

### 13.3 Result contract

| Status | Meaning |
| --- | --- |
| `Success` | Published or previewed valid plan; all hard constraints and soft targets within their tolerances. |
| `Degraded` | Valid under the reported effective policy, with soft misses or explicitly allowed relaxations. |
| `NoOp` | Empty request with no required output. |
| `InvalidInput` | Malformed schema, contradictory declarations, unsupported data, or invalid template. |
| `NoFeasiblePlan` | Search found no acceptable plan; report whether contradiction was proven or search incomplete. |
| `BudgetExceeded` | Deterministic work/resource budget ended before any acceptable plan; never publish partial geometry. |
| `Cancelled` | Caller/world lifecycle cancellation; no output published. |
| `MaterializationFailed` | Planned output could not be realized or failed engine validation; staging is discarded. |

If a valid plan exists when search budget expires, return it as `Success`/`Degraded` according to
its constraints and set `searchComplete = false`. Publication is a separate boolean; a preview is
never described as a spawned map. Include:

* Seed, generator version, normalized request hash, content/template hashes, and plan hash.
* Selected alternatives/members/transforms; exclusions that eliminated candidates.
* Automatic placement provenance, matched/unmatched exterior edges, component fit scores, residual
  fragmentation/coverage, and constant-region fingerprints. State whether enumeration/search was
  incomplete; an empty candidate sample is not proof that the library cannot fit.
* Per-cell ownership, remaining void, usable area, requested/achieved size distribution.
* Per-port destination, routes, stubs/seals, actor profile, and network roots/components.
* Marker source/channel/profile, normalized blob masks and IDs, bound entrance IDs, selected
  connection configuration, requested/actual port-component matrices, and isolation scope.
* Constraint results (`Satisfied`, `Missed`, `Relaxed`, `NotApplicable`, `Unverified`) with original
  values, achieved values, and cell/room/port IDs.
* Gas-boundary and operational-door-state checks, window denominator/counts, tactical metrics.
* Resolved room themes/palettes, entity-pack and assembly counts, mandatory/optional omissions,
  cluster membership, interaction approaches, lighting fixture supply/state, and coverage estimates
  versus validated coverage.
* Resolved relational prototype/variant/binding IDs, support/container parents and slots, relation
  witnesses, member counts, required failures, and omitted optional members with reasons.
* Search counters, fallback trace, timings, publication state, and actionable conflict diagnostics.

Example reason codes: `MaskTooSmallForHull`, `PortApproachBlocked`, `RequiredLinkUnroutable`,
`RoomPortsDisconnected`, `AccessProfileRejectedDoor`, `AlternativeConflict`, `WindowTargetMissed`,
`UnverifiedBoundary`, `TemplateCopyUnsupported`, and `WorldChangedDuringGeneration`.
New diagnostics include `AreaMarkerConflict`, `EntranceBlobAmbiguous`, `ConnectionPortMissing`,
`ConnectionPolicyConflict`, `EntranceGroupsJoined`, `NoConnectionConfigurationFits`,
`AssemblyRelationUnmet`, `SurfacePlacementUnsupported`, and `AssemblySupportCycle`.

### 13.4 Materialization and isolation

Version 1 materializes into an isolated, paused/nonpublic map or grid controlled by the generator,
including copied preserved context needed for validation. Use supported engine serialization to
retain authored overrides/references; reject unsupported template content explicitly. A prefab
instance must not share entity references with its atlas or another instance.

Apply tiles, structural entities, ports/doors, authored room contents, optional generated content,
and decals in a deterministic order compatible with engine initialization. Resolve references,
anchoring, containers, and map initialization before validating actual collision and airtightness.
Resolve relational support/container dependencies parent before child, then validate the supported
pose, interaction reach, chair facing and approach against the actual initialized entities. A
shared tile cannot be treated as a collision error or as a valid surface mount without that adapter
check. Re-evaluate the selected entrance connectivity matrix after actual doors and entities exist.
Some components can perform global side effects on startup even on a hidden map: the integration
layer must defer such activation or reject those components from speculative materialization.
Document this support boundary and prove it with a fixture before promising arbitrary prefab copying.

Do not rely on deleting a half-built public grid as transactional rollback. On failure, discard the
private staging output and restore temporary engine flags/resources. On success, publish only
after all required checks and activation preconditions pass. Fire one completion event containing
the accepted result. Cancellation/deletion of the destination and map/template reload invalidate
the snapshot; revalidate or cancel instead of committing stale data.

Patching an existing live grid requires a future explicit integration contract for world-change
checks, player isolation, rollback of entity side effects, and gas/physics initialization. Until that
exists, reject live patch requests rather than offering unsupported atomicity. Authored host layouts
and explicit constant regions assembled in staging are fully supported by version 1.

## 14. Determinism and performance

For identical normalized input, relevant context, content versions, seed, generator version, and
work budgets, produce the same plan and diagnostics ordering. Entity UIDs and wall-clock timings
are not part of the semantic plan hash.

Use a pinned random algorithm and stable seed derivation with test vectors. Derive independent
streams from the root seed, stable region/room ID, and stage name (selection, routing, windows,
theme palettes, lighting, furnishing). Do not use runtime `GetHashCode`, dictionary iteration order, global game RNG, entity
UIDs, thread timing, or default random seeding. Extra decoration draws must not change room choices.
Quantize solver costs/scores where needed so tie-breaking is specified and reproducible.

Hash normalized marker blobs/profiles, scoped entrance IDs, all eligible connection configurations
and the chosen one, isolation policy, assembly bindings/variants, resolved relations, and layered
reservations. Sort set-valued port groups and footprints canonically; retain meaningful variant
priority and weights. Marker scan order and shorthand whitespace do not affect results. Increment
the generator contract version when replacing the old all-connected or whole-pack semantics.

Initial configurable defaults, to be profiled before production tuning:

| Budget | Initial default | Exhaustion behavior |
| --- | --- | --- |
| Total target + envelope + inspected context cells | 65,536 distinct cells | Reject oversized request before allocation/search. |
| Candidate placements | 20,000 | Stop enumeration with a budget diagnostic; do not silently omit alternatives and claim exhaustive search. |
| Candidate transform probes | 2,000,000 | Counts rejected origins/rotations and failed mask tests as well as accepted placements; stop with a diagnostic. |
| Search node expansions | 50,000 across all branches/fallbacks | Return best feasible plan or `BudgetExceeded`. |
| Path node expansions | 2,000,000 total | Same; never drop a required port to meet the cap. |
| Repair proposals | 256 total | Escalate to next permitted fallback within remaining budgets. |
| Furniture/cover proposals | 4,096 total | Stop optional additions, validate current plan. |
| Visibility sample origins / radius | 512 / 16 tiles | Deterministically subsample and report sample coverage. |
| Nested choice depth | 16 | Reject deeper/cyclic input; reject cycles regardless of depth. |
| Area/entrance markers | 65,536 / 1,024 per request | Reject before unbounded graph or component allocation; area cells also share the total cell cap. |
| Entrance configurations | 64 per blob | Reject oversized authored sets; solving them shares the request-wide search/path budgets. |
| Expanded members / relations in one assembly | 64 / 256 | Reject oversized definitions before search; placement retries share the 4,096 assembly-proposal cap. |

Reserve 10% of search and path expansion budgets for the permitted sparse-fill attempt. Charge
required validation separately using explicit limits sufficient for the accepted cell cap; if it
cannot complete, return a budget failure instead of accepting unvalidated output. Bound template
entity/spawn counts and ray/cut-check work as well; initial limits are 100,000 entities per request,
262,144 sampled rays shared by bounded light/tactical sampling, and 128 alternate-route checks.
Lighting and assembly placement each allow 4,096 proposals; pack expansion shares the candidate
and entity caps. A required light-coverage validation that exceeds its cap fails rather than
silently subsampling its hard predicate. Reject a single template exceeding these caps
before cloning it. Report counts separately from elapsed time.

Use sparse masks or bounded local bitsets so a few distant cells cannot force an enormous bounding
rectangle allocation. Account for exterior flood/context expansion in the cell budget. Use checked
integer arithmetic and a documented maximum absolute tile coordinate; initial limit is 1,000,000.

Yield server work regularly; initial scheduling target is a configurable 2 ms slice. Time slicing
changes scheduling only, not candidate choices or operation budgets. A wall-clock watchdog may
cancel the operation with a diagnostic, but cannot select a different random fallback. Check
cancellation at every yield and before materialization/publication.

## 15. Author workflow and debug tools

Provide an admin/developer preview entry point accepting a request/profile, mask source, and seed.
Proposed commands `ksprocgen preview`, `ksprocgen validate`, and `ksprocgen replay` are requirements
for tooling, not claims about existing commands. Publication must be a deliberate caller action
after a valid preview or an explicitly configured automatic generation call.

An author should be able to:

1. Draw a target/void mask or paint area markers; choose `Footprint` or `InteriorFill`. Preview the
   connected blobs, holes, profiles, and explicit/derived blob IDs before solving.
2. Choose a theme and, for prefab/hybrid output, a room/layout library. The generator discovers
   room choices, sizes, positions, rotations, and exterior matches without manual slots/anchors.
3. Optionally mark constant areas that must remain exactly authored. Advanced users may also add
   choice-region/anchor overrides or mandatory room-type/count constraints. Library assets carry
   their own validated ports, anchors, seams, and atomic family definitions.
4. Select constraints, fallbacks, room theme(s), pack overrides, and seed. A basic pure-fill request
   needs only a mask and theme; a hybrid request adds a library, often supplied by that theme.
   Optionally label entrances and select a connection profile, or enter one group expression per
   acceptable configuration line. Preview required connections and forbidden cross-group routes.
   Choose reusable relational assemblies through packs; inspect device support, seat facing, and
   required versus preferred corner placement without hand-positioning a room layout.
5. Preview automatically selected layouts, exterior fit, constant areas, paths, hull, windows, and
   diagnostics before spawning the map.
6. Reproduce a reported failure using its request/content hashes and seed.

Overlay layers: target/envelope/preserved/void masks; region claims and selected alternative IDs;
cell ownership; ports/normals/approaches; reserved routes and disconnected cells; pressure leaks;
eligible window cells and chosen windows; cover/sightline/choke samples. Distinct colors need labels
or patterns so diagnostics remain understandable without relying on color alone.
Include theme/pack/cluster IDs, assembly relations, reserved interaction approaches, lighting
coverage, fixture power assumptions, and unmet furnishing goals in the content preview.
Show discovered placement candidates, selected facade-to-boundary correspondences, residual fill,
and why a visually close-fitting candidate lost to connectivity or another hard constraint.
Show area components/channel/profile IDs, entrance ownership, the chosen connection configuration,
group labels, physical separating cells, and the first forbidden path when isolation fails. Overlay
assembly support links/slots, member-facing arrows, required approach paths, and optional omissions.

Show the route or obstruction cells for a failed port and the competing claims for a placement
conflict. Log one concise summary plus structured detailed diagnostics; avoid one warning per cell.
Export the normalized request and plan/report through repository/engine-supported resource or admin
tooling APIs. Do not add direct unrestricted filesystem calls to sandboxed content assemblies.

## 16. Acceptance fixtures and regression matrix

Use compact, inspectable authored fixtures with exact masks/ports, plus seeded generated cases.
Every fixture asserts the semantic invariants and checks actual output when engine behavior matters.

| ID | Fixture and expected assertion |
| --- | --- |
| A01 | Concave mask with a hole and preserved obstacle: every target cell assigned once; hole preserved; no write outside `W`. |
| A02 | Same region offers large, medium-pair, L-three, and four-small layouts: each chosen package is complete and exclusive. |
| A03 | Force each L/four alternative separately, then make one member/link impossible: rollback leaves no member, passage, claim, or exclusion from the rejected package. |
| A04 | Parent with three regions and explicit size/count goals produces large + medium + small rooms together; West/East scoped exclusions do not leak across instances. |
| A05 | L layout's fourth area is generated; it connects valid nearby A2/A3 ports and never receives B4. |
| A06 | Residual gap with several room doors, obstacle, and a distant required door: mandatory routes succeed despite the proximity radius; optional misses are reported. |
| A07 | Allowed reachable stub succeeds; wall-adjacent door with no outside landing fails; sole entrance to an isolated room is not rescued by a stub. |
| A08 | Several ports in one room with furniture: all connect inside the room on cardinal routes. An outside detour cannot satisfy room-local connectivity. |
| A09 | Diagonally touching floors, blocked bends, and overhanging fixtures fail false connectivity; a clear one-tile route passes for the declared actor. |
| A10 | Required two-tile width fails on a pinch; desired two-tile width may degrade to one with a report; no route degrades below one. |
| A11 | Each 1x3 case in section 9.2, plus 1x1 and empty masks: exact expected status, area, and unmet constraints; no exception or infinite loop. |
| A12 | Disconnected islands pass only under the declared policy or a real authorized connector; no floor is invented outside masks. |
| A13 | Intact room retains gas under specified door states; removed hull segment, non-airtight glass, and space hole produce a detected leak. |
| A14 | Host-dependent sealed room verifies known host hull; incomplete context yields `UnverifiedBoundary`. Independently sealed rooms are checked separately. |
| A15 | Window fractions 0, 0.25, and 1, plus `N=0`, fixed windows, forbidden corners, and multi-cell modules: counts follow the exact denominator/tolerance rules. |
| A16 | Negative coordinates, irregular prefabs, and all allowed rotations transform anchors, ports, footprints, entities, and decals consistently. Conflicting anchors fail. |
| A17 | Preserved entity overrides, nested/container entities, and cross-entity references survive instancing without referencing the atlas or another instance. Unsupported startup side effects are rejected/deferred. |
| A18 | Optional generated clutter cannot block a route or leak the hull; immutable authored clutter causes candidate rejection unless a specific repair is authorized. |
| A19 | Doors with valid and invalid access/power states distinguish geometric reachability from actual reference-actor traversal. |
| A20 | Direct shared seam and two-door vestibule fixtures contain one owner per physical cell/entity slot, correct approach clearance, and valid pressure closure. |
| A21 | Contradictory tags/requires, nested cycles, missing prototypes, malformed masks, and constant-region conflicts fail before spawning. |
| A22 | Identical seed/input/version yields identical semantic plan/report across repeated runs and different yield timing; decoration random draws do not alter layout selection. |
| A23 | Deliberately tiny budgets and pathological masks terminate; a valid saved plan survives search exhaustion, while an unvalidated/partial plan is never published. |
| A24 | Cancellation, target deletion, content reload, and materialization failure leave no published partial output and clean up staging resources. |
| A25 | Known articulation choke and open room produce expected graph metrics; window blocks the configured projectile model while allowing sight where configured; tactical proposals preserve hard constraints. |
| A26 | Optional port sealing, solid fill, unsealed output, and island relaxations occur only with matching permission and report the original/effective constraints. |
| A27 | Mask + office theme generates compatible floor/wall/light choices and grouped workstations; primary palette stays coherent and supporting storage stays in the same logical room. |
| A28 | Atomic desk/chair assembly fails to fit: try another placement/variant or omit the entire optional core; no orphan chair/claimed complete workstation remains. Mandatory pack minima cannot silently disappear. |
| A29 | Tiny 1x3 themed fill omits optional furniture, preserves routes/materials, and places only supported nonblocking lights; omissions are reported. Hard assembly minima correctly fail. |
| A30 | Lighting requiring absent supply does not satisfy working-light coverage; occluding furniture triggers coverage re-evaluation. Unverified light estimates never pass a hard target. |
| A31 | Theme inheritance/list replacement/goal overrides, invalid pack references, incompatible materials, and fallback cycles have deterministic validation; theme preferences cannot weaken a required hull. |
| A32 | Plain entity-list pack produces grouped singleton content without custom assemblies; support surfaces, facing, container capacity, and interaction approaches are validated when applicable. |
| A33 | Adding optional pack decoration leaves structural selection unchanged; repeated seeds reproduce palette/cluster choices. Budgets cap pack expansion, assembly placement, and light sampling. |
| A34 | Down-facing default computer next to a right wall uses the 90-degree authoring rotation and faces left. All four backing walls and rotated assemblies produce matching actual interaction faces/approaches. |
| A35 | Corner machine chooses between the two inward directions using reachable empty floor or a usable associated chair; blocked fronts and isolated empty pockets are rejected. A required machine with no legal orientation fails. |
| A36 | Chair with a cardinal walking route is usable; a chair reachable only by vaulting anything that normally blocks movement fails. Test tables/desks and a non-table obstacle with the same movement behavior: neither can satisfy room/door/machine clean-passage routes even for a vault-capable actor. Adding furniture cannot cut off a previously reserved chair approach. |
| A37 | Hybrid request supplies only an irregular mask, theme/library, and seed: no target anchors, slots, choice regions, or preset rooms. Solver discovers mixed room sizes/positions/rotations and fills remaining cells without changing the outline. |
| A38 | Two otherwise feasible exterior candidates differ in actual contour fit: with adequate search budget, prefer the closer directed-edge/corner match. Test concavity, rotation, space holes, and near matches; matching bounding boxes alone is insufficient. |
| A39 | No prefab fits a stair-step boundary: hybrid uses a procedural boundary strip/residual room; prefab-only mode reports unmet coverage. Neither crops a room, crosses the target boundary, or points an ordinary door into vacuum to improve the score. |
| A40 | Constant landmark, nonrectangular preserved area, and entrance hall keep identical content/transforms across seeds while surrounding rooms vary. Routes connect to declared constant ports without modifying protected seams; impossible constant obligations fail explicitly. |
| A41 | Automatically placed L/four family retains atomic exclusivity and owns its residual gap at every discovered origin. A better-looking exterior candidate blocking mandatory interior access is backtracked completely. |
| A42 | Valid prefab placement exists only at a one-tile-shifted origin away from fast alignment proposals. Full bounded enumeration finds it with sufficient budget; tight probe limits report incomplete search and terminate even when most candidates are rejected. |
| A43 | A reusable table/device pair binds a microwave, then a supported fixture device. Engine-supported surface placement shares XY without rejecting legal overlap; missing support capability, capacity overflow, or an unreachable device front rejects the mandatory core and rolls back all reservations/children. |
| A44 | Repeated chairs explicitly adjacent to and facing a table satisfy cardinal footprint adjacency and clean seating approaches. A required corner table supports an independently oriented device facing reachable open floor; a preferred corner can be missed with a report. Test all rotations and a deep surface that exceeds interaction reach. |
| A45 | Cyclic support/container dependencies, missing members/bindings and contradictory facing constraints fail; spatial adjacency plus facing between the same pair is valid. Variants remain atomic; omitted optional singleton content does not discard unrelated valid cores or satisfy a required assembly count. |
| A46 | Cardinally connected painted cells form one blob; diagonal contact, disjoint paint and channels produce distinct blobs. Holes stay unfilled, a one-cell bridge joins components, explicit void subtraction can split them, profile conflicts and writable overlaps fail, scan order preserves normalized identity, and 1x1/1x3 blobs have bounded fallbacks. |
| A47 | Generic entrance markers and numbered convenience prototypes bind equivalent ports. Repeated labels across blobs work; duplicates within a blob, ambiguous ownership, orphan markers, unknown labels, and missing operational ports fail with coordinates/IDs. Source markers survive preview and none remain in published output. |
| A48 | Shorthand `1-3-4;9-8` and its canonical configuration normalize identically. All first-group ports connect locally, second-group ports connect locally, and no internal cross-group path exists in exact mode. Required-connections mode permits extras; a singleton receives a landing. No marker is silently omitted. |
| A49 | Two configurations have different groupings; geometry forces each in separate runs. Selection/rejection is atomic with no leaked routes, rooms or seams. A direct open door, optional nearby link, widened corridor, preserved prefab, or open/merged fallback joining exact groups is rejected. An impossible thin separation terminates with a configuration/budget diagnostic. |
| A50 | An external host loop can connect locally separate groups under `SingleNetwork`; the same loop fails request-network isolation. `DeclaredNetworks` validates independent roots inside one geometric island, and a global policy conflict or unknown required host context cannot be hidden by root auto-selection. |
| A51 | Changing a relation/binding, blob profile or connection alternative changes relevant replay identity; marker scan order, set ordering and shorthand whitespace do not. Budget/cancellation during assembly or configuration backtracking leaves no partial stage or branch claims. |
| A52 | Combined marker-authored hybrid fixture selects rooms automatically around a constant landmark, realizes an accepted exact entrance configuration, and places a relational corner table/device/chair assembly. Engine collision, operational doors, support, interaction, required lighting and pressure checks pass before publication; preview shows matching relation and connectivity witnesses. |

Add property-based or deterministic seed-sweep tests over small arbitrary masks, asserting bounds,
exclusive ownership, atomic package selection, cardinal connectivity, finite work, and honest result
statuses. Failure output must include a replayable minimal request or a retained failing seed/mask.
Successful cases alone are insufficient: include impossible inputs and negative fixtures.

Follow CONTRIBUTING.md's test guidance: demonstrate that relevant tests fail when the protected
behavior is deliberately broken, then restore the implementation. In particular, disable package
rollback, permit a diagonal edge, remove a hull segment, and bypass route protection in controlled
test verification; each corresponding test must detect its defect. Keep such mutations out of the
committed implementation.

## 17. Piecemeal implementation tasks

### Implementation ledger

Update this ledger in the same change as each implementation step. Keep task checkboxes below
unchecked until their full exit criteria pass. Each row records concrete work and validation, not
an estimate. A partial implementation is labeled `In progress`; an untouched task is `Pending`.

**Revision gate (updated 2026-10-11):** R01-R06 are in progress; R07 remains pending. Existing T-row results
remain evidence for the older supported subset only. In particular, the 99 focused unit tests and
loaded fixture do not establish relational stacking, marker ingestion or selectable connection
groups. No T task may be completed against the revised contract until its mapped rewrite exits
pass. Keep existing default-behavior regressions while adding A43-A52; do not relabel old tests as
proof of the new semantics. New contracts are supported only where the rewrite ledger explicitly
records implemented behavior; schema loading alone does not establish placement or publication.

| Rewrite | State | Existing tasks requiring revision |
| --- | --- | --- |
| R01 | In progress | `KsProcgenAssemblyPrototypes` and `KsProcgenAssemblyCompiler` implement reusable named members, required/optional multiplicities, entity bindings, complete ordered variants, typed spatial/context/support relations, and normalized core records. Expansion is bounded to 64 members/256 relations, targets must resolve to one instance, support/container cycles and multiple support parents are rejected separately from valid spatial cycles. Theme validation compiles both assemblies and legacy singleton entries; entries and assembly references carry core minima. Loaded fixtures resolve a table/device/optional-chair core and a standing variant. The live office pack now references an explicit desk/seat/console assembly. `KsProcgenPrototypeCapabilityInspector` copies inherited surface, fixture, rotation and container declarations into bounded snapshots and support inspection witnesses; declaration hashes are separate from full content/geometry hashes. Remaining: full schema/replay exit coverage and R02/R06 adapters proving actual placement and insertion; snapshots never certify engine placement. |
| R02 | In progress | T09a/T15/T15a: normalized floor cores replace whole-pack atomic lists. Mandatory core counts are enforced across all selected packs before optional content, then required pack minima select complete cores. Bounded mandatory continuations revisit earlier poses, whole variants and weighted core choices when later requirements cannot fit, rolling back claims, protected paths, witnesses and copy/anchor metadata. Unrelated optional singletons can be omitted. Office adjacency, chair facing and console seat association are explicit. Required/preferred corner context and clean-path `Near` rules produce persisted geometric witnesses. Authored landings rotate with members and remain root-accessible; accepted Near paths are protected from later furnishing. Floor search revisits member positions, permitted orientations, selected interaction approaches and optional-member omissions. Path searches share a 4096-node room budget. Optional initial cores and repeated density draws use seeded weighted ordering; density placement retries other whole cores when a heavier candidate cannot fit. Semantic hash v20 distinguishes mandatory continuation search. Remaining: joint optional/lighting search and preference optimization, support/container layers, lighting layer checks and engine validation. |
| R03 | In progress | T01/T02/T03/T04: geometry/theme/entrance profile references, paint and generic/numbered entrance components/prototypes, explicit void-grid host binding, bounded read-only grid snapshot, exact cardinal components, void subtraction, directed wide entrance ownership, scoped labels, conflict checks and stable identity. Geometry-only conversion rejects entrance-bearing blobs. Real doorway/context inspection, constants/envelopes, complete profiles and publication remain pending. |
| R04 | In progress | Complete configuration contracts, shorthand, request transport, DeclaredNetworks roots and replay identity. A bounded seeded selector checks complete supplied scenes without mixing alternatives. Complete owned interior search retries ordered subdivision preferences within declared configurations after downstream rejection and retains tagged atomic attempt journals. Configuration and layout caps are separate; all reported pipeline work survives failed layouts. Painted-area profiles transport immutable subdivision preferences, room-size goals, fallback permissions and window goals into explicit owned planning. An opt-in rectangular size-goal strategy precedes scalar attempts, with requested/used strategy diagnostics and protected-floor refusal. Bounded global quota allocation across separate rectangular networks is implemented. Bounded complete quota retries after protected seams are implemented. Bounded fitting of L-shaped and stepped networks is implemented. Joint route/room/content choice, general shapes with holes and quota reassignment after content failures remain pending. |
| R05 | In progress | Component analysis checks local groups, forbidden joins, host bypasses, roots, sealed thresholds and complete floor assignment with shared work limits. Procedural adapters construct routes, grow owned floor, and partition each network independently. Opt-in rectangular strips fit simultaneous final floor-size bands including wall loss, then use the same protected-route seam solver. Themes, furnishings and bounded lighting retain entrance reservations. A final interior audit checks room-local clean access, interaction/door approaches and the complete post-placement entrance scene; vault-required furniture and floor fixtures are blockers. Local fallback cannot remove inter-network walls. Independently revalidated local tactical queries measure chokes, alternate routes and redundancy with shared budgets; host detours cannot join groups. Owned materials preserve primary entrance tiles and read-only floor, check doors, cover separators and reject shared wall conflicts. Owned hull/window adapters reserve entrance/host spans, retain unknown shell, validate explicit directional exterior facts, count fixed windows without writes and substitute compatible theme prototypes under shared budgets. A complete owned proposal composes these stages atomically; legacy generation entry points reject entrance-aware inputs. Alternate trees, arbitrary-shape/joint room fitting, prefab/hybrid/interior-fill composition, live windows, full-host tactical ownership and engine validation remain pending. |
| R06 | In progress | T10/T12/T13/T15: `KsProcgenContainerPreflightSystem` delegates live eligibility to engine APIs. `KsProcgenContainerStageSystem` owns disposable paused container-only previews, inserts members in dependency order, checks actual membership/parent transforms and rolls back failures. Explicit initialization verifies selected lifecycle, membership and managed-slot filter compatibility afterward and is idempotent. Insertion-veto fixtures cover preflight, commit and actual container insertion without replaying attempts during verification. Surface/spatial relations remain unsupported there; temporary root coordinates are not validated layout poses. A joint room-core adapter now owns optional grid floor/wall terrain with detached masks, cumulative allocation/initialization limits and exact frame/terrain verification. Fresh initialized scene inspection rejects foreign entities, unsupported shapes and member/wall hard overlaps; continuous fixed-actor cardinal sweeps enforce conservative floor coverage and declared vault/blocking exclusions without moving the actor. The wall lattice is a permitted structural union. Remaining: primary layered pipeline integration, support settling, actual machine/chair/door operation, gas and working-light checks, side-effect isolation and publication. Verification flags remain false; the standalone tile stage still rejects furniture. |
| R07 | In progress | Stable declaration/geometry identities, detached versioned JSON diagnostics, exact fresh-report comparison, escaped SVG review samples and a localized Debug paint-snapshot command are implemented. Metadata and geometry truncation are separate; unverified engine claims remain false. Remaining: full request work metering, engine/content replay, richer editor workflows, complete acceptance/release coverage and the parent rewrite exits. |

Latest composition amendment (2026-10-10): R05 now composes a complete procedural entrance-aware
footprint proposal through `KsProcgenOwnedInteriorPlanner`, sharing reported graph, furnishing
candidate and lighting fixture allowances, checking fallback permissions and retaining an atomic
stage journal. It includes exact structure/window choices and a fresh final interior-access audit.
Its replay identity is owned interior v1. R02 relation-path allowance defaults to 4096 per room but
may be reduced by the coordinator's remaining reported graph budget. Complete pipeline integration
in the R05 row still requires downstream retry, prefab/hybrid/interior-fill support, full metering
and actual staged engine validation/publication; this composition is an unpublished geometric
candidate. Rough overall completion is 57% (40-60% uncertainty), not a percentage of passed tests.

Latest search amendment (2026-10-11): R04/R05 now retry complete positive-weight declared
configurations after geometric/content rejection or denied fallback. Shared reported graph, order,
merge, furnishing and provisional fixture work survives discarded attempts. Invalid input and
budget exhaustion stop immediately. Attempt journals retain failure reasons and local fallback
traces; accepted placements and root fallback events belong to one complete candidate. Owned
interior identity is v2; owned floor selection remains v1 and full geometry v20. Alternate trees,
room subdivision, optional/lighting optimization, complete CPU metering and live publication
remain open. Rough overall completion is 58% (40-60% uncertainty).

Latest clean-passage accounting amendment (2026-10-11): the owned interior coordinator now
charges furnishing terminal routes, machine-facing searches, chair approaches, declared landing
validation and reserved interaction walkways to the same remaining graph allowance as relational
paths. Counters distinguish relation and clean-passage expansions; rejected placement branches
cannot refund either. A dequeued node, including the goal, costs one expansion. Exhaustion returns
no partial furnishing proposal. Standalone furnishing callers retain relation-only accounting
unless `meterCleanPaths: true` is supplied. In that mode `maxRelationPathExpandedCells` limits the
combined work (0-65536 per room); the owned coordinator supplies the lesser of 65536 and its
remaining request allowance. Partition seam work and non-graph placement/compilation work still
use separate conservative limits, so `GraphWorkFullyMetered` stays false. Rough overall completion
is 59% (40-60% uncertainty); engine validation and publication remain incomplete.

Latest partition accounting amendment (2026-10-11): entrance-owned partitions now charge every
zone/group/source/final-floor connectivity flood to the remaining request graph allowance.
`KsProcgenPartitionWorkBudget` also meters seam ownership scans, directed cardinal boundary
queries, interface edges, door candidates and accepted-door approach checks. Its shared seam
allowance defaults to 262144, accepts 0-1000000, and survives network and configuration retries.
`MaximumPartitionSeamProbes`, attempt/stage journals and result `PartitionSeamProbes` expose
that separate work; it is not counted as a graph expansion. `MergeAttempts` counts actual merges,
including those performed before a later failure, separately from rejected split diagnostics.
Exhausted work produces no open fallback or reusable partition geometry. Standalone legacy
partition callers remain unmetered unless a work budget is supplied. These counters do not change
the fully funded geometry hashes (owned interior v2, floor/partition v1, full geometry v20).
Allocation, sorting, normalization and content compilation still require complete work accounting;
`GraphWorkFullyMetered` remains false. Rough overall completion is 60% (40-65% uncertainty).

Latest subdivision search amendment (2026-10-11): the owned interior coordinator can retry
ordered automatic zoning sizes inside one entrance configuration. Supply the preferred maximum
room size and optional distinct `AlternativeMaximumRoomCells`; each fresh complete attempt
shares all request work allowances. `MaximumLayoutProbes` caps attempts independently of the
configuration cap. A floor rejection skips redundant subdivision attempts; downstream rejection
or forbidden fallback tries the next size. Stages carry the layout ordinal and size preference.
Only the accepted attempt supplies artifacts/fallbacks; its semantic identity matches a direct
attempt producing identical content. This bounded preference search is not exhaustive geometry,
route-tree or room-size optimization. Marker/profile authoring, automatic size-goal strategies,
prefab/hybrid search, full work accounting, replay serialization and engine publication remain
rewrite work. Rough overall completion is 61% (40-65% uncertainty), subject to the gates below.

Example programmatic owned-planner preferences:

```csharp
var budgets = new KsProcgenOwnedPlanBudgets
{
    PreferredMaximumRoomCells = 8,
    AlternativeMaximumRoomCells = [24, 64],
    MaximumConfigurationProbes = 4,
    MaximumLayoutProbes = 12,
};
```

For each enabled configuration, try 8, then 24, then 64 only after a complete downstream failure.
Each attempt spends its own floor and downstream work from the shared request allowance; these
preferences do not refund route work or permit mixing geometry from attempts. All three sizes
still use automatic zoning within the painted footprint, subject to existing seam merges.

Latest subdivision authoring amendment (2026-10-11): `ksProcgenAreaProfile.roomSubdivision`
now serializes minimum/preferred/ordered alternative sizes. Live marker snapshots validate and
detach these values; authored size preferences override caller size preferences when explicitly
planning a blob, while every work cap remains caller controlled. Omitting the field preserves
caller-selected sizing. Snapshot identity is now `ks-procgen-area-snapshot-v3`, including presence,
minimum, preferred size and ordered alternatives; geometry-derived blob IDs remain v1. Accepted
owned-interior identity stays v2 and describes final content independently of unused alternatives.
Declaring subdivision preferences for hybrid or interior-fill profiles fails explicitly pending
their rewrite. Snapshots still do not allocate interiors, remove markers or publish geometry.
Automatic size-goal strategies and authored fallback/size-goal transport remain pending.
Rough overall completion is 62% (40-65% uncertainty); engine validation/publication is incomplete.

```yaml
- type: ksProcgenAreaProfile
  id: MyOfficeArea
  theme: KsProcgenSimpleOffice
  roomSubdivision:
    minimumRoomCells: 4
    preferredMaximumRoomCells: 8
    alternativeMaximumRoomCells: [24, 64]
```

Assign `MyOfficeArea` to the `profile` of connected `KsProcgenAreaCell` markers. After a snapshot,
explicitly call `KsProcgenOwnedInteriorPlanner.Plan` with one blob, seed, inspected host context,
exterior facts and caller budgets. To customize request goals/fallback permissions, use
`blob.CreateRequest(seed)` and `blob.CreateOwnedBudgets(budgets: callerBudgets)` with the request
overload. The blob overload consumes the profile theme and subdivision values automatically.

Latest goal authoring amendment (2026-10-11): area profiles now expose optional `generationGoals`
with `sizeMix`, `fallbackPolicy` and `windowGoal`, using the existing request definitions. Live
snapshots validate and copy them to immutable values. Each `blob.CreateRequest(seed)` reconstructs
fresh mutable request objects; modifying one request or a live prototype cannot change a previous
snapshot or a later request. Shared `KsProcgenGoalValidation` preserves request validation reason
codes, area/count/window limits and nonoverlapping size bands. Authoring additionally caps size
bands at 64 and IDs at 128 printable ASCII characters, matching bounded profile conventions.
Snapshot identity is now area snapshot v4: it includes goal presence, canonical size bands by ID,
all four fallback permissions and every window target field. Blob IDs remain v1 and final owned
interior identity v2. Omission keeps default request goals/policies; an explicit defaults block is
distinguished in the input snapshot. The explicit blob planner consumes these goals automatically.
Caller work limits, inspected host/exterior facts and publication authority remain separate.

```yaml
generationGoals:
  sizeMix:
  - id: Medium
    minCells: 12
    maxCells: 24
    targetCount: 2
  fallbackPolicy:
    allowMergedPartition: false
    allowRoomSizeShortfall: false
    allowSparseFurnishing: true
    allowSparseLighting: true
  windowGoal:
    exteriorWindowFraction: 0.25
    hardFraction: false
    toleranceCells: 1
    minimumCount: 0
```

Add this block beside `roomSubdivision` in a `ksProcgenAreaProfile`. Size counts apply to final
themed room interiors after walls/merges; they do not guarantee that any footprint can satisfy the
target. Disallowed deviation rejects that candidate and tries the next subdivision/configuration;
allowed fallback can accept the first degraded candidate. Hard window goals still need suitable
inspected boundary facts and geometry. `KsProcgenWholeOfficeArea` is a concrete loaded example
with a required 30-cell room and 4/8/64 subdivision preferences. Automatic size-goal-driven zoning,
joint route/content search, full work accounting, replay serialization and engine publication
remain rewrite work.

Rough overall completion after goal authoring is 63% (40-65% uncertainty); the geometric
candidate pipeline still does not certify or publish an engine interior.

Latest size-goal subdivision amendment (2026-10-11): `roomSubdivision.useSizeGoals: true`
opts a procedural footprint into a bounded rectangular strip strategy before its ordinary
scalar-size alternatives. `KsProcgenOwnedPlanBudgets.UseSizeGoals` supplies the same preference
for request callers; an authored subdivision profile overrides this preference, preserving all
caller work allowances. Omission retains existing behavior. Area input snapshot identity is now
v5, including the strategy flag; geometry-derived blob IDs remain v1 and accepted owned-interior
identity remains v2. A discarded strategy or unused preference does not change final identity.

The first strategy targets one connected owned entrance network whose floor exactly fills a
rectangle. Sort size bands by descending maximum, then ordinal ID; repeat each band by its
requested count, capped at 256 proposed rooms. Try strips along X, then Y. For cross-section `H`,
the first room has `width * H` floor cells; each subsequent room has
`width * H - (H - 1)` floor cells, reserving a wall band with one door opening. Compute each
strip's feasible integer width interval from the inclusive size band and minimum room size.
Require at least two columns per strip for a cardinal inside approach. If total minimum/maximum
widths bracket the available dimension, distribute spare columns in canonical room order.
This covers the rectangle exactly without an unclassified leftover strip. It neither pads the
footprint nor changes room-size bands. Ordinal zone IDs are padded to preserve spatial wall
ownership after the tenth room.

For example, `KsProcgenMixedOfficeArea` declares a 16-cell large room, a 9-cell medium room and
a 5-cell small room. A 9x4 footprint yields raw strips of 16, 12 and 8 cells; the latter two lose
three wall tiles each, leaving exactly 16/9/5 floor cells and two doorways. Assign this profile to
connected area markers and place an entrance at the left edge near the first room. The profile
selects all sizes together without editing the generated request. Furniture, materials, lighting
and independent access validation still run on the resulting final interiors.

```yaml
roomSubdivision:
  useSizeGoals: true
  minimumRoomCells: 4
  preferredMaximumRoomCells: 64
generationGoals:
  sizeMix:
  - id: Large
    minCells: 16
    maxCells: 16
    targetCount: 1
  - id: Medium
    minCells: 9
    maxCells: 9
    targetCount: 1
  - id: Small
    minCells: 5
    maxCells: 5
    targetCount: 1
  fallbackPolicy:
    allowMergedPartition: false
    allowRoomSizeShortfall: false
```

This is a layout proposal, not a promise that protected routes or preserved content permit its
seams. The existing seam solver, ownership audit and final themed size-count check remain
authoritative. If either axis cannot fit the size bands, the goal attempt uses ordinary scalar
zoning. Irregular, one-tile-wide and multiple-network footprints also retain that path; quotas
above 256 do not expand into an oversized room list. If a proposed seam or downstream content
fails, the coordinator tries a separate scalar attempt, then its authored scalar alternatives
and entrance configurations. A goal attempt consumes one layout slot, including when fitting
falls back internally; denied proposals never refund graph, seam, merge, furnishing or fixture
work. Floor failures still skip subdivision alternatives. `SizeGoalsRequested` in attempt/stage
journals identifies the attempted strategy; `Partition.SizeGoalSubdivisionUsed` reports whether
rectangle fitting actually supplied the zones. Neither flag claims all goals survived merging.

Inspecting goal-strategy witness cells is charged to the shared graph allowance, including failed
fitting. Partition connectivity and seam work use the existing shared allowances. Bounded width
arithmetic, rectangle metadata, ordering and zone-list allocation are not completely metered;
`GraphWorkFullyMetered` remains false. Arbitrary-shape goal search, quota allocation among separate
networks, alternate feasible width distributions, joint route/content search, replay serialization
and real engine publication remain open. The orientation retry amendment below supersedes the
initial strategy's stopping at the first geometrically feasible axis.
Rough overall completion after this amendment is 64% (40-66% uncertainty).

Latest orientation retry amendment (2026-10-11): rectangular goal fitting now retains both feasible
X/Y layouts in canonical order. The group partition planner solves their seams independently,
preferring the first `Proposed` result over a merge/open fallback. A protected wall on the X layout
can therefore select a Y layout within the same complete goal attempt. If every feasible
orientation requires a fallback, retain the first fallback and its matching zone assignment.
Never combine floor, walls, doors or zone membership from different orientations. One-room
orientations are identical and are proposed once. Legacy scalar/explicit-zone planning is unchanged.

All orientation attempts share the existing graph/seam work object and remaining merge allowance.
Failed attempts consume actual work, including merges performed before discarding their geometry.
Budget exhaustion is terminal, even when an earlier valid fallback is available; it cannot be
converted into that fallback. The final ownership/access scene is checked after selecting one
whole layout. `SizeGoalAttempts` exposes immutable nonreusable diagnostics: `AcrossX`, partition
status, reason code, expansions, seam probes and merge attempts. On a direct group failure these
diagnostics survive with no placement artifacts/hash. On complete owned-planner failures the
existing stage journal retains total work and the terminal reason; full serialized per-orientation
replay remains pending. Group totals include discarded orientations; the accepted region's own
partition counters describe only its selected seam solve. Orientation attempts are bounded at two
inside one complete `MaximumLayoutProbes` slot, not two complete downstream pipeline attempts.

An abstract 6x6 rectangle with protected floor on X=3 and Y=0 can retain 18/13-cell rooms by turning
the seam to Y=3 and placing its opening at X=3. This geometric witness does not authorize a new
door on preserved floor: the full material adapter still rejects a readonly new door threshold.
The loaded complete-pipeline fixture instead preserves Y=0 and the first three cells of X=3,
blocking X seams while leaving the alternate doorway writable. It requires exact room counts and
disallows merged partitions. `KsProcgenEmptyOfficeThemeFixture` requests floor/wall/door materials
without optional contents or lights, isolating orientation and access from content search. The
ordinary office theme can produce a disconnected clean pocket for this fixture; the independent
access audit correctly rejects that candidate. Joint retry of those content choices remains open.
The loaded empty-theme fixture verifies all pipeline stages, exact allowable budgets and unchanged
engine entities. Existing area snapshot v5, group partition identity v1 and owned interior
identity v2 remain unchanged: final selected geometry/membership already describes orientation,
while discarded diagnostics do not enter accepted semantic identity. Rough completion remains 64%.

Latest width-search amendment (2026-10-13): rectangular goal subdivision now searches a bounded
neighborhood of feasible strip widths. The canonical greedy allocation stays first. A breadth-first
neighbor transfers one column from a donor room above its minimum width to a recipient below its
maximum width, preserving total footprint length and all inclusive final floor-size bands. Clone
each width vector before changing it and deduplicate equal vectors. Explore at most eight width
vectors and 4096 transfer examinations per axis; try all X candidates before Y candidates, for at
most sixteen internal seam solves per complete goal attempt. Exact size bands often yield only
one vector. One-room layouts remain deduplicated across axes. This supersedes the earlier two-solve
orientation bound; it does not add an authored field or grant extra request work allowances.

Each layout is an atomic zone assignment. The seam solver still validates protected floor, door
approaches and connectivity. It selects the first unmerged candidate; if every permitted solve
needs a fallback, it retains the first whole fallback. Shared graph/seam/merge allowances include
every discarded width candidate, and exhaustion remains terminal. `SizeGoalAttempts.WidthVariant`
identifies the zero-based candidate within its axis; `AcrossX` disambiguates equal indices on the
other axis. Room IDs stay stable across width changes; selected zone membership and final geometry
already enter group/interior identity. Discarded ordinals remain diagnostic metadata. Area snapshot
v5, group partition v1, owned interior v2 and blob IDs v1 remain unchanged.

For a 10x4 rectangle, large-room floor band 20..28 and small-room band 5..13 produce greedy raw
widths 7/3 (28/9 final floor cells). If protected floor blocks X=7, widths 6/4 move the seam to X=6
and produce final 24/13 rooms. The loaded fixture preserves two cells on X=7, adds a declared
entrance-group root at (7,0), and lets routing create the connecting writable walkway. It uses an
empty room theme to isolate strict room counts, materials and access, requiring a writable new
door threshold. It passes all owned stages without engine writes and replays at exact graph/seam
allowances. An 11x4 fixture verifies that the search can move through two successive transfers;
other cases enforce inclusive bounds, eight/sixteen-candidate caps, input permutations and atomic
      budget failure during a width retry.

Width arithmetic, vector deduplication, metadata ordering and zone-list allocation remain bounded
metadata work rather than fully charged request work. `GraphWorkFullyMetered` remains false.
The capped neighbor search may miss a feasible distribution farther from the greedy vector, and
it does not retry width choices after a later furnishing/light/access failure. Arbitrary shapes,
per-network quota allocation, joint route/content search, full work accounting/replay and actual
engine validation/publication remain rewrite work. Rough full-system completion is now 65%
(40-67% uncertainty).

Latest network-quota amendment (2026-10-13): `useSizeGoals` can now allocate the request-wide
room-count goals across multiple independently owned entrance networks. Reconstruct ownership
from independently inspected component witnesses before allocating; never combine networks,
duplicate a global quota per network, or replace separation walls to meet a size goal. Sort networks
by component identity and goals by ordinal ID. Enumerate bounded count vectors, subtract each
candidate from the remaining request quotas, and backtrack if a later network cannot fit. Every
network must receive at least one room. The last network receives exactly the remaining counts.
Accept only a complete allocation whose every network has a feasible rectangular strip proposal.

The allocator supports at most 32 networks and 256 rooms across the entire request. It permits
4096 count-vector examinations and 64 rectangle-fit calls per group-partition invocation, including
failed/backtracked allocations. `Partition.SizeGoalQuotaProbes` and `SizeGoalFitProbes` report these counts, including
on partition failure. This metadata allowance is a fixed implementation cap, not extra graph or
seam work. Charge each inspected component cell once against the shared graph allowance, even
when allocation fails or an unsupported input falls back. Explicit supplied zones bypass allocation.
Single-network planning keeps its existing proposal order and width/orientation behavior.

An infeasible or capped allocation discards all tentative goal layouts and uses scalar subdivision
for every network; it cannot expose a partially fitted set. Strict final size-goal auditing still
rejects any deficit or excess. A successful allocation supplies each network's existing bounded
width/orientation alternatives to its seam solver. All selected and discarded solves share the
original graph, seam and merge allowances. At most sixteen seam solves per network are permitted,
with a 512-solve maximum across the supported 32 networks before shared allowances terminate work.
`SizeGoalAttempts.NetworkId` identifies the owning network alongside `AcrossX` and `WidthVariant`.
The allocator picks its first geometrically feasible complete quota assignment; it does not retry
other assignments after protected seams or later furnishings/access reject that choice. Therefore
this remains a bounded heuristic and may miss an otherwise feasible room distribution.

The loaded strict-count fixture contains disconnected 9x4 and 4x4 networks and requests two
16-cell rooms, one 9-cell room and one 5-cell room. Allocation gives the first network 16/9/5
final floors and the second network one 16-cell room. Material, empty-theme content and independent
access stages complete without engine writes; both entrance networks remain separate. Flexible
bands also exercise backtracking from two large rooms in the first rectangle, which would leave
an impossible quota for the second. Existing snapshot v5, group partition v1 and owned interior
v2 identities remain unchanged: selected region/zone membership and final geometry already encode
the accepted allocation. Probe counts and rejected alternatives are diagnostics, not semantic hash
inputs. Arbitrary-shape fitting, quota reassignment after seam/content failure, prefab/hybrid and
interior-fill goal fitting, full work metering/replay and engine validation/publication remain open.
Rough full-system completion remains 65% (40-67% uncertainty).

Latest protected-seam quota retry amendment (2026-10-13): after exhausting a network's bounded
width/orientation candidates, an owned multi-network partition may retry a different complete
room-quota assignment. This supersedes the first-geometric-allocation restriction above for seam
fallbacks. Solve every network for an assignment; accept the first assignment whose every network
has an unmerged `Proposed` partition. If any network needs an open/merged fallback, retain that
first whole assignment and blacklist its quota identity before searching another one. Never combine
regions, zones, walls or floors from different assignments, and never combine entrance networks.
The quota identity uses sorted goal IDs, inspected component IDs and exact count vectors under
`ks-procgen-size-quota-v1`; it is an internal search key, not accepted map identity.

Retry at most eight complete quota assignments per group-partition invocation. All allocator
invocations share the original 4096 vector / 64 fit probe caps; reexamined vectors and rejected
complete assignments count again. Every discarded seam solve shares the original graph, seam and
merge allowances. Inspect witness cells once before quota search. A graph/seam budget exhaustion
is terminal and publishes no artifact, even if a previous whole fallback exists. If quota metadata
caps, the eight-assignment cap or feasible alternatives run out, independently audit and retain the
first whole fallback with its matching region geometry. The final strict room/fallback policy may
still reject it. At most 4096 internal seam solves (8 assignments x 32 networks x 16 layouts) are
possible, and shared request allowances can stop search earlier. No fallback pass is exempt from
the ordinary work limits.

`Partition.SizeGoalAllocationAttempts` counts begun complete assignments. Frozen
`SizeGoalAttempts.AllocationOrdinal` tags seam diagnostics alongside `NetworkId`, `AcrossX` and
`WidthVariant`. Graph/seam/merge totals include failed assignments; accepted network artifacts
describe only the selected assignment. Quota/fit counters remain cumulative, including failed
enumeration after the last candidate. Explicit zones, disabled size goals and single-network
planning retain their existing behavior. These are bounded diagnostics; full request metering and
serialized replay remain open, and `GraphWorkFullyMetered` remains false. Accepted geometry hashes
remain group partition v1 / owned interior v2: chosen membership and geometry already encode the
result, while discarded search ordinals do not enter semantic identity.

The regression fixture has two separate 8x4 rectangles and requests three rooms in band 4..64.
The second rectangle is preserved floor. The first quota assigns one room to the writable network
and two to the preserved network; every candidate second-network seam is blocked. The retry
assigns two rooms to the writable network (24 and 5 final cells) and one 32-cell preserved room.
Both networks remain separate and all preserved cells remain floor. A loaded empty-theme run
completes strict counts, forbids merged partitions, replays exact reported work and leaves live
entities unchanged. If both rectangles are preserved, retain the original whole 1/2-room quota
fallback, despite fully charging the attempted 2/1-room assignment. Separate cases exhaust graph
or seam allowance during the retry and verify diagnostic retention without artifacts. Later
furnishing/light/access failures still require joint quota/content search; arbitrary shape fitting,
prefab/hybrid and interior-fill modes, engine validation and publication remain pending.

Latest reflected-strip amendment (2026-10-13): opted-in goal subdivision can try the opposite
edge of the same rectangular entrance network. Keep every existing canonical X then Y candidate
first, then append reflected X then Y candidates. For a reflected candidate, select cells using
`length - 1 - coordinate` while preserving the same axis and target rectangle. Room size goals
stay ordered by descending maximum size and ordinal ID; their spatial order reverses. This is a
bounded alternative whole layout, not a coordinate transform of authored entities or entrance ports.

Compute wall loss from spatial ownership, not from the goal's original index. The low-coordinate
room owns no dividing wall; each subsequent room loses one full cross-section minus its single
door threshold. In reflected order the last goal is that first spatial room. Assign padded zone
IDs by increasing physical strip order, reverse the goal index for reflected candidates, and sort
the exposed zone list by those IDs. The seam solver uses that same stable order to decide wall
ownership. Simply reflecting cells while retaining the original indices or floor arithmetic can
silently put the dividing wall in the wrong room and violate exact size goals.

Permit eight width vectors and 4096 transfer examinations per axis per direction, for at most
32 internal seam candidates per network (canonical X/Y plus reflected X/Y). One-room reflections
are identical and are omitted; one-room axis duplicates are still omitted. This supersedes the
earlier sixteen-layout bound. A multi-network solve can now attempt at most 8192 seam candidates
(8 quota assignments x 32 networks x 32 layouts), subject to the unchanged shared request graph,
seam and merge allowances and unchanged cumulative quota-fit caps. Failure never refunds work
or grants a fallback an additional allowance. Retain the first complete fallback if all permitted
directions fail; terminal graph/seam exhaustion still returns diagnostics without artifacts.

Frozen `SizeGoalAttempts.Reflected` disambiguates direction alongside axis, width variant,
network ID and allocation ordinal. No new authored flag is needed: `useSizeGoals` opts into the
entire bounded strategy. Explicit zones and disabled goal planning retain their behavior.
Accepted partition/interior identity remains v1/v2 because chosen cells, zone membership and
door geometry already encode reflection; search diagnostics are excluded. Width-vector arithmetic,
ordering and allocation remain bounded metadata work, not fully metered request work.

Regression geometry: a 10x4 rectangle requests large floors in band 20..28 and small floors in
5..13. Protected pairs on canonical X=6, X=7 and Y=2 seams block all canonical candidates. A
reflected X candidate puts the small room in columns 0..2 (12 floors), its dividing wall in
column 3, and the large room in columns 3..9 (25 floors after three wall tiles). Transposing the
fixture tests reflected Y. Exact 25/12 goals are feasible only in reflected order, independently
of those blockers, proving the first-spatial-room wall-loss calculation. The loaded empty-theme
fixture forbids merges and size deviations, independently checks materials/access, replays exact
reported work and leaves live entities unchanged. Arbitrary-shape fitting, other room-order
permutations, later quota/content retries, full metering/replay and engine publication remain open.

Latest irregular-strip amendment (2026-10-14): opted-in room-size goals now support a bounded
subset of nonrectangular entrance networks. Preserve the existing rectangular strategy unchanged.
When an inspected network does not cover its bounding rectangle, use
`KsProcgenIrregularSizeGoalSubdivision` to try canonical and reversed goal order on both axes.
Require every column on the chosen axis to contain one contiguous span, with no missing columns,
and require adjacent spans to overlap. This supports L shapes, stepped outlines and other shapes
admitting that strip representation. It does not fill absent corners, holes or exterior cells.
Shapes unsupported on both axes retain whole scalar fallback and final strict-goal auditing.

Compute prefix floor areas and shared boundary overlaps from inspected witness cells. Enumerate
cuts in ascending coordinate order, keeping at least two columns per room. The first spatial room
loses no dividing wall. Each later room loses only the cells shared with the preceding column,
minus its one door threshold: `rawStripArea - (overlapCount - 1)`. Do not subtract the entire new
column height when its span extends beyond the previous room. Require a nonempty common span
across the preceding, boundary and following columns so a cardinal threshold and two approach
tiles are geometrically possible. The ordinary seam solver still decides protected-floor refusal,
actual door selection, connectivity, merging and fallback; these metadata predictions do not
certify engine movement or spaceproofing.

Limit irregular fitting to 64 rooms per network, 4096 columns on either axis, 4096 examined cut
endpoints and eight completed cut vectors per axis/order. Clone each vector before further search
and expose frozen zone/layout lists. At most 32 internal layouts can be proposed per network.
Try canonical X/Y before reversed X/Y, assign padded room IDs by physical strip order, and use
the existing `AcrossX`, `Reflected` and `WidthVariant` diagnostics; for irregular shapes the latter
is the deterministic cut-vector ordinal. Deduplicate the one-room axes and omit its reverse order.
Rectangle limits remain 256 rooms and their existing width-neighbor search. Multi-network quota
allocation automatically uses the new irregular fitter under its unchanged shared metadata caps;
all seams, retries and final scene audits use the same request graph/seam/merge allowances.

An 8x4 L shape with the upper-right 2x2 corner absent contains 28 cells. A cut at X=4 assigns a
16-cell first room and a raw 12-cell second room. Its four-cell shared boundary loses three wall
tiles, yielding the exact requested 16/9 floors. Transposition verifies the other axis. Flexible
16..20 and 5..9 bands can retry a protected X=4 seam at X=5, producing 20/5 floors. A reflected
step with two initial 2-cell columns and six 4-cell columns yields 4/23 floors: the later room
loses one shared-boundary wall tile, not three full-column tiles. Interior holes unsupported on
both axes fall back atomically. All-preserved fixtures bound cut vectors and retain the canonical
whole fallback; graph/seam exhaustion still exposes diagnostics without artifacts.

The loaded L-shaped empty-theme fixture requests exact 16/9 counts, forbids merges and size
deviations, checks independent materials/interior access, replays exact reported work and leaves
live entities unchanged. Area snapshot v5, partition v1 and owned interior v2 identities remain
unchanged: selected cells, membership and doors already encode nonrectangular layouts. Span
construction, prefix arithmetic, cut enumeration and output allocation remain bounded metadata
work rather than fully metered request work. General shapes with holes, arbitrary room-order
permutations, joint content search and real engine validation/publication remain rewrite work.
The historical checkpoint after this irregular-strip amendment was **66%** (40-68% uncertainty).

Latest room-core amendment (2026-10-14): `KsProcgenRoomAssemblyComposer` namespaces complete
selected instances into one detached assembly for joint support-aware room pose search. Bound
composition to 16 instances, 64 total members and 256 total relations. Sort pack/core/copy identity,
then source member/relation IDs; use short ordinal logical IDs and retain a separate frozen binding
manifest with source pack, core, copy, assembly, variant and member provenance. Rewrite every
relation endpoint inside its own instance, retain slot/container declarations and copy mutable
entry footprint/rotation lists independently for every copy. A malformed reference, duplicate
copy or exceeded bound returns no partial assembly or manifest. Composition does not authorize
physical overlap or treat a surface support as a transform parent.

`KsProcgenRoomCoreSearch` enumerates complete submitted variant combinations and calls the
existing supported pose/rotation/operating-landing search on the entire composed room. It can
backtrack floor roots across instances because they participate in one search; it cannot scatter
members from different variants. Enforce declared minimum copies of submitted cores. Share
variant (at most 64), placement, landing and path budgets across failed combinations; exhausted
work is terminal and returns diagnostics without a selected composition or pose. `SearchPacks`
resolves every named pack and includes every authored core minimum. It defaults to one seeded,
weighted optional core per pack. An explicit optional list, including empty, replaces only that
default; optional overrides cannot remove required copies. Bound packs to 16 and optional copies
per core to eight. Optional selection is currently fixed before pose search; trying other optional
cores, density optimization and all per-core assembly-rotation combinations remain further work.

Projected supported members may extend beyond their floor root's reserved footprint. Strengthen
the shared supported search to reject overlap between the projected unions of different support
trees, including unrelated roots within a single core. Members of one support tree may share
declared XY but still require engine approval. Keep external occupied/blocking masks and protected
network cells active. Fixed-root regression fixtures demonstrate that a shifted surface item cannot
claim another root's tile, while a separated root remains feasible.

`KsProcgenAssemblyStageSystem.TrySearchRoomStage` consumes a freshly produced room-search
witness directly, without resetting its allowance or rerunning geometric paths. Preflight operation,
retention, cancellation, member and allocation limits; then allocate one owned paused map through
the existing assembly staging lifecycle. Every source binding still points to its own support
parent after initialization; generated entities stay captured by the existing owned resource
budget and disposal. Preserve the existing rejection of spatial engine relations. The primary
floor furnishing/owned interior pipeline remains unchanged until supported layering and operating
validation can be consumed there safely. All room search, support, pose and initialized preview
results still report engine placement as unverified; there is no publication API.

Latest owned room scene amendment (2026-10-14, verified limited scope): the room-core preview
can allocate declared floor/wall terrain on its own disposable grid using a supplied registered
tile and wall prototype. Tile centers are `(X+0.5,Y+0.5)`; map-only previews retain their original
frame. Terrain inputs are detached, floor/wall masks cannot overlap, all allocations share the
spawn allowance and ownership cleanup, and verification rejects changed/extra/missing tiles,
walls or grid transforms. The map stays paused before and after explicit initialization.
`KsProcgenRoomFixtureSystem` reads initialized live hard fixtures and actual transforms, refuses
foreign entities/unsupported shapes, checks same-scene overlap and models cardinal access for
an external actor at its fixed current orientation. Each cardinal step uses an exact continuous
convex translation sweep with skin radii and the engine's bidirectional mask/layer filter rule.
The actor's entire swept bounds must conservatively remain on declared floor; vault-required or
normally blocking declared content is excluded even when its physics fixture is soft. Scans,
fixture pairs, graph expansion and floor probes have separate bounded totals. Internal authored
wall-wall joins are a permitted structural union; member-member/member-wall collisions are checked,
and actor sweeps inspect every actual wall shape. Failure returns no
partial connectivity witness. This adds fresh fixture geometry evidence, not movement simulation,
door operation, support settling, machine/chair use, gas closure, working lights or publication.
The existing spatial-relation staging gate remains. Thirteen loaded room-scene cases and eight
sweep unit cases pass. All 513 focused procgen units and 164 focused integration/client-sandbox
cases pass with no skips on the final source. Fresh Release and Debug builds have zero errors.
Deliberate source regressions prove continuous sweep, reverse filtering and terrain tamper checks,
then restore exact bytes. This verified adapter advances the engine domain from 9 to 13 points;
the overall capability estimate reaches approximately 75%, while full-system exits remain open.

Latest author diagnostics amendment (2026-10-14): `KsProcgenOwnedDiagnosticReportBuilder`
creates detached bounded review snapshots of owned plans, including failed stage/configuration
journals, room-goal cut attempts, retained work totals, fallbacks, requested/selected geometry,
room ownership, entity pack/core/variant provenance, cardinal doors and proposed approaches.
Keep metadata and geometry truncation separate. Clone nested attempt fallback lists. A rejected
or incomplete candidate cannot expose floor/entity artifacts as reusable proposals or retain a
nonzero accepted hash. Mark engine access, gas closure, working light, full metering and complete
global search as unverified; a diagnostic report never promotes a proposal to publication.

`KsProcgenDiagnosticExport` writes version-one canonical JSON with stable property/order rules,
named enum values and bounded output bytes. Byte exhaustion returns no partial data. Exact
diagnostic replay compares supplied bytes with a freshly built canonical report; changed engine
claims, metadata or geometry reject. This is neither a content-library fingerprint nor authorization
to recreate a map. Full request/content/context hashes, validated materialization and broader
replay compatibility remain separate R07 work. `KsProcgenDiagnosticSvg` renders a static schematic
of rooms, walls, windows, door thresholds, entrances, protected/preserved cells and proposed
furniture. Show declared movement classes and facing; clearly label truncated diagnostics and
unverified engine state. Use XML text/attribute escaping and invariant numeric formatting. Bound
cell count, details, extent and output bytes; keep user names out of executable markup.

Register the Debug-admin `ksprocgen_snapshot <grid entity> <stable host key>` console command
with localized descriptions/errors. It reads the existing paused-safe paint snapshot and exports
connected blobs, profile goals/subdivision preferences and complete entrance configuration/group
contracts. It does not generate, remove markers, assume live door/landing context or publish.
Loaded fixture exports are available in `Klovn/Docs/Examples/Procgen/l-shape.json` and `.svg` with
their review guide. Regeneration requires explicit `KS_PROCGEN_ARTIFACT_DIR`; ordinary tests
never write workspace samples. Pipeline mutation, engine staging and report export remain separate
actions so reviewing an artifact cannot accidentally allocate or publish map contents.

Latest surface amendment (2026-10-03): R01 copies optional tracker declarations under capability
hash v2; R02 includes existing tracked items in finite unnamed support reservations; R06 checks
live prerequisites and offers owned, bounded two-entity drop-coordinate previews. These
previews can now be initialized explicitly while paused, with lifecycle/pose/filter revalidation
and rollback (the adapter still handles only one surface/item pair). Full assembly surface poses,
owned contact simulation, mounting and usable fronts remain pending. A separate read-only observer
now checks real contact/tracker records in live physics fixtures; it cannot certify complete placement.
Container preview allocation accounting is implemented
as detailed below; deferred/cleanup callbacks and external side effects remain transaction work.

Latest supported-search amendment (2026-10-04): R02 now has bounded automatic tile-aligned root
and member-rotation search for one selected supported variant, preserving clean operating access
and enforcing declared adjacency, facing-target, corner and clean-path distance geometry with
persisted witnesses. Near routing and operating access share a bounded expansion allowance.
R06's owned preview can consume that internally accepted candidate without repeating its path work.
This advances the earlier surface amendment's disconnected pose/access candidates; joint room/core
search, live operating/collision/mount validation, owned settling and publication remain open.

| Task | State | Implemented or remaining work | Verification |
| --- | --- | --- | --- |
| T01 | In progress | `Content.Shared/_KS14/Procedural/KsProcgenRequest.cs` defines initial serialized request, shape, limit, policy, status, named constant-region mask/transform/source/fingerprint fields, optional soft procedural room-size goals, and four explicit fallback permissions. `KsProcgenGeometry.TryNormalize` reports invalid requests with stable codes and rejects overlapping size bands. Remaining: full result/constraint/relaxation schema, prototype-reference validation, and fixture resources. | Focused Debug request/geometry tests pass; earlier loaded-content fixture passed before unrelated global prototype-loader failures appeared. |
| T02 | In progress | `KsProcgenGeometry` normalizes exact cell/rectangle/text masks with subtraction, void/envelope/preserved ownership, coordinate/cell limits, clockwise quarter turns, cardinal island detection, and transformed constant masks. Remaining: all requested geometry adapters/fixtures and full A01/A16 exit coverage. | Focused Debug geometry tests pass, including named transformed constants, collision/out-of-mask/invalid-transform rejection, and earlier diagonal-island cases. |
| T03 | In progress | `KsProcgenPlan` records exclusive per-cell dispositions and supports checkpoint/rollback; stable FNV-1a hashing and named SplitMix64 streams are in place. `KsProcgenGeometryPipeline` hashes accepted claims, placements, routes, partition seams/doors, theme choices, required pack obligations, exact materials, furnishing/light/window proposals, hull inventory, declared constant identities, roots, inspected-passage/window-boundary facts, the abstract port-network report, and fallback policy flags for semantic replay. Failed plans have hash zero. Remaining: complete placement/package journal state, verified content/context hashes, work counters, serialized replay/report, and full T03 exit tests. | Focused rollback/hash and pinned RNG tests pass; a changed declared constant fingerprint changes the plan hash without changing its mask. Earlier loaded-server pure replay produced the same semantic hash, while a hybrid plan differed. |
| T04 | In progress | `KsProcgenRoomPort` declares a one-cell room boundary opening with a cardinal normal. Layout validation requires a unique port ID, a threshold on the room boundary, and an in-room landing. Candidate discovery transforms its threshold, normal, and approach cells and rejects ordinary entrances aimed outside the target. The residual connector consumes selected port IDs and checks room claim ownership. Remaining: actual map/marker inspection, wide spans, explicitly approved exterior interfaces, door/collision classification, support footprints, connection classes, and engine entity adapters. | Shared Release build, 29 focused unit tests, and a loaded-server port fixture passed. Temporarily bypassing target-side landing validation made its negative case fail, then the guard was restored. |
| T04a | In progress | `KsProcgenThemePrototypes.cs` defines serialized room themes, typed goals, weighted tile/wall/light/entity packs, entity movement roles and footprints, and declarative lighting supply. `KsProcgenThemeValidator` resolves single-parent inheritance and checks fallback cycles, pack references, candidate IDs/weights, tile and entity prototype IDs, ranges, and rotations. `Resources/Prototypes/_KS14/Procedural/office.yml` supplies a steel-office example with a dominant workstation pack and compatible storage support. Remaining: goal-ID merge semantics, full filters and assembly relations, actual prototype capability inspection, compatible fallback substitution, and complete A31 schema cases. | Shared Release build and loaded-server integration test passed; the latter resolves the office theme and rejects a missing theme with a stable code. Fixture operation and placement remain unverified. |
| T05 | In progress | `KsProcgenLayoutGeometry.cs` validates equal family reservations, complete room/generated ownership, nonoverlap, facade edges and ports, and all-or-nothing candidate cell claims. Remaining: nested choices, exclusion scopes, prototype serialization, and complete rollback beyond cell claims. | 29 focused Debug unit tests passed, including L-versus-four exclusivity and malformed-layout cases. |
| T05a | In progress | Candidate discovery aligns every local reservation cell to a target pivot at every allowed quarter turn, checks exact transformed masks, transforms declared ports, and scores matching exterior edges. Remaining: broad area search, corners/near-match penalties, fragmentation and coverage scores, library indexing, and full A38/A42 fixtures. | 29 focused Debug unit tests passed, including shifted placement, facade preference, port rotation, and explicit incomplete-enumeration result. |
| T05b | In progress | A request can name a constant source/fingerprint and an exact local mask, origin, quarter turn, and declared one-cell entryways. Normalization transforms the mask/ports, rejects duplicate IDs/cells, overlaps with other constants or anonymous preserved cells, cells outside the target, invalid transforms, and malformed ports lacking a cardinal boundary threshold/interior landing. Packing reserves constant cells before search as named preserved claims; rollback cannot remove them. Residual routing treats constant ports as obligations, including opposite direct port pairs; an unmatched port facing another constant fails preliminary routing. A seed-independent constant-contract hash records declared source/fingerprint/transform/cells/ports, while plan/pipeline semantic hashes include the identity; successful pipeline results flag unverified constant content. Remaining: inspect and verify actual source content/fingerprint and ports, reserve off-anchor entity footprints, inspect seams, stage/copy unchanged entities, enforce complete constant accessibility/closure, and A21/A40. Declared metadata is not proof of actual authored content. | Focused Debug geometry and plan tests pass for transformed reservation, immutable claim ownership, rollback, fingerprint-sensitive hashing, unchanged contract hash across seeds, constant-only inspected root access, residual port routing, blocked-port rejection, and matched direct ports. Temporarily omitting constant ports from residual routing made the focused route test fail; restoring it returned all 62 focused procgen unit tests to green. Shared Release build and the loaded-server declared constant-port/root case pass with the temporary prototype override; actual constant source copying is still unverified. |
| T06 | In progress | `KsProcgenPackingPlanner` searches complete layout claims across types, origins, rotations, and residual floor, preferring matched exterior edges and a requested prefab coverage percentage. At each complete cover it checks residual routing, rejects blocked-port candidates, and continues backtracking. It distinguishes no geometric cover, no preliminary route, and budget exhaustion; root/passage context is validated and total route expansions are bounded and reported. Pure `SingleNetwork` now rejects disconnected procedural floor even without explicit roots; `PerIsland` permits it. Remaining: early route/hull pruning, required room counts, exclusions, near-match scoring, stronger search strategy, full diagnostics, and publication gating. `GeometryReady` still does not certify a playable map. | Shared Release build and 29 focused Debug unit tests passed, including reachable-option choice, disconnected-root and disconnected-floor rejection, L-versus-four selection, mixed sizes, and 1x3/budget behavior. Temporarily bypassing completed-plan routing made the pure-network test fail; the restored rule passes. |
| T07 | In progress | `KsProcgenTraversal` validates a cardinal clean-passage snapshot: operational one-cell approaches, room-local port and chair/machine approach connectivity, global root reachability, and vault-only blockers. `KsProcgenPortNetworkAnalyzer` also reports abstract connected groups of declared prefab/constant rooms, direct opposite port pairs, procedural/inspected passage components, and roots; rooms without declared ports are listed. An inspected clean cell inside an authored room joins that room to its passage component, allowing a root inside a constant-only room. The pipeline uses partitioned floor cells, rejects disconnected abstract groups under `SingleNetwork`, and exposes the report. This assumes authored ports work inside their rooms and never verifies actual engine access. Remaining: engine actor/collision/door adapter, wide ports, width profiles, port destination classification, and final materialized-state checks. | Five focused network tests pass for opposite direct ports, an isolated constant despite complete packing cover, a constant-to-procedural root path, an inspected constant-only root, and an explicit node budget; all 74 focused procgen unit tests pass. The loaded-server fixture passes with the temporary prototype override, including a declared constant port reaching a procedural root. |
| T08 | In progress | `KsProcgenRoutePlanner` builds a bounded least-cost cardinal tree across explicitly writable cells and reusable clean passages for required terminals. `KsProcgenResidualConnector` derives writable residuals from packing claims, validates prefab and declared constant port ownership, matches opposite adjacent ports, and routes exposed entrances to roots through procedural or inspected passage cells. Packing backtracks when this check fails. Pure `SingleNetwork` requires all procedural floor to form one cardinal component. The pipeline additionally checks the combined room/port/passage network against the final proposed partition floor, validates protected passage and door-opening geometry, and returns `NoPreliminaryRoute` for a disconnected `SingleNetwork`; `PerIsland` can retain multiple groups. It reserves no partial route on failure. Remaining: port classes and width, fixed links, optional loops, approved stubs, engine clearance, and final access validation. `PreliminaryReady` is not a playable-map success status. | Focused Debug constant-port and network tests pass for residual connection, blocked neighbor, direct pairing, isolated declared rooms, and root connection. The loaded-server pure and hybrid fixtures pass with the temporary prototype override. |
| T09 | In progress | `KsProcgenPureFillPlanner` covers each procedural cell with connected zones, protects reserved routes, and treats narrow/tiny components such as 1x3 as passage. Optional nonoverlapping soft size bands request counts of large, medium, and small preliminary rooms in one exact mask; it reports achieved counts and shortfalls without discarding tiny cells. `KsProcgenSizeMixAnalyzer` separately counts final themed room interior areas after partition fallback, excluding passages, and the pipeline exposes final soft shortfalls and hashes both stages. `KsProcgenPartitionPlanner` proposes tile-thick walls and unique door openings with room landings, protected passages, and local/island clean-floor connectivity. Failed interfaces merge their adjacent zones and retry within an explicit merge budget; a budget failure returns open floor without partial walls. `KsProcgenGeometryPipeline` composes packing, material choices, bounded furnishing proposals, and bounded provisional light positions for pure/hybrid requests. `GeometryPlanned` remains a read-only status. A required entity pack that cannot fit returns `ContentUnmet` with no semantic plan hash. Remaining: final port/root access, real wall/door placement and actor/pressure checks, hard room-size feasibility/backtracking, and tile/entity spawning. | Focused Debug tests pass for exact large/medium/small proposal counts, deterministic replay, a tiny 1x3 shortfall with complete coverage, invalid/overlapping bands, and final room-area accounting that excludes passages. Earlier seam/pure-fill tests remain green; 74 focused procgen unit tests and the loaded-server fixture pass with the temporary prototype override. |
| T09a | In progress | `KsProcgenThemeSelector` chooses deterministic room-scoped palette, wall family, light fixture, and coherent dominant/support packs. `KsProcgenThemeAssignmentPlanner` maps final open/merged zones to one choice per room, strips optional entity packs from passages, rejects mandatory packs there, records unplaced mandatory pack minima in rooms, and owns every partition wall by one region. `KsProcgenMaterialPlanner` resolves exact floor, interior-wall, and required door prototype IDs, assigns a stable rounded accent fraction only inside rooms, protects door thresholds and tiny passages with primary tiles, and rejects a selected wall family without a required door. Remaining: request overrides, hull/shared-seam material compatibility, actual placement, mandatory fixture fulfillment, and operational validation. | Loaded-server integration verifies office workstation/storage grouping, tiny passage without furnishing, exact tile/wall/door choices, a primary door threshold, accent replay, mandatory-pack diagnostics, and missing-door rejection. Selected lights are content choices, not verified working coverage. |
| T10 | In progress | `KsProcgenHullBoundaryPlanner` inventories legacy generated floor; the entrance-aware `KsProcgenGroupHullBoundaryAnalyzer` independently revalidates owned floor, includes preserved/constant floor, reserves complete entrance/host spans and retains unknown boundary kinds. `KsProcgenGroupHullWallPlanner` selects compatible registered hull-wall prototypes only for explicit writable envelope claims. Missing shell cells remain unresolved. `KsProcgenGeometryPipeline` exposes the legacy inventory as a diagnostic for layouts without prefabs; a pure 1x3 footprint reports eight unresolved sides. `KsProcgenGasClosure` separately checks caller-declared cardinal gas edges in one nominated door state; missing edges remain unknown and complete closed snapshots are only `PreliminarilyClosed`. None of these stages inspects live gas behavior. Remaining: integrate owned hull/window choices into the complete pipeline, include copied prefab geometry, obtain actual gas edges/door states and host context, run materialized gas flood, and A13-A14. | Historical thin-envelope/static gas tests and the loaded legacy pipeline fixture pass. Owned boundary and candidate wall verification is recorded under R05; gas and engine verification flags remain false. |
| T11 | In progress | `KsProcgenWindowPlanner` provides immutable stable eligible/fixed/editable counts, per-cell exclusions, fraction rounding, soft clamping and hard count/tolerance failures. The legacy pipeline accepts optional caller-inspected boundary facts and hashes provisional choices. `KsProcgenGroupWindowBoundaryAnalyzer` independently revalidates owned hull geometry and directional exterior context, includes fixed preserved/constant walls and exposes unclassified cells. `KsProcgenGroupWindowPlanner` resolves incident theme window prototypes and replaces editable wall choices exactly once; entrances, read-only content and unknown shell stay protected. Missing prototype support excludes cells and hard misses discard all earlier artifacts. Neither geometry nor caller-declared airtight support proves live eligibility; verification flags remain false. Remaining: actual exterior/prototype inspection, complete pipeline and room/segment scopes, multi-cell modules, live airtight substitution, movement/pressure rechecks and A15. | Historical fixed-count/exclusion/legacy-pipeline tests pass. New owned face/substitution, fixed ownership, context, shared budget and replay evidence is recorded under R05. |
| T12 | In progress | `KsProcgenTileStageSystem` provides the first tile-only materialization slice. It preflights a successful, hashed pure plan for exact procedural floor coverage, known tile IDs, and a tile budget; it rejects prefab/constant claims, interior walls and doors, furnishings, lights, and selected windows. Accepted tiles are placed on a new uninitialized, paused map and grid, then can be read back and discarded through an owned stage handle. Bulk cancellation discards all owned stages; round restart and system shutdown invoke it. Map identity is checked before deleting a stage map. This does not establish map visibility isolation or publishability. Remaining: safe prefab and constant copying with authored entity references, entity/material placement, startup side-effect isolation, engine traversal/gas verification, and A16-A17/A20/A24/A40. | The loaded-server fixture stages and verifies a 1x3 floor, detects a deliberately changed tile, discards one stage, bulk-cancels two more with map removal, handles an externally removed map, rejects a two-tile budget and hybrid content, and leaves the plan unpublished. All 78 focused Debug procgen unit tests, the loaded-server fixture, and Server Release build pass with the temporary prototype override. |
| T13 | Pending | Final engine validation and publication. | Pending. |
| T14 | In progress | `KsProcgenFallbackPolicy` names four default-permitted planning degradations: merged/open partitions, final room-size shortfalls, sparse optional furnishing, and sparse provisional lighting. Disabling one makes the pipeline return `FallbackDisallowed` with a stable reason and zero semantic hash; immutable coverage and access requirements remain enforced. Policy flags enter the semantic plan hash. `KsProcgenPlanningReportBuilder` produces bounded, deterministic constraint outcomes and a trace of applied fallbacks from a pipeline result, distinguishing proposed/degraded/rejected/no-op planning from publication; window count and fraction are separate hard/soft outcomes, while operational access, gas closure, constant source content, and working light remain unverified. Remaining: request-wide retry budgets, alternative/backtracking fallback order, hard relaxation permissions, per-port and per-cell conflict details, verified content/context hashes, serialized reports, and final publication statuses. | 78 focused Debug procgen unit tests pass, including report degradation, separate hard window count and soft fraction, unverified engine conditions, invalid-request reporting, and truncation. The loaded-server fixture passes for all four policy gates, a policy-sensitive semantic hash, and a degraded unpublished report using the temporary prototype override. |
| T15a | In progress | `KsProcgenLightingPlanner` resolves the selected theme fixture and proposes bounded, deterministic positions on free room floor, avoiding protected passages, furniture, and reserved interaction approaches. It greedily estimates cardinal floor-graph coverage from the fixture's preferred spacing; tiny passages or oversized rooms can report sparse fill, and fixture-budget exhaustion has an explicit status. The pipeline tracks a request-wide fixture count and hashes the proposal. `WorkingCoverageVerified` is always false: the estimate does not account for actual light falloff, occlusion, fixture power/startup, or supply validity. Remaining: prototype placement capability, wall/ceiling mounting, live supply checks, actual light sampling after furnishing, hard coverage gate, and A29-A30. | Loaded-server office test verifies deterministic positions, passage/furniture avoidance, estimated target, sparse oversized fallback, capped-budget status, and unverified working state. A 1x3 passage reports sparse light placement. 46 focused unit tests and Shared Release build passed. |
| T15 | In progress | `KsProcgenInteractionFacingPlanner` supplies one-cell and declared multi-cell machine facing plus chair-approach geometry using the documented 0/90/180/270 convention and multi-source clean paths. Machine approaches are chosen from the rotated footprint's exposed front edge. `KsProcgenFurnishingPlanner` proposes atomic, room-local instances of selected plain-list entity packs. The selected theme carries its validated furnishing-density goal; larger rooms can repeat the dominant pack up to eight separated clusters with explicit cluster indices and a soft density-shortfall omission. Declared footprints of up to sixteen cells are validated, rotated in quarter turns, placed entirely on free floor, and recorded with value-comparable occupied cells. Density targets count occupied footprint cells. The planner reserves door and interaction routes against every footprint cell; vault-required footprints block clean passage, and lighting avoids their full extent. Seats remain single-cell until their approach geometry is generalized. The request-wide candidate budget remains bounded; running out during optional density repeats reports a sparse omission. Remaining: authored assembly relations/variants, actual prototype collision/seat/rotation checks, more nuanced density/distribution, engine placement, and full A27-A29/A32-A36 checks. | The loaded-server office fixture covers deterministic repeated workstation clusters, density contrast, distinct cells, final clean approach routes, a bounded density-budget shortfall, desk/chair/computer/storage grouping, doorway protection, sparse tiny optional fill, required-pack failure, candidate budget, and oversized-room fallback. Loaded fixtures verify a two-cell vault-required table stays on free floor, avoids reserved passages and peer footprints, excludes lights from both occupied tiles, replays identically, and rotates into a 1x3 room; a two-cell interactive console obtains a reachable front-edge approach. Focused multi-cell facing tests cover an associated chair and a forced rotation. The focused loaded-server test and 99 focused Debug procgen unit tests pass with the temporary prototype override. |
| T16 | Pending | Preview, validation, replay, and overlays. | Pending. |
| T17 | In progress | `KsProcgenTacticalAnalyzer` computes exact cardinal movement articulation cells and bridge edges on the clean walk graph, with bounded choke-side cell/required-terminal counts. It excludes vault-only cells and reports detail truncation separately from complete graph detection. `KsProcgenExposureAnalyzer` counts visible sampled peers within a radius and reports per-cell fractions and longest clear rays through a caller-supplied vision mask. `KsProcgenCoverAnalyzer` tests nominated threat origins against sampled positions and legal cardinal neighbors through an independent projectile mask. Both use a conservative tile-center supercover ray that inspects both incident cells at corner ties; pair/ray budgets fail without partial metrics. `KsProcgenAlternateRouteAnalyzer` checks named terminal pairs against each nominated unavailable choke cell, reporting endpoint removal, retained connectivity, and shortest remaining distance. It excludes vault-only movement, rejects already disconnected pairs, and caps searches/expanded cells without returning partial outcomes. `KsProcgenRouteRedundancyAnalyzer` counts internally cell-disjoint clean routes with bounded node-split flow; a count below the configured cap is exact, while a capped count is labeled as a lower bound. `KsProcgenFiringPositionAnalyzer` nominates clean reachable cells protected from one threat origin with a clear projectile ray to a distinct target, bounded by candidate, flood, and ray limits. None of these geometric results claims engine traversal, vision, or projectile verification. Remaining: engine-derived masks and validation, selected actor/stance/projectile models, narrow clearance runs, cut witnesses, and A25 engine fixtures. | Eight focused Debug vision/cover tests, four alternate-route tests, three firing-position tests, and four route-redundancy tests pass for clear and blocked rays, separate masks, nearby cover, loop and corridor detours, vault-only blockers, reachable protected firing positions, one/two/three independent routes, bounded lower bounds, empty range, work budgets, and extreme coordinates. All 97 focused Debug procgen unit tests and Shared Release build pass. |
| T18 | Pending | Bounded tactical furnishing preferences. | Pending. |
| T19 | Pending | End-to-end examples and release validation. | Pending. |

### Rough implementation assessment (2026-10-10)

The full specified system is approximately **55% implemented**, with an uncertainty range of
**40-60%**. This is an engineering estimate weighted by the remaining work and acceptance gates,
not the fraction of files written, tests passing or task rows started. None of the major T/R rows
has its complete revised exit criteria signed off; a partially working planner is useful progress
without making its materialization and validation requirements complete.

| Area | Approximate implementation | Current practical boundary |
| --- | --- | --- |
| Planning and geometric solvers | 65-70% | Exact masks, bounded packing/routing/partitioning, themes, floor assemblies, facing, windows, lighting proposals and tactical measurements work in the tested model. |
| Engine materialization and final validation | 20-25% | Tile previews and owned support/container assembly previews work, with insertion, initialization, filters, cancellation, allocation limits and cleanup fixtures. Spatial relations still reject before live allocation; full furnished rooms are not validated or published. |
| Author tools and release workflow | 15-20% | Area/entrance profiles and markers, explicit grid-binding API, exact blob and entrance ownership, complete configuration validation and shorthand import exist. Editor UI, replay/debug overlays and the final end-to-end example/release matrix remain unfinished. |

The largest remaining blocks are R03-R05 complete marker authoring and configurable entrance networks,
R02 joint core/lighting/landing search and engine operating validation, actual prefab/constant copying,
complete entity/wall/door/light/window materialization, actor access and pressure/power validation,
private staging/publication, and T16/T19 tooling and release evidence. These estimates should be
revisited after those milestones; they do not promise that the current output is a playable map.

Unchecked tasks are future work. Each task requires its own reviewable change, updated schema/docs,
and relevant tests. A task is done only when its listed exit criteria hold. Do not mark later phases
done because an earlier placeholder returns plausible-looking rooms.

Earlier focused verification: 78 Debug procgen unit tests, the Shared Release build, and one
loaded-server procgen integration test pass after constant-port, size-mix, window-diagnostic,
request-window-goal, abstract port-network,
partition-aware access, inspected window-boundary, and explicit fallback/report changes. The
loaded test covers provisional window selection, a hard window miss, four denied fallback cases,
a policy-sensitive hash, and a declared constant room whose port joins procedural floor and a
root. It does not inspect the constant's real source or
materialize any generated map.

The composed pipeline now checks its abstract room/port network against the partition's final
proposed floor cells. Planned wall cells cannot bridge that network; protected residual passage
cells and each proposed door's threshold and approaches are checked for cardinal floor access.
An invalid partitioned access snapshot fails planning, while multiple network groups fail
`SingleNetwork`. This is still a geometry check, not operational door or actor clearance proof.

All four `_KsModule*` submodules were fetched to `origin/main` on 2026-09-28. The prototypes
module's `HEAD` still equals `origin/main`, but its `Entities/Mobs/Operative/base.yml` worktree
is now intentionally modified: the two undefined `NpcSquadMember`/`NpcReactionTime` component
blocks in each operative prototype are commented out so the loaded-server test can start. This
temporary local override must be removed after the corresponding code definitions arrive.

### Requested 75% milestone accounting (2026-10-14)

The overall percentage is a coarse capability estimate, not the fraction of checked boxes in this
historical ledger. Helpers count only within their tested scope; no unverified engine claim receives
completion credit. The current 75-point assessment uses the following weighting so later changes
can be reviewed against concrete capabilities rather than inflated by adding tiny checklist items.

| Capability domain | Weight | Implemented points | Evidence and remaining limits |
| --- | ---: | ---: | --- |
| Contracts, prototypes and authoring | 20 | 18 | Relational compilation, capability inspection, painted blobs and entrance contracts; remaining library/context inspection and schema exits. |
| Layout, packing, routing and room partitioning | 25 | 17 | Owned routing, atomic network separation, quota/width/direction retries and bounded irregular strips; general holes, wider layouts and joint prefab/hybrid fitting remain. |
| Interiors, materials, packs, lighting and tactics | 20 | 16 | Coherent material themes, floor furnishings, joint support-aware core/variant search and geometric tactical metrics; primary layered integration and live lighting remain. |
| Engine staging, validation and publication | 20 | 13 | Owned grid-backed room/support previews, real insertion, initializer/callback spawn checks, terrain/frame verification, fresh member/wall collision and continuous fixed-actor cardinal clearance; primary pipeline integration, contact stability, operational use, gas/lighting and publication remain. |
| Diagnostics, replay, author tools and release evidence | 15 | 11 | Detached versioned JSON, exact fresh-report comparison, escaped SVG review examples and localized painted-grid inspection join stable identities and bounded journals; full work metering, engine replay and complete release matrix remain. |
| **Total** | **100** | **75** | Rough estimate; uncertainty 45-80%. |

The user's approximately 75% milestone is reached after the diagnostic and owned scene exits.
The engine gain covers combining previously separate previews on real terrain and inspecting
fresh initialized geometry with continuous actor clearance; it does not award gas, operational
lighting, movement simulation, prefab/constant copying or publication credit. R01-R07 and the
full system remain incomplete. Next work should integrate supported cores/terrain into the primary
owned pipeline, complete joint hybrid fitting, then establish operational and publication gates.

### Required rewrite checklist for the 2026-09-30 contract

These are concrete implementation tasks, not optional follow-up documentation. R01 and R03 may
start independently. Implement R02 after R01; R04 after R03; R05 after R04; R06 after R02/R05 and
its existing staging/engine prerequisites. Extend R07 throughout and complete it last. Prototype
data references are declarations; unknown or unsupported new fields must fail explicitly until
the responsible stage exists. Never ignore them and run the old behavior successfully.

- [ ] **R01 - Define reusable relational prototypes and validate constraints.** Revise
  `KsProcgenThemePrototypes.cs`, `KsProcgenThemeValidator.cs`, and resolved theme/pack contracts.
  Add named members/bindings, bounded multiplicities, explicit relation IDs/targets, severity,
  complete variants, support/container dependencies and contextual corner/open-face predicates.
  Separate dependency-cycle detection from the spatial constraint graph. Compile legacy plain
  entries into singleton cores while preserving their required/optional provenance. Exit: A45,
  A31 and schema parts of A43-A44 pass; no missing entity, ambiguous target or unsupported relation
  can silently degrade to scatter. Migrate the existing office workstation example to an explicit
  desk/seat/console assembly so its intended atomic core survives the plain-list semantics change.
  - [x] Add prototype/data schemas for members, relations, bindings, rotation mode, counts and variants.
  - [x] Compile bounded repeated members and explicit targets; distinguish support dependencies from
    spatial cycles; reject missing bindings/entities, ambiguous targets, optional mandatory dependencies,
    conflicting support parents, malformed counts/rotations and oversized expansion.
  - [x] Resolve entity packs to named cores, including copied legacy singleton entries and core minima.
  - [x] Load actual relational/variant prototypes in the server fixture. R02 now consumes supported
    floor variants and plain-entry core minima. Variants requiring unsupported placement capabilities are
    skipped whole; a supported complete alternative may replace them. If no variant has supported
    placement semantics, return `AssemblyPlacementUnsupported` without proposals.
  - [x] Migrate the live office workstation pack to its explicit desk/seat/console assembly;
    retain meaningful existing workstation access tests.
  - [x] Inspect inherited entity declarations through `KsProcgenPrototypeCapabilityInspector` and
    inspect normalized assembly support relations through `TryInspectAssembly`. Copy surface enablement,
    centering and offset, item presence, initial anchoring, rotation verb/increment, hard fixture masks,
    declared containers and item-slot filters into detached read-only records. Sort member IDs, relation
    IDs, fixture/container IDs and filter sets for stable inspection. Bound assemblies to 64 members/
    256 relations and prototype output to 64 fixtures/64 containers; unknown entities and malformed
    support references return stable issues without partial reports. Cache each bound prototype within
    an assembly inspection without retaining mutable prototype components in the result.
  - [x] Add support preflight records distinguishing `Rejected` from `Unverified`. Missing/disabled
    surfaces, missing named containers, locked/reserved item slots and item-slot/container type conflicts
    reject the initial declaration. Real `ContainerSlot` declarations imply capacity one; generic
    containers have unknown capacity. Preserve whitelist/blacklist components, tags, sizes and
    `RequireAll` without pretending to evaluate runtime insertion events or operational access.
  - [x] Check hard collision candidates using the engine's pair-filter rule: either fixture mask matching
    the other fixture's layer is sufficient. Soft fixtures do not become hard overlap candidates.
    A candidate is a warning for engine validation, not proof of geometric collision: fixture shapes,
    transforms and runtime collision state are not part of this snapshot. `PlaceableSurface` supports
    drop placement and exposes neither named surface slots nor capacity. Named surface slots remain
    unverified; do not invent support slots or infer capacity from the presence of this component.
  - [x] Hash the inspected declaration subset under `ks-procgen-prototype-capabilities-v2` for later
    capability witnesses. This is not a full content hash and is not yet an input to the geometry plan;
    that plan remains version 20. Every snapshot has `EnginePlacementVerified == false`.
  - [x] Copy optional `ItemPlacer` tracking declarations separately from `PlaceableSurface`:
    raw unsigned maximum, initial tracked count and detached whitelist rules. Zero means unlimited
    tracking, not zero capacity; maxima outside the signed planner range remain unknown there.
    Only an unnamed surface claim may use a finite declared tracker capacity. An initially full
    tracker rejects support inspection; named surface slots remain unverified with unknown capacity.
    Capability hash v2 includes tracker presence, maximum, occupancy and normalized filter rules.
    Tracking limits do not prove a drop is mounted, colliding correctly or usable. A plain table
    without a tracker still has unknown capacity. Geometry plan identity remains v20.
  - [x] Reject inapplicable relation fields during schema compilation with
    `InapplicableAssemblyRelationField` and no resolved output. `slot` is exclusive to `OnSurface`;
    `containerId` is exclusive to `InContainer`. Supplied identifiers must be nonblank and at most
    256 characters. Nondefault approach policies are accepted only for `FacingOpenSpace` and
    `UsesSeat` (the latter already requires its associated seat). Distance bounds are configurable
    only for `Near`; other kinds retain schema defaults, with explicit `1..1` also accepted for
    `AdjacentTo`, whose geometry is always cardinal adjacency. Do not imply that arbitrary distance,
    slot or container fields modify a relation whose evaluator never consumes them.
  - [x] Canonicalize compiled member footprint sets by Y/X and allowed quarter-turn sets numerically,
    including legacy singleton cores. Preserve repeated-member IDs, required/optional provenance,
    ordinal member/relation order, complete alternatives and authored variant priority. Compilation
    copies mutable source lists; subsequent source edits do not alter the compiled declaration.
    This strengthens declaration replay without claiming full content identity, serialization,
    engine placement or completion of R01/R07. Geometry plan identity stays v20.
  - Schema replay fixtures (2026-10-03): equivalent member/relation/footprint/rotation orderings
    compile identically, and subsequent edits to source lists/relations leave prior compilation
    intact. Nine negative cases cover misplaced support/container fields, blank/oversized identifiers,
    ignored approach policies and unsupported distance overrides. The loaded-server
    `LoadedRelationalDeclarationsRejectIgnoredFieldsWithoutChangingPrototypeData` test rejects a
    spatial relation carrying a container field, then resolves the unchanged real pack again and
    verifies its original base core and complete standing alternative survive.
  - [x] Bound authored local member/relation/variant/core/binding names to 128 ASCII letters,
    digits or underscores, prototype/assembly IDs to 256 characters, and each concrete binding
    dictionary to 64 entries. Reject oversized binding dictionaries before prototype lookups.
    Generated member references allow the additional `/0` through `/63` instance suffix; the
    128-character source-name boundary therefore remains usable for repeated members and explicit
    instance targets. Direct `TryResolve` calls validate reference core IDs as strictly as pack
    resolution. These limits complement the existing 64-member/256-relation expansion caps.
  - Identifier fixtures (2026-10-03): six unit cases cover oversized member/relation/variant/binding
    names, a lookup callback that must not run for oversized binding input, and an explicit repeated
    instance target at the maximum source-name length. Loaded content verifies direct reference
    resolution rejects oversized core IDs and binding dictionaries with no returned variants and
    preserves the original valid prototype/pack. Compiler limits are input-contract bounds, not
    engine placement or a complete content-hash/replay serialization guarantee.
  - Schema/identifier verification (2026-10-03): all 186 focused Debug procedural unit tests,
    all fourteen loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Temporarily bypassing inapplicable-field
    rejection makes all nine negative cases fail (`Expected: False`, `But was: True`); source was
    restored in `finally`. After the sequential Release build, a Debug `--no-incremental` rebuild
    restored consistent shared-output assemblies; the complete 186-unit/fourteen-loaded corpus
    passed on that final set. Scoped diff and new-file whitespace checks pass. Unsuppressed CI
    warnings-as-errors remains unverified. R01/R02/R07 stay open for the remaining exit fixtures,
    placement adapters, full identity/reporting and actual support/operational verification.
  - [x] Route prototype reference resolution through a bounded, atomic `TryCompileVariants` family
    compiler. Permit the base plus sixteen complete alternatives (17 total); retain authored variant
    priority rather than sorting it by ID. Preflight all variant list sizes, local variant IDs,
    binding dictionaries and declared binding-name bounds before discovering the union of bindings
    or inspecting any entity. Reject duplicate variant IDs and binding keys unused by the entire
    family. A binding used only by a later alternative is valid for the reference; it does not need
    an invented matching member in the base. Every alternative has its own members, relations and
    anchor, with no inheritance/merge from the base.
  - [x] Fail a malformed family atomically with an empty result, even if earlier variants compiled.
    Do not treat an unknown entity, missing binding or invalid alternative as placement infeasibility
    and silently skip it. Schema failure differs from a well-formed variant requiring an unsupported
    placement layer, which the floor placement stage may replace with a supported complete alternative.
  - Variant-family fixtures (2026-10-03): nine unit cases cover whole-core replacement, nonalphabetic
    authored priority, an alternative-only binding, source-list detachment, unknown later entities,
    missing later bindings, duplicate variant IDs, unused binding keys and family-wide preflight of
    oversized variant/member/relation lists or declared binding names without invoking prototype
    inspection. The loaded-server schema fixture rejects a valid real base paired with a malformed
    standing alternative, returns no partial family, spawns no entities and resolves the unchanged
    original pack afterward. These are declaration/compiler checks, not full engine placement.
  - Variant-family verification (2026-10-03): all 195 focused Debug procedural unit tests,
    all fourteen loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Temporarily continuing after a failed
    alternative instead of failing its family makes the unknown-entity and missing-binding cases
    incorrectly return success; both negative tests fail (`Expected: False`, `But was: True`).
    Source was restored in `finally`. After the sequential Release build, a Debug `--no-incremental`
    rebuild restored consistent shared-output assemblies; the complete 195-unit/fourteen-loaded
    corpus passed on that final set. Scoped diff and new-file whitespace checks pass. Unsuppressed
    CI warnings-as-errors remains unverified. Successful geometry plan identity stays v20;
    malformed declarations never receive a valid plan. R01/R02/R07 remain open for their remaining
    exit fixtures, placement adapters and full identity/reporting requirements.
  - [ ] Finish remaining schema/replay exit fixtures and connect inspection reports to R02/R06 placement adapters
    and R07 reporting. Validate actual overlap, mounting, allowed orientation, operational fronts,
    container insertion and capacity in the engine before accepting supported layer placements.
    Floor proposal search still skips whole variants containing `OnSurface`/`InContainer`; declaration
    inspection alone must not enable them or mark R01/R02 complete.
  - Verification: 112 focused Debug procgen tests, Shared Release compilation, and the loaded-server
    procgen fixture pass. The new tests cover true spatial cycles, repeated/optional members,
    ambiguous targets, missing entities/bindings, mandatory-to-optional dependencies, support
    cycles/conflicts, malformed anchors/container declarations and bounded expansion. The loaded
    fixture verifies actual prototype deserialization, complete variant replacement and explicit
    unsupported-placement rejection. Temporarily bypassing support-cycle rejection made its test fail; the
    validator was restored and the full focused suite passed. These checks establish schema and
    compiler behavior only, not supported surface placement or completed R01/R02.
  - Capability inspection fixtures (2026-10-02): six unit tests cover one-direction hard-mask
    matches, soft fixtures, missing/disabled surfaces, unresolved named surface slots, known slot
    capacity versus unknown generic capacity, missing/locked/reserved/type-conflicting containers,
    order-independent declaration hashing, relevant metadata changes, and normalization of filter
    sets without conflating whitelist and blacklist rules. The loaded-server
    `PrototypeCapabilityInspectionRetainsEngineLimits` test reads inherited `Table`,
    `KitchenMicrowave` and `ComputerShuttle` declarations. It verifies that the microwave/table
    masks do not flag a hard collision candidate, while table/table masks flag potential
    hard collision; neither result proves actual supported placement. The shuttle disk slot declares
    one place and a component whitelist for `ShuttleDestinationCoordinates`, not a tag filter;
    the microwave's generic container has no inferred capacity. Inspection of both real compiled
    relational variants retains all four base members/one support witness and the independent
    one-member standing alternative. Repeated prototype inspection retains the declaration hash,
    and unknown prototypes return `UnknownCapabilityPrototype`. No entities are spawned or inserted.
  - Capability inspection verification (2026-10-02): all 162 focused Debug procedural unit tests,
    all four loaded-server Debug tests and Shared Release compilation pass using the existing
    `--no-restore -p:WarningLevel=0` workaround. The first loaded assertion incorrectly expected a
    shuttle disk tag whitelist; an isolated detailed run exposed the actual component whitelist,
    the assertion was corrected, and the entire loaded fixture passed without skipped tests.
    Temporarily changing the collision pair rule from OR to AND makes the one-direction unit
    case fail (`True` expected, `False` returned). The source was restored in `finally` and the
    complete focused suite rebuilt and passed. Scoped diff and new-file whitespace checks pass.
    Unsuppressed CI warnings-as-errors remains unverified. This completes declaration inspection,
    not R01's full exit coverage, R02's support layers, R06's live collision/insertion validation,
    working lighting or publication. Those rewrite gates remain open.
- [ ] **R02 - Rewrite furnishing placement around explicit assembly cores and layers.** Replace
  `KsProcgenFurnishingPlanner.TryPack`'s role-ordered whole-list atomic assumptions, nearest-primary
  seat guesses, and blanket `occupied` exclusion. Extend `KsProcgenEntityProposal` with stable
  core/member IDs, support/container parent and slot, independent permitted orientations, and
  resolved relation witnesses. Extend `KsProcgenInteractionFacingPlanner` to use the supported
  pose, interaction reach and explicit seat association, including nontraversible tabletop tiles.
  Resolve mandatory cores atomically and omit optional singleton cores independently. Preserve
  all accepted approach paths through rollback and later additions. Update density accounting
  to count the union of legally layered footprint cells, and `KsProcgenLightingPlanner` to inspect
  actual mounting/support layers rather than prohibiting all shared XY positions. Exit: A43-A45
  plan cases and existing workstation/rotation/clean-access tests pass; engine claims await R06.
  - [x] Replace whole-pack `TryPack` with normalized core/variant placement. Plain entries are
    independent singleton cores; required minima run before optional cores. A missing mandatory
    copy fails the speculative room with no entities; an optional singleton miss records
    `CoreCannotFit/<coreId>` without discarding unrelated accepted cores.
    Passage and room-size budget gates also account for mandatory core minima even when the
    selected theme has no separate pack minimum.
  - [x] Preserve whole-variant selection and stable core/member/assembly/variant identities. The
    standing fixture replaces the unsupported tabletop base whole; no table/chair members survive.
    Initial core-identity hashing used `ks-procgen-geometry-plan-v14`; relation witness hashing
    below advances that domain to `ks-procgen-geometry-plan-v15`.
  - [x] Implement required floor `AdjacentTo`, `FacingTarget`, `UsesSeat` and `FacingOpenSpace`
    checks. Use cardinal footprint geometry, permitted independent/assembly-relative quarter turns,
    explicit seat association and front-edge machine approaches. Remove nearest-primary and
    unique-chair guesses. Other occupied tiles cannot be chosen as machine interaction approaches.
    A seat still needs a clean cardinal route; vault-required and normally blocking entities remain
    excluded from clean traversal. Density counts actual accepted footprint unions per core copy.
  - [x] Add a separate `KsProcgenAssemblyRelationEvaluator` for supported required/preferred floor
    rules and `AtCorner`. A corner needs two inspected/planned perpendicular backing walls beside
    the same footprint corner; opposite walls, backing faces from different footprint corners,
    self-occupied tiles, inferred concave outlines and other furniture do not qualify as that corner.
    Preferred misses remain legal and report `Missed`; required misses reject the candidate.
  - [x] Score complete legal candidates by satisfied applicable preferences. Try corner anchors
    before center anchors, compare assembly-relative rotations when preferences remain unmet,
    and retain the best legal candidate if optimization is cut short. After finding a valid
    candidate, allow at most 64 further placement probes per rotation within the shared 4096-probe
    maximum. A retained candidate reports `PreferenceSearchTruncated` and a sparse result; no
    candidate before budget exhaustion still reports the existing hard budget failure.
  - [x] Persist selected relation witnesses with pack/core/assembly/variant/copy identity, subject
    and target IDs/cells, severity, geometric state, omission/miss reason, explicit corner backing
    coordinates and interaction approach. Omitted optional subjects are `NotApplicable` and earn
    no preference score; omitted preferred targets report a miss. Unsupported relations remain
    `Unverified` and invalidate the entire variant even when preferred. Preview reports expose
    geometric outcomes and optimization truncation; they do not verify engine interaction.
    Semantic hash v15 includes these witnesses and truncation in canonical order.
  - [x] Add declared, bounded `approachLanding` offsets to member schemas and normalized records;
    reject noncardinal or self-blocked landings and rotate them with member poses using checked
    coordinate arithmetic. Proposal landings must reach the clean room root. Avoid blocking them
    within their own core and reserve landing routes before placing further cores.
  - [x] Evaluate `Near` using selected approaches, declared landings or clear origins. Search only
    cardinal clean cells, including explicit walls and all blocking/vault-required trial footprints.
    Record the shortest discovered path and exact step count, not a Euclidean distance or invented
    detour to satisfy a minimum. Searching through `maximumDistance` without finding a path means
    no qualifying path within that radius; it does not claim global disconnection or an exact
    larger distance. Missing/blocked landings have explicit reasons. Required misses reject the
    candidate; preferred misses remain scored soft failures. Missing walk geometry for a present
    subject/target pair is unverified
    and invalidates the candidate rather than silently satisfying a preferred rule.
  - [x] Share a 4096 expanded-node `KsProcgenRelationPathBudget` across all Near evaluations and
    candidate branches of a room. A depleted search reports unverified `NearPathBudget`, never
    a false unreachable result. Retain an earlier valid candidate when possible and report path
    truncation; otherwise fail with `FurnishingRelationPathBudget`. A shared landing at zero distance
    needs no node expansion. Value-comparable paths preserve replay equality. Protect accepted
    witness paths from subsequent blockers so later support packs cannot invalidate the result.
    Preview outcomes expose requested ranges and exact discovered distances; semantic hash v16
    includes the paths, ranges, declared landings and path-search truncation.
  - [x] Replace greedy supported-floor member placement with bounded constraint/pose backtracking
    in `KsProcgenFloorAssemblySearch`. Revisit earlier positions, independent permitted rotations,
    multi-cell machine front-edge approaches and selected chair approaches when later members or
    required relations fail. Optional members have an explicit omission branch even after a legal
    footprint later makes the whole core invalid. Required spatial relations prune available pairs
    in either placement order; full branch validation checks clean root access and selected approaches
    against all accepted trial blockers before evaluating Near and preferred witnesses.
    Branch-local occupancy and tentative proposals are discarded on every return, including budget
    stops. Only complete legal candidates survive; incomplete cores never escape rollback.
  - [x] Bound the supported-floor search to 64 normalized members, 65,536 floor cells and the shared
    room maximum of 4096 placement probes. Every attempted cell/turn consumes a probe, and extra
    interaction-approach alternatives consume additional probes. Member positions cover the entire
    floor rather than the former four-tile member radius. Existing anchor spacing and supporting-core
    anchor proximity rules remain selection restrictions. Prune equivalent rotations only when no
    directional relation, authored landing or machine facing distinguishes them. Preserve machine
    wall-backing, open-floor and explicit-seat ranking while retaining alternative orientations.
    Preference lookahead remains at most 64 further probes per assembly rotation after the first
    legal candidate, within the shared room limit. Near's room-wide node budget remains independent;
    truncation can retain an earlier complete candidate and cannot promote an unverified path to
    success. Semantic hash v17 prevents changed solver choices sharing the previous replay domain.
  - [x] Apply normalized core weights to optional initial placement and density repetition using
    `KsProcgenCoreSelector`. Give each enabled core an independent seeded exponential ordering key
    derived from region, pack, draw and core identity. Reordering the prototype list cannot change
    that order; adding a zero-weight core cannot perturb active choices. Zero weight disables the
    candidate, and negative/nonfinite weights, duplicate IDs and pools over 64 cores are rejected.
    Positive finite float extremes remain valid without summing overflowing weights. Within each
    pack, all authored mandatory minima retain their stable ID order and precede optional choices.
    Initial optional candidates are tried once each in weighted order, so independent low-weight
    content can still appear when space permits. Density draws use a new order without replacement
    for each cluster and try remaining cores when an earlier whole core cannot fit. The room's
    shared placement/path budgets cap all retries; unsupported variants retain the existing rejection
    policy. This adds weighted priority and density choices, not new per-core count maxima or
    room-wide optimal selection. Semantic hash v18 distinguishes the new selection algorithm.
  - [x] Schedule mandatory content across all selected packs before optional furnishing. First place
    every explicit core minimum in selected-pack order and stable core-ID order. Next, for each
    required pack not already satisfied, try enabled whole cores in seeded weighted order until one
    fits. Only then attempt optional initial cores and density repetition. A core chosen for a pack
    minimum counts as its initial copy and is not duplicated during the optional pass. Per-pack copy
    indices survive the phase changes; only actual dominant placements establish dominant anchors
    and density counts. Required placement does not use the optional dominant-proximity restriction.
    Retain mandatory support if optional dominant furniture cannot fit; skip only remaining optional
    support when no dominant core exists. Preserve complete relation witnesses and reserve accepted
    approach/Near paths before subsequent placements in every phase. Hard minimum failure or budget
    exhaustion returns no partial room entities or witnesses. All phases share the existing room
    placement and relation-path budgets. Semantic hash v19 distinguishes these scheduling semantics.
    Required pack counts greater than one still return the existing unsupported-count failure;
    explicit core counts remain schema-supported. Joint mandatory geometry is searched by the
    following rewrite item within its explicit nesting and work budgets.
  - [x] Search the supported-floor mandatory tasks together. An explicit core minimum contributes
    fixed-core copies; a required pack without explicit core minima contributes a weighted core-choice
    task. Feed each complete legal candidate into the remaining tasks through an assembly continuation.
    A later geometric failure resumes the earlier core's member poses, anchors, permitted rotations,
    complete variants and then eligible core choices. Never combine variant members or discard a
    mandatory copy to make the remainder fit. Preserve the established first-supported-variant policy
    for optional placement and unsupported-capability rejection for the mandatory search.
    Snapshot and restore entities, occupancy/blockers, protected passage/approach/Near paths,
    relation witnesses, copy counts, initialized-core identities, dominant anchors and preference
    truncation on every rejected continuation, including terminal budget/error returns. Preserve the
    whole committed mandatory chain only on success. The terminal task retains the existing local
    preference search; earlier tasks accept the first jointly feasible chain rather than claiming a
    room-wide preference optimum. Failed required alternatives do not become accepted witnesses or
    content omissions. `MandatoryBacktracks` reports rejected continuation count as search telemetry;
    it is excluded from semantic identity.
  - [x] Bound mandatory continuation recursion independently of geometric work: at most 64 tasks,
    with at most 256 total maximum compiled member counts across those tasks (conservatively including
    all their core/variant choices). Requests above either limit return `FurnishingMandatoryDepthBudget`
    with no partial entities. Existing 4096 placement-probe and 4096 Near-node room budgets remain
    shared across all levels. Charge child probes back to their parent search; never reset work
    counters on rollback. A depleted or invalid child stops its parent without a partial chain.
    Infeasibility without exhaustion remains `RequiredPackCannotFit`; a depth/work limit is a budget
    result rather than proof that no arrangement exists. Semantic hash v20 distinguishes this solver.
  - [ ] Extend search across core/variant selection and lighting constraints,
    with bounded hard-failure branch diagnostics and full replay exit fixtures. The current supported
    floor search is complete only within its member-pose domain and remaining budgets; room-wide
    optional selection remains sequential and does not retry earlier cores to improve density or
    make lights fit. Mandatory tasks now use joint continuations within their declared bounds.
    `OnSurface`, `InContainer` and multi-cell seats still make a variant unsupported rather than being
    silently ignored or approximated. Add joint optional/density/lighting and room-wide preference
    optimization, explicit optional core count/selection policies and higher required pack counts.
  - [ ] Add approved surface/container occupancy layers, parent/slot placement records and supported
    interaction poses; then update lighting mounting checks. Keep these blocked behind prototype
    capability inspection and R06 engine validation; ordinary XY footprint exclusion remains active.
  - [x] Add `KsProcgenAssemblySupportPlanner` for one selected complete variant and explicit selected
    member set. Require every mandatory member and the anchor, reject duplicate/unknown selected IDs,
    missing selected support parents, multiple support parents and selected support cycles. Bound
    input to 64 members/256 relations and declaration output to 64 fixtures/containers per prototype;
    reject mismatched member/prototype capability reports and duplicate container declarations.
    On failure return no member records, claims, witnesses or structure hash. Do not silently omit a
    selected optional member to resolve a conflict: the caller must try a different complete selection.
  - [x] Emit deterministic parent-before-child support records using ordinal member IDs to choose
    among ready members. Retain direct parent/relation/slot, floor root, dependency depth, entity ID,
    layer and declaration hash. A direct `InContainer` member and every descendant remain unexposed,
    including a descendant with a direct surface relation. This is containment metadata, not a usable
    machine/interaction pose or actual entity parent. Ordinary spatial relations add no support edges.
    The bounded graph walk uses at most 64 passes over at most 64 pending members.
  - [x] Aggregate claims by parent member, placement layer and named slot/container. Reject two selected
    children claiming a declared capacity-one slot. Generic container capacity and surface capacity
    without an explicit tracker remain unknown; multiple claims there remain unverified rather
    than inventing capacity one.
    Surface and container claims with the same textual name are distinct. Omitted optional children
    contribute no claims or support witnesses. These claims describe a single core instance, not a
    global cross-core reservation journal or permission to overlap floor footprints.
  - [x] Account for finite unnamed surface-tracker reservations using initial tracked items plus
    all selected children claiming that parent. Reject over-capacity claims atomically, without
    partial support records or a structure hash. Named surface slots do not inherit the unnamed
    tracker limit; zero/unrepresentably large maxima do not create a finite planner capacity.
    Successful plans still require engine validation and do not reserve live tracking capacity.
  - [x] Recompute selected support inspections from the bound declarations instead of trusting an old
    supplied witness, and reject initially missing/disabled/locked/reserved support. Successful
    structure plans always report `NeedsEngineValidation` and `EnginePlacementVerified == false`.
    Hash selected dependency records and claims under `ks-procgen-support-structure-v1`; sorting makes
    member/relation/selection/declaration enumeration order irrelevant. Selection, variant and relevant
    declaration changes alter that structure hash. It is a structural subset hash, not a full content
    or transform hash; geometry pipeline identity remains v20 until this becomes a planning input.
  - [x] Add `KsProcgenAssemblyPosePlanner` to derive detached transform candidates from one selected
    complete variant, inherited capability declarations, explicit floor-root coordinates and a chosen
    quarter-turn for every selected member. Recompute the support structure; require exactly one root
    pose per floor member and exactly one orientation per selected member. Bound each input/output to
    64 members and coordinates to finite values within one million units per axis. Validate authored
    allowed turns and assembly-relative orientation offsets. Invalid input or any rejected dependency
    returns no poses, support plan or pose hash; selected optional members cannot silently disappear.
  - [x] Distinguish support parent from transform parent. Unnamed surface drops remain siblings of
    their support in the common map/grid coordinate frame. A centered drop adds the declared offset
    in that common frame, matching `PlaceableSurfaceSystem`; rotating the support does not rotate the
    offset. A noncentered drop chooses the support origin as its click position and ignores unused
    centered offsets. Active offsets must be finite and within 16 units per axis. Nested exposed
    surface drops accumulate these offsets. Named surface slots, surfaces below a container ancestor,
    and non-item surface subjects are unsupported candidates. Declared anchoring alone does not
    reject a candidate: off-grid spawning can yield an unanchored item. The owned adapter must
    reject any subject that is actually anchored when the drop is attempted or reverified.
  - [x] Represent container candidates with the direct parent member, zero local position and zero
    local rotation, matching engine insertion. Their common-frame position/quarter-turn equals the
    parent's. Reject a chosen child orientation that conflicts with that insertion contract. Preserve
    support floor root, depth and containment exposure through each proposed transform. Hash the
    canonical support structure, common/local transforms and assembly orientation under
    `ks-procgen-support-poses-v1`; input enumeration order does not change replay identity.
    A successful pose plan still reports `NeedsEngineValidation` and `EnginePlacementVerified == false`.
    This is a transform subset identity; geometry v20 and capability v2 remain unchanged.
  - [ ] Consume these transform candidates in layer-aware room pose search and complete engine validation;
    journal claims per assembly instance across speculative branches, validate surface mounting/overlap and live insertion,
    and derive actual supported interaction landings. Floor search and tile staging retain their
    unsupported-layer gates. Transform candidates do not establish room containment, floor/support
    footprint legality, contact capacity, live rotation locks, anchoring, machine operating access,
    spatial relations or lighting feasibility. Surface candidates with the same position are not
    approved overlaps. Add a staged consumer that checks these facts before adopting any candidate;
    structural and transform planning alone cannot complete R02/R06.
  - [x] Add `KsProcgenAssemblyStageSystem` as a consumer for a complete selected support-only variant.
    Reinspect inherited prototype capabilities and rebuild poses from explicit selected members,
    floor-root positions and member orientations; never trust a caller-supplied pose plan. Stage floor,
    surface and container members together in parent-first order on a newly owned paused map. Apply
    exact floor/surface coordinates and quarter-turns without overriding live rotation locks. Perform
    live surface preflight and real non-swapping item-slot/generic-container insertion, then verify
    actual membership rather than accepting an item-slot return value alone. Selected spatial
    relations remain unsupported until their engine/access evaluator is wired in; do not ignore them.
  - [x] Verify every selected member's lifecycle, prototype, queued-deletion state, transform parent,
    position and rotation; require actual container membership and retained slot filters. Recheck live
    surface enablement, item/anchoring eligibility, tracking filters and effective drop offset. Across
    each surface parent, add every selected child's claim to the live tracked count and reject a
    finite capacity conflict even if each individual child passes preflight. Unknown/zero capacity
    does not become one. Surface poses remain drop candidates rather than settled contacts or mounts.
  - [x] Bound each assembly preview to 64 selected members, 256 cumulative synchronous allocations and
    sixteen retained maps. Charge map, selected members, insertion audio and startup/initialization
    children, including allocations later deleted or moved off-map. Reject surviving generated entities
    outside the owned map. Share the existing preview operation gate with both primitive adapters,
    through spawn, initialization and discard cleanup; nested callbacks cannot allocate or discard
    another preview. Failure/cancellation cleans all captured UIDs and releases the gate. No borrowed
    entities or published destination map are used.
  - [x] Initialize complete owned assembly previews explicitly once while keeping the map paused.
    Verify before initialization and afterward; retain real initializer-created content only within
    the allocation limit and owned map. Repeated initialization only revalidates. Pose, filter,
    capacity, lifecycle or containment drift, replacement/deletion, exceptions or cancellation discard
    the whole assembly and leave independently retained previews intact. Primitive previews remain
    available while their callers migrate to complete assembly staging.
  - [ ] Connect this complete assembly preview to room placement and joint layer-aware search. Add
    supported interaction landings, spatial relation evaluation, room/fixture collision legality,
    surface contact settling and mounting checks, lighting integration, speculative claim journaling
    across core instances, privacy/publication and rollback. Synchronous allocation capture still
    does not establish ownership of asynchronous or cleanup-generated spawns or external side effects.
  - [x] Add `KsProcgenSupportedAccessPlanner` to rebuild selected poses and derive declared operating
    approaches for exposed floor/surface members. Its room mask explicitly supplies floor, walls,
    blocking/occupied cells, access-network roots and the common-frame location of tile center (0,0).
    Do not infer a coordinate frame from rounded entity positions. Conservatively rasterize each
    rotated authored unit footprint: aligned boxes cover one tile per cell; fractional offsets can
    cover up to four tiles per cell. All projected exposed footprints must remain inside the room.
    Union every exposed blocking or vault-required footprint into ordinary movement obstruction;
    contained contents contribute no floor obstruction. This does not approve overlapping footprints.
  - [x] Require a cardinal clean route from at least one supplied network root to each operating
    landing. A machine's landing must lie directly beyond its chosen rotated front edge, including
    multi-cell edges; it cannot be a wall or blocking/vaulting tile. It must be empty or its explicitly
    associated traversible one-cell floor chair, with no other projected/preexisting occupancy there.
    Required `UsesSeat` fixes the selected chair; required `FacingOpenSpace` respects its empty or
    associated-chair policy. A declared local landing constrains the selected front-edge landing.
    The machine and all its support ancestors are excluded from that machine's route even if an
    authored movement flag says clear. A chair gets its own adjacent clean route that excludes the
    chair itself. Surface/multi-cell chairs and operating access for hidden contents are unsupported.
  - [x] Bound access masks to 4096 floor/network cells, 8192 wall/blocking/occupied cells, 64 members
    and sixteen authored footprint cells per member. Coordinates/frame origins must be finite and
    bounded. Share a node-expansion budget across every member and candidate landing (default 4096,
    configurable from zero through 65536); do not reset it between routes. On rejection or exhaustion
    return no poses, footprints, access witnesses or hash; retain only work telemetry and an issue.
    Sorted roots/edges/member IDs and fixed cardinal neighbor order make witness selection replayable.
    Hash pose identity, room/frame facts, projected movement/role geometry and accepted paths under
    `ks-procgen-supported-access-v4` (advanced by spatial, Near and landing-continuation amendments).
    The work limit/counter is excluded from successful identity.
  - [x] Let complete assembly previews optionally require this room geometry before allocation via
    `accessMask` and `maximumAccessExpandedCells`. Reuse the accepted access plan's rebuilt poses and
    retain its geometric witnesses on `stage.Access`; failure propagates without creating a map or
    member. Existing live pose/lifecycle/support validation remains active through initialization.
    All access records and stage results remain explicitly unverified for engine placement/use.
  - [x] Add `KsProcgenSupportedAssemblySearch` for one explicitly selected complete compiled variant.
    Search tile-aligned floor-root positions in a seed-ranked canonical order, each member's allowed
    turns and all four assembly turns. Use the support planner's deterministic parent-before-child
    order; assembly-relative members follow their authored turn offset, independent surface members
    may turn separately, and contained members inherit their container parent's world turn while
    retaining zero local insertion rotation. Backtrack earlier floor positions and orientations when
    a complete model lacks a cardinal operating approach. The selected membership never changes;
    this helper does not choose sibling variants or silently omit mandatory members.
  - [x] Keep floor-root footprints disjoint and inside the supplied room, excluding walls, existing
    blocking/occupied cells and protected network cells, including for clear floor furniture. Allow
    an optional subset of fixed tile-aligned floor roots; reject duplicate, fractional, nonfinite or
    non-floor-root constraints. A fixed root is a search restriction, not immutable map ownership.
    Reject offset surface projections onto preexisting blocking/occupied cells as well. Shared
    surface XY remains a proposal; projected layers have not passed engine collision checks.
    `OnSurface`, `InContainer`, `FacingOpenSpace`, `UsesSeat`, `AdjacentTo`, `FacingTarget` and
    `AtCorner` and `Near` are handled in this search. Unknown enum values reject before probes.
  - [x] Bound selected-variant search with one placement-probe counter and one path-expansion
    allowance across all branches and assembly turns. Defaults are 4096 each, independently
    configurable from zero through 65536. Charge each attempted member position/turn and every
    access planner expansion, including discarded models; do not reset allowances on backtracking.
    Exhaustion is distinct from exhausted-search rejection. Failed results contain no roots,
    orientations or access candidate; successful results retain chosen inputs, the v4 access
    witness/hash and aggregate telemetry. Work limits/counters do not alter successful candidate
    identity when the same geometry wins. This synchronous bounded helper has no cancellation token.
  - [x] Add `KsProcgenAssemblyStageSystem.TrySearchStage` to inspect fresh loaded prototypes, search
    before allocating a map, and consume its internally accepted poses/access witness directly in
    the owned staging transaction. Do not recompute access with a reset path budget. Retention,
    member/spawn budgets and pre-search cancellation remain enforced; staging checks cancellation
    again before allocation and through spawning. Live initialization, rotation locks, insertion,
    lifecycle checks and complete failure cleanup remain active. An engine rejection does not
    automatically resume geometry search. The stage still rejects selected spatial relations;
    geometric `UsesSeat`/`FacingOpenSpace` support does not bypass that engine gate.
  - [x] Reuse `KsProcgenAssemblyRelationEvaluator` over the selected exposed scene to enforce
    `AdjacentTo`, `FacingTarget`, `AtCorner`, `UsesSeat` and `FacingOpenSpace` after operating paths
    are found. Filter support relations into their existing separate support/pose domain; successful
    geometric witnesses must not claim surface mounting or insertion. Selected hidden members have
    already passed required-selection validation and do not become floor-space proposals. Reject
    spatial relations naming selected hidden endpoints. Require exactly tile-aligned common-frame
    positions at selected spatial endpoints; never round fractional surface positions into facing
    or adjacency proof. Fractional conservative footprint/access planning remains available when
    no spatial relation names that endpoint. Omitted optional subjects remain `NotApplicable`;
    selected required subjects with missing targets fail their relation normally.
  - [x] Reject a complete model atomically when a required spatial relation misses, allowing the
    bounded search to revisit earlier roots or member turns. Accepted results persist sorted
    `Relations` witnesses, backing wall cells for corner predicates and satisfied/applicable counts
    for preferred relations. Preferred misses remain visible; the current search does not optimize
    their score or compare all accepted models. Non-distance spatial predicates consume no additional BFS work;
    all attempted models still charge their access paths to the existing shared room allowance.
  - [x] Advance supported-access candidate identity to v2. Include canonical authored relation IDs,
    endpoint IDs, kinds, severities, approach policies, declared distances and support/container
    names, plus accepted spatial witness states/reasons/cells/backing walls/approaches. Required
    versus preferred semantics must change identity even when the winning poses happen to match.
    Reordered declarations must preserve identity. Failed plans expose no witnesses/hash. The
    existing full room geometry identity remains v20 because its unsupported-layer gates remain.
  - [x] Support `Near` for exposed, tile-aligned selected spatial endpoints. Reuse the existing
    cardinal shortest-path evaluator between the endpoints' accepted operating landings, declared
    usable landings or clear fallback cells. Required misses reject the complete model; preferred
    misses preserve their reason and any computed shortest path. A shortest path below the minimum
    cannot be padded with loops to satisfy the interval. Validate distances as 0 <= minimum <=
    maximum <= 64 before planning/search. Use the declared movement mask and all exposed blocking/
    vault-required footprints, plus both endpoints' complete support ancestors even if authored
    clear, so a supported device does not gain a shortcut through its support. Retain geometric
    route/distance witnesses; these are not engine reach or interaction proofs.
  - [x] Charge `Near` expansions to the same budget as operating access, across all relations and
    discarded candidate models. A truncated required or preferred route rejects atomically with
    `SupportedRelationPathBudget`, no poses/witnesses/hash and bounded telemetry. Per-relation
    masks preserve the complete assembly's association context via the evaluator's explicit
    `relationsToEvaluate` subset; do not erase other declarations while evaluating one predicate.
    In particular, `FacingOpenSpace` with an associated-chair policy still sees `UsesSeat`.
  - [x] Advance supported-access identity to v3 and hash accepted route distances and clean path
    cells, including the shortest path retained for a preferred below-minimum miss. Canonical
    relation/input ordering and accepted identity with larger sufficient budgets remain stable.
    The existing floor planner's default evaluator behavior and full geometry identity stay v20.
  - [x] Replace first-reachable operating landings with deterministic bounded continuation over
    complete landing combinations. Keep cardinal front-edge rules, authored landing restrictions,
    associated-chair ordering and each chair's independent access proof. Evaluate all required
    spatial/Near relations at a complete combination; on a miss, retry later landings and then earlier
    members' choices before rejecting that pose. Find access paths lazily, stopping at the first
    accepted combination. A later member failure rolls back prior witnesses. Preferred misses do
    not trigger score optimization. Existing floor furniture and publication gates stay active.
  - [x] Add `maximumLandingProbes` (default 4096, range zero through 65536) and `LandingProbes`
    telemetry. Each attempted member landing spends one probe; all access/Near BFS work, including
    discarded combinations, spends the same expansion allowance. Exhausted probes return
    `SupportedLandingProbeBudget` atomically. Supported root/rotation search shares both counters
    across all complete pose attempts, and both preview entry points forward the new limit before
    allocation. Empty operating domains need no probes. Advance supported-access identity to v4;
    unused sufficient probe allowances/counters do not affect accepted identity.
  - Landing continuation fixtures (2026-10-04): five unit cases cover a two-cell machine whose
    first reachable front landing fails required Near but its second succeeds; later chair choices
    requiring an earlier machine retry; authored landing restrictions; zero/one/invalid probe
    budgets; search forwarding and shared probe telemetry; completely infeasible combinations;
    and canonical replay with a larger sufficient allowance. A loaded-server case checks both
    owned preview entry points reject exhausted/invalid allowances before allocation.
  - [x] **Joint selected room cores and owned support preview (2026-10-14).** Compose complete
    namespaced instances with source bindings, detached entry lists and local-only relation targets.
    Enumerate whole variant combinations with cumulative work; search all floor/support poses in
    one shared room mask. Resolve packs without dropping any core minimum, with seeded optional
    selection or an explicit override. Reject projected overlap between unrelated support trees.
    Add the owned room-preview bridge using existing fresh capability inspection and lifecycle.
    Preserve spatial engine gates and the primary floor furnishing path; do not promote geometry
    or initialization to operational placement or publication.
    - [x] Unit evidence: repeated same-name cores retain separate parents and provenance;
      input permutations and detached per-copy lists; foreign targets, duplicate copies and
      request-wide member bounds reject atomically; shifted support cannot claim another root.
    - [x] Loaded evidence: two support cores initialize on one disposable map with exact identity
      replay, correct parents and complete cleanup; unsupported whole variants retry; exhausted
      budgets allocate nothing; failed poses keep their cost; authored minima survive optional
      suppression and weighted/explicit optional selection. Required copies cannot disappear.
    - [x] Record full Release/fresh Debug, focused unit/loaded/actual sandbox gates and deliberate
      regressions before updating the overall estimate for this capability.
    - [x] Final room-core gates (2026-10-14): restored production code passes Server Release
      (512 existing permitted warnings, zero errors), fresh nonincremental integration Debug
      (1107 existing permitted warnings, zero errors), **498 procgen unit cases**, and **147
      loaded cases** (146 procgen integration cases plus actual client `SandboxTest`); no skips.
      Compile-valid mutations caught aliased entry footprints, foreign-instance support parents
      and a refunded failed-variant placement budget. Original source bytes are restored in
      `finally`. Logs: `.ks_procgen_room_core_mutation_AliasedEntries.log`,
      `.ks_procgen_room_core_mutation_ForeignCoreParent.log`,
      `.ks_procgen_room_core_mutation_ResetBudget.log`, `.ks_procgen_room_core_release.log`,
      `.ks_procgen_room_core_final_debug.log`, `.ks_procgen_room_core_final_units.log`,
      `.ks_procgen_room_core_final_integration.log`. Overall estimate advances to **68%**
      (40-70% uncertainty); the 75% milestone remains active. Primary pipeline integration,
      spatial/operating engine validation, contact stability and publication remain open.
  - [ ] Extend joint room search into the primary furnishing pipeline and engine operating validation.
    They do not check actual fixture overlap, interaction reach/direction, all spatial relations,
    network-root connectivity/configuration, optimal preferred-relation choices, initializer-created
    obstructions, lighting, pressure or publication. The supplied mask is declared scene geometry,
    not a live-world query. Selected spatial relations still reject in the engine preview; the new
    planners reuse the spatial evaluator and retry operating landings for the supported subset;
    joint core/lighting selection and cross-core path protection remain pending.
    Floor furnishing and tile staging retain their unsupported-layer gates. Preserve these boundaries
    when wiring candidate paths into room generation, and rerun live collision/reach checks afterward.
  - Supported `Near` fixtures (2026-10-04): six unit cases cover a four-edge cardinal detour around
    a vault-required support despite a two-cell Manhattan distance; shortest paths below the
    minimum and preferred miss diagnostics; excluding a support even if its authored movement is
    clear and cutting that detour with walls; one allowance shared by access and multiple required/
    preferred `Near` relations; search retrying root placements to meet a required interval; bounded
    distance input, replay ordering, changed declared intervals and persisted path identity. An
    existing associated-seat/open-facing fixture detects lost cross-relation context and passes
    after the evaluator subset correction. One loaded-server case uses actual table/microwave/paper
    declarations to verify the detour and shared allowance, and confirms that a geometric winner
    still rejects at the spatial-relation staging gate without allocating entities.
  - Supported spatial-relation fixtures (2026-10-04): six unit cases exercise a corner table with
    a surface device and adjacent chair facing the table, all required geometric witnesses, selected
    backing walls, clean associated-seat access and an independent chair approach. Required corner
    and facing misses reject atomically; preferred misses remain accepted and counted. Search
    retries chair orientations and roots for required corner geometry. Hidden/fractional spatial
    endpoints reject rather than becoming fabricated tile witnesses. Omitted optional subjects and
    missing relation targets retain their distinct meanings. Reordered declarations preserve replay
    identity; changing a satisfied required relation to preferred changes v2 identity. One new loaded
    fixture inspects actual table/device/chair declarations, obtains matching geometry through
    search, rejects a wrong chair orientation and proves that the live spatial-relation staging
    gate still prevents allocation. This does not establish engine collision or usable direction.
  - Supported assembly search fixtures (2026-10-04): eight unit cases cover independent west-facing
    operation on a one-by-three floor; retrying an isolated floor-root placement after a route fails;
    disjoint roots and protected network/preexisting occupancy; assembly-relative contained turns,
    inherited insertion rotation and mandatory-selection rejection; shared path allowance across
    failed models and atomic probe/path exhaustion; unsupported selected spatial relations and
    malformed/fractional fixed roots; shifted surface projections onto preexisting occupied/blocking
    tiles; canonical input ordering, seed replay, changed origins and stable accepted identity with
    a larger work limit. One loaded-server fixture exercises automatic search through owned staging
    and initialization, budget/input failures before allocation and live rotation-lock rejection
    after a geometric winner. Complete cleanup preserves an independently retained initialized
    preview. This is a selected-variant helper and preview adapter, not joint core/lighting search.
  - Supported access fixtures (2026-10-04): nine unit cases cover machine access from a nontraversible
    support; west-facing access with a right wall on a one-by-three floor; diagonal-only isolation and
    an actual selected vault-required obstacle cutting a corridor; associated-chair selection and
    independent chair access; empty-versus-associated-chair policy, extra occupancy and unsupported
    multi-cell seats; explicit wide-machine front landings; conservative fractional offsets and an
    explicit shifted tile-center frame; contained contents versus hidden operating access; shared
    budget exhaustion and later-member atomic failure; reordered inputs, changed masks and replay.
    One new loaded-server case stages a rotated microwave on a vault-required table with hidden
    contents only after its supplied room mask admits a cardinal route. Wrong facing, a blocked
    front tile or an exhausted access budget creates no entities. Successful initialization retains
    exact live poses and the same access candidate but still reports no engine placement/use proof.
  - Complete assembly preview fixtures (2026-10-04): eight loaded-server cases cover a rotated table
    with an independently rotated microwave, a stored paper and a separate console/disk root; exact
    local transforms, real generic and slot insertion, initializer-created board accounting, paused
    initialization, idempotence and complete discard. Other cases reject selected spatial relations,
    live rotation locks, required-member omissions, changed root/member poses, effective offsets,
    disabled surfaces, changed tracking filters and queued deletion before or during initialization.
    A two-child surface fixture acquires a live capacity-one tracker during map initialization: both
    children individually remain eligible, but the whole pending claim set is rejected. Real insertion
    vetoes, throwing/cancelling callbacks, replacement spawners, off-map/over-budget generated items,
    startup faults and retention overflow leave no owned entities behind. Startup and map-init
    callbacks cannot nest/discard transactions, including calls between complete and primitive
    previews in both directions. Independently retained previews survive failures.
  - Implementation correction (2026-10-04): the initial complete fixture exposed an overstrict
    declaration-only anchoring rejection in `KsProcgenAssemblyPosePlanner`. A real microwave declares
    anchoring but is unanchored when the engine spawns it off-grid. Allow that detached candidate and
    require actual unanchored state through the live surface adapter before and after initialization.
    The unit fixture now distinguishes an initially anchored declaration (unverified candidate) from
    a non-item declaration (unsupported subject). Never forcibly clear live anchoring or rotation locks.
  - Supported pose fixtures (2026-10-04): eight new unit cases cover all four support rotations with
    common-frame centered offsets; noncentered origin clicks ignoring unused offsets; nested surface/
    container coordinates, parent-first ordering and containment exposure; engine insertion rotation
    conflicts; authored allowed/relative turns; duplicate, missing, nonfinite and mismatched inputs;
    unsupported named/contained/non-item surface subjects and bounded offsets; deterministic replay
    under reordered declarations, selection and orientations, with changed poses changing the hash.
    One loaded-server fixture executes the actual `AfterInteractUsingEvent` drop handler with an item
    held in a real hand/container on both the centered tracked table fixture and ordinary `Table`,
    for all four table rotations. Proposed coordinates match the actual sibling transforms. The
    same fixture compares container parent/local coordinates and zero local rotation against an
    owned preview using real disk-slot insertion. This tests handler transforms with reach supplied
    by the fixture; it does not test player reachability, settled contacts or machine operating access.
  - Supported pose verification (2026-10-04): all 215 focused Debug procedural unit tests and all
    twenty-six loaded-server Debug tests pass (zero skipped). Server Release compilation also passes
    with the existing `--no-restore -p:WarningLevel=0` workaround. Release and Debug builds were
    sequential; the Debug integration build used `--no-incremental` before the complete final corpus.
    Temporarily rotating centered offsets with the parent's quarter-turn makes the isolated offset
    test fail with `<1.5, 3.25>` instead of `<2.25, 3.5>`; source was restored in `finally` before
    final compilation/tests. Scoped diff and new-file whitespace checks pass. Unsuppressed CI
    warnings-as-errors remains unverified. R02/R06 stay in progress: these detached candidates have
    not entered room search or full assembly staging, and owned settling, supported interaction
    paths, collision/mount legality and publication remain open. Overall estimate remains about
    40% (35-45%); these tests do not increase the estimate by themselves.
  - Complete assembly preview verification (2026-10-04): all 215 focused Debug procedural unit tests,
    all thirty-four loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Release and Debug builds were sequential.
    Disabling the live aggregate capacity gate makes its isolated fixture fail with `InitializedPreview`
    instead of `Rejected`; source was restored in `finally`, Debug rebuilt with `--no-incremental`
    and the complete corpus rerun. `EmitCompilerGeneratedFiles=true` confirms the generated
    `KsProcgenAssemblyStageSystem.AutoSubscriptions()` calls
    `SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup, ...)`. Scoped diff and
    new-file whitespace checks pass. Unsuppressed CI warnings-as-errors remains unverified.
    Full support-only assembly staging now consumes rebuilt poses, replacing the earlier disconnected
    candidate boundary. R02/R06 remain open for room search, spatial/access/collision/mount checks,
    owned settling, lighting/pressure integration and publication. Geometry identity remains v20,
    capability identity v2 and support-pose subset identity v1. Overall estimate remains about 40%
    (35-45%); this is useful engine preview progress, not a complete room generator.
  - Supported access verification (2026-10-04): all 224 focused Debug procedural unit tests, all
    thirty-five loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Release and Debug builds were sequential;
    Debug was rebuilt with `--no-incremental` before the final corpus. Temporarily treating only
    `Blocks` as movement obstruction and allowing `VaultRequired` makes the actual corridor-obstacle
    fixture fail: the planner returns `Candidate` instead of rejection. Source was restored in
    `finally` before final builds/tests. Scoped diff and new-file whitespace checks pass. Unsuppressed
    CI warnings-as-errors remains unverified. Geometry identity remains v20, capability identity v2,
    support-pose subset identity v1 and the new supported-access subset identity v1. R02/R06 remain
    open for joint room search and live operating/collision/mount validation, owned settling and
    publication. Overall estimate remains about 40% (35-45%): access candidates are now connected to
    owned previews, but they do not certify a complete engine-operable interior.
  - Supported assembly search verification (2026-10-04): all 232 focused Debug procedural unit tests,
    all thirty-six loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Release and Debug builds were sequential;
    the final Debug integration build used `--no-incremental` and rebuilt the referenced unit tests.
    Temporarily restricting searched member turns to zero makes the isolated right-wall fixture
    fail with `Rejected` instead of `Candidate`; source was restored in `finally` before final builds
    and tests. Scoped diff and new-file whitespace checks pass. Unsuppressed CI warnings-as-errors
    remains unverified. Geometry identity stays v20, capability identity v2 and supported-access
    identity v1: this detached helper does not change the existing room-generation pipeline's
    decisions. R02/R06 remain open for joint room/core/lighting search, live collision/reach/mount
    checks, owned settling and publication. Overall estimate remains about 40% (35-45%); automatic
    selected-assembly geometry and an owned preview are now connected, with full room exits pending.
  - Supported spatial-relation verification (2026-10-04): all 238 focused Debug procedural unit
    tests, all thirty-seven loaded-server Debug tests (zero skipped) and Server Release compilation
    pass with the existing `--no-restore -p:WarningLevel=0` workaround. Release and Debug builds were
    sequential; the final Debug integration build used `--no-incremental` and rebuilt referenced
    unit tests. Temporarily bypassing required spatial satisfaction makes the isolated wrong-facing
    chair fixture fail: it returns `Candidate` instead of `Rejected`. Source was restored in `finally`
    before final builds/tests. Scoped diff and new-file whitespace checks pass. Unsuppressed CI
    warnings-as-errors remains unverified. Supported-access candidate identity advances to v2;
    full room geometry identity stays v20 and capability identity stays v2. R02/R06 remain in
    progress for supported `Near`, joint core/lighting/landing continuation, live collision/reach/
    mounting validation, owned settling and publication. Overall estimate remains about 40%
    (35-45%): explicit spatial relations now constrain supported candidate search, while live
    spatial-relation staging still rejects before allocation and full room exit fixtures remain open.
  - Supported `Near` verification (2026-10-04): all 244 focused Debug procedural unit tests, all
    thirty-eight loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Release and Debug builds were sequential;
    the final Debug integration build used `--no-incremental` and rebuilt referenced unit tests.
    Giving each relation a fresh path budget makes the isolated shared-allowance fixture fail with
    `Candidate` instead of `BudgetExceeded`. Source was restored in `finally` before final builds
    and tests. An initial focused run also caught lost `UsesSeat` context when `FacingOpenSpace`
    was evaluated against a stripped assembly; `relationsToEvaluate` now restricts evaluation work
    while retaining the complete declaration context, and the original fixture passes in the final
    corpus. Scoped diff and new-file whitespace checks pass. Unsuppressed CI warnings-as-errors
    remains unverified. Supported-access identity advances to v3; full room geometry identity stays
    v20 and capability identity stays v2. R02/R06 remain open for joint core/lighting/landing
    continuation, cross-core path protection, live collision/reach/mounting validation, owned
    settling and publication. Overall estimate remains about 40% (35-45%): supported candidate
    `Near` now works, while live spatial-relation staging still rejects before allocation.
  - Support structure fixtures (2026-10-02): eight unit tests cover a four-level surface/container
    chain ordered against lexical dependency order, floor-root propagation and containment exposure;
    capacity-one conflicts with no partial records; optional omission releasing exactly its claim;
    unknown generic/surface capacity and distinct layer claims; missing selected optional parents;
    support cycles and multiple parents; stale witnesses failing to override changed locked-slot
    declarations; canonical order/hash replay; selection/declaration changes; and malformed selection,
    report/prototype mismatch and input budgets. Loaded capability fixtures now feed real compiled
    table/device/seat and standing variants into the support planner. They verify the table preceding
    its device, direct parent/root/layer metadata, unknown surface capacity, selected-order replay and
    independent standing floor structure. `Exposed` is only containment classification; it does not
    certify reachable operational controls. No member transforms or actual support placement are tested.
  - Support structure verification (2026-10-02): all 170 focused Debug procedural unit tests,
    all five loaded-server Debug tests and Shared Release compilation pass with the existing
    `--no-restore -p:WarningLevel=0` workaround. Configurations were built sequentially to avoid
    shared-output assembly mismatches. Temporarily raising the capacity-conflict threshold to 64
    lets two children claim a declared single slot and makes the atomic rejection fixture fail:
    it receives `NeedsEngineValidation` instead of rejection. Source was restored in `finally`;
    the complete focused suite rebuilt and passed, including rejection of a stale unverified witness
    after its target's slot is declared locked. Scoped diff and new-file whitespace checks pass.
    Unsuppressed CI warnings-as-errors remains unverified. Structure plans do not satisfy A43-A44
    engine placement or close the R02/R06 rewrite gates.
  - Verification added: loaded fixture checks explicit office relationships, complete standing
    fallback, unsupported support-only rejection, independent optional-core omission and mandatory
    core-count rollback on a 1x3 floor, including passage and room-budget gates. Existing rotated multi-cell table, wide console, repeated
    workstation density, protected passages and deterministic replay assertions remain active.
    The standing fallback fixture disables density repetition to isolate variant replacement;
    workstation density remains covered separately. Temporarily treating every optional core as
    mandatory caused the independent-core assertion to report `MandatoryUnmet` instead of `Sparse`;
    the mutation was restored. Remaining work includes cross-core/lighting coordination and the
    layer/search tasks above; this does not complete R02 or authorize engine publication.
  - Core placement verification (2026-09-30): Shared Release build, all 112 focused Debug procedural unit
    tests and the loaded-server Debug fixture pass after restoring the mutation and adding the
    mandatory-core early-gate checks. Scoped `git diff --check` passes. Commands use `--no-restore`
    and the existing `-p:WarningLevel=0` workaround for repository analyzer warnings; this verifies
    compilation/runtime assertions but does not establish an unsuppressed CI warnings-as-errors gate.
  - Relation evaluation verification (2026-09-30): Shared Release compilation, all 120 focused
    Debug procedural unit tests and the loaded-server Debug fixture pass with the same warning
    workaround. Seven new evaluator cases cover perpendicular backing geometry, distinct footprint
    corners, self-occupied backing, preferred/required misses, omitted optional subjects/targets,
    diagonal rejection and unsupported preferred semantics. A new preview test checks severity,
    optimization truncation, degraded disposition and unverified engine access. Loaded fixtures
    verify preference-driven corner placement, named backing walls, deterministic witnesses,
    a legal corner miss, required-corner rejection, office relation witnesses and retention of a
    mandatory core after a one-probe preference budget. Temporarily promoting all misses to hard
    failures made both the preferred-corner and missing-preferred-target tests fail; the severity
    check was restored before the final passes. Scoped whitespace checks pass, including new files.
  - Near/landing verification (2026-09-30): Shared Release compilation, all 139 focused Debug
    procedural unit tests and the loaded-server Debug fixture pass with the existing
    `--no-restore -p:WarningLevel=0` workaround; unsuppressed CI warnings-as-errors remains unverified.
    Nineteen added cases cover exact wall detours versus insufficient ranges, blocking/vault-only
    footprints, shortest-path minimum bounds, shared zero-distance landings, shared node budgets,
    rotated/missing/blocked declared landings, malformed offsets, cardinal gaps, coordinate overflow,
    required/preferred path misses, value-equal replay witnesses and preview distance reporting.
    The loaded table/console/storage fixture verifies actual landing deserialization, complete
    core placement, preserved root/near routes and deterministic witnesses after a supporting
    locker is added. Temporarily excluding only `Blocks` from clean cells caused the vault-only
    regression to wrongly satisfy Near and fail its test; the full nonclear exclusion was restored
    before final verification. Scoped diff/whitespace checks pass. These are proposal/mask checks,
    not engine collision, interaction, support or published-map verification; R02 remains open.
  - Floor backtracking verification (2026-10-01): eight new unit cases exercise earlier-member
    repositioning for a later adjacent member, independent rotation for a required corner, optional
    member omission to preserve an exact Near path, alternate multi-cell front-edge approaches,
    chair-approach revisits when its machine occupies the first approach, retained wall-facing
    preferences, one-probe failure without a partial core and retrying an anchor without leaked claims.
    All 147 focused Debug procedural unit tests, the loaded-server Debug fixture and Shared Release
    compilation pass with `--no-restore -p:WarningLevel=0`; unsuppressed warnings-as-errors remains
    unverified. Existing office relationships, density, rotated footprints, protected paths and replay
    checks remain active. The revised solver can use every interior tile of the 4x4 mask for furniture
    and access: the loaded fixture explicitly verifies its permitted sparse-lighting fallback and
    separately retains nonempty lighting checks on a 5x5 mask. Furnishing does not claim a joint
    lighting-feasible search. Temporarily stopping after a failed member branch made five of the
    eight new cases fail (position, rotation, chair approach, machine front edge and optional omission);
    the solver was restored before final verification. Scoped diff and new-file whitespace checks
    pass. These results verify geometry proposals, not engine support, live collision or publication;
    R02 and the R01/R03-R07 rewrite gates remain open.
  - Weighted-core verification (2026-10-01): six new unit cases cover seeded replay, prototype-list
    reordering, disabled-candidate isolation, weighted first-choice frequency over 1024 fixed seeds,
    independent draw indices, finite extreme weights and invalid/duplicate/oversized pools. The
    loaded `weighted_cores.yml` fixtures check that a heavy optional core wins the only free tile
    despite its later ID, disabled content is absent and replayed entity proposals are identical.
    A second fixture retains a low-weight mandatory core and reaches three density copies by trying
    it after the heavy eight-tile-wide candidate fails each draw; failed candidates claim no cells.
    All 153 focused Debug procedural unit tests, the loaded-server Debug fixture and Shared Release
    compilation pass with the existing `--no-restore -p:WarningLevel=0` workaround. Temporarily
    replacing weights with equal weights makes the first-choice frequency test fail; the selector
    was restored before final verification. Scoped diff and new-file whitespace checks pass.
    Unsuppressed CI warnings-as-errors, cross-pack hard scheduling, joint lighting search, support
    layers and engine publication remain unverified/pending; this does not complete R02.
  - Mandatory scheduling verification (2026-10-02): the new loaded-server
    `MandatoryContentPrecedesOptionalFurnishing` test uses actual loaded pack prototypes and
    preselected region themes. It verifies explicit support minima claiming the only free tile
    before optional dominant content, retention of required support when the dominant cannot fit,
    deterministic replay, required-pack alternative attempts after the heavier footprint fails,
    single initial copies and stable support copy indices, hard-minimum rollback and one-probe
    budget rollback with no partial entities/witnesses. Failed required-pack alternatives are not
    reported as omissions of accepted content; the unattempted optional support pass remains
    distinct from retained mandatory support. Temporarily disabling the explicit-minimum phase
    makes the scarce-space assertion fail because optional furniture wins the tile; the planner
    was restored before final verification. All 153 focused Debug procedural unit tests, both
    loaded-server Debug tests and Shared Release compilation pass with the existing
    `--no-restore -p:WarningLevel=0` workaround. Scoped diff and new-fixture whitespace checks pass.
    Unsuppressed CI warnings-as-errors remains unverified. This verifies proposal scheduling,
    not joint mandatory feasibility, live collision, support layers, working lighting or publication;
    R02 remains open for the cross-core/layer/engine rewrites above.
  - Mandatory continuation verification (2026-10-02): three additional floor-search unit cases
    verify rejected complete poses resuming at another anchor, child probe exhaustion stopping the
    parent without a partial core, and child work remaining charged across a rejected branch and
    the next anchor. The new loaded-server `MandatoryCoresBacktrackAcrossPosesChoicesAndVariants`
    test checks a wide table moving away from the only required corner, replacement of a locally
    fitting wide core that blocks another required pack, and selection of a complete compact variant
    when its wide base blocks that pack. It verifies exact accepted landing-path retention, no rejected
    witness/footprint claims, stable copy indices and replay, positive backtrack telemetry, one-probe
    rollback and explicit task-depth budget rejection. Temporarily stopping after a rejected mandatory
    continuation makes the first pose fixture report `MandatoryUnmet` instead of `Proposed`; the
    continuation was restored before final verification. All 156 focused Debug procedural unit tests,
    all three loaded-server Debug tests and Shared Release compilation pass with the existing
    `--no-restore -p:WarningLevel=0` workaround. Scoped diff and new-fixture whitespace checks pass.
    Unsuppressed CI warnings-as-errors remains unverified. These checks establish bounded joint
    mandatory floor proposals, not unrestricted feasibility, room-wide preference/lighting optimality,
    support/container placement or engine publication. R02 and the remaining rewrite gates stay open.
- [ ] **R03 - Implement area-marker normalization and entrance ownership.** Add area-profile and
  marker data components/prototypes plus a server snapshot adapter. Feed exact normalized blobs
  into `KsProcgenRequest`/`KsProcgenGeometry`, preserving profiles, channels, explicit/derived IDs,
  constant claims and explicit void semantics. Add generic labeled entrance markers and optional
  numbered aliases. Resolve directed approach ownership without using entity enumeration or UIDs;
  reject ambiguous settings and out-of-domain writes. Keep source markers during preview and strip
  metadata only from generated output. Exit: A46-A47 and prior mask/constant tests pass.
  - [x] Add the initial `ksProcgenAreaProfile` prototype and `KsProcgenAreaCell` paint component/entity
    with a sample office profile. Profiles carry mode, geometry interpretation, connectivity policy,
    theme reference and bounded cell/coordinate limits. Paint entities are unanchored metadata so
    their presence does not depend on existing floor anchoring. They do not yet implement the full goals,
    library or publication profile. Entrance connection references are now supported as declaration
    contracts below; routing them remains pending. Unknown future connectivity enum values
    remain unsupported rather than silently selecting an existing policy.
  - [x] Add `KsProcgenAreaNormalizer` over detached host-frame records. Apply explicit void cells
    before cardinal components; preserve unpainted holes and diagonal separation. Deduplicate
    identical paint, reject conflicting duplicates, mixed connected profiles/blob IDs, reused blob
    IDs across components and overlapping retained claims across channels. Bound markers to 65536,
    profiles/components to 4096 and absolute coordinates to 1000000, with profile-specific limits.
    Fail atomically with no blobs/hash on malformed input or exhausted budgets. Validate source
    metadata even when its cell is subtracted; void paint cannot hide malformed references.
  - [x] Derive IDs from stable authored host key, channel and sorted exact cells under
    `ks-procgen-area-id-v1`; explicit IDs survive mask edits but geometry still affects replay.
    Hash canonical blob/profile/policy/theme/limit/void data under `ks-procgen-area-snapshot-v2`,
    now including declared entrances, connection profile references and normalized contract identity.
    Entity UIDs and enumeration order do not enter identity. `CreateRequest(seed)` copies exact
    component cells, geometry/profile limits, entrance spans, complete configurations and explicit
    group roots into a detached request. Complete transport and legacy generation guards replace
    the earlier conversion exception, preserving all declarations without silently dropping them.
    Theme remains on the detached blob profile for later selection. This grants no envelope or
    copied constant claims. Derived blob ID remains v1 and does not change when entrance data changes.
  - [x] Add `KsProcgenAreaSnapshotSystem.Snapshot` as a synchronous read-only unit-grid adapter.
    Bound global paint scans to 131072 and selected paint to 65536 (both configurable), scope by
    selected grid, copy loaded profiles and verify theme references. Use an inclusive entity query
    so paused editor/source/preview maps remain visible; resolve grid ownership through the engine
    transform API and direct-parent identity. Direct grid children use local
    coordinates even on rotated grids; nested children convert through the engine transform API.
    Require finite exact tile-center alignment; reject fractional placements instead of snapping.
    Reject invalid/queued source markers and queued/non-unit grids. Snapshotting creates/deletes
    no entities and leaves source markers intact. Exact alignment after nested transformed-parent
    conversions is conservative; numerical roundoff can reject a marker and is not rounded away.
    Ownership is authored, not inferred from world-space overlap: spawning on an empty grid can
    reparent paint to its map. Loaded fixtures establish the intended grid parent through public
    `SharedTransformSystem.SetCoordinates`; snapshotting never repairs source transforms.
  - Area normalization fixtures (2026-10-04): seven unit cases cover diagonal separation, exact
    holes, bridge void subtraction, identical duplicates, conflicting paint/component metadata,
    channel overlap, profile/marker/blob/coordinate limits, atomic failure, all-void NoOp, explicit
    and derived identity, canonical replay, and copied geometry requests without implicit envelopes
    or preserved cells. Three loaded-server cases cover paused rotated grids, source entity
    preservation, profile/theme resolution, fractional paint rejection, missing profiles, overlapping
    channels, selected/global scan budgets, equal stable identities on different entity grids,
    and snapshots detached from later prototype edits.
  - [x] Add `BindMarkerToGrid` as an explicit author/import action separate from read-only snapshots.
    Bind an existing live unanchored paint/entrance marker to an exact tile center on a unit grid,
    including void cells. Reject nonmarkers, invalid coordinates, queued entities and potential
    transform cycles before mutation; bound ancestor inspection to 256. The action creates no entity
    or floor. Loaded fixtures verify map-parented paint becomes visible only after explicit binding,
    invalid actions preserve its coordinates, exact nested-parent conversion works, and queued
    markers/grids reject. Editor UI wiring and non-unit-grid loaded coverage remain pending.
  - [x] Add `KsProcgenEntranceComponent`, generic `KsProcgenEntrance` and numbered convenience
    prototypes 1-10. Labels, channel, optional blob ID, inward normal, 1-64 threshold offsets and
    optional-sealing permission are declarations in host-grid axes; marker sprite rotation does not
    rotate them. The host grid's world rotation also does not alter their meaning. Snapshot both
    paint and entrances on paused grids, sharing the global scan budget and bounding selected
    entrances to 1024. Reject fractional marker anchors and wide-offset overflow before conversion.
  - [x] Normalize the entire perpendicular contiguous threshold span and its one-cell inside/outside
    approaches. Every inside cell must belong to the same blob in the named channel. Explicit blob
    IDs verify that geometric ownership rather than override it. Reject orphan/ambiguous/mismatched
    ownership, internal pseudo-boundaries, duplicate labels within a blob and overlapping same-blob
    thresholds. Labels may repeat across blobs. Fail atomically with no blobs/entrances/hash and
    report port/channel/blob/threshold context. Threshold metadata grants no additional write claim.
    Detached canonical records survive source-list/prototype edits; snapshots retain source entities.
  - [ ] Finish full profile constraints/library data, real doorway/door-state/external-landing inspection,
    constant/context/envelope integration and invocation-wide publication/rollback.
    These initial blobs are normalized authoring domains, not validated generated rooms or engine
    success. Add A46-A47's remaining full-workflow fixtures and verify source stripping only in
    published copies. R03 remains in progress.
  - Combined R02/R03 verification (2026-10-04): all 256 focused Debug procedural unit tests and
    all forty-two loaded-server Debug tests pass with zero skipped. Server Release compilation and
    a fresh Debug integration build pass with `--no-restore -p:WarningLevel=0`; builds were sequential
    and Debug used `--no-incremental`, rebuilding the referenced unit test assembly before final
    `--no-build` test runs. Temporary mutations restricting landing continuation to one choice and
    disabling void subtraction make both isolated tests fail at their expected assertions; both
    sources were restored in `finally` before final builds/tests. Initial loaded checks caught
    paused markers excluded by the ordinary query and empty-grid spawn reparenting; the adapter
    uses an inclusive query and fixtures explicitly author the grid parent. The final corpus passes
    after those corrections. Scoped diff and all new/edited batch-file whitespace checks pass.
    Unsuppressed CI warnings-as-errors remains unverified. No event subscriptions were added.
    Supported-access identity is v4, area identity/snapshot are v1, full room geometry remains v20
    and capability identity remains v2. R01-R03/R06 remain in progress; R04-R05/R07 remain pending.
    Overall estimate is approximately 42% (40-45%), reflecting landing continuation and initial
    marker domains; complete entrance authoring, live spatial validation and publication remain open.
- [ ] **R04 - Add entrance configuration contracts and atomic selection.** Extend requests,
  port/plan records and `KsProcgenPackingPlanner`/`KsProcgenGeometryPipeline` with canonical
  configuration IDs, complete port dispositions, exact/minimum modes, isolation scope, group IDs,
  root obligations and `DeclaredNetworks`. Implement shorthand as an editor/import adapter to that
  one canonical format. Reject global/local contradictions and missing labels before search.
  Journal selection and all branch-local consequences with room/layout alternatives; variants
  cannot contribute a union of links. Exit: A48 normalization and A49 rollback cases pass, while
  requests without a profile retain the prior default all-connected contract.
  - [x] Add `ksProcgenEntranceConnections` and typed configuration/group records for weighted
    `ExactComponents`/`RequiredConnections`, `DomainLocal`/`RequestNetwork`, grouped labels and
    explicitly sealed ports. Normalize at most 64 alternatives against at most 1024 blob-local
    labels, with bounded group/member lists. Every port has exactly one known disposition in every
    alternative, including zero-weight alternatives; seal permission is required. Reject duplicate
    configuration/group IDs, empty groups, missing labels, negative/nonfinite weights and an empty
    positive-weight set. Preserve singleton groups; this does not prove a real landing/destination.
  - [x] Reject the provable combination of `SingleNetwork`, multiple exact groups and request-wide
    isolation before search. Domain-local groups do not establish host attachment. `DeclaredNetworks`
    and explicit roots are supported below; hard-link/seal conflicts and actual inspected host scope
    remain pending. Do not
    silently change global connectivity policy or treat this validator as full compatibility proof.
  - [x] Add bounded import-only `KsProcgenEntranceShorthand.TryImport`: one alternative per line,
    hyphens for unordered group membership and semicolons between groups. Accept ASCII alphanumeric/
    underscore labels, ignore whitespace, reject empty segments/repeated labels and require persisted
    unique configuration IDs supplied by the author/editor. Canonical member-derived group IDs make
    group/port ordering stable. Canonical YAML with those same IDs normalizes identically. This adapter
    does not accept sealing/direct-link syntax or union lines into one connectivity graph.
  - [x] Hash detached normalized alternatives under `ks-procgen-entrance-contract-v2`, including blob
    scope, global policy, label seal permissions, configuration/group IDs, membership, weights, mode
    and isolation scope plus sorted explicit group roots. Canonicalize negative zero weights. Area profile `entranceConnections`
    resolves a loaded prototype and binds a separate contract to each blob's ports. Add a service
    profile and the two canonical service/storage alternatives from section 5.4. Area snapshots hash
    the profile reference and contract alongside exact entrance geometry; contract identity alone
    is a membership identity, not a scene geometry or engine validation identity.
  - [x] Add request `entranceDomain` with copied geometry, channel and complete configurations. This
    API handles one blob-scoped domain per request; invocation-wide multi-blob coordination remains
    pending. `KsProcgenGeometry.TryNormalize` retains detached entrances/contracts, with no partial
    shape on failure. Without authored alternatives, use canonical all-connected `RequiredConnections`.
    Requests without entrances keep prior behavior. `DeclaredNetworks` requires a domain and explicit
    roots for every group. Roots are unique host-grid cells, bounded to 1024 per configuration and
    inside the exact target; marker normalization applies the same check. Root markers and verified
    host-attachment adapters remain pending. A local root does not prove external access.
  - [x] Add `KsProcgenEntranceSceneSelector.Select` over one complete proposed scene per positive-weight
    configuration. Require complete eligible coverage, unique known IDs and at most 64 candidates.
    Seeded weighted ordering uses independent configuration streams. Failed scenes retain no claims
    or witnesses. Share up to 64 probes and 131072 component expansions across retries; stop at the
    first candidate or budget/invalid-input failure. Retain the chosen ID and matrix. This selects
    supplied geometry only, rather than searching every possible room/carving layout.
  - [ ] Carry chosen configurations/groups into packing/partition plan records and implement joint
    room/carving selection with branch journals. Preserve the legacy generation guards until every
    downstream solver consumes the declarations. The normalizer never selects a configuration;
    scene selection remains a geometric candidate without live structure/access certification.
  - R03/R04 fixtures (2026-10-04): nine entrance unit cases cover wide span binding, repeated labels
    across blobs versus duplicates within one, full directed ownership with explicit IDs, orphan
    approaches after void subtraction, ambiguity, malformed spans/threshold overlap, real boundary
    geometry, input/coordinate budgets, source detachment, permission-sensitive canonical replay and
    the geometry-request conversion guard. Nine connection unit cases cover separate whole
    alternatives, shorthand/canonical equivalence, membership completeness even for disabled choices,
    sealing permission/exclusivity, singleton retention, global exact-isolation contradictions,
    malformed weights/definitions, hash inputs/zero canonicalization, bounded shorthand rejection,
    missing profile references and independent blob scopes. Five new loaded cases exercise all ten
    numbered prototypes, generic equivalence on paused rotated grids, grid-axis declaration semantics,
    explicit binding and invalid-action preservation, nested parents and queued entities/grids,
    shared scan/entrance budgets, offset overflow/fractional markers, missing loaded references,
    both service/storage alternatives, atomic invalid membership and detached source/prototype data.
  - R03/R04 verification (2026-10-04): all 274 focused Debug procedural unit tests and all forty-seven
    loaded-server Debug tests pass with zero skipped after source restoration. Server Release and
    fresh Debug integration compilation pass with `--no-restore -p:WarningLevel=0`. Builds were
    sequential; final Debug used `--no-incremental` and rebuilt the referenced unit test assembly
    before the independent `--no-build` unit/integration runs. Three temporary mutations prove the
    ownership, disposition and binding checks: examining only the first inside landing makes the
    explicit-owner orphan test return Success; omitting complete disposition checking makes the
    missing-port test return Success; omitting grid binding leaves the live marker map-parented and
    fails the grid-parent assertion. All three isolated tests fail at those intended assertions;
    their sources were restored in `finally` before final builds/tests. Scoped document diff and
    batch C#/YAML whitespace checks pass. The snapshot system is now partial as CONTRIBUTING's
    dependency-generation convention requires; no event subscriptions were added. Unsuppressed CI
    warnings-as-errors remains unverified. Current versions: area ID v1, area snapshot v2, entrance
    contract v1, shorthand group ID v1, supported-access v4, full room geometry v20 and capability v2.
    R01-R04/R06 remain in progress and R05/R07 pending. Normalization is verified authoring metadata,
    not route selection, live door/landing access or publication. The full implementation estimate
    is approximately 44% (40-45%); A46-A52's combined engine/publication exit cases remain open.
- [ ] **R05 - Rewrite routing and partition fallbacks to respect groups.** Replace unconditional
  all-terminal/all-floor connection in `KsProcgenResidualConnector`, `KsProcgenRoutePlanner`,
  `KsProcgenPortNetworkAnalyzer`, `KsProcgenPureFillPlanner`, `KsProcgenPartitionPlanner`, and their
  pipeline callers with explicit per-group obligations and forbidden joins. Assign floor ownership
  to groups, budget separating structure, and reject incompatible preserved/prefab through-routes.
  Feed unrouteable or inseparable candidates back into configuration and room selection. Rewrite
  merged/open fallback gates in `KsProcgenFallbackPolicy`: exact separation is not a soft partition
  goal. Update theme regions, furnishing roots, lighting connectivity checks and tactical optional
  path proposals to use permitted components. Also constrain Near landing ownership, clean masks
  and preserved witness paths to the selected permitted group; a Near rule must not join forbidden
  entrance groups. Exit: A48-A50 pass for planned and adversarial
  connections, including thin masks, optional loops, open doors and external bypasses. Re-run
  existing default routing cases; do not exempt declared groups from port destination/access checks.
  - [x] Add `KsProcgenEntranceSceneAnalyzer.Analyze` over declared clean domain/host cells and blocked
    thresholds. Validate positive-weight configuration identity, exact domain ownership, disjoint host
    cells and input limits. Operational entrances require their complete inside span, thresholds and
    exterior landings to be clean; sealed thresholds must be explicitly blocked. Omitted floor is no
    sealing proof. Scene cells are hypotheses; no fixture, actor, vaulting, door or atmosphere is queried.
    External cells remain read-only context; this analyzer adds no write rights or wall placements.
  - [x] Compute deterministic cardinal components. Each group's ports and roots connect locally
    without leaving the domain; every clean local component has group ownership. Exact groups cannot
    share a component in the selected scope; RequiredConnections permits joins. Request-wide exact
    isolation requires declared complete host context and checks external bypasses in the combined
    graph. SingleNetwork may connect locally separate groups through known host paths and requires
    all domain floor/request roots in one combined component. DeclaredNetworks permits multiple rooted
    groups. Retain copied local/combined component cells and distinct local/scope/global port matrix IDs.
    Global IDs are null when the combined host graph was not needed or inspected by the analyzer.
    Failures retain no witnesses/configuration ID/hash. Host completeness remains a caller declaration.
  - [x] Charge local and combined component work to one allowance, shared by the selector across
    discarded scenes. Hash target/envelope/preserved/void masks, constant identity, entrance geometry,
    clean domain/host cells, blocked thresholds, roots, completeness and contract/configuration under
    `ks-procgen-entrance-scene-v1`. Accepted scene plus request ID/seed yields
    `ks-procgen-entrance-selection-v1`; counters and unused sufficient budgets do not affect identity.
  - [x] Reject normalized entrance-aware inputs before legacy packing, residual routing, pure fill or
    old port-network analysis. The geometry pipeline/tile preview reject before allocation. These guards
    prevent entrance declarations reaching unconditional all-terminal/all-floor connection. Full room
    geometry identity remains v20 because this generation path is still gated. Generic cardinal route
    primitives are reused by the initial group adapter below.
  - [x] Add `KsProcgenGroupRoutePlanner.Plan` for entrance-aware `Procedural`/`Footprint` requests.
    Build one cardinal tree per selected group from full inside approaches, in-target operational
    thresholds and explicit roots. Assign exact-group ownership; protect foreign mandatory cells,
    inspected passage cells and earlier claims together with their cardinal neighbors. This allows
    detours without a touching floor join. RequiredConnections may merge. Classify preserved clean
    components before routing: exact mode requires each to contain terminals of exactly one group.
    Reject unknown preserved context, unassigned existing components and already joined groups.
    Request-wide roots must already have explicit group terminal ownership.
  - [x] Give every unused writable target cell an explicit proposed wall disposition. Preserve
    inspected floor and read-only host cells; do not write the envelope or gain exterior write rights.
    Proposed sealed thresholds must be writable target cells, or already explicitly blocked in the
    supplied context. Reject intersecting operational/blocked terminals. Run independent complete
    scene analysis before retaining any routes, floor/wall cells, witnesses or hashes; failed attempts
    retain counters only. These are cell dispositions, not materialized airtight walls or door states.
  - [x] Add `KsProcgenGroupRoutePlanner.Select` with seeded complete-configuration ordering shared
    with the supplied-scene selector. Share probes and existing-passage, route and final component
    work across rejected proposals. Limits: 64 configurations/probes, 64 groups, 1000 unique terminals
    per group, 65536 cells per context mask, default 131072 work expansions and maximum 1000000;
    final scene analysis additionally caps its own allowance at 131072. Exhaustion reports
    BudgetExceeded without partial geometry. Retain frozen scene masks and copied sorted route,
    floor and wall records. Current identity versions are `ks-procgen-group-route-v2` and
    `ks-procgen-group-route-selection-v2`; unused sufficient budgets do not change accepted identity.
  - [x] Add complete-order `KsProcgenGroupRoutePlanner.Search`. Try canonical group ID order, its
    reverse, then remaining lexicographic permutations lazily, without repeats or factorial storage.
    `Plan` optionally accepts an explicit complete unique group-order list; reject unknown, duplicate
    and missing IDs before carving. Accepted proposals retain a detached immutable `GroupOrder`,
    included in route identity v2. Retry only order-sensitive routing/forbidden-terminal failures;
    invalid input, incompatible mandatory/preserved components and final scene failures retain their
    diagnostic. This does not retry individual tree choices within an order.
  - [x] Carry search through automatic `Select`, sharing at most 64 group-order probes and the
    expansion allowance across every complete order and configuration. Count existing-passage scans,
    failed trees and final scene analysis, including discarded attempts. Configuration probes and
    group-order probes have separate counters and allowances. Zero order allowance returns
    BudgetExceeded before any tree. A remaining untried order at the cap is budget exhaustion, not
    infeasibility; exhausting all permutations reports rejection without accepted geometry. Never
    restore a rejected order's reservations into the next attempt. Budget/invalid-input failures stop
    selection. Sufficient unused allowances do not change selected geometry/order identity.
  - Constructor boundary: greedy cardinal trees within each attempted group order, without alternate
    tree/layout search, room fitting or full floor partitioning. Existing clean components
    with no group terminals are rejected rather than automatically attached. Host/preserved completeness
    flags are caller declarations, not live inspection or source-fingerprint proof. No actor, fixture,
    vaulting, wall material, room/door/window/hull, pressure or power validation is added. A rejected
    greedy proposal does not prove that every layout for the shape is impossible. EngineAccessVerified
    remains false; generation guards remain active until all downstream stages preserve these groups.
  - [ ] Extend group floor/wall ownership and protected routes into compatible room/prefab/constant
    selection, partitions and branch journals. Add alternate tree/layout search where needed.
    Rewrite partition/fallback/theme/furnishing/
    lighting/Near callers to preserve boundaries. Use real actor/door/host graphs for engine validation
    before publication. Rejecting a supplied scene does not prove every scene for that shape impossible.
  - Request/scene fixtures (2026-10-04/05): thirteen unit cases cover exact versus minimum connectivity,
    complete local component/port matrices, local obligations despite external bypasses, incomplete
    host context, request-wide forbidden joins, SingleNetwork host joins with separate local scope IDs,
    explicit DeclaredNetworks roots and unreachable/out-of-domain roots, available approaches and
    declared permanent sealing, unassigned floor, diagonal-only connectivity, complete alternative
    retry without rejected floor retention, shared expansion/probe limits, canonical replay with
    geometry/root-sensitive identity, malformed/incomplete candidate sets, detached request copies,
    all legacy entry-point guards and root ordering/duplicates/bounds. Loaded generic/numbered marker
    fixtures now round-trip complete entrance requests. The loaded service/profile case retains both
    alternatives and rejects its unsplittable one-cell-wide scene with no accepted claims or entity
    mutation. A new loaded case normalizes a rooted DeclaredNetworks request, then checks pipeline
    and tile-stage rejection before allocation. These are declaration/graph checks, not actor traversal.
  - Group construction fixtures (2026-10-05): ten unit cases cover a separated thin strip with explicit
    wall ownership, inseparable adjacent terminals versus allowed minimum-connectivity merges, a
    constructive detour around another group's landing, preserved passages and incompatible existing
    joins, incomplete/unassigned preserved context, writable versus exterior sealing, complete retry
    after a host bypass, shared route/final-analysis/probe limits, canonical identity, detached frozen
    snapshots, malformed context, unsupported geometry and unassigned roots. A loaded marker-authored
    1x5 request constructs two networks with one separating wall cell; source entities and snapshot
    identity remain unchanged, and legacy packing still rejects before generation.
  - Request/scene/group verification (2026-10-05): all 297 focused Debug procedural unit tests and
    all forty-nine loaded-server tests across `KsProcgenContentLoadTests`,
    `KsProcgenAssemblyStageTests` and `KsProcgenAreaSnapshotTests` pass, with zero skipped tests.
    The ten loaded area cases include the new marker-to-route construction and the entrance-aware
    pipeline/tile rejection gate. Server Release succeeds, followed sequentially by a fresh Debug
    integration-project `--no-incremental` build and both final no-build test suites; the Debug rebuild
    also rebuilds the referenced unit project. Builds use `--no-restore -p:WarningLevel=0`, retaining
    the existing unrelated-warning workaround; unsuppressed warnings-as-errors CI remains unverified.
    Release reports three existing package-pruning warnings, no errors; fresh Debug reports no errors.
    Four intentional behavioral defects are detected at assertions: disabling exact scene separation
    accepts a forbidden join; separately forgetting rejected scene work accepts an over-budget retry;
    removing only the constructor's cardinal separation reservations rejects the feasible detour;
    separately forgetting rejected route work accepts an over-budget complete proposal. Source is
    restored in `finally` after each mutation, before final builds/tests. Scoped tracked diff and
    edited/new-file trailing-whitespace checks pass. No event subscriptions were introduced.
    Current identities: area ID v1, area snapshot v2, entrance contract v2, shorthand group v1,
    entrance scene/selection v1, group route/selection v1, supported access v4, full geometry v20
    and capability v2. Earlier dated ledgers retain the identities/statuses at their verification time.
    This completes the initial constructor subtasks, not R05 or A48-A50: complete pipeline integration,
    room/partition selection and live actor/door/host validation remain required. R01-R06 remain
    in progress; R07 remains pending. Rough full-system completion is 47% (40-50% uncertainty).
  - Group-order fixtures (2026-10-10): eight unit cases cover an irregular 7x7 mask whose canonical
    order routes one group then fails, while reverse order constructs two separate networks with
    twenty-two floor cells; complete rollback, explicit order validation and detached witnesses;
    exact shared expansion/order limits; all six unique three-group permutations; canonical replay
    despite reordered declarations; invalid bounds/configurations; and probe/work sharing across
    a rejected six-order configuration and a minimum-connectivity alternative. A loaded case paints
    the same irregular blob with four entrance markers, transports its request, retries its order and
    retains the source entity set and snapshot identity. The standalone constructor remains guarded
    out of legacy full generation, and EngineAccessVerified remains false.
  - [x] Restore procgen compatibility after merging current `origin/master` (2026-10-10). Fetch
    `origin/master` without recursive fetch and merge `eac1062cacf` into `ksprocgen` as `905dd42c6fd`;
    no merge conflicts. The repository's post-merge BuildChecker hook updates pinned submodules,
    including RobustToolbox to `3708c7a31094649d60cecdab11a0a6c3aa96223e`. The first Debug build
    exposes the removed `BaseContainer.ExpectedEntities` API. Update prototype/live slot inspection
    to `PvsDetachedEntities`, rename snapshot data to `PvsDetachedContentCount`, and bump capability
    identity to v3. Retain rejection for detached occupancy even when real contained count is zero;
    loaded item-slot and generic ContainerSlot checks cover rejection and restored eligibility.
    Detached count is replication occupancy evidence, not proof of a physical occupant on the server.
    The first warnings-enabled Release build also exposes seven RA0042 errors for redundant explicit
    prototype type names. Use `[Prototype]` for those matching generated names; YAML type IDs remain
    the same. Prototype capability inspection now uses the current `TryComp` API. Verify prototype
    loading again after these declarations change.
    Warnings-enabled server compilation additionally exposes twenty-four RA0030/RA0045 errors in
    existing procgen systems. Use typed nongeneric Transform/MetaData TryComp overloads and the
    EntitySystem TryComp/AllEntityQuery proxies. Preserve AllEntityQuery's paused-entity behavior;
    no event handlers, cleanup ownership or fixture access policies change in this API migration.
  - [x] Repair the procgen references rejected by the real client sandbox. Ordinary loaded fixtures
    had not checked these references. Replace `ReadOnlyCollection` wrappers and inferred conditional
    wrappers with copied immutable lists through `KsProcgenReadOnly`; source mutation and attempted
    mutation through IList cannot alter normalized snapshots. Replace BCL PriorityQueue with a
    SortedSet ordered by the same `(cost, y, x)` keys, retaining route tie behavior and stale-entry
    checks. Replace compiler-generated nonempty List collection expressions with ordinary list
    initializers. Replace forbidden ArgumentNullException usage with ArgumentException and explicitly
    check transformed long coordinates against integer bounds before casting; malformed transforms
    still reject without wrapped coordinates. Geometry and layout adapters catch that supported
    exception. No engine sandbox whitelist is relaxed. SandboxTest must remain in future release
    evidence alongside unit and loaded-server suites when adding unfamiliar shared/client APIs.
  - Merge/search/compatibility verification (2026-10-10): all 307 focused Debug procedural unit
    tests and all fifty loaded-server tests across the three procgen fixtures pass. The real client
    SandboxTest also passes: the final combined integration filter reports 51 passed, zero failed,
    zero skipped. Unit tests likewise have zero skips. The eleven loaded area cases include the new
    irregular marker blob and the unchanged legacy generation rejection gate. Server Release passes
    with warnings enabled and the repository's normal warnings-as-errors settings, without the old
    WarningLevel=0 workaround. It reports 250 permitted warnings and zero errors in the final build.
    Sequential fresh Debug integration-project `--no-incremental --no-restore` compilation also
    passes without a warning-level override (1102 warnings, zero errors), followed by final no-build
    unit/integration runs. These are focused procgen plus sandbox gates, not the entire repository's
    integration suite. Existing permitted upstream/other-feature warnings remain.
    Four independent mutations fail at assertions: stopping after the first order rejects the feasible
    retry; separately discarding previous-order work accepts an over-budget retry; disabling explicit
    integer bounds stops throwing for a wrapping transform; ignoring detached slot occupancy admits
    a reserved item slot. Each source is restored in `finally` before final Release/Debug builds and
    tests. The sandbox's original red run specifically identifies procgen's forbidden references;
    the repaired code passes the same real checker. Snapshot immutability and sorted-route behavior
    pass the complete unit corpus. Scoped diff and all changed/new-file whitespace checks pass.
    No event subscriptions were introduced. Current identities: area ID v1, area snapshot v2,
    entrance contract v2, shorthand group v1, entrance scene/selection v1, group route/selection v2,
    supported access v4, full geometry v20 and capability v3. Existing dated ledgers describe their
    earlier gates and identities. R01-R06 remain in progress; R07 remains pending. Full room fitting,
    partitions/furnishing, materialization and live connection/access validation are still required;
    route-order success is a geometric proposal. Rough full-system completion is 48% (40-50%).
  - [x] Add the first bounded entrance-aware floor growth adapter, `KsProcgenGroupFloorPlanner`.
    Route the entire selected configuration first, then use deterministic cardinal growth from all
    accepted floor seeds. Exact components cannot gain a cardinal edge to another owner; those
    contested writable cells become separation walls. RequiredConnections may merge floors and
    retain all group memberships in the resulting connected region. Every initial route/preserved
    clean cell remains protected, with an immutable per-region subset for future furnishing.
  - [x] Reserve inspected host components during RequestNetwork exact growth. Components already
    attached to a seed belong to its owner; unattached read-only host passages can be claimed by
    the first growing owner. A foreign owner cannot subsequently connect through that passage.
    DomainLocal intentionally imposes only local separation. This adapter uses caller-declared
    clean masks and completeness; it never claims live actor or door inspection.
  - [x] Carry one expansion allowance through route-order search, host ownership inspection,
    floor growth and independent final component analysis. Complete configuration selection charges
    rejected proposals and shares order/candidate limits. Failure exposes counters and diagnostics,
    with no retained routing, regions, scene, masks or hashes. Default allowance is 262144 expansions,
    bounded at 1000000; each scene analysis remains capped at 131072. Work counters describe these
    graph operations, not total normalization CPU time.
  - [x] Preserve write boundaries: sealed thresholds remain blocked, preserved non-floor/void/envelope
    cells are never converted, and unreachable writable islands remain proposed walls. One-by-three
    targets work. Regions are entrance-connected floor components, not yet theme-sized rooms or
    partitions. Stable floor/selection identity uses `ks-procgen-group-floor-v1` and
    `ks-procgen-group-floor-selection-v1`, including the accepted route and independently analyzed
    scene identity. Accepted outputs detach from mutable author input.
  - [x] Add a first bounded owned-network partition/theme/furnishing adapter. Reconstruct ownership
    from independently revalidated floor connectivity, rather than trusting caller-provided region
    labels. Validate source scene/floor identity against the current request; reject stale requests,
    incomplete/overlapping zones and any logical zone spanning different exact networks. Generate
    connected logical room zones automatically, or accept complete nominated zones for a proposal.
    Run the seam solver separately per connected network, preserving all route and read-only floor
    cells. Its merged/open fallback remains inside that network. Preserve separation walls and sealed
    thresholds, and independently analyze the complete resulting scene before accepting any output.
  - [x] Extend `KsProcgenPartitionPlanner.Plan` with optional `protectedFloorCells`, independent of
    passage-zone classification. A room may contain protected walkways and still receive furniture.
    `KsProcgenGroupThemePlanner` retains network group IDs and each themed region's protected-route
    subset. `KsProcgenThemedRegion.EntranceGroupIds` and `ProtectedPassageCells` default to empty for
    legacy callers. Furnishing validates declared protected cells against the room floor, uses them
    as route terminals, and retains them through atomic core placement, backtracking and access search.
  - [ ] Complete the main pipeline rewrite, including joint prefab fitting, room size goals,
    full-host tactical network ownership, fallback policy and live door/access verification. The owned
    adapter currently bounds source floor to 4096 cells and logical zones to 256, sharing at most
    64 actual seam merges across all networks. The legacy seam solver's CPU work is bounded by those
    caps but is not represented in the graph expansion counter. Shared graph work covers source
    validation, logical zoning and final validation; an accepted open fallback may exhaust its soft
    merge allowance. Tiny/thin auto-generated networks remain passage zones. This is a conservative
    geometric proposal, not proof of infeasibility, optimal room sizing or an operational door.
    Keep the existing full-pipeline rejection gates until downstream materialization and validation
    preserve the entrance contract. Alternate growth seeds/orders also remain pending.
  - Floor-growth verification (2026-10-10): twelve new focused unit cases cover a 5x5 exact split
    into two ten-cell floors separated by a continuous five-cell wall; minimum-connectivity merging;
    multi-entrance trees and declared roots retained as protected cells; a 1x3 domain and unreachable
    writable island; preserved/void/envelope masks; sealed thresholds; foreign and initially unowned
    host passages; exact shared expansion boundaries; complete-configuration retry with candidate
    and order caps; detached immutable replay; and invalid input with no partial artifacts.
    A new loaded area fixture paints twenty-five live markers, binds two entrances, constructs the
    separated floor proposal, and verifies both source entity membership and snapshot identity remain
    unchanged. It retains the legacy packing rejection and `EngineAccessVerified == false` boundary.
    The final full focused unit run passes 319 tests; the combined integration run passes all fifty-one
    loaded-server cases and the real client SandboxTest (52 total), zero failures or skips in either
    suite. This remains focused verification, not the entire repository integration suite.
    Three independent mutations disable local separation, foreign-host exclusion and first-owner
    host claiming. Each fails its expected candidate assertion with `ForbiddenEntranceGroupJoin`
    from the independent final analyzer. All three sources are restored in `finally` before the final
    builds and suites. Warnings-enabled Server Release passes with 512 permitted warnings and zero
    errors; sequential fresh Debug integration `--no-incremental --no-restore` passes with 1102
    warnings and zero errors. No warning suppression or sandbox whitelist changes are introduced.
    Tracked diff checks and new-file trailing-whitespace checks pass. No event subscriptions are added.
    Floor and floor-selection identities are v1; route/route-selection remain v2, capability v3 and
    full geometry v20. R05 remains in progress, R01-R04/R06 stay in progress and R07 stays pending.
    Rough full-system implementation is now 49% (40-50% uncertainty); floor proposals alone do not
    establish playable rooms, collision-free furnishings, live entrance access or spaceproofing.
  - Owned partition/theme/furnishing verification (2026-10-10): eight new focused unit cases cover
    automatic logical zones scoped to separate networks, complete nominated room zones with usable
    cardinal door geometry, fully protected seam fallback without deleting separation walls,
    RequiredConnections merged memberships, preserved floor, thin passage networks, shared graph
    allowance boundaries, cross-network/incomplete/duplicate-zone rejection, detached immutable replay
    and stale request rejection. Accepted network artifacts copy legacy solver collections into
    immutable snapshots; ownership is reconstructed from the validated graph, not source region labels.
    A new loaded marker fixture follows an 8x7 blob with four entrances and two declared roots through
    floor growth, owned partitioning, office themes and floor furniture. Every protected route cell
    remains reserved; blocker masks include both ordinary movement blockers and vault-required entities,
    and clean cardinal paths connect each protected terminal after furnishing. Theme membership stays
    separate. An invalid protected tile rejects furnishing with no entities. A second configuration of
    the same floor makes the later network a passage with mandatory room content: theme rejection
    discards earlier network choices. Source entities and marker snapshot identity remain unchanged,
    and the full geometry pipeline retains its entrance-aware rejection gate before allocation.
    Two independent mutations fail at expected assertions: dropping the seam solver's protected-floor
    constraint yields `InvalidOwnedPartitionProposal`; dropping furnishing's entrance-route terminals
    loses required protected coverage in the loaded fixture. Both sources are restored in `finally`
    before final builds and tests. Final focused Debug tests pass 327 units and fifty-two loaded-server
    cases plus the real client SandboxTest (53 combined), zero failures/skips. Warnings-enabled Server
    Release passes with 512 permitted warnings and zero errors. Sequential fresh Debug integration
    `--no-incremental --no-restore` passes with 1102 warnings and zero errors, followed by those final
    no-build test runs. Verification is focused procgen plus sandbox, not the repository's full suite.
    Scoped tracked diff and new-file whitespace checks pass; no event subscriptions or sandbox
    exceptions are introduced. Owned partition identity is `ks-procgen-group-partition-v1`; floor and
    floor selection stay v1, route/route selection v2, capability v3 and full geometry v20.
    R01-R06 remain in progress and R07 pending. Rough full-system completion is 50% (40-55% uncertainty).
    The accepted artifacts describe proposed clean masks and floor entities. Actual wall/door/light
    operation, actor access, pressure/power behavior and publication still need their remaining gates.
  - [x] Check retained entrance reservations in `KsProcgenLightingPlanner` before fixture selection,
    including the NotRequested path. Reject furnishing inputs missing the themed region's protected
    route cells or placing movement blockers on a protected passage. Keep fixtures off reserved
    routes, entity footprints and operating approaches, and retain the cardinal connectivity guard.
    Fixture reach remains an estimate through the supplied floor graph; it does not model actual
    illumination, occlusion, supply, startup or engine fixture footprints.
  - [x] Bound all lighting reach and connectivity flood expansions with optional `maxExpandedCells`
    (default and ceiling 1000000). Report actual graph work in `ExpandedCells`. A graph-budget failure
    returns no partial lights or coverage metrics. Fixture-count exhaustion keeps the existing
    per-room `LightingProposalBudget` semantics, including its speculative partial fixture list;
    callers must inspect status. Accepted lists are immutable. Skip unused post-selection connectivity
    probes when coverage is already met or the fixture cap does not govern the result.
  - [x] Add `KsProcgenGroupLightingPlanner` for complete room sets: require unique matched region IDs,
    process rooms canonically, and share both fixture and graph allowances across all rooms. Any later
    invalid/budget result discards all region proposals while retaining counters and a diagnostic.
    Successfully bounded sparse estimates stay explicit; NotRequested rooms do not consume fixtures.
    These per-stage allowances are not yet a single request-wide pipeline work budget.
  - [x] Add `KsProcgenGroupInteriorAnalyzer` to independently audit complete themed/furnished/lit
    proposals against the current request and owned partition. Revalidate the source scene; require
    complete, nonoverlapping floor ownership with matching entrance memberships and route reservations;
    reject foreign/overlapping footprints and writes on read-only floor. Ordinary movement blockers,
    anything requiring vaulting, and proposed floor lights are excluded from clean walking. Preserve
    all protected entrance routes and inspect door/operating approach cells. Flood each themed room
    separately so access cannot rely on a detour outside that room, then independently analyze the
    complete final entrance scene, including declared host bypass and sealed-threshold checks.
    Source validation, room floods and final validation share one graph allowance (default 262144,
    ceiling 1000000). Scene analyses individually retain their 131072 cap. Limit source floor to
    4096 cells, rooms to 256, and declared partition doors to 4096; bound entity footprint sizes and
    proposal lists too. Every failure exposes no scene, blocker mask, analysis or access hash.
    Accepted scenes/blockers are immutable. `ks-procgen-group-interior-access-v1` identifies the
    partition, final clean scene and room terminals; it is an access identity, not a full content hash.
    EngineAccessVerified and WorkingCoverageVerified remain false. Keep the full pipeline allocation
    rejection until actual door/furniture/light/material validation and ownership are implemented.
  - Lighting/interior-access verification (2026-10-10): thirteen new focused unit cases verify
    complete owned interior replay and detached scenes; ordinary, vault-required and traversible
    movement classes; protected entrance and machine approach exclusion; disconnected room rejection;
    foreign group metadata, missing reservations, duplicate region records and overlapping/foreign
    footprints; unexpected NotRequested fixtures; read-only floor writes even for traversible entities;
    each partition door's threshold and both approaches; and exact shared source/room/final graph
    allowance boundaries. The existing loaded 8x7 marker case now follows actual office furniture
    proposals through lighting and the final owned access audit. It checks per-room and aggregate
    work limits, exact sufficient budgets, shared fixture caps, canonical replay, missing reservation
    rejection, final two-group connectivity, and exclusion of all fixture cells from protected routes.
    It still verifies untouched source entity membership/snapshot identity and the main pipeline's
    allocation rejection. Neither geometric light coverage nor clean access is promoted to engine
    verification by this fixture.
    Two independent mutations fail at expected assertions: ignoring vault-required furniture produces
    zero blockers instead of one; resetting lighting expansion allowances per room accepts a complete
    aggregate that exceeds its total allowance. Both sources are restored in `finally` before final
    builds and suites. All 340 focused Debug procedural units pass, along with fifty-two loaded-server
    cases and the real client SandboxTest (53 combined), zero failures or skips. Warnings-enabled
    Server Release passes with 512 permitted warnings and zero errors. Sequential fresh Debug
    integration `--no-incremental --no-restore` passes with 1102 warnings and zero errors, followed
    by the final no-build suites. These remain focused gates rather than the repository's full suite.
    Scoped tracked diff and new-file whitespace checks pass. No event subscriptions, warning
    suppression or sandbox whitelist changes are introduced. Interior access identity is v1;
    owned partition/floor/floor selection remain v1, route/route selection v2, capability v3 and full
    geometry v20. R01-R06 stay in progress and R07 pending. Rough full-system completion is 51%
    (40-55% uncertainty). Main-pipeline integration, tactical ownership, joint content/light search,
    real doors/actors/materials, operational lighting and publication still require their exit gates.
  - [x] Add `KsProcgenGroupTacticalAnalyzer.Analyze` over a successful post-furniture/light interior
    scene. Independently revalidate the current request/configuration and compare scene identity;
    reconstruct ownership instead of trusting caller-supplied component labels. Reject cross-network
    terminal pairs, host/out-of-floor query cells and duplicate pair IDs before optional searches.
    RequiredConnections may measure pairs across group labels only when the validated local graph
    actually merges those groups. Exact DomainLocal groups remain separate even when host paths
    join them externally. RequestNetwork isolation still checks those bypasses during revalidation.
  - [x] Measure articulation cells, bridges and choke-side sizes per permitted local component.
    Side terminal counts include complete operational inside approaches and declared group/request
    roots; they do not enumerate engine interaction targets. Nominated choke removal and optional
    internally cell-disjoint route counts use only that component's post-interior clean cells.
    Foreign host paths cannot become optional detours. Measurement scope is explicitly DomainLocal,
    even when the source contract requires RequestNetwork isolation; full-host tactical metrics are
    still pending an ownership policy. Vision/projectile cover and tactical placement are separate.
  - [x] Share graph expansions across source revalidation, iterative movement DFS, component/side
    floods and named alternate-route BFS. Share side-detail, search and flow-edge allowances across
    all networks. The wrapper accepts at most 4096 clean cells, 64 networks/pairs/nominated chokes,
    512 detailed chokes, 65536 searches, one million graph expansions, eight million examined flow
    edges and eight independent routes per pair. A measured route count at the configured cap is
    a lower bound. Exhaustion discards all network outcomes/hash while preserving actual counters.
    Side-detail truncation retains every exact articulation/bridge and labels the omitted details.
  - [x] Make accepted movement/alternate/redundancy collections immutable and guard movement DFS
    and side floods against integer-overflow adjacency. Preserve alternate/flow failure counters;
    zero remaining work is a valid exhausted allowance. Removed endpoints require no BFS and do not
    consume search reservations. Canonical `ks-procgen-group-tactical-v1` identity includes scene,
    nominated pairs/chokes, actual selected detail cells and redundancy policy/cap. Input order,
    work counters and unused sufficient allowances do not change it. This is movement measurement
    identity, not full content identity or a live collision/door/actor proof.
  - Remaining after tactical ownership: integrate the complete entrance-aware branch into the main
    pipeline only after its material/hull/access dependencies are proven. Derive live scene and
    vision/projectile masks, define any full-host tactical ownership policy, and connect bounded
    tactical furnishing proposals to hard access/pressure revalidation. Keep the existing allocation
    guard until replacement gates pass. Joint furnishing/light search and request-wide fallback
    composition remain pending; a later optional analyzer cannot authorize an invalid interior.
  - Tactical ownership verification (2026-10-10): twelve new focused unit cases cover independent
    corridor chokes/bridges and entrance/root side counts; outside host bypasses and cross-network
    pair rejection; RequiredConnections joins across merged labels; exact shared graph/search/flow
    allowances and atomic failures; capped redundancy lower bounds; shared detail truncation;
    canonical replay/detached output; stale scenes and forged ownership labels; host/blocked query
    cells and duplicate IDs; 1x3 networks, removed endpoints and zero-work fallbacks; detailed flood
    accounting; and extreme-coordinate adjacency. The loaded 8x7 marker/office fixture continues
    through actual theme/furniture/light proposals to local tactical analysis, exact-allowance replay
    and foreign-pair rejection, retaining its source entity/snapshot checks. These remain declared
    geometric proposals; the fixture does not materialize furniture or prove actor/light operation.
    Three deliberate mutations fail at the expected assertions: resetting each network's graph
    allowance accepts over-budget results; unchecked neighbor arithmetic invents a bridge between
    opposite integer limits; ignoring side-flood limits accepts an exhausted detailed analysis.
    Every source is restored in `finally` before final gates. All 352 focused Debug procedural
    units and fifty-two loaded-server cases plus the real client SandboxTest (53 combined) pass,
    with zero failures/skips. Sequential warnings-enabled Server Release and fresh Debug integration
    `--no-incremental --no-restore` builds pass with 512/1102 permitted warnings respectively and
    zero errors, followed by final no-build suites. Scoped tracked diff/new-file whitespace checks
    pass. The four `_KsModule*` worktrees remain clean and match their existing `origin/main` refs
    (no fetch performed in this batch). No event subscriptions, warning suppression or sandbox
    whitelist edits are introduced. Tactical measurement identity is v1; owned interior/partition/
    floor/floor selection remain v1, route/selection v2, capability v3 and full geometry v20.
    R01-R06 remain in progress and R07 pending. Rough completion is 52% (40-55% uncertainty).
    Main-pipeline composition, full-host tactical ownership, live materials/actors/doors/lights,
    joint content/light search and publication still need their documented exit gates.
  - [x] Add `KsProcgenGroupMaterialPlanner.Plan` as the bounded entrance-aware material prerequisite.
    Independently revalidate the current request and selected partition scene, reconstruct floor
    ownership, and check complete writable target-wall disposition. Reject missing/duplicate floor
    ownership, foreign group metadata, missing protected routes, overlapping theme walls, incomplete
    network coverage and door thresholds/approaches outside their permitted local component. Door
    approaches must be opposite cardinal neighbors of a writable threshold; preserve their complete
    geometry in immutable output. No exterior sealing or operational door behavior is inferred.
  - [x] Resolve primary/accent tiles and each network's interior wall/door choices independently.
    Extend the legacy material planner with optional `protectedPrimaryCells`; default callers retain
    their original choices. Entrance-route and read-only floor cells cannot become accent candidates.
    Requested accent counts remain rounded from total themed floor; exclusions are reported as soft
    accent shortfalls. Omit all tile writes to preserved/constant floor. Require actual registered
    tile definitions and entity prototypes, including wall families selected for rooms without
    interior seams. A missing later door or prototype discards every earlier material choice.
  - [x] Flood complete cardinal wall components and inspect incident floor-region and existing
    themed-wall owners. Require identical interior-wall prototype IDs across every participating
    family, including junctions; this is a conservative compatibility policy, not proof that two
    different prototypes cannot work together. Reject conflicts atomically. Preserve authored seam
    ownership, then assign the remaining wall cells to the first canonical incident region. An
    unused wall island with no incident room uses the first canonical region's wall prototype and
    increments `UnattachedWallCells`; it stays wall and gains no invented floor access. The exposed
    `SeparationWalls` set includes every wall outside logical room seams, including these islands.
  - [x] Share actual scene-validation and wall-flood expansions, allowing zero exhausted work and
    at most one million expansions. Bound target geometry to 4096 cells, local networks to 64 and
    themed rooms to 256; legacy material selection CPU work is bounded by these input caps rather
    than included in the graph counter. Freeze accepted tiles/walls/doors/openings/separator masks.
    `ks-procgen-group-material-v1` includes source partition/scene, full door approach geometry,
    canonical prototype choices/owners, accent flags, separator classifications and fallback counts.
    Input enumeration order and unused sufficient work allowances do not change identity.
    `EngineMaterialsVerified` and `GasClosureVerified` remain false. Main-pipeline allocation guards
    stay in force until hull/window/materialization/actor/pressure gates can preserve the contract.
  - Owned material verification (2026-10-10): twelve new loaded-server cases exercise complete
    separated-network coverage; primary route tiles and full-accent shortfalls; canonical ordering,
    exact sufficient allowances and detached output; preserved floor and declared constant masks;
    interior door choices/immutable approach geometry and foreign approach rejection; missing later
    door material with atomic discard; incompatible separator families; shared scene/wall work
    exhaustion and zero-work failure; missing wall disposition and stale host context; foreign theme
    group labels/missing reservations; 1x3 passage and unattached wall-island fallback; and actual
    prototype-reference rechecks for a room without interior walls. Two small valid palette/wall-pack
    fixtures supply the accent and conflicting-family cases. A temporary missing entity reference is
    restored in `finally`. The existing live marker/office test now runs owned material planning
    alongside furnishing/light/tactical stages and retains source entity/snapshot identity checks.
    Three deliberate mutations fail at their expected assertions: emitting read-only floor choices
    rejects an otherwise valid preserved-floor plan; ignoring primary reservations turns both
    protected entrance tiles into accents; and disabling shared-wall compatibility accepts conflicting
    families. Each source is restored in `finally` before final gates. Warnings-enabled Server Release
    passes with 512 permitted warnings and zero errors. Sequential fresh Debug integration
    `--no-incremental --no-restore` passes with 1105 permitted warnings and zero errors. Final no-build
    runs pass all 352 focused procedural units and sixty-four loaded-server cases plus the real client
    SandboxTest (65 combined), zero failures/skips. These are focused gates, not the complete repository
    test suite. Scoped tracked diff and new-file whitespace checks pass; no event subscriptions,
    warning suppression or sandbox whitelist changes are introduced. Owned material identity is v1;
    full geometry remains v20. R01-R06 remain in progress and R07 pending. Rough full-system completion
    is 53% (40-55% uncertainty). Exterior hull/window choices, complete pipeline/fallback composition,
    joint content/light search, real wall/door placement, actor/pressure/lighting checks and publication
    still need their documented exit gates. Identical wall IDs here are a conservative planning policy,
    not verified airtightness, collision behavior or a playable room.
  - [x] Add `KsProcgenGroupHullBoundaryAnalyzer.Analyze` for the selected owned partition. Normalize
    the current request, independently reconstruct entrance-scene components and group ownership,
    compare scene identity, and require unique exact floor and complete writable target-wall
    disposition. Caller-supplied component/group labels cannot substitute for fresh graph analysis.
    Inventory every cardinal floor edge, including preserved/constant floor; distinguish target
    wall proposals, claimable envelope, operational/sealed ports, known clean host openings,
    movement-blocked context with unknown gas behavior, explicit void, preserved unknown geometry,
    missing footprint shell and unknown interior host context. No diagonal edge is invented.
  - [x] Reserve complete entrance threshold, inside and outside spans, known clean host and blocked
    threshold context before proposing any envelope wall. Handle thresholds both inside and outside
    target floor and every lane of a multi-cell port. Expose immutable exact claim/reservation masks;
    no claim overlaps an entrance, host passage, preserved cell or explicit void. An operational or
    sealed port remains unresolved for pressure: a traversable passage or movement blocker is not a
    gas-seal witness. `Candidate` means complete geometric inventory, not complete or airtight hull.
  - [x] Bound inventory inputs to 4096 target/floor/wall cells and 16384 envelope cells; share actual
    independent scene work and one four-neighbor inventory expansion per floor cell, allowing zero
    exhausted allowance and at most one million expansions. Fail atomically with empty edges/masks
    and zero identity. Freeze edges and copied ownership. `ks-procgen-group-hull-boundary-v1` includes
    fresh scene identity, geometry mode, canonical edges/kinds/ports/groups and envelope reservations.
    `GasClosureVerified` and `EngineBoundaryVerified` remain false, including known blockers.
  - [x] Add `KsProcgenGroupHullWallPlanner.Plan` as a candidate envelope-wall adapter. Revalidate
    interior materials first, then owned boundary geometry under the remaining shared graph budget.
    For each claimable envelope cell, require every incident themed floor region to nominate the
    same registered `HullWall` entity prototype; reject missing or conflicting families atomically,
    including one envelope hole touching otherwise separate entrance networks. Select one wall per
    claimed cell and assign the first canonical incident region as owner. Retain validated interior
    choices and the complete boundary inventory; a missing envelope produces no invented walls and
    retains unresolved edges. `Planned` means this candidate wall list only. Windows, unknown shell
    repair, actual collision, resting doors and gas closure are not certified by this result.
  - [x] Charge one additional selection expansion per envelope claim and freeze accepted wall
    choices. `ks-procgen-group-hull-walls-v1` binds interior material and boundary identities plus
    canonical hull cells/owners/prototype IDs. Input enumeration order and unused sufficient work
    allowances do not alter identity. Budget, source or later prototype incompatibility exposes no
    earlier material/boundary/wall artifacts. `WindowGoalVerified`, `GasClosureVerified` and
    `EngineMaterialsVerified` remain false. Keep main-pipeline entrance-aware allocation guards.
  - [ ] Compose owned structure/window adapters with complete request-wide planning, inspect actual
    hull/door/window gas and collision properties, and implement private materialization plus
    final actor/atmosphere validation before this adapter can contribute to publication. Shared
    identical hull IDs are a conservative planning rule, not proof of a sealed or playable room.
  - Owned hull implementation evidence (2026-10-10): twelve new pure boundary cases cover complete
    cardinal inventory, operational/sealed entrance reservations, known clean host and unknown
    blocked context, preserved/constant floor, explicit void/preserved non-floor, missing footprint
    versus unknown interior host, exact shared graph work and zero exhausted allowance, stale scene
    and missing target-wall disposition, forged component labels, detached immutable output and
    every lane of two-cell entrances with thresholds inside or outside target. Four new loaded-server
    cases cover exact envelope wall/prototype coverage with canonical replay, shared selection work
    exhaustion and missing later hull families, conflicting hull prototypes on a hole incident to
    two separate networks, and no invented shell when envelope authorization is absent. A valid
    alternate hull-family fixture has the same interior wall as the office but a different hull
    wall, so the conflict case reaches hull compatibility rather than an earlier material gate.
    The real painted-marker/office fixture now also audits four entrance edges and missing shell
    authorization, retaining entity and source snapshot identity checks after all planning stages.
    Three deliberate source mutations fail at their intended assertions: skipping read-only floor
    gives five boundary edges instead of eight; disabling shared hull-family compatibility returns
    `Planned` instead of `IncompatibleMaterials`; resetting envelope selection work returns `Planned`
    instead of `BudgetExceeded`. Each source is restored in `finally` before final fresh builds.
    Final warnings-enabled Server Release passes with 512 permitted warnings and zero errors;
    sequential fresh Debug integration `--no-incremental --no-restore` passes with 1107 permitted
    warnings and zero errors. Final no-build runs pass all 364 focused procedural units and
    sixty-eight loaded-server cases plus the real client SandboxTest (69 combined), no failures or
    skips. These are focused gates, not the full repository suite; unsuppressed CI warnings-as-errors
    remains unverified. Scoped tracked diff and new-file whitespace checks pass. No warning
    suppression, sandbox whitelist or event subscription changes were added. All four private
    KS modules are clean and have zero commits ahead/behind their locally recorded `origin/main`;
    this batch did not fetch remote refs. Owned hull boundary/wall identities are v1; full geometry
    remains v20. R01-R06 remain in progress and R07 pending. Rough full-system completion is 54%
    (40-55% uncertainty). Next rewrite work is owned exterior/window eligibility and complete
    request-wide composition; joint assembly/light search, real materialization, actor/pressure/
    lighting validation and publication still require their documented exit gates.
  - [x] Add `KsProcgenGroupWindowBoundaryAnalyzer.Analyze` and typed directional
    `KsProcgenExteriorWindowFact` snapshots. Independently revalidate the current owned hull before
    classifying cells. Facts nominate one structural cell and a cardinal outward face with clean
    domain floor immediately behind it; exterior probes cannot contradict target/envelope structure
    or known clean/blocked host context. Reject duplicate cells, diagonal/invalid normals, entrances,
    unknown/nonstructural positions and editable cells claimed as existing windows. Missing exterior
    context stays explicitly unclassified; absence of a tile is never treated as proof of space.
  - [x] Admit preserved/constant non-floor boundary cells as fixed walls/windows for counting only.
    Derive write permission from normalized ownership, never a caller-supplied fixed flag. Require
    declared existing windows to have supported exterior context. Cells incident to multiple floor
    faces are conservatively corners and excluded. Known structural cells without facts expose
    `UnclassifiedStructuralCells`; unknown/missing hull kinds also prevent `ExteriorScopeComplete`.
    Complete scope means this declared geometric classification, not engine inspection or closure.
    Accepted snapshots, classifications and masks are immutable. `EngineExteriorVerified` is false.
  - [x] Add `KsProcgenGroupWindowPlanner.Plan`: revalidate complete owned material/hull choices and
    independently revalidate window context under the remaining shared graph allowance, checking
    that the two hull identities agree. Resolve registered window prototypes from incident selected
    room wall families; missing/unsupported choices exclude editable cells from the denominator.
    Conservatively require identical window IDs if incident families participate. Preserve corners,
    required structure and author-disabled cells. Reuse stable legacy fraction rounding, fixed counts,
    minimum/maximum constraints, hard tolerance and soft clamping. A hard count/fraction miss discards
    every earlier artifact rather than returning partial structure or window writes.
  - [x] Emit exact `RemainingWalls` and `Windows` structural candidates: each editable chosen wall
    becomes one window, and no selected cell retains a wall choice. Fixed windows contribute achieved
    counts only and are never written. `SourceHull` is diagnostic pre-substitution input, not an
    additional materialization list. Keep entrance spans and read-only content protected. A valid
    plan with no declared exterior context emits no windows; an unmet positive hard minimum fails.
    `WindowGoalVerified`, `GasClosureVerified` and `EngineMaterialsVerified` stay false, even when
    provisional numeric targets match. Room/segment scope extensions and live substitution remain open.
  - [x] Bound face input to 16384 cells and all shared graph work to at most one million expansions;
    include hull source work, one structural classification visit per cell and one prototype visit
    per classified cell. Bounded fact validation/prototype lookups and deterministic counting/sorting
    are not graph expansions. Zero exhausted allowances are valid budget failures. Freeze legacy
    `KsProcgenWindowPlanner` selection/exclusion lists too, preserving its existing choice semantics.
    `ks-procgen-group-window-boundary-v1` hashes hull identity and complete canonical directional fact
    snapshots; `ks-procgen-group-windows-v1` binds owned wall/context identities, seed, exact goal,
    supported/unavailable cells, all eligible prototype IDs, counts and final wall/window choices.
    Fact/theme enumeration order and unused sufficient allowances do not affect semantic identity.
  - Owned window implementation evidence (2026-10-10): ten new pure cases exercise directional
    exterior eligibility, explicit missing context/entrance reservations, fixed preserved/constant
    windows, malformed/duplicate/foreign/contradictory faces, known host probes, complete non-exterior
    scope and canonical detached identity, shared classification work/zero allowance, missing shell
    scope, and concave corner exclusion versus a face pointing through another floor lane. Seven
    new loaded-server cases exercise exact wall-to-window substitution and replay, immutable selection
    lists, missing later window families and atomic hard-count misses, absent exterior context,
    required/disabled/unsupported cells, fixed preserved/constant windows with hard versus soft
    fraction behavior, exact shared prototype allowance, duplicate facts and stale source rejection.
    A valid wall-pack fixture supplies hull/interior walls but intentionally has no window choice.
    The live marker/office fixture now also runs the owned window adapter with no exterior declaration,
    checks that no windows or complete exterior scope are invented, and retains its final source
    entity/snapshot identity assertions. This stage consumes caller-declared geometry/support facts;
    it does not inspect actual station exposure, existing window entities or gas/collision behavior.
    Four deliberate source mutations fail at their intended assertions: allowing an exterior probe
    through known host floor returns `Candidate` instead of `InvalidInput`; making fixed walls
    editable loses preserved/constant ownership; keeping selected wall choices adds nine duplicate
    structural writes; and resetting prototype work returns `Planned` instead of `BudgetExceeded`.
    Every source is restored in `finally` before fresh final gates. An initial host mutation that
    broke nullable-flow compilation was corrected and rerun to produce the intended assertion
    failure; a build failure alone is not treated as regression evidence.
    Final warnings-enabled Server Release passes with 512 permitted warnings and zero errors.
    Sequential fresh Debug integration `--no-incremental --no-restore` passes with 1107 permitted
    warnings and zero errors. Final no-build suites pass all 374 focused procedural units and
    seventy-five loaded-server cases plus the real client SandboxTest (76 combined), no failures
    or skips. These are focused gates, not the full repository suite; unsuppressed CI
    warnings-as-errors remains unverified. Scoped tracked diff and new-file whitespace checks pass.
    No warning suppression, event subscription or sandbox whitelist changes were introduced.
    All four private KS modules are clean and zero commits ahead/behind their locally recorded
    `origin/main`; no remote fetch was performed. Owned window boundary/proposal identities are v1,
    and existing full geometry identity stays v20 because legacy choice semantics are unchanged.
    R01-R06 remain in progress and R07 pending. Rough full-system completion is 55% (40-60%
    uncertainty). Next rewrite work is complete request-wide stage composition and fallback/report
    ownership, followed by joint assembly/lighting search and actual materialization/actor/pressure/
    light validation. Candidate prototype choices and matching counts do not complete those gates.
  - [x] Add `KsProcgenOwnedInteriorPlanner.Plan` as an atomic composition adapter for procedural
    footprints with normalized entrance contracts, including read-only constant reservations.
    Select a complete floor configuration, partition its owned networks, assign their themes,
    resolve exact materials/hull/window choices, furnish each room, select lights, and independently
    audit the complete post-content entrance scene. Accept only a complete `Candidate`; on any
    failure return no floor, partition, theme, structure, furnishing, lighting or access artifacts
    and no semantic identity. Typed stage outcomes retain actual reported work and the failure
    reason. These outcomes describe unpublished attempts; they are never partially applied maps.
  - [x] Share remaining reported graph expansions across floor, partition, structure, relational
    furnishing paths, lighting and the final independent audit. Share configuration/order/merge,
    furnishing candidate and fixture allowances across their relevant stages and owned networks.
    Expose `GraphWorkFullyMetered = false`: legacy furnishing clean-walk/placement and partition
    seam CPU work remain unmetered, bounded by input/room/probe limits. Do not reinterpret the
    reported graph counter as a complete CPU bound. Adapter limits include 4096 target cells,
    64 size bands, 512 content-room cells, and the existing stage-specific caps. Zero reported
    graph/configuration allowance is a bounded failure. An exhausted furnishing allowance stops
    before any further room, even if that room might have needed no candidates.
  - [x] Preserve work on furnishing search failures, including candidate probes, relational-path
    expansions/truncation and mandatory backtracks. Add an optional named relational path allowance
    while keeping the legacy default of 4096. Freeze accepted entity/omission/witness lists,
    occupied footprints, ordered path values and owned theme support-pack IDs so a caller cannot
    cast those stored snapshots back to mutable arrays/lists. Value comparison stays unchanged.
  - [x] Reserve every read-only floor cell before furnishing and lighting, in addition to entrance
    routes and interaction approaches. Use post-window `RemainingWalls` for backing-wall context;
    remove substituted windows from local partition wall inputs as well. Preserve original owned
    entrance reservations for the independent final ownership/access audit. No allocations, tile
    writes, marker edits or publication occur in this adapter.
  - [x] Apply all four request fallback permissions to actual merged partitions, size-count
    deviations, sparse furnishings and sparse lights. Report allowed outcomes with their network,
    room or size-goal ID. A denied outcome has its own incomplete journal entry and stable reason,
    is not listed as an applied fallback, and discards every stage artifact. Earlier allowed
    outcomes in a failed attempt remain diagnostic events, not published changes.
  - [x] Add owned interior semantic identity v1, binding request/seed, floor selection, partition,
    exact post-window structure, selected theme/packs/density, required pack minima, actual entities
    and rotations/footprints/approaches, relational witnesses and ordered paths, light choices and
    estimates, original size-band bounds and achieved counts, permissions and fallback outcomes.
    Canonical input permutations and unused sufficient allowances preserve the identity. Work
    counters are diagnostics. Existing full geometry identity stays v20; legacy selection semantics
    and entrance-aware packing/tile-stage rejection guards remain intact.
  - Verification (owned composition batch, 2026-10-10): two new focused unit cases prove copied,
    immutable occupied footprints and ordered relational paths. Sixteen loaded coordinator cases
    cover complete separated networks, canonical/exact-allowance replay, independent final-audit
    exhaustion, shared fixture exhaustion, preserved and constant floor, allowed/denied sparse
    furnishing, size-band identity and denied size deviations, a connected 1x3 passage, zero graph
    and configuration allowances, unsupported hybrid/interior-fill requests, missing themes,
    impossible hard window counts, positive exterior substitution, failed mandatory probe counts,
    and required `Near` relations consuming the remaining request graph allowance. Extend the real
    painted-marker fixture to compare complete composition to standalone stage results and retain
    its unchanged marker snapshot/entity inventory. These establish geometric planning only.
    Four deliberate mutations each produce the intended assertion failure: resetting the remaining
    graph budget yields `Candidate` instead of `BudgetExceeded`; bypassing sparse permission yields
    `Candidate` instead of `FallbackDisallowed`; erasing failed furnishing work reports zero probes
    instead of one; and exposing a path array fails the immutable-snapshot assertion. Restore all
    mutated sources in `finally` before final builds. No compiler error counts as mutation evidence.
    Final warnings-enabled Server Release passes with 512 existing permitted warnings and zero
    errors. Sequential fresh Debug integration `--no-incremental --no-restore` passes with 1107
    existing permitted warnings and zero errors. Final no-build gates pass 376 focused procedural
    units and ninety-one loaded-server cases plus the real client SandboxTest (92 combined), no
    failures or skips. These are focused gates; the full repository suite and unsuppressed CI
    warnings-as-errors remain unverified. Scoped tracked diff and new-file whitespace checks pass.
    No warning suppression, event subscription or sandbox whitelist change was introduced. All
    four private KS modules are clean and zero commits ahead/behind their locally recorded
    `origin/main`; no remote fetch was performed. R01-R06 remain in progress and R07 pending.
    Rough full-system completion is 57% (40-60% uncertainty). Next rewrite work is joint request
    search and complete work metering, followed by actual layer-aware materialization and actor,
    pressure, lighting and publication validation.
  - [x] Extend `KsProcgenOwnedInteriorPlanner.Plan` to seeded complete-configuration retry through
    all interior stages. A rejected floor/content proposal or denied fallback advances to the
    next positive-weight declared configuration; an invalid input or work exhaustion is terminal.
    Every attempt owns fresh floor, partition, material, furnishing and lighting proposals. No
    placement artifact from a rejected configuration is reusable in another alternative. A final
    miss preserves a specific reason and exposes only diagnostics, never an interior or hash.
  - [x] Add immutable `KsProcgenOwnedConfigurationAttempt` records with configuration IDs, ordinal,
    status/reason, actual work and attempt-local fallback traces. Tag every stage row with its
    configuration ID. Share remaining reported expansions, order probes, partition merges,
    furnishing probes and provisional fixture selections across discarded and accepted attempts;
    failed attempts cannot refund work. Result counters and stage journals cover the whole search.
    Successful result fallback events belong to the accepted attempt; discarded allowed outcomes
    remain in that attempt's trace. A refusal before another configuration consumes no attempt.
  - [x] Version owned interior identity to v2 for complete configuration search. Hash selected
    geometry/content/contract outcomes; diagnostic attempts, spent work and unused sufficient
    allowances are not output semantics. Reuse the unchanged owned floor selection v1 hash helper.
    Existing full geometry remains v20 and legacy entrance-aware publication guards remain.
  - Verification (complete configuration search, 2026-10-11): thirteen added loaded cases cover
    a geometrically valid first floor rejected for mandatory contents, a rejected floor followed
    by a viable configuration, disabled alternatives, denied size deviation followed by an
    allowed candidate, exhausted alternatives, malformed exterior context, canonical/exact-budget
    replay, shared configuration and order limits, late final-audit exhaustion after retry, and
    discarded furnishings/lights consuming the same global allowances. The latter fixture fails
    at sparse-light admission after selecting real prototype candidates, then accepts a joined
    configuration without carrying forward its rejected fallback events or placements. Exact
    allowances replay its accepted identity; exhausted furnishing or fixture work stops before
    unfunded content. Attempt records are frozen and remain intact after source declarations clear.
    Four deliberate mutations produce assertion failures: stopping on the first rejected attempt
    yields `Rejected` instead of `Candidate`; refunding graph or order work yields `Candidate`
    instead of `BudgetExceeded`; refunding fixtures selects four provisional lights against an
    allowance of two. Restore every source in `finally`. The initial fixture mutation was already
    caught by the changed early/late failure code; move the work-count assertion ahead of that code
    check and rerun to verify the actual overconsumption directly. No compilation failure is used
    as regression evidence. Final warnings-enabled Server Release passes with 512 existing
    permitted warnings and zero errors. Sequential fresh Debug integration
    `--no-incremental --no-restore` passes with 1107 existing permitted warnings and zero errors.
    Final no-build suites pass all 376 focused procedural units and 104 loaded-server cases plus
    the actual client SandboxTest (105 combined), no failures or skips. Scoped tracked diff and
    new-file whitespace checks pass. No warning suppression, event subscription or sandbox
    whitelist change was introduced. The full repository suite and unsuppressed CI
    warnings-as-errors remain unverified. All four private KS modules are clean and zero commits
    ahead/behind their locally recorded `origin/main`; no remote fetch was performed. R01-R06
    remain in progress and R07 pending. Rough full-system completion is 58% (40-60% uncertainty).
  - [x] Meter every ordinary clean-passage search used by the owned floor furnishing stage:
    terminal connectivity, machine rotation/ranking and front-edge alternatives, chair approaches,
    declared operating landings, complete trial validation and accepted walkway reservations.
    Extend `KsProcgenRelationPathBudget` with opt-in clean-path accounting and separate relation/
    clean expansion and truncation diagnostics. Share one total allowance across both kinds of
    query and all placement/mandatory continuation branches. Count every dequeued node, including
    a root that is already the goal. An invalid endpoint requires no graph work; a fully exhausted
    queue is a proven miss rather than a truncation, including at the exact allowance.
  - [x] Return explicit machine/chair `BudgetExceeded` results without earlier successful options
    when a later query exhausts the allowance. Propagate that stop through floor assembly search
    and mandatory continuations. Furnishing returns `FurnishingCleanPathBudget` with actual work
    but no entities, protected walkways or relational witnesses. A failed optional/density search
    cannot silently convert a clean-path budget stop into sparse admission. Preserve relation-only
    standalone behavior by default; the owned coordinator explicitly opts in and journals the sum
    of both expansion counters against its remaining request budget, including discarded attempts.
    Keep owned interior identity v2, floor selection v1, full geometry v20 and publication guards.
    Accounting diagnostics do not change a fully funded candidate's semantic identity.
  - Verification (clean-passage accounting, 2026-10-11): eleven new focused unit cases cover
    zero/exact/insufficient allowances, goal charging, repeated queries sharing relation work,
    disconnected and diagonal cells, vault-only blockers, invalid endpoints, atomic chair/machine
    exhaustion, discarded earlier facing options, canonical multi-root accounting and legacy
    relation-only compatibility. Four added loaded cases cover office and required `Near` contents
    with unchanged fully funded placements, exact accounting replay, zero allowance before a
    placement probe, mandatory backtracking and a final walkway reservation one expansion short,
    and owned request accounting/exhaustion without artifacts. Update the existing zero remaining
    request allowance case: terminal connectivity now exhausts first, before required `Near` work,
    with `FurnishingCleanPathBudget`; fully funded relation witnesses are still independently
    checked. All eleven focused units and thirty-three furnishing/coordinator cases pass.
    Three deliberate mutations fail at the intended assertions: disabling clean path charging
    returns a path against zero/two expansions; removing the facing/chair budget returns `Ready`
    instead of `BudgetExceeded`; disabling the coordinator opt-in reports zero clean-path work.
    Restore each source's original bytes in `finally`. Compilation failures are not mutation
    evidence. Final restored-source Server Release passes with 512 existing permitted warnings
    and zero errors. Sequential fresh Debug integration `--no-incremental --no-restore` passes
    with 1107 existing permitted warnings and zero errors. The focused Debug unit suite passes
    all 387 cases; the final no-build integration suite passes 108 loaded-server cases plus the
    real client SandboxTest (109 combined), without failures or skips. An initial overlapping
    Debug build attempt hit a compiler output file lock; rerun builds sequentially, and use only
    the successful restored-source final gates as evidence. Scoped diff/new-file whitespace checks
    pass. No warning suppression, event subscription or sandbox whitelist change was introduced.
    All four private KS modules are clean and zero commits ahead/behind their locally recorded
    `origin/main`; no remote fetch was performed. The full repository suite and unsuppressed CI
    warnings-as-errors remain unverified. R01-R06 remain in progress and R07 pending. Rough
    full-system completion is 59% (40-60% uncertainty). Next rewrite work is partition seam and
    non-graph placement accounting, alternate layout search within a configuration, diagnostics
    serialization, then layer-aware materialization and engine validation before publication.
  - [x] Add `KsProcgenPartitionWorkBudget` for connectivity floods and seam probes, shared across
    all networks in one owned partition proposal. Charge zone input validation, remaining room
    connectivity, original island discovery and final floor connectivity one dequeued cell at a
    time. Charge ownership scans/reconstruction, each directed cardinal interface query, each
    interface edge, door candidate and accepted-door approach check one seam probe at a time.
    Retain work spent on rejected splits and repeated rebuilds; graph/seam counters describe
    separate operations. Standalone callers opt in; metered inputs are capped at 256 zones and
    65536 declared cells, while the owned adapter retains its tighter 4096-cell limit.
  - [x] Return partition `BudgetExceeded` with the specific graph/seam reason and work deltas,
    without floor, walls, doors or merged membership. Distinguish actual `MergeAttempts` from
    rejected splits; merge-cap fallback still behaves as declared, while graph/probe exhaustion
    cannot become an open fallback. Freeze per-network work diagnostics with owned artifacts.
    Meter authored zone connectivity through the remaining request graph allowance as well.
  - [x] Share `MaximumPartitionSeamProbes` across complete configuration retries. Preserve spent
    work in immutable attempt/stage journals and root results. Exact sufficient allowances retain
    semantic identity; unused allowances and work counters are diagnostics. Reject invalid seam
    limits before work. Keep `GraphWorkFullyMetered` false pending remaining accounting.
  - [x] Reject partition `BudgetExceeded` in downstream theme, material, furnishing and abstract
    port-network planners, including forged owned network artifacts. Extend the legacy geometry
    pipeline's status mapping for the new failure without enabling entrance-aware legacy packing
    or publication. Preserve existing hashes and successful legacy partition behavior.
  - Verification (partition accounting, 2026-10-11): thirteen added unit cases establish explicit
    tiny-passage costs (four connectivity passes, six seam probes per cell), independent zero/
    exact/insufficient graph and seam limits, monotonic work across two planner calls, failed split
    rebuild work, actual merge counts versus rejected split diagnostics, unchanged legacy doors/
    walls/membership, invalid-input work and exact-budget owned network replay. Authored-zone
    validation and the seam solver share the graph allowance; a later failure exposes no earlier
    network artifact. Five added loaded cases cover rejected complete configurations retaining
    seam costs, exact request replay, graph exhaustion inside seam validation, invalid seam limits
    before any attempt, and forged exhausted partitions rejected by theme, material, furnishing
    and owned material adapters. Initial focused gates pass 21 partition units and 35 coordinator
    cases. An initial test compilation needed explicit `KsProcgenZone` construction for the single
    `params` argument; fix it before recording passing gates.
    Four deliberate mutations produce assertion failures: allowing one extra graph expansion
    reports one/twelve cells against zero/eleven allowances; skipping directed boundary probe
    accounting reports six probes where seventeen/eighteen are required; refunding seam work
    across configurations consumes 324 probes against an allowance of 323; bypassing furnishing's
    exhausted-partition guard returns `Proposed` instead of `InvalidInput`. Restore original source
    bytes in `finally` for every mutation. No compilation failure is used as mutation evidence.
  - [x] Complete restored-source Release/fresh Debug builds, full focused procedural regressions
    and the actual client sandbox gate. Final Server Release passes with 265 existing permitted
    warnings and zero errors (the initial Server Release emitted 512; the final incremental build
    rebuilt fewer projects). Sequential fresh Debug integration `--no-incremental --no-restore`
    passes with 1107 existing permitted warnings and zero errors. All 400 focused procedural
    Debug unit cases pass. Final no-build integration passes 113 loaded-server cases and the real
    client SandboxTest (114 combined), with no failures or skips. Scoped tracked diff and new-file
    whitespace checks pass. No warning suppression, event subscription or sandbox whitelist
    change was introduced. All four private KS modules are clean and zero commits ahead/behind
    their locally recorded `origin/main`; no remote fetch was performed. The full repository suite
    and unsuppressed CI warnings-as-errors remain unverified. R01-R06 remain in progress and R07
    pending. Rough full-system completion is 60% (40-65% uncertainty). Next rewrite work is full
    normalization/allocation/sorting/content accounting, alternate layout search within one
    configuration and serialized replay diagnostics, followed by layer-aware materialization and
    actual actor, support, lighting, pressure and publication validation.
  - [x] Add bounded subdivision retries inside each entrance configuration (2026-10-11).
    `KsProcgenOwnedPlanBudgets.AlternativeMaximumRoomCells` supplies up to 63 distinct ordered
    sizes after `PreferredMaximumRoomCells`. Each size must be between `MinimumRoomCells` and
    4096, and cannot duplicate another size or the preferred size. Empty alternatives retain the
    prior one-subdivision behavior. Marker/profile authoring now supplies these preferences through
    `roomSubdivision`, as detailed below. These sizes are preferences passed to the existing automatic zoning
    and seam solver, not guaranteed final room areas or exhaustive partition enumeration.
  - [x] Separate configuration and layout allowances. `MaximumConfigurationProbes` counts
    entrance configurations actually entered; new `MaximumLayoutProbes` (default 64, range 0-64)
    counts complete pipeline attempts across every configuration and subdivision. A cap stops
    with `OwnedLayoutProbeBudget`, diagnostics only, and no reusable artifacts. Invalid limits,
    null/oversized/duplicate/out-of-range subdivision lists fail before an attempt. A zero layout
    allowance consumes no configuration probe. Disabled entrance configurations consume neither.
  - [x] On a downstream rejection or denied fallback, recompute a fresh complete proposal with
    the next subdivision preference. Retain spent graph expansions, order/seam/merge probes,
    furnishing probes and provisional light selections in the request-wide ledgers. Input errors
    and budget exhaustion remain terminal. Floor rejection advances directly to the next entrance
    configuration because subdivision cannot repair it. The first complete permitted candidate
    wins; allowed degraded candidates are not superseded by later alternatives.
  - [x] Journal the selected subdivision preference and global layout ordinal per stage/attempt,
    and expose separate `ConfigurationProbes` and `LayoutProbes`. Failed attempts expose only
    immutable diagnostics; final artifacts and fallbacks come solely from the accepted layout.
    Preserve the existing accepted-candidate semantic hash: unused preferences, failed attempts
    and work allowances do not change identical final geometry/content. A direct accepted layout
    and the same layout reached through rejection must have identical semantic identity.
  - [x] Complete restored-source verification for subdivision retries: loaded refusal of a
    merged small-room subdivision followed by acceptance of a larger subdivision in the same
    configuration, shared seam costs and exact-budget replay, layout/configuration cap separation,
    floor-failure skipping, invalid profile admission, and zero-work cap handling. Prove retry and
    budget guards with deliberate mutations, then run Release, fresh Debug, focused procedural
    regressions and the actual client SandboxTest before recording final counts.
    Fourteen new loaded cases pass: rejected small-room merge fallback followed by an accepted
    larger layout under one configuration probe; ordered 4/8/64 preferences with unused 128;
    exact graph/seam/order/layout replay; retained seam costs and atomic one-probe-short failure;
    independent layout/configuration caps across configurations; redundant subdivision skips on
    floor failure; permitted merged fallback stopping before alternatives; zero-layout admission;
    invalid negative/oversized caps and null/duplicate/preferred-size/undersized/oversized/too-many
    profile lists. Direct and retried accepted layouts have identical semantic/partition hashes.
    The focused coordinator suite passes all 49 cases. Two deliberate restored-byte mutations
    produce assertion failures: treating denied fallback as terminal returns `FallbackDisallowed`
    where `Candidate` is required; permitting one attempt beyond the layout cap returns `Candidate`
    where `BudgetExceeded` is required for both zero and one-attempt limits. Both mutation builds
    succeed; no compiler failure is counted as test evidence. Restore source in `finally` and
    rebuild before recording regression gates.
  - [x] Finish subdivision-search gates (2026-10-11). Server Release passes with 512 existing
    permitted warnings and zero errors. Restored-source fresh Debug integration build with
    `--no-incremental --no-restore` passes with 1107 existing permitted warnings and zero errors.
    All 400 focused procedural unit cases pass. Final no-build integration passes 127 loaded
    cases plus the actual client SandboxTest (128 combined), with no failures or skips. Scoped
    whitespace checks pass. No event subscription, warning suppression or sandbox whitelist
    changes were introduced. All four KS modules remain clean and zero commits ahead/behind
    locally recorded `origin/main`; no remote fetch was performed. R01-R06 remain in progress,
    R07 pending. Rough full-system completion is 61% (40-65% uncertainty). The complete repository
    suite and unsuppressed CI warning gates remain unverified. Next rewrite work is marker/profile
    transport and automatic size-goal subdivision strategies, alternative route/content search,
    full work accounting and replay serialization, then staged engine validation/publication.
  - [x] Serialize opt-in `KsProcgenRoomSubdivisionSpec` under area profile `roomSubdivision`:
    `minimumRoomCells` (default 4), `preferredMaximumRoomCells` (default 24) and ordered
    `alternativeMaximumRoomCells` (default empty). Reuse one bounded validity predicate with the
    owned request planner, so authoring and planning reject the same size/list errors. Reject
    declared subdivisions for unsupported hybrid/interior-fill profiles explicitly. Profile omission
    retains caller preferences, including legacy profiles and tiny passage behavior.
  - [x] Validate live profile definitions and snapshot an immutable subdivision value. Copy again
    when normalizing caller-built profile values, so source lists and live prototype edits cannot
    mutate prior blobs. Hash subdivision presence, minimum, preferred and ordered alternative sizes
    under area snapshot v3. Preserve geometry-derived area IDs v1 and accepted interior hashes v2.
    Profile preference/order changes invalidate input snapshots without renaming the painted area.
  - [x] Add `blob.CreateOwnedBudgets` and an explicit owned planner blob overload. Authored size
    values override only the three room-sizing preferences; every graph/layout/configuration/order/
    merge/seam/furnishing/lighting/content cap remains caller controlled. The overload uses the
    profile theme and a copied request; forged invalid subdivision values return `InvalidInput`
    before work. Planning and snapshotting allocate no engine interior and preserve markers.
    Provide `KsProcgenOfficeSubdivisionArea` as a loaded YAML example. Keep explicit request
    construction available for custom size goals and fallback permissions; those declarations
    are not yet transported by area profiles.
  - [x] Finish subdivision-authoring verification: detached size/order/hash tests, caller-cap
    preservation and invalid serialized/value profile tests; loaded YAML/paint-to-retry coverage,
    live profile edit isolation and explicit blob planning; deliberate copy/hash/transport mutations;
    restored Release, fresh Debug, focused procedural tests and the actual client SandboxTest.
    Thirteen added unit cases pass: immutable ordered snapshot sizes, order/minimum/preferred/
    presence hash changes with stable blob IDs, unchanged caller work caps, legacy profile
    omission, detached serialized definitions, eight invalid definition/value variants, and two
    unsupported profile modes. Two new loaded cases deserialize the YAML example, paint a grid,
    feed 4/8/64 sizes into complete retries with a required 30-cell room, compare explicit blob/
    request planning identity, retain old snapshots across live prototype edits, reject malformed
    live profiles and forged null-alternative blobs, and prove unchanged map entity sets. Initial
    focused gates pass 13 new units and 64 combined area/coordinator loaded cases.
    Three deliberate mutations compile and produce assertion failures: retaining the caller's
    mutable list changes a prior snapshot's first size from 8 to 64; sorting alternative sizes only
    while hashing makes two different orders share one snapshot hash; dropping the live profile
    subdivision during transport makes the end-to-end planner use caller size 128 instead of
    authored size 4. Restore each source file's exact original bytes in `finally`. The first fresh
    Debug build exposes two new literal prototype-ID analyzer warnings in the added tests; use
    local ID variables to resolve them, then repeat the fresh build without suppressing warnings.
  - [x] Finish restored subdivision-authoring gates (2026-10-11). Server Release passes with
    512 existing permitted warnings and zero errors. Final fresh Debug integration build with
    `--no-incremental --no-restore` passes with 1107 existing permitted warnings and zero errors.
    All 413 focused procedural unit cases pass. Final no-build integration passes 129 loaded
    cases and the actual client SandboxTest (130 combined), with no failures or skips. Scoped
    tracked/new-file whitespace checks pass. No event subscriptions, warning suppressions,
    dependency changes or sandbox whitelist edits were introduced. All four KS modules remain
    clean and zero commits ahead/behind locally recorded `origin/main`; no remote fetch was
    performed. R01-R06 remain in progress and R07 pending. Rough full-system completion is 62%
    (40-65% uncertainty). Full repository tests and unsuppressed CI warning gates remain unverified.
    Next rewrite work is authored goals/fallback transport, automatic size-goal subdivision,
    alternate route/content search, complete work accounting and replay serialization, followed
    by staged engine validation/publication.
  - [x] Add area-profile `generationGoals` authoring for room-size bands, four existing fallback
    permissions and window targets. Share goal validation with the geometry request normalizer
    without constructing dummy geometry. Preserve existing request reason codes and supported
    ranges; cap authored size bands at 64 with bounded unique IDs. Reject null nested definitions,
    invalid window fields/counts, overlapping bands and excessive total room counts atomically.
  - [x] Snapshot immutable size/policy/window values, canonicalize goal order by ID, and reconstruct
    fresh request objects on each blob request. Independently freeze caller-built profile lists.
    Hash every declared field under area snapshot v4; preserve blob IDs v1 and accepted owned
    interior hash v2. Validate forged malformed goal values before explicit blob planning. Omitted
    goals preserve default request behavior. Keep all caller work caps and engine/publication
    permissions outside authored goal data.
  - [x] Add `KsProcgenWholeOfficeArea` YAML example combining subdivision preferences, a required
    final 30-cell room and denied merge/size fallbacks. The explicit blob planner can retry complete
    layouts from these declarations without manual request edits. Live policy changes affect future
    snapshots only; minimum window counts can reject complete candidates at the structure stage.
  - [x] Finish goal-authoring verification: canonical replay, deep request/profile detachment, all
    fallback/window/size hash fields, invalid authored/value admission, legacy defaults, loaded
    paint-to-goal retries and window refusal. Prove critical transport/hash guards with deliberate
    mutations and restore source bytes; run Release, fresh Debug, focused procedural regressions
    and the actual client sandbox before recording final counts.
    Twenty-two added unit cases cover immutable authored/value lists, fresh request objects,
    canonical goal permutations, every size/policy/window hash field, independent permission
    transport, absent/default blocks, omitted versus explicit zero window maximums, thirteen
    invalid authored variants and forged invalid value profiles. Existing request geometry cases
    verify the extracted validator's behavior. Initial focused units pass 35 cases (21 new goals
    plus 14 existing geometry cases); the final full run also includes the added nullable maximum
    case. Two new loaded cases exercise the YAML profile and painted 30-cell blob, automatic
    4/8/64 retries without request edits, required final size counts, permitted fallback stopping
    at the first candidate after live edits, old-snapshot replay, impossible minimum-window
    refusal at the structure stage, malformed live/forged goals and unchanged map entity sets.
    Initial area/coordinator loaded gates pass 66 cases.
    Four deliberate mutations compile and fail assertions: cross-wiring sparse lighting to the
    furnishing permission breaks the two independently toggled permissions; dropping minimum
    window counts from metadata hashes makes changed goals collide; dropping nullable maximum
    presence makes unlimited and explicit zero collide; dropping generation goals during live
    snapshot transport accepts size 4 alone where the declared 4/8/64 retry is required. Every
    mutation restores exact source bytes in `finally`; no compiler failure is used as evidence.
  - [x] Complete restored goal-authoring gates (2026-10-11). Server Release passes with 512
    existing permitted warnings and zero errors. Final fresh Debug integration build with
    `--no-incremental --no-restore` passes with 1107 existing permitted warnings and zero errors.
    All 435 focused procedural unit cases pass. Final no-build integration passes 131 loaded
    cases and the real client SandboxTest (132 combined), with no failures or skips. Scoped
    tracked/new-file whitespace checks pass. No event subscriptions, warning suppressions,
    dependency changes or sandbox whitelist edits were introduced. All four KS modules remain
    clean and zero commits ahead/behind locally recorded `origin/main`; no remote fetch was
    performed. R01-R06 remain in progress and R07 pending. Rough full-system completion is 63%
    (40-65% uncertainty). Full repository tests and unsuppressed CI warning gates remain unverified.
    Next rewrite work is automatic size-goal-driven subdivision, joint route/content alternatives,
    complete work accounting and replay serialization, followed by staged engine validation and
    publication. Profile authoring now transports the goals and fallback permissions needed to
    exercise that search directly.
  - [x] Add opt-in simultaneous size-goal subdivision (2026-10-11). Extend serialized
    `KsProcgenRoomSubdivisionSpec`, immutable profile snapshots, `CreateOwnedBudgets` and request
    budgets with `UseSizeGoals`. Hash the authored flag under area snapshot v5, preserving blob
    IDs v1 and accepted interior identity v2. The authored preference overrides the caller's
    preference while all caller work caps survive. Default false preserves earlier scalar retries.
  - [x] Propose bounded rectangular strips from final floor-size bands, accounting for one
    tile-thick seam minus the doorway for every room after the first. Try X/Y fitting, inclusive
    width intervals, canonical spare-column distribution and at most 256 requested rooms. Require
    two columns for door approaches and complete rectangle coverage. Pad zone indices so wall
    ownership remains spatially consistent beyond ten rooms. Irregular/tiny/multiple-network
    shapes and infeasible quotas retain ordinary zoning. Explicit zones bypass this strategy.
  - [x] Integrate goal subdivision as the first complete candidate before scalar alternatives.
    Count configurations once even when the goal and first scalar attempts share a size ceiling.
    Retain shared nonrefundable work and independent seam/ownership/content/access validation.
    Charge inspected witness cells even on fitting failure. Add attempt/stage strategy diagnostics
    and a partition flag distinguishing requested goals from actual fitted zones. Protected or
    constant floor cannot become a seam; a denied candidate exposes no reusable placement artifacts.
  - [x] Add `KsProcgenMixedOfficeArea` as a loaded authoring example. A painted 9x4 blob with an
    entrance produces final 16/9/5 rooms and two cardinal doors, then passes theme, structure,
    furniture, lighting and independent abstract access checks without request edits or map writes.
    A protected whole-floor fixture refuses goal seams and retries the scalar strategy without
    refunding work; its accepted semantic identity matches direct scalar planning.
  - [x] Add size-goal regressions: horizontal/vertical rectangles, stable input permutations,
    exact graph/seam limits, atomic shortfall, inclusive band expansion, twelve-room wall ownership,
    quotas of 257/4096, malformed fitting, irregular outlines, explicit zones and separate network
    isolation. Profile tests cover immutable flags, snapshot hash changes and authored precedence.
    Focused units pass 20 cases initially; the extended full procedural unit run passes 446 cases.
    Two added loaded cases pass, including exact complete-pipeline budget replay and retry diagnostics.
  - [x] Prove size-goal regressions with four deliberate, compiling source mutations. Removing
    wall loss makes both rectangle orientations stop using the goal strategy. Removing ordinal
    padding makes the twelve-room fixture produce a six-cell room where nine is required.
    Dropping the strategy bit makes enabled/disabled authored profiles collide in snapshot hash.
    Dropping the authored flag in `CreateOwnedBudgets` makes the painted profile return
    `FallbackDisallowed` instead of `Candidate`. All four runs fail assertions, not compilation;
    restore exact original source bytes in `finally` after each mutation. Diagnostic logs are
    `.ks_procgen_sizegoals_mutation_{WallLoss,OrdinalOwnership,StrategyHash,StrategyTransport}.log`.
  - [x] Complete restored size-goal gates (2026-10-11). Server Release passes with 512 existing
    permitted warnings and zero errors. Fresh Debug integration build with `--no-incremental`
    passes with 1107 existing permitted warnings and zero errors. Rebuilt procedural units pass
    all 446 cases; final no-build integration passes 133 loaded cases and the actual real client
    SandboxTest (134 combined), with no failures or skips. No new warnings in the changed size-goal
    sources, warning suppressions, subscriptions, dependencies or sandbox whitelist changes.
    Tracked/new-file whitespace checks pass. All four KS modules remain clean and zero commits
    ahead/behind locally recorded `origin/main`; no remote fetch was performed. Exact production
    bytes are restored after all four mutation runs and no test/build processes remain running.
    Logs: `.ks_procgen_sizegoals_release.log`, `.ks_procgen_sizegoals_final_debug.log`,
    `.ks_procgen_sizegoals_final_units.log`, `.ks_procgen_sizegoals_final_integration.log`.
    R01-R06 remain in progress and R07 pending. Rough whole-system completion is 64% (40-66%
    uncertainty). Full repository tests and unsuppressed CI warning gates remain unverified.
    Next rewrite tasks: arbitrary-shape/per-network size-goal strategies, joint route/content
    alternatives, complete work accounting and replay serialization, then real staged validation
    and publication. The initial rectangular strategy is complete within its documented scope.
  - [x] Extend size-goal subdivision to retain both feasible X/Y layouts (2026-10-11). Keep
    canonical orientation order and deduplicate one-room layouts. Run the ordinary seam solver
    on each complete candidate until an unmerged `Proposed` result is found; protected floor can
    redirect the selected seam to the other orientation. Retain the first matching fill/partition
    fallback only if neither candidate succeeds. No cross-orientation placement artifacts survive.
  - [x] Share graph/seam allowances and actual remaining merge attempts across orientations.
    Budget exhaustion immediately rejects all placement artifacts, even after a reusable-looking
    earlier fallback; discarded work is never refunded. Final scene audit runs on selected geometry.
    Add immutable `KsProcgenSizeGoalPartitionAttempt` diagnostics with axis, status, reason and work
    totals. Direct group failures retain diagnostics only. Whole-pipeline stages retain aggregate
    work; complete diagnostic serialization remains pending. Keep existing semantic hash domains.
  - [x] Add orientation regressions: protected full-column square, zero/one merge allowances,
    successful other-axis door approaches, exact budgets and input permutations, exhaustion during
    the second seam/connectivity solve, deterministic first fallback when both axes are blocked,
    immutable attempt reports and duplicate single-room elimination. Fifteen focused geometry cases
    pass before adding the immutable-list assertion; final runs verify that assertion too.
  - [x] Verify loaded strict-goal orientation retry through the complete owned pipeline with
    `KsProcgenEmptyOfficeThemeFixture`. Protected initial floor blocks the X seam while the Y door
    remains writable; final 18/13 room counts pass with merged/size fallback disallowed. Discarded
    merging remains charged, a complete attempt uses one layout slot, exact work-cap replay retains
    accepted semantic identity, second-orientation seam exhaustion exposes no root artifacts, and
    engine entities remain unchanged. This fixture explicitly requests no optional content/lights:
    ordinary office contents can disconnect an unused clean pocket, which the access audit rejects.
    No existing constant-floor, door-write or clean-access validation was relaxed.
  - [x] Prove orientation regressions with four deliberate compiling source mutations. Stopping
    at the first fallback produces 18-cell proposals where the final smaller room must have 13.
    Omitting discarded seam work reports only 227 probes instead of 455/635 for the zero/one merge
    fixtures. Reusing a fallback after seam exhaustion returns `Candidate` where `BudgetExceeded`
    is required. Returning mutable diagnostics allows `Clear()` where `NotSupportedException` is
    required. Every mutant fails assertions; exact original source bytes are restored in `finally`.
    The graph-exhaustion variant still rejects under the fallback-reuse mutant because its final
    audit has no remaining graph allowance; the seam-exhaustion variant specifically proves the
    erroneous reuse. Logs: `.ks_procgen_axis_mutation_{StopAtFallback,DiscardedSeamWork,
    ReuseFallbackAtExhaustion,MutableDiagnostics}.log`. No compiler failure is counted as evidence.
  - [x] Complete restored orientation gates (2026-10-11). Server Release passes with 512
    existing permitted warnings and zero errors. Fresh `--no-incremental` Debug integration
    build passes with 1107 existing permitted warnings and zero errors. Rebuilt procedural
    units pass all 452 cases, including the immutable-list assertion. Final no-build integration
    passes 134 loaded cases and the actual client SandboxTest (135 combined), with no failures
    or skips. No new warnings in the changed orientation sources, warning suppressions, event
    subscriptions, dependencies or sandbox whitelist edits. Tracked/new-file whitespace checks
    pass. All four KS modules remain clean and zero commits ahead/behind locally recorded
    `origin/main`; no remote fetch was performed. Exact production bytes are restored after all
    four mutations; no build/test processes remain running. Logs: `.ks_procgen_axis_release.log`,
    `.ks_procgen_axis_final_debug.log`, `.ks_procgen_axis_final_units.log`,
    `.ks_procgen_axis_final_integration.log`. R01-R06 remain in progress and R07 pending. Rough
    full-system completion remains 64% (40-66% uncertainty). Full repository tests and unsuppressed
    CI warning gates remain unverified. Next rewrite work: arbitrary-shape/per-network size goals,
    alternate width distributions and joint route/content search, complete work accounting/replay,
    then real staged validation and publication. Alternate orientation solving is complete within
    its documented rectangular, single-network scope.
  - [x] Add bounded alternate-width generation to rectangular size-goal subdivision (2026-10-13).
    Start with the unchanged greedy vector, then breadth-first one-column donor/recipient transfers.
    Enforce minimum/maximum width intervals and total length, copy before mutation, deduplicate
    vectors and cap at eight vectors/4096 transfer examinations per axis. Preserve one-room
    deduplication and complete coverage. Canonical X candidates precede Y candidates, at most sixteen
    seam solves in one complete goal layout slot. No new profile schema or implicit work allowance.
  - [x] Reuse atomic partition selection and shared work for width alternatives. Failed layouts
    cannot refund graph, seam or merge work; budget exhaustion cannot return an earlier fallback.
    Preserve the first whole fallback only after every bounded solve fails to remain unmerged.
    Extend immutable diagnostics with `WidthVariant`, keeping ordinals out of final semantic hash.
    Room IDs are stable and selected cell membership carries actual width changes into identity.
  - [x] Add width-search regressions: protected boundary movement with zero/one merge budgets,
    successive breadth-first states, upper-band enforcement, eight/sixteen-layout limits and first
    fallback retention, exact-budget replay, input permutations and atomic exhaustion on the second
    width solve. All 23 focused geometry cases pass. The loaded strict-band case also passes:
    raw widths 7/3 fail at protected X=7, widths 6/4 produce final 24/13 rooms and pass materials,
    empty content/light stages and independent access. Exact root work replay retains semantic
    identity; seam exhaustion exposes no root artifacts; engine entity sets remain unchanged.
  - [x] Prove width-search regressions with four deliberate compiling mutations (2026-10-13).
    Aliasing the current width vector instead of copying it breaks both zero/one merge fixtures,
    yielding an 11-cell room where 13 is required. Removing the recipient maximum allows an illegal
    X candidate to bypass protected seams instead of reaching the valid Y candidate. Increasing
    the vector cap to nine produces nine/eighteen solves where eight/sixteen are required. Dropping
    `WidthVariant` from diagnostics records zero where the selected retry must be one. All four
    runs fail assertions; no compiler failure is accepted as evidence. Restore exact original source
    bytes in `finally` after each mutation. Logs: `.ks_procgen_width_mutation_{AliasedWidths,
    RecipientMaximum,LayoutCap,VariantDiagnostics}.log`.
  - [x] Complete restored width-search gates (2026-10-13). Server Release passes with 512
    existing permitted warnings and zero errors. Fresh `--no-incremental` Debug integration build
    passes with 1107 existing permitted warnings and zero errors. Rebuilt procedural units pass
    all 460 cases. Final no-build integration passes 135 loaded cases and the actual client
    SandboxTest (136 combined), with no failures or skips. No new warnings in the changed width
    sources, warning suppressions, event subscriptions, dependencies or sandbox whitelist edits.
    Tracked/new-file whitespace checks pass. All four KS modules remain clean and zero commits
    ahead/behind locally recorded `origin/main`; no remote fetch was performed. Exact production
    bytes are restored after all four mutations; no build/test processes remain running. Logs:
    `.ks_procgen_width_release.log`, `.ks_procgen_width_final_debug.log`,
    `.ks_procgen_width_final_units.log`, `.ks_procgen_width_final_integration.log`. R01-R06 remain
    in progress and R07 pending. Rough full-system completion is 65% (40-67% uncertainty). Full
    repository tests and unsuppressed CI warning gates remain unverified. Next rewrite work:
    arbitrary-shape/per-network size goals, wider partition and joint route/content search, complete
    work accounting/replay, then real staged validation and publication. Width retries are complete
    within their documented rectangular, single-network, bounded-search scope.
  - [x] **Bounded request-wide room quotas across separate entrance networks (2026-10-13).**
    Implement `KsProcgenSizeGoalAllocator` before automatic subdivision for opted-in multi-network
    requests. Sort inspected networks and goals deterministically; enumerate count vectors, debit
    the remaining global quotas, backtrack failed downstream fits and accept only a complete
    assignment. Preserve the single-network and explicitly supplied-zone paths. Hard caps are
    32 networks, 256 total rooms, 4096 vector probes and 64 rectangle-fit calls. Every network needs
    at least one room; unsupported/capped searches discard all tentative layouts and use the whole
    scalar fallback. Strict final size-count checks continue to decide whether that fallback is
    permitted. Charge inspection cells once, preserve shared solve budgets, report allocation
    probes on failure and tag every size-goal seam attempt with its network identity.
    - [x] Unit coverage: exact and flexible 16/9/5 bands across separate 9x4 and 4x4 rectangles;
      no duplicated request counts; input permutations and exact graph/seam replay; backtracking
      when an early feasible quota strands the next network; atomic fallback for an unsupported
      later footprint; insufficient global room counts; request-wide 257/4096-room rejection;
      a 64-fit cap; inspection exhaustion and shared graph/seam exhaustion in the second network
      without partial artifacts. Revise the older multi-network test to assert successful fitting
      while preserving both inspected networks and the original separation geometry.
    - [x] Loaded pipeline fixture: strict counts, disallowed merges, independent materials/access,
      one complete successful layout, exact reported work replay and an unchanged live engine
      entity set. Deny an impossible one-room global quota after both permitted goal/scalar
      attempts; the failure exposes no stage artifacts. Use the existing empty office theme to
      isolate geometry from content search.
    - [x] Deliberate compile-valid regression checks (production bytes restored in `finally`):
      stop debiting remaining quotas and all four exact/flexible permutation cases fail;
      retain abandoned partial layouts and the later unsupported-network case fails; raise the
      rectangle-fit cap from 64 to 65 and the hard-cap assertion fails with actual 65. Evidence:
      `.ks_procgen_allocation_mutation_DuplicatedQuota.log`,
      `.ks_procgen_allocation_mutation_PartialAllocation.log`,
      `.ks_procgen_allocation_mutation_FitCap.log`. None relies on compiler errors.
    - [x] Final gates (2026-10-13): restored production code passes Server Release (512 existing
      permitted warnings, zero errors), fresh nonincremental integration Debug (1107 existing
      permitted warnings, zero errors), all **472 procgen unit cases**, and **137 loaded cases**
      comprising 136 procgen integration cases plus the actual client `SandboxTest`. No skips.
      The loaded rejection test initially exhausted its one-layout allowance before the existing
      scalar retry; allow two layouts for that denial check and rebuild the integration tests.
      All positive fixtures still complete with one layout. No production relaxation was needed.
      New/tracked-file whitespace checks pass; all three mutation sources are restored exactly.
      All four KS modules are clean and 0/0 against locally recorded `origin/main` (no fetch).
      Logs: `.ks_procgen_allocation_release.log`, `.ks_procgen_allocation_final_debug.log`,
      `.ks_procgen_allocation_test_fix_build.log`, `.ks_procgen_allocation_final_units.log`,
      `.ks_procgen_allocation_final_integration.log`. R01-R06 remain in progress and R07 pending.
      Overall estimate remains **65%** (40-67% uncertainty). Full repository tests, unsuppressed
      CI warning gates, full work metering and live publication remain unverified.
    - [x] **Retry complete room-quota allocations after protected seams (2026-10-13).** Preserve
      the first whole fallback, reject its sorted count-vector identity and enumerate a different
      complete allocation with cumulative metadata caps. Try at most eight assignments. Solve
      every owned network with the original shared graph/seam/merge budget; never mix assignments
      or reset any allowance. Terminal solve exhaustion discards all artifacts. If every bounded
      allocation needs a fallback, restore the first complete regions/walls/floor together and
      independently audit it. Report begun assignment count and frozen per-solve allocation ordinal.
      - [x] Unit coverage: a preserved second 8x4 network forces the two-room quota into the first
        network, producing 24/5/32 floors and exact three-room counts. Preserving both networks
        retains the canonical first 1/2-room quota fallback. Validate preserved floor, separate
        ownership, input permutation and exact work replay. Assert cumulative metadata counters
        (6 fits / 8 vectors for success; 10 fits / 14 vectors for two failed whole assignments),
        and terminal graph/seam exhaustion during the second assignment without fallback artifacts.
      - [x] Loaded strict-count fixture: one complete owned layout performs two internal quota
        assignments, rejects the first protected seam, forbids merges, completes independent
        material/interior validation, replays exact work and leaves engine entities unchanged.
      - [x] Final gates (2026-10-13): restored production source passes Server Release (512
        existing permitted warnings, zero errors), fresh nonincremental integration Debug (1107
        existing permitted warnings, zero errors), **476 procgen unit cases**, and **138 loaded
        cases** (137 procgen integration cases plus actual client `SandboxTest`); no skips. The
        protected-network fixture completes strict counts and exact reported-work replay with no
        engine writes. Tracked and new-file whitespace checks pass. All four KS modules remain
        clean and 0/0 against locally recorded `origin/main` (no fetch). No commits, prototype
        changes, upstream edits, warning suppressions or sandbox whitelist changes were needed.
        Logs: `.ks_procgen_quota_retry_release.log`, `.ks_procgen_quota_retry_final_debug.log`,
        `.ks_procgen_quota_retry_final_units.log`, `.ks_procgen_quota_retry_final_integration.log`.
        R01-R06 remain in progress and R07 pending; rough full-system completion remains **65%**
        (40-67% uncertainty). Full repository tests, unsuppressed CI warning gates, arbitrary-shape
        fitting, joint content search, full work metering/replay and engine publication remain open.
      - [x] Compile-valid regression mutations caught by assertions: returning the last regions
        instead of restoring the first whole fallback changes the expected first network from
        one room to two; resetting quota/fit counters between retries reports 4 instead of 10 fit
        probes for the all-protected fixture; reducing the retry cap to one reports one instead of
        two attempted assignments. Restore exact original source bytes in `finally` for each run.
        Evidence: `.ks_procgen_quota_retry_mutation_LastFallback.log`,
        `.ks_procgen_quota_retry_mutation_ResetMetadata.log`,
        `.ks_procgen_quota_retry_mutation_NoRetry.log`. Compiler failures are not counted.
    - [x] **Reflected rectangular room-goal strips (2026-10-13).** Keep canonical axis/width
      proposals first, then try reflected proposals from the opposite rectangle edge. Recompute
      first-room wall loss from physical order and assign/sort padded room IDs in that order.
      Expose a frozen `Reflected` direction tag. Bound each axis/direction to eight vectors and
      4096 transfer examinations; at most 32 layouts per network. Deduplicate one-room axes and
      omit one-room reflections. Preserve whole fallback selection and original shared budgets.
      - [x] Unit fixtures: blocked canonical X/Y seams require 12/25 floors on reflected X and
        transposed Y; exact 25/12 bands fit only reflected order; input permutations and exact
        graph/seam replay; graph or seam exhaustion during reflection cannot expose an earlier
        fallback; all-protected rectangles enforce 16/32 total candidate caps while retaining
        the original canonical fallback. Existing quota-retry and single-room regressions pass.
      - [x] Implement loaded strict-count empty-theme fixture with preserved floor, disallowed
        merges and size deviations, exact reported-work replay and an unchanged engine entity set.
      - [x] Final gates (2026-10-13): restored production source passes Server Release (512
        existing permitted warnings, zero errors), fresh nonincremental integration Debug (1107
        existing permitted warnings, zero errors), **481 procgen unit cases**, and **139 loaded
        cases** (138 procgen integration cases plus actual client `SandboxTest`), with no skips.
        Strict reflected interiors complete exact reported-work replay without engine writes.
        Tracked/new-file whitespace checks pass. All four KS modules are clean and 0/0 against
        locally recorded `origin/main` (no fetch). No prototype changes, upstream edits, warning
        suppressions, sandbox whitelist changes or commits were required. Logs:
        `.ks_procgen_reflection_release.log`, `.ks_procgen_reflection_final_debug.log`,
        `.ks_procgen_reflection_final_units.log`, `.ks_procgen_reflection_final_integration.log`.
        R01-R06 remain in progress and R07 pending; full-system estimate remains **65%**
        (40-67% uncertainty). Arbitrary-shape fitting, wider room-order and joint content search,
        full work metering/replay and engine validation/publication remain open. Full repository
        tests and unsuppressed CI warning gates remain unverified.
      - [x] Compile-valid deliberate regressions caught: charging wall loss by original goal
        index makes the exact reflected 25/12 case fall back instead of fitting; retaining
        original indices for spatial IDs puts the wall in the small room and reports 9 instead
        of 12 floors on both axes; omitting reflected candidates reports 8/16 instead of 16/32
        attempts for the all-protected cap cases. Restore exact original bytes in `finally` after
        each mutation. Logs: `.ks_procgen_reflection_mutation_WrongWallLoss.log`,
        `.ks_procgen_reflection_mutation_WrongSpatialIds.log`,
        `.ks_procgen_reflection_mutation_NoReflection.log`. No compiler failures are counted.
    - [x] **Bounded irregular strip fitting (2026-10-14).** Dispatch nonrectangular inspected
      networks to `KsProcgenIrregularSizeGoalSubdivision`, preserving rectangular candidate order.
      Validate contiguous column spans and overlaps on each chosen axis; compute prefix areas and
      actual shared-boundary wall loss; require a possible cardinal door with both approach tiles.
      Enumerate canonical/reversed goal order and ascending cut coordinates. Bound irregular
      networks to 64 rooms, 4096 endpoint probes and eight completed vectors per axis/order.
      Clone cut vectors and freeze results. Reuse the existing shared seam solver, fallback,
      independent audit, quota allocation and work accounting. Do not extend missing corners.
      - [x] Ten unit cases: exact 16/9 L-shape floors on both axes, exact replay/input permutation,
        protected cut retry yielding 20/5, reflected 4/23 shared-boundary loss, bounded cut vectors
        with whole canonical fallback, 65/256-room cap fallback, unsupported interior holes and
        atomic graph/seam exhaustion. The protected retry test initially used overlapping bands;
        correct it to the required disjoint inclusive 16..20 and 5..9 definitions.
      - [x] Implement loaded L-shape fixture with strict counts, disallowed merges, unchanged
        footprint coverage, exact reported-work replay and an unchanged live entity set.
      - [x] Final gates (2026-10-14): restored production code passes Server Release (512 existing
        permitted warnings, zero errors), fresh nonincremental integration Debug (1107 existing
        permitted warnings, zero errors), **491 procgen unit cases**, and **140 loaded cases**
        (139 procgen integration cases plus actual client `SandboxTest`); no skips. The loaded
        L shape completes strict counts, exact reported-work replay and independent materials/
        interior checks without engine writes. Tracked/new-file whitespace checks pass. All four
        KS modules are clean and 0/0 against locally recorded `origin/main` (no fetch). Logs:
        `.ks_procgen_irregular_release.log`, `.ks_procgen_irregular_final_debug.log`,
        `.ks_procgen_irregular_final_units.log`, `.ks_procgen_irregular_final_integration.log`.
        R01-R06 remain in progress and R07 pending. Estimate: **66%** (40-68% uncertainty).
        Full repository tests and unsuppressed CI warning gates remain unverified. Continue toward
        the requested 75% milestone through concrete capability/rewrite exits, not checklist inflation.
      - [x] Compile-valid deliberate regressions caught by assertions: subtracting full column
        height instead of shared overlap prevents the reflected 4/23 fit; aliasing cut vectors
        changes the first fallback's third room from 8 to 36 raw cells; raising the vector cap
        from eight to nine produces 18 rather than 16 attempted layouts. Restore exact source
        bytes in `finally` after each mutation. Evidence:
        `.ks_procgen_irregular_mutation_FullColumnLoss.log`,
        `.ks_procgen_irregular_mutation_AliasedCuts.log`,
        `.ks_procgen_irregular_mutation_VectorCap.log`. Compiler failures are not counted.
      - [ ] Extend fitting to holes, disconnected strip spans, arbitrary cuts and wider room-order
        search while retaining bounded work, complete ownership and clean entrance access.
    - [ ] Extend quota reassignment to later content failures. Do not globally search the product
      of quota allocations, routes, widths and content choices without sharing the remaining work
      allowances. Arbitrary shapes and fully metered metadata/replay remain separate rewrite work.
  - [ ] Finish joint request-wide search over alternate route trees, room subdivisions and
    prefab/assembly/light choices within a configuration. The coordinator currently accepts the
    first complete permitted candidate; it does not globally optimize degradation or retry alternate
    route trees, arbitrary partition boundaries or content choices after a complete failure. Ordered
    subdivision-size retries now cover a bounded subset of interior layouts within a configuration.
    Extend the rectangular size-goal strategy to arbitrary shapes and quota reassignment after later failures, as well
    as prefab/hybrid and interior-fill modes,
    meter remaining normalization and non-graph allocation/sorting/placement/compilation work,
    reserve fallback passes, serialize replay diagnostics, and connect real staged materialization/
    publication only after the R06 validation gates pass.
- [ ] **R06 - Materialize and verify relations and connection configurations.** Extend the planned
  T12 entity stage beyond `KsProcgenTileStageSystem`'s current tile-only support boundary using
  engine-validated surface/container adapters and dependency order. Retain its rejection of unsupported
  entities until the replacement is proven. Preserve actual support slots, parent references and
  member transforms through initialization and cancellation. Validate use reach, clean chair paths,
  footprint collision, operational light and gas/door behavior; compare actual port components to
  the selected configuration in its declared scope. A geometric witness is insufficient for engine
  success. Exit: A43-A44/A49-A50 engine cases and A52 pass; failed validation discards staging.
  - [x] Add `KsProcgenContainerPreflightSystem.Check` as a live eligibility adapter for future staged
    `InContainer` members. Require initialized, nonterminating entities that are not queued for
    deletion, a bounded nonblank container ID, and a subject outside existing containers. Reject
    self-insertion, missing live containers, mismatched item-slot/container instances, locked slots,
    occupied slots and expected slot occupants. Never swap or move preexisting content.
  - [x] Delegate item-slot eligibility to `ItemSlotsSystem.CanInsert` with no user and `swap: false`,
    preserving actual whitelist/blacklist rules and cancellable attempt events on host and subject.
    Delegate unmanaged container eligibility to `SharedContainerSystem.CanInsert` with
    `assumeEmpty: false`, including the engine's containment-cycle and insertion-event checks.
    A declared item slot cannot fall through to the generic API to bypass its restrictions.
    Known slot capacity is one; generic capacity remains unknown.
  - [x] Keep this API a preflight: `Eligible` describes the check's current result and every returned
    record has `InsertionVerified == false`. The adapter itself neither creates containers nor inserts,
    reparents or reserves entities. Engine attempt-event handlers may have side effects; eligibility
    must not be cached or treated as a pure lookup. Actual insertion must repeat engine checks and
    verify the resulting parent/container membership after initialization. Geometric plan hash v20
    is unchanged because this helper is not yet consumed by planning or staging.
  - [x] Add `KsProcgenSurfacePreflightSystem` as a read-only candidate check. Require initialized,
    nonterminating entities, an unanchored item subject, no containing container and an enabled
    live surface with a finite offset. Reject self-placement, malformed slot IDs and unsupported
    named surface slots. Read optional tracker occupancy, maximum and live engine whitelist rules;
    reject already tracked subjects, full trackers and tracker collections above the 256-item bound.
    Zero retains the engine's unlimited-tracking meaning. Return centering/offset and current tracking
    metadata without moving entities, allocating a preview, reserving capacity or raising drop events.
    All results have `PlacementVerified == false`. Tracker filters control contact tracking rather
    than generic surface drop permission; the stricter candidate check does not invent a mount API.
  - [x] Add the loaded-server
    `LiveSurfacePreflightRetainsTrackingLimitsWithoutMovingOrMountingEntities` fixture. Inspect a
    real table/microwave candidate and a hidden inherited table fixture with a centered offset,
    one-item tracker and component whitelist. Verify live component removal/restoration changes
    eligibility, disabled surfaces and nonfinite offsets reject, and named slots remain unsupported.
    Exercise invalid IDs, self-placement, nondroppable subjects, missing surfaces, contained subjects
    and queued deletion; verify unchanged coordinates/entity count and empty tracking state.
    Declaration and support-planner tests cover finite/full/unlimited capacities, existing claims,
    unknown named slots and hash sensitivity. These fixtures do not demonstrate actual contacts or
    a physically full live tracker.
  - [x] Add `KsProcgenSurfaceStageSystem` as a disposable two-entity drop-coordinate primitive.
    Spawn a new surface and item on an owned paused, uninitialized map. Use the centered surface
    offset when declared, otherwise a deterministic center click; apply a requested quarter-turn
    using the transform system and verify the resulting item coordinates/orientation. Bound input
    IDs, rotations, drop offsets (16 map units per axis), retained previews (16) and cumulative
    generated entities per preview (1-256, default 256). Count the map and initializer allocations,
    including deleted/off-map entities. Reject failed preflight, pose drift, generated off-map
    survivors and exhausted budgets, then clean the whole allocation journal. Explicit discard
    also deletes generated entities moved off-map and releases retention. No existing entities are
    borrowed. `PositionedPreview` and `PlacementVerified == false` distinguish this primitive from
    engine-supported mounting or a publishable room. No contact events/tracker membership are faked.
    Reject a nonzero requested turn when the live transform has `NoLocalRotation`; never clear the
    lock to make a proposal succeed. The inherited real microwave currently has this lock. An
    author who needs an independently facing supported device must provide appropriate content;
    the hidden rotatable fixture exercises only the transform-enabled alternative.
  - Surface preview fixtures (2026-10-03):
    `OwnedSurfaceDropPreviewVerifiesCoordinatesOrientationAndRejectsDrift` checks all four turns
    on inherited rotation-enabled microwave content, actual rotation/coordinate drift, a centered
    tracker offset, live disablement/offset changes, paused/uninitialized map state and empty tracker
    membership. It rejects the real microwave's locked nonzero turn and removes a subject moved to
    nullspace on discard. `SurfaceDropPreviewBoundsAllocationRetentionAndCleansRejectedPairs`
    checks invalid inputs, pre-allocation minimum, incompatible/missing surfaces, precancellation,
    startup-generated off-map items charged against allocation caps, off-map rejection below the cap,
    sixteen-preview retention and quota release. Both verify no surviving fixture entities.
    `SurfaceAndContainerPreviewTransactionsRejectCrossAdapterReentrancy` exercises nested staging,
    initialization and discard from surface startup and container map initialization, mid-startup
    cancellation, startup exceptions and lease release. Unrelated retained previews survive those
    failures. Rotation support is inherited content behavior, not a forced transform override.
  - [x] Share `KsProcgenPreviewOperationSystem` between surface and container previews. Hold one
    synchronous operation lease across staging, container initialization and cleanup; nested staging,
    initialization and public discard cannot capture or delete another adapter's entities. Container
    failure cleanup uses an internal owned-discard path while keeping that lease. This bounds
    cross-adapter reentrancy without claiming isolation from unrelated engine side effects or
    deferred/cleanup-time spawning. Other generator adapters must join this lease before composition.
  - Surface preview verification (2026-10-03): all 198 focused Debug procedural unit tests,
    all eighteen loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. The initial rotation fixture failed:
    the real microwave inherits `noRot: true`, so a requested turn was ignored. The adapter now
    rejects that request explicitly; all four supported turns use an inherited fixture with rotation
    enabled. Temporarily filtering cleanup to survivors still on the owned map leaves the moved
    item alive; the isolated fixture fails (`Expected: False`, `But was: True`). Source was restored
    in `finally`. Sequential Release and Debug `--no-incremental` builds preceded the final complete
    corpus; scoped diff and new-file whitespace checks pass. Unsuppressed CI warnings-as-errors
    remains unverified. Geometry identity stays v20 and capability identity v2; the preview primitive
    is not a new geometry planning input. R02/R06 remain open for full assembly/support poses,
    physical contacts, initialization, operational validation and publication. Overall estimate
    remains approximately 40% (35-45%).
  - [x] Add explicit `TryInitialize` to owned surface previews. Record uninitialized/initialized
    phases and require the matching selected-entity lifecycle and map state. Initialize the map once
    with `unpause: false`, capturing all synchronous initialization allocations in the original
    cumulative journal. Repeated successful calls validate current state and return the same
    `InitializedPreview` without replaying map initialization or charging allocations twice.
    Recheck exact subject/surface prototypes, selected lifecycle, queued deletion, parent/position,
    subject orientation, effective surface drop offset, live enablement/filters and generated-map
    ownership afterward. A precondition failure does not run initialization. Any invalid result,
    allocation overflow, cancellation or engine exception discards the owned stage under the shared
    operation lease; unrelated surface/container previews survive. Cancellation also discards an
    already initialized preview. `PlacementVerified` remains false and the map remains paused.
  - Initialization fixtures (2026-10-03):
    `SurfacePreviewInitializationRetainsPausedPosesAndInitializesOnlyOnce` initializes real table/
    microwave, paper and tracked coordinates-disk pairs, verifies actual selected lifecycle, counts
    real microwave-generated content, checks idempotence and rejects a discarded handle.
    `SurfacePreviewInitializationRejectsLifecyclePoseAndFilterChanges` exercises invalid preconditions,
    initialization-time offset/enablement/coordinate changes, a live tracking tag removed through
    `TagSystem`, and a real replacement spawner queuing deletion of the selected disk. Every failure
    removes its stage and generated content. `SurfacePreviewInitializationCleansFaultsAndPreservesUnrelatedPreviews`
    covers pre/mid-initialization cancellation, engine exceptions, allocation overflow, off-map
    initializer spawns and cross-adapter nested initialization/staging/discard. It verifies lease
    release, cleanup and survival of unrelated retained previews, including initialized content.
  - Surface initialization verification (2026-10-03): all 198 focused Debug procedural unit tests,
    all twenty-one loaded-server Debug tests (zero skipped) and Server Release compilation pass
    with the existing `--no-restore -p:WarningLevel=0` workaround. Temporarily omitting discard
    from the initialization exception handler leaves the failed stage active; the isolated fault
    fixture fails (`Expected: False`, `But was: True`). Source was restored in `finally`.
    Release and Debug builds ran sequentially; a Debug `--no-incremental` rebuild preceded the
    final complete unit/loaded corpus. Scoped diff and new-file whitespace checks pass.
    Unsuppressed CI warnings-as-errors remains unverified. This establishes paused lifecycle,
    coordinate/filter revalidation and owned initialization rollback, not physical contacts,
    mounted/operational devices, pressure/access or publication. Geometry identity remains v20,
    capability identity remains v2, and R02/R06 stay in progress. Overall estimate remains
    approximately 40% (35-45%); a higher test count does not imply full-system completion.
  - [ ] Add complete-assembly surface staging, actual contact/overlap verification and
    post-initialization tracking checks. Explicit paused initialization does not simulate physical
    contacts, infer a support footprint, handle multiple children/named slots or integrate floor poses.
    Apply the independently tested live contact/capacity checks to owned previews,
    distinguish dropped items from mounted devices, validate operational fronts and clean chair
    access, and integrate approved support poses into R02 search. Floor search and container staging
    still reject surface variants; successful preflight cannot bypass those gates.
  - [x] Add independent read-only `KsProcgenSurfaceContactSystem` observations of real engine
    contacts. Require ready, uncontained entities on the same non-null map, live collision bodies
    and fixture declarations; bound fixtures to 64 per entity, tracker collections to 256 and
    contact-record requests to 1-256 (one extra record detects overflow). Inspect actual nondeleting
    touching records for the exact pair, check current fixture identity/child indices and reject hard
    contacts. Current disjoint fixture bounds refute a cached sleeping contact; overlapping bounds
    alone do not establish fresh exact shape overlap. Report soft/hard records, tracker membership,
    count/maximum and enablement. A tracked member at capacity can have valid contact while new drops
    are disabled; do not reuse new-drop eligibility as a retained-contact check. Reject missing
    tracker membership, denied live filters and excess capacity. Zero preserves unlimited tracking.
    Budget failure counters are diagnostic prefixes, never complete contact evidence.
  - [x] Exercise real physics contact/tracker capacity with a chemistry hotplate and beakers,
    plus an inherited unlimited-tracker fixture. Verify contact creation, a finite tracker filling
    and rejecting a new drop, a physically touching excess item remaining untracked, two accepted
    unlimited members, real end-contact capacity release and restored preflight eligibility.
    Manual moves refute old records through current disjoint bounds; moved sleeping fixture bodies
    are explicitly awakened through the physics system for end-contact reevaluation. A separate
    inherited hard-sensor fixture captures read-only rejection evidence from an actual engine
    `ItemPlacedEvent` before solver separation. Two overlapping hotplates exercise contact budget
    exhaustion. No test fabricates collision events or changes tracker collections directly.
  - [ ] Integrate contacts with an owned, bounded simulation/settling phase and support pose search.
    This observer does not unpause preview maps, simulate physics, validate mounting/stability,
    establish sensor coverage or operational fronts, or certify all tracked members/other-world
    collisions. Contact caches can be stale even after an unrelated tick; bounds only refute clearly
    disjoint poses. Full shape validation/freshness and private simulation side effects remain work.
    `Observed` and `PlacementVerified == false` cannot authorize shared-XY placement or publication.
  - [x] Refine contact observations with `KsProcgenConvexShapeOverlap` for fresh current-pose
    circle/convex-polygon overlap. Support circles, legacy AABBs and `PolygonShape` with 3-8 vertices;
    transform authored offsets/vertices, preserve skin radii, reject nonfinite/out-of-range poses,
    invalid radii and nonconvex/degenerate polygon data. Use closed-shape containment/intersection
    and bounded point-to-segment distances rather than treating overlapping bounding boxes as
    overlap. Each pair uses at most 8x8 edge comparisons and bounded convexity validation.
    Reject unsupported shapes explicitly with no positive overlap result. The contact observer
    still requires an existing nondeleting touching engine record and matching fixture identity,
    but can now refute stale records even when their current bounds overlap.
  - Current-shape fixtures (2026-10-03): circle separation/tangency, rotated authored circle offsets,
    triangular containment and absent corners, legacy box rotation, rounded polygon corner skins,
    thin rotated polygons crossing without containing vertices, and unsupported/nonfinite inputs.
    `SurfaceContactInspectionRefutesStaleContactsInsideOverlappingBounds` creates real circle-sensor
    contact, then moves the tracked circle into a separated diagonal pose whose bounding boxes
    still overlap. Before any new physics tick, the observer reports `NoContact` while separately
    exposing the engine's stale tracker membership. It allocates no entities or modifies contacts.
  - Current-shape verification (2026-10-03): all 207 focused Debug procedural unit tests,
    all twenty-five loaded-server Debug tests (zero skipped) and Server Release compilation pass
    with the existing `--no-restore -p:WarningLevel=0` workaround. Temporarily ignoring a negative
    fresh shape result makes the diagonal circle fixture report `Observed` instead of `NoContact`;
    the isolated test fails and source was restored in `finally`. Sequential Release and Debug
    `--no-incremental` builds preceded the complete final corpus; scoped diff and new-file whitespace
    checks pass. Unsuppressed CI warnings-as-errors remains unverified. This establishes bounded
    current convex geometry, not engine solver-policy equivalence, owned settling or operational
    mounting. Geometry identity stays v20 and capability identity v2. R02/R06 remain in progress,
    and the overall estimate remains approximately 40% (35-45%).
  - [ ] Finish owned settling rather than stepping global physics from a synchronous preview call.
    The exposed engine stepping loop processes the whole world; simply capturing every allocation
    across ticks would adopt unrelated entities. Add map-scoped/origin-aware allocation tracing,
    bounded asynchronous step scheduling, deferred safe cleanup and final repause before composing
    simulation with previews. Do not bypass engine access boundaries or manually invoke/fabricate
    contact events. The new geometric predicate does not supply this lifecycle or isolation proof.
    Edge/chain/other polygon representations remain unsupported by this predicate; extend them
    only with bounded geometry and dedicated engine comparisons. Rounded shapes use authored skin
    radii and floating-point closed intersection, not the engine solver's contact-slop policy.
  - Contact observer verification (2026-10-03): all 198 focused Debug procedural unit tests,
    all twenty-four loaded-server Debug tests (zero skipped) and Server Release compilation pass
    with the existing `--no-restore -p:WarningLevel=0` workaround. Initial separation fixtures
    exposed sleeping contact caches; disjoint current bounds now refute old records before another
    tick, and supported physics wake calls allow real end-contact/capacity updates. The hard-sensor
    fixture requires its complete shape/layer declaration when overriding an inherited fixture.
    Temporarily bypassing current-bounds rejection makes both finite/unlimited movement fixtures
    report `Observed` instead of `NoContact`; both fail and source was restored in `finally`.
    Sequential Release and Debug `--no-incremental` builds preceded the final complete corpus.
    Scoped diff and new-file whitespace checks pass. Unsuppressed CI warnings-as-errors remains
    unverified. Geometry identity stays v20 and capability identity v2; the observer is not consumed
    by geometry search or paused preview validation. R02/R06 stay in progress for owned simulation,
    full support poses, fresh exact shape validation, operational use and publication. Overall
    estimate remains approximately 40% (35-45%).
  - Surface amendment verification (2026-10-03): all 198 focused Debug procedural unit tests,
    all fifteen loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Temporarily excluding existing tracked
    items from reservation accounting makes the capacity fixture return `NeedsEngineValidation`
    instead of atomic rejection; the test fails and source was restored in `finally`. Release and
    Debug builds ran sequentially; a Debug `--no-incremental` rebuild preceded the final complete
    test runs. Scoped diff and new-file whitespace checks pass. Unsuppressed CI warnings-as-errors
    remains unverified. These checks establish declaration accounting and live read-only preflight,
    not actual surface placement, contact capacity, operating access or publication. R01/R02/R06
    remain in progress, and the overall estimate remains approximately 40% (35-45%).
  - [x] Add the loaded-server `LiveContainerPreflightUsesEnginePermissionWithoutInsertion` fixture.
    Spawn a real shuttle console, coordinates disks and an incompatible table in nullspace. Verify
    repeatable eligible checks leave the disk coordinates and empty slot unchanged, reject the table
    through actual component filtering, and respond to live lock changes. Insert the first disk using
    the engine API solely to establish an occupied test state; preflight then rejects a second disk
    without swapping the first and rejects already-contained subjects. A separate empty generic
    container reports eligible with unknown capacity and stays empty. Verify missing/blank IDs,
    self-insertion, an ancestor-to-descendant containment cycle and queued deletion. Delete every
    fixture entity in `finally`, including children and actually inserted contents.
  - Live container preflight verification (2026-10-02): all 162 focused Debug procedural unit tests,
    all five loaded-server Debug tests and Server Release compilation pass with the existing
    `--no-restore -p:WarningLevel=0` workaround. Concurrent Debug/Release builds initially left
    incompatible engine assemblies in configuration-independent output folders, causing setup to
    fail on `ReceiveLocalRayAtMainThread` before any fixture assertions. A sequential Debug
    `--no-incremental` rebuild produced a consistent assembly set; loaded and unit tests then passed.
    Use sequential configuration builds for this repository's shared output folders.
    Temporarily replacing the item-slot permission check with the generic container permission API
    makes the incompatible-table assertion fail: `ItemSlotInsertionDenied` becomes
    `ContainerInsertionEligible`. The adapter source was restored in `finally`, rebuilt, and all
    five loaded tests passed again. Scoped diff and new-file whitespace checks pass. Unsuppressed
    CI warnings-as-errors remains unverified. This establishes live preflight behavior, not completed
    relational placement, table/device overlap, operational reach, group isolation or publication.
  - [x] Add `KsProcgenContainerStageSystem` for disposable container-only assembly previews.
    Reinspect prototypes and rebuild the bounded support plan before creating a paused, uninitialized
    owned map. Spawn selected members in dependency order and use live preflight plus the actual
    item-slot or generic container insertion API. Verify actual named-container membership, direct
    transform parent, zero local position and unanchored contents; an item-slot success return alone
    is insufficient because its internal container insertion can fail.
  - [x] Track preview ownership and retain only a completely verified member set. On rejection or
    engine exception delete recorded members in reverse order and delete the owned map. Recorded
    members are deleted even if an event moved them off-map. Explicit discard, discard-all, round
    reset and shutdown clean owned previews; repeated discard is harmless. Verification rejects
    missing/deleted/queued members, changed prototypes or membership, an unpaused map or lifecycle-phase mismatch,
    missing map and invalid ownership handles. Initializer-created descendants remain subject to
    normal engine parent/map deletion; arbitrary event side effects outside the stage are not journaled.
  - [ ] Connect the container preview to layer-aware pose proposals and full owned entity staging.
    Surface and selected spatial relations remain explicitly unsupported by this preview; root
    positions are temporary display coordinates, not validated floor footprints. The existing tile
    stage still rejects furnished plans. No grid floor, map initialization, traversal, operational
    lighting, pressure, visibility isolation or publication is certified by `InsertedPreview`.
    `KsProcgenAssemblyStageSystem` now consumes rebuilt poses and stages selected floor/surface/
    container members together (R02's complete assembly preview tasks). Migrate planning callers to
    that adapter and connect it to tile/grid staging and relation/access checks; the container-only
    primitive retains its temporary root coordinates and unsupported surface/spatial boundary.
  - Owned container stage fixtures (2026-10-03): the loaded-server
    `OwnedContainerStageInsertsVerifiesAndRollsBack` test compiles real shuttle-console/disk cores,
    verifies actual disk-slot membership and parent transforms, then removes a child through the
    engine API and confirms stage verification fails. Explicit discard removes both members and map,
    rejects repeated discard and invalidates the handle. A second core inserts a valid disk before a
    later table fails another console's whitelist; all newly created entities are removed and no stage
    escapes. A too-small member budget creates no preview. An unmanaged `board` container exercises
    generic insertion independently of item slots. External map initialization/deletion invalidates
    previews, discard-all clears both handles and is repeatable, and a surface assembly remains
    unsupported without leaked entities. The fixture cleans retained previews in `finally`.
    Uninitialized maps remain effectively paused after clearing their explicit pause flag; the fixture
    tests that engine behavior and uses actual map initialization to exercise the initialization gate.
  - [ ] Verify initialized operational members and supported mounting, preserving rollback on event
    failure, cancellation and state changes. Extend post-map-init mounting checks beyond containers.
    Insertion-attempt exception fixtures are implemented in the later entry below. The tile stage still rejects every furnished plan, and
    the floor solver still rejects unsupported surface/container variants. No publication or surface
    overlap support is enabled by the container preview; R02/R06 remain open.
  - [x] Add explicit uninitialized/initialized preview phases and `TryInitialize` for owned container
    stages. Validate ownership and all current members before initialization; reject and discard an
    already-invalid owned preview. Retain an explicit map pause before recursive map initialization
    with `unpause: false`. Afterward require the map phase and every selected member's exact engine
    lifecycle to match, plus unchanged prototype, actual named-container membership, direct parent,
    zero contained local position and unanchored contents. Managed slots must still reference the
    actual named container. External initialization does not silently advance the tracked phase.
  - [x] Discard initialization failures and return no stage on invalidated membership/lifecycle or
    an engine exception. Repeated initialization of an already valid initialized preview is idempotent
    and does not rerun initializer events. A disposed/unowned handle returns `ContainerStageNotOwned`;
    no unrelated stage is discarded. `InitializedPreview` certifies only selected lifecycle and
    membership at that moment; actor interaction, operating power, spatial
    pose and surface mounting remain separate validation tasks. Geometry plan identity stays v20.
  - Initialization fixtures (2026-10-03): `ContainerStageInitializationPreservesMembersOrDiscardsPreview`
    initializes a real shuttle console/disk core and verifies both selected members reach
    `MapInitialized`, the disk remains in its named slot, the normal computer board appears, and
    the map stays paused even after its explicit pre-init pause flag was cleared. A second call creates
    no new entities. An initialized preview rejects an unpaused map and validates again when paused;
    successful discard removes its initializer-created board as well as selected members and map.
    A Debug fixture disk uses the real `RandomSpawner` initializer to queue deletion
    of the selected member and create a replacement; initialization rejects that invalidated member
    and removes the original/replacement, board and map without leaked entities. Tampering before
    initialization also discards the preview with an explicit precondition reason. Exception-driven
    initializer failures and live filter changes are covered by the subsequent work below.
  - Initialization verification (2026-10-03): all 170 focused Debug procedural unit tests,
    all seven loaded-server Debug tests and Server Release compilation pass with the existing
    `--no-restore -p:WarningLevel=0` workaround. Temporarily bypassing the post-map-init member
    checks retains the deleting-spawner preview as `InitializedPreview` instead of `Rejected`;
    the new fixture fails on that incorrect result. Source was restored in `finally` and the loaded
    suite passed again. After sequential Release verification, a Debug `--no-incremental` rebuild
    restored consistent shared-output assemblies; the complete 170-unit/seven-loaded corpus passed
    on that final set. Scoped diff and new-file whitespace checks pass. Unsuppressed CI
    warnings-as-errors remains unverified. This proves selected container membership/lifecycle and
    cleanup through initialization; it does not close R02/R06 or prove operational room placement.
  - [x] Revalidate retained managed-slot contents against live whitelist and blacklist declarations
    through `EntityWhitelistSystem.CheckBoth`, alongside actual membership and container identity.
    Apply this both before and after initialization and on subsequent `Verify`/`TryInitialize` calls.
    Do not call insertion permission APIs against an already occupied slot or replay attempt events
    during verification. A locked slot alone does not invalidate compatible retained contents;
    locking controls insertion/ejection. Generic containers still have no equivalent persistent
    filter contract: earlier attempt-event permission is not proof of later operational usability.
  - [x] Add pair-local engine cancellation fixtures, disabled outside explicit test scope and reset
    in `finally`. Cover vetoing the first item-slot permission attempt (preflight), the second
    (commit), and the third generic container attempt (actual managed-slot insertion after both
    earlier checks passed). The last case verifies that an item-slot success return without actual
    membership cannot retain a preview. Each rejection must return no stage and remove all newly
    created entities/map. Verification and map initialization must not replay insertion attempts.
  - Runtime compatibility fixtures (2026-10-03): `RetainedContainerContentsMustMatchLiveFilters`
    changes a real console slot whitelist to require a component the disk lacks, or adds a declared
    blacklist tag to its still-contained disk. Exercise both uninitialized and initialized phases;
    verification rejects each mismatch, then `TryInitialize` discards it with a precondition reason
    and no leaked entities. A locked compatible occupied slot is the positive control.
    `ContainerStageHonorsInsertionVetoesAndDoesNotReplayAttempts` exercises real engine event vetoes
    at all three described points and proves repeated membership verification/initialization leaves
    attempt counters unchanged. These checks cover retained compatibility and disposable rollback,
    not surface placement, layout, actor interaction, pressure, PVS isolation or publication.
  - Runtime compatibility verification (2026-10-03): all 170 focused Debug procedural unit tests,
    all nine loaded-server Debug tests (zero skipped) and Server Release compilation pass with the
    existing `--no-restore -p:WarningLevel=0` workaround. The first content-load run caught the new
    fixture's incorrect lowercase `tag` prototype type; corrected it to the engine's registered
    `Tag` type, then reran the complete loaded suite. Temporarily removing retained filter validation
    makes the incompatible still-contained disk pass `Verify`, and the isolated fixture fails
    (`Expected: False`, `But was: True`). Source was restored in `finally`. Following the sequential
    Release build, a Debug `--no-incremental` rebuild restored consistent shared-output assemblies;
    the complete 170-unit/nine-loaded corpus passed on that final set. Scoped diff and new-file
    whitespace checks pass. Unsuppressed CI warnings-as-errors remains unverified. R02/R06 remain
    open for full placement, surfaces, operational checks, initializer spawn accounting and publication.
  - [x] Add optional `CancellationToken` arguments to owned container preview staging and
    initialization, with distinct `Cancelled` results (`ContainerStageCancelled` and
    `ContainerStageInitializationCancelled`). A pre-cancelled staging request allocates no map.
    Check between selected-member spawns, after insertion preflight/commit, before retention and
    around initialization/verification. Cancellation of an owned initialization request discards
    that preview, including an already initialized one; disposed/unowned handles still return
    `ContainerStageNotOwned` without affecting another preview. Filtered `OperationCanceledException`
    handling requires the supplied token to be cancelled; unrelated exceptions remain `EngineFailure`.
    Cancellation is cooperative: synchronous engine callbacks finish before the next checkpoint;
    it cannot interrupt a running callback, capability inspection or support-plan construction.
  - [x] Exercise exception rollback through real item-slot/container attempt handlers and
    `MapInitEvent`, using a pair-local fixture disabled by default and reset in `finally`.
    Move initialization precondition verification inside exception handling so failure during
    verification also discards the owned preview. Exceptions during staging return
    `ContainerStageEngineFailure`; initialization exceptions return
    `ContainerStageInitializationEngineFailure`. Cleanup still must not alter another retained stage.
  - Exception/cancellation fixtures (2026-10-03):
    `ContainerStageExceptionsDiscardOwnedEntitiesAndPreserveOtherPreviews` throws during each of
    the three container permission/insertion attempts, the second item-slot attempt, and disk map
    initialization. Assert `EngineFailure`, no returned stage, no new surviving entities or map,
    and an unrelated retained preview remains valid. `ContainerStageCancellationDiscardsOnlyTheCancelledPreview`
    cancels before allocation, during preflight, after actual insertion, before initialization,
    during a real map-init callback, and on a previously initialized preview. Assert distinct
    cancellation diagnostics and cleanup, no initialization event for a pre-cancelled initialization,
    and no leaked entities (including initializer-created computer boards). This does not establish
    rollback of arbitrary external effects caused by prototype/event handlers or failure during
    cleanup callbacks; those require separate transaction contracts and fixtures.
  - Exception/cancellation verification (2026-10-03): all 170 focused Debug procedural unit tests,
    all eleven loaded-server Debug tests (zero skipped) and Server Release compilation pass with
    the existing `--no-restore -p:WarningLevel=0` workaround. Temporarily removing discard from the
    initialization exception handler leaves the failed preview active; the isolated exception fixture
    fails (`Expected: False`, `But was: True`). Source was restored in `finally`. A sequential Release
    build followed by a Debug `--no-incremental` rebuild restored consistent shared-output assemblies;
    the complete 170-unit/eleven-loaded corpus passed on that final set. Scoped diff and new-file
    whitespace checks pass. Unsuppressed CI warnings-as-errors remains unverified. Geometry plan
    identity stays v20; the cancellation API does not change a successful geometry plan. R02/R06
    remain open for layer-aware layout, surface mounting, operational validation, spawn accounting
    and publication.
  - [x] Bound owned container preview retention to 16 stages and cumulative allocations per stage
    to a configurable `maxSpawnedEntities` (1-256, default 256), independently of the existing
    64-selected-member bound. Count the map, selected members, insertion audio entities and
    initializer-created entities, including subsequently deleted or moved-off-map allocations.
    `SpawnedEntityCount` and `MaxSpawnedEntities` expose the cumulative charge; successful repeated
    initialization does not count allocations twice. Retention is thus at most 4096 charged allocations
    across accepted container previews. Discard releases a retained-stage slot; lowering a request's
    allocation cap is allowed, raising it above 256 is invalid input.
  - [x] Capture `IEntityManager.EntityAdded` only during synchronous staging/initialization scopes.
    Record UIDs before components/parents exist; never inspect, throw or delete from that callback.
    All new allocations during such an operation belong to its disposable transaction, even when
    handlers place them outside the preview map. Reject surviving off-map generated entities and
    explicitly clean every captured UID in reverse allocation order, then the map. Existing entities
    are never adopted into the allocation journal. Reject nested staging/initialization while a
    capture is active; explicit discard returns false and bulk discard returns zero during that
    engine operation, preventing reentrant map deletion and cross-stage ownership capture.
  - [x] Check allocation caps after spawning, insertion preflight/commit and map initialization;
    return `ContainerStageSpawnBudget` or `ContainerStageInitializationSpawnBudget` with no retained
    output after excess allocations. An impossible map-plus-selected-member minimum and a full
    retained-stage quota fail before allocation (`ContainerStageRetentionBudget` for the latter).
    These are acceptance/checkpoint limits: an engine callback can temporarily allocate more than
    the cap before it returns. They cannot preempt recursive or nonterminating initializer handlers;
    hard callback work limits, deferred spawns, cleanup-time spawns and external side effects remain
    separate transaction work. No room publication or operational guarantee is introduced.
  - Allocation fixtures (2026-10-03):
    `ContainerStageSpawnBudgetsIncludeInitializerEntitiesAndBoundRetention` exercises invalid caps,
    a pre-allocation minimum failure, an initialization cap that counts the real computer board,
    exact-budget success and idempotence, the 16-preview limit, and quota release after discard.
    The baseline includes insertion audio; selected-member count alone is deliberately insufficient.
    `ContainerStageAccountsForOffMapInitializerSpawnsAndCleansThemUp` creates two real paper entities
    in nullspace during disk initialization and verifies both budget failure and off-map rejection
    clean them up, together with selected members, board, audio and map. A callback attempts nested
    staging/initialization and explicit/bulk discard to verify the operation guard.
  - Allocation verification (2026-10-03): all 170 focused Debug procedural unit tests, all thirteen
    loaded-server Debug tests (zero skipped) and Server Release compilation pass with the existing
    `--no-restore -p:WarningLevel=0` workaround. Initial fixtures incorrectly assumed only map and
    selected members were allocated during insertion; real insertion audio exposed that assumption.
    Fixtures now measure the staging baseline and verify the additional board allocation separately.
    Replacing captured-UID cleanup with selected-member-only cleanup leaves the two off-map papers
    alive; the isolated fixture fails (`Expected: True`, `But was: False` for their removal).
    Source was restored in `finally`. After the sequential Release build, a Debug `--no-incremental`
    rebuild restored consistent shared-output assemblies; the full 170-unit/thirteen-loaded corpus
    passed on that final set. Scoped diff and new-file whitespace checks pass. Unsuppressed CI
    warnings-as-errors remains unverified. Geometry plan identity stays v20; these checks establish
    disposable preview allocation accounting and ownership cleanup, not full room publication.
  - [ ] Extend allocation/work budgets and cancellation propagation to all pipeline stages, deferred
    initializer effects and cleanup callbacks. Keep external event side effects and post-initialization
    repairs explicit in the transaction contract before offering publication.
  - Owned container stage verification (2026-10-03): all 170 focused Debug procedural unit tests,
    all six loaded-server Debug tests and Server Release compilation pass with the existing
    `--no-restore -p:WarningLevel=0` workaround. The first isolated fixture run exposed an incorrect
    explicit-pause assumption; the fixture was corrected to test effective uninitialized-map pause
    behavior and actual initialization invalidation. Temporarily bypassing retained container
    membership checks makes tamper detection return `True` when `False` is required. Source was
    restored in `finally`, rebuilt, and all six loaded fixtures passed again. After the sequential
    Release build, a Debug `--no-incremental` rebuild restored consistent shared-output assemblies;
    all 170 unit and six loaded tests passed on that final set. Scoped diff and new-file whitespace
    checks pass. Unsuppressed CI warnings-as-errors remains unverified. These checks establish
    disposable pre-map-init container insertion and ownership cleanup, not usable room placement,
    post-map-init correctness, surface mounting, actor access, visibility isolation or publication.
  - [x] **Owned grid-backed room scene and fixed-actor clearance (2026-10-14).** Complete this
    limited R06 adapter for the 75% milestone; retain every existing support/spatial gate.
    - [x] Allocate detached floor/wall masks on a disposable grid, count grid/wall/generated members
      in one allowance, preserve container parenting and verify exact terrain/frames/lifecycle.
    - [x] Implement fresh bounded initialized scene scanning, hard-fixture overlap, foreign entity
      refusal and fixed-orientation cardinal actor clearance without actor mutation or physics steps.
    - [x] Implement continuous convex translation sweeps, finite segment containment and radius
      handling; preserve declared vault/blocking exclusions and conservative floor bounds.
    - [x] Verify tiny corridor, wall/terrain drift, preallocation failures, exact and exhausted work
      budgets, missed-endpoint obstacles, reverse filter matches, hard overlaps, foreign entities,
      unsupported shapes, vault exclusions and unchanged borrowed actor/world state.
      Thirteen loaded cases and eight sweep units pass on the final source. The terrain case checks
      detached input masks and member/wall overlap using fresh fixture offsets. Neighboring authored
      wall skins form a structural union; test wall movement first unanchors through the engine.
      An initialization spawn-budget failure restores the complete live entity set, including grid,
      map, original member and newly generated contents. Callback checks stop additional allocation
      after grid creation, each tile callback and each wall spawn exceeds the shared allowance.
    - [x] Prove regressions via deliberate source mutations, restore exact source, run final
      Release/fresh Debug/unit/loaded/client sandbox gates, then reassess engine-domain progress.
      Deliberate compile-valid regressions have caught omitted sweep endpoints (`True` becomes
      `False`), ignored reverse mask/layer matches (`Rejected` becomes `Observed`) and bypassed
      terrain verification (all four tamper cases return `True` instead of `False`). Exact original
      bytes are restored in `finally`. Evidence: `.ks_procgen_room_scene_mutation_sweep_assertion.log`,
      `.ks_procgen_room_scene_mutation_filter.log`, `.ks_procgen_room_scene_mutation_terrain_assertion.log`
      and their assertion-bearing TRX results. A first case-name filter matched no tests and is not
      counted as evidence; the corrected terrain run executed and failed all four intended cases.
      Final server Release `--no-incremental` passes with 574 existing warnings and zero errors;
      integration Debug `--no-incremental` passes with 1,107 existing warnings and zero errors.
      All 513 procgen unit cases and 164 focused loaded/client-sandbox cases pass, zero skips.
      No warnings refer to the new scene/sweep sources. Evidence:
      `.ks_procgen_room_scene_final_release2.log`, `.ks_procgen_room_scene_final_debug2.log`,
      `.ks_procgen_room_scene_final_units.log`, `.ks_procgen_room_scene_final_integration.log`
      and `Content.IntegrationTests/TestResults/ks_procgen_scene_final.trx`. Scoped diff/new-file
      whitespace checks pass. All four KS modules are clean and 0/0 against locally recorded
      `origin/main`. Full repository tests and unsuppressed warnings-as-errors CI remain unverified.
      Engine-domain coverage rises from 9 to 13 points; total is approximately 75% (45-80%
      uncertainty). The requested intermediate milestone is complete; the full R06 rewrite is not.
- [ ] **R07 - Update identity, diagnostics, author tools and release evidence.** Extend semantic
  hashes and rollback state in `KsProcgenPlan`/`KsProcgenGeometryPipeline`, reports in
  `KsProcgenPlanningReport`, and future T16 tools for blobs, relation/support witnesses and actual
  versus requested connection matrices. Bump the generator contract version; preserve canonical
  normalization and independent randomness. Add an editor/example workflow for painted blobs,
  generic/numbered ports, alternative shorthand lines, and reusable table/device/seat bindings.
  Add A43-A52 and mutation checks that deliberately join forbidden groups or bypass support/seat
  constraints. Exit: A51-A52 and the affected existing corpus pass, budgets/limitations are recorded,
  and the ledger distinguishes plan-only checks from actual materialization verification.
  - [x] **Bounded author reports, JSON/SVG exports and paint inspection (2026-10-14).** Add
    detached owned diagnostic snapshots with cumulative work, failed-attempt journals, geometry,
    room/content provenance and unverified engine constraints. Clone nested lists and enforce
    separate entry/geometry truncation. Reject incomplete candidate artifacts. Export version-one
    canonical JSON, verify exact fresh-report bytes and return no partial bytes on exhaustion.
    Render escaped, invariant SVG schematics with room/door/path/preserved markers, furniture
    movement labels, facing and truncation warnings. Register/localize the Debug-only paint
    snapshot command and publish fixture-derived review samples with explicit regeneration.
    - [x] Seven unit cases cover rejection telemetry, independent truncation and retained totals,
      incomplete candidate refusal, detached request/nested fallbacks and invalid limits.
    - [x] Four loaded cases cover exact L-shape export/replay and tamper rejection, culture-neutral
      bytes, escaped script-like names, atomic byte exhaustion, facing/truncation display and
      registered/localized paint snapshot export with `planGenerated == false`.
    - [x] Final Release (512 existing warnings, zero errors), fresh Debug (1,107 existing warnings,
      zero errors), all 505 procgen units and all 151 focused loaded/client-sandbox cases pass.
      Three compile-valid deliberate regressions catch aliased nested fallback lists, a false
      engine-verification claim and a bypassed byte limit; each source is restored in finally.
      Evidence: `.ks_procgen_diagnostic_release.log`, `.ks_procgen_diagnostic_final_debug.log`,
      `.ks_procgen_diagnostic_final_units.log`, `.ks_procgen_diagnostic_final_integration.log`
      and `.ks_procgen_diagnostic_mutation_*.log`. Diagnostics advances from 8 to 11 points;
      overall estimate is 71% (45-75% uncertainty). The 75% milestone remains active.
### Phase A: foundation and geometry

- [ ] **T01 - Freeze contracts and build minimal fixtures.** No dependencies. Define request/result
  enums, policy defaults, field validation, and fixture resources for A01-A04/A11. Record reviewed
  choices from section 18. Exit: schemas deserialize, reject malformed requests, and clearly separate
  hard conditions from permitted relaxations; this does not require generation yet.
  - Implemented in this slice: optional soft, nonoverlapping room-size count bands and a typed
    exterior-window fraction/count goal with default 0.25 fraction; normalization rejects invalid
    bands, nonfinite fractions, and contradictory count bounds.
- [ ] **T02 - Implement mask normalization and transforms.** Depends on T01. Cell lists, rectangles,
  text masks, ownership/dispositions, integer quarter-turn transforms, holes, negative coordinates,
  budgets, and island detection. Exit: A01/A11 geometry/A12/A16 mask cases pass without engine
  spawning; a 1x3 request is never rejected merely for size.
- [ ] **T03 - Implement deterministic plan storage and replay identity.** Depends on T01-T02. Plan
  records, undo journal, stable iteration/seed streams, content hashes, work counters, and report
  serialization. Exit: reversible speculative edits leave identical plan hashes and random stream
  test vectors are pinned.

### Phase B: authored content and alternative selection

- [ ] **T04 - Implement markers and template inspection.** Depends on T02-T03. Anchors, explicit
  ports/approaches, actual footprint extraction, collision/gas classification adapters, and authored
  repair permissions. Exit: malformed/unmarked openings and internally blocked room templates are
  identified with coordinates; marker metadata is available without spawning output.
- [ ] **T04a - Implement room-theme and pack schemas.** Depends on T01/T03-T04. Reusable tile, wall,
  lighting, and entity packs; inheritance; typed goals/filters; assembly relations; compatible
  fallback themes. Ship a small office theme using simple defaults and optional desk/chair
  assemblies. Exit: A31 schema cases pass; a pure-fill request can reference one theme without
  listing materials or entity positions itself.
- [ ] **T05 - Implement atomic layout definitions.** Depends on T04. Members, nested choices, exposed
  port mappings, claim/residual/void masks, common external contracts, scoped exclusions/requires.
  Exit: A02/A03/A04/A21 definition validation and full package rollback tests pass.
- [ ] **T05a - Implement automatic fit proposals and exterior scoring.** Depends on T02-T05.
  Library filtering, inspected facade signatures, directed-edge/corner alignment, lazy integer-origin
  enumeration, footprint checks, fit/fragmentation scores, and probe budgets. Exit: A38/A42 candidate
  cases pass; candidates are found without authored target slots or anchors.
- [ ] **T05b - Implement constant-region contracts.** Depends on T02-T05. Exact masks/transforms,
  immutable content fingerprints, footprint reservations, ports, and explicit outside seams. Exit:
  A21/A40 planning cases reject conflicts and retain constants through every rollback/fallback.
  - Implemented: named constant request fields, exact local-mask transform, overlap/target/limit
    checks, named preserved cell claims before candidate search, rollback persistence, and
    declared source/fingerprint inclusion in stable plan hashes, and a separate seed-independent
    constant-contract hash. Declared one-cell entryways transform with the mask and participate in
    residual routing and opposite-port matching. Successful pipeline results explicitly flag
    unverified constants.
  - Remaining: inspect actual authored content, fingerprint, and entryways; full entity/support
    footprint reservation, seams and operational access, plus unchanged staging/copy verification.
- [ ] **T06 - Implement bounded automatic placement search.** Depends on T03-T05b. Joint selection
  of room type, origin, rotation, family alternative, and residual assignment; constrained-cell
  branching, fit-ranked alternatives, counts, and conflict reports. Exit: A37-A42 planning cases
  pass with route/fill integration in T08-T09; a selected L alternative cannot contain four-room
  members in its gap. No fixed slot plan is required, and budget exhaustion is explicit.

### Phase C: traversal and pure fill

- [ ] **T07 - Implement traversal graph and invariant validator.** Depends on T02/T04. Reference
  actor, cardinal clearance, doors/access states, room-local versus global graphs, roots, and port
  destinations, plus behavior-based clean-passage classification excluding anything that normally
  blocks movement and requires vaulting (including tables/desks), and any climb route.
  Exit: A07-A10/A19/A36 distinguish valid access from disconnected doors or vault-only paths.
  - Implemented: abstract declared-room/port/passage connected groups, root counts, and rooms
    with no declared ports. `SingleNetwork` planning rejects multiple groups. This report does
    not certify the actual prefab interior, collisions, or operational doors. The composed
    pipeline now checks against partitioned floor rather than pre-partition procedural claims;
    proposed walls cannot serve as walkable connections.
- [ ] **T08 - Implement required routing and residual connectors.** Depends on T06-T07. Route
  reservation, feasible component connections, explicit link obligations, proximity preferences,
  width handling, and permitted stubs. Feed failure back to package search. Exit: A05-A10 pass on
  plans; required entrances beyond the optional search radius are still serviced or fail.
  - Implemented: direct opposite-port pairs and exposed ports join the abstract network through
    procedural or caller-inspected passage components; invalid facts and node-budget exhaustion
    have distinct statuses. The partition-aware report also checks protected residual passages
    and each proposed door threshold/approach before accepting the floor network.
- [ ] **T09 - Implement tile-resolution pure fill.** Depends on T07-T08. Connected growth partitions,
  tile-thick seams, merging, sparse fallback, and exact residual coverage. Exit: A01/A11/A12/A18 pass
  at the planning level in `Procedural` and `Hybrid`, including one-cell fragments. No artificial
  room size lattice is present; A37/A39 hybrid boundary strips match the requested exterior.
  - Implemented: complete procedural-claim coverage, reserved-passage protection, seeded connected
    zone proposals, cardinal interface discovery, narrow/tiny passage fallback, and bounded rejection.
    A conservative seam planner reserves tile-thick partition cells with one opening per interface;
    it checks clean room and island connectivity, merges only the two zones around a bad split,
    retries, and reports a bounded open-floor fallback if its merge budget expires. Optional soft
    size bands propose large, medium, and small zones together; the plan reports both preliminary
    assigned counts and final themed-room counts after seams/merges, excluding passages. Authored
    size-goal order is the preliminary proposal priority; these counts are soft, not hard guarantees.
  - Next: derive final port/root obligations, materialize real walls/doors, and validate actor
    access and closure. Current material choices are exact plans, not spawned map content.
- [ ] **T09a - Implement theme assignment and coherent material palettes.** Depends on T04a/T09.
  Per-room weighted themes, compatible tile/wall families, shared seam ownership, and theme
  feasibility feedback. Exit: A27 material checks/A29/A31 pass at plan level; tiny fragments use
  the primary palette, and incompatible wall families cannot bypass structural constraints.
  - Implemented: assign the selected theme to final room/passages after seam merges, keep optional
    furniture off passages, record unplaced required packs, and choose exact primary/accent floor,
    interior-wall, and door prototypes. Reject a wall family that cannot supply a required door.
  - Next: reconcile interior/hull seams with actual entity footprints and place the planned tiles,
    doors, and walls. Prototype choice alone does not verify a door's access or pressure behavior.

### Phase D: structure and output

- [ ] **T10 - Implement hull planning and gas validation.** Depends on T04/T08-T09a. Real airtight
  boundaries, holes, host-versus-room closure scope, external access contracts, and unknown-context
  detection. Exit: A13-A14 catch planned leaks and impossible tiny sealed interiors; route and hull
  reservations never overwrite one another.
  - Implemented: exact cardinal boundary inventory for generated floor, distinguishing writable
    envelope cells, explicit void, preserved/unknown neighbors, missing footprint structure, and
    unknown host context. A 1x3 footprint without an envelope reports all eight unresolved sides.
  - Implemented: R05's independently revalidated owned inventory includes preserved/constant floor,
    reserves complete entrance/host spans, and selects compatible registered hull-wall prototypes
    only for explicit writable envelope claims. Missing/unknown shell remains unresolved.
  - Remaining: complete pipeline composition, window substitution, real gas-blocking inspection,
    resting door states, and engine atmosphere validation before claiming spaceproofing.
  - Implemented: a separate static gas-graph checker for explicit cardinal edge states in one
    nominated resting door state. Missing edges remain unknown; open edges to declared vacuum
    fail, blocked edges stop reach, and only complete known snapshots can be preliminarily closed.
    This cannot satisfy the engine atmosphere exit criterion by itself.
- [ ] **T11 - Implement window selection.** Depends on T10. Stable eligibility, fixed versus editable
  counts, scopes/tolerances, airtight substitutions, and diagnostics. Exit: A15 passes and window
  placement preserves traversal and closure.
  - Implemented: a typed request-level window goal (fraction, hard/soft status, tolerance, and
    minimum/maximum count), plus a side-effect-free selection/counting stage for explicitly
    classified exterior boundary cells. It handles fixed windows, deterministic rounding/choice,
    soft feasibility clamping, hard fraction/count bounds, and per-cell exclusion reasons. With
    caller-inspected boundary facts inside the requested target/envelope, the pipeline exposes
    and hashes a provisional window plan and rejects a missed hard goal. It flags hard window
    goals as unverified until operational placement and airtightness checks exist.
  - Implemented: R05's owned adapter validates explicit directional exterior facts against current
    hull geometry, tracks missing context, counts preserved/constant windows without writes,
    resolves theme window prototypes and replaces exact editable wall choices under shared budgets.
  - Remaining: actual exterior/prototype inspection, complete pipeline and scope-aware constraints,
    live airtight window substitution, and post-placement movement/gas checks. Selection is not
    closure validation.
- [ ] **T12 - Implement safe prefab materialization and staging lifecycle.** Depends on T03-T05/T10.
  Prove supported map copying preserves authored data and entity references; isolate startup side
  effects; support cleanup/cancellation and private validation. Include T05b constant-region copying.
  Exit: A16-A17/A20/A24/A40 engine fixtures
  pass. Reject unsupported cloning or live patch cases explicitly.
  - Implemented: a tile-only stage for complete procedural floor plans. It validates exact floor
    ownership, known tile prototypes, and a tile budget before creating an uninitialized, paused
    map/grid; tile readback detects drift, and the owned handle supports explicit discard. Bulk
    cancellation cleans up all owned maps and runs on round restart/system shutdown. Map identity
    is checked before deleting. Prefabs, constants, walls, doors, furniture, lights, and selected
    windows are rejected up front.
  - Remaining: prove stage visibility and startup isolation;
    safely copy authored rooms and constants with references intact; place planned entities;
    validate live traversal, atmosphere, and other engine conditions before any publication.
- [ ] **T13 - Implement final engine validation and publication.** Depends on T07/T10-T12. Reconcile
  actual collision, operational doors, anchoring, and atmosphere with the plan, then publish once.
  Exit: A08-A10/A13-A14/A19/A24 pass against materialized entities; a visually correct but leaking or
  inaccessible result never receives success/publication.

### Phase E: degradation, author tools, and release

- [ ] **T14 - Complete ordered fallbacks and diagnostic reporting.** Depends on T06-T13. Named
  relaxations, original/effective contracts, per-port reasons, reserve budgets, and best-feasible
  results. Exit: A11/A23/A26 assert exact statuses and permitted behavior; no silent requirement loss.
  - Implemented: four explicit permissions for existing bounded soft fallbacks; a disallowed
    outcome fails planning with a stable reason and no semantic hash. The planning report gives
    requested/achieved size and window values, separate hard window count and soft fraction,
    other hard/soft/unverified states, a bounded
    applied-fallback trace, and an explicit unpublished disposition. Denied fallbacks are reported
    as misses rather than applied events.
  - Remaining: an ordered request-wide search across alternatives, shared retry budget and
    reserved sparse pass, individually enabled hard relaxations, full conflict provenance,
    serialized replay/report, and final engine/publication validation.
- [ ] **T15a - Implement lighting packs and supply/coverage validation.** Depends on T09a/T13-T14.
  Fixture selection, spacing/support, working-state checks, bounded coverage estimates, and explicit
  validation of any hard lighting metric. Exit: A29-A30 pass; no unpowered light is counted as
  working, and a basic theme works without requiring a generated station power network.
  - Implemented: deterministic, bounded fixture proposals using the selected lighting pack;
    geometric floor-graph coverage estimates; exclusion of protected passages, furniture, and
    reserved interaction approaches; conservative clean-floor connectivity preservation for
    solid floor fixtures; request-wide fixture count, sparse/budget statuses, and
    semantic replay hashing. The planner never reports verified working coverage.
  - Remaining: inspect mounting/collision and actual supply at placement, measure engine light
    coverage after all entities spawn, validate hard targets, and exercise A29-A30 in live maps.
- [ ] **T15 - Implement coherent entity-pack furnishing.** Depends on T04a/T09a/T13/T14/T15a. Dominant
  activity selection, grouped singleton defaults, atomic relational assemblies, interaction
  approaches, wall-aware machine facing, reserved clean chair access, optional supports, density
  goals, and small-room fallbacks. Exit: A18/A27-A29/A32-A36
  pass with adversarial clutter and narrow paths; removal of optional objects can rescue a layout
  without editing protected rooms. Final lighting coverage is rechecked after furnishing.
  - Implemented: one-cell machine-facing and chair-approach geometry with clean cardinal routes,
    documented facing rotations, backing-wall preference, and vault-required blockers. Initial
    plain-list pack instances are proposed atomically with nearby compatible supports,
    protected door routes, mandatory-failure handling, and sparse optional fallbacks. A selected
    density goal requests up to eight separated dominant-pack clusters; each proposal records its
    cluster index. Accepted chair/machine approach paths remain reserved for subsequent packs.
    Declared footprints of up to sixteen cells rotate in quarter turns, must fit wholly on free
    floor, and block clean routes across every occupied tile when movement requires vaulting or
    is blocked. Density counts footprint tiles; lighting excludes them. Multi-cell machines select
    a reachable interaction approach on an exposed front edge of their rotated footprint. Seats
    still require one-cell footprints. This is a geometric proposal only: engine collision,
    support, and spawn feasibility have not been checked.
  - Next: inspect actual prototype collision, facing, seats, support surfaces, and container
    relations; perform R01-R02/R06 for explicit relational assemblies and improve density/distribution.
- [ ] **T16 - Add preview, validation, replay, and overlays.** Depends on T14/T15a/T15. Expose section 15's
  workflow, diagnostic layers, and exportable report. Exit: an author can reproduce and explain an
  L-versus-four conflict and a 1x3 hull failure without reading server source.
- [ ] **T17 - Add tactical measurements.** Depends on T07/T11/T15. Exposure, nominated-direction
  cover, articulation/bridge chokes, bounded alternate-route checks, and approximation labels.
  Exit: A25's known cases pass; metrics remain optional and budgeted.
  - Implemented: exact movement articulation/bridge graph and bounded choke-side terminal counts;
    bounded sampled exposure through a vision mask; nominated-threat cover and nearby protected
    positions through a separate projectile mask; conservative supercover rays with explicit
    corner ties; named pair resilience when one nominated choke is removed; bounded counts of
    internally cell-disjoint routes; and reachable protected firing-position candidates with a
    clear sampled shot to a distinct target. Results
    label engine behavior unverified and work-budget failures never return partial tactical outcomes.
    R05 now supplies independently revalidated local-network ownership for post-interior movement,
    choke-removal and redundancy queries, shared graph/search/detail/flow budgets and immutable
    measurement identity. Host paths cannot be used as tactical detours between local groups.
  - Next: derive masks from actual prototype/engine properties, compare with live vision/projectile
    behavior, add narrow-clearance runs and cut witnesses, and exercise A25 engine fixtures.
- [ ] **T18 - Add bounded tactical furnishing preferences.** Depends on T17. Propose and score cover
  with hard-constraint revalidation. Exit: A25 and A18 still pass with optimization enabled; users
  can compare the measured change using the same seed.
- [ ] **T19 - Run release validation and publish examples.** Depends on T01-T18. Run the full matrix,
  fixed seed corpus, and pathological budget/cancellation cases; profile representative small,
  medium, and maximum supported requests. Ship the four-alternative example, mixed-size parent,
  arbitrary concave pure fill, hybrid gap fill, and tiny-area examples with documented seeds/results.
  Include a slot-free irregular hybrid map with automatically fitted exterior rooms, and a second
  version preserving one constant landmark across several seeds.
  Include a low-effort office theme demonstrating tile/wall/lighting packs, grouped workstations,
  supporting storage, and gracefully reduced content in a tiny room.
  Exit: required builds/tests pass, replay is stable, and measured budgets are recorded. Any existing
  dungeon adapter has dedicated integration tests before it is advertised as supported.

The functional core is T01-T16; T17-T18 satisfy the requested initial cover/defensibility tooling
without making it a prerequisite for basic room generation. T19 records which release scope is
delivered; if tactical work is deferred, explicitly label it deferred rather than complete.
R01-R07 are mandatory amendments to that core for this revision; historical T completion or partial
geometry tests do not waive them. T19 also ships and verifies the combined A52 authoring example.

For implementation changes, build affected projects in Release and run relevant integration tests
in Debug as required by CONTRIBUTING.md. Include at least one content-load test to exercise the
engine sandbox. Suggested starting commands (adjust the filter to the actual test namespace):

```powershell
dotnet build Content.Server/Content.Server.csproj -c Release
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~_KS14.Procedural"
```

Build `Content.Client` in Release as well when adding overlays. A documentation-only change does
not claim these future implementation gates have been run or passed.

## 18. Review defaults and limits

These are concrete proposed defaults for manual review, so an implementation agent does not need
to invent fundamental behavior:

| Decision | Proposed default |
| --- | --- |
| Smallest generation unit | One tile; no minimum room dimensions. |
| Unspecified boundary interpretation | `Footprint`; extra closure requires explicit envelope cells. |
| Unassigned target cells in Hybrid | Procedural fill; keep-empty holes require `KeepVoid`. |
| Room selection and placement | Automatic fitting from a library; choose room types, counts, origins, rotations, and subdivisions without predefined slots. |
| Exterior preference | Prefer compatible room contours matching directed target boundary edges/corners; fill unmatched hybrid strips procedurally. |
| Constant map areas | Optional explicit immutable masks/content/transforms; fit generated rooms around them across seeds. |
| Choice regions and alignment markers | Optional advanced overrides/library metadata; no target-wide manual placement requirement. |
| Multi-room alternatives | Exactly one atomic layout per choice region. |
| Layout gaps | Owned by the selected layout's fill; never available to a sibling alternative. |
| Movement | Cardinal edges, at least one tile clear, validated against a nominated actor. |
| Connectivity | One network by default; root auto-selection does not promise an outside station connection. |
| Painted area markers | One discrete blob per cardinal component within a grid/channel, with one consistent profile; holes and explicit void stay outside fill. |
| Entrance labels | Generic per-instance string IDs; numbered editor presets are conveniences, not a ten-port limit. |
| Authored entrance configurations | Select one complete alternative; exact components by default, explicit minimum-only mode available. Isolation is local unless requested across host context. |
| Relational furniture | Reusable named-member assemblies; required cores/relations are atomic, optional singleton cores can be omitted independently. |
| Surface placement | Shared XY only via validated support/container capability; support objects remain blocking in clean traversal. |
| Corner/open-facing content | Explicit corner predicate (required or preferred) and a reachable open approach; member rotation may solve independently when declared. |
| Room entrances | Required by default; network preferred, reachable stub allowed unless a stricter destination is declared. |
| Prefab repair | Forbidden except for explicitly marked seams/entities. |
| Station spaceproofing | Required at rest, relying on verified host hull unless independent room closure is requested. |
| Windows | Soft 25% of eligible exterior boundary cells, tolerance one cell. |
| Low-effort pure interiors | Mask + room theme; reusable tile/wall/lighting/entity packs supply content. |
| Entity-pack distribution | One dominant compatible activity per room, coherent nearby clusters, optional support packs. |
| Assemblies | Required core members placed atomically; optional garnish may be omitted. Plain entity lists remain supported. |
| Machine facing | Default down; right-wall placement rotates 90 degrees to face left. At corners, prefer a reachable clear or usable chair approach. |
| Chair access and obstacles | Chairs require a clean cardinal approach. Anything that normally blocks movement and requires vaulting, including tables/desks, blocks clean passages. |
| Lighting | Place supported working fixtures; explicit supply profile and reported coverage/operating state. |
| Impossible tiny interiors | Sparse fill when constraints permit; otherwise explained failure. Solid/unsealed output requires named permission. |
| Determinism | Seed + normalized input/context + content/version + work budgets; independent stage streams. |
| Publication | Validate private staging output before exposing it; live occupied-grid patching deferred. |
| Tactical objectives | Optional, measured heuristics; never override required movement or pressure constraints. |

Revisions to these defaults should update the contracts, fixture expectations, and task acceptance
criteria together. The final implementation must make its limits visible through results and
diagnostics, especially when a requested shape cannot physically support every requested property.
