<!-- KS14: added in this fork -->
# KS14 procedural generation system specification

Status: proposed design for manual review. This document specifies future behavior; the types,
prototype formats, commands, and guarantees below are not implemented merely by documenting them.

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
| `Prefabs` | Select authored rooms/layouts; generate structural seams and required connectors, but no procedural room interiors. Uncovered cells must have an explicit disposition or fail. |
| `Hybrid` | Place authored rooms/layouts and procedurally fill all designated remaining areas. |

Shapes may be supplied directly as a mask, authored using markers, or produced by a shape provider
inside an explicit bounding mask. Once normalized, every mode uses the same cell representation.
Shape providers are optional adapters; a caller never needs to use a rectangle, noise, or a room pack.

Required capabilities:

1. The same region can offer a large-room layout, several medium rooms, or many small rooms.
2. A single output can contain large, medium, and small rooms in different, nonoverlapping regions.
3. Alternatives comprising multiple rooms are selected and rolled back as complete layouts. Three
   small rooms forming an L and four small rooms with different connections cannot be mixed.
4. Authored empty areas can be filled procedurally, including narrow remnants between prefabs.
5. Every operational room entrance has a walkable destination; a reachable dead-end passage is valid.
6. Every pair of operational entrances to one logical room is connected through that room by a
   cardinal walkway at least one tile wide. Required global connections are also validated.
7. Spaceproofing, exterior window proportion, and traversal are explicit constraints. Cover,
   sightlines, and defensibility are configurable secondary goals.
8. Impossible requests produce a bounded, explained fallback or a failure without publishing a
   broken layout. No retries continue indefinitely.
9. Low-effort procedural interiors use room themes that select tile, wall, lighting, and entity
   packs. Related entities aggregate into coherent room contents instead of independent uniform
   scatter. Theme goals are specifiable preferences with explicit outcomes and fallbacks.

Version 1 covers generation before players enter the output. It includes preserved authored rooms
inside a new generated host. Arbitrary regeneration of an occupied live station, multi-grid routes,
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

## 4. Data and authoring contracts

The following are proposed contracts, not current engine APIs. Serialized names use camelCase;
IDs use PascalCase. Implementation may split types for clarity but MUST preserve these semantics.

### 4.1 Generation request and profiles

| Field | Contract/default |
| --- | --- |
| `requestId`, `seed`, `generatorVersion` | Required replay identity; version names the algorithms, defaults, and random implementation. |
| `mode` | `Procedural`, `Prefabs`, or `Hybrid`. |
| `shape`, `geometryMode` | Normalized masks from section 3; `Footprint` default. |
| `theme` | Floors, walls, airtight windows, doors, allowed furnishings, and classification adapters. Required for materialization. |
| `roomThemes` | Weighted room-theme pool or one fixed theme; request theme supplies the default room theme when omitted. |
| `themeGoals` | Typed appearance, lighting, and content goals; override theme defaults within request-hard constraints. |
| `fixedPlacements` | Required authored room/layout instances with explicit transforms; default empty. |
| `choiceRegions` | Region instances offering interchangeable layout alternatives; default empty. |
| `anchors`, `externalPorts` | Stable local alignment and connection metadata; default empty. |
| `rootCells` | Traversable access roots; supplied roots are hard requirements. If absent, choose the lexicographically first usable cell per permitted network and report that external station access was not established. |
| `connectivityPolicy` | `SingleNetwork` by default. |
| `traversalProfile` | Reference actor collision footprint, door access capabilities, and required operational states. Required; theme may supply a default. |
| `constraints` | Named hard conditions and soft targets, with scope and tolerances. |
| `fallbackPolicy` | Ordered, explicitly permitted degradations. Default permits soft-target misses and one-tile sparse fill; it never disables a hard condition. |
| `budgets` | Tile/candidate/search/path/repair/decoration/tactical limits from section 14. |

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

An authored room with mutually disconnected entrances is invalid as one logical room. It must be
repaired by its author or described as multiple logical rooms inside one atomic layout. A metadata
claim that a path is clear never overrides inspection of the actual map entities.

Default repair permissions prohibit deleting walls/furniture, moving doors, or modifying authored
entities. Authors may mark specific cells/entities as replaceable and specific wall spans as
generator-owned seams. The generator may edit only those locations and must report the edits.

### 4.3 Layout and choice region

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

An optional assembly specifies relational placement around an anchor entity rather than a whole
room layout: for example, a desk, a chair facing the desk, and papers on its surface. Relations can
be `AdjacentTo`, `Facing`, `OnSurface`, `InContainer`, or `Near`, each with explicit distance/range
and allowed orientation where needed. Surface capacity, item size, seat use position, interaction
approach, mounting support, and engine-valid container relationships must be checked. Contradictory
or cyclic unsupported relations fail pack validation.

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

### 4.7 Minimal authoring example

The desired end-user workflow is a mask plus a reusable room theme. This conceptual syntax names
new prototype types and example pack IDs to implement; these resources do not yet exist:

```yaml
- type: ksProcgenRoomTheme
  id: KsSimpleOffice
  tilePacks:
  - pack: KsOfficeFloorTiles
    weight: 1
  wallPacks:
  - pack: KsStationOfficeWalls
    weight: 1
  lightingPacks:
  - pack: KsOfficeWorkingLights
    weight: 1
  entityPacks:
  - pack: KsOfficeWorkstations
    weight: 4
  - pack: KsOfficeStorage
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

## 6. Atomic alternatives and blacklist rules

### 6.1 Choose complete packages

The primary solver variable is the selected layout for a region, not an independent room for each
marker. Selecting an alternative atomically claims its reservation, member IDs, connections,
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

This is a logical sketch of one region divided into four possible subareas. Letters represent
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

Conceptual authoring syntax (new schema to implement; not loadable by the current engine):

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
3. Build transformed candidate placements. Filter by bounds, reservations, anchors, required port
   reachability potential, seam ownership, and exclusions. Reserve closure/approach space early.
4. Place fixed required instances. Select the unassigned region with the fewest feasible choices;
   break ties by stable region ID. Order alternatives by a seeded weighted permutation without
   replacement. Zero-weight alternatives are disabled; a required region with none enabled fails.
5. Tentatively claim an entire alternative. Propagate occupancy, count, port, and exclusion
   constraints. Use optimistic reachability through carveable cells to prune impossible branches.
   Optimistic reachability is a pruning aid, never proof that the final layout works.
6. For each complete candidate assignment, derive residual masks, choose room partitions, reserve
   paths, and construct a valid enclosing structure. A failure returns a conflict to the solver.
7. Resolve room themes and structural pack compatibility during step 6, including required assembly
   and lighting feasibility. Validate and retain candidate plans satisfying these hard obligations.
   Select the best structural plan under the search budget, preferring fewer required relaxations,
   then structural soft targets (size mix, connections, requested geometry), then a stable tie-break.
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

## 8. Traversal, entrance destinations, and residual connections

### 8.1 Two traversal graphs

Build a cell graph from the final collision plan: nodes are cells usable by the reference actor;
edges are valid cardinal steps with sufficient physical clearance. A tile labeled "floor" is not
automatically traversable: fixtures, furniture, door frames, and neighboring overhangs count.
Door transitions require that the actor can actually open/pass the door in the requested operational
state. Merely ignoring all closed doors is not an acceptable access test.

Maintain a separate room/port graph for author intent and diagnostics. Validate both graphs:

* **Room-local invariant:** within each logical room, all operational inside approaches and
  thresholds connect to each other through that room's usable cells. Do not satisfy this by exiting
  the room and walking around through a hallway. A room with one port must have a reachable interior
  landing; a zero-port decorative room cannot be counted as a required accessible room.
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
or furniture. Optional additional paths create loops after required connectivity succeeds.

For global routing, begin with components already connected through rooms and existing passages.
Search for feasible paths joining different components, commit the least-cost viable candidate,
recompute components, and repeat until the required network is connected. Include required explicit
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
2. Build and reserve the required route network in that usable mask. Select a deterministic usable
   root if none is supplied. A narrow target may consist entirely of passage.
3. Propose room partitions using connected seeded region growth. Select seed cells in stable seeded
   order; expand each region through cardinal unassigned neighbors until its target area or boundary
   is reached. Absorb remaining fragments into compatible neighbors or treat them as small spaces.
   Every cell is handled; fragments are not discarded because they miss a preferred room size.
4. Propose partition walls on available cells between regions, with door openings where required.
   Tile-thick partitions must fit around reserved routes. Reject a split when its walls would destroy
   an interior landing, mandatory route, or requested minimum area. Merge regions when necessary.
5. Derive ports for the accepted partition doors and validate local and global connectivity. The
   initial route network remains protected throughout this stage.
6. Add theme content only in available cells using section 9.3, then perform the final checks.

Large rectangular areas may later use BSP or another partition proposal algorithm; every proposal
still operates within the exact mask and passes the same constraints. No partition algorithm can
weaken the one-cell resolution guarantee. Partitions express desired variety; a single unpartitioned
open space is a valid fallback when room-count targets are soft.

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
* Request-hard: required ports/links/roots, connectivity policy, required spaceproofing, minimum
  usable area/counts, access profile, and any explicitly hard window/size/theme constraint, including
  mandatory functional assemblies and lighting coverage.
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
   effects disappear with that layout. Required fixed placements remain required.
5. Replace an optional region's layout with sparse procedural fill only if that region declares a
   procedural fallback and the request mode allows it. Required prefabs cannot disappear silently.
6. Merge procedural subdivisions into one sparse connected interior, keeping required routes and
   closure. Hard room-count/minimum-area requirements still apply.
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
* Per-cell ownership, remaining void, usable area, requested/achieved size distribution.
* Per-port destination, routes, stubs/seals, actor profile, and network roots/components.
* Constraint results (`Satisfied`, `Missed`, `Relaxed`, `NotApplicable`, `Unverified`) with original
  values, achieved values, and cell/room/port IDs.
* Gas-boundary and operational-door-state checks, window denominator/counts, tactical metrics.
* Resolved room themes/palettes, entity-pack and assembly counts, mandatory/optional omissions,
  cluster membership, interaction approaches, lighting fixture supply/state, and coverage estimates
  versus validated coverage.
* Search counters, fallback trace, timings, publication state, and actionable conflict diagnostics.

Example reason codes: `MaskTooSmallForHull`, `PortApproachBlocked`, `RequiredLinkUnroutable`,
`RoomPortsDisconnected`, `AccessProfileRejectedDoor`, `AlternativeConflict`, `WindowTargetMissed`,
`UnverifiedBoundary`, `TemplateCopyUnsupported`, and `WorldChangedDuringGeneration`.

### 13.4 Materialization and isolation

Version 1 materializes into an isolated, paused/nonpublic map or grid controlled by the generator,
including copied preserved context needed for validation. Use supported engine serialization to
retain authored overrides/references; reject unsupported template content explicitly. A prefab
instance must not share entity references with its atlas or another instance.

Apply tiles, structural entities, ports/doors, authored room contents, optional generated content,
and decals in a deterministic order compatible with engine initialization. Resolve references,
anchoring, containers, and map initialization before validating actual collision and airtightness.
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
and fixed rooms assembled in staging are fully supported by version 1.

## 14. Determinism and performance

For identical normalized input, relevant context, content versions, seed, generator version, and
work budgets, produce the same plan and diagnostics ordering. Entity UIDs and wall-clock timings
are not part of the semantic plan hash.

Use a pinned random algorithm and stable seed derivation with test vectors. Derive independent
streams from the root seed, stable region/room ID, and stage name (selection, routing, windows,
theme palettes, lighting, furnishing). Do not use runtime `GetHashCode`, dictionary iteration order, global game RNG, entity
UIDs, thread timing, or default random seeding. Extra decoration draws must not change room choices.
Quantize solver costs/scores where needed so tie-breaking is specified and reproducible.

Initial configurable defaults, to be profiled before production tuning:

| Budget | Initial default | Exhaustion behavior |
| --- | --- | --- |
| Total target + envelope + inspected context cells | 65,536 distinct cells | Reject oversized request before allocation/search. |
| Candidate placements | 20,000 | Stop enumeration with a budget diagnostic; do not silently omit alternatives and claim exhaustive search. |
| Search node expansions | 50,000 across all branches/fallbacks | Return best feasible plan or `BudgetExceeded`. |
| Path node expansions | 2,000,000 total | Same; never drop a required port to meet the cap. |
| Repair proposals | 256 total | Escalate to next permitted fallback within remaining budgets. |
| Furniture/cover proposals | 4,096 total | Stop optional additions, validate current plan. |
| Visibility sample origins / radius | 512 / 16 tiles | Deterministically subsample and report sample coverage. |
| Nested choice depth | 16 | Reject deeper/cyclic input; reject cycles regardless of depth. |

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

1. Draw a target/void mask or select an authored region; choose `Footprint` or `InteriorFill`.
2. Mark fixed rooms, alignment anchors, every entrance, and replaceable seam cells.
3. Define a choice region and add whole-layout alternatives; declare residual areas as `Generate`.
4. Select constraints, allowed fallbacks, room theme(s), pack overrides, and seed. A basic pure-fill
   request needs only a mask and an existing theme; advanced cluster rules remain optional.
5. Preview selected layouts, paths, hull, windows, and diagnostics before spawning the map.
6. Reproduce a reported failure using its request/content hashes and seed.

Overlay layers: target/envelope/preserved/void masks; region claims and selected alternative IDs;
cell ownership; ports/normals/approaches; reserved routes and disconnected cells; pressure leaks;
eligible window cells and chosen windows; cover/sightline/choke samples. Distinct colors need labels
or patterns so diagnostics remain understandable without relying on color alone.
Include theme/pack/cluster IDs, assembly relations, reserved interaction approaches, lighting
coverage, fixture power assumptions, and unmet furnishing goals in the content preview.

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
| A21 | Contradictory tags/requires, nested cycles, missing prototypes, malformed masks, and fixed-placement conflicts fail before spawning. |
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

Unchecked tasks are future work. Each task requires its own reviewable change, updated schema/docs,
and relevant tests. A task is done only when its listed exit criteria hold. Do not mark later phases
done because an earlier placeholder returns plausible-looking rooms.

### Phase A: foundation and geometry

- [ ] **T01 - Freeze contracts and build minimal fixtures.** No dependencies. Define request/result
  enums, policy defaults, field validation, and fixture resources for A01-A04/A11. Record reviewed
  choices from section 18. Exit: schemas deserialize, reject malformed requests, and clearly separate
  hard conditions from permitted relaxations; this does not require generation yet.
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
- [ ] **T06 - Implement bounded placement search.** Depends on T03-T05. Candidate enumeration,
  constraint propagation, stable weighted alternatives, most-constrained-region ordering, counts,
  required placements, and replayable conflict reports. Exit: a forced L alternative cannot contain
  any four-room member, including in its unused quadrant; budget exhaustion is explicit.

### Phase C: traversal and pure fill

- [ ] **T07 - Implement traversal graph and invariant validator.** Depends on T02/T04. Reference
  actor, cardinal clearance, doors/access states, room-local versus global graphs, roots, and port
  destinations. Exit: A07-A10/A19 distinguish valid geometry from disconnected or unusable doors.
- [ ] **T08 - Implement required routing and residual connectors.** Depends on T06-T07. Route
  reservation, feasible component connections, explicit link obligations, proximity preferences,
  width handling, and permitted stubs. Feed failure back to package search. Exit: A05-A10 pass on
  plans; required entrances beyond the optional search radius are still serviced or fail.
- [ ] **T09 - Implement tile-resolution pure fill.** Depends on T07-T08. Connected growth partitions,
  tile-thick seams, merging, sparse fallback, and exact residual coverage. Exit: A01/A11/A12/A18 pass
  at the planning level in `Procedural` and `Hybrid`, including one-cell fragments. No artificial
  room size lattice is present.
- [ ] **T09a - Implement theme assignment and coherent material palettes.** Depends on T04a/T09.
  Per-room weighted themes, compatible tile/wall families, shared seam ownership, and theme
  feasibility feedback. Exit: A27 material checks/A29/A31 pass at plan level; tiny fragments use
  the primary palette, and incompatible wall families cannot bypass structural constraints.

### Phase D: structure and output

- [ ] **T10 - Implement hull planning and gas validation.** Depends on T04/T08-T09a. Real airtight
  boundaries, holes, host-versus-room closure scope, external access contracts, and unknown-context
  detection. Exit: A13-A14 catch planned leaks and impossible tiny sealed interiors; route and hull
  reservations never overwrite one another.
- [ ] **T11 - Implement window selection.** Depends on T10. Stable eligibility, fixed versus editable
  counts, scopes/tolerances, airtight substitutions, and diagnostics. Exit: A15 passes and window
  placement preserves traversal and closure.
- [ ] **T12 - Implement safe prefab materialization and staging lifecycle.** Depends on T03-T05/T10.
  Prove supported map copying preserves authored data and entity references; isolate startup side
  effects; support cleanup/cancellation and private validation. Exit: A16-A17/A20/A24 engine fixtures
  pass. Reject unsupported cloning or live patch cases explicitly.
- [ ] **T13 - Implement final engine validation and publication.** Depends on T07/T10-T12. Reconcile
  actual collision, operational doors, anchoring, and atmosphere with the plan, then publish once.
  Exit: A08-A10/A13-A14/A19/A24 pass against materialized entities; a visually correct but leaking or
  inaccessible result never receives success/publication.

### Phase E: degradation, author tools, and release

- [ ] **T14 - Complete ordered fallbacks and diagnostic reporting.** Depends on T06-T13. Named
  relaxations, original/effective contracts, per-port reasons, reserve budgets, and best-feasible
  results. Exit: A11/A23/A26 assert exact statuses and permitted behavior; no silent requirement loss.
- [ ] **T15a - Implement lighting packs and supply/coverage validation.** Depends on T09a/T13-T14.
  Fixture selection, spacing/support, working-state checks, bounded coverage estimates, and explicit
  validation of any hard lighting metric. Exit: A29-A30 pass; no unpowered light is counted as
  working, and a basic theme works without requiring a generated station power network.
- [ ] **T15 - Implement coherent entity-pack furnishing.** Depends on T04a/T09a/T13/T14/T15a. Dominant
  activity selection, grouped singleton defaults, atomic relational assemblies, interaction
  approaches, optional supports, density goals, and small-room fallbacks. Exit: A18/A27-A29/A32-A33
  pass with adversarial clutter and narrow paths; removal of optional objects can rescue a layout
  without editing protected rooms. Final lighting coverage is rechecked after furnishing.
- [ ] **T16 - Add preview, validation, replay, and overlays.** Depends on T14/T15a/T15. Expose section 15's
  workflow, diagnostic layers, and exportable report. Exit: an author can reproduce and explain an
  L-versus-four conflict and a 1x3 hull failure without reading server source.
- [ ] **T17 - Add tactical measurements.** Depends on T07/T11/T15. Exposure, nominated-direction
  cover, articulation/bridge chokes, bounded alternate-route checks, and approximation labels.
  Exit: A25's known cases pass; metrics remain optional and budgeted.
- [ ] **T18 - Add bounded tactical furnishing preferences.** Depends on T17. Propose and score cover
  with hard-constraint revalidation. Exit: A25 and A18 still pass with optimization enabled; users
  can compare the measured change using the same seed.
- [ ] **T19 - Run release validation and publish examples.** Depends on T01-T18. Run the full matrix,
  fixed seed corpus, and pathological budget/cancellation cases; profile representative small,
  medium, and maximum supported requests. Ship the four-alternative example, mixed-size parent,
  arbitrary concave pure fill, hybrid gap fill, and tiny-area examples with documented seeds/results.
  Include a low-effort office theme demonstrating tile/wall/lighting packs, grouped workstations,
  supporting storage, and gracefully reduced content in a tiny room.
  Exit: required builds/tests pass, replay is stable, and measured budgets are recorded. Any existing
  dungeon adapter has dedicated integration tests before it is advertised as supported.

The functional core is T01-T16; T17-T18 satisfy the requested initial cover/defensibility tooling
without making it a prerequisite for basic room generation. T19 records which release scope is
delivered; if tactical work is deferred, explicitly label it deferred rather than complete.

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
| Multi-room alternatives | Exactly one atomic layout per choice region. |
| Layout gaps | Owned by the selected layout's fill; never available to a sibling alternative. |
| Movement | Cardinal edges, at least one tile clear, validated against a nominated actor. |
| Connectivity | One network by default; root auto-selection does not promise an outside station connection. |
| Room entrances | Required by default; network preferred, reachable stub allowed unless a stricter destination is declared. |
| Prefab repair | Forbidden except for explicitly marked seams/entities. |
| Station spaceproofing | Required at rest, relying on verified host hull unless independent room closure is requested. |
| Windows | Soft 25% of eligible exterior boundary cells, tolerance one cell. |
| Low-effort pure interiors | Mask + room theme; reusable tile/wall/lighting/entity packs supply content. |
| Entity-pack distribution | One dominant compatible activity per room, coherent nearby clusters, optional support packs. |
| Assemblies | Required core members placed atomically; optional garnish may be omitted. Plain entity lists remain supported. |
| Lighting | Place supported working fixtures; explicit supply profile and reported coverage/operating state. |
| Impossible tiny interiors | Sparse fill when constraints permit; otherwise explained failure. Solid/unsealed output requires named permission. |
| Determinism | Seed + normalized input/context + content/version + work budgets; independent stage streams. |
| Publication | Validate private staging output before exposing it; live occupied-grid patching deferred. |
| Tactical objectives | Optional, measured heuristics; never override required movement or pressure constraints. |

Revisions to these defaults should update the contracts, fixture expectations, and task acceptance
criteria together. The final implementation must make its limits visible through results and
diagnostics, especially when a requested shape cannot physically support every requested property.
