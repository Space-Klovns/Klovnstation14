<!-- KS14: added in this fork -->
# KS14 procedural generation system specification

Status: implementation in progress. This document remains the behavior contract; the detailed
implementation ledger in section 17 records what exists, what has been verified, and what remains.
An unchecked task is not implemented merely because its API or a partial algorithm exists.

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

**Revision gate (updated 2026-10-05):** R01-R06 are in progress; R07 remains pending. Existing T-row results
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
| R04 | In progress | Complete configuration contracts, shorthand, request transport, DeclaredNetworks roots and replay identity. A bounded seeded selector checks complete supplied scenes without mixing alternatives. Joint room/carving selection and branch journals remain pending. |
| R05 | In progress | Component analysis checks local groups, forbidden joins, host bypasses, roots, sealed thresholds and complete floor assignment with shared work limits. The initial procedural footprint adapter constructs separate cardinal trees with wall dispositions and retries whole configurations. Legacy packing/residual/fill/network entry points reject entrance-aware inputs. Joint room fitting, partition/furnishing integration, fallbacks and live validation remain pending. |
| R06 | In progress | T10/T12/T13/T15: `KsProcgenContainerPreflightSystem` delegates live eligibility to engine APIs. `KsProcgenContainerStageSystem` owns disposable paused container-only previews, inserts members in dependency order, checks actual membership/parent transforms and rolls back failures. Explicit initialization verifies selected lifecycle, membership and managed-slot filter compatibility afterward and is idempotent. Insertion-veto fixtures cover preflight, commit and actual container insertion without replaying attempts during verification. Surface/spatial relations remain unsupported there; temporary root coordinates are not validated layout poses. Remaining: full layer-aware staging, surface mounting, operational and connection checks, spawn accounting and rollback before publication. The tile stage still rejects furniture. |
| R07 | Pending | T03/T14/T16/T19 and all affected fixtures: versioning, hashes, diagnostics, editor examples and regression coverage. |

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
| T10 | In progress | `KsProcgenHullBoundaryPlanner` inventories every cardinal edge of generated floor against the normalized exact masks and known interior walls. It distinguishes writable envelope cells, preserved/unknown neighbors, explicit void sinks, missing footprint hull, and unknown `InteriorFill` host context. For layouts without prefabs, `KsProcgenGeometryPipeline` exposes and hashes this inventory as a diagnostic; a pure 1x3 footprint reports eight unresolved sides. `KsProcgenGasClosure` separately checks a caller-supplied cardinal gas-adjacency snapshot in a nominated door state. It treats missing edges as unknown, distinguishes explicit vacuum leaks from unknown boundaries, respects blocked edges, validates duplicate/reversed edges and budgets, and returns only `PreliminarilyClosed` when all reachable edges are known and blocked as needed. Neither stage inspects engine gas behavior; both verification flags remain false. Remaining: include prefab/constant floor, plan complete shell and exterior ports without overwriting claims, obtain actual gas edges/door states and host context, run materialized gas flood, and A13-A14. | Seven focused Debug tests pass for thin-envelope inventory, missing footprint/unknown host/void exposure, closed strip, missing edge, explicit leak, blocked door, duplicate/invalid inputs, and budget. All 56 focused procgen unit tests and Shared Release build pass. The loaded-server fixture verifies the pipeline hull diagnostic with the temporary prototype override. |
| T11 | In progress | `KsProcgenWindowPlanner` accepts explicit hull/engine-classified boundary cells and computes stable eligible, fixed-window, fixed-wall, editable, requested, and achieved counts. It reports a reason for each excluded cell: missing interior/exterior face, unsupported airtight window, corner, doorway, required structure, or author-disabled segment. Fixed eligible windows remain in the denominator. Seeded editable choices clamp a soft fraction to feasible counts, while hard fraction tolerance and minimum/maximum counts can return `HardTargetUnmet`. Zero eligible cells are `NotApplicable` unless a positive hard minimum is requested. The composed pipeline can consume an optional inspected boundary, reject cells outside the normalized target/envelope, expose the provisional window plan, return `WindowTargetUnmet` on a hard miss, and hash the supplied classification. Without inspected boundary input, the hard goal remains unverified. Hull geometry alone cannot certify window eligibility; no map mutation exists yet and `AirtightnessVerified` remains false. Remaining: derive real boundary eligibility from T10 plus engine inspection, room/segment scopes, multi-cell window modules, prototype airtight capability, traversal/pressure rechecks, and A15. | Focused Debug unit tests pass for fixed-window denominator and replay, per-cell exclusions and hard miss, zero eligible cells, impossible minimum, duplicate rejection, and inspected cells outside the shape. All 73 focused procgen unit tests pass. Loaded-server fixture assertions for pipeline selection and hard miss pass with the temporary prototype override. |
| T12 | In progress | `KsProcgenTileStageSystem` provides the first tile-only materialization slice. It preflights a successful, hashed pure plan for exact procedural floor coverage, known tile IDs, and a tile budget; it rejects prefab/constant claims, interior walls and doors, furnishings, lights, and selected windows. Accepted tiles are placed on a new uninitialized, paused map and grid, then can be read back and discarded through an owned stage handle. Bulk cancellation discards all owned stages; round restart and system shutdown invoke it. Map identity is checked before deleting a stage map. This does not establish map visibility isolation or publishability. Remaining: safe prefab and constant copying with authored entity references, entity/material placement, startup side-effect isolation, engine traversal/gas verification, and A16-A17/A20/A24/A40. | The loaded-server fixture stages and verifies a 1x3 floor, detects a deliberately changed tile, discards one stage, bulk-cancels two more with map removal, handles an externally removed map, rejects a two-tile budget and hybrid content, and leaves the plan unpublished. All 78 focused Debug procgen unit tests, the loaded-server fixture, and Server Release build pass with the temporary prototype override. |
| T13 | Pending | Final engine validation and publication. | Pending. |
| T14 | In progress | `KsProcgenFallbackPolicy` names four default-permitted planning degradations: merged/open partitions, final room-size shortfalls, sparse optional furnishing, and sparse provisional lighting. Disabling one makes the pipeline return `FallbackDisallowed` with a stable reason and zero semantic hash; immutable coverage and access requirements remain enforced. Policy flags enter the semantic plan hash. `KsProcgenPlanningReportBuilder` produces bounded, deterministic constraint outcomes and a trace of applied fallbacks from a pipeline result, distinguishing proposed/degraded/rejected/no-op planning from publication; window count and fraction are separate hard/soft outcomes, while operational access, gas closure, constant source content, and working light remain unverified. Remaining: request-wide retry budgets, alternative/backtracking fallback order, hard relaxation permissions, per-port and per-cell conflict details, verified content/context hashes, serialized reports, and final publication statuses. | 78 focused Debug procgen unit tests pass, including report degradation, separate hard window count and soft fraction, unverified engine conditions, invalid-request reporting, and truncation. The loaded-server fixture passes for all four policy gates, a policy-sensitive semantic hash, and a degraded unpublished report using the temporary prototype override. |
| T15a | In progress | `KsProcgenLightingPlanner` resolves the selected theme fixture and proposes bounded, deterministic positions on free room floor, avoiding protected passages, furniture, and reserved interaction approaches. It greedily estimates cardinal floor-graph coverage from the fixture's preferred spacing; tiny passages or oversized rooms can report sparse fill, and fixture-budget exhaustion has an explicit status. The pipeline tracks a request-wide fixture count and hashes the proposal. `WorkingCoverageVerified` is always false: the estimate does not account for actual light falloff, occlusion, fixture power/startup, or supply validity. Remaining: prototype placement capability, wall/ceiling mounting, live supply checks, actual light sampling after furnishing, hard coverage gate, and A29-A30. | Loaded-server office test verifies deterministic positions, passage/furniture avoidance, estimated target, sparse oversized fallback, capped-budget status, and unverified working state. A 1x3 passage reports sparse light placement. 46 focused unit tests and Shared Release build passed. |
| T15 | In progress | `KsProcgenInteractionFacingPlanner` supplies one-cell and declared multi-cell machine facing plus chair-approach geometry using the documented 0/90/180/270 convention and multi-source clean paths. Machine approaches are chosen from the rotated footprint's exposed front edge. `KsProcgenFurnishingPlanner` proposes atomic, room-local instances of selected plain-list entity packs. The selected theme carries its validated furnishing-density goal; larger rooms can repeat the dominant pack up to eight separated clusters with explicit cluster indices and a soft density-shortfall omission. Declared footprints of up to sixteen cells are validated, rotated in quarter turns, placed entirely on free floor, and recorded with value-comparable occupied cells. Density targets count occupied footprint cells. The planner reserves door and interaction routes against every footprint cell; vault-required footprints block clean passage, and lighting avoids their full extent. Seats remain single-cell until their approach geometry is generalized. The request-wide candidate budget remains bounded; running out during optional density repeats reports a sparse omission. Remaining: authored assembly relations/variants, actual prototype collision/seat/rotation checks, more nuanced density/distribution, engine placement, and full A27-A29/A32-A36 checks. | The loaded-server office fixture covers deterministic repeated workstation clusters, density contrast, distinct cells, final clean approach routes, a bounded density-budget shortfall, desk/chair/computer/storage grouping, doorway protection, sparse tiny optional fill, required-pack failure, candidate budget, and oversized-room fallback. Loaded fixtures verify a two-cell vault-required table stays on free floor, avoids reserved passages and peer footprints, excludes lights from both occupied tiles, replays identically, and rotates into a 1x3 room; a two-cell interactive console obtains a reachable front-edge approach. Focused multi-cell facing tests cover an associated chair and a forced rotation. The focused loaded-server test and 99 focused Debug procgen unit tests pass with the temporary prototype override. |
| T16 | Pending | Preview, validation, replay, and overlays. | Pending. |
| T17 | In progress | `KsProcgenTacticalAnalyzer` computes exact cardinal movement articulation cells and bridge edges on the clean walk graph, with bounded choke-side cell/required-terminal counts. It excludes vault-only cells and reports detail truncation separately from complete graph detection. `KsProcgenExposureAnalyzer` counts visible sampled peers within a radius and reports per-cell fractions and longest clear rays through a caller-supplied vision mask. `KsProcgenCoverAnalyzer` tests nominated threat origins against sampled positions and legal cardinal neighbors through an independent projectile mask. Both use a conservative tile-center supercover ray that inspects both incident cells at corner ties; pair/ray budgets fail without partial metrics. `KsProcgenAlternateRouteAnalyzer` checks named terminal pairs against each nominated unavailable choke cell, reporting endpoint removal, retained connectivity, and shortest remaining distance. It excludes vault-only movement, rejects already disconnected pairs, and caps searches/expanded cells without returning partial outcomes. `KsProcgenRouteRedundancyAnalyzer` counts internally cell-disjoint clean routes with bounded node-split flow; a count below the configured cap is exact, while a capped count is labeled as a lower bound. `KsProcgenFiringPositionAnalyzer` nominates clean reachable cells protected from one threat origin with a clear projectile ray to a distinct target, bounded by candidate, flood, and ray limits. None of these geometric results claims engine traversal, vision, or projectile verification. Remaining: engine-derived masks and validation, selected actor/stance/projectile models, narrow clearance runs, cut witnesses, and A25 engine fixtures. | Eight focused Debug vision/cover tests, four alternate-route tests, three firing-position tests, and four route-redundancy tests pass for clear and blocked rays, separate masks, nearby cover, loop and corridor detours, vault-only blockers, reachable protected firing positions, one/two/three independent routes, bounded lower bounds, empty range, work budgets, and extreme coordinates. All 97 focused Debug procgen unit tests and Shared Release build pass. |
| T18 | Pending | Bounded tactical furnishing preferences. | Pending. |
| T19 | Pending | End-to-end examples and release validation. | Pending. |

### Rough implementation assessment (2026-10-05)

The full specified system is approximately **47% implemented**, with an uncertainty range of
**40-50%**. This is an engineering estimate weighted by the remaining work and acceptance gates,
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
  - [ ] Extend this selected-variant pose/rotation search into joint room search and engine operating validation.
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
    floor and wall records. Identity versions are `ks-procgen-group-route-v1` and
    `ks-procgen-group-route-selection-v1`; unused sufficient budgets do not change accepted identity.
  - Initial constructor boundary: canonical group order and greedy cardinal trees only, without
    group-order/layout backtracking, room fitting or full floor partitioning. Existing clean components
    with no group terminals are rejected rather than automatically attached. Host/preserved completeness
    flags are caller declarations, not live inspection or source-fingerprint proof. No actor, fixture,
    vaulting, wall material, room/door/window/hull, pressure or power validation is added. A rejected
    greedy proposal does not prove that every layout for the shape is impossible. EngineAccessVerified
    remains false; generation guards remain active until all downstream stages preserve these groups.
  - [ ] Extend group floor/wall ownership and protected routes into compatible room/prefab/constant
    selection, partitions and branch journals. Add alternate group-order/layout search where needed.
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
- [ ] **R07 - Update identity, diagnostics, author tools and release evidence.** Extend semantic
  hashes and rollback state in `KsProcgenPlan`/`KsProcgenGeometryPipeline`, reports in
  `KsProcgenPlanningReport`, and future T16 tools for blobs, relation/support witnesses and actual
  versus requested connection matrices. Bump the generator contract version; preserve canonical
  normalization and independent randomness. Add an editor/example workflow for painted blobs,
  generic/numbered ports, alternative shorthand lines, and reusable table/device/seat bindings.
  Add A43-A52 and mutation checks that deliberately join forbidden groups or bypass support/seat
  constraints. Exit: A51-A52 and the affected existing corpus pass, budgets/limitations are recorded,
  and the ledger distinguishes plan-only checks from actual materialization verification.

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
  - Remaining: complete floor ownership, shell material/port reservations, real gas-blocking
    inspection, resting door states, and engine atmosphere validation before claiming spaceproofing.
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
  - Remaining: T10 boundary classification, scope-aware constraints, real airtight window
    substitution, and post-placement movement/gas checks. Selection is not closure validation.
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
