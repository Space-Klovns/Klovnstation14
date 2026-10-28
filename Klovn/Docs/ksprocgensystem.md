<!-- KS14: added in this fork -->
# KS14 procedural generation system specification

Rendering contract amendment (2026-10-28): map previews now use `Content.MapRenderer` exclusively.
The bespoke procgen Python/PowerShell/SVG/gallery pipeline is removed. Section 21.4 supersedes older
rendering requirements and historical ledger entries; do not rebuild that retired pipeline.

Status: implementation in progress. This document remains the behavior contract; the detailed
implementation ledger in section 17 records what exists, what has been verified, and what remains.
An unchecked task is not implemented merely because its API or a partial algorithm exists.

Current expanded-scope estimate (2026-10-28): **approximately 69%**, using the fixed weighted
capability index below. This estimates implemented capability, not production readiness.
The active user milestone is **at least 80%** of this expanded scope. Section 17's current
capability index fixes the weights and records the evidence; helpers and documentation do not
earn credit as complete runtime/editor workflows.

Historical implementation estimate (2026-10-14 scope): **approximately 75%**. This is weighted
capability coverage, not production readiness. Section 17's milestone table and detailed rewrite
ledger record the evidence, uncertainty and remaining exits; earlier percentages are historical
checkpoints. R01-R07 remain in progress. Engine access certification and publication remain gated.

Contract revision **2026-10-15** adds configurable initial atmospheres (section 19), automatic
HV/MV/LV distribution (section 20), and a complete in-game authoring workspace (section 21).
These remain incomplete features; section 17 records the implemented utility authoring foundations.
The section 17 review queue R08-R18
records required rewrites and delivery gates. The earlier 75% estimate applies only to its original
scope; the current expanded-scope assessment below supersedes it. Documentation work adds no
implementation credit. Existing section/task IDs are retained to preserve historical references.

Latest verified source-library batch (2026-10-28): automatic packing excludes whole alternative/
rotation pairs that conflict with captured native rotation or anchored-pose constraints, then
selects compatible complete alternatives without mixing their members. Eligibility work across
selected and unselected alternatives is bounded and rejected orientations retain reasons. Complete
terrain/entity preparation still precedes writes. Normal Release passes **918 procedural units**
and **360 selected native/sandbox cases**, including 16 new unit and 10 new native eligibility cases.
This is the bounded internal source adapter; primary library/hybrid generation, physical entrance/
hull certification and publication remain open. Expanded-scope credit stays **69/100**.

Previous verified terrain-composition batch (2026-10-28): native room copying now combines exact
captured floor masks and metadata with the entity graph. Native mirroring/rotation, atlas rebasing,
untouched surrounding cells, cancellation and failed-transaction cleanup are covered. Normal Release
passes **314 selected integrations** (46 new terrain cases); a further Release/six-case check
strengthens anchored-room ordering. Scope/evidence are in section 17. Primary automatic hybrid
integration, physical access/hull certification and publication remain open. Credit stays **69/100**.

Previous verified native entity-copy batch (2026-10-28): captured prefab entity graphs now instantiate
inside an owned paused transaction. Every member is allocated and loaded before parent-first startup;
entity/network/map references stay within each copy, authored overrides merge with captured defaults,
and post-startup verification checks the complete graph. Actual container contents, removals, all
quarter turns, unprototyped members, cancellation and cleanup are covered. Passing normal Release
verification: **268 selected native/sandbox cases**, including **24 new copy cases**. MapInit is
explicitly deferred. This is the component-graph prerequisite; terrain copying, complete hybrid
composition and publication remain open. Credit stays **69/100** and the **80% goal remains active**.

Previous verified generator batches (2026-10-27): hole-aware size-band fitting now supports the
bounded courtyard/mixed-size cases in primary generation. Normal Release passes 906 procedural/
power-storage units and 62 selected native cases; the fixed index awards one distinct layout point,
raising total capability to **69/100**. Extraction schema 3 additionally retains complete immutable
component override bodies; its Release gate passes 181 selected native/sandbox cases with 13 new
image checks. That source-preservation prerequisite earns no separate workflow credit. Native room
graph instantiation now exists; primary library/hybrid composition remains the current implementation task.
Showcase/presentation expansion is closed for this phase. Detailed scope and evidence are in section 17.

Previous verified resource snapshot batch (2026-10-27): manifest schema 11 binds observed resource
paths/states and detached file bytes, including folder/compiled RSI assets and source-only overrides.
Missing or changed bytes reject revalidation; selected and unselected candidate drift retires owned
stages. Normal Release and fresh Debug each pass **891 units / 937 integration and sandbox cases /
31 serialization cases**, with identical inventories and all prior cases retained. The 42 new cases
cover resource observation, immutable bytes, bounded/failed reads, source overrides, empty entity
sprite IDs and owned retirement. Four compile-valid controls cause nine intended assertion failures;
mutated sources restore exactly, and 27 source hashes remain unchanged across the final gates.
Five regenerated native exports pass save/load checks and match installed maps. All 72 sprite-source
hashes and six output identities pass; six sprite PNGs retain exact bytes. Both refreshed browser
overviews pass five-image/two-mode/five-overlay checks. Six historical review files stay unchanged.
The office's 936 resource files total 3,819,355 bytes and fit unchanged limits. Full content closure,
legacy/client/custom schemas, portable replay, operating checks and authoring remain open.
Credit stays **68/100**; the **80% goal remains active**.

Previous preview maintainability batch (2026-10-27): offline entry points now delegate to private
asset/frame/mapping/layer/export/gallery modules with invocation-owned caches and explicit wire-last
composition. Seven presentation tests and five native showcase cases pass; three isolated controls
fail as intended before exact restoration. Normal Release builds and all 33 retained artifact hashes
pass. Section 17 records the server/presentation boundary and remaining workspace work. Credit stays
**68/100**; this refactor supplies no new end-to-end generator or editor capability.

Previous verified inheritance-parent prerequisite batch (2026-10-26): manifest schema 10 now
follows registered entity, tile and other-kind parent chains in both runtime arrays and source
declarations. Abstract parents retain declaration-only authority, including the new tile-declaration
kind. Partial abstract fields are inspected through schema validators without fake complete
instances or missing-required-field failures. Canonical inspection-gap codes retain unrecognized
fields explicitly; recognized world references still reject. The office catalog now has 649 combined
definitions and fits the unchanged capture limits. Its one declared-schema gap is legacy
`femaleMask` on `ClothingUniformSkirtBase`; full content closure remains unverified. Corrected the
native `CarpetBase` destruction-behavior nesting and verified its inherited destruction act.
Normal Release and fresh Debug each pass **891 units / 895 integration and sandbox cases / 31
serialization cases**, retaining all prior 891/862/31 cases. The 33 new cases comprise 32 inheritance
cases and one source override ancestor case. Six isolated controls produce 14 intended assertion
failures before exact source restoration; all 12 source hashes remain unchanged across the gates.
Five regenerated native exports match installed maps, both refreshed office import cases pass,
and 72 sprite-source hashes/six output identities match. All six sprite PNGs retain their previous
bytes, including the cable overlay drawn above all layers. Both browser overviews are refreshed
and checked. Credit stays **68/100**; the **80% goal remains active**. Resource/client/custom-schema
closure, portable assets/replay, operating checks and in-game authoring still require their own exits.

Previous verified typed-reference prerequisite batch (2026-10-26): native manifest schema 9
captures registered typed prototype dependencies across kinds, including nested collections,
abstract reference-discovery templates and map override seeds with distinct provenance. Whole
capture and parent-placement checks reject transitive drift. The measured office closure contains
538 combined definitions; the default bound is now 1,024, with the hard maximum unchanged at 4,096.
Normal Release and fresh Debug each pass **891 units / 862 integration and sandbox cases / 31
serialization cases**, retaining all previous 891/825/31 cases. The 37 new integration cases comprise
29 direct kind cases, six source-map cases and two imported-wire cases. Three compile-valid
negative controls produce 13 expected assertion failures; all mutated sources restore exactly.
Five native exports and installed maps match, 72 sprite-source hashes and six output identities
pass, and both browser overviews are refreshed with all images/modes/controls checked. The wiring
preview draws cables last above scene layers. Capability credit stays **68/100**; inheritance-parent,
legacy/custom/client/resource closure, portable imports, operational validation and editor/replay
exits remain open. `FullContentClosureVerified` remains false and the **80% goal remains active**.

Previous verified generated-hull closet batch (2026-10-25): electrical reservations now share
explicit writable generated-envelope walls while keeping their interiors, entrance thresholds and
approaches on target floor. Immutable claims distinguish partition walls from reused hull, include
structural corners without cardinal exposure, and bind native service-layout inspection. Hull-wall
materials reconcile utility ownership with incident room palettes. Full internal enclosures remain
a fallback. Native 1x4/4x1 wall-substation rooms and a 4x4 floor-substation layout now initialize with
separate wall APCs, complete usable-floor LV coverage and no uncertified occupied LV squares.
Missing, preserved, void and foreign target corners cannot be borrowed; a 1x3 strip still cannot
invent a mandatory two-tile closet plus entrance and outside approach.
Normal Release and fresh Debug each pass **891 units, 825 integration/sandbox cases and 31 native
serialization cases**, retaining all previous **881/817/31** plus **10 unit / 8 integration** cases.
Two isolated compile-valid controls produce **6 unit + 6 native assertion failures**; source restores
exactly before 34 focused unit / 59 focused native positives and both complete gates. All nine
changed C# files retain their captured hashes. Both actual sandbox checks and updated installed-map
checks pass. Five native showcases regenerate, save/load and match their installed resources.
The office now has 32 floor tiles, a hull-sharing furnished 2x2 closet, 1 HV / 23 MV / 8 LV segments,
17 MV targets, coverage of all 30 usable floor cells and no LV 2x2 blocks. All 72 actual sprite-source
hashes and six output hashes/plan associations pass; both browser overviews are refreshed and all
five images, two modes and five diagnostic toggles are checked. Schema-two allocation identity is
now algorithm 3; general planning/review versions remain 10/3 and legacy schema-one context identity
is unchanged. Credit remains **68/100**: this extends the already credited standalone utility/layout
capability. Inspected existing-hull/hosted/hybrid reuse, complete pose/route portfolios, operational
power, default migration, replay/publication and in-game authoring remain open. The **80% milestone
remains active**; approximately **32%** of expanded scope remains.

Previous verified authority/utility-layout batch (2026-10-25): the owned scene now retains the
original effective electrical policy, including theme fallback and explicit-request precedence.
Native inspection binds complete converter claims to captured infrastructure poses and actual
anchored transforms/prototypes/mounts before topology or square-proof checks. MV terminal targets
come from captured poses, not routed device coordinates. Full-band closet reservations now have
stable ordinals and a bounded independent per-network search frontier: later content/device/route
rejection can advance complete geometry while retaining all work spent. Hardware/content-aware
space preferences choose a furnished default room without a size or planning-budget override; the
office showcase now uses the normal named preset. Compact shapes can still select 1x2/2x1.
Normal Release and fresh Debug each pass **881 units, 817 integration/sandbox cases and 31 native
serialization cases**, retaining all previous **873/797/31** plus **8 unit / 20 integration** cases.
One compile-valid control yields **3 unit + 15 native assertion failures**; four source files restore
exactly before complete positive gates. Both actual sandbox checks, ten installed-map cases per
configuration, five native exports and final artifact audits pass. General planning/review versions
remain 10/3; schema-two context identity additionally binds utility allocation algorithm version 2.
Weighted credit rises to **68/100** (layout 17/22) for complete late-failure utility-geometry retries
through native initialization, including independent networks. Original-policy/device auditing and
default comfort ordering are repairs/supporting work and earn no extra duplicated points. Hull
reuse, hosted/hybrid capture, global content/layout optimization, operating power, publication and
in-game authoring remain open. The **80% milestone remains active**; approximately **32%** remains.

Previous verified validation/routing batch (2026-10-24): native electrical inspection now rebuilds
allowed cable cells, MV service targets, eligible LV floor and utility-room identities from the
separately captured terrain/room/passage/door/furnishing/write inputs. A frozen witness cannot
silently omit or substitute those domains. Shared work bounds include reconstruction and comparison.
LV routing algorithm **2** uses shortest cardinal branches, intrinsic occupied-square exclusions and
an independent full-branch check before atomic commitment. The independent exhaustive oracle agrees
on **19,171** 3x3 domain/terminal assignments and **56,745** joins; both 1x1024 orientations fit
100,000 shared routing work units. Whole-layout/consumer/APC/closet ordering is still separate work.
Normal Release and fresh Debug each pass **873 units, 797 integration/sandbox cases and 31 native
serialization cases**, retaining all previous **870/786/31** and adding **3 unit / 11 integration**
cases. Separated compile-valid controls yield **1 unit + 7 domain-audit assertion failures** with
exact source restoration before the final complete positive gates. Earlier combined-control
routing interference is explicitly discarded as domain-audit evidence. Five native showcases and
sprite plan associations are regenerated through the Release tool path; six sprite PNGs and five
SVGs stay unchanged, and HTML changes only the hidden office plan hash. Weighted credit stays
**67/100**: this repairs previously credited validation/routing guarantees. The 80% goal stays active.

Previous verified implementation batch (2026-10-24): electrical profile schema two now reaches the
primary standalone owned generator with the same frozen closet catalog throughout capture,
planning, native staging and project-area generation. Substations occupy dedicated 1x2/2x2 rooms;
APCs occupy walls in served ordinary rooms. Coherent electrical supplies retain clean approaches.
MV backbones include protected circulation, room service crosses and entry branches. LV routing
services the declared usable-floor fraction independently of required real consumers, forbids
avoidable 2x2 blocks and retains checkable four-fixed-terminal certificates for its narrow supported
exception. Native inspection rechecks provider ranges, service coverage, enclosure and topology.
Normal Release and fresh Debug each pass **870 units, 786 integration/sandbox cases and 31 native
serialization cases**, retaining all previous **862/774/31** and adding **8 unit / 12 integration**
cases. A compile-valid combined control causes **8 assertion failures**; all three mutated files
restore byte-for-byte before both complete positive gates. The five regenerated native showcases
include a furnished 2x2 electrical closet, separate wall APC, 14 MV targets and LV coverage of all
23 usable office floor cells, with zero occupied LV 2x2 blocks. Actual repository sprites and
browser-checked gallery/overviews are current. Weighted credit rises to **67/100** (utilities 7/12):
one point for complete native dedicated-closet service, one for coverage-driven MV/LV distribution.
These exits do not award operating supply, hosted/preserved networks, independent circuits, hull
reuse, complete geometric retries, default migration, replay/publication or in-game editor credit.
The **80% milestone remains active**; approximately **33%** of the expanded specification remains.

Previous verified structural batch (2026-10-24): the shared partition pipeline now reserves
mandatory 1x2/2x2 utility-room interiors, enclosing walls, one entrance and clean approaches before
ordinary seam/material selection. Required rooms cannot be lost through merge/open fallback;
ordinary rooms may still merge under policy. Theme assignment checks actual floor interfaces,
revalidates the enclosure and requires the matching frozen closet catalog. Geometry/native
structural tests cover 1x2, axis-swapped 2x1 and 2x2 rooms, independent entrance networks, complete
budget replay and atomic failures. Normal Release and fresh Debug each pass **862 units, 774
integration/sandbox cases and 31 native serialization cases**, retaining all previous 846/766/31
plus **16 new unit / 8 new integration** cases. One compile-valid combined control produces five
assertion failures; all three source files restore exactly and all 24 new positives pass in both
final configurations. All 39 existing showcase/review artifact hashes remain unchanged.
At that checkpoint, weighted credit remained **65/100** for the verified structural subset. It
searched full enclosing bands inside writable floor and proved native structural primitives.
The subsequent owned electrical service batch above supplies frozen closet content, separated
converters, furnishing and MV/LV coverage through the real generator. Hull reuse, complete joint
geometric retries, wider necessity proofs, default migration and operating utilities remain open.

Previous verified electrical authoring batch (2026-10-23): electrical **profile schema 2** now freezes
closet size/theme intent, MV backbone/room-entry targets, LV floor-service fraction/domain and a
strict forbid-or-certify square policy. Explicit upgrade preserves old source data and historical
schema-one identities. Requests, painted blobs, utility drafts, native serialization and project
closet-theme dependency capture retain the new intent. Shared/server context capture explicitly
rejects revised generation until joint closet/routing work can fulfill it. Normal Release and fresh
Debug each pass **846 units, 766 integration/sandbox cases and 31 native serialization cases**,
retaining all previous 825/756/31 plus **21 new unit / 10 new integration** cases. One compile-valid
combined negative control produces five assertion failures; four source files restore exactly and
all 31 new positives pass in both final configurations. Weighted credit remains **65/100**: this
is prerequisite authoring/transport, not actual closet geometry, generous MV routing, LV floor
coverage or necessity proofs. Schema-two default activation and the section 20.3a rollout remain
unchecked; the previous native wiring examples still establish only historical primitive behavior.

Previous verified supported-member review batch (2026-10-23): owned diagnostic schema 3 now reviews all
supported member layers, parent/slot/local-transform metadata, exact operating approaches and
cardinal relational paths. Hidden contents have no world position or exposed footprint. One
projection budget bounds all metadata rows and geometry collections, with source-processing limits
and explicit partial witnesses. SVG review distinguishes layers, facing, paths and contained-item
listings. Planning version 10 adds required-root contradiction pruning, canonical anchor/support/
required-neighbor traversal and early missing-wall-domain rejection. Normal Release and fresh Debug
each pass **825 units, 756 integration/sandbox cases and 31 native serialization cases**, retaining
all previous 825/740/31 cases plus sixteen new ones. Four compile-valid controls produce five
assertion failures; exact restoration and all sixteen positives pass. Populated JSON/SVG/PNG review
samples and five native showcases are regenerated; all six game-sprite PNGs are unchanged.
Weighted credit stays 65/100. The new section 20.3a substation-closet/MV/LV amendment is required
but unimplemented: existing open-room wiring examples retain only historical topology evidence.
In-game authoring, revised utility layout/routing and broader R08-R18 delivery remain open.

Previous verified native wiring foundation (2026-10-22): standalone shared-Main AutoDistribute/WiredOnly
requests now materialize native cables and converters, build bounded owned node groups, and verify
same-tile tier terminals, solid-wall mounts and actual receiver/provider membership. APCs occupy
walls; substation palettes can offer complete floor and wall variants. A 1x2 furnished interior
selects a wall substation when its preferred floor variant cannot fit, retaining clean approaches.
Input-only prototype data, including nodes and item slots, is preserved by an explicit prototype
capture opt-in (image schema 8); ordinary map serialization keeps its existing exclusions. Owned
slot loading uses native eligibility and container events without playing interaction audio.
Zero furnishing density omits optional fill while retaining hard minima. Gas/wiring evidence
commits only after the complete transaction; cancellation and late range drift publish neither.

Normal Release and fresh Debug builds pass with the normal localization hook. Each configuration
passes **825 units, 685 integration/sandbox cases and 31 native serialization cases**, retaining
the previous 820/659/23 inventory with the explicitly replaced obsolete electrical-allocation
guard. The actual sandbox definition passes. Three compile-valid negative controls produce eight
assertion failures and two unaffected passes; source bytes are restored before positive gates.
Five regenerated showcases include loadable native grids, verified export tile/prototype counts,
and browser-checked overlays. Initial office topology is verified before export; post-load topology,
operating supply, hosted/hybrid fitting, stable simulation, publication and the in-game editor remain
open. This earns one distinct utility point (utilities 5/12), for approximately **65/100** overall.
The **80% milestone remains active**; approximately 35% of the expanded specification remains.

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
13. Select room gas mixtures, temperature and pressure through reusable atmosphere profiles;
    initialize actual owned gas-bearing tiles and verify the nominated initial state.
14. Automatically place and connect electrical distribution to eligible room consumers, including
    HV/MV under substations and MV/LV under APCs, with real engine power verification.
15. Author themes, room libraries, packs, relational assemblies, painted areas, entrances and
    utilities through an in-game editor, with inspectable previews, reusable assets and undo.

Version 1 covers generation before players enter the output. It includes preserved authored rooms
inside explicit constant regions of a new generated host. Content mode governs the remaining
generated area; `Procedural` may therefore surround a constant authored landmark without using any
prefabs for its generated rooms. Arbitrary regeneration of an occupied live station, multi-grid routes,
vertical connections, automatic atmos pipe networks/life support, automatic power-plant construction,
and balanced combat simulation are later extensions. Initial gas filling and electrical distribution
are now in scope through explicit profiles. Power sources may be caller-provided or explicitly
authored; wiring cannot create energy. Gas composition, operational power and job access must be
specified rather than inferred from the word "room." A station preset can supply those defaults.

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
| `Content.Client/_KS14/Procedural/` | In-game authoring workspace, inspectors and overlays (section 21); clients do not determine authoritative generation results. |
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
| `envelopeMask` (`E`) | Additional cells explicitly authorized for structural closure, connector approaches or named utility connectors. Empty by default; permission is scoped by purpose. |
| `writableMask` (`W`) | `T union E`, minus preserved cells. Every generated tile/entity footprint must fit here. |
| `preservedMask` (`P`) | Existing/authored cells and entity footprints that must retain their content. They may participate in traversal. |
| `voidMask` (`V`) | Explicitly excluded cells intended to remain empty/space. They cannot overlap `T`, `E`, or preserved occupied content. |
| `contextMask` | Read-only surrounding cells needed to inspect existing connections, obstacles, and atmosphere boundaries. Reading them grants no write permission. |
| `routeReserve` | Planned walkable cells and doorway clearances protected from later blocking content. |

Preserved cells may lie within `T`; they count as already assigned when computing residual fill.
Envelope cells are not an invitation to expand the requested floor area: each use must be tagged as
closure or an explicitly requested movement/utility connector. Unused envelope cells remain unchanged.

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
| `atmosphereProfile`, `atmosphereAssignments` | Default and scoped initial gas requirements; section 19 defines mixture resolution, preservation and validation. |
| `electricalProfile`, `electricalInterfaces` | Distribution policy, cable/device choices and inspected source/network interfaces; section 20. |
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
| `atmosphereProfile`, `electricalProfile` | Optional utility defaults; explicit instance/area/request overrides take precedence without weakening request-hard constraints. |
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
  lighting target. Section 20 adds generated distribution alongside supported self-contained and
  caller-supplied options; every powered light participates in the electrical load/coverage plan.
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
| `furnishingDensity` | Occupied furnishing-footprint cells / usable room cells before furnishing, in `[0,1]`; count overlapping legal layers once. Zero disables optional fill. Hard core/pack minima remain required and may exceed the soft density target. |
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
not arbitrary entity totals. R02's implemented singleton/core behavior supersedes the historical
whole-list atomic implementation; complete layered integration remains required under R09 below.

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
in that configuration. Unknown IDs, duplicate membership, missing operational ports, groups with
neither ports nor roots,
negative/nonfinite weights, and empty eligible configuration sets fail validation. A singleton group
requires a real reachable interior landing/destination; it does not mean "discard this entrance."
Sealing requires the port's `OptionalSealable` permission and cannot erase an unrelated hard link.

Standalone owned requests may supply an empty `EntranceDomain` with no authored entrances. Their
groups use real floor roots, without invented thresholds or host corridors. With no authored
configurations, `PerIsland` creates one rooted group per cardinal target island, using all explicit
request roots on that island or its first canonical writable cell (first target cell if none is
writable). `SingleNetwork` requires those roots in one network and rejects disconnected islands.
`DeclaredNetworks` requires explicit configurations and roots. Authored groups may have no ports
when their root list is nonempty; the independent component audit still rejects forbidden joins,
unreachable roots and unassigned floor. Derivation is bounded to 1024 target islands and existing
contract root limits; preprocessing remains input bounded rather than fully graph-metered.
An absent/null `EntranceDomain` retains the existing legacy-request semantics. Canonical snapshots
retain resolved defaults, and planning does not mutate the authored request. Tiny strips, including
one tile and 1x3, remain proposals without usable-room, gas or engine-access certification.

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
    -> validate required geometry/connectivity -> reserve utility devices and gas domains
    -> place windows/furnishings/lights -> route electrical distribution
    -> validate final plan -> materialize privately -> initialize utilities
    -> engine validation -> publish
```

Routing and closure feasibility feed back into selection: this is not a greedy place-everything
pass followed by an attempt to repair an arbitrarily blocked map.

1. Normalize masks, markers, references, profiles, and budgets. Snapshot preserved geometry and
   port/door states. Hash the relevant content for replay and cache invalidation.
2. Inspect room maps into immutable template records. Resolve actual footprints and preserve map
   data. Reject illegal rotations and rooms whose mandatory paths are already blocked. Combine
   each member's rotation with the proposed family rotation before checking native mounting and
   rotation restrictions. Exclude the complete affected option/rotation, then allow the solver to
   select another complete alternative. Do not repair it by mixing members from other alternatives
   or changing authored member poses. A pose-compatible alternative still needs complete source,
   path, hull and engine validation. Eligibility work is bounded across unselected alternatives too.
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
   and lighting feasibility. Resolve gas domains and reserve substations/APCs, their cable tiles,
   maintenance approaches and source interfaces before optional furnishing. Check mandatory consumer
   loads/coverage and conflicting gas profiles. Validate and retain plans satisfying these obligations.
   Select the best structural plan under the search budget, preferring fewer required relaxations,
   then the exterior/structural score from section 7.2, then a stable tie-break.
   Freeze that selection before optional furnishing; aesthetic random draws do not rank layouts.
8. Complete the selected plan with optional loops, windows, lighting, cover, and coherent entity
   clusters under separate stage budgets. Required window counts and other hard content obligations
   were reserved/proven feasible in step 7. Revalidate collision, sightlines, and gas edges after
   relevant changes; revert optional additions locally when they break an invariant. Validate the
   full plan again. Final electrical routing includes accepted lights and machine consumers;
   an optional powered assembly that cannot be supplied is omitted atomically with its dependents.
   Optional decoration can optimize its own soft goals, never reroll structural
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
Resolve atmosphere domains and reserve electrical converter/cable interfaces alongside placement
and interaction space for required assembly cores and fixtures with hard lighting requirements
before optional decoration. Plan general lighting to cover the usable room/routes,
then add coherent activity clusters and supported task lighting. Re-evaluate coverage after tall
furniture or walls change occlusion. A powered fixture placed on a disconnected wire is not working
lighting; respect the explicit supply profile.

Default placement order is structure/palette, required routes, utility-device reservations, required
assembly cores and their clear approaches, general lighting, optional cluster members, supporting
clusters, loose items, and decoration, followed by final utility routing and validation. Structure
and routes remain protected at every stage; if a required assembly cannot
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

Section 19 separately specifies filling those sealed volumes. Closure alone proves neither gas
composition nor pressure. Section 20 supplies operating power where a nominated door requires it.

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
  hard window/size/theme constraint, including mandatory functional assemblies, lighting coverage,
  requested initial gas state and required operating electrical supply.
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
* Resolved atmosphere profiles, domain/tile volumes, requested/measured mixtures and tolerances,
  electrical layer routes, source/converter/provider/consumer witnesses and load margins (19-20).
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

Initialize gas and electrical networks through section 19/20's owned lifecycle before operational
door, machine, chair and light validation. Pausing or hiding a map is not proof of isolation from
global node groups, power processing, atmosphere, callbacks or network visibility. No speculative
operation may step global simulation to manufacture a successful preview.

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

The 2026-10-15 revision also hashes resolved gas profiles/assignments, electrical policies, source
contracts, cable layers, selected devices and authored operating states. Give utility searches their
own deterministic streams and request-wide work counters. Interactive drafts have revision IDs;
these identify editor edits, not semantic changes when normalized content remains identical.

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

Section 21 expands this workflow into the required in-game workspace. Commands and static exports
remain useful automation/debug interfaces; they do not fulfill the interactive authoring requirement.

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
| A53 | Fill air, pure nitrogen, a custom mixture and vacuum in known enclosed masks. Read every gas-bearing tile back; assert pressure/temperature/fractions and total moles using actual volumes. Repeated initialization is rejected or idempotent, never double-fills. Invalid gases, nonfinite values and inconsistent fractions fail before writes. |
| A54 | Two open-connected rooms with different hard gas profiles conflict; identical profiles merge. Closed compatible doors separate mixtures. Door tiles receive an assignment; null-air walls, immutable map air and space fallbacks are never filled. A reactive mix passes only its declared initial-state check and fails an incompatible stable-at-rest requirement. |
| A55 | Constant-region gas and existing utility entities remain unchanged through success, rejected retries and cancellation. A required fill reaching unknown/unowned gas context fails verification. No global map-mixture replacement or whole-grid reset affects unrelated tiles. |
| A56 | 1x1/1x3 interiors under valid host closure accept requested gas and external electrical service when feasible. An isolated impossible footprint or solid-only output returns the exact failure/authorized degradation without claiming useful pressure/power. |
| A57 | Real HV source, substation, MV, APC, LV and consumer operate as a chain. Remove each required same-tile cable independently, use the wrong tier, unanchor a device, block a terminal direction or leave diagonal-only cable contact: topology fails. Different cable tiers on one tile stay separate. |
| A58 | Default unit-grid LV coverage includes offsets (3,0) and (2,2), excludes (3,1), and covers 29 centered cells including its origin. Check effective range overrides, fractional positions, unrelated grids, wall-crossing reach and anchored receiver eligibility. Never confuse diagonal coverage with diagonal wiring. |
| A59 | Overlapping live/dead and unequal-range providers, startup-order changes and equal-distance ties cannot produce a false intended-circuit claim. Inspect actual provider membership. Adjacent same-tier circuits accidentally merge; a wall cannot isolate provider influence. Surface/contained devices without receiver eligibility fail required supply. |
| A60 | Source shortage, converter supply/charge/ramp limits, breaker disabled/tripped, depleted batteries, startup load and internal battery fallback produce accurate supply reports. Charged batteries or idle operation alone do not prove sustained supply; WiredOnly does not satisfy a hard working-light/door requirement. |
| A61 | Utility routing obeys masks/constants, preserves all clean approaches/hull and deduplicates same-tier cables. Cancel/fail during tile gas mutation, node linking, map initialization and observation: no surviving owned cable/provider links or changes to unrelated live gas/power state. Work counters include rejected attempts. |
| A62 | Through UI alone create/save/reopen a room theme and tile/wall/light/entity packs, override/reset inheritance, bind gas/electrical profiles and export/import portable assets. Roundtrip preserves normalized semantics; missing dependencies and unsupported schema remain actionable errors. |
| A63 | Through UI create table/device/chair relations and complete large/medium/L-trio/four-room alternatives. Preview all rotations, corner preference and seat approach; support cycles and missing targets are identified. No successful preview combines members from competing variants. |
| A64 | Painting, erase, undo/redo and channel/profile edits produce the exact expected blobs/holes, including diagonal contact and a one-cell bridge. Graph/matrix and shorthand connection edits roundtrip; ambiguous ports and gas-domain conflicts highlight actual cells. |
| A65 | Stale edits/previews, unauthorized or duplicate apply, malicious references/oversized imports, disconnect/cancel and concurrent writers cannot mutate shared prototypes or publish invalid output. Draft recovery works; one accepted operation publishes once. Keyboard, localization and non-color labels remain usable. |
| A66 | End-to-end in-game creation of an automatically fitted hybrid area with a constant landmark, relational furnishings, two valid gas domains, generated wiring and operating lights. Compare seeds, inspect topology/pressure witnesses, apply a verified snapshot, and inspect the actual output. Preview with any required unverified gate cannot apply. |

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

### Current capability index and active 80% milestone (2026-10-27; weights fixed 2026-10-17)

This is the current source/test-inventory assessment, superseding the historical 75% estimate
for milestone tracking. Weights are fixed before further work toward 80%; changing them later
requires an explicit scope amendment, not a convenient way to reach the target. Partial helpers
earn credit only within their tested domain. Shared end-to-end exits are not counted twice.
The baseline is approximately **54/100**, with 45-65% uncertainty; 80 is not reached.
Current credit is **69/100**: canonical request/version/utility pinning and scoped utility/consumer
intent add two contract points (contracts 14/16). Owned support/container candidate integration and
independent post-lighting layered access each add one interior point (interiors 15/18). These are
planning exits, not runtime verification; no layout, engine, utility or editor points are counted
again for those same exits. Shared component-backed ownership/retirement adds one engine lifecycle
point. Shared allocation capture, tile callback cleanup/cancellation and complete map-scoped scene
inspection add one further engine point (engine 11/20), without operational/isolation/publication
credit. Standalone rooted layout through the complete owned pipeline adds one layout point
(layout 16/22), including real supported/contained room proposals without fabricated entrances or
host corridors. This does not recount the baseline's 1x3 geometry or already credited support math.
Complete standalone request-to-stage materialization now adds one engine point (engine 12/20):
the primary candidate reaches one frozen, budgeted, initialized scene containing real supported
and hidden container members, terrain, structures and lights. This does not recount the existing
placement math, shared capture or preview ownership, or award operational/isolation/publication credit.
Owned InitialOnly gas initialization adds two utility points (utilities 4/12): actual grid-tile
volumes and resting cardinal boundaries are reconciled, independent air/nitrogen/vacuum mixtures
are written through native hooks, hard constraints and current state are checked, and callbacks,
cancellation and repeated initialization cannot publish a failed receipt or silently refill gas.
These are operational initial-state exits on closed private grids. They do not award hosted gas,
stable-at-rest simulation, electrical supply, global isolation or publication credit. The flag writer
and image-version repair are prerequisites, not additional contract/engine points.
Native standalone WiredOnly realization adds one further utility point (utilities 5/12): actual
paused cable/converter node groups, same-tile terminal connections, solid-wall support and owned
receiver/provider backlinks now pass complete configuration gates. Exact shared work, cancellation,
post-gas callback drift and read-only repeats preserve atomic evidence. This does not award source
capacity, operating supply, hosted interfaces, independent circuits, global isolation or publication.
Image schema 8, silent slot loading, corrected zero density and narrow-room classification are
prerequisite repairs, not additional duplicated contract/layout/engine points.
Complete standalone schema-two service adds two distinct utility points (utilities 7/12): one for
actual dedicated-closet substation/furnishing and separate wall APC generation through native
initialization; one for required MV service targets and independently validated floor-service LV
routing with strict block rejection/certificates. Earlier geometry/authoring primitives earned no
extra points. Normal Release/fresh Debug 870/786/31 and the eight failing negative-control
assertions support these exits. Only four fixed mandatory terminals currently certify a block;
heuristic failure is never necessity. Hull reuse, complete joint retries, sustained source/load
operation, hosted networks and independent circuits remain open. No other category gains points.
Whole-core fallback and ordinary-pack wall/density unification maintain existing furnishing credit.
Bounded joint preferences and coherent proximity/spacing now have verified placement coverage;
bounded preference diagnostics and independent policy auditing now have verified coverage. Full
supported-member review now has bounded JSON/SVG coverage; in-game author workflows remain open. Static sprite previews provide
review artifacts; in-game authoring/editor exits remain unimplemented.
Bounded complete utility-reservation retries add one layout point (layout 17/22). A later hard
converter constraint now changes the actual reservation, then reaches native initialization without
reusing failed geometry or advancing independent networks in lockstep. All costs and exhausted
frontiers remain explicit. Automatic comfortable sizing supports this exit; it is not another point.
Original-policy and physical-claim audits repair prior validation credit. Generated writable hull
sharing now reaches initialized native narrow closets and keeps the same layout credit. Inspected
existing-hull reuse and general hybrid fitting remain open. Normal Release/fresh Debug 891/825/31
retain all 881/817/31 cases. Other domains retain their preceding credit.
The two remaining contract points require immutable referenced content with manifests and complete
project asset contracts. Candidate binding and actual utility realization remain in their own
layout/runtime domains and gain no credit from declaration transport. The table shows baseline and current points separately.

Hole-aware size-band fitting adds one distinct layout point (layout 18/22): complete primary
requests now meet hard counts/areas around one or multiple holes, including one large + one medium
+ three small rooms, cardinal doors and shared quotas across separate islands. Native initialization
preserves void tiles and scene ownership; fitting floods share the graph allowance and exhaustion
publishes no partial layout. This closes the bounded courtyard-fitting portion of A01/A04/A12, not
all preservation/prefab/actor requirements in those fixtures. No additional engine, interior or
utility point is counted for the same generated rooms. General non-strip/joint fitting remains open.

| Capability / acceptance focus | Weight | Baseline points | Current points | Current support and missing exit |
| --- | ---: | ---: | ---: | --- |
| Contracts, prototypes and authoring; A21/A31/A41/A43-A52 | 16 | 12 | 14 | Validated relational cores, paint/entrance contracts, gas/power profiles, revisioned utility drafts, canonical requests and scoped utility/consumer intent exist. R08 still needs complete content manifests and project assets. |
| Layout, packing, routing and partitioning; A01-A12/A37-A42/A45-A50 | 22 | 15 | 18 | Cardinal geometry, atomic library alternatives, mixed size targets, owned configuration/subdivision retries and bounded strip strategies. The complete owned path now supports standalone rooted networks without entrance/host fabrication, including layered room contents. Complete utility-geometry retries now resolve later hard placement failures through native initialization with independent network choices. Writable generated-hull sharing supports compact native utility rooms. Hole-aware strip fitting now meets hard size/count goals through native generation, including several small rooms and request-wide quotas across courtyard islands. It still rejects hybrid/interior-fill; general joint fitting and work metering remain R09/R14. |
| Interiors, materials and tactics; A25/A27-A36/A43-A44/A52 | 18 | 13 | 15 | Coherent packs, facing, clean approaches, supported-core search and tactical geometry exist. Owned planning now integrates supported whole-core poses and independently audits access after lighting. Unified utilities, operational room staging/lighting and remaining R09/R13 exits stay open. |
| Owned engine lifecycle, validation and publication; A13-A24/A34-A36/A61 | 20 | 9 | 12 | Disposable previews, callback checks, insertion and conservative clearance exist. Component-backed ownership/retirement and allocation capture now include the complete owned scene. The primary standalone entry freezes exact write intents before allocation and verifies terrain, structures, room poses and complete map membership after shared paused initialization. Hosted/hybrid capture, author sessions, isolation, actual operation and publication remain R10/R17. |
| Atmosphere and electrical operation; A53-A61 | 12 | 2 | 7 | Current credit is 7/12: closed private-grid InitialOnly gas state/volume/edge checks and transactional filling, plus standalone WiredOnly native cable/converter groups, wall supports, actual receiver/provider membership and scoped registered solver lists/backlinks. The complete standalone schema-two path adds dedicated furnished closets/separate wall APCs and required MV/LV floor service with strict block auditing. Hosted/stable gas, live electrical sources, independent circuits, supply under load and working lighting remain R11-R13. |
| Replay, editor and release workflow; A22-A24/A51/A62-A66 | 12 | 3 | 3 | Bounded canonical JSON, native map rendering, paint inspection, stable proposal identities and headless utility draft replacement exist. The bespoke SVG/gallery pipeline is retired. Native theme-pack/goal/profile selection and immutable server saves now exist as a partial authoring slice. Full content/context replay, project service, pack-content editors, undo/redo and apply remain R15/R16/R18. |
| **Total** | **100** | **54** | **69** | Current verified assessment for the expanded-scope 80% goal; complete bounded courtyard size fitting adds one layout point, with inspected-hull/hybrid/operation/editor work still open. |

Implementation sequence: finish R08 schema/snapshot prerequisites, integrate through R09, then
R10 lifecycle and operational adapters R11-R13/R17; R15 replay contracts and R16 headless/editor
work follow their documented prerequisites. Keep unsupported gates visible. Update points only
with an acceptance-backed implemented capability, recording its limitations and test evidence.

### Implementation ledger

Update this ledger in the same change as each implementation step. Keep task checkboxes below
unchecked until their full exit criteria pass. Each row records concrete work and validation, not
an estimate. A partial implementation is labeled `In progress`; an untouched task is `Pending`.

**Revision gate (updated 2026-10-17):** R01-R12/R15/R17-R18 are in progress;
R13-R14/R16's expanded delivery exits remain pending. The dated batches below distinguish
implemented subsets from each parent task's incomplete exit.
Historical T-row results establish only their tested subsets. Later entries supersede earlier
counts/statuses; the 2026-10-14 checkpoint records 513 focused unit and 164 selected integration
passes, including the client sandbox test. Those gates were not rerun for this documentation edit.
No T task may be completed until its mapped rewrite exits pass. Existing tests do not establish
the new gas, electrical or UI requirements. Keep default-behavior regressions and add A53-A66;
schema loading, geometric proposals and static exports do not establish runtime operation.

| Rewrite | State | Existing tasks requiring revision |
| --- | --- | --- |
| R01 | In progress | `KsProcgenAssemblyPrototypes` and `KsProcgenAssemblyCompiler` implement reusable named members, required/optional multiplicities, entity bindings, complete ordered variants, typed spatial/context/support relations, and normalized core records. Expansion is bounded to 64 members/256 relations, targets must resolve to one instance, support/container cycles and multiple support parents are rejected separately from valid spatial cycles. Theme validation compiles both assemblies and legacy singleton entries; entries and assembly references carry core minima. Loaded fixtures resolve a table/device/optional-chair core and a standing variant. The live office pack now references an explicit desk/seat/console assembly. `KsProcgenPrototypeCapabilityInspector` copies inherited surface, fixture, rotation and container declarations into bounded snapshots and support inspection witnesses; declaration hashes are separate from full content/geometry hashes. Remaining: full schema/replay exit coverage and R02/R06 adapters proving actual placement and insertion; snapshots never certify engine placement. |
| R02 | In progress | T09a/T15/T15a: normalized floor cores replace whole-pack atomic lists. Mandatory core counts are enforced across all selected packs before optional content, then required pack minima select complete cores. Bounded mandatory continuations revisit earlier poses, whole variants and weighted core choices when later requirements cannot fit, rolling back claims, protected paths, witnesses and copy/anchor metadata. Unrelated optional singletons can be omitted. Office adjacency, chair facing and console seat association are explicit. Required/preferred corner context and clean-path `Near` rules produce persisted geometric witnesses. Authored landings rotate with members and remain root-accessible; accepted Near paths are protected from later furnishing. Floor search revisits member positions, permitted orientations, selected interaction approaches and optional-member omissions. Path searches share a 4096-node room budget. Optional initial cores and repeated density draws use seeded weighted ordering; density placement retries other whole cores when a heavier candidate cannot fit. Semantic hash v20 distinguishes mandatory continuation search. Remaining: joint optional/lighting search and preference optimization, support/container layers, lighting layer checks and engine validation. |
| R03 | In progress | T01/T02/T03/T04: geometry/theme/entrance profile references, paint and generic/numbered entrance components/prototypes, explicit void-grid host binding, bounded read-only grid snapshot, exact cardinal components, void subtraction, directed wide entrance ownership, scoped labels, conflict checks and stable identity. Geometry-only conversion rejects entrance-bearing blobs. Real doorway/context inspection, constants/envelopes, complete profiles and publication remain pending. |
| R04 | In progress | Complete configuration contracts, shorthand, request transport, DeclaredNetworks roots and replay identity. A bounded seeded selector checks complete supplied scenes without mixing alternatives. Complete owned interior search retries ordered subdivision preferences within declared configurations after downstream rejection and retains tagged atomic attempt journals. Configuration and layout caps are separate; all reported pipeline work survives failed layouts. Painted-area profiles transport immutable subdivision preferences, room-size goals, fallback permissions and window goals into explicit owned planning. An opt-in rectangular size-goal strategy precedes scalar attempts, with requested/used strategy diagnostics and protected-floor refusal. Bounded global quota allocation across separate rectangular networks is implemented. Bounded complete quota retries after protected seams are implemented. Bounded fitting of L-shaped, stepped and courtyard networks is implemented, including shared fitting work and strict mixed-size native output. Joint route/room/content choice, general non-strip fitting and quota reassignment after content failures remain pending. |
| R05 | In progress | Component analysis checks local groups, forbidden joins, host bypasses, roots, sealed thresholds and complete floor assignment with shared work limits. Procedural adapters construct routes, grow owned floor, and partition each network independently. Opt-in rectangular strips fit simultaneous final floor-size bands including wall loss, then use the same protected-route seam solver. Themes, furnishings and bounded lighting retain entrance reservations. A final interior audit checks room-local clean access, interaction/door approaches and the complete post-placement entrance scene; vault-required furniture and floor fixtures are blockers. Local fallback cannot remove inter-network walls. Independently revalidated local tactical queries measure chokes, alternate routes and redundancy with shared budgets; host detours cannot join groups. Owned materials preserve primary entrance tiles and read-only floor, check doors, cover separators and reject shared wall conflicts. Owned hull/window adapters reserve entrance/host spans, retain unknown shell, validate explicit directional exterior facts, count fixed windows without writes and substitute compatible theme prototypes under shared budgets. A complete owned proposal composes these stages atomically; legacy generation entry points reject entrance-aware inputs. Alternate trees, arbitrary-shape/joint room fitting, prefab/hybrid/interior-fill composition, live windows, full-host tactical ownership and engine validation remain pending. |
| R06 | In progress | T10/T12/T13/T15: `KsProcgenContainerPreflightSystem` delegates live eligibility to engine APIs. `KsProcgenContainerStageSystem` owns disposable paused container-only previews, inserts members in dependency order, checks actual membership/parent transforms and rolls back failures. Explicit initialization verifies selected lifecycle, membership and managed-slot filter compatibility afterward and is idempotent. Insertion-veto fixtures cover preflight, commit and actual container insertion without replaying attempts during verification. Surface/spatial relations remain unsupported there; temporary root coordinates are not validated layout poses. A joint room-core adapter now owns optional grid floor/wall terrain with detached masks, cumulative allocation/initialization limits and exact frame/terrain verification. Fresh initialized scene inspection rejects foreign entities, unsupported shapes and member/wall hard overlaps; continuous fixed-actor cardinal sweeps enforce conservative floor coverage and declared vault/blocking exclusions without moving the actor. The wall lattice is a permitted structural union. The primary layered planner now materializes standalone owned scenes through the shared lifecycle. The complete owned scene now reaches the shared fixture/cardinal observer. Remaining: hosted/hybrid materialization, support settling, actual actor/machine/chair/door operation, gas and working-light checks, side-effect isolation and publication. Verification flags remain false; the standalone tile stage still rejects furniture. |
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
completion credit. The historical 75-point assessment uses the following weighting so later changes
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
### 2026-10-15 review findings and required rewrite queue

This review covered the specification, implementation/test inventory and the main planning,
relational, staging, scene-validation, gas, lighting and export paths, plus the engine utility
code linked in sections 19-20. It is an architectural/source review, not a claim that every line
or every historical test was re-audited. No new runtime test evidence was produced by this edit.
The following are concrete changes to implement; existing conservative refusal/verification
guards MUST remain until their replacements pass the listed exits. Do not "fix" incomplete work
by commenting out those guards. R08-R18 extend and refine R01-R07 rather than erase their exits.

- [ ] **R08 - Version utility and editor contracts before adding implementations.** Priority P0;
  depends on the existing R01/R03/R04 schema groundwork. Revise `KsProcgenRequest`,
  `KsProcgenAreaMarkers`, `KsProcgenThemePrototypes`, theme validation/resolution and area snapshot
  transport. Gas/electrical profile declarations, transport and versioned utility asset drafts now
  exist (see the dated batches below), along with canonical request snapshots and captured theme
  candidate pools. Detached themes, material/light/entity packs and relational assemblies can now
  feed catalog capture without global registration. Scoped utility/consumer contracts are implemented
  as detached intent in request schema 2. Planning projects now bind canonical requests, captured
  theme bodies and portable geometry libraries. Direct resolved entity/tile bodies now have a bounded
  detached manifest with stage drift checks. A separate project-bound static map manifest captures
  server-resolved source bytes and map/theme references. Registered typed prototype references
  across kinds now have transitive body capture, including external override seeds and abstract
  reference-discovery templates, followed by schema-10 parent closure and explicit declared-schema
  gap witnesses. Explicit owned entities instantiate from captured defaults; complete
  declaration/resource/component-reference closure and source-map copying remain unfinished.
  Candidate binding, complete map/editor project contracts, full static entity/library
  content manifests and typed live verification evidence remain
  missing; the light-supply enum still has
  only `SelfContained`/`CallerSupplied`.
  - [ ] Define sections 19-21's serializable profiles, layer/domain ownership, stable references,
    editor draft schema, scope precedence and typed verification evidence. Retain explicit generic
    defaults; migrate old requests without silently enabling gas or power writes.
  - [ ] Deep-freeze resolved content including inherited mutable prototype lists; validate gas
    fractions, numeric bounds, references, source tiers and revisions before any allocation.
  - [ ] Version normalized request/content hashes and portable exports; reject unsupported newer
    versions instead of silently ignoring fields. Exit: canonical roundtrips and invalid-input
    cases for A53/A57/A62, with legacy fixtures retained.

- [ ] **R09 - Integrate layered room cores and utilities into one candidate transaction.** Priority
  P0; depends on R08 and R02/R05. The owned planner now reuses `KsProcgenRoomCoreSearch` and
  `KsProcgenSupportedAssemblySearch` inside its existing retry path when component inspection context
  is supplied, including captured ordinary packs. Geometry-only callers without component inspection
  retain the legacy projection helper; wall mounts reaching the flat stage compiler are rejected.
  Standalone primary candidates now materialize
  through one frozen owned scene. Proposed gas domains and scoped bindings now participate in the
  same retry transaction from retained initial prototype facts. Owned gas initialization and native
  HV/MV/LV cable/device realization now have retained receipts and regression evidence;
  bounded joint soft-preference/proximity search now participates in furnishing. Electrical supply,
  hosted/hybrid materialization, revised utility closets/routing and in-game author workflows remain open.
  Another disconnected utility planner would leave the primary output incomplete.
  - [ ] Introduce one immutable plan containing ground, support/container, approach and cable-layer
    reservations, gas domains and dependency witnesses. Adapt existing pure algorithms to this plan.
    Supply content through a validated resolver supporting both loaded prototypes and session draft
    assets; do not require global prototype hot reload for every UI edit.
  - [ ] Route required utilities/device reservations through room/layout selection and whole-core
    rollback. Integrate complete variant search; avoid retrying utilities forever after layout freeze.
  - [ ] Keep floor-only helpers as supported fast paths, not a second conflicting authoring model.
    Exit: A43-A44/A52/A61 traverse the primary entry point with no member/cable/gas leakage across
    rejected alternatives. Unsupported combinations still return explicit failure.

- [ ] **R10 - Consolidate stage ownership and activation lifecycle.** Priority P0; depends on R08/R09
  and R06. Tile, surface, container, assembly and complete owned previews share component-backed
  ownership, retirement, allocation capture and allocation-list cleanup. Surface/container/assembly/
  owned initialization shares one typed lifecycle transaction. The primary owned entry now consumes
  frozen scenes through shared assembly placement and map-tree membership inspection. Remaining
  specialized surface/container materialization and full author sessions need integration. Preview
  allocation still has an absolute ceiling
  at 256, smaller than full-room utilities can require.
  - [ ] Create one owned generation-session lifecycle with typed phases and shared allocation/work
    counters. Put persistent per-map/session entity state on an owned component per CONTRIBUTING.md;
    use a bounded session registry only for non-entity author operations. Preserve existing callback
    allocation capture, off-map child cleanup, reentrancy guards and lifecycle checks during migration.
  - [ ] Define activation/isolation for node groups, atmosphere, power, containers and network
    visibility. Prove scoped processing or reject unsupported operational previews; paused-map
    creation and deletion alone are not rollback of global side effects.
    Native prerequisite for R12: `NodeGroupSystem.ForceUpdate` drains all pending group/reflood/
    removal queues. It cannot certify a single owned grid without also processing unrelated work.
    `KsRebuildOwnedPowerNodes` scopes known power nodes through the native rebuild algorithm;
    the native-node ledger records the verified queue/frame/inspection scope and WiredOnly stage
    integration. The scoped power-reconnect ledger below adds solver membership/backlink checks.
    Broader source, solver, battery-sync and initializer isolation remain activation prerequisites.
  - [x] Native power-reconnect prerequisite: `PowerNetSystem.Update` still drains both global
    reconnect queues before the solver. Add a bounded scoped adapter around the existing
    `DoReconnectApcNet`/`DoReconnectPowerNet` implementation, after inspected native topology.
    Preflight registered network/load/supply/battery identities, complete owned group membership,
    provider/receiver backlinks, component ownership, paused state and all writable target lists.
    Reserve reconnect work before mutation, remove only selected completed entries from the
    queues, and preserve unrelated pending networks. Repeated checks must inspect rather than
    reconnect. Prove two-grid queue isolation, foreign references, exact budgets/cancellation and
    unchanged battery/load/solver state. This is a prerequisite to supply validation, not permission
    to tick the global solver or claim live electrical operation.
  - [ ] Make stage destruction, disconnect, invalidation and cancellation release retained state
    exactly once. Derive limits from declared request budgets rather than simply removing caps.
    Exit: A61/A65 plus existing initializer, insertion-veto, nested-callback and off-map cleanup tests.

- [ ] **R11 - Replace gas declarations with owned atmosphere initialization and evidence.** Priority
  P1; pure profile/domain work follows R08/R09; engine work follows R10. `KsProcgenGasClosure`
  accepts supplied edge facts and always leaves materialized verification false; hull/window
  proposals do not inspect or fill actual gas. Preserve that useful preliminary checker. The primary
  planner now binds proposed domains from retained initial airtight facts, without promoting these
  declarations into engine evidence.
  - [x] Add nominal gas-domain conflict resolution to the primary candidate transaction, retaining
    required/preferred demands and omissions; reuse the existing mixture-math foundation.
  - [x] For closed private owned grids with InitialOnly intent, inspect actual resting-state gas
    edges after engine initialization, reconcile them with the retained candidate and fill through
    a scoped adapter with native revalidation, actual volume, independent mixtures and notifications.
    Never treat a non-null map/space fallback as owned gas. Verify current state without refill and
    commit initial evidence only after the shared transaction's final checks.
  - [x] Complete live preferred-gas omission handling on the supported owned path, retaining hard
    state/observation constraints and historical receipts without periodic refill.
  - [ ] Extend actual gas context to hosted/preserved areas. Keep those guards until inspected
    compatibility and scoped mutation replace them.
  - [ ] Separate closure, initial fill and stable-at-rest certificates; never replace them with
    a call to `fixgridatmos`, global map atmosphere or periodic refill.
  - [ ] Exit: A53-A56/A61 on real grid atmospheres, including leak mutation and preserved context.

- [ ] **R12 - Implement actual electrical distribution and provider assignment checks.** Priority
  P1; pure topology/routing follows R08/R09; runtime work follows R10. Standalone shared-Main
  AutoDistribute/WiredOnly now has a native cable/converter pipeline; broader sources, hosted
  networks and operating supply remain open. Lighting labels do not establish working supply.
  - [x] Inspect the supported standalone palette and declared loads, place required infrastructure
    jointly with furnishings, route cardinal voltage tiers and cover consumers with the actual
    circular LV rule. Native paused groups, same-tile converter wires, wall supports and actual
    receiver/provider backlinks are verified in this bounded scope; repeats only inspect.
  - [ ] Finish live source/capacity/load-scenario inspection, hosted interfaces, wider infrastructure
    strategies and independent circuits. Preserve the existing operational-supply guard.
  - [ ] Validate actual provider/network membership and supply under the chosen load scenario;
    account for stateful provider selection and nearest-ineligible-provider behavior. If engine
    behavior needs an upstream fix, isolate that change with its own tests/provenance; never patch
    private receiver links or override `Powered` in procgen to conceal it.
  - [ ] Exit: A57-A61, including independently missing input/output cables, default range 3,
    overloaded/disabled devices, accidental circuit merges and battery-only false positives.

- [ ] **R13 - Replace supply labels and walkway-distance lighting with operating-light validation.**
  Priority P1; depends on R08/R12 and the owned stage from R10. `KsProcgenLightingPlanner` spreads
  estimated coverage through the floor graph using fixture spacing; that is neither electrical
  range nor engine light transport. Its false `WorkingCoverageVerified` guard is correct.
  - [ ] Keep the estimator explicitly named as a proposal heuristic; add mounting/capability and
    demand bindings to the shared consumer plan. Preserve self-contained and caller-supplied cases.
  - [ ] Measure the specified engine light metric with actual fixture state/occlusion, or reject an
    unsupported hard metric. Recheck after furnishings, utility device placement and power settle.
  - [ ] Exit: A29-A30/A60/A66 distinguish nearby-but-unpowered, obstructed and actually lit cells.

- [ ] **R14 - Generalize bounded layout search and centralize work accounting.** Priority P1;
  depends on R09 and R05's current retry contracts. `KsProcgenSizeGoalSubdivision` and
  `KsProcgenIrregularSizeGoalSubdivision` are useful strip fast paths, but contiguous-column
  assumptions and local 4096-probe/8-vector bounds exclude general holes/branching shapes and do
  not expose complete request work. `GraphWorkFullyMetered` correctly remains false.
  - [ ] Retain deterministic fast paths behind one strategy interface; add bounded general-mask
    partitions and joint prefab/procedural fitting with real seams and exterior scores.
  - [ ] Use a request-owned budget ledger for preprocessing, candidate construction, rejected
    vectors, relations, utilities, compilation, serialization and engine inspection. Expose
    strategy truncation separately from infeasibility; do not merely increase magic limits.
  - [ ] Exit: A37-A42/A49/A61, including feasible holes/branches, deliberately insufficient budgets
    and stable same-seed output independent of yield boundaries.

- [ ] **R15 - Separate replayable authoring input, diagnostic views and verification receipts.**
  Priority P0 for schema boundaries, P1 for delivery; depends on R08/R09. Current
  `KsProcgenOwnedDiagnosticReport` schema 3 is a bounded review view; `VerifyExact` compares fresh
  diagnostic bytes, not a complete content/context replay. Hardcoded false flags correctly limit
  its claims. The static room-map manifest now binds canonical project declarations, immutable
  content-resource bytes and direct prototype bodies; actual source instantiation, full dependency
  closure, portable imports and live context capture still need implementation. Diagnostic schema 3
  shares geometry-collection and entry limits across the report, bounds source processing and
  marks partial paths. These projection limits do not meter the complete generator or certify replay.
  - [ ] Add versioned canonical authoring/request artifacts with content manifests, separate bounded
    diagnostic projections, and typed scoped verification receipts keyed to snapshot identity.
  - [ ] Include gas/electric/UI revisions and provenance; meter total export input work and bytes.
    Page geometry without presenting a truncated view as a complete plan.
  - [ ] Implement replay through the actual generator with matching context/content or an explicit
    mismatch. Exit: A51/A62/A65 and tampered/stale/partial receipt rejection; keep schema-1 samples
    recognizable as historical geometric diagnostics.

- [ ] **R16 - Deliver the in-game workspace, not just commands and SVGs.** Priority P1; drafts can
  start after R08/R15; verified apply requires R10-R13/R17. The existing `ksprocgen_snapshot` command
  reads painted markers; JSON/SVG exports do not edit packs, create assemblies or generate a map.
  - [ ] Implement document service, revisions, permission checks, persistence/import/export and
    undo/redo first; share normalized contracts with the headless entry point.
  - [ ] Add area/entrance tools, theme/room-library/pack editors, assembly and connection graphs,
    gas/electrical panels, inspectors and diagnostic overlays in section 21.
  - [ ] Add cancellable preview jobs, comparison, recovery, stale-result handling and deliberate
    apply. Keep apply unavailable with an actionable reason while required runtime gates are absent.
  - [ ] Exit: A62-A66 and an actual client UI walkthrough; serialized-message unit tests alone do
    not prove that authors can complete the workflow.

- [ ] **R17 - Scope scene inspection and complete operational validation.** Priority P0 before
  publication; depends on R09/R10, then R11-R13 for utility-dependent operation.
  `KsProcgenRoomFixtureSystem.TryScene` now inspects the engine-maintained owned-map transform
  tree; unrelated world population no longer consumes its scan cap. Fixed convex sweeps are valuable
  clearance checks, but do not prove door use, seating, interaction vetoes or support stability.
  - [x] Replace the global scan with a maintained owned-map membership view plus independent
    detection of foreign insertions/reparenting. Include paused/noncollidable/container entities
    where needed; do not optimize by inspecting only selected members and missing callback spawns.
  - [ ] Add actor/door/seat/device adapters and bounded supported-surface settling under the isolated
    lifecycle. Recompute actual entrance components, machine fronts and clean approaches after
    utilities initialize; keep conservative geometry and unsupported-shape rejection as prechecks.
  - [ ] Exit: identical scene validation with many unrelated entities, foreign-entity drift
    rejection, A34-A36/A43-A44/A52/A66 operational checks, and no global physics stepping.

- [ ] **R18 - Reconcile task dependencies, current status and release accounting.** Priority P0
  planning cleanup; complete at release with R07/T19. Historical T rows mix prerequisites with
  end-to-end exits: T13 needs content whose T15 task depends on T13. Large chronological progress
  notes also leave stale "current" estimates/statuses that can be mistaken for runtime support.
  - [x] Publish one current capability index linking evidence and open exits; retain dated history
    below it, clearly historical. Track exact entry-point support for pure/hybrid/prefab modes,
    geometry interpretations, relational layers, utilities and editor operations.
  - [ ] Schedule schema -> pure planning -> stage/lifecycle -> engine adapters -> integrated
    validation/publication. UI drafts can proceed after schemas; apply waits for verification.
    Distinguish those construction prerequisites from joint acceptance dependencies in every T row.
  - [ ] Recalculate an evidence-based estimate for the expanded scope only after updating weights;
    do not carry the earlier 75% forward or use test counts as capability percentages. Exit: a
    dependency-consistent release checklist and A01-A66 evidence, with unsupported modes visible.
  - [ ] Audit older procedural `GameTest` fixtures, starting with `KsProcgenContentLoadTests`,
    `KsProcgenOwnedInteriorTests` and `KsProcgenRoomSceneTests`, for assertion failures masked by
    the pool's dirty-disposal warning. Adopt failure-preserving teardown where needed and prove
    an actual failed result with deliberate source mutation. The new atmosphere, electrical and
    utility-draft fixtures use `KsProcgenGameTest`; requiring zero skipped cases remains a gate.

### Preview compartmentalization (2026-10-27, R10/R15/R16)

The preview pipeline must keep generation, authoritative staging and presentation independently
maintainable. The offline showcase renderer must not become a second generator or an apply path.
New editor work must observe these same boundaries rather than growing another combined preview
controller.

- [x] Replace the two combined offline renderers with small entry points and a private
  `Klovn/Tools/_KS14/Procgen/preview` package. Python file orchestration, sprite verification,
  card markup, viewport coordinates, diagnostic layers, page markup, CSS and interaction script
  now have separate homes. Pure SVG layers consume exported values and never load engine assets.
- [x] Isolate sprite rendering in `preview/sprites/ShowcaseSprites.psm1`, exporting only
  `Invoke-ShowcaseSpritePreview`. Private asset, frame, mapping, scene, wiring, encoding and batch
  modules receive an explicit invocation context. No dot-sourced drawing helper or shared mutable
  image cache is installed into the caller's scope. Each invocation releases images in `finally`;
  local frames/graphics/attributes have their own disposal scopes, including error paths.
- [x] Keep cable drawing as an explicit final scene pass. Replace repeated whole-cable scans with
  one cardinal occupancy lookup per voltage tier; preserve native masks and all output pixels.
- [x] Inspect the existing server split: planning, assembly placement, allocation transaction,
  component-backed stage ownership/retirement, paused initialization, atmosphere/electrical
  adapters and bounded diagnostic presentation remain separate systems. Offline renderers do
  not hold live stage handles or entity references. Preserve this separation in future work.
- [x] Add seven presentation behavior tests using the real exports: unchanged gallery/SVGs and
  PNGs, final nonempty wire overlay, pure composition, escaping, exact plan/byte sprite matching,
  missing-sprite fallback, invalid export rejection, private module exports, repeated invocation,
  intervening render failure and exclusive source-image access after cleanup.
- [ ] Complete R16's server job coordinator and client workspace with explicit revision/cancel
  state; presentation receives detached bounded reports only. Permission checks, authoritative
  receipts, ownership and apply remain server responsibilities. Offline images do not satisfy
  this exit or certify native runtime appearance, power supply or publication.

Module ownership and commands are documented in
[`Klovn/Tools/_KS14/Procgen/README.md`](../Tools/_KS14/Procgen/README.md).
The first post-refactor comparison retains all 15 recorded preview-artifact byte hashes, including
six PNGs, five SVGs, two overviews, the gallery and sprite manifest. Both existing rendering entry
points retain their arguments. Rendering failures remain explicit; no placeholder artwork is
introduced. Capability credit remains **68/100**: this is a maintainability rewrite, not a newly
completed generator/editor workflow.

Verification: seven Python presentation tests pass, including the Windows module-session test.
Three isolated controls (wrong final wire layer, bypassed sprite hash and omitted cache disposal)
each produce the intended assertion failure; each source restores to its exact original bytes.
The normal Release integration-project build passes with localization hooks enabled. That build
also exposed and fixed an existing missing server compile reference to ImageSharp in the unfinished
resource-capture prerequisite; no runtime resource-closure credit is awarded for that dependency
repair. Native checks also found two malformed `/Audio//Weapons/pierce.ogg` declarations on the
cardboard table; corrected them to the existing canonical asset rather than weakening the resource
path guard. Staging now preserves the underlying resource error message for diagnosis, and invalid
path summaries are bounded. Final native showcase verification passes **5/5**, with zero failures
or skips (`ks_preview_final_native.trx`), following the normal Release build. All 33 recorded
showcase artifact hashes still match. No showcase exports are regenerated against the unfinished
schema-11 prerequisite in this batch. The older
schema-10 Release/Debug gates remain historical evidence and are not relabeled as schema-11 tests.

### Complete source-layout preparation before map allocation (2026-10-28, R08/R09/R10, in progress)

- [x] Extract complete eligibility, geometric selection, binding reconciliation, terrain and entity
  preparation into `KsProcgenLayoutInstantiationSystem.TryPrepare`, without requiring an owned
  map/grid/allocation transaction. Preserve cumulative preparation allowances and source identity.
- [x] Return a system-bound single-use layout plan. Expose immutable provenance, counts, eligibility
  and explicit ungenerated residuals; keep mutable packing objects and native load inputs private.
  Consume a materialization attempt before definition callbacks, recheck the frame and remaining
  allocations before terrain writes, and retain final whole-layout verification.
- [x] Keep the existing combined adapter as a wrapper around preparation/materialization. Preserve
  its original frame identity across preparation callbacks. A failed write attempt still requires
  retirement of the complete caller-owned transaction.
- [ ] Verify preparation success/failure with no map or member allocation, frozen caller settings
  and source bytes, captured revision/hash metadata, immutable residual review, frame/budget/cancel
  entry preconditions, definition drift, foreign-system plans and failed-attempt reuse. Retain all
  source capture, alternative selection, native generation, roundtrip and sandbox regressions.
- [ ] Integrate the plan with the primary owned lifecycle and post-MapInit source verification,
  then inspected physical ports/hull and composed residual generation/utilities. No primary library
  or entrance-aware guard is removed by this planning prerequisite. Current credit stays **69/100**.

### Native pose eligibility for automatic layout alternatives (2026-10-28, R08/R09/R14, verified pose subset)

- [x] Add detached, immutable eligibility for complete family/option/rotation identities to packing.
  Preserve whole-alternative reservation, seeded fit ordering, routing and residual semantics.
  Reject unknown alternatives/rotations instead of silently weakening constraints.
- [x] Derive eligible orientations from captured native member poses and the composed member/family
  rotations before geometric selection. Exclude a whole orientation when any member forbids its
  rotation or has an unsupported anchored pose. Record the rejected orientation, asset and reason.
  Bound all checked orientations and members across selected and unselected alternatives.
- [x] Keep complete source/terrain preparation and final native verification after selection.
  A declared generate-only alternative needs no fabricated room binding; its residual cells remain
  explicitly ungenerated. This adapter still does not produce complete hybrid maps.
- [x] Verify automatic fallback between L/three-member and four-member alternatives, composed
  rotations, exact/short shared eligibility budgets, all-ineligible prefab/hybrid outcomes,
  immutable constraints and unknown/invalid eligibility. Retain earlier preparation and native gates.
  Initial normal Release passed 918 units and 359/360 integrations; the sandbox gate rejected
  `Array.AsReadOnly` in shared code. Replaced it with the existing sandbox-compatible immutable-list
  helper. Initial evidence:
  `.ks_procgen_eligibility_release.log`, `ks_eligibility_units.trx`, `ks_eligibility_native.trx`.
  Final normal Release build passes **918 units / 360 selected integrations**, zero failures/skips.
  Native inventories retain all previous 350 case names and add ten source-eligibility cases; the
  unit run includes sixteen new eligibility cases. The late-room preparation regression now uses
  a separate source asset with an opaque registered override so it still tests full preparation
  beyond the earlier pose filter. Eleven implementation/test hashes are stable through build/tests;
  UTF-8/no-BOM, CRLF, trailing whitespace and `git diff --check` checks pass.
  Final evidence: `.ks_procgen_eligibility_final.log`,
  `Content.Tests/TestResults/ks_eligibility_final_units.trx`,
  `Content.IntegrationTests/TestResults/ks_eligibility_final_native.trx` and
  `.ks_procgen_eligibility_final_sources.json`. This is a focused Release gate, not Debug or the
  full repository suite. No preview/rendering implementation is added.
- [ ] Extend eligibility beyond authored poses to complete source/component compatibility and
  retry complete candidates after native initialization failures, retiring the complete failed
  transaction and sharing all search/preparation/allocation allowances across attempts.
- [ ] Extract complete selection/binding/terrain/entity preparation into a private preallocation
  layout plan factory. The current adapter receives an already-owned map/grid; the primary entry
  must finish planning before allocating its map rather than using a populated speculative map as
  its plan. Preserve exact source revision, definition identity, budgets and single-use load inputs.
- [ ] Bind the plan to the primary owned lifecycle, including paused map initialization, complete
  map-member inspection, cleanup/retention and post-MapInit source verification. Inspect physical
  source ports/hull and compose residual generation/routes/utilities before accepting hybrid output.
  Do not remove existing library/entrance guards until their actual exits pass. No additional
  expanded-scope credit is claimed for this prerequisite subset; current **69/100**.

### Prepare complete selected room graphs before writes (2026-10-28, R08/R09/R10, verified prerequisite)

- [x] Separate captured entity-image decoding, prototype reconstruction and rebased pose checks
  from native allocation/loading. Use internal single-use preparations bound to their originating
  system; recheck source definitions, owned frame and allocation allowances when consuming them.
- [x] Prepare every selected room's complete entity inputs alongside its terrain before writing
  any selected terrain or allocating members. Keep shared image/member allowances and final
  whole-layout verification. Apply the same ordering to standalone complete-room copying.
- [x] Verify later-room rotation rejection leaves earlier rooms and all terrain unwritten;
  preparation allocates nothing; plans cannot be consumed twice or on another frame after use;
  cancellation/definition drift reject before allocation; unsupported anchors leave terrain empty.
  Run the retained native generation, capture, report, roundtrip and sandbox regressions.
  Normal Release build and **350 selected integration cases pass**, with zero failures/skips.
  The TRX inventory retains all 343 cases from the native renderer replacement gate and adds
  seven preparation cases. Implementation/test source hashes remain unchanged during the run.
  Evidence: `.ks_procgen_preparation_release.log`,
  `Content.IntegrationTests/TestResults/ks_preparation_release.trx` and
  `.ks_procgen_preparation_sources.json`. The five changed implementation/test files pass
  UTF-8/no-BOM, CRLF and trailing-whitespace checks; `git diff --check` passes. This is a focused
  Release gate, not a full repository or Debug run. No renderer work is added.
- [ ] Follow with complete source-aware alternative retries, physical source/port inspection and
  composed residual generation. Preparation alone does not certify a hybrid candidate or remove
  the primary library/entrance-aware guards. Expanded-scope credit remains **69/100**.

### Automatic source-layout binding (2026-10-28, R08/R09/R10/R14, verified prefab-selection subset)

- [x] Run packing internally from the captured project area/library; do not accept an editable
  public packing result as write authority. Bind every selected family/option/member to its source
  asset, compose member and placement transforms, and require exact ownership-mask agreement.
- [x] Preflight complete selected alternatives with shared room, terrain, source-probe, entity and
  image allowances before writes. Reuse prepared terrain plans instead of rescanning each source
  during materialization. Freeze caller budget settings before callbacks run.
- [x] Copy every member of each selected alternative inside the existing owned transaction. Keep
  each room's entity/network references independent, then reverify all room graphs and terrain
  after all selected rooms have started. Reject cross-room late mutation and frame/map drift.
- [x] Verify automatic three-room L-versus-four-room selection at nonzero target coordinates,
  composed rotations, shared budget exhaustion before writes, late cross-room mutation and
  revision/entrance-contract guards. Normal Release build and **324 selected native/sandbox tests**
  pass with zero failures/skips, including 10 new layout cases. Evidence:
  `.ks_procgen_layout_verified.log`, `.ks_procgen_layout_final.log`, `ks_layout_verified.trx` and
  `ks_layout_final.trx`. Existing source, terrain, generation and ordinary-import cases remain green.
- [ ] Finish entrance-aware packing and residual generation against inspected source interiors,
  including actual ports, hull, gas/electrical domains and clean cardinal access. The current adapter
  explicitly returns ungenerated residual cells and retains the legacy entrance-aware guard.
- [ ] Add whole-alternative retries after native/physical rejection, sharing all search/staging
  allowances and retiring the failed transaction; never substitute individual members from another
  option. Connect the composed result to the primary owned lifecycle before crediting a hybrid exit.

`KsProcgenLayoutInstantiationSystem` materializes the automatically selected prefab portion only.
It is not a complete hybrid generator or a public editor/apply operation. Source constants and
physical certification remain open. Expanded-scope credit stays **69/100**.

### Replace bespoke previews with the existing map renderer (2026-10-28, user-directed)

- [x] Remove the separate Python/PowerShell sprite renderer, SVG/gallery pipeline and associated
  presentation-only assets/tests. Use `Content.MapRenderer` for native map images and its existing
  viewer output; do not retain a second procgen drawing pipeline. Keep bounded diagnostic JSON,
  generation, ownership, validation and native map export as feature code.
- [x] Extend the native map renderer with `--wires-last`: reveal actual installed cable sprites
  through native subfloor appearance and paint them in a final entity pass. Process queued native
  appearance changes explicitly in the headless renderer. Own sprite-cache/layer/tint images and
  streams, and fix grid-relative descendant placement and fractional sprite offsets.
- [x] Update map examples, regeneration commands and section 21.4 to the native rendering path.
  Remove old custom gallery, sprite manifest, PNG/SVG composites and SVG-only assertions. Retain
  canonical JSON/report tests and the native generation/map-roundtrip checks.
- [x] Finish the post-removal native renderer and retained-feature regression gate. The normal
  Release renderer build passes; all five native maps render with the final appearance,
  offset and relative-position changes. All **343 selected integration cases pass**, with no
  failed or skipped cases, and renderer/diagnostic source hashes remain stable through the gate.
  Evidence: `.ks_procgen_render_replacement_final.log` and
  `Content.IntegrationTests/TestResults/ks_render_replacement_final.trx`.
  Native PNG signatures and dimensions are checked; the wired-office image is visually reviewed.
  The thin regeneration coordinator passes PowerShell syntax validation; the full coordinator
  was not rerun. This is a focused Release gate, not the full repository suite or a Debug gate.

The bespoke preview replacement is complete. Native images live under
`Klovn/Docs/Examples/Procgen/Showcases/Native/`; `--wires-last` is an optional native renderer
mode, not a separate compositor. This maintenance change adds no procgen capability credit:
expanded-scope implementation remains **69/100**, with native hybrid residual generation,
physical certification and primary lifecycle integration still pending.

### Captured room terrain and entity composition (2026-10-28, R08/R09/R10/R14, verified bounded materialization)

- [x] Add an immutable source-bound terrain plan that selects exactly the extracted room mask,
  checks source/extraction identity, uses captured runtime tile IDs and keeps flags and variants.
  Rebase source atlas coordinates before applying the room's clockwise turn and target origin.
- [x] Compose native tile orientation using the actual engine convention: ordinary rotation codes
  decrease for clockwise room turns; mirrored codes increase because native mirroring is applied
  after rotation. Preserve the mirror bit. Reject unsupported orientation codes instead of guessing.
- [x] Write through the native map API only within a captured, owned, paused, uninitialized unit
  grid with splitting disabled. Check map identity, allocation allowances and cancellation at write
  boundaries. Verify all selected tiles after callbacks; do not clear cells outside the footprint.
- [x] Combine terrain and the verified entity graph adapter in `TryInstantiateRoom`. Return one
  internal immutable receipt only after entity startup and final terrain verification succeed.
  Failure can leave writes in the private map and requires caller-owned retirement of the entire
  captured transaction; no partial receipt authorizes retaining or publishing the failed scene.
- [x] Verify 32 native orientation combinations, L-shaped captured footprints plus entity graphs
  at all quarter turns, positive/negative atlas origins, preserved surrounding cells, source identity
  mismatch, budgets, cancellation and entity-startup tile drift. Run retained native generation,
  ordinary map-import/simulation and actual sandbox cases. Strengthen the four combined-footprint
  cases with anchored tables, proving terrain exists before native anchoring/startup.
- [ ] Integrate complete room instances into the primary library/hybrid pipeline. Select whole
  layout alternatives atomically, accumulate terrain/entity/probe/image allowances across all
  placements, fill residual areas, and verify native ports and composed cardinal access paths.
  Keep host/constant, entrance-aware packing and primary library guards until their rewrites pass.

Default allowances per prepared room are 65,536 selected tiles and 262,144 source-grid tile probes;
accepted ranges are 0-65,536 and 0-1,048,576 respectively. Scans charge each decoded tile in the
selected source grid, including cells outside the selected room. These are bounded local counters,
not complete end-to-end CPU/memory accounting. The eventual multi-room caller must share its
remaining allowances across candidates rather than resetting them for each copied room.

No preview rendering or new editor workflow is part of this adapter. Client appearance, legacy and
custom reference closure, physical door/hull/access checks, operational activation and publication
remain unverified. `CopyReady` and `ComponentReferencesVerified` remain false; the expanded-scope
estimate stays **69/100** pending an end-to-end capability exit.

Verification: normal Release build with localization hooks enabled and **314 selected integrations**
pass with zero failures/skips: 234 source-map cases (46 new terrain/materialization cases), 16 captured
prototype cases, 56 retained primary owned-generation cases, 5 hole-aware cases, 2 ordinary native
map-import cases and the actual sandbox test. Four changed C# source hashes remain stable during
that gate. Evidence: `.ks_procgen_room_terrain_final.log` and
`Content.IntegrationTests/TestResults/ks_room_terrain_final.trx`. After strengthening the four
combined-room cases to use anchored structures, another normal Release build and all six targeted
combined-room/atlas-origin cases pass (`.ks_procgen_room_terrain_anchors.log`,
`ks_room_terrain_anchors.trx`). The runtime implementation is unchanged by that test-only hardening;
the six-case follow-up is not represented as another 314-case full rerun. Four source files pass
UTF-8-without-BOM, CRLF and whitespace checks. No fresh Debug, full station-map suite or new
renderer/export artifacts are claimed.

The L-shaped fixture proves copying its captured footprint and actors, not room enclosure or clean
access. The combined adapter's caller must retire its owned map after any failure, including an
entity image-budget rejection after terrain writes. Final terrain verification rejects tile edits
made during entity startup. Native grid/frame identity and the allocation capture remain mandatory;
this is not authorization to paste into an arbitrary live or host map.

### Native room graph instantiation (2026-10-28, R08/R09/R10/R14, verified bounded prerequisite)

- [x] Add a native allocation path that reserves all room identities before component loading,
  using independent captured loading and metadata prototype images. Keep the allocation capture
  and caller-owned cleanup contract; do not create a separate renderer/export staging path.
- [x] Extend native map component loading to accept captured default mappings instead of live
  prototype-manager mappings. Retain native merge/copy/container hooks and modification ticks.
- [x] Add strict per-instance entity/network/map reference decoding. Unknown graph aliases throw;
  invalid world defaults remain invalid. Do not silently drop links while copying a room.
- [x] Implement an internal source adapter for a paused, owned, uninitialized grid. Preflight full
  override bodies and image/allocation allowances, rebase/rotate root poses consistently with
  integer cell transforms, retain child-local poses, load all components, then initialize members
  in parent-first order. Cancellation/failure returns no successful member receipt; the owning
  transaction must retire every captured allocation.
- [x] Verify actual native values and supported parent/reference graphs, independently repeated
  copies, all quarter turns, component removals, containers, strict dangling references and rollback.
  The 24 new native cases also cover unprototyped members, captured partial-override merging during
  allocation-time live prototype drift, image/allocation bounds, cancellation, initializer allocations,
  explicit MapInit suppression and late startup changes to earlier roots, free children and metadata.
- [x] Verify all members after every startup callback has returned. Reject changed parent/map,
  lifecycle, deletion queue, prototype identity/image, anchoring and local-rotation constraints.
  Roots and free children must retain their authored poses; native containers canonicalize contained
  child poses through the ordinary container hooks. Recheck the owned map identity and transaction.
- [x] Add bounded captured terrain copying with exact source-mask selection, flags/variants and
  native rotation/mirroring composition, plus a combined terrain/entity receipt. The preceding
  terrain-composition ledger records its verification; full hybrid integration remains unchecked.
- [ ] Wire this adapter into primary hybrid generation with tiles, complete layout alternatives,
  generated residual regions and composed physical access/port checks. Keep primary guards until
  those requirements pass. General constant/host capture, operational startup isolation and
  publication remain separate open requirements.

Verification: normal Release build with localization hooks enabled and **268 selected integrations**
pass with zero failures/skips: 188 source-map cases (24 new native copy cases), 16 captured-prototype
cases, 56 retained primary owned-generation cases, 5 hole-aware generation cases, 2 ordinary native
map-import/simulation cases and the actual sandbox test. Six changed C# source hashes remain stable
through the final gate; UTF-8 without BOM, CRLF and whitespace checks pass. Evidence:
`.ks_procgen_room_copy_complete.log` and
`Content.IntegrationTests/TestResults/ks_room_copy_complete.trx`. Earlier runs exposed and corrected
native anchoring and premature MapInit behavior; the final gate includes those assertions. The
resumed 264-case gate and subsequent 267-case callback audit remain intermediate evidence, not the
final 268-case inventory. No fresh Debug, full station-map suite, source-wide custom/client schema
closure, operational activation or publication is claimed.

The caller must own the paused uninitialized map/grid and allocation capture, enforce the shared
allocation ceiling (at most 256), and retire the entire captured transaction after failure. Default
per-copy image allowance is 4,194,304 characters (supported 0-67,108,864). Anchored members must be
grid roots at exact tile centers; offset/nested anchors reject rather than silently snapping. A
nonzero required rotation of a no-local-rotation root rejects. Registered overrides with known
inspection gaps reject; ignored client overrides remain captured but are skipped by the native
server loader, so complete client appearance is not certified. Unknown typed world links throw.
This does not establish generic legacy-string reference schemas or isolation of arbitrary startup
side effects. `CopyReady` and `ComponentReferencesVerified` remain false.

No capability credit is awarded for this internal prerequisite: **69/100** remains the verified
expanded-scope assessment. Do not treat this adapter as an editor feature or complete hybrid workflow.
No renderer, gallery, export or showcase artifact is changed in this batch.

### Authored room component images (2026-10-27, R08/R09/R10/R14, verified prerequisite)

Showcase presentation is closed for this phase. Continue the actual generator through immutable
prefab instantiation and hybrid composition; do not expand galleries or renderer infrastructure.

- [x] Retain canonical immutable component override bodies for each extracted room member,
  including ignored client component data, tags and nested data. Keep removal lists and
  map-local reference witnesses separate. Parser nodes never escape the source adapter.
- [x] Bump extraction identity to schema 3 and bind every returned component body to its hash;
  source hashes and override type names alone cannot describe an extracted blueprint.
- [x] Bound selected image nodes, characters and depth. Share node/character allowances across
  all selected rooms, including overlapping atlas masks; reject atomically without a partial
  extraction. Excluded entities consume source traversal work but no selected-image allowance.
- [x] Verify authored values, detached decode mutation isolation, cumulative exact/short limits,
  structural bounds, reference preservation and retained source-extraction regression cases.
  Decode an authored light into the actual native component type and assert radius/energy; retain
  ignored client sprite overrides without marking their reference inspection complete. Require
  callers constructing extracted members to supply their override image collection explicitly.
- [ ] Instantiate complete rooms from these images and captured prototype defaults, allocating
  members before remapping references and initializing them. Preserve hierarchy, removals,
  tile metadata, rotation and anchoring; prove independent repeated copies and cleanup.
- [ ] Compose automatically fitted complete alternatives with procedural residual areas in the
  primary owned hybrid transaction, then verify physical ports and all required access paths.

The first three steps preserve source data; they do not certify generic component dependencies,
container/support closure, native load-hook isolation or activation. `CopyReady` and
`ComponentReferencesVerified` remain false. Keep the primary hybrid/library guards until actual
instantiation and composed validation are implemented. No new capability point is assigned:
**69/100** remains the verified expanded-spec estimate.

Selected-image defaults: 262,144 nodes, 4,194,304 canonical characters and depth 64. Accepted limits
are 0-1,048,576 nodes, 0-16,777,216 characters and depth 1-128. Zero image allowance permits only
selections without component override images. Existing source-map decoding, parent-graph,
mask-selection and prototype-default allowances remain independently enforced. Budget exhaustion
reports `RoomEntityOverrideImageBudget`; excessive image nesting reports
`RoomEntityOverrideDepthBudget`. This is not a claim of complete end-to-end CPU/memory accounting.

Verification: normal Release build passes with the localization hooks enabled. All **181 selected
integration tests pass**, with zero failures/skips, covering the source-map adapter, captured
prototype allocation and the actual sandbox test (identified by its TestMethod class, rather than
its generic `Test` display name). The new override-image fixture contributes 13 cases. Six changed
C# source hashes remain stable throughout the final build/test run; all six retain UTF-8 without
BOM, CRLF and no trailing whitespace. Evidence: `.ks_procgen_override_images_final.log` and
`Content.IntegrationTests/TestResults/ks_override_images_final.trx`. No fresh Debug or full station-map
suite is claimed. The final gate follows a compiler-caught nullable output contract after making
image collections required; the helper now explicitly guarantees a non-null collection on success.
No preview renderer, gallery, export, screenshot or showcase artifact is changed in this batch.

### Referenced resource byte capture (2026-10-27, R08/R09/R10/R14, observed scope verified)

Manifest schema 11 is verified for the observed native resource scope. Observed `ResPath` and
`SpriteSpecifier` values now retain detached VFS bytes and SHA-256 identities. Explicit sprite
observation is necessary because the native RSI writer does not pass its serialization context
into its nested path write; entity-based icons likewise need explicit typed entity observation.
This does not discover arbitrary string fields or ignored client/custom component schemas.
`FullContentClosureVerified`, source `CopyReady` and `ComponentReferencesVerified` remain false.

- [x] Capture files and referenced RSI folders (metadata plus every declared state frame), with
  compiled `.rsic` metadata fallback. Canonicalize native rooted/relative texture forms; preserve
  explicit source-resource seed provenance separately from candidate roots and discovered refs.
  Hash discovered paths/states and exact file identities into the detached prototype image.
- [x] Add independent allowances for resource-reference occurrences, file count, per-file and
  aggregate bytes, visible directory entries, RSI states and JSON depth. Defaults are respectively
  65,536 references, 4,096 files, 16 MiB/file, 128 MiB total, 16,384 directory entries,
  16,384 states and depth 32. Charge references/visible directory entries before deduplication;
  use a one-byte sentinel to detect over-limit nonseekable input without reading an entire file.
- [x] Fail atomically on missing/invalid resources and malformed RSI metadata. Dispose streams on
  success and failure; return resource-specific issues rather than unrelated prototype errors.
  Revalidation converts missing/invalid captured resources into manifest drift. Stop processing
  further definitions after the resource observer has already recorded an issue.
- [x] Pass 36 focused real-engine resource cases: detached/read-only bytes, same-size drift,
  matching parent-body checks, automatic path/RSI/entity-icon observation, canonical path forms,
  directory ordering and repeated entry work, exact/chunked nonseekable reads, invalid contracts,
  missing/throwing resources, folder/compiled RSI metadata and frames, state presence/duplication,
  state/JSON-depth allowances and no partial manifests. The initial compiled fixtures exposed a
  bad PNG IDAT CRC in test data; corrected the fixture bytes without weakening native validation.
  Evidence: `ks_resources_focused_fixed.trx`, `.ks_procgen_resources_focused_fixed.log`.
- [x] Verify two source-only path/texture override cases join extraction identity, preserve
  candidate-root authority, reject missing bytes and change replay identity on same-size drift
  without loading source entities. Verify two selected/unselected candidate cases retire owned
  stages after resource drift. All 40 new cases pass together in
  `ks_resources_control_positive.trx`; the preceding broad preflight passes 933 cases with no
  failures/skips, retaining the earlier tests. The final broader gate must include both newly
  added ownership cases as well. Evidence: `.ks_procgen_resources_regressions.log`,
  `ks_resources_release_regressions.trx`.
- [x] Prove four compile-valid isolated controls: omitted ordinary path observation causes three
  assertion failures; omitted whole-image byte comparison causes three; deduplicating resource
  references before charging causes one; omitting override resource seeds causes two. Restore
  every mutated source to its original bytes in `finally`. All 40 then-current new cases pass on
  restored sources. Evidence: `.ks_procgen_resources_controls_audit.log`, control build/test logs
  and `ks_resources_control_restored.trx`.
- [x] Reject empty/whitespace entity-based sprite IDs instead of letting them disappear through
  the optional-entity-reference sentinel path. Two added cases plus the preceding 40 new cases
  and the existing native office probe pass together: **43/43**, zero failures/skips, in
  `ks_resources_final_focused.trx`. The new-case total is now **42**.
- [x] Complete final normal Release/fresh Debug gates: each passes 891 units, 937 integration/
  sandbox cases and 31 serialization cases, zero failures/skips; inventories agree and preserve
  every earlier case. All 27 source hashes remain unchanged. Evidence:
  `.ks_procgen_resources_gates_audit.log`, `.ks_procgen_resources_final_audit.log`, and
  `ks_resources_{release,debug}_{units,integration,engine}.trx`.
  These supersede the earlier schema-10 configuration checkpoint for the observed resource scope.
- [x] Measure the native office against unchanged defaults: 649 combined definitions,
  7,253 typed reference occurrences, 133,845 canonical nodes and 2,987,550 canonical characters;
  1,226 resource occurrences, 327 unique resource references, 936 captured files and 3,819,355
  detached bytes. The four-character canonical reduction follows the corrected table sound
  paths. Both elevated prototype limits and unchanged defaults capture identical images.
  The one legacy `femaleMask` declaration-schema gap remains explicit.
- [x] Regenerate all five schema-11 showcase exports and installed native maps; save/load checks
  pass and installed bytes match. Validate 72 sprite-source hashes and six output/plan associations;
  the six sprite PNGs retain exact prior bytes. Refresh both browser overviews and verify five
  loaded images, two modes and five nonempty working overlay controls. Preserve six historical
  review files. Evidence: `.ks_procgen_resources_showcases.log`, `ks_showcases.trx`,
  `.ks_procgen_resources_browser_audit.log`, `.ks_procgen_resources_final_audit.log`.
- [ ] **R14 resource preprocessing rewrite:** VFS enumeration deduplicates internally before
  exposing entries. Its hidden raw enumeration work is not metered by the visible-entry cap.
  ImageSharp identifies compiled PNG metadata before the expanded JSON length check; native
  metadata decompression/decoder work is likewise trusted preprocessing. Add a bounded native
  enumeration/metadata adapter before claiming a hard per-operation CPU/allocation limit or a
  responsive resumable job. Current byte/state/JSON bounds do not prove that stronger property.
- [ ] **R14 resource capture profiling:** instrument file opens, hashing and repeated parent-body
  checks before optimizing the new byte capture. The broad preflight took 14m26s for 933 cases;
  the prior schema-10 Release run took approximately 6m31s for 895 cases. These different runs
  and workloads do not establish causality or a controlled performance ratio. Measure individual
  stage operations and test-pool overhead. Any reuse must bind an immutable resource revision or
  perform a fresh comparison at the required authority boundary; do not hide drift with a cache
  keyed only by file path or timestamps.
- [ ] Complete ignored client/custom/legacy resource schemas, code identity and portable native
  asset capture/import. Resource snapshots alone do not validate image pixels, shader/animation
  appearance, actual audio decoding, native client rendering or portable replay publication.

Capability credit remains **68/100** while this prerequisite is verified. Helpers and additional
assertions do not constitute a completed authoring, generation or publication workflow.

### Clothing schema and explicit string resource paths (2026-10-27, R08, in progress)

- [x] Audit the `femaleMask` gap against current shared/server/client code. The field has no
  consumer; the remaining `ClothingMask` enum is unused. Current client equipment visuals select
  inventory displacement maps by sex. Do not reintroduce a dummy data field to silence validation.
- [x] Explicitly migrate the two skirt bases and the towel base: remove their obsolete active
  `femaleMask: UniformTop` declarations with inline KS14 comments preserving the old value and
  explaining the current displacement mechanism. Keep public enum compatibility; do not weaken
  unknown-field detection or silently discard arbitrary future declarations.
- [x] Add an opt-in `KsResourcePathStringSerializer` in shared serialization code and annotate
  `ClothingComponent.RsiPath`. Preserve its public string/YAML representation while forwarding
  resource semantics through native `ResPath` serialization and validation. It has no dependency
  on the procgen system and is not the global string serializer. Existing arbitrary text remains
  text; only explicitly annotated fields acquire resource semantics.
- [x] Pass 52 focused native cases, including automatic resource-string observation and source
  overrides, three clothing string/YAML roundtrips, all three migrated abstract declarations and
  continued reporting/hashing of injected unknown fields. The office now has zero registered
  declaration-schema gaps; its 649 definitions capture 1,145 files / 4,174,678 bytes from 336
  unique resource references / 1,238 occurrences. Complete client/custom/legacy coverage and
  portable replay remain explicitly unverified. Evidence: `ks_clothing_tests.trx` and
  `.ks_procgen_clothing_tests.log`.
- [x] Pass the actual client sandbox check after adding the shared serializer.
- [x] Audit four broad native prototype-test failures using an isolated reverted-annotation
  baseline. All four outcomes and normalized failure signatures reproduce exactly without the
  new annotation. The sandbox case passes. Source restores byte-for-byte. Evidence:
  `ks_clothing_native_validation.trx`, `ks_clothing_global_baseline.trx`,
  `.ks_procgen_clothing_global_baseline_audit.log`. These four tests remain failed, not green.
- [ ] **R18 global prototype-fixture rewrite:** `PrototypeSaveTest.TestEntityUidContext` only
  handles `EntityUid`; all-prototype save/load tests now encounter test-only `NetEntity` fields
  and throw before finishing. Add invalid-only network/map default handling consistent with
  native prototype contexts, preserving rejection of live references. Create the client context
  from the client serialization manager instead of passing a server-backed provider.
- [ ] **R18 material-fixture rewrite:** concrete procgen material test prototypes omit valid icons;
  native global validation sees the `SpriteSpecifier.Invalid` self sentinel as `/Textures` and
  rejects it. Supply real fixture icons or use abstract declarations where appropriate, then
  adjust resource/counter expectations without weakening native validation. Re-run the four
  global prototype tests with failure-preserving teardown. Do not silence or skip these errors.
- [x] Verify two isolated compile-valid context-omission controls: dropping context in the shared
  writer loses automatic resource-string observation; dropping context in validation loses source
  override observation. Each produces one intended assertion failure. Sources restore exactly;
  the final normal Release build and all 53 focused/sandbox cases pass on restored sources.
  All 891 procedural units also pass. Evidence: `.ks_procgen_clothing_controls_audit.log`,
  `ks_clothing_control_{writer-context,validator-context}.trx`, `ks_clothing_final_restored.trx`,
  `ks_clothing_units.trx`, `.ks_procgen_clothing_final_audit.log`.
- [ ] Incorporate these nine added cases into the next full procgen Release/Debug gates after the
  related R18 fixture repairs. The last complete broad checkpoint remains 891/937/31; the scoped
  53/891 checks above do not replace it. This batch does not reopen the completed showcase refresh
  or award a complete-manifest capability point.

### Global prototype-fixture repair (2026-10-27, R18, in progress)

- [x] Add a test-only serializer for invalid `NetEntity`/`MapId` defaults and register it in the
  existing prototype save context. Preserve legacy `EntityUid` handling, including its transform
  exception, instead of weakening map-save checks. Reject live/malformed network and map references
  in read/write/validation; six dedicated context cases cover both types.
- [x] Construct client prototype-test contexts from the client serialization manager rather than
  reusing a server-backed provider.
- [x] Give the eight concrete material fixtures valid texture icons. Keep the fixtures concrete
  where their tests require runtime objects; do not mark them abstract to hide invalid defaults.
- [x] Audit the first repaired preflight: 221/223 cases pass. Both all-prototype save/load/save
  tests now pass; remaining failures expose the resource probe's unset path/icon sentinels.
  Change those optional probe fields to nullable defaults. Configured values still pass through
  native/contextual validation, and resource budget/capture assertions remain intact.
- [ ] Verify the expanded selected suite, including all four previously failed global prototype
  tests, resource/clothing/source-map suites, original typed/inheritance tests and sandbox.
- [ ] Prove live-reference rejection and fixture-icon validity with isolated compile-valid
  controls, restore exact sources and include these changes in the pending broad configuration
  gates. Keep the last 891/937/31 checkpoint historical until the new gates pass.

### In-game room-theme authoring (2026-10-27, R16, in progress)

User priority: stop expanding preview infrastructure and deliver generator/editor behavior.
The completed showcase batch stays closed. Remaining fixture/broad-gate follow-up remains recorded.

- [x] Implement the first native editor command, `ksprocgen_theme [saved-key]`, for mapping admins.
  Its EUI selects loaded floor/wall/light/entity packs and configures furnishing density, lighting
  coverage, coherent contents, open center and loose items. Pack selectors use server-supplied
  bounded catalogs, with visible truncation. No raw YAML editing or offline preview is required.
- [x] Build editor values into the existing immutable content-draft and theme-catalog contracts.
  Validate selected references/candidate pools on the server. Keep EUI-bound document identity,
  expected revisions and permission rechecks; close on permission loss. Preserve local unsaved
  input on errors/conflicts.
- [x] Implement immutable saved revisions in server user data under `/Procgen/RoomThemes`.
  Server-issued GUID/revision keys prevent arbitrary paths. Write a temporary file, then rename
  without overwriting an earlier revision. Bound loaded bytes/JSON depth and revalidate catalogs.
  `KsProcgenThemeLibrarySystem.TryCaptureCatalog` exposes saved themes to normal generator callers.
  Opening a saved key creates an explicitly labeled editable copy with a fresh document identity.
- [x] Compile native client/server/XAML and verify shared/client sandbox compatibility.
- [x] Verify saved values are detached from caller edits, reload into a normal catalog, and generate
  a valid 1x3 scene. Reject duplicate revision writes, missing packs and arbitrary file keys while
  retaining previous revisions. The virtual file provider reports duplicate rename differently
  from the physical provider; both must return a rejected save rather than an unhandled exception.
- [x] Verify the connected native client can open the window, click Save and receive the persistent key.
  Normal Release build and all seven focused editor/generator/sandbox tests pass, with zero skips.
  Evidence: `.ks_procgen_theme_editor_verified.log` and
  `Content.IntegrationTests/TestResults/ks_theme_editor_verified.trx`. These checks cover the first
  theme-authoring slice; they do not replace the full Release/Debug acceptance gates. The fixed
  capability assessment remains 68/100 pending the remaining R16 workflow exits.
- [x] Verify all four pack selectors, reopen a saved copy under a fresh identity, save it without
  changing its source, reject stale revisions and allow a valid retry, and close the native window
  when mapping permission is lost. Validate old files without utility fields and reject oversized,
  malformed, mismatched-identity and unknown-schema stored files.
- [ ] Finish hard I/O failure/cleanup handling and complete editor input-limit coverage. Full
  document-service/undo/import/export/application acceptance remains separate from this slice.

Mapper workflow for this slice: run `ksprocgen_theme`, choose loaded packs and room goals,
then use **Validate and save theme**. Copy the resulting GUID/revision key. Running
`ksprocgen_theme <key>` opens an editable copy; saving that copy creates its own identity.
Server-side generation callers use `KsProcgenThemeLibrarySystem.TryCaptureCatalog(key, ...)`
and pass the resulting catalog as `content` to the existing generator with theme ID `Theme`.
This slice does not provide a map-painting or live-map apply button.

- [ ] Extend to weighted multiple-pack editing, creating/editing pack contents and relational
  assemblies, atmosphere/electricity controls, area painting and integrated generation/apply.
  This first theme-authoring slice must not be described as the complete R16 workspace.

The fixed assessment stays 68/100: this native authoring slice does not close the complete
R16/replay/release exits or add new utility-operation credit.

Documentation-only specification revision, preceding the implementation batch below:

- [x] Inspect local atmos, node topology, extension coverage and power-state code; record primary
  source links and correct the assumed LV radius from 2 to the current default of 3 world units.
- [x] Specify gas profiles/initialization, automatic distribution and in-game asset authoring with
  runtime evidence, fallbacks, ownership and bounded work requirements.
- [x] Reconcile scope, request/theme fields, pipeline, result evidence, defaults and stale top-level
  revision status. Add A53-A66 and R08-R18 without marking those features implemented.
- [x] Review the existing architecture for concrete integration, lifecycle, search, metering,
  lighting and authoring limitations; retain existing conservative guards and historical evidence.
- [x] Check all 28 relative document links, unique contiguous A01-A66 fixture IDs and balanced
  Markdown code fences; inspect the scoped diff and whitespace. Only the specification changed
  in that documentation revision; its checks supplied no C# build or runtime/UI test evidence.

### Theme utility-profile authoring (2026-10-27, R16, in progress)

- [x] Add atmosphere and electricity profile selectors to the native room-theme editor. Supply
  bounded sorted catalogs from the server, retaining the existing visible catalog limit. None
  leaves the utility unspecified; it must not invent a default gas fill or power requirement.
- [x] Carry profile IDs through detached editor values, immutable saved revisions and normal
  theme-catalog capture. Validate missing/malformed profiles through the existing utility resolvers.
  Existing saved files without the optional fields retain empty selections.
- [x] Keep the form scrollable and identify selector items by prototype ID. Explain why a wired-only
  profile lays infrastructure without proving operating power; gas filling still requires closure.
- [x] Verify selection of all four pack kinds and both utility profiles through the connected native
  client Save workflow. Generate from reloaded standard-air, nitrogen and vacuum choices; initialize
  a nitrogen room with schema-two closet electrical service and inspect native gas/wiring evidence.
- [ ] Complete custom mixture/profile editing, converter and distribution preferences, and the
  full room-pack/relational authoring workflow. Profile selection alone does not complete the
  atmosphere/electricity panels or the R16 workspace acceptance exit.

- [x] Repair atomic load publication: a stored theme whose profile validation fails returns neither
  editable values nor a partial content draft. Keep validated draft publication after catalog capture.
- [x] Verify actual gas species/fractions, temperature and pressure on reloaded standard-air,
  nitrogen and vacuum cases, including combined nitrogen/closet electrical generation. Inspect
  native topology/cable/converter evidence and complete floor service while keeping operating
  supply and stable-at-rest evidence false. Retire scenes without leaking their entities.
  Normal Release build and all 20 focused editor/generator/sandbox cases pass with zero failures
  or skips, including native save/copy, stale retry, permission loss, legacy files and atomic
  rejection. Evidence: `.ks_procgen_theme_utilities_verified.log` and
  `Content.IntegrationTests/TestResults/ks_theme_utilities_verified.trx`. The preceding 20-case
  run in `.ks_procgen_theme_utilities_final.log` also passed before the stronger gas assertions.

The earlier broader regression process (`.ks_procgen_theme_utilities.log`) was explicitly stopped
before a final verdict because the loader/session verification source had advanced. It is not a
passing gate. The final focused run covers changed authoring behavior and real native generation;
full Release/Debug acceptance remains open. No additional weighted credit is claimed. The completed
showcase/presentation work remains closed. The scoped native power-reconnect prerequisite is now
verified in its ledger below; isolated solver/battery synchronization and operating power remain open.

### Standalone rooted owned planning (2026-10-17, R09)

- [x] Allow a root-bearing connection group with no ports; reject groups with neither ports nor
  roots. For explicit empty entrance domains with no authored configurations, derive deterministic
  rooted networks: one per target island under `PerIsland`, one required group under `SingleNetwork`.
  Preserve every supplied request root, otherwise prefer the first canonical writable cell.
  `DeclaredNetworks` still requires explicit group/root configurations; do not invent them.
- [x] Audit root-only groups through the existing local/full component analysis, exact isolation,
  root reachability and complete floor ownership. Keep normal port-bearing validation/reason codes.
  No synthetic entrance, threshold, host passage or separate planning algorithm is introduced.
- [x] Reuse the existing routing, growth, partitioning, themes, structure, supported furnishings,
  lighting and independent interior audit. Keep null entrance-domain legacy semantics. Canonical
  snapshots retain resolved root configurations; the original authoring request remains untouched.
- [x] Existing 764 procgen units pass after these changes. Seventeen focused standalone/layered
  integration cases pass with zero skips, including 1x3, islands, rejected false single-network
  connectivity, explicit roots/networks, forbidden joins, normalized roundtrips, graph exhaustion
  and a complete table/device/contained-item room with no host floor. The first focused run found
  a fixture expecting canonical snapshots to omit resolved defaults; assert retained normalized
  configurations and identical replay semantics instead. Evidence:
  `.ks_procgen_standalone_initial_units.log`, `.ks_procgen_standalone_roundtrip_debug.log`,
  `.ks_procgen_standalone_roundtrip.log` and the corresponding TRX files.
- [x] Independently discard explicit roots and bypass exact-group join rejection in compile-valid
  source. Each targeted case fails as a real failure, with zero skips; restore both sources exactly
  in `finally`. Evidence: `.ks_procgen_standalone_mutation_root_build.log`,
  `.ks_procgen_standalone_mutation_root.log`, `.ks_procgen_standalone_mutation_isolation_build.log`,
  `.ks_procgen_standalone_mutation_isolation.log` and `ks_standalone_mutation_*.trx`.
- [x] Index explicit roots once by cell rather than rescanning all roots for every target island;
  reject excess auto-derived root declarations before indexing. Preserve duplicate validation and
  canonical ordering. Repeat the explicit-root fault against the indexed implementation: one real
  failure, zero skips, source restored exactly. Evidence:
  `.ks_procgen_standalone_indexed_mutation_build.log`, `.ks_procgen_standalone_indexed_mutation.log`
  and `ks_standalone_indexed_mutation.trx`.
- [x] The first full gate passed Release/fresh Debug, 764 units and all 302 procgen integration cases,
  including the new single-tile case, but actual sandbox verification failed: two nonempty list
  collection expressions lowered to forbidden `CollectionsMarshal.SetCount` calls. Replace them
  with ordinary list initializers; do not change sandbox permissions. That failed run is retained
  in `.ks_procgen_standalone_final_*.log`/TRX for review and is not a successful full gate.
- [x] The corrected full run again passes all 302 procgen cases but actual sandbox verification
  rejects the newly introduced `ILookup` type. Replace the LINQ lookup with ordinary dictionary/
  list indexing, preserving duplicate validation. No whitelist change. The failed second run is
  retained in `.ks_procgen_standalone_corrected_final_*.log`/TRX; rerun the actual sandbox on the
  dictionary implementation before final acceptance.
- [x] The dictionary implementation passes all 10 standalone cases and the actual sandbox check
  (11 total, zero skips), including single-tile/1x3 and the early root-budget rejection. Repeat
  explicit-root removal on this implementation: one real failure, zero skips, exact source restore.
  Evidence: `.ks_procgen_standalone_dictionary_debug.log`, `.ks_procgen_standalone_dictionary.log`,
  `.ks_procgen_standalone_dictionary_mutation_build.log`,
  `.ks_procgen_standalone_dictionary_mutation.log` and corresponding TRX files.
- [x] Final dictionary-source Release succeeds with 512 existing warnings and zero errors; fresh
  Debug succeeds with 1,107 existing warnings and zero errors. All 764 procgen units and 303 selected
  integration cases pass with zero skips. Match the actual sandbox definition/result to confirm
  `SandboxTest.Test` passed. No warning suppressions, sandbox whitelist changes or new warnings.
  Evidence: `.ks_procgen_standalone_dictionary_final_release.log`,
  `.ks_procgen_standalone_dictionary_final_debug.log`, `.ks_procgen_standalone_dictionary_final_units.log`,
  `.ks_procgen_standalone_dictionary_final_integration.log` and `ks_standalone_dictionary_final_*.trx`.
  The full repository suite was not run. Encoding, CRLF, whitespace, relative links, contiguous
  acceptance IDs and Markdown fence checks pass.
- [x] Award one layout point for standalone rooted networks traversing the complete owned pipeline
  and independently audited layered room proposals with no host geometry. Layout advances from
  15 to 16 of 22; total expanded coverage is approximately 61%. No repeated credit for baseline
  small-mask geometry, existing support math, or shared placement extraction. No engine operation,
  utility, UI or publication credit. The 80% goal remains active.
- [ ] Continue complete owned scene materialization below. Root derivation is input bounded under
  existing contract limits, not a fully metered request ledger. Proposals do not prove usable or
  spaceproof rooms. Null-domain legacy handling remains deliberate; the stage entry must capture
  its resolved standalone input before allocation rather than silently changing review artifacts.

### Hole-aware room-size fitting (2026-10-27, R04/R09/R14, verified bounded subset)

User priority: the showcase/presentation batch is closed; continue actual room selection and area
filling. This batch extends the primary procedural generator rather than adding presentation code.

- [x] Replace the irregular strip fitter's contiguous-column restriction with exact column cell
  sets and cardinal overlap counts. A hole may split a boundary into multiple spans; all spans
  contribute to wall loss, while a door requires a common threshold/approach coordinate across
  three columns. Keep the normal seam solver authoritative for walls, protected floor and doors.
- [x] Require cardinal connectivity for every proposed non-convex strip. Cache interval witnesses
  within each axis/order search, retain the existing bounded cut/vector search, and reject
  disconnected proposals before they reach the seam solver. Do not fill a hole to make a room fit.
- [x] Charge the added connectivity floods to the request's graph allowance, including failed
  quota fits, separate networks and whole-allocation retries. An exhausted fit returns no layout
  artifacts or fallback success. Preserve existing metadata caps and scalar fallback when fitting
  is infeasible with sufficient work. Column-convex and rectangular cases keep their old path.
- [x] Verify exact 28/33 courtyard rooms, simultaneous 16/23/35 rooms around two courtyards,
  transposed/canonical inputs, global quotas across separate courtyard islands, exact/short graph
  budgets, fitting exhaustion and disconnected-strip fallback through the real partition path.
- [x] Generate and initialize those layouts through `KsProcgenOwnedStageSystem`, with merged-room
  and size-deviation fallback denied. Inspect real hole tiles and request-wide size outcomes;
  verify cleanup leaves no generated entities. Run retained size-goal and procedural regressions.
- [ ] Continue entrance-aware prefab packing, source-room instantiation and complete hybrid
  composition. The legacy packing guard and primary owned hybrid/source guards remain necessary;
  extending pure room-size fitting does not establish prefab/hybrid materialization.

- [x] Extend the same-footprint example to one 35-tile large room, one 23-tile medium room and
  three 16-tile small rooms. Verify four real door reservations, exact requested counts and no
  hole tile writes through normal generation/initialization. No presentation/export work is involved.

Verification: normal Release build, all 906 procedural/power-storage units and all 62 selected
loaded-server/sandbox cases pass, with zero failures or skips. Retain the older size-goal and owned
interior retry/fallback cases. Seven changed source hashes remain stable throughout final gates.
Evidence: `.ks_procgen_hole_rooms_final.log`, `Content.Tests/TestResults/ks_hole_rooms_final_units.trx`
and `Content.IntegrationTests/TestResults/ks_hole_rooms_final_native.trx`. Two compile-valid controls
restore the old split-column exclusion and bypass shared fitting work: each produces exactly two
intended assertion failures. The fitter source is restored byte-for-byte before final builds;
`.ks_procgen_hole_rooms_controls.log` and `ks_hole_control_{hole-guard,fit-budget}.trx` retain evidence.
No fresh full Debug or complete repository gate is claimed.

This end-to-end bounded courtyard-fitting exit adds one layout point, taking the fixed assessment
from 68/100 to 69/100. It adds no duplicate engine/interior/utility credit. This remains an axis-strip
strategy, not a complete arbitrary-shape partition optimizer; valid fallback and budget reporting
remain required. Prefab/hybrid source instantiation and the overall 80% goal remain unfinished.

### Scoped native power reconnect (2026-10-27, R10/R12, verified scoped subset)

This implements the next electrical runtime prerequisite. It must not be credited as operating
supply, solver isolation or permission to publish a room. This checkpoint retained 68/100 pending further weighted exits; the current capability index above
records later verified layout work.

- [x] Add internal scoped reconnect/read-only inspection APIs in the native `PowerNetSystem`
  partial extension. Reuse `DoReconnectApcNet`/`DoReconnectPowerNet`, with complete native-node
  topology inspection before solver membership processing. Never call global `Update` or clear
  global reconnect queues. Reconnect only selected pending networks; repeats inspect clean ones.
- [x] Preflight registered network/load/supply/battery objects, paused state, group/component
  membership in both directions, receiver/provider backlinks and current writable network lists.
  Reject foreign, missing, stale, duplicate or unsupported bindings before the first write.
  Require work for validation, native reconnect and final comparisons before modifying links.
- [x] Integrate through the existing owned electrical initialization transaction and shared work
  allowance. Add `NativePowerLinksVerified` to the initial wiring receipt only after this path
  succeeds. Read-only repeat/gas-callback checks also validate native solver membership; neither
  that flag nor `ElectricalTopologyVerified` claims delivered power.
- [x] Repair native `PowerState.GenIdStorage` capacity growth: the final newly allocated slot was
  left at generation zero, contrary to the allocator/restore contract. Initialize that slot too;
  preserve stale-ID invalidation and normal storage registration. Four unit cases now pass across
  capacities 4/8/16, restore, and freed-last-slot reuse. A confirmed preflight `OwnedPowerNetworkDrift`
  exposed this allocator issue; the obsolete preflight was stopped before a final suite verdict.
- [x] Compile and verify own/foreign pending-queue behavior, load/supply/receiver/battery links,
  unchanged stored/delivered power, exact versus short work, cancelled calls, foreign members,
  omitted native-list entries, unpaused loads, stale registrations and read-only drift rejection.
- [x] Run retained node-scope/native wiring, combined saved-theme gas/wiring, compact utility-room
  and small native power-system acceptance cases. Prove key assertions with compile-valid negative
  controls and restore sources. Snapshot all public scalar load/supply/battery/network fields;
  only intended member lists and LinkedNetwork backlinks may change. Include the receiver state
  on node-less machines while collecting graph networks only from entities with node containers.
- [ ] Continue isolated solver/battery synchronization, operational sources and supply-under-load
  validation. Preserve the operational-supply guard; this adapter does not finish R10/R12.

Verification: normal Release build and 895 procedural/power-storage units pass without skips.
The broad 97-case runtime run passed 96 cases, including saved-theme gas/wiring, compact electrical
rooms, native power fixtures and retained solver-backlink rejection. Its sole failure was a new
snapshot helper calling the strict node accessor on a node-less computer. After the test-only guard
fix, all 32 node/power scope cases pass with seven source hashes stable throughout. The receiver's
scalar state remains included; no production guard was relaxed. Latest retained outcomes therefore
cover all 97 runtime cases, but the original 97-case run itself must not be described as all green.
The earlier 52-case preflight found diagnostic precedence and pending-network-count expectations;
those corrections are covered by the retained runs. No fresh full Debug or all-station-map gate is
claimed. Evidence: `.ks_procgen_power_reconnect_{allunits,final,verified,audit}.log` and corresponding
`ks_power_reconnect_allunits`, `ks_power_reconnect_final_native`, `ks_power_reconnect_verified_native`
TRX files in the test projects' `TestResults` directories.

Two isolated compile-valid negative controls produce exactly three intended allocator failures and
one intended foreign-queue failure, with zero skips. Both controlled sources are restored byte for
byte before normal builds. Evidence: `.ks_procgen_power_reconnect_controls.log` and
`ks_power_control_{storage,queues}.trx`. The tests use normal component registration/native reconnect
hooks; procgen does not patch private provider links or solver values. Test mutations are restored
before fixture destruction. Source growth repair and this scoped prerequisite add no operating-
supply credit; this checkpoint retained 68/100; consult the current capability index for later layout credit. The 80% goal stays active.

### Native node inspection and queue/frame rewrites (2026-10-21, R10/R12)

- [x] Add `KsInspectOwnedPowerNodes` alongside rebuilding, sharing the bounded scene/graph capture
  and current native component checks. Inspection never enters the mutation branch, replaces
  groups, registers networks, emits rebuilt callbacks or drains pending queues. Pending owned
  work is an explicit `OwnedNodesPendingRebuild` result rather than permission to repair it.
  Both paths return `KsOwnedNodeTopologyResult`; neither certifies supply or activation.
  Inspection checks current native consistency against freshly read edges, not equality to an
  authored utility plan. The caller still pins scene/profile/interface identity and separately
  checks declared device state, provider assignment, consumers and operational requirements.
- [x] Pin the grid's map ID/UID and registered map identity. Recheck map/grid retirement, grid
  component identity, unit size, disabled splitting, transform/frame identity and member map/grid
  identity after callbacks. Split enablement, unpause, position changes and reparenting must reject.
- [x] Extend actual engine coverage to 21 scoped-node cases, retaining the prior ten. New cases
  check repeated read-only group/network identity and callback counts; drift without repair;
  inspection work/cancellation; separate foreign remake/removal queues; rejected owned callback
  reflood/remake work surviving until the ordinary updater; and all four frame mutations.
  Initial and refined preflight both pass all 21, zero failures/skips. Evidence:
  `.ks_procgen_noderewrites_{initial,preflight}_build.log`,
  `.ks_procgen_noderewrites_{initial,preflight}.log`, and `ks_noderewrites_{initial,preflight}.trx`.
- [x] Prove the new exits with mutations for inspection becoming mutating, split-frame checks,
  foreign remake/removal isolation and preservation of both callback requeue kinds. Restore exact
  source bytes before final positive gates.
- [x] Six compile-valid mutations produce nine assertion failures and 117 unaffected passes,
  zero skips. Mutating inspection fails three cases; bypassed split-frame checks and global
  remake/removal processing each fail one; lost callback reflood work fails two; lost callback
  remake work fails the old-group retirement assertion. Every mutation runs all 21 cases.
  Exact source bytes are restored and compared in `finally`. Evidence:
  `.ks_procgen_noderewrites_gates_summary.log`,
  `.ks_procgen_noderewrites_mutation_<name>_build.log`,
  `.ks_procgen_noderewrites_mutation_<name>.log`, and `ks_noderewrites_mutation_<name>.trx`,
  for `inspect_mutates`, `split_frame`, `foreign_remake`, `foreign_remove`, `restore_reflood`,
  `restore_remake`. Restored-source positive gates follow.
- [x] Run restored Release/fresh Debug, retained procgen/fixture/native/sandbox cases, identity
  audit, formatting checks and completed-runner cleanup. Expected integrations: 625, including all
  previous 614 cases plus eleven new cases.
- [x] Restored normal-hook Release build passes (403 warnings, zero errors; 46s). All 809 units,
  625 selected integrations/sandbox cases and 23 native contracts pass with zero failures/skips.
  Fresh Debug with `--no-incremental` also passes its normal-hook build (1,066 warnings, zero errors;
  6m36s) and the same 809/625/23 gates. Case identity comparison retains all prior 809 units,
  614 integrations and 23 native contracts with multiplicity, plus all current 21 scoped-node
  preflight cases. Actual `Content.IntegrationTests.Tests.Utility.SandboxTest.Test` definitions map
  to Passed in both configurations. Evidence:
  `.ks_procgen_noderewrites_{release,final_debug}_build.log`,
  `.ks_procgen_noderewrites_{release,final}_{units,integration,engine}.log`,
  corresponding `ks_noderewrites_{release,final}_{units,integration,engine}.trx`, and
  `.ks_procgen_noderewrites_final_audit.log`. Full-repository tests are not claimed.
- [x] All four touched source/document files pass strict UTF-8/no-BOM, CRLF and trailing-whitespace
  checks. All 48 specification fences are balanced; main and engine `git diff --check` pass.
  The completed runner is removed. Evidence: `.ks_procgen_noderewrites_final_style.log`.
  Existing module revisions and unrelated changes are preserved. No additional capability points
  are awarded for this unintegrated prerequisite; the 80% goal remains active at approximately 64%.
- [ ] Integrate inspection/rebuilding with the primary infrastructure plan and shared stage
  lifecycle. Retain pinned scene/utility identity and call the read-only path on repeated checks;
  retire the complete owned stage on failed mutation, callback drift or cancellation.

This rewrite closes native queue/frame/inspection prerequisites; it does not route cables, select
APCs/substations, bind operating consumers, scope power reconnect/solver work, certify retained
preview isolation or authorize publication. Those R09/R10/R12 exits remain open. No capability
points are awarded for this unintegrated prerequisite; the last fully verified estimate is 64/100.

### Rotation adapter rewrite prerequisite (2026-10-21, R06/R10/R12)

- [x] Reproduce and fix the authoring/engine rotation-sign mismatch before new infrastructure
  placement relies on existing poses. `KsProcgenInteractionFacingPlanner` declares clockwise turns
  (`0` south, `1` west), and `RotateFootprintOffset` rotates `(x,y)` to `(y,-x)` at turn one.
  Engine `Angle.RotateVec` is counter-clockwise, but before this rewrite assembly placement,
  owned scene placement and surface staging emitted `Angle.FromDegrees(quarterTurns * 90)` and
  verified that same raw angle. Matching an emitted angle to itself does not prove the authored pose is realized.
- [x] Introduce one explicit author-turn/engine-angle conversion and apply it consistently to
  materialization and live pose verification, including container-relative and supported members.
  Inspect prototype-specific interaction/sprite offsets rather than assuming every visual has the
  same baseline. Keep unsupported operational-facing claims guarded.
- [x] Audit map-import/native-angle capture and directional gas/fixture transforms at this boundary.
  Distinguish already-authored clockwise integers from engine angles; do not invert both or change
  pure clockwise geometry to conceal the adapter error. Version affected identities/artifacts.
- [x] Use an asymmetric native fixture/offset and an explicit face/approach case to prove actual
  engine geometry/orientation matches the planned clockwise pose. Include the right-wall computer
  facing left at author turn one. Do not rely only on raw-angle equality or symmetric square boxes.
  Prove the cases fail with the old conversion and rerun relevant runtime/source/sandbox gates.

- [x] Before the fix, five native cases produce three assertion failures and two unaffected passes,
  zero skips: both odd turns put the asymmetric fixture center on the wrong side, and the right-wall
  computer faces east instead of west. Evidence: `.ks_procgen_rotation_before_build.log`,
  `.ks_procgen_rotation_before.log`, `ks_rotation_before.trx`. This is an actual engine geometry/
  facing reproduction, not merely a raw-angle comparison.
- [x] Add `KsProcgenRotation.ToEngineAngle` (clockwise `0..3` to negative engine degrees) to assembly,
  complete owned-scene and surface emission/verification, and directional gas nomination. Keep pure
  clockwise offsets, native source-map hierarchy composition and raw extraction radians unchanged.
  Version support-pose identity to v2, owned interior identity to v3 and gas-context identity to v2;
  corrected meanings cannot reuse the older plan/context identities.
- [x] Expand native coverage to ten cases: all four asymmetric fixture turns, the right-wall
  computer/default renderer west direction, odd-turn surface placement, odd-turn inherited container
  rotation applied once, and a complete owned materialization before/after initialization. The two
  debug fixtures explicitly permit rotation; the hotplate fixture is droppable for surface coverage.
  Six unit cases cover native vector rotation and invalid author turns; directional gas tests now
  expect clockwise rather than the earlier reversed odd-turn domains. Preflight passes all 815
  procgen units and 635 selected integration/sandbox cases, zero failures/skips.
  Evidence: `.ks_procgen_rotation_{cases_build,cases,preflight_units,preflight_integration}.log`,
  `ks_rotation_{cases,preflight_units,preflight_integration}.trx`.
- [x] Prove the central sign, each placement/verification adapter and directional gas nomination
  with compile-valid mutations, then restore exact source bytes before positive gates.
- [x] Finish restored Release/fresh Debug, retained-case/sandbox audit, source/document formatting
  checks and completed-runner cleanup.
- [x] Five compile-valid mutations produce 22 assertion failures and 62 unaffected passes,
  zero skips. Reversing the bridge fails eight native and four unit cases; restoring the old
  sign in both emission and verification fails five assembly cases, two surface cases and the
  complete owned-scene case. Reversing only gas nomination fails both odd-turn domain cases.
  Each adapter mutation preserves matching raw-angle verification deliberately: independent
  native geometry/facing assertions must expose the wrong pose. Exact bytes are restored and
  compared in `finally`. Evidence: `.ks_procgen_rotation_gates_summary.log`,
  `.ks_procgen_rotation_mutation_<name>_build.log`,
  `.ks_procgen_rotation_mutation_<name>_{integration,units}.log`, and corresponding
  `ks_rotation_mutation_<name>_{integration,units}.trx`, for `bridge_sign`, `assembly_sign`,
  `surface_sign`, `owned_sign`, `gas_sign`. Restored-source positive gates are recorded below.
- [x] Restored normal-hook Release build passes (952 warnings, zero errors; 2m44s). Fresh Debug
  with `--no-incremental` also passes its normal-hook build (1,066 warnings, zero errors; 7m36s).
  Both configurations pass all 815 procgen units, 635 selected integrations/sandbox cases and
  23 retained native serialization contracts, zero failures/skips. Case identities retain all
  previous 809 units, 625 integrations and 23 native cases with multiplicity, plus every current
  815/635-case preflight. The actual `Content.IntegrationTests.Tests.Utility.SandboxTest.Test`
  definitions map to Passed in Release and Debug. Evidence:
  `.ks_procgen_rotation_{release,final_debug}_build.log`,
  `.ks_procgen_rotation_{release,final}_{units,integration,engine}.log`, corresponding
  `ks_rotation_{release,final}_{units,integration,engine}.trx`, and
  `.ks_procgen_rotation_final_audit.log`. Full-repository tests are not claimed.
- [x] All seventeen touched source/data/document files pass strict UTF-8/no-BOM, CRLF and
  trailing-whitespace checks. The specification's 48 fences and README's six fences are balanced;
  main and engine `git diff --check` pass. The completed runner is removed. Evidence:
  `.ks_procgen_rotation_final_style.log`. Existing module revisions and unrelated changes are
  preserved. No additional capability points are awarded for this correction; the 80% goal
  remains active at approximately 64% of the expanded specification.

Sprite review: the default four-direction renderer selects south at engine zero, east at positive
90 degrees and west at negative 90 degrees. Layer `DirOffset`, direction overrides and sprite
rotation policy can change the visual mapping. The native default-baseline case is covered; this
does not certify arbitrary custom sprite/interaction calibration or operational machine use.
Those claims remain guarded under R06/R13. Source-map `LocalRotationRadians`/`GridRotationRadians`
remain native radians (extraction schema 2); converting those again here would invert source data.

Review evidence: `Content.Shared/_KS14/Procedural/KsProcgenInteractionFacingPlanner.cs`,
`RobustToolbox/Robust.Shared.Maths/Angle.cs`,
`Content.Server/_KS14/Procedural/KsProcgenAssemblyPlacementSystem.cs`,
`Content.Server/_KS14/Procedural/KsProcgenOwnedStageSystem.cs`, and
`Content.Server/_KS14/Procedural/KsProcgenSurfaceStageSystem.cs`, and the client sprite render/layer
direction implementation. This default-baseline adapter prerequisite is verified; no additional
capability points are awarded for its correction. Continue the unified infrastructure plan under
R09/R12 while retaining R06/R13 guards for custom operational-facing and machine-use certification.

### Scoped native power-node rebuilding (2026-10-21, R10/R12, in progress)

- [x] Reuse the native `NodeGroupSystem` rebuild/flood/group creation algorithm through transient
  owned queues. Leave its ordinary entry points on the original queues outside the synchronous
  scope. Preserve unrelated reflood/remake/removal work instead of globally forcing an update.
- [x] Add `KsRebuildOwnedPowerNodes(gridUid, members, maximumWork, cancellationToken)` for an
  already inspected complete owned scene on an initialized paused unit grid. Accept the standard
  cable/device/terminal node classes for HV/MV/APC groups; reject unsupported subclasses, pending
  owned removals, cross-scope connections and shared foreign groups before mutation.
- [x] Capture native compatible edges before mutation and retain complete connected components.
  Check actual edges, component membership, frame and component/reference identity after native
  callbacks. Bound reported node/edge/queue work before writes; preserve cancellation and reject
  callback reentrancy or immediate group creation that could bypass the scoped transaction.
- [x] Exercise ten actual runtime cases: cardinal HV/MV/LV grouping, diagonal nonconnections,
  same-tile converter input/output cables, untouched foreign pending graph fields and later
  consumption by the normal updater, insufficient work, cancellation, cross-scope connections,
  nested ForceUpdate, changed device enablement and removed node declarations during callbacks.
  First five-case preflight passes; the tightened callback suite passes nine; the final ten-case
  preflight passes, all with zero failures/skips. Evidence:
  `.ks_procgen_nodescope_{initial,cases,callback,preflight}_build.log`,
  `.ks_procgen_nodescope_{first_cases,callback_cases,preflight}.log` and corresponding
  `ks_nodescope_{first_cases,callback_cases,preflight}.trx`.
- [x] Prove isolation, reentrancy, fresh-graph validation and budget assertions using compile-valid
  mutations; restore exact source bytes before Release/fresh Debug and all retained regression gates.
- [x] Four compile-valid mutations produce thirteen assertion failures and 27 unaffected passes,
  zero skips. Redirecting the scoped flood pass to the global queue fails all ten cases, including
  an actual foreign cached-field change. Bypassing nested-update protection, fresh native edge
  comparison, or work-limit rejection each fails the corresponding runtime case. Every mutation
  runs all ten cases; source bytes are restored and compared exactly in `finally`.
  Evidence: `.ks_procgen_nodescope_gates_summary.log`,
  `.ks_procgen_nodescope_mutation_<name>_build.log`, `.ks_procgen_nodescope_mutation_<name>.log`,
  and `ks_nodescope_mutation_<name>.trx`, for `foreign_queue`, `reentry`, `fresh_graph`, `budget`.
- [x] Restored Release passes its normal-hook build (403 warnings, zero errors; 47s), all 809
  procgen units, 614 selected integrations/sandbox cases and 23 retained native serialization
  contracts. Zero failures/skips. Evidence: `.ks_procgen_nodescope_release_build.log`,
  `.ks_procgen_nodescope_release_{units,integration,engine}.log`, and
  `ks_nodescope_release_{units,integration,engine}.trx`. Fresh Debug and retention audit follow.
- [x] Fresh restored Debug with `--no-incremental` passes its normal-hook build (1,066 warnings,
  zero errors; 6m02s). All 809 procgen units, 614 integrations/sandbox cases and 23 native contracts
  pass with zero failures/skips. The case identity audit retains all previous 809 units,
  604 integrations and 23 native cases with multiplicity, plus every current ten-case preflight.
  Actual `Content.IntegrationTests.Tests.Utility.SandboxTest.Test` definitions map to Passed in
  both configurations. Evidence: `.ks_procgen_nodescope_final_debug_build.log`,
  `.ks_procgen_nodescope_final_{units,integration,engine,audit}.log`, and
  `ks_nodescope_final_{units,integration,engine}.trx`. Full-repository tests are not claimed.
- [x] All five touched source/document files pass UTF-8/no-BOM, CRLF and trailing-whitespace
  checks. All 48 specification fences are balanced; main and engine `git diff --check` pass.
  The completed temporary runner is removed. Evidence: `.ks_procgen_nodescope_final_style.log`.
  Existing module revisions and unrelated working changes are preserved. No additional capability
  points are awarded for this unintegrated native prerequisite; the 80% goal remains active at 64%.
- [x] Extend coverage to preexisting foreign remake/removal queues and owned callback requeues;
  add read-only topology inspection. See the queue/frame/inspection rewrite ledger above.
- [x] Add explicit frame-mutation cases and recheck unit-grid/splitting properties and pinned map
  identity after callbacks. See the rewrite ledger above; activation approval remains separate.
- [ ] Integrate the native entry and read-only verifier with the primary infrastructure plan,
  shared complete-scene inspector and stage lifecycle. The owning caller must retire its complete
  scene after any post-mutation issue; repeated verification must not recreate networks.

This is a native topology prerequisite, not an electrical distribution workflow. It registers owned
power groups through the normal engine hooks but never advances the global power solver or certifies
provider assignment, delivered power, current load, battery supply, working light or publication.
`PowerNetSystem.InitPowerNet`/`InitApcNet` allocate registered networks and invalidate `GroupedNets`;
normal updates later reconnect queued networks and tick the solver. Native pause handling marks
loads, supplies and batteries separately. R10 must prove those states and scoped reconnect/solver
behavior before an operational retained preview; a scoped node rebuild alone proves none of them.
It does not roll back arbitrary callback side effects. Engine iterator/callback cost is not a complete
CPU/serialization ledger; `GraphWorkFullyMetered` remains false. Existing electricity guards remain
until the complete R09/R12 plan and owned realization exist. No extra capability points are assigned;
the last fully verified overall estimate remains 64/100 and the 80% goal stays active.

### Live gas preference rewrite (2026-10-21, R11)

- [x] Preserve hard required gas/stability conditions while reporting optional unsupported stability,
  leaking optional gas domains, unrepresentable optional mixtures and unmet optional initial values.
  Do not promote preferred constraints into hard failures or silently claim they were satisfied.
- [x] Carry binding and runtime omissions into immutable initial receipts. Distinguish required
  initial-state success, all requested initial values, and stable-at-rest evidence.
- [x] Preserve request-hard gas restrictions even when a preferred mixture is omitted. Required
  stability and hard observation bounds remain guarded until scoped simulation is implemented.
- [x] Exercise preferred/required counterparts, omitted writes, precision failures, stable preferences,
  hard bounds, repeated read-only verification and existing gas transaction failures. Prove critical
  cases by source mutation, then run restored Release/fresh Debug and retained regression gates.

Preflight passes all 588 selected procgen-namespace/sandbox cases, including nine new preference
cases and all 28 retained owned-atmosphere/gas-planning cases. The new cases compare preferred and
required stability, open footprints and numeric overflow; retain hard observation/pressure limits;
assert omitted gas remains unwritten; and check that later optional drift neither refills gas nor
rewrites historical receipt evidence. Required rejection paths assert no allocated entities or
committed receipts survive. The wider final gate must also retain the sixteen prototype-fixture
cases outside the procgen namespace (604 expected cases total).
Evidence: `.ks_procgen_gaspreferences_preflight_complete_build.log`,
`.ks_procgen_gaspreferences_preflight_complete.log`, `ks_gaspreferences_preflight_complete.trx`.
Completed mutation and restored-source Release/fresh Debug gates are recorded below. No capability
points are awarded for this prerequisite rewrite; the expanded-scope estimate remains 64/100.

- [x] Seven compile-valid source mutations produce eleven assertion failures and 52 unaffected
  passes, zero skips. The cases detect hardening preferred stability, bypassing required stability,
  bypassing a hard observation bound, writing into an open optional domain, bypassing optional
  numeric fallback, hardening optional initial-state drift, and falsely certifying all initial
  values. Every mutation runs all nine preference cases. Exact source bytes are restored and
  compared in `finally`. Evidence: `.ks_procgen_gaspreferences_mutations_summary.log`,
  `.ks_procgen_gaspreferences_mutation_<name>_{build,tests}.log`, and corresponding
  `ks_gaspreferences_mutation_<name>.trx`. Restored-source gates follow in this ledger.
- [x] Restored Release build passes with the normal localization hook (403 warnings, zero errors;
  54s). Release passes 809 procgen units, 38 gas/planning/sandbox integrations and 23 native flag/
  context serialization cases, with zero failures/skips. Fresh Debug with `--no-incremental` also
  passes (1,066 warnings, zero errors; 5m36s), normal localization hook enabled.
- [x] Final restored Debug gates pass all 809 units, 604 selected integrations and 23 native cases,
  zero failures/skips. The identity audit retains all previous 809 units, 595 integrations and
  23 native cases, plus every one of the current 588-case namespace/sandbox preflight selection.
  Actual `Content.IntegrationTests.Tests.Utility.SandboxTest.Test` definitions map to Passed in
  Release and Debug. Final integration filter: `FullyQualifiedName~KsProcgen|FullyQualifiedName~SandboxTest`;
  it includes the sixteen retained prototype-fixture cases outside the procgen namespace.
  Evidence: `.ks_procgen_gaspreferences_final_{release,debug,audit}.log`,
  `.ks_procgen_gaspreferences_{release,final}_{units,integration,engine}.log`, and corresponding
  `ks_gaspreferences_{release,final}_{units,integration,engine}.trx`. Full-repository tests are not claimed.
- [x] Correct stale example README gas guards and describe the supported planning/initialization
  entry, preference omissions, historical evidence and remaining operational limits.
- [x] All six touched source/document files pass strict UTF-8/no-BOM, CRLF and trailing-whitespace
  checks; the specification's 48 fences and README's six fences are balanced. Main and engine
  `git diff --check` pass. Both completed temporary runners are removed. Evidence:
  `.ks_procgen_gaspreferences_final_style.log`. Existing module revisions and unrelated changes
  are preserved. The 80% goal remains active at approximately 64%; this rewrite earns no extra
  operational workflow points. The scoped native node-processing prerequisite is recorded under
  R10 before electrical realization; global `ForceUpdate` cannot substitute for it.

### Flag serialization rewrite prerequisite (2026-10-20, R08/R11)

- [x] Fix the engine `FlagSerializer<TTag>` writer, which starts at bit index one and drops the
  least significant flag during captured-image roundtrips. Its highest-bit metadata is a bit count;
  inspect all 32 integer bits and use an exclusive loop bound to avoid wrapping shift 32 back onto
  bit zero for signed flags or silently discarding undefined high bits.
- [x] Reproduce the existing defect with native zero/low/sparse/high/sign/all flag roundtrips:
  fourteen cases run, three real failures and eleven unaffected passes, zero skips. Evidence:
  `.ks_procgen_ownedgas_flag_before_fix.log`, `ks_ownedgas_flag_before_fix.trx`.
- [x] Verify corrected images preserve full wall and window airtight defaults through actual spawning.
  The first live-gas case exposed a loaded wall mask of 15 becoming 14 in the captured instance;
  the domain's north edge really leaked. Hash roundtrip agreement alone did not detect this loss.
- [x] Advance the prototype-image schema/identity to seven and update its contract tests. Retain all prior
  snapshot, source-reference and allocation cases. Finish this rewrite before continuing gas filling.

Preflight after the rewrite passes all 23 selected native serialization cases, including sixteen
new flag-boundary/undefined-bit cases and the seven retained contextual-validator cases. All 809
procgen units and 588 selected integrations/sandbox cases pass. The 21-case runtime gas selection
passes real air/nitrogen/vacuum filling, independent mixtures, repeated initialization without refill,
barrier changes, cleanup, separate private grids, tiny-grid failure and existing gas planning guards.
Evidence: `.ks_procgen_ownedgas_flagfix_build.log`, `.ks_procgen_ownedgas_corrected_{cases,engine}.log`,
`.ks_procgen_ownedgas_preflight_{units,integration}.log`, and corresponding `ks_ownedgas_*.trx`.
Completed mutation and restored Release/fresh Debug gates are recorded in the owned-atmosphere
initialization ledger below.

### Owned atmosphere initialization (2026-10-20, R10/R11, in progress)

- [x] Add scoped native atmosphere preparation/observation and independent mixture writes for
  complete owned paused grids. Reuse engine tile-data, adjacency, volume, activation and device
  notifications. Keep map atmosphere and other grids untouched; no global rebuild or refill loop.
- [x] Reconcile actual initialized gas-transfer components and exterior leaks with the retained
  candidate before any fill. Resolve every required/preferred demand and preserve write masks.
- [x] Integrate filling once into the existing owned initialization transaction. Retain point-in-time
  initial-state evidence on the component-owned stage; repeat initialization verifies rather than
  refills. Any failure retires the complete owned stage through the shared lifecycle.
- [x] Retain explicit guards for hosted/preserved gas and unsupported stable-at-rest processing;
  do not promote initial-state evidence into simulation, electrical, isolation or publication proof.
- [x] Verify real tile mixtures, volumes, independence, missing/leaking barriers, stale callbacks,
  repeat initialization, write permissions, separate private grids, and failure cleanup. Prove critical
  assertions with compile-valid mutations, then run restored Release/fresh Debug/sandbox and all
  retained procgen/engine gates.

- [x] Runtime preflight passes 595 selected integration/sandbox cases with zero failures/skips.
  Seventeen new owned-atmosphere cases cover real air/nitrogen/vacuum, distinct per-tile mixtures,
  repeat verification without refill, different gases across closed internal doors, hard trace-gas
  restrictions, separate private maps, before/after barrier changes, gas-write masks, stability guards,
  tiny sealed 1x1/1x3 footprints, corrupting/reparenting device callbacks and cancellation after writes.
  Failed transactions retire the owned stage and commit no receipt. The receipt records simulation
  tick/time; `Verify` performs current gas inspection without revalidating or refilling mixtures.
  The no-gas path retains its original single scene check per lifecycle inspection.
  Evidence: `.ks_procgen_ownedgas_{callbacks,receipt,preflight}_build.log`,
  `.ks_procgen_ownedgas_callbacks_cases.log`, `.ks_procgen_ownedgas_preflight_complete.log`,
  `ks_ownedgas_callbacks_cases.trx`, `ks_ownedgas_preflight_complete.trx`.
- [x] Complete mutation/restored-source gates and workspace hygiene before assigning capability credit.
- [x] Nine compile-valid mutations produce 25 real assertion failures and 24 unaffected passes,
  zero skips: omitted bit zero (six failures), shift-32 wrapping (six), omitted gas write (three),
  shared tile-mixture references (three), skipped repeat-state verification (two), bypassed gas
  boundaries (two), skipped hard restrictions (one), bypassed callback frame checks (one), and
  early receipt commitment (one). Each run builds before testing and checks for an actual
  assertion failure. Source bytes are restored in `finally` and compared exactly after the run.
  Evidence: `.ks_procgen_ownedgas_mutations_summary.log`,
  `.ks_procgen_ownedgas_mutation_<name>_{build,tests}.log`, and
  `ks_ownedgas_mutation_<name>.trx`, with `flag_low`, `flag_wrap`, `fill_write`,
  `independent_mixtures`, `repeat_verification`, `gas_boundary`, `hard_restrictions`,
  `callback_frame`, `receipt_commit`. Restored-source positive gates are recorded below.
- [x] Restored-source Release build passed (465 warnings, zero errors; 2m03s), with the normal
  localization hook enabled. Release procgen units passed 809 cases; focused runtime gas/planning/
  sandbox integrations passed 29; native flag/context serialization cases passed 23. Zero failed/
  skipped. Evidence: `.ks_procgen_ownedgas_final_release.log`,
  `.ks_procgen_ownedgas_release_{units,integration,engine}.log`, and
  `ks_ownedgas_release_{units,integration,engine}.trx`. Fresh Debug and final retention audit are
  recorded below.
- [x] Fresh restored Debug build with `--no-incremental` passed (1,066 warnings, zero errors;
  7m08s), normal localization hook enabled. Final procgen units passed 809 cases; selected
  integrations/sandbox passed 595; native serialization cases passed 23. Zero failed/skipped.
  The definitions/case audit retains all previous 809 units, 578 integrations and seven engine
  contracts, every current 595-case preflight integration and all 23 native flag/context cases.
  The actual sandbox definition maps to Passed in both configurations. Evidence:
  `.ks_procgen_ownedgas_final_{debug,units,integration,engine,audit}.log`,
  `ks_ownedgas_final_{units,integration,engine}.trx`. Full-repository tests are not claimed.
- [x] Remove both completed runners and normalize the document. All 13 touched source/document
  files pass strict UTF-8/no-BOM, CRLF and trailing-whitespace checks; all 48 Markdown fences are
  balanced. Main and engine `git diff --check` pass. Evidence:
  `.ks_procgen_ownedgas_final_style.log`. Module revisions and unrelated working changes were
  preserved; no commits or remote updates were made.
- [x] Award two operational utility points for the verified closed private-grid InitialOnly
  workflow (utility subtotal 4/12, overall 64/100). No duplicate contract, layout or lifecycle
  credit is assigned for the supporting serializer, binding or ownership work. The 80% goal
  remains active and unachieved.
- [x] Complete live preferred-gas omission handling; see the 2026-10-21 rewrite ledger above.
- [ ] Continue required stable-at-rest simulation and hosted/preserved gas context. The operational
  adapter verifies initial values on private owned grids and can omit unsupported preferences;
  it does not complete R11, simulation isolation or publication.

### Candidate gas binding integration (2026-10-20, R09/R11)

- [x] Bind request/area/room/gas-domain declarations to each complete candidate, retaining
  individual verification requirements and reporting preferred omissions. Reject unknown targets,
  equal-priority disagreement, incompatible required states and writes outside owned gas masks.
- [x] Derive proposed gas-transfer components from retained prototype airtight defaults and exact
  nominated placements, including rotated directional blockers. Exclude unanchored supported
  members; never substitute walking connectivity for gas connectivity.
- [x] Run gas binding inside the primary configuration/subdivision retry transaction. Retain only
  accepted bindings in its identity and artifacts, and charge failed attempts against shared work.
- [x] Keep electrical and live atmosphere initialization unsupported until their adapters exist.
  Binding is planning intent; closure, initial fill, stable-at-rest and publication remain unverified.
- [x] Verify scope/precedence, whole-candidate rejection, retry, deterministic identity, masks,
  supported placements and budget boundaries with focused tests and meaningful source mutations.
- [x] Run Release, fresh Debug, actual sandbox and retained procgen/engine regression gates.

Verified scope and evidence:

- Gas bindings retain each demand's requirement, tolerance and observation interval separately.
  A required initial fill plus a compatible preferred stable-at-rest request does not become a
  required stability promise. Required nominal state wins over preferred state; incompatible
  preferred demands and preferred rooms without volume receive explicit omissions.
- Topology uses both endpoints of each cardinal edge and combines anchored blockers, including
  the engine's independent no-air flag contribution. A closed all-direction blocker may still
  hold gas when its no-air flag is false. Prototype facts, domains and binding identities are
  detached; work counters never contribute to semantic identity.
- The primary server planning entry captures initial prototype facts before search. Gas binding
  runs after the complete furnishing/light/access candidate, inside its existing retry transaction.
  Failed gas attempts retain diagnostics and spent work; they expose no reusable room artifacts.
- Hosted entrances, constants and preserved gas still require a real gas-transfer context adapter.
  This gas path rejects those cases instead of substituting an empty exterior. Electrical
  declarations/reservations remain unsupported. Gas-bearing scene allocation remains unsupported.
- Initial twenty binding cases passed. The next thirty-five-case pure run and ten-case integration/
  sandbox run passed after fixture corrections: retry fixtures now have door materials and the
  engine comparison has an explicit hull envelope. These runs precede the final no-air flag and
  hosted-context refinements. Evidence: `.ks_procgen_gasbindings_{tests,refined}_build.log`,
  `.ks_procgen_gasbindings_{preflight,refined}_{units,integration}.log` and corresponding TRXs.
  No full-system completion or weighted credit is claimed from these preliminary runs.
- [x] Preflight with the no-air contribution and hosted-context guards passed all 809 units and
  all 578 selected integration/sandbox cases, zero failed/skipped. Definitions/case audit retains
  every prior 773-unit and 567-integration case, and identifies the actual sandbox definition.
  The legacy no-gas stage assertion now explicitly names its seven stages and checks `Gas == null`;
  it no longer assumes every optional enum stage must run. Evidence:
  `.ks_procgen_gasbindings_{preflight,legacy}_build.log`,
  `.ks_procgen_gasbindings_preflight_all_units.log`, `.ks_procgen_gasbindings_legacy_integration.log`,
  `.ks_procgen_gasbindings_preflight_audit.log`, `ks_gasbindings_preflight_all_units.trx`,
  `ks_gasbindings_legacy_integration.trx`.
- [x] Seven compile-valid source mutations produce ten real assertion failures, four unaffected
  passing parameter cases and zero skips: required-state conflict (one failure), preserved/narrowed
  gas ownership (two), directional rotation (two), supported-member exclusion (two), independent
  no-air contribution (one), shared work charges (one), and primary binding bypass (one).
  Every mutation builds before testing; failure inspection requires an actual assertion. Exact
  original source bytes are restored in `finally` and compared after the run. Evidence:
  `.ks_procgen_gasbindings_mutations_summary.log`,
  `.ks_procgen_gasbindings_mutation_<name>_{build,tests}.log`, and
  `ks_gasbindings_mutation_<name>.trx`, for `required_states`, `gas_ownership`,
  `directional_rotation`, `supported_barriers`, `no_air_contribution`, `shared_work`,
  `primary_binding`. Restored-source positive gates follow; mutation binaries are not green evidence.
- [x] Restored-source Release build passed (952 warnings, zero errors; 1m50s). The normal
  localization hook was enabled. Release gas units passed 36 cases; gas/sandbox integrations
  passed 12 cases, zero failed/skipped. Evidence: `.ks_procgen_gasbindings_final_release.log`,
  `.ks_procgen_gasbindings_release_{units,integration}.log`, and corresponding
  `ks_gasbindings_release_{units,integration}.trx`.
- [x] Fresh restored Debug build with `--no-incremental` passed (1,066 warnings, zero errors;
  6m45s), normal localization hook enabled. Final units passed 809 cases; selected integrations/
  sandbox passed 578; native engine contracts passed seven. Zero failed/skipped. Definitions/case
  audit retains all prior 773 units, 567 integrations and seven engine contracts, plus all 809/578
  current preflight cases. The actual sandbox definition is Passed in both configurations.
  Evidence: `.ks_procgen_gasbindings_final_{debug,units,integration,engine,audit}.log`,
  `ks_gasbindings_final_{units,integration,engine}.trx`. Full-repository tests are not claimed.
- [x] Remove both completed temporary runners. All 18 touched source/document files pass strict
  UTF-8/no-BOM, CRLF and trailing-whitespace checks; all 48 Markdown fences are balanced. Main
  and engine `git diff --check` pass. Evidence: `.ks_procgen_gasbindings_final_style.log`. Module
  revisions and unrelated working changes were preserved.
- [ ] Continue R11 with actual initialized gas-edge inspection and scoped owned tile filling,
  closure and stable-at-rest evidence. Candidate binding alone earns no completed operational
  workflow credit; the fixed weighted estimate remains 62/100 and the active 80% goal remains open.

### Source default image work rewrite (2026-10-19, R08/R14, in progress)

Close the repeated/empty prototype-group reconstruction issue identified in the preceding batch
before continuing source-copy materialization. This is operation-local decode work, not entity state.

- [x] Cache immutable position/rotation/anchor/map-default values once per prototype across all
  maps in one extraction. Retain captured-image reconstruction and final definition drift checks;
  do not cache live components, mutable prototype images or data across separate requests.
- [x] Charge each group and each new default image to extraction graph work. Skip image creation
  for empty groups while still charging their group read. Add `MaximumDefaultImageCharacters`
  (default 4,194,304, absolute 16,777,216; zero allows prototype-free sources). Charge canonical
  body plus metadata input characters once per image, before any field reader runs. This is input
  size accounting, not a claim of fully metered serializer CPU. Add actual image/character diagnostics;
  work diagnostics do not alter semantic output identity.
- [x] Initial cache implementation Debug build passed (403 warnings, zero errors; 1m14s):
  `.ks_procgen_defaultcache_initial_build.log`. Empty-group skip/read-counter cases are added next.
- [x] Verify eight new cases using a real prototype field reader to count reconstruction work,
  including groups, files, separate extractions, exact/short character allowances, empty groups,
  and invalid limits. Retain all prior source-reference and transient-default cases. Initial six-case
  preflight retained all 559 prior integrations and passed 565 total. Strengthened seven-case
  counter run passed; final preflight with graph-before-image coverage passed all 567 integrations,
  zero failed/skipped, including the sandbox. Evidence:
  `.ks_procgen_defaultcache_cases_build.log` (403 warnings, zero errors),
  `.ks_procgen_defaultcache_preflight_tests.log`, `ks_defaultcache_preflight.trx`,
  `.ks_procgen_defaultcache_counters_{build,tests}.log`, `ks_defaultcache_counters.trx`,
  `.ks_procgen_defaultcache_imagework_{build,tests}.log`, `ks_defaultcache_imagework.trx`.
- [x] Detect seven compile-valid mutations with eight assertion failures, zero passing/skipped
  cases. Bypass cache reuse and reset it per map: each produces four actual field reads versus two.
  Process empty groups: zero allowance incorrectly rejects. Omit group work: 7 versus 27 graph
  units. Omit image work or move character checking after reconstruction: two reads versus zero
  (two character-bound cases). Omit cumulative character charges: the undersized shared allowance
  incorrectly succeeds. Exact original bytes are restored in `finally`; each failure is checked
  for an actual assertion. Evidence: `.ks_procgen_defaultcache_mutations_summary.log`,
  `.ks_procgen_defaultcache_mutation_<name>_{build,tests}.log`, and corresponding
  `ks_defaultcache_mutation_<name>.trx`, with `cache_reuse`, `per_map_cache`, `empty_groups`,
  `group_work`, `image_work`, `image_budget_order`, `cumulative_characters`.
- [x] Restored-source Release build passed (403 warnings, zero errors; 1m19s), and fresh Debug
  build with `--no-incremental` passed (1,066 warnings, zero errors; 8m50s). Normal localization
  hook enabled. Evidence: `.ks_procgen_defaultcache_final_{release,debug}.log`.
- [x] Final procedural units: 773 passed. Selected integrations: 567 passed. Engine contracts:
  seven passed. Zero failed/skipped cases. Definition/case audit retains all 773 prior units,
  all 559 prior integrations, all 567 current preflight integrations and all seven engine contracts;
  the actual `Content.IntegrationTests.Tests.Utility.SandboxTest` definition maps to Passed.
  Evidence: `.ks_procgen_defaultcache_final_{units,integration,engine,audit}.log`,
  `ks_defaultcache_final_{units,integration,engine}.trx`. Full-repository tests are not claimed.
- [x] Remove both temporary scripts after their processes finish. All five edited/new source and
  document files pass UTF-8/no-BOM, CRLF and trailing-whitespace checks. Main and engine
  `git diff --check` pass; all 48 Markdown fences are balanced. No module revisions or unrelated
  working changes were reset. Coverage credit remains 62/100.
- [ ] Continue full R14 request/serializer work metering and full source-copy/utility/editor exits;
  no weighted capability credit is assigned merely for this decoder-work optimization.

### Source component reference extraction rewrite (2026-10-19, R08/R09/R10, in progress)

Continue from transform-only source locators to schema-directed reference observations needed by
the room copier. Validation uses registered serializers without loading source entities or
inventing live IDs. This is not complete component/container/support closure: client-ignored
components and unsuccessful validations are listed as known gaps. A custom validator may also
accept data without exposing its internal references; an empty gap list cannot certify completeness.
`ComponentReferencesVerified` and `CopyReady` stay false.

- [x] Implement typed `EntityUid`, `NetEntity` and `MapId` observations on component overrides,
  including nested collections. Charge inspection and reference checks to extraction graph work.
  Record uninspected component types rather than infer references from numeric-looking strings.
- [x] Implement selected-room checks for observed references. Reject dangling/cut references;
  retain selected-grid context anchors and require map references to name its owning source map.
  Existing transform-branch checks remain independent.
- [x] Capture entity prototypes referenced by overrides into an extended immutable definition
  manifest before materialization. Pin original roots and new typed references together; preserve
  shared definition/reference limits. Add extraction schema 2 identity binding source and extended
  definition hashes, detached observed links and uninspected-type lists.
- [x] Replace live prototype reads for source transform defaults with captured prototype images.
  Keep boundary drift checks for both original and extended definitions.
- [x] Build and verify existing source-map extraction regressions plus actual nested/reference
  boundary, context-anchor, override-prototype, opacity, budget and frozen-default cases.
- [x] Fix engine contextual validator precedence: regular prototype-ID validators previously
  bypass context overrides, hiding override prototype dependencies. Preserve ordinary/null/tag
  behavior and isolate the dispatch implementation in a same-folder engine partial extension.
- [x] Distinguish explicit concrete roots from additional reference seeds in prototype manifests.
  Override filters can legally name abstract entities; capture their declarations without treating
  them as spawn requests. Include seed provenance in identity/replay, bound repeated references,
  and version affected manifests. Explicit abstract-root rejection remains intact. Prototype
  manifest schema is now 6 and source-map manifest schema is now 8; extraction schema is 2.
- [x] Initial implementation compiled (403 warnings, zero errors) and retained all 104 prior
  source-map cases. Added 23 cases; after correcting two test-code analyzer errors, 121/127 passed
  and six exposed the engine validator precedence bug. The engine fix compiled (1,070 warnings,
  zero errors) and all 127 source-map cases passed. Evidence:
  `.ks_procgen_maprefs_initial_{build,tests}.log`, `ks_maprefs_initial.trx`,
  `.ks_procgen_maprefs_cases_{build,tests}.log`, `.ks_procgen_maprefs_cases_rebuild.log`,
  `ks_maprefs_cases.trx`, `.ks_procgen_maprefs_context_{build,tests}.log`, `ks_maprefs_context.trx`.
  Five further seed/abstract/repeated-work cases bring this batch to 28 added cases.
- [x] Seed-aware Debug build passed (403 warnings, zero errors; 1m02s); full selected preflight
  passed all 555 integrations, zero failed/skipped, including the actual sandbox case. Evidence:
  `.ks_procgen_maprefs_seeds_build.log`, `.ks_procgen_maprefs_preflight_tests.log`,
  `ks_maprefs_preflight.trx`. Final restored-source proof remains pending below.
- [x] Detect ten compile-valid mutations, with 35 assertion failures and five passing control cases,
  zero skipped cases. Omit observations (nine failures); invert room membership (two failures/two
  passes); omit owning-map identity (one failure/three passes); read live transform defaults (one
  failure); omit inspection work (one failure); omit override prototype seeds (seven failures);
  omit seed identity (one failure); mislabel known opaque components (two failures); bypass engine
  context precedence (seven failures); omit returned output witnesses from identity (four failures).
  Each source is byte-restored in `finally`; assertion-bearing failures are checked independently
  of native test exit codes. Evidence: `.ks_procgen_maprefs_mutations_summary.log`,
  `.ks_procgen_maprefs_mutation_<name>_{build,tests}.log`, and corresponding
  `ks_maprefs_mutation_<name>.trx`, with `observations`, `boundary`, `map_owner`, `frozen_defaults`,
  `inspection_work`, `prototype_seeds`, `seed_identity`, `opacity`, `context_precedence`,
  `output_identity`. Debug binaries remain intentionally invalid after these checks until the
  restored-source final builds below; they are not used as passing evidence.
- [x] Pass restored-source Release, fresh Debug, focused engine contracts, procedural units and
  selected integrations including the actual sandbox check. Retain all 773 units, 527 prior
  integrations and 559 current preflight cases. Detailed evidence follows; temporary-script cleanup
  and final formatting are recorded separately below.
- [x] Restored-source Release integration-project build passed (1,072 warnings, zero errors;
  4m49s), normal localization hook enabled: `.ks_procgen_maprefs_final_release.log`.
- [x] Fresh restored-source Debug integration-project build passed with `--no-incremental`
  (1,066 warnings, zero errors; 8m11s), normal localization hook enabled:
  `.ks_procgen_maprefs_final_debug.log`.
- [x] Final engine contracts: seven passed. Final procedural units: 773 passed. Final selected
  integrations: 559 passed. Every run has zero failed/skipped cases. Identity-based result audit
  retains all 773 prior units, all 527 prior integrations, all 555 first-preflight cases and all
  559 witness-preflight cases; the actual
  `Content.IntegrationTests.Tests.Utility.SandboxTest` definition maps to Passed. Evidence:
  `.ks_procgen_maprefs_final_{engine,units,integration,audit}.log`,
  `ks_maprefs_final_{engine,units,integration}.trx`. Full repository tests are not claimed; the
  earlier wider-run/pool-sequence reproduction remains R17 work. Coverage credit remains 62/100.
- [x] Remove all three temporary mutation/gate scripts after their processes finish. Byte-level
  checks confirm 14 edited/new source/document files are UTF-8 without BOM, CRLF and free of trailing
  whitespace. Main and engine `git diff --check` pass; 48 Markdown fences are balanced. Evidence:
  `.ks_procgen_maprefs_final_format.log`. Ignored logs/TRXs retain test evidence; unrelated work and
  module revisions were not reset.
- [x] Bind the actual extracted poses, observed references and known inspection gaps into output
  identity, not just source/definition input hashes. Validators can consult additional runtime
  context; equal input hashes alone do not prove equal returned witnesses. Use bounded-memory
  hashing and test independent witness changes before a copier consumes this identity. Incremental
  SHA-256 frames source/definition hashes, masks/origins, entity poses/flags, override/removal names,
  observed links and known gaps. Numeric text is invariant; mask sorting remains within existing
  mask limits. Hashing does not build one large canonical output string. This identity is not apply
  permission; full reference/copy verification remains false.
- [x] Add four independent output-witness identity cases and seven focused engine contract cases
  covering regular/nullable override precedence, null guards, ordinary fallback, nested collection
  propagation and invalid type tags. Restored Debug build passed (462 warnings, zero errors;
  1m15s); all seven engine cases and all 559 selected integrations passed, zero failed/skipped.
  Evidence: `.ks_procgen_maprefs_witness_{build,engine,preflight}.log`,
  `ks_maprefs_witness_engine.trx`, `ks_maprefs_witness_preflight.trx`.
- [ ] Implement complete client/component/container/support dependency extraction and actual
  source-map materialization before claiming full source-copy readiness or contract credit.
- [x] **Default-image work rewrite before copying (R08/R14).** The dated rewrite above now caches
  detached defaults across the entire extraction, skips empty images, charges group/image work and
  bounds canonical input characters before reconstruction. Repeated-group/file, reader-order and
  transient-default cases pass. Full serializer/CPU metering remains unverified under R14.

### Typed entity dependency capture rewrite (2026-10-19, R08/R09/R10, in progress)

Extend prototype capture through serialized typed entity references before claiming complete
referenced-content contracts. This slice covers `EntProtoId`, `EntProtoId<TComponent>` and
`ProtoId<EntityPrototype>`, including nested serialized collections. Legacy string/custom
serializers, other prototype kinds, references in ignored client declarations, resource files and
runtime-generated children still require discovery and freezing. Capturing dependency bodies
does not yet make callbacks spawn their children from those bodies.

- [x] Implement a deterministic bounded work queue. Deduplicate definitions, charge repeated
  reference occurrences, terminate cycles, and share existing structural/definition allowances
  across roots and discovered dependencies. Return no partial manifest on failure.
- [x] Preserve original root provenance separately from dependency bodies. Replay from those
  roots under the original limits; do not incorrectly count every dependency as another root.
  Include roots in manifest identity and compare each placement's fresh dependency bodies.
  Prototype manifest schema is now 5; containing source-map manifest schema is now 7.
- [x] Retain bounded abstract entity dependencies as declaration-only records. They cannot be
  spawned or accepted as concrete root requests. Keep other typed prototype kinds in their
  ordinary scalar representation without looking them up as entities.
- [x] Initial Debug build passed (403 warnings, zero errors). Initial selected regression run:
  504 passed, nine failed, zero skipped, 513 total. Failures exposed old direct-only body-count
  and `Single()` assumptions. Update assertions to identify their intended root and count all
  dependency bodies in shared-budget tests; preserve the original test cases.
  Evidence: `.ks_procgen_entityrefs_initial_{build,tests}.log`, `ks_entityrefs_initial.trx`.
- [x] Verify 14 new real-engine cases for chains, all wrappers, nested duplicate references, cycles,
  exact occurrence budgets, atomic failures, child and abstract declaration drift, root identity,
  snapshot roundtrips and automatic owned-stage rejection/retirement after dependency drift.
  Updated Debug build passed (401 warnings, zero errors). Selected preflight passed all 527
  cases, zero failed/skipped, including the sandbox. Evidence:
  `.ks_procgen_entityrefs_tests_build.log`, `.ks_procgen_entityrefs_preflight_tests.log`,
  `ks_entityrefs_preflight.trx`. Final restored-source evidence remains pending below.
- [x] Prove twelve compile-valid mutations fail; restore and byte-verify source in `finally`.
  Omit ordinary observation (one failure), constrained observation (one failure/three passes),
  generic entity observation (one failure/three passes), duplicate occurrence charging (two
  failures/three passes), cycle deduplication (one bounded failure), definition allowance (one
  failure/two passes), original-root replay (two failures), dependency placement comparison
  (one failure), abstract declaration bytes (one failure), root identity hashing (one failure),
  materializable-kind rejection (one failure) and abstract declaration comparison (one failure).
  Independent result audit confirms all 14 failures across the twelve mutations contain actual
  assertion mismatches, not setup failures or skipped disposal warnings:
  `.ks_procgen_entityrefs_mutations_audit.log`. Evidence:
  `.ks_procgen_entityrefs_mutation_<name>_{build,tests}.log`, corresponding
  `ks_entityrefs_mutation_<name>.trx`, and `.ks_procgen_entityrefs_mutations_summary.log`, with
  names `untyped`, `component`, `typed`, `duplicates`, `cycles`, `definitions`, `replay`,
  `placement`, `abstract`, `root_hash`, `declaration_kind`, `abstract_compare`.
  Restored Debug build passed (403 warnings, zero errors); all 14 restored dependency cases
  passed: `.ks_procgen_entityrefs_restored_{build,tests}.log`, `ks_entityrefs_restored.trx`.
- [x] Restrict per-placement checks to concrete entity/tile kinds. A declaration-only record must
  not pass because recapturing an unsupported kind produces an empty set whose comparisons are
  vacuously true. Add a negative assertion and verify concrete prototype images can still retain
  abstract references. Boundary Debug build passed (401 warnings, zero errors); all 14 dependency
  cases passed: `.ks_procgen_entityrefs_boundary_{build,tests}.log`, `ks_entityrefs_boundary.trx`.
  Deliberate guard/comparison regression checks are recorded with the mutation evidence below.
- [x] Restored-source Release integration-project build passed (403 warnings, zero errors;
  1m33s), normal localization hook enabled: `.ks_procgen_entityrefs_final_release.log`.
- [x] Fresh restored-source Debug integration-project build passed with `--no-incremental`
  (1,066 warnings, zero errors; 6m19s), normal localization hook enabled:
  `.ks_procgen_entityrefs_final_debug.log`.
- [x] Final procedural units: 773 passed; selected integrations: 527 passed; zero failed/skipped
  in either run. Audit class/case identities: all 773 prior units, all 513 prior integrations and
  all 527 preflight integrations are retained. The actual
  `Content.IntegrationTests.Tests.Utility.SandboxTest` definition maps to Passed. Evidence:
  `.ks_procgen_entityrefs_final_{units,integration,audit}.log`,
  `ks_entityrefs_final_{units,integration}.trx`. This is selected coverage, not a whole-repository
  integration result; the earlier wider-run/pool sequence remains an R17 obligation.
- [x] Remove temporary mutation/gate scripts after their processes finish. Confirm the twelve
  edited/new source/document files are UTF-8 without BOM, use CRLF and have no trailing whitespace.
  Main and engine `git diff --check` pass; all 48 Markdown fences are balanced. Ignored logs/TRXs
  retain verification evidence. No module revisions or unrelated working changes were reset.
- [ ] Complete referenced-content/resource closure and captured child allocation before assigning
  remaining contract credit. Estimate remains 62/100; operational and publication gates stay open.

### Captured prototype instantiation rewrite (2026-10-18, R08/R09/R10, in progress)

Continue from body identity to actual owned-stage allocation from those bodies. A live prototype
being equal before/after a callback does not establish which defaults were read in between.
The main owned entry must use captured defaults for direct entities and relational assembly
members; complete source-map dependency closure, resource/code freezing and publication remain
separate open requirements. Legacy individual stage adapters still need consolidation under R10.

- [x] Add a bounded inverse of the canonical body format, without a YAML conversion. Preserve
  tags, key tags, null flags, ordered sequences and scalar text. Reject malformed lengths/counts,
  duplicate/unsorted mapping keys, trailing/alternate encodings and depth/node/character excess.
  Decoding alone performs no component construction or entity allocation. Expose the engine's
  mapping-key tag setter with an inline provenance marker for lossless reconstruction.
- [x] Extend entity manifests with complete resolved category metadata, `HideSpawnMenu`, and
  effective component network-sync defaults. Categories are sorted by ID, bounded to 64 per entity
  by default (absolute 256), and share the existing structural budget. Serialized entity fields
  alone omit this resolved state. Version prototype manifests to 4 and source-map manifests to 6.
- [x] Reconstruct complete private prototype images from immutable strings, with a private
  non-interning component-registry serializer and readers accepting only invalid world-reference
  defaults. Restore category/hide metadata and reject serialized/metadata roundtrip drift before
  accepting an image. Reconstruct each placement separately to isolate successive entities.
- [x] Add explicit engine allocation from a captured prototype, without prototype-ID lookup.
  Before allocation callbacks, independently reconstruct private loading and exposed metadata
  prototypes. Copy serialized node trees explicitly because generated copies may honor `CopyByRef`
  and ordinary mapping copies share key-tag dictionaries. Pass the private loading source explicitly
  to the existing component loader, including its network-sync decisions. Avoid the server live-
  prototype spawn path's tick clearing; client defaults have not been certified identical.
- [x] Route the main owned entry's direct entities and assembly members through captured allocation,
  retaining existing initialization, poses, insertion, allocation capture, cleanup and drift guards.
  Inspect instance metadata prototypes against the captured image when verifying retained scenes.
- [x] Bound image work before map allocation using `MaximumPrototypeImageCharacters`: default
  16,777,216, absolute 67,108,864, accounting for four body/metadata image reconstructions per
  explicit entity. Zero permits floor-only scenes. Actual callback/serializer CPU metering remains R14.
- [x] Compile the route and verify 193 initial decoder/manifest/source-map/owned-stage/physics
  cases. Initial compilation exposed the required validators inherited by reference readers;
  implement them explicitly rather than bypassing the interfaces. Debug rebuild passed (403
  warnings, zero errors): `.ks_procgen_snapshots_initial_rebuild.log`,
  `.ks_procgen_snapshots_initial_integration.log`, `ks_snapshots_initial_integration.trx`.
- [x] Add and pass 32 focused decoder/adversarial cases (17 codec, 15 captured prototype),
  including actual transient global edits during generated-wall and contained-assembly allocation,
  metadata mutation before another component loads, separate mutable lists and `CopyByRef` objects,
  retained metadata drift, category/network capture, malformed IDs/world references, category
  allowances and pre-allocation image budgets. A relation-free core is optimized into direct
  placement; use a real computer/disk container relation to prove the assembly branch independently.
  Decoder visit counts prove node/depth limits stop reconstruction itself. Evidence:
  `.ks_procgen_snapshots_counters_{build,integration}.log`, `ks_snapshots_counters_integration.trx`.
- [x] Selected full preflight passed all 513 cases (zero failed/skipped), including the sandbox;
  ordinary fixtures and prior owned stages remain compatible with the new image route.
  Evidence: `.ks_procgen_snapshots_preflight_{build,integration}.log`,
  `ks_snapshots_preflight_integration.trx`. This is pre-mutation evidence, not final restoration proof.
- [x] Prove ten compile-valid mutations fail, restoring and byte-verifying source in `finally`:
  omit key-tag restoration (one failure); omit depth guard (one failure, nine visits versus three);
  omit node guard (one failure, four visits versus three); use live direct spawning (one failure,
  transient default reaches instance); ignore assembly snapshot spawning (one failure, unsafe scene
  rejected); use exposed metadata as loading source (one failure, value 99 versus captured 17);
  use generated copies (one failure, `CopyByRef` payload shared with metadata); bypass instance
  image comparison (two failures); bypass image-work budget (two failures); omit metadata from
  identity (one category-hash failure, network case still passes). Evidence:
  `.ks_procgen_snapshots_mutation_<name>*`, `ks_snapshots_mutation_<name>.trx`, and
  `.ks_procgen_snapshots_mutations_summary.log`, using `decode_tags`, `decode_depth`,
  `decode_nodes`, `live_direct`, `live_assembly`, `loading_source`, `copybyref`, `instance_audit`,
  `image_budget`, `metadata_hash`.
- [x] Restored-source Release integration-project build passed (1,072 warnings, zero errors;
  4m03s): `.ks_procgen_snapshots_final_release.log`. Normal localization hook enabled.
- [x] Fresh restored-source Debug integration-project build passed with `--no-incremental`
  (1,066 warnings, zero errors; 5m40s): `.ks_procgen_snapshots_final_debug.log`.
- [x] Final focused procedural unit run: 773 passed, zero failed/skipped, all 773 prior cases
  retained. Final selected integration run: 513 passed, zero failed/skipped, all 481 prior cases
  and all 513 preflight cases retained by class/case name. The actual
  `Content.IntegrationTests.Tests.Utility.SandboxTest` definition maps to Passed. Evidence:
  `.ks_procgen_snapshots_final_{units,integration}.log`, `ks_snapshots_final_{units,integration}.trx`.
  Independent audit repeated on 2026-10-19 after the original process handle became unavailable.
  Full repository integration coverage is not claimed.
- [x] Remove temporary scripts and pass final main/engine whitespace and Markdown fence checks.
  Ignored logs/TRXs retain evidence. The existing capability credit remains 62/100: referenced
  content/project contracts and operational runtime/editor exits are still incomplete.
- [ ] Continue full component/container/support/reference and resource closure, frozen source-map
  copying, client agreement and runtime operation. `CopyReady`, engine access, utilities and
  publication claims remain gated; no weighted credit is assigned to this unverified work.

### Tile prototype/engine binding reconciliation (2026-10-18, R08/R10/R17, in progress)

The wider atlas verification run exposed `UnsupportedPrototypeManifestTileBinding` on a reused
test pair. Inspection found a supported engine/content condition that the old object-identity
guard rejected: `PrototypeManager.ReloadPrototypes` creates a new tile prototype, while
`Shared.Entry.EntryPoint.PrototypeReload` assigns it the registered runtime ID without replacing
the tile manager's original object. Equal content can therefore have distinct objects. The exact
test/pool sequence from the wider run is still a separate reproduction obligation below.

- [x] Replace the object-identity requirement between loaded and named tile definitions with
  complete ordinal canonical body equality. Preserve the actual registry invariant: loaded and
  named definitions must share a runtime tile ID, and numeric lookup must identify that same
  registered object. Matching IDs alone never authorize capture.
- [x] Serialize and charge both loaded and registered tile bodies, including when they are the
  same object, against one aggregate node/character/depth allowance. Retain the common detached
  body only after exact equality. Capture cost does not depend on whether reload replaced the
  prototype object. Declaration capture shares that same allowance.
- [x] Fresh whole-manifest and per-body verification translate a newly conflicting tile binding
  to `PrototypeManifestChanged`; direct capture retains `UnsupportedPrototypeManifestTileBinding`.
  Either case rejects before authorizing a write. Full body comparison still detects equal
  simultaneous edits to the two objects relative to the captured body.
- [x] Version prototype-body manifests to 3 and room-map manifests to 5 for the revised binding
  proof and structural accounting contract. Layout schema remains 2; project schema remains 1.
- [x] Add nine real-engine cases using supported prototype reload, covering stable identity and
  owned scene generation, loaded/registered conflicts, loaded/named/numeric ID disagreement,
  aggregate character/node exhaustion and simultaneous drift. No reflection or transient global
  prototype substitution is used to construct the reload condition.
- [x] Initial Debug integration-project build passed (401 warnings, zero errors; 42s). All 153
  selected prototype-manifest, source-map and owned-scene physics integration cases passed,
  including all nine new reload/binding cases. Evidence:
  `.ks_procgen_tilebindings_initial_build.log`, `.ks_procgen_tilebindings_initial_integration.log`,
  `ks_tilebindings_initial_integration.trx`.
- [x] Prove six compile-valid regressions fail: restore object identity (one failure after reload),
  omit full-body comparison (two failures), omit loaded runtime-ID agreement (one failure, two
  other ID cases still reject), omit numeric object binding (one failure, two others still reject),
  reset shared accounting before registered-body traversal (two failures), and omit drift-code
  translation (two failures). Every mutation restores exact source bytes in `finally` and verifies
  restoration. Evidence: `.ks_procgen_tilebindings_mutation_<name>*` logs and
  `ks_tilebindings_mutation_<name>.trx`, using `identity`, `bodies`, `runtime`, `numeric`,
  `aggregate`, `driftcode`; `.ks_procgen_tilebindings_mutations_summary.log` retains counts.
- [x] Restored-source Release integration-project build passed (401 warnings, zero errors;
  1m23s), with the normal localization hook enabled: `.ks_procgen_tilebindings_final_release.log`.
- [x] Fresh restored-source Debug integration-project build passed with `--no-incremental`
  (1,066 warnings, zero errors; 7m34s): `.ks_procgen_tilebindings_final_debug.log`.
- [x] All 773 focused procedural unit cases passed after that rebuild (zero failed/skipped):
  `.ks_procgen_tilebindings_final_units.log`, `ks_tilebindings_final_units.trx`.
- [x] All 481 selected integration cases passed (zero failed/skipped), including all nine new
  tile-binding cases and the actual `Content.IntegrationTests.Tests.Utility.SandboxTest`
  definition/result. Retained-case auditing by class/case name preserves all 472 prior integrations
  and all 773 prior units. Evidence: `.ks_procgen_tilebindings_final_integration.log`,
  `ks_tilebindings_final_integration.trx`. Filter:
  `FullyQualifiedName~KsProcgen|FullyQualifiedName~SandboxTest`. Full repository integration
  coverage and exact reproduction of the earlier wider-run sequence are not claimed.
- [x] Remove both temporary tile-binding mutation/final-gate scripts; retain ignored logs/TRXs.
  Final main/engine whitespace checks pass; all 48 Markdown fences balance. No weighted capability credit
  is claimed for this correctness rewrite.
- [ ] Continue complete referenced-content capture and frozen instantiation. No capability credit
  is assigned for this correctness rewrite; the overall estimate remains approximately 62%.

### Explicit source atlas extraction rewrite (2026-10-18, R08/R09, in progress)

Room asset geometry is room-local. A source binding may select `sourceGridUid` (a positive
map-local YAML UID) and `sourceOrigin` (the native tile corresponding to room-local `(0, 0)`).
The source coordinate is `roomCell + sourceOrigin`. This translation only selects source content;
family/member placement, ports and exterior facades continue to use room-local coordinates.
Without a grid selector, exactly one occupied unit grid must match. An explicit selector never
falls back to another grid, even if that other grid matches perfectly. Source origins may be
negative. Native tile coordinates after translation must remain within +/-1,000,000 per axis.

Example room declaration inside a layout document with `schemaVersion: 2`:

```yaml
rooms:
  AtlasBooth:
    sourceKey: station-room-atlas
    contentFingerprint: 'sha256:<exact map byte digest>'
    sourceGridUid: 17
    sourceOrigin: 40,-12
    cells:
    - 0,0
    - 1,0
    - 0,1
```

- [x] Add detached source selector/origin fields to room assets and source reference records.
  Include both fields in canonical layout/project identity without changing fitting geometry.
- [x] Introduce layout schema 2; migrate schema 1 with absent selectors/zero origin to canonical
  schema 2. Reject explicit extraction fields under schema 1, unsupported versions, nonpositive
  grid UIDs, selectors without source bindings and out-of-range translated masks.
- [x] Reconcile translated masks against frozen decoded source cells. Index only the explicitly
  selected grid when supplied; preserve cumulative probe budgets, occupancy checks, scale rejection,
  exact project binding, cancellation and atomic failure. Retain native cells and the source origin.
- [x] Extract hierarchy membership in the selected native grid and mask. Retain native grid poses
  and original parent-local poses; add `RoomPosition` by subtracting the source origin after composing
  the hierarchy. The future copier must rebase grid children only; changing every child-local pose
  would apply the offset repeatedly.
- [x] Version the static room-map manifest to 4 because canonical project/layout serialization
  and source-selection semantics have changed. Prototype-body manifest remains version 2.
- [x] Initial validation: Debug integration-project build passed (952 existing warnings, zero errors);
  37 layout unit cases and 123 selected source-map/project integration cases passed. The batch adds
  nine unit cases and nine integration cases. Logs: `.ks_procgen_atlas_initial_build.log`,
  `.ks_procgen_atlas_initial_units.log`, `.ks_procgen_atlas_initial_integration.log`.
- [x] Prove five compile-valid mutations fail, restoring exact source bytes in `finally` after
  every mutation: reverse grid selection (two failures, selected grid swapped); ignore mask origin
  (one failure, negative atlas no longer matches); ignore entity rebasing (one failure, native
  position returned as room position); omit selected grid ID from identity (one failure, origin
  case still passes); widen translated X bounds (one failure, translated-range case wrongly
  accepted; six other invalid-input cases still reject). Logs and TRXs use
  `.ks_procgen_atlas_mutation_<name>*` / `ks_atlas_mutation_<name>.trx` for `grid`, `origin`,
  `roompose`, `gridhash`, `bounds`. `.ks_procgen_atlas_mutations_summary.log` retains counts.
- [x] Restored-source Release integration-project build passed (952 warnings, zero errors;
  3m27s): `.ks_procgen_atlas_final_release.log`. Normal localization hook enabled.
- [x] Fresh restored-source Debug integration-project build passed with `--no-incremental`
  (1,066 warnings, zero errors; 6m34s): `.ks_procgen_atlas_final_debug.log`.
- [x] All 773 focused procedural unit cases passed after the restored-source rebuild (zero failed
  or skipped): `.ks_procgen_atlas_final_units.log`, `ks_atlas_final_units.trx`.
- [x] All 472 selected integration cases passed (zero failed or skipped), including the actual
  `Content.IntegrationTests.Tests.Utility.SandboxTest` definition/result. Audit by class name and
  case name retained all 463 prior integration cases; the unit audit retained all 764 prior unit
  cases. Evidence: `.ks_procgen_atlas_final_integration.log`, `ks_atlas_final_integration.trx`.
  Final filter: `FullyQualifiedName~KsProcgen|FullyQualifiedName~SandboxTest`. Main/engine whitespace
  checks pass. Full repository integration coverage is not claimed; the expanded-run failure below
  remains an explicit open obligation. The weighted estimate remains approximately 62%.
- [x] Remove the two temporary atlas mutation/final-gate scripts; retain ignored logs and TRXs.
  Final document fence count is 48 (balanced). No build or test process remains running.
- [ ] **R17 regression follow-up: prototype/tile registration after pooled content changes.** An
  accidental wider filter selected the complete construction/damage/device-linking classes rather
  than only their procgen fixture parameters. During that run,
  `KsProcgenOwnedScenePhysicsTests.ActualFixtureOverlapIsDetectedAcrossTheCompleteOwnedScene`
  rejected with `UnsupportedPrototypeManifestTileBinding` on a reused pair. Preserve the guard:
  a tile registered with the tile manager must agree in complete body and runtime binding with
  the prototype being captured (see the subsequent tile-binding reconciliation above). Reproduce the
  responsible test/pool/content-change sequence, determine why the loaded/registered objects differ,
  and repair conflicting registration or fixture isolation if necessary. Equivalent-body tile reload
  is now independently reproduced and supported by the subsequent tile-binding rewrite above;
  confirm whether it fully explains this wider run before closing this sequence-specific task.
  Evidence: `.ks_procgen_atlas_overbroad_integration.log`; that expanded run was
  deliberately aborted and does not supply a passing gate. The intended previous selection is
  `FullyQualifiedName~KsProcgen|FullyQualifiedName~SandboxTest`, which also selects the existing
  parameterized procgen prototype checks. The final gate audits retention against all 463 prior cases.
- [ ] Continue R08 component/container/support/reference closure and frozen instantiation, followed
  by R09 hybrid materialization. `ComponentReferencesVerified` and `CopyReady` remain false. This
  extraction contract grants no live write authority and adds no weighted capability credit.

### Resolved declaration and mapping-key identity rewrite (2026-10-17, R08/R15, in progress)

Direct prototype identity must include resolved declaration data that has no server component
instance, notably intentionally ignored client-only components. Capture the engine's inheritance-
composed `TryGetMapping` result alongside each existing live serialized body; retain detached
canonical strings rather than cached mutable `GetPrototypeData` values. Neither declaration data
nor live instance bodies alone substitute for the other.

- [x] Add `CanonicalDeclaration` to direct entity/tile body records and include it in fresh record
  comparison/content identity. Charge live and declaration nodes/characters to one shared capture
  allowance. Bound/validate both component registries and normalize only their entry ordering;
  preserve ordered data sequences and every value/container tag.
- [x] Close the canonical mapping-key tag omission using a supported public engine accessor.
  `MappingDataNode.GetKeyTag` reads both parser-retained key tags and representation-model key nodes.
  The small marked engine API change avoids private-field reflection and does not mutate mappings.
  Root component sorting uses a bounded temporary sequence and does not rewrite shared declarations.
- [x] Advance prototype-body manifest schema/hash to 2 and source-map manifest schema/hash to 3
  because their represented identity changes. Canonical project bodies also gain key-tag identity;
  the ordinary project/request authoring schema and geometric planning hashes do not change.
- [x] Verify inherited/ignored client declaration fields, detached strings, tile declarations,
  tagged keys from both engine representations, registry-order normalization, meaningful sequence
  order, invalid registries, aggregate budgets and main owned-stage drift rejection without spawning.
  All 125 focused prototype/map-source cases pass (113 retained plus 12 declaration cases).
  This includes retaining the registry's own sequence tag and charging live plus declaration text
  to one allowance. Evidence: `.ks_procgen_declarations_aggregate.log` and
  `ks_declarations_aggregate.trx`. Earlier missing-prototype error-code regressions were corrected
  by preserving prototype/tile existence checks before declaration lookup.
- [x] Prove nine critical paths under compile-valid mutations and restore exact source bytes in
  `finally`: replace declarations with live data (two failures), ignore declarations in comparison
  (one), omit declarations from hashes (one), ignore key tags (two), omit registry normalization
  (one), lose the registry sequence tag (one), sort meaningful sequences (one), reset live/declaration
  accounting (one), and remove the engine representation-model key-tag fallback (one failure/one
  passing case). The final mutation includes an engine rebuild; its intended assertion fails.
  Evidence: `.ks_procgen_declarations_mutation_` build/test logs and corresponding
  `ks_declarations_mutation_` TRXs for `declaration`, `comparison`, `hash`, `keytags`, `registry`,
  `sequencetag`, `ordered`, `aggregate`, `representation`. Compiler/setup failures are not evidence.
- [x] Run restored Release, fresh Debug, procedural units/integrations and the real sandbox check;
  retain all preceding 451 integration definitions. Verify the engine API change as part of that gate.
  Final verification completed 2026-10-18. Release: 1072 warnings, zero errors (including rebuilt
  engine dependencies); fresh `--no-incremental` Debug: 1066 warnings, zero errors. All 764 procedural
  units and 463 selected integrations pass with zero failures/skips. Class/name comparison retains
  all prior 451 definitions and confirms 12 new declaration cases (30 total prototype-manifest cases).
  The exact `Content.IntegrationTests.Tests.Utility.SandboxTest` definition maps to Passed.
  Evidence: `.ks_procgen_declarations_final_{release,debug,units,integration}.log` and
  `ks_declarations_final_{units,integration}.trx`. Full repository integration coverage was not run.
  Temporary mutation/build scripts are removed; ignored logs/TRXs retain the evidence. The marked
  engine accessor edit remains in the `RobustToolbox` worktree; it is the only engine-submodule edit
  in this batch. No commit, reset or submodule synchronization was performed. Current fixed-weight
  expanded coverage remains approximately 62%; broader content-closure/project delivery exits remain.
- [ ] Finish transitive prototype/resource/code closure, portable manifests and frozen supported
  instantiation. In particular, raw client-only declarations do not capture client defaults supplied
  by code, referenced textures/audio or dynamic component behavior. The current 80% milestone and
  remaining contract points stay open until those broader exits have acceptance evidence.

### Source entity hierarchy extraction (2026-10-17, R08/R09, in progress)

Continue the static map-copy adapter with a read-only entity extraction step. Use the frozen source
bytes and the exact captured project/native masks; never instantiate source entities to discover
their membership. Transform override fields merge with verified prototype defaults, while parent
IDs remain map-local references. No world UID or shared mutable DataNode is retained in the result.
Known source component names include types intentionally ignored on the server, such as `Sprite`.
Their override/removal locators and original bytes remain intact without component deserialization;
unknown names still fail. Server prototype bodies alone do not capture client-only defaults or
their transitive resources, which remain part of the open complete-content manifest work.
Use the engine's public `TryGetMapping`/`GetPrototypeData` APIs when extending that capture to raw
prototype declarations. Do not mistake `GetPrototypeData`'s frozen outer dictionary for immutable
content: its component values are mutable `MappingDataNode` objects. Snapshot the actual mappings,
account for inheritance and the separately stored key tags, and retain existing live-body drift
checks rather than replacing them with a raw declaration hash.

| Extraction limit | Default | Supported range / charged work |
| --- | ---: | --- |
| Mask cell probes | 524,288 | 0-2,097,152, using the shared native-mask index/query counter |
| Entity graph work | 1,048,576 | 0-4,194,304, shared across all files and room extractions |
| Parent depth | 128 | 1-512, counting the complete source parent chain, including map/grid roots |
| Extracted members | 4,096 | 0-65,536, cumulative across all requested room extractions |

Graph work charges source rows, override/removal entries, unresolved/resolved parent visits and
both room selection/output scans. Failed branches do not refund work. Primitive parsing, immutable
source DataNode reconstruction, sorting and direct-definition serialization still need complete R14
CPU/memory accounting; these counters do not certify the entire operation's cost.

- [x] Add bounded source-row/component traversal, finite local poses and explicit map-local parent
  parsing. Preserve local/relative-to-grid poses, anchoring/no-local-rotation facts, override/removal
  type names and qualified room/resource/grid/entity locators. Check direct definition identity
  before and after extraction; boundary checks still do not establish frozen instantiation.
- [x] Resolve parent chains iteratively with shared work/depth bounds, cycle/missing-parent checks,
  stable parent-before-child output and a fresh local coordinate frame at each grid. Reject nested
  grids and invalid/unsupported core removals. Source map world translations/rotations must not
  leak into grid-local extraction coordinates.
- [x] Select origins using floor-to-cell coordinates and require complete non-grid transform
  branches: masks cutting parent/child membership reject instead of dropping or orphaning members.
  Empty selections remain valid. Bound cumulative output across room extractions atomically.
- [x] Verify real and synthetic hierarchies, prototype defaults/overrides, radians/degrees, negative
  cell coordinates, source order, missing/cyclic parents, depth/work/output exhaustion, cancellation,
  cross-boundary branches and stale definitions/projects without allocating source entities.
  All 95 selected source cases pass: 68 retained map/decoder/mask cases and 27 new extraction cases.
  The real `/Maps/Test/empty.yml` yields its two spawn markers at native (-0.5,-0.5) without loading
  a map or running initialization. Parent-first descriptors remain stable when source groups reverse;
  source map/grid world offsets do not alter local poses. Empty complete branches may be excluded,
  while either direction of a cut branch rejects. Evidence: `.ks_procgen_entity_extract_real.log`
  and `ks_entity_extract_real.trx`. The earlier expanded run exposed server-ignored `Sprite` removal;
  registered-or-ignored type recognition fixes it without silently dropping the original data.
- [x] Prove ten critical guards under compile-valid mutations and restore exact source bytes in
  `finally`: remove parent rotation (four failures); leak grid source poses (two failures/two passing
  cases); allow split branches (two/eight); round negative coordinates (one failure); ignore depth
  limits (one/three); ignore cumulative output (one failure); skip definition drift rejection (one);
  bypass graph work (one/three); sort by ID instead of ancestry (four); accept nested grids (one).
  All failures are the intended fixture assertions, not compiler/setup failures. Evidence:
  `.ks_procgen_entity_extract_mutation_` build/test logs and corresponding
  `ks_entity_extract_mutation_` TRXs for `composition`, `gridframe`, `branches`, `floor`, `depth`,
  `output`, `definitions`, `work`, `ordering`, `nested`.
- [x] Run restored Release, fresh Debug, procedural units, retained/new integrations and actual
  sandbox validation. Retain all preceding 424 definitions and record verified final counts.
  Restored Release: 403 existing warnings, zero errors. Fresh `--no-incremental` Debug: 1066 existing
  warnings, zero errors. All 764 procedural units and 451 selected integrations pass with zero
  failures/skips. Class/name comparison retains all prior 424 definitions and confirms 27 new cases
  (95 total source cases). The exact `Content.IntegrationTests.Tests.Utility.SandboxTest` definition
  maps to Passed. Evidence: `.ks_procgen_entity_extract_final_{release,debug,units,integration}.log`
  and `ks_entity_extract_final_{units,integration}.trx`. Full repository integration coverage was
  not run. Temporary mutation/build scripts are removed; ignored logs/TRXs retain the evidence.
  The fixed-weight expanded estimate remains approximately 62%; transform ancestry alone does not
  complete source dependency closure, immutable instantiation or owned hybrid materialization.
- [ ] Resolve all component entity references, container/support ownership and activation/removal
  semantics before copy-ready output. A transform-descendant set is not complete component dependency
  closure. `ComponentReferencesVerified` and `CopyReady` remain false; all general override fields
  remain located in the immutable original map bytes for the later supported instantiation adapter.
- [ ] Connect full immutable source instantiation to the owned hybrid transaction, verify physical
  hull/ports/access/utility operation and preserve constants before publication. No new capability
  credit or 80% milestone completion comes from this preliminary extraction step alone.

### Static map chunk decoding and native-mask reconciliation (2026-10-17, R08/R09, in progress)

Continue the source adapter before enabling hybrid map writes. Version-6/7 tile-table aliases use
nonnegative 32-bit IDs; they must not be confused with resolved runtime tile indices. The inspector
now removes its previous unsigned-16-bit alias ceiling and rejects duplicate numeric aliases even
when their textual keys differ. Source manifest schema/identity advances to version 2.

| Decode/matching allowance | Default | Supported bound / accounting scope |
| --- | ---: | --- |
| Source chunks | 1,024 | 1-4,096, shared across all files/grids |
| Decoded tile cells, including empty cells | 262,144 | 1-1,048,576, shared across all files/grids |
| Decoded payload bytes | 2,097,152 | 1-8,388,608, checked before exact buffer allocation |
| Chunk side length | 16 | Explicit source sides 1-256; chunk and grid sides must agree |
| Native tile coordinates | Source values | Each axis stays within -1,000,000 through 1,000,000 |
| Mask index visits plus cell queries | 524,288 | 0-2,097,152, shared across all room extractions |

Captured grids retain tile size as source metadata; native room-mask matching accepts only unit
tiles. These counters bound this adapter's structural work, not all serializer/parser CPU or a
future live loader's component initialization and callback work.

- [x] Implement read-only decoding matching `MapChunkSerializer`: chunk versions 1-5 have 4-byte
  tiles, version 6 has 6-byte tiles, and version 7 has 7-byte tiles. Preserve flags, variants and
  rotation/mirroring, all cells including empty ones, grid-local coordinates and map-local grid UIDs.
- [x] Check exact payload lengths, declared grid/chunk dimensions, matching unique chunk indices,
  alias resolution and coordinate bounds. Share chunk/cell/decoded-byte allowances across every file
  in one capture. Allocate only the budgeted exact base64 destination. Do not invoke grid serializers
  or change `SuppressOnTileChanged`. Inherited prototype grids remain explicitly unsupported.
- [x] Validate the decoder against real and synthetic old/new chunk fixtures, malformed payloads,
  large aliases, duplicate numeric indices, cancellation and cumulative decode budgets.
  All 68 map-capture/decoder/mask cases pass in `.ks_procgen_chunks_contracts.log` and
  `ks_chunks_contracts.trx`: 38 retained cases and 30 new cases. The real map fixture also decodes
  its default-size chunks. Live probe-grid tile notifications work after successful and rejected
  capture; the test observes public behavior rather than reflecting into internal engine flags.
- [x] Reconcile native extraction masks against decoded unit-grid cells, reject missing/empty cells
  and ambiguous source grids, and retain exact source binding plus work counts. This verifies only
  occupied source tiles, not clean passage, hull/ports, entities, support or room operation.
  `TryReconcileRoomMasks` requires the captured canonical project and shares one index/probe budget
  across room extractions, returning no partial result on exhaustion. A four-cell index plus two
  one-cell room queries consumes exactly six probes; five rejects. Reconciliation can use frozen
  sources after the original resource becomes unavailable. Its result names the source manifest,
  resource path, map-local grid UID and immutable native extraction mask, not a live entity handle.
- [x] Prove ten critical decode/reconciliation checks with compile-valid mutations and restore exact
  source bytes in `finally`. Narrowing modern aliases: two failures/two passing cases; accepting
  short payloads: one failure/eleven passing cases; ignoring stored indices: one/eleven; ignoring
  grid/chunk size mismatch: one/eleven; resetting decode allowances per file: three failures; treating
  empty tiles as occupied: one/six; accepting ambiguous source grids: one/six; bypassing shared mask
  quotas: one failure; accepting stale project geometry: one/six; ignoring inherited grid data: one
  failure. Each failure is an actual fixture assertion, not a setup/compiler failure.
  Evidence: `.ks_procgen_chunks_mutation_` build/test logs and corresponding `ks_chunks_mutation_`
  TRXs for `alias`, `length`, `index`, `dimensions`, `cumulative`, `space`, `ambiguity`, `maskbudget`,
  `project` and `inherited`.
- [x] Run restored Release, fresh Debug, procedural units, retained/new integrations and actual
  sandbox. Retain all preceding 394 integration definitions and record the final counts.
  Restored Release: 403 existing warnings, zero errors. Fresh `--no-incremental` Debug: 1066 existing
  warnings, zero errors. All 764 procedural units and 424 selected integrations pass with zero
  failures/skips. Class/name comparison retains all prior 394 definitions and confirms 30 new cases
  (68 total map/decoder/mask cases). The exact `Content.IntegrationTests.Tests.Utility.SandboxTest`
  definition maps to Passed. Evidence: `.ks_procgen_chunks_final_{release,debug,units,integration}.log`
  and `ks_chunks_final_{units,integration}.trx`. Full repository integration coverage was not run.
  Temporary mutation/build scripts are removed; ignored logs/TRXs retain the evidence. The weighted
  expanded estimate remains approximately 62%; native occupied-tile matching does not complete the
  remaining content closure, physical-source or owned hybrid-copy exits.
- [ ] Complete source transforms/entity dependency extraction, physical hull/port reconciliation,
  transitive content identity and immutable instantiation before owned hybrid copying/publication.
  The active 80% milestone remains open; this unfinished source rewrite earns no new credit.
- [x] Version explicit source-grid selection and extraction-coordinate authoring for ambiguous
  atlases; retain automatic unique-grid matching as the default. Implemented by the 2026-10-18
  atlas extraction rewrite above; that batch's verification ledger governs its current evidence.

### Static room-map content capture rewrite (2026-10-17, R08/R15, in progress)

`KsProcgenRoomMapCaptureSystem` binds a captured planning project to actual content-resource map
bytes. Source keys remain symbolic: a trusted server dictionary resolves keys to canonical
`/Maps/...yml` or `.yaml` resource paths. Editor/project data never resolves directly to disk or
user-data paths. Existing opaque fingerprints remain valid editing declarations but actual source
capture requires `sha256:<64 hexadecimal digits>` matching the observed bytes. Every bound room,
including unselected alternatives, participates; geometry-only rooms do not become copied content.

The manifest retains the canonical complete project declaration body, exact detached map bytes,
ordinal room/key/path bindings, format/entity metadata and direct entity/tile prototype bodies from
both map tables and every area's theme candidates. It compares actual values and bytes on fresh
verification, not only declaration hashes. Snapshot streams are read-only and do not expose buffers;
copies cannot alter the retained source. Server bindings are detached before any resource callbacks.

This is read-only static capture, not map instantiation, mask/port reconciliation, transitive
component/resource/code closure, frozen instantiation, replay import or verification of room operation.
It never invokes the engine loader (which can prefer user data and allocate/initialize entities).
The map inspector currently supports formats 6 and 7, indexes registered override component types,
checks local UID uniqueness and direct references, and retains all other source bytes unchanged.
Serialized component fields are not deserialized or certified here. Duplicate YAML keys, anchors,
aliases, multiple documents, invalid UTF-8 and unsupported structures fail atomically.
Constant-region source content and live host context are not resolved by this room-source adapter;
the owned entry retains its `OwnedStageContextCaptureRequired` refusal for those contexts.

File count, per-file/total byte probes, cumulative YAML event nodes, nesting, scalar size, cumulative
declared entities and direct definition budgets are bounded. Non-seekable/short-read streams work;
all opened streams close on failure/cancellation. YAML event preflight precedes recursive DataNode
construction, and aliases cannot expand a small file into an unbounded graph. Parser tokenization
and trusted project/definition serialization still do not establish complete R14 CPU accounting.
Canonical project declarations have separate node/character/depth limits, independent of the
definition limits; `ProjectNodes`/`ProjectCharacters` report their consumed structural work.

- [x] Implement detached map bytes, server-only source bindings, expected/observed SHA-256 checks,
  bounded event preflight, static direct-reference inspection and full declaration/body comparison.
- [x] Initial 29 integration cases pass, including actual `/Maps/Test/empty.yml`, both supported
  formats, buffer isolation, read-time binding mutation, prototype drift, invalid sources, short
  non-seekable reads, cancellation and resource disposal. Evidence: `.ks_procgen_roommap_initial.log`
  and `ks_roommap_initial.trx`.
- [x] Complete multi-file cumulative budget, geometry-only project candidate, duplicate direct map
  reference occurrences and invalid UTF-8 cases. All 75 selected map/project/prototype cases pass:
  38 new map cases plus 37 retained project/prototype cases. Evidence:
  `.ks_procgen_roommap_occurrences.log` and `ks_roommap_occurrences.trx`.
- [x] Prove eight critical checks fail under compile-valid mutations and restore exact source bytes.
  Fingerprint bypass: one failure/seven passing cases; live binding reuse: one failure; byte guard
  bypass: two failures/five passing cases; depth bypass: one failure/six passing cases; premature
  reference deduplication: one failure; resetting YAML work between files: five failures, including
  real cumulative-budget acceptance and false reported totals; ignoring canonical project bodies:
  one failure; comparing only definition counts: one failure. Each source file is restored to its
  exact prior bytes in `finally`. Evidence: `.ks_procgen_roommap_mutation_` build/test logs and
  corresponding `ks_roommap_mutation_` TRXs for `fingerprint`, `bindings`, `bytes`, `depth`,
  `occurrences`, `cumulative`, `project` and `definitions`. Compile-invalid variants are not proofs.
- [x] Run restored Release, fresh Debug, all procedural units/integrations and the actual sandbox
  check. Retain the preceding 356 integration definitions and update the verified totals.
  Restored Release passes with 403 existing warnings and zero errors; fresh `--no-incremental`
  Debug passes with 1066 existing warnings and zero errors. All 764 procedural units and 394 selected
  integrations pass with zero failures/skips. Class/name comparison retains all preceding 356
  definitions and confirms 38 new map cases. The exact
  `Content.IntegrationTests.Tests.Utility.SandboxTest` definition maps to a Passed result.
  Evidence: `.ks_procgen_roommap_final_{release,debug,units,integration}.log` and
  `ks_roommap_final_{units,integration}.trx`. Full repository integration coverage was not run.
  Temporary verification scripts are removed; ignored logs/TRXs retain the evidence. Current
  fixed-weight expanded coverage remains approximately 62%; these source prerequisites do not
  complete the remaining content-closure/project-asset exits or the 80% milestone.
- [ ] Reconcile captured map tiles/entities, transforms, masks, facade edges, ports, preserved
  boundaries and all nested prototype/resource/code dependencies. Bind supported immutable source
  instantiation to the complete owned hybrid transaction; do not treat captured bytes or a matching
  expected checksum as a playable room or complete replay artifact. This subset earns no new credit.
  Before loading, separately bound decoded/decompressed chunk payloads, actual tile extents and
  component override work; byte/YAML limits alone do not bound a later engine loader's expansion.
  Engine audit: chunk payloads use 4-byte tiles (2-byte IDs) before chunk version 6, 6-byte tiles in version 6,
  and 7-byte tiles (including rotation/mirroring) from version 7; the default chunk side is 16.
  Implement exact version/size/length checks from `MapChunkSerializer`, not guessed payload layouts.
  The original inspector's unsigned-16-bit tile-table alias ceiling is removed by the schema-2 decoder
  above: version-6/7 chunk aliases are signed 32-bit nonnegative IDs; runtime tile IDs remain separate.
  The engine chunk reader also toggles `SharedMapSystem.SuppressOnTileChanged` during reads. Static
  inspection must remain free of that global side effect, and later loading must prove cleanup after
  failure before using the ordinary loader as a transaction boundary.

### Resolved prototype body capture rewrite (2026-10-17, R08/R10, in progress)

`KsProcgenPrototypeManifestSystem` captures immutable canonical serialized bodies for direct
candidate entity and tile references, including every captured material/light choice and every
member of every entity-core alternative. Entity component registries are normalized by component
name; mapping keys sort ordinally, while meaningful sequence order and node tags/nulls remain.
The manifest retains strings, runtime tile indices, a version and a SHA-256 identity, never mutable
prototype/component/data-node references. Fresh verification compares actual body values, not
only matching hashes or the theme's declaration ID.

The prototype serialization context includes all serialized defaults and accepts only invalid
entity/network-entity IDs and the nullspace map default. A live world reference fails capture;
the context does not query or serialize live entities. This initial rewrite required tile prototype
object identity with the engine registration. The subsequent 2026-10-18 binding rewrite replaces
that restriction with exact full-body equality and loaded/named/numeric runtime-ID agreement;
stale conflicting terrain data still rejects, while semantically equal reloads are supported.

The owned entry captures these bodies before planning and validates them after planning, around
allocation/terrain phases, immediately before tile writes, around explicit entity/member spawning,
and during retained/initialization inspection. The shared assembly placement helper accepts a
transient validator from the owning transaction; it retains no manifest, callback or per-map state.
`KsProcgenOwnedStageBudgets.PrototypeCapture` bounds definition count, reference occurrences,
components, canonical nodes/characters and depth. Duplicate references consume the occurrence
allowance. Serialization preprocessing remains trusted loaded-content work; these counters do not
prove complete serializer CPU/memory accounting or close R14.

This original direct-body subset is extended by the resolved-declaration and static room-map
capture batches above. Complete transitive prototype/resource/code identity, portable import/export
and frozen component instantiation remain required. In particular, boundary comparisons do not prove that a callback
which transiently changes and restores a prototype could not affect a spawned instance. Do not
promote live component, operation, closure, power, lighting or publication evidence on this basis.

- [x] Add bounded detached body capture, ordinal body comparison and direct candidate-pool
  enumeration. Include runtime tile identity and reject unsupported prototype/registry bindings.
- [x] Use a real prototype serialization context rather than dropping entity-reference fields or
  skipping defaults when ordinary serialization lacks their writers. Reject valid live references.
- [x] Connect capture and drift checks to the primary owned request-to-stage path, retained
  inspection and shared assembly member placement, preserving whole-stage rollback.
- [x] Initial seven capture cases and forty combined capture/stage/physics cases pass. Evidence:
  `.ks_procgen_manifest_context.log`, `.ks_procgen_manifest_owned.log` and corresponding TRXs.
- [x] Verify nested mutable component data, tile mutation, all unselected alternatives, component
  registry order, culture, duplicate-reference limits, live-reference rejection, preallocation
  capture limits and tile/startup/initialization callback rollback against the main entry.
  All 18 cases pass in `.ks_procgen_manifest_callbacks_final.log` and
  `ks_manifest_callbacks_final.trx`. The startup fixture targets `CoordinatesDisk`, whose
  `ShuttleDestinationCoordinatesComponent` actually receives the registered hook; a computer
  without that component cannot demonstrate this callback boundary. The callback also allocates
  an entity outside the stage map, and rejection removes that captured allocation.
- [x] Catch six compile-valid mutations: ignore canonical bodies (two failures), omit unselected
  alternatives (one), skip per-member validation (one), bypass retained drift rejection (one),
  accept a live entity reference (one failure/two passing live-reference cases), and ignore repeated
  reference occurrences (one). Each changed file is restored byte for byte in `finally`.
  Evidence: `.ks_procgen_manifest_mutation_{body,alternatives,member,retained,live,references}.log`
  and corresponding build logs/TRXs. The member test additionally checks that no insertion attempt
  occurs before rejection: the strengthened mutation rerun observes two item-slot attempts and fails
  that assertion when the member guard is disabled. Restored final gates follow below.
- [x] Prove critical checks fail on compile-valid source mutations, restore exact bytes, then run
  Release, fresh Debug, procedural units, retained/new integrations and actual sandbox validation.
  Restored final Release: 403 existing warnings, zero errors; fresh `--no-incremental` Debug:
  1066 existing warnings, zero errors. All 764 procedural units and 356 selected integrations pass
  with zero failures/skips. Match test definitions by class and name: all previous 338 are retained,
  with 18 new manifest cases. Match the exact `Content.IntegrationTests.Tests.Utility.SandboxTest`
  definition to its result: Passed. Evidence: `.ks_procgen_manifest_final_{release,debug,units,integration}.log`
  and `ks_manifest_final_{units,integration}.trx`. Full repository integration coverage was not run.
  Temporary mutation/build scripts are removed; ignored logs and TRXs retain the evidence.
- [ ] Complete transitive prototype/resource/code identity and actual static room-map capture.
  Bind those manifests to canonical project/replay schemas instead of interpreting a direct-body
  manifest or a declaration hash as complete content closure.
- [x] Close the canonical mapping-key tag omission through the resolved-declaration rewrite above.
  The writer now records value/container tags and explicit key tags using the public engine accessor.
  Equal-key/different-tag fixtures cover parser-backed and representation-model mappings; a getter
  mutation proves the representation-model fallback is required. No private storage reflection is
  used. Complete arbitrary content closure and serializer CPU accounting remain separate exits.
- [ ] Implement supported frozen component instantiation or equivalent instance evidence before
  relying on body checks as a write/operation receipt. Keep R17 activation/isolation guards and
  complete serializer/work accounting open. This preliminary rewrite earns no new milestone credit.
  Engine audit: `EntityManager.CreateEntity` obtains a live prototype before allocation, and
  `EntityPrototype.LoadEntity` subsequently iterates that prototype's live component registry.
  `ComponentRegistry` overrides supply component data but do not replace that iteration or its
  `NetSyncEnabled` reads. Passing overrides alone therefore does not establish frozen instantiation.
  A replacement must test transient edits/restoration during `EntityAdded` and component callbacks,
  unexpected component additions/removals, copied nested data and network-component membership.

### Complete owned scene physics observer rewrite (2026-10-17, R09/R17, in progress)

The complete owned stage must reach the existing fixture/cardinal-passage observer. A separate
validator with different contact, filtering, floor or vault semantics is not acceptable. This
integration earns no additional capability credit for already credited clearance mathematics;
actual actor/seat/door operation and activation isolation remain the operational exit.

`KsProcgenRoomFixtureSystem.Check(ownedStage)` and `CheckCardinalAccess(ownedStage, actorUid,
root, terminals, ...)` now use the same pair filtering, supported convex geometry, continuous
sweeps, conservative floor bounds and cardinal search as assembly previews. The actor stays
outside the preview and is never moved by observation. Wall/window underlay is excluded from clean
floor. Door thresholds retain their declared floor classification, but closed door fixtures can
still obstruct actor passage.
Scene views are transient data; persistent frozen masks and entity intent kinds belong to the
owned stage retained by its map component. No per-map observer registry is introduced.

The owned inspector now returns its bounded verified member list and scan count to the observer.
It still checks exact terrain, frame, lifecycle, prototypes, poses, container membership and all
captured live allocations. The observer consumes that same list to inspect fixtures, including
paused descendants and generated entities. Entity scan accounting covers that map walk; terrain,
content preprocessing and other existing work still need the full R14 accounting exit.

- [x] Retain immutable clean-floor and declared blocking masks in the frozen owned scene. Keep
  structural underlay outside the clean floor. Preserve blocking footprints of anything normally
  blocking or requiring vaulting, including supported room roots and their full footprints.
- [x] Classify frozen entity intents as walls, windows, doors, furnishings or lights. Only the
  wall/window lattice forms the permitted structural union; furniture/door collisions are not
  exempted by calling every spawned object structural.
- [x] Adapt the existing fixture and cardinal sweep implementation to both stage kinds through one
  transient scene view. Retain real supported/container members and inspect every owned map descendant.
- [x] Share the owned stage's complete membership inspection with the observer, charging one map
  walk against its scan allowance. Do not call full `Verify` and secretly repeat the same walk.
- [x] Migrate the older room-scene test callbacks to `KsProcgenGameTest.AssertOnServer` so deliberate
  failures cannot be replaced by dirty-disposal skips.
- [x] Verify actual overlap, thin obstacles between free centers, asymmetric collision filtering,
  clean passage/vault exclusions for floor and layered content, structural underlay, disconnected
  islands, complete scan bounds and no actor movement against the primary request-to-stage path.
- [x] Run compile-valid failure mutations and restore exact bytes; finish Release, fresh Debug,
  procedural units, existing/new integrations and actual sandbox validation.
- [x] Strengthen the thin-obstacle fixture: place a narrow polygon across every row of a generated
  room, prove both nominated centers are individually clear, then require their connecting sweep
  to fail. Add the replacement fixture before removing the original: the engine disables a body
  when its last fixture is removed. Check asymmetric collision filtering and recovery after removal.
- [x] Use explicit mandatory fixture counts with zero optional density. A 1x3 strip remains an
  unfurnished passage; do not alter that valid fallback to make a physics fixture appear there.
- [x] Make the flat vault case target the second cell of a two-cell footprint, not just its anchor.
  Disable layered member collisions in that case so the retained vault classification itself must
  block passage. The first mutation exposed the former test's accidental reliance on table collision;
  the strengthened tests both pass normally and both fail with empty retained blocking masks.
- [x] The 28 focused cases pass with zero skips: ten complete owned-scene physics cases, thirteen
  existing room-scene cases and five scoped membership cases. These include foreign descendants
  under noncollidable and contained members, clean recovery after deletion, exact entity scan
  allowance, expansion/floor-probe exhaustion, and unchanged external actor coordinates. Evidence:
  `.ks_procgen_owned_observer_footprints_build.log`, `.ks_procgen_owned_observer_footprints.log`
  and `ks_owned_observer_footprints.trx`; the strengthened vault cases are additionally recorded in
  `.ks_procgen_owned_observer_vault_strengthened{_build,}.log` and
  `ks_owned_observer_vault_strengthened.trx`.
- [x] Five compile-valid faults are caught as real test failures with zero skips: empty blocking
  masks (both strengthened cases fail), treating all structural underlay as clean floor (one),
  exempting furniture as part of the structural union (one), replacing continuous sweeps with
  center-only checks (both old and new cases fail), and ignoring the caller's scan allowance (one).
  Restore exact source bytes in `finally` after every mutation. Evidence:
  `.ks_procgen_owned_observer_mutation_{floor,union,sweep,scan}_build.log` and corresponding test
  logs/TRXs, plus `.ks_procgen_owned_observer_mutation_blocked_strengthened{_build,}.log` and
  `ks_owned_observer_mutation_blocked_strengthened.trx`. The earlier blocked mutation failed only
  the flat case and prompted the layered test correction; its weaker evidence is retained separately.
- [x] Final complete integration-project Release succeeds with 403 existing warnings and zero errors.
  Restored-source fresh Debug (`--no-incremental`) succeeds with 1,066 existing warnings and zero
  errors. All 764 procedural units and 338 selected integrations pass with zero failures/skips,
  including the actual `Content.IntegrationTests.Tests.Utility.SandboxTest.Test` definition/result.
  Retain every previous 328-case regression definition. Evidence:
  `.ks_procgen_owned_observer_final_release.log`, `.ks_procgen_owned_observer_final_debug.log`,
  `.ks_procgen_owned_observer_final_units.log`, `.ks_procgen_owned_observer_final_integration.log`
  and matching `ks_owned_observer_final_{units,integration}.trx`. The full repository suite was not
  run. No warning policy, suppressions, sandbox whitelist or prototype definitions were changed.
- [x] Keep the current expanded estimate at approximately 62% under the fixed weights. This closes
  the complete-scene observer integration gap; it does not count the baseline's static clearance
  mathematics twice or finish R17's actual operation, actor-profile binding, settling, isolation
  or publication exits. The user milestone remains at least 80%.
- [ ] Bind fresh observations to an exact stage/content revision and a nominated normal actor
  profile before any apply permission. Caller-supplied geometry and an old read-only report do not
  prove a normal player's current operation or authorize publication.
- [ ] Continue R08 referenced entity/component/library capture and R17 actual actor/seat/door use,
  fixture support settling and scoped activation. Static sweep observations must not promote
  `EngineAccessVerified`, lighting, gas or publication evidence.

### Owned scene materialization rewrite (2026-10-17, R09/R10, in progress)

Verified resumed batch: `KsProcgenOwnedStageSystem.TryGenerate` accepts a request, resolves a
detached standalone entrance contract, invokes the primary owned planner and compiles a separate
immutable server scene before allocating. One engine point is credited after the gates below;
the expanded estimate advances from 61% to approximately 62%. The 80% milestone is not reached.

The current server integration entry is:

```csharp
var result = ownedStages.TryGenerate(request, themeId,
    planningBudgets: planningBudgets,
    stageBudgets: new KsProcgenOwnedStageBudgets
    {
        MaximumTerrainCells = 4096,
        MaximumSpawnedEntities = 256,
        MaximumPlacementWork = 8192,
    },
    content: capturedThemeCatalog,
    cancellationToken: cancellationToken);
```

`PositionedPreview` retains one disposable paused map/grid. Call `TryInitialize(result.Stage)`
explicitly to run the shared paused initialization transaction; `Verify` checks the frozen scene,
and `Discard`/`DiscardAll` release ownership. No call here authorizes unpausing or publication.
The source request and public proposal are review data, not a replay/apply permission. Complete
entity/component content manifests and author-session ownership remain separate open requirements.
Disconnected islands intentionally share a nonsplitting preview grid; introducing a published
multi-grid policy is later work. Surface members are explicitly unanchored before surface drop
preflight; floor-root structures keep their declared engine anchoring.

Fresh-map exterior classification now occurs after partitioning, using the actual proposed hull
faces. `KsProcgenGroupRouteContext.ExteriorIsEmptySpace` is an explicit planning declaration;
the owned stage sets it only after excluding host entrances, constants and preserved cells.
It cannot be combined with caller-supplied face facts or existing host/domain context. Classification
charges its face work to the same owned graph allowance and retains resolved face facts in the
structure witness. Window percentage selection still does not establish live airtightness.

- [x] Verify complete scene compilation, including under-structure tiles and per-room layered
  dependency poses. Keep the public review proposal separate from the retained write scene.
- [x] Verify tiny shapes, independent islands, multiple supported rooms, initialized scene drift,
  callback cancellation/allocation cleanup, preallocation budgets and global retention.
- [x] Reuse one bounded map-tree membership inspection in the existing fixture observer and owned
  scene verification. Preserve existing map-scoped inspection regressions.
- [x] Prove new critical tests fail on compile-valid mutations; restore exact source bytes, then
  complete Release, fresh Debug and selected unit/integration/sandbox gates.
- [x] Confirm the new `RoundRestartCleanupEvent` subscription exists in generated
  `AutoSubscriptions`, using the fresh Debug compiler output. Evidence:
  `.ks_procgen_owned_stage_subscriptions.log`. This checks subscription wiring; operational reset
  isolation and author disconnect cleanup still require their own acceptance exits.
- [x] Run 23 new owned-stage cases plus the existing scene/ownership/assembly regressions. The
  successful large case initializes 24 independent rooms with 72 supported members, including
  24 real surface devices and 24 hidden container members. A lighting-enabled version deliberately
  exceeds the 256-allocation initializer ceiling and returns `OwnedStageInitializationSpawnBudget`,
  with the entire scene removed. The smaller lighting case initializes successfully. Do not raise
  or suppress the ceiling to make this test pass. The initial 50-case gate is recorded in
  `.ks_procgen_owned_stage_limits.log`; four additional context conflicts are in the final gate.
- [x] Verify strict 50% windows on a fresh 7x7 floor with an authored cardinal envelope: 14 windows
  and 14 remaining hull walls, actual entities over nonempty structural floor tiles. Verify actual
  door transforms follow cardinal approaches in both rectangular orientations. Hosted/preserved
  requests still fail before allocation; incomplete, null or conflicting exterior context is rejected.
- [x] Demonstrate seven real failures with zero skips across four compile-valid source mutations:
  disabling surface unanchoring (3), bypassing foreign map membership (1), disabling unsuccessful
  allocation cleanup (2), and disabling fresh exterior classification (1). Restore exact bytes in
  `finally` after every mutation. Evidence: `.ks_procgen_owned_stage_mutation_{surface,membership,
  cleanup,windows}_build.log`, corresponding test logs and `ks_owned_stage_mutation_*.trx`.
- [x] Fix inherited procgen test Release failures discovered by building the complete integration
  project: enable nullable checking in eleven unit and three integration files, acknowledge two
  asserted nonnull outputs, and pass validated prototype wrappers at eight literal-ID call sites.
  No warning policy, analyzer suppression or sandbox whitelist was changed. Earlier failed gate
  evidence is retained in `.ks_procgen_owned_stage_final_release.log`,
  `.ks_procgen_owned_stage_release_nullable.log` and
  `.ks_procgen_owned_stage_release_test_contracts.log`.
- [x] Final complete integration-project Release build passes (154 existing warnings in the
  incremental output, zero errors). Fresh Debug with `--no-incremental` passes (1,066 existing
  warnings, zero errors). All 764 procedural unit cases and 328 selected integration cases pass
  with zero failures/skips. Match the actual sandbox test definition/class to its passed result.
  The integration filter includes the previous prototype validation cases outside the procgen
  namespace; the narrower namespace-only gate passed 312 cases but omitted those sixteen cases.
  Final evidence: `.ks_procgen_owned_stage_final_release_fixed.log`,
  `.ks_procgen_owned_stage_final_debug.log`, `.ks_procgen_owned_stage_final_units.log`,
  `.ks_procgen_owned_stage_final_complete_integration.log` and matching
  `ks_owned_stage_final_{units,complete_integration}.trx`. The full repository test suite was not run.
- [ ] Continue R08 authoritative entity/component/library manifests and captured host/constant
  content; the frozen scene records exact write intents, not a complete referenced content manifest.
- [ ] Continue R17 complete-scene fixture/actor/seat/door operation and supported activation
  isolation. MapInit side effects, network visibility and global atmosphere/power registration need
  explicit supported capability guards or scoped evidence; deleting the preview map is not their
  rollback proof. Retain false operation, working-light, gas and publication evidence.
- [ ] Continue R11/R12 candidate-bound gas domains and utility reservations, actual gas filling,
  cable/converter routing and supply evidence. Existing unsupported utility-planning guards remain.
- [ ] Continue author-session/disconnect ownership, replay/apply and in-game authoring under R15/R16.

This is prerequisite work for materializing the existing complete owned planner output. The
standalone assembly preview remains supported; it must not grow a second incompatible placement
implementation alongside whole-map staging. This extraction alone earns no capability points.

- [x] Extract support member spawning/insertion/rotation and retained member inspection into
  `KsProcgenAssemblyPlacementSystem`; delegate the existing assembly stage to that implementation.
  Require the caller's exact active capture list, a captured live coordinate frame and bounded
  per-assembly membership before placing anything. The owning transaction retains all partial
  allocations and performs rollback; the helper retains no per-map/member dictionaries.
- [x] Preserve container live eligibility, managed-slot filter rechecks without reinsertion,
  dependency ordering, surface preflight/drop offsets/capacity, exact prototype/lifecycle/parent/
  transform checks, cancellation and cumulative allocation counting. Keep the existing standalone
  spatial-relation refusal until a complete independently audited owned pose is supplied.
- [x] Existing 43 selected assembly/room-scene/lifecycle/ownership cases pass with zero skips after
  extraction. Evidence: `.ks_procgen_placement_debug.log`, `.ks_procgen_placement_initial.log` and
  `ks_placement_initial.trx`.
- [x] Move all twelve older assembly server-assertion callbacks to `KsProcgenGameTest.AssertOnServer`
  so dirty-disposal warnings cannot replace actual failures with skipped tests.
- [x] Force all extracted placements to angle zero in compile-valid source. The nested-support/
  independent-root rotation case fails as a real failure, with zero skips. Restore exact helper
  bytes in `finally`. Evidence: `.ks_procgen_placement_mutation_build.log`,
  `.ks_procgen_placement_mutation.log` and `ks_placement_mutation.trx`.
- [x] Final Release succeeds with 250 existing warnings and zero errors; fresh Debug succeeds with
  1,107 existing warnings and zero errors. All 764 procgen units and 293 selected integration cases
  pass with zero skips. The actual sandbox definition/result match confirms `SandboxTest.Test`
  passed. No new warnings, suppressions or sandbox whitelist changes. Evidence:
  `.ks_procgen_placement_final_release.log`, `.ks_procgen_placement_final_debug.log`,
  `.ks_procgen_placement_final_units.log`, `.ks_procgen_placement_final_integration.log` and
  corresponding `ks_placement_final_*.trx`. The full repository suite was not run. Shared scene
  materialization is still open; this verified extraction alone leaves the estimate at 60%.
- [x] Compile a frozen complete scene before allocating: exact floor tiles, tiles beneath structures
  from captured room palettes, wall/window/door transforms, floor-only entities, lighting and each
  supported assembly's real dependency poses. Never flatten surface/container members into floor
  projections, or merge all rooms through the existing 64-member single-room composer limit.
- [x] Add an owned request-to-stage entry that invokes the primary owned planner and consumes the
  frozen scene through shared placement, ownership, capture and initialization. Public mutable plan
  objects and a matching semantic hash alone are not engine write authority. Retain proposal data
  for review separately from the scene used to verify actual engine objects.
- [x] Derive allocation/work limits from explicit generation budgets; reject an insufficient known
  minimum before map allocation, and share cumulative callback costs across terrain and every room.
  Keep absolute server bounds. Check both per-kind and global retention limits before allocation.
- [x] Reinspect complete terrain, structures, all assembly instances and independent scene membership
  after initialization. Reuse existing observers/adapters rather than adding a separate room validator.
  Destroy/cancel/reset/invalidate the complete stage as one owned unit, including escaped children.
- [x] Materialize authoritatively captured constant/host content or reject unsupported context
  before allocation. A blank preview outside generated cells is not proof of preserved content,
  host connectivity, closure or a complete usable room. Keep operational/lighting/gas/power/
  publication evidence false until the corresponding actual engine exits pass.
- [x] Resolve the primary owned planner's standalone-entry gap before treating it as the general
  generation entry: current entrance-aware candidates require declared clean outside approaches.
  Support explicit/deterministic roots with no authored entrances through the same owned network
  transaction, while retaining real entrance contracts when supplied. Do not fabricate a host
  corridor in a new map or silently substitute an unrelated planning path to satisfy that precondition.
  The subsequent rooted-planning batch implements the explicit empty-domain path; the eventual
  request-to-stage entry must select/retain that resolved input before engine allocation.

### Shared preview initialization lifecycle (2026-10-17, R10)

- [x] Add one `KsProcgenStageLifecycleSystem` for surface, container and assembly map initialization.
  The existing public adapters preserve their result types, reason codes and kind-specific content
  inspectors. The common transaction owns locking, capture, cancellation, pre/post inspection,
  cumulative budget checks, idempotence and failure cleanup.
- [x] Move initialization phase state to the common owned stage handle. Expose uninitialized,
  initializing, initialized and retired lifecycle states; the legacy per-kind phase properties are
  read-only projections of the common state. Retirement remains authoritative during callbacks,
  and an initializer cannot revive a session whose component was removed.
- [x] Clear capture before cleanup and make a single release attempt in the common failure `finally`,
  with operation-lock release protected by its own `finally`. The transaction retains no inspector
  delegates or persistent entity registry on the system. Already-retired handles still clean late
  callback allocations through the ownership system.
- [x] Existing 50 initialization/ownership integration cases pass with zero skips after delegation.
  Evidence: `.ks_procgen_lifecycle_debug.log`, `.ks_procgen_lifecycle_initial.log` and
  `ks_lifecycle_initial.trx`.
- [x] Seventeen focused lifecycle/ownership cases pass with zero skips, including six new scenarios
  across all three adapters: callback-time typed phases, nested initialization/discard rejection,
  idempotent repeated initialization and pre/during-callback cancellation with escaped allocations
  and recovery. Evidence: `.ks_procgen_lifecycle_cases_debug.log`, `.ks_procgen_lifecycle_cases.log`
  and `ks_lifecycle_cases.trx`.
- [x] Independently disable common failed-transaction release and post-initialization inspection in
  compile-valid source. The cancellation cases fail 3/3; ownership-loss cases fail 4/4; zero skips.
  Restore the lifecycle source byte-for-byte in `finally` after each check. Evidence:
  `.ks_procgen_lifecycle_mutation_release_build.log`, `.ks_procgen_lifecycle_mutation_release.log`,
  `.ks_procgen_lifecycle_mutation_postcheck_build.log`, `.ks_procgen_lifecycle_mutation_postcheck.log`
  and corresponding `ks_lifecycle_mutation_*.trx` files.
- [x] Final Release succeeds with 512 existing warnings and zero errors (includes rebuilt shared
  production outputs after the private prototype-name cache refresh). Fresh Debug succeeds with
  1,107 existing warnings and zero errors; all 764 procgen units and 293 selected integration cases
  pass with zero skips, including explicit before/during/after paused-map assertions. Match the
  actual sandbox test definition/result in the TRX: `SandboxTest.Test` passed. No new warnings,
  suppressions or sandbox whitelist changes. Evidence: `.ks_procgen_lifecycle_final_release.log`,
  `.ks_procgen_lifecycle_final_debug.log`, `.ks_procgen_lifecycle_final_units.log`,
  `.ks_procgen_lifecycle_final_integration.log` and `ks_lifecycle_final_*.trx`.
  The full repository suite was not run.
- [x] Name optional diagnostic arguments in the new tests per CONTRIBUTING.md. The test-only cleanup
  rebuild succeeds (174 existing warnings, zero errors), and all 20 new allocation/scoped-scene/
  lifecycle cases pass with zero skips. Production source is unchanged by that cleanup. Evidence:
  `.ks_procgen_lifecycle_namedargs_debug.log`, `.ks_procgen_lifecycle_namedargs.log` and
  `ks_lifecycle_namedargs.trx`. Encoding, CRLF, whitespace and balanced Markdown checks pass;
  the 29 relative links resolve and A01-A66 remain unique and contiguous.
- [ ] Continue R10 owned-plan materialization, request-derived allocation/work budgets, disconnect
  lifecycle and operational/PVS isolation. Sharing synchronous initialization is not safe rollback
  of arbitrary global initializer effects and grants no gas/power/room-use/publication certificate.
  This refactor alone earns no additional capability points; the assessed milestone remains 60%.

### Shared allocation capture and scoped room inspection (2026-10-17, R10/R17)

- [x] Centralize `EntityAdded` capture in `KsProcgenPreviewOperationSystem` for all four preview
  kinds. Only the exact active operation may change the capture sink. Capture records allocations
  before components/transforms exist; budget checks and cleanup remain outside that callback.
  Retained lists remain on the owned session handle; the system keeps only synchronous operation state.
- [x] Give tile staging cumulative allocation limits and cancellation before entry/during tile
  callbacks. Count deleted callback entities; reject live unexpected allocations even in nullspace.
  Failed/cancelled stages clean all recorded entities and clear capture before cleanup/lock release.
  Tile-only staging still rejects furniture, walls, doors and layered/operational content.
- [x] Share the existing 256 allocation and 16-per-kind retention constants. This consolidates
  limits, not request-derived budgeting; whole-plan limits remain an explicit R10 rewrite.
- [x] Inspect the engine-maintained transform tree rooted at the owned map instead of querying all
  world transforms. Include paused/noncollidable/contained descendants independently of the captured
  allocation list; reject foreign insertions and membership drift. Bound pending traversal memory
  as well as visited entities, and re-evaluate the complete tree on each inspection.
- [x] Twenty focused allocation/ownership integration cases pass with zero skips. Eighteen focused
  scoped/legacy room-scene cases pass with zero skips. New cases exercise off-map callback leaks,
  deleted-allocation budgets, exceptions, cancellation, recovery, unrelated-map scan independence
  and foreign entities under map/grid/member/container parents. Inspecting a paused map still
  does not authorize operational activation, global side-effect rollback or publication.
- [x] Remove duplicate prototype/component dependencies from the server planning entry and container
  stage in favor of the inherited `ProtoMan`/`Factory`, following CONTRIBUTING.md.
- [x] Independently disable capture, tile-failure cleanup and foreign membership rejection in
  compile-valid source mutations. The corresponding tests fail 2/1/4 cases respectively, with
  zero passes or skips. Restore each source byte-for-byte in `finally`. Evidence:
  `.ks_procgen_capture_mutation_capture_build.log`, `.ks_procgen_capture_mutation_capture.log`,
  `.ks_procgen_capture_mutation_tilecleanup_build.log`, `.ks_procgen_capture_mutation_tilecleanup.log`,
  `.ks_procgen_capture_mutation_sceneforward_build.log`, `.ks_procgen_capture_mutation_sceneforward.log`
  and their `ks_capture_mutation_*.trx` result files.
- [x] Final Release succeeds with 250 existing warnings and zero errors. The first Release attempt
  caught RA0030 on a generic transform lookup; use the engine-required non-generic overload, then
  restart final verification from Release. Fresh Debug succeeds with 1,107 existing warnings and
  zero errors; all 764 procgen units and 287 selected integration cases pass with zero skips.
  Match the TRX sandbox definition to its result to confirm actual `SandboxTest.Test` passed.
  No new warnings, suppressions or sandbox whitelist changes. Evidence:
  `.ks_procgen_capture_corrected_final_release.log`, `.ks_procgen_capture_final_debug.log`,
  `.ks_procgen_capture_final_units.log`, `.ks_procgen_capture_final_integration.log` and
  `ks_capture_final_*.trx`. The full repository suite was not run. UTF-8/no-BOM, source line endings,
  whitespace and balanced Markdown fence checks pass.
- [x] Award one engine point for shared capture, tile callback failure/cancellation behavior and
  independent complete map-scoped inspection. Engine advances from 10 to 11 of 20; total expanded
  coverage is approximately 60%. No extra utility, editor, layout or publication credit. The 80%
  milestone remains active.
- [x] Fast-forward clean private prototype/audio modules to their locally recorded `origin/main`
  (`41ad0da`/`2144064`); maps/replacement prototypes remain `1247ebc`/`10b5ed9`. All four have clean
  worktrees and zero ahead/behind counts. Refresh the prototype-name cache with a successful Debug
  build (993 existing warnings, zero errors), then rerun all 287 selected integration cases against
  that content: all pass, zero skips, actual sandbox result passed. Evidence:
  `.ks_procgen_capture_modules_debug.log`, `.ks_procgen_capture_modules_integration.log` and
  `ks_capture_modules_integration.trx`. No remote fetch is claimed. This is the earlier user-requested
  sync, not procgen capability credit. No commits or pushes were made.
- [ ] Continue shared typed activation/initialization phases, request-derived work/allocation limits,
  owned-plan materialization, operational isolation and actor/door/seat/device adapters. Neither
  scoped fixture observation nor allocation capture establishes a working room certificate.

### Component-backed preview ownership and retirement (2026-10-16, R10)

- [x] Replace retained per-map dictionaries in tile, surface, container and assembly stage systems
  with `KsProcgenStageSessionComponent` on each owned map. The component holds a typed common stage
  handle and its existing data graph; transient synchronous allocation-capture lists remain operation
  state, not retained per-map registries. Preserve existing public stage handles and phase APIs.
- [x] Share ownership checks, paused-inclusive queries, identity checks and allocation/map cleanup
  through one owner system. Snapshot/discard by kind; cap 16 retained maps per kind and 64 total.
  Add a matching tile retention cap and share the existing preview operation lock for tile staging.
- [x] Retire on map termination/component shutdown before cleanup, release escaped owned members,
  and protect replacement maps with numeric-map-ID plus entity-UID identity. Retired registered
  handles may finish cleaning allocations captured later in an interrupted initialization.
- [x] Reject ownership loss/queued deletion in post-initialization checks. Source members and new
  callback allocations cannot survive a failed stage merely because its ownership component vanished.
- [x] Release succeeds with 250 existing warnings and zero errors (server-only incremental rebuild;
  shared production source is unchanged). Fresh Debug succeeds with 1,107 existing warnings and
  zero errors. All 764 procgen units and 273 selected integration cases pass with zero skips.
  Inspect the TRX definition/result link to confirm actual client/shared `SandboxTest.Test` passed.
  No new warnings, suppressions or sandbox whitelist changes. Source restoration, UTF-8/no-BOM
  and whitespace checks pass. Evidence: `.ks_procgen_stageowner_final_release.log`,
  `.ks_procgen_stageowner_final_debug.log`, `.ks_procgen_stageowner_final_units.log`,
  `.ks_procgen_stageowner_final_integration.log` and corresponding `ks_stageowner_final_*.trx` files.
  The full repository suite was not run. The first full regression exposed an old container test
  counting an externally deleted stage in `DiscardAll`; assert automatic retirement and only one
  remaining live stage, then rebuild/rerun the entire final Debug gate successfully.
- [x] Eleven focused engine integration cases pass with zero skips: external map deletion and
  stale numeric ID reuse for all four kinds, escaped surface/container/assembly members, mixed
  paused registry/discard, component loss and queued deletion during initialization both with and
  without late nullspace allocations, tile cross-operation rejection and preallocation retention
  exhaustion. Update the old tile fixture to require automatic retirement and a subsequent no-op
  discard, rather than manual cleanup of a stale registry entry.
- [x] Deliberately omit escaped-entity cleanup and bypass tile staging's operation guards: four
  focused cases fail, seven pass, zero skip. Restore both sources byte-for-byte in `finally`.
  Evidence: `.ks_procgen_stageowner_mutation_build.log`, `.ks_procgen_stageowner_mutation.log`.
  The three escaped-member failures and the tile callback failure cover separate protected paths.
- [x] Award one engine lifecycle point for authoritative component ownership, shared retirement and
  invalidation cleanup across all four preview kinds. Engine advances from 9 to 10 of 20; total
  expanded-scope coverage is approximately 59%. This is not a completed R10 activation/isolation
  exit and earns no additional schema, interior, UI, gas/power or publication credit. The 80% goal
  remains active.
- [ ] Continue R10's unified phase/activation, whole-owned-plan materialization, derived allocation
  limits, disconnect lifecycle and atmos/power/container/PVS isolation. Shared allocation capture
  for every kind and tile callback cancellation are completed by the subsequent dated batch. This ownership
  rewrite does not establish safe global side-effect rollback, operation, visibility or publication.

### Owned layered room candidate integration (2026-10-16, R09)

- [x] Extend the existing owned configuration/layout retry path with support-aware whole-core
  furnishing when component inspection context is supplied. Retain the floor-only fast path.
  Use captured selected pack/core bodies, enforce required copies, and remove optional instances
  only as complete units after rejection. Do not refund probe or graph work after discarded trials.
- [x] Retain typed assembly, capability, support-pose, footprint and operating-access witnesses;
  keep floor-root entity projections distinct from surface/container poses. No projected floor
  entity authorizes flattening a supported member onto the ground during materialization.
- [x] Reserve all exposed occupied tiles, operating approaches and clean operating paths during
  lighting. Recompute supported access in the final independent audit using retained pose/capability
  data and the current structure mask, then audit whole-room/entrance connectivity after lighting.
- [x] Add a server planning entry supplying static component inspection context to the same owned
  planner. Preserve false engine/operational/publication evidence and reject layered data in legacy
  tile staging. Utilities remain explicitly unsupported; this batch does not integrate gas/wires.
- [x] Final Release succeeds with 512 existing warnings and zero errors; fresh Debug succeeds with
  1,107 existing warnings and zero errors. All 764 procgen units and 262 selected integration cases
  pass with zero skips. Inspect the TRX definition/result link to confirm actual client/shared
  `SandboxTest.Test` passed. No new warnings, suppressions or sandbox whitelist changes.
  Evidence: `.ks_procgen_layeredowned_final_release.log`, `.ks_procgen_layeredowned_final_debug.log`,
  `.ks_procgen_layeredowned_final_units.log`, `.ks_procgen_layeredowned_final_integration.log` and
  corresponding `ks_layeredowned_final_*.trx` files. Source restoration, UTF-8/no-BOM and whitespace
  checks pass. The full repository suite was not run. Correct the initial fixture's assumed member
  suffix to use retained source bindings; remove the new system's initial analyzer warnings before
  the final gates, without suppressions.
- [x] Focused acceptance: eight owned-path integration cases plus the actual sandbox check pass
  with zero skips. Retain a required microwave on a table, nested paper in its container, complete
  compact-variant replacement of an impossible base, required-versus-optional whole-instance
  rejection, missing/tampered projection audit failures, lighting/path protection, deterministic
  replay and zero entity allocations. Shared furnishing work includes final-audit landing probes;
  zero remaining landing work and a caller's room-size cap fail without partial artifacts.
- [x] Deliberate source regression: bypass the owned supported branch and weaken the projection
  audit; all eight focused tests fail with zero skips. Because the planner failure prevents the
  audit test reaching its valid source candidate, additionally run an isolated audit-only mutation:
  that one test reaches a valid layered plan and then fails on the accepted tampered projection.
  Restore every changed source byte-for-byte in `finally`. Evidence:
  `.ks_procgen_layeredowned_mutation_build.log`, `.ks_procgen_layeredowned_mutation.log`,
  `.ks_procgen_layeredowned_audit_mutation_build.log`, `.ks_procgen_layeredowned_audit_mutation.log`.
- [x] Award two interior planning points: one for integrated whole-core surface/container/variant
  candidates with required/optional rollback, one for protected lighting/operating paths and an
  independent post-lighting layered audit. Interiors advance from 13 to 15 of 18; overall coverage
  is approximately 58%. No engine, utility, editor or duplicate layout credit is awarded. The 80%
  goal remains active; R09 is not complete while the exits below remain open.
- [ ] Complete unified cable/gas reservations, read-only edge placement search, general density/
  optional selection goals, lifecycle/engine room materialization and actual operational validation.
- [ ] R15/R16: extend diagnostics, replay and overlays for layered poses/containers/support/source
  bindings. Legacy floor projections alone are not a complete layered furnishing export.

### Planning projects and portable geometry libraries (2026-10-16, R08/R15/R16 prerequisites)

- [x] Add versioned room geometry, local layout members, atomic family alternatives and room-library
  declarations. Compile singleton library rooms into automatically fitted families. Validate common
  reservations, exact member/generate partitions, references, local transforms, ports and facades.
  Preserve source keys/fingerprints as authored expectations, never evidence of inspected map contents.
- [x] Retain immutable source-room bindings for each compiled family/option/member. Global placement
  remains selected by the existing fitter. Reserve `Room/` for generated singleton family IDs.
- [x] Add a revisioned project binding areas to canonical requests, captured themes and libraries.
  Require matching nested document/revision metadata, stable area/request IDs and mode/library
  compatibility. Import referenced theme inheritance/fallback, pack, assembly and utility bodies into
  the portable project without registering prototypes. Preserve unreferenced draft assets as well.
- [x] Add captured content to the existing geometry pipeline, with matching-theme and unsupported
  utility guards before packing. Keep prior geometry/engine limitations visible; no new utility or
  map-copy implementation is implied by successful project capture or geometry-only planning.
- [x] Verify serialization, whole alternatives, shifted/rotated ports, instance bindings, mutation
  isolation, revisions, imported loaded bodies, project budgets and existing planner integration:
  28 focused units and 19 project integrations plus the actual sandbox check pass with zero skips.
  Preserve `DisconnectedPortNetwork` rejection for the unported fixture, then require success with
  a declared port and external root. No connectivity guard is relaxed to accept source metadata.
- [x] Final Release succeeds with 512 existing warnings and zero errors; fresh Debug succeeds with
  1,107 existing warnings and zero errors. All 764 focused procgen units and 254 selected integration
  cases pass with zero skips. Inspect the TRX definition/result link to confirm the actual client/
  shared `SandboxTest.Test` passed. No new warnings, suppressions or sandbox whitelist changes.
  Evidence: `.ks_procgen_project_final_release.log`, `.ks_procgen_project_final_debug.log`,
  `.ks_procgen_project_final_units.log`, `.ks_procgen_project_final_integration.log`, and corresponding
  `ks_project_final_*.trx` files. UTF-8/no-BOM, whitespace and source restoration checks pass.
  The full repository suite was not run. The initial unported positive fixture was corrected to
  preserve its disconnected-network rejection and separately test a declared-port successful case.
- [x] Deliberately alias room masks and omit imported tile definitions. Three of 28 unit cases and
  one of 19 project integration cases fail with zero skips; restore both sources byte-for-byte in
  `finally`. Evidence: `.ks_procgen_project_mutation_build.log`, `.ks_procgen_project_mutation_units.log`,
  `.ks_procgen_project_mutation_integration.log`. The successful fixture requires a real declared port
  in the preliminary network; the original unported fixture remains a checked rejection.
- [ ] Complete room-map content capture/inspection, full dependency manifests, `KeepVoid`, seams,
  named alignment anchors, required inter-member links, library eligibility/goals and integrated
  owned hybrid execution.
  Project values do not supply permissions, server storage, undo, UI, engine operation or publication.
  Expanded-scope coverage remains approximately 56%; this prerequisite does not earn the complete
  project/editor or manifest exit. The 80% goal stays active. See the project/library walkthrough
  in `Klovn/Docs/Examples/Procgen/README.md` for the implemented API and limits.

### Detached interior asset drafts (2026-10-16, R08/R16 prerequisites)

- [x] Share explicit authoring spec bases between loaded prototypes and editable themes, tile/wall/
  lighting/entity packs and relational assemblies. Preserve existing prototype kinds and YAML fields.
- [x] Add an isolated document/revision value with scoped asset names, cumulative element/text bounds,
  deep copies and declaration identity. Preserve ordered variants and authoring lists. Draft capture
  checks structure, not semantic validity: broken references can remain visible while editing.
- [x] Resolve draft assets before loaded assets without registering or mutating global prototypes.
  Validate effective themes and complete candidate pools at catalog capture; resolve relational
  assembly references through the same source. Retained catalogs no longer need the editable source.
- [x] Verify custom loaded/draft inheritance, source shadowing, variant binding, snapshot isolation,
  serialization, revision conflicts, invalid drafts and the primary owned planner with custom assets:
  44 focused unit cases and 9 integration cases plus actual sandbox pass with zero skips. The primary
  test requires nonempty furnishings and unchanged output after editing the original asset bodies.
- [x] Final Release succeeds with 512 existing warnings and zero errors; fresh Debug succeeds with
  1,107 existing warnings and zero errors. All 736 procgen units and 235 selected integration cases
  pass with zero skips. Inspect the TRX definition/result link to confirm the actual client/shared
  `SandboxTest.Test` passed. No new warnings, suppressions or sandbox whitelist changes.
  Evidence: `.ks_procgen_contentdraft_final_release.log`, `.ks_procgen_contentdraft_final_debug.log`,
  `.ks_procgen_contentdraft_final_units.log`, `.ks_procgen_contentdraft_final_integration.log`, and
  corresponding `ks_contentdraft_final_*.trx` files. UTF-8/no-BOM, scoped whitespace and source
  restoration checks pass. The full repository suite was not run.
- [x] Deliberately alias base assembly members and ignore draft tile assets. Five of 44 unit cases
  and three of nine loaded cases fail with zero skips; restore the source byte-for-byte in `finally`.
  Evidence: `.ks_procgen_contentdraft_mutation_build.log`, `.ks_procgen_contentdraft_mutation_units.log`,
  `.ks_procgen_contentdraft_mutation_integration.log`. The initial sandbox run rejected generic
  `new T()` reflection; explicit constructor factories replace it without whitelist changes.
- [ ] Complete the larger project contract: room maps/libraries/layout families, assignments and
  constants, dependency manifests, server-owned storage/undo/revision enforcement and actual UI.
  This draft value alone earns no completed project/editor capability point. Expanded-scope
  coverage remains approximately 56%; the 80% goal is active. The fixture walkthrough is in
  `Klovn/Docs/Examples/Procgen/README.md`, under detached interior asset drafts.

### Scoped utility intent and ownership contracts (2026-10-16, R08)

- [x] Introduce request schema 2 for optional `utilities`, keeping legacy schema 1 requests valid
  and their existing declaration hashes unchanged. Older consumers must reject the new request
  version; utility intent cannot hide in a schema 1 extension and disappear on transport.
- [x] Add bounded gas assignments with stable request, area, room and gas-domain scopes. Area scope
  defaults to the request ID. Pin loaded/draft named gas profiles into immutable, portable values;
  reject unequal declarations at the same declared scope/target and conflicting request defaults.
  Equivalent aliases can coexist. Room/domain overlap must still be resolved after candidate binding.
- [x] Add bounded consumer declarations selecting all powered entities, an entity prototype, or a
  stable planned member ID. Retain circuit, required/preferred, wired/operational, minimum matches,
  and nullable nominal/peak watts. Missing watts require inspection, never an assumed zero load.
  Validate explicit profile circuits and loaded prototype selectors without claiming live membership.
- [x] Capture separate gas, cable and device write masks. Null inherits request permission; empty
  forbids that layer's writes. Masks only narrow normalized writable cells and cannot include void,
  preserved/constant cells or exterior cells outside the owned envelope. Deduplicate and sort sets.
- [x] Capture request-hard allowed gas modes/species, pressure/temperature bounds, minimum observation
  interval and electrical operational/battery restrictions. Validate structure and finite bounds.
  These are retained constraints, not evidence that any assignment or existing gas satisfies them.
- [x] Extend request snapshot deep copies, declaration identity and engine serialization transport;
  preserve source profile identity without retaining mutable prototypes or draft aliases.
- [x] Reject nonempty scoped requirements in both composed planner entry points before packing or
  owned layout search. `UtilityPlanningUnsupported` supplies no partial plan or engine certificate.
- [x] Prove negative coverage by deliberately omitting scoped utility hashing and bypassing the
  planning guard: 17/54 unit cases and 4/7 loaded cases fail with zero skipped tests. Restore both
  source files byte-for-byte in `finally`. Evidence: `.ks_procgen_scoped_mutation_build.log`,
  `.ks_procgen_scoped_mutation_units.log`, `.ks_procgen_scoped_mutation_integration.log`.
- [x] Final Release succeeds with 512 existing warnings and zero errors; fresh Debug succeeds with
  1,107 existing warnings and zero errors. All 691 procgen units and 226 selected integration cases
  pass with zero skips. Inspect the TRX definition/result link to confirm the actual client/shared
  `SandboxTest.Test` passed. No new warnings, suppressions or sandbox whitelist changes.
  Evidence: `.ks_procgen_scoped_final_release.log`, `.ks_procgen_scoped_final_debug.log`,
  `.ks_procgen_scoped_final_units.log`, `.ks_procgen_scoped_final_integration.log` and corresponding
  `ks_scoped_final_*.trx` files. Scoped whitespace, UTF-8/no-BOM and restoration checks pass.
  The full repository suite was not run. The initial focused run exposed an abstract `Computer`
  selector in the new fixture; replace it with concrete `ComputerAlert` and rerun all final gates.
- [x] Award one contract point for the scoped utility/consumer declaration and transport exit:
  contracts 14/16, total expanded-scope coverage approximately 56%. No utility operation, editor,
  runtime or candidate-binding points are awarded. The 80% goal remains active.
- [ ] R09: bind selectors against candidate rooms/domains/members, reject unknown targets and unmet
  minimum matches, resolve room/domain equal-specificity overlap, apply every request-hard constraint
  to effective theme/area/room profiles, inspect unknown demand and enforce layer masks in one joint
  transaction. This batch does not place utilities, fill gas, route wires or verify operational load.

### Captured theme candidate catalogs (2026-10-16, R08/R15)

- [x] Add an immutable `KsProcgenThemeCatalog` for one resolved theme's complete current planning
  pools: weighted tile/wall/light alternatives, entity compatibility/activity tags, compiled cores,
  complete ordered assembly variants, inherited goals and utility defaults. Do not retain mutable
  prototype lists or recompile a selected core from changed source declarations.
- [x] Add canonical declaration identity, including unselected candidates, weight/minimum policies,
  geometry/rotation/approach declarations, relation constraints and utility profiles. Sort unordered
  pools/references/tags; preserve authored variant priority and canonicalize signed zero. This is a
  planning-declaration manifest, not an entity-component or writable-context fingerprint.
- [x] Select per-room content directly from retained catalogs without prototype lookups. Remove the
  earlier selected-content recapture path. Theme assignment shares one catalog across regions;
  the primary owned planner shares it across all configuration/layout retries. `PlanSnapshot`
  accepts an explicit catalog, and owned results expose `ContentDeclarationHash` as provenance.
  The existing no-catalog entry captures current content once. Utility/publication guards remain.
- [x] Verify unselected source changes invalidate new hashes without affecting earlier views;
  prove canonical pool/tag ordering, ordered variant identity, immutable nested lists and complete
  owned planning after source declarations are removed. Keep mismatched catalog IDs and unsupported
  utility defaults rejected before layout work. Run Release/Debug, actual sandbox and deliberate
  mutation checks with restored source and the full focused procgen corpus. Fifteen new loaded
  cases cover ten independent declaration changes, canonical pools/tags/signed zero, authored
  variant order, complete owned planning after source-theme references are cleared, mismatched
  theme rejection, invalid null input and retained gas/electric unsupported guards.
- [x] Deliberately omit candidate weights and primary tiles from declaration hashing, and ignore
  a supplied catalog in `PlanSnapshot`. Three loaded cases fail with actual assertions (12 passed,
  zero skipped). Restore both files byte-for-byte in `finally`; evidence is retained in
  `.ks_procgen_catalog_mutation.log` and its TRX/build log.
- [x] Rewrite the old missing-theme expectation for preflight rejection before any layout work.
  Preserve full two-alternative failure-journal coverage by using an impossible required window
  count, rather than using missing metadata to manufacture downstream search failures. Move
  `KsProcgenOwnedInteriorTests` onto failure-preserving teardown; earlier checks exposed another
  cleanup-masked skip. The remaining R18 legacy-fixture audit is still open.
- [x] Final Release passes (512 existing warnings, zero errors); final fresh Debug passes (1,107
  existing warnings, zero errors). All 637 focused procgen units and 219 selected integration cases
  pass with zero failures/skips, including actual `Content.IntegrationTests.Tests.Utility.SandboxTest.Test`.
  Logs: `.ks_procgen_catalog_final_release.log`, `.ks_procgen_catalog_final_debug.log`,
  `.ks_procgen_catalog_final_units.log`, `.ks_procgen_catalog_final_integration.log`.
  No new warnings, suppressions or sandbox whitelist changes; scoped encoding/whitespace checks
  pass. The full repository suite was not run. Expanded-scope credit remains 55%, with no full
  content-manifest point claimed while the wider R08/R15 asset exits below remain open.
- [ ] Finish wider R08/R15 assets: room-library/fallback eligibility, entity component/static
  capability fingerprints, full project draft contracts and scoped utility ownership/consumers.
  Capturing/hashing currently remains input bounded, not fully charged to R14's future request work
  ledger. Legacy diagnostic schema 1 is unchanged and is not a replay manifest or verification receipt.

### Retained selected planning content (2026-10-16, R08)

- [x] Capture selected floor palette, wall/hull/door/window family, light fixture, theme goals
  and compiled entity-pack cores into `KsProcgenSelectedContent` when selecting a theme. Keep the
  supporting-pack list immutable. Captured values are detached from editable loaded declarations.
- [x] Route material, owned hull/window, furnishing and lighting consumers through the retained
  selection. A changed selected ID cannot silently reuse a mismatched captured declaration.
  A manually constructed selection without captured content retains the legacy reference path.
  Explicit omission of optional packs/lights still works without reintroducing their contents.
- [x] Verify source edits leave earlier selected planning output unchanged, new selections observe
  edits, mismatched-ID refusal and immutable nested lists. Loaded cases compare actual material,
  furnishing and lighting proposals after editing their source declarations; new captures see the
  changes. Uncaptured furnishing packs reject before probing and return no partial entity proposal.
- [x] Rewrite explicit reference-edit fixtures to clear their old captured content, preserving
  original material, mandatory-support, choice and backtracking scenarios. Move
  `KsProcgenContentLoadTests`, `KsProcgenGroupMaterialTests` and `KsProcgenCleanFurnishingTests` to
  failure-preserving `KsProcgenGameTest`; the first regression run exposed a cleanup-masked skip.
  R18 still needs the remaining older fixtures audited.
- [x] Deliberately bypass retained furnishing cores and permit mismatched captured wall IDs. Both
  loaded cases fail with actual assertions (two failed, zero skipped); restore both source files
  byte-for-byte in `finally`. Evidence: `.ks_procgen_selected_content_mutation.log` and its TRX.
- [x] Final Release passes (512 existing warnings, zero errors), fresh Debug passes (1,107 existing
  warnings, zero errors), all 637 procgen units and 204 selected integration cases pass with zero
  failures/skips, including actual `Content.IntegrationTests.Tests.Utility.SandboxTest.Test`.
  Logs: `.ks_procgen_selected_content_final_release.log`, `.ks_procgen_selected_content_final_debug.log`,
  `.ks_procgen_selected_content_final_units.log`, `.ks_procgen_selected_content_final_integration.log`.
  No new warnings, suppressions or sandbox whitelist changes; scoped encoding/whitespace checks
  pass. The full repository suite was not run. Expanded-scope credit remains 55%; the 80% goal
  stays active. This prerequisite alone does not earn a full content-manifest point.
- [ ] Complete full candidate/library capture, source dependency manifests, static capability
  snapshots, draft material/entity assets and request-wide content binding. This batch pins only
  selected planning declarations; engine entity component bodies, live context and the standalone
  pack-only room-core preview still require their own captured resolver. It does not prove live
  fixture behavior, publication or full replay, and earns no additional point in isolation.

### Compiled assembly entry isolation (2026-10-16, R08)

This is a prerequisite for a retained content snapshot. Previously a resolved assembly's entry
objects still had mutable fields/lists, and several returned core/variant/member/relation lists
were writable arrays. These are concrete R08 isolation defects even though most compiler paths
already copied their source declarations.

- [x] Introduce separate `KsProcgenResolvedEntityEntry` records with detached read-only footprint
  and rotation collections. Keep editable `KsProcgenEntityEntry` data definitions for authoring.
  Constructing a resolved member copies its supplied declaration instead of retaining it.
- [x] Freeze compiler-returned pack/core, variant, member and relation collections, including
  singleton cores. Room assembly composition preserves immutable entry collections while
  namespacing selected members; it does not reintroduce mutable lists.
- [x] Adapt entry-body validation and furnishing consumers to the compiled type. Rewrite fixture
  setup to construct changed assembly/entry values instead of mutating earlier compiled objects;
  retain existing unsupported-seat, facing, pose, overlap and staging scenarios.
- [x] Verify all existing procgen cases plus explicit source-alias and collection-write rejection
  cases. Two new unit cases test member-construction detachment and immutable variant/member/
  relation lists. Two loaded cases cover actual assembly packs and plain-list packs: later valid
  edits are visible to a new compilation, while the earlier compiled view stays unchanged and
  nested collection edits are refused. No engine entities are spawned by compilation.
- [x] Deliberately retain source footprint/rotation lists and return a mutable member array. The
  focused mutation run produces two failed unit assertions (43 passed, zero skipped); exact
  compiler bytes are restored in `finally`. Evidence: `.ks_procgen_frozen_entries_mutation.log`.
- [x] Final Release passes (265 existing warnings; initial Release 512), fresh Debug passes
  (1,107 existing warnings), both with zero errors. All 637 focused procgen units and all 202
  selected integration cases pass with zero failures/skips, including actual
  `Content.IntegrationTests.Tests.Utility.SandboxTest.Test`. Logs:
  `.ks_procgen_frozen_entries_final_release.log`, `.ks_procgen_frozen_entries_final_debug.log`,
  `.ks_procgen_frozen_entries_final_units.log`, `.ks_procgen_frozen_entries_final_integration.log`.
  No new warnings, suppressions or sandbox whitelist changes. Scoped encoding/whitespace checks
  pass; all four KS modules are clean and 0/0 against locally recorded `origin/main` (no fetch).
  The full repository suite was not run. The expanded-scope estimate remains 55%; the 80% goal
  remains active, with no extra point awarded for this prerequisite in isolation.
- [ ] Finish the wider R08 content catalog/manifest: material and light pack bodies, eligibility
  tags, room libraries and entity prototype capabilities still need a captured resolver. This
  isolated entry rewrite does not close that full exit or earn another percentage point alone.

### Canonical request snapshots and version rejection (2026-10-16, R08/R15)

The active expanded-scope 80% milestone uses the fixed capability index at the start of section 17.
This batch finishes the request-input snapshot subset; it does not close R08/R15 or establish a
complete content/context replay. References to actual theme/room/entity prototype bodies and
inspected host facts still need their own detached content manifest and context certificates.

- [x] Add `KsProcgenRequest.schemaVersion`, defaulting to 1 for legacy inputs. Geometry normalization
  and the composed geometry entry reject unsupported versions explicitly before packing; the
  owned planner inherits the same normalizer gate. No existing request silently enables utilities.
- [x] Add `KsProcgenRequestSnapshot` with canonical versioned declaration identity, exact normalized
  masks, detached constant transforms/ports, roots, entrance alternatives and group roots, size
  bands, window goals, fallback permissions, limits, seed and utility declarations. Expand implicit
  entrance defaults; sort sets with ordinal identifiers and tile order. Equivalent rectangle/cell
  descriptions and irrelevant input ordering retain the same identity. Required constants do not
  become duplicate anonymous preserved reservations when canonical data is normalized again.
- [x] Resolve named utilities against loaded prototypes or a supplied immutable draft catalog,
  then retain complete inline copies and their selected reference provenance. Later profile edits
  cannot alter earlier captured utility declarations. Headless capture accepts inline/legacy input
  and rejects unresolved names; a portable captured request can be recaptured without the catalog.
  Gas/entity prototype dependencies still require validation in the receiving loaded content.
- [x] Return independent editable `CopyRequest()` data for engine serialization. Earlier snapshots
  do not expose mutable lists or nested authoring objects. Canonical identity is not a permission
  token, cryptographic signature, loaded-content fingerprint or verified generation result.
- [x] Add the primary owned planner's `PlanSnapshot` entry. It consumes a fresh copy of retained
  input while callers supply theme/content, budgets and inspected context separately. Existing
  gas/power and publication guards remain; passing an input snapshot cannot certify those gates.
- [x] Deliberately break returned-copy isolation, omit both limit fields from identity and disable
  the normalizer's version guard. The 39 new unit cases then report four actual failed assertions
  (35 passed, zero skipped); restore both source files byte-for-byte in `finally`. Evidence:
  `.ks_procgen_snapshot_mutation_units.log` and the corresponding mutation TRX/build log.
- [x] Finish final Release/fresh Debug and full focused procgen regressions, including the new
  owned-planner snapshot case and actual client sandbox. Final evidence:
  `.ks_procgen_snapshot_final_release.log` (512 existing warnings, zero errors),
  `.ks_procgen_snapshot_final_debug.log` (fresh Debug, 1,107 existing warnings, zero errors),
  `.ks_procgen_snapshot_final_units.log` (635 passed), and
  `.ks_procgen_snapshot_final_integration.log` (200 passed). Both test runs have zero failures
  and zero skips. The integration TRX identifies the actual `Utility.SandboxTest.Test` as passed.
  No new warnings, suppressions or sandbox whitelist changes; scoped whitespace checks pass.
  The full repository suite was not run. Contracts advance from 12 to 13 points for this tested
  request-snapshot capability; total expanded-scope estimate is 55%, and the 80% goal stays active.
- [ ] Complete the remaining R08 content snapshot, scoped utility ownership/consumer declarations
  and full project asset contracts, followed by R09 integration. Current declaration identity does
  not hash external generation budgets, live context or referenced entity component bodies; R15
  must bind these separately for complete replay. Parsing/upload byte/depth bounds and persistence
  remain R16 responsibilities; this adds no externally callable deserialization endpoint.

### Utility draft catalog and inherited theme isolation (2026-10-16, R08)

This batch implements the first headless authoring document subset from section 21.3.
R08 remains open: a utility asset catalog is not a complete editable room project or a unified
request/content snapshot. R09/R16 consume these contracts later; no production planner guard is removed.

- [x] Add `KsProcgenUtilityDraftSpec` schema 1 with stable document ID, nonnegative edit revision,
  and named atmosphere/electrical assets. The combined asset count is bounded at 128; names,
  nested numeric policies and referenced gases/entities use the existing profile validators.
  Unknown document or nested profile schemas and invalid receiving-content dependencies reject
  the entire import, without returning a partially usable catalog.
- [x] Freeze the catalog and nested profiles into `KsProcgenUtilityDraft`; return independent
  editable copies. Canonical content identity sorts names by ordinal comparison and excludes
  document identity/revision. This hash covers utility declarations, not a loaded-content manifest,
  authorization, verified network capacity or a complete generation request.
- [x] Add a full-document headless revision replacement check. Replacement must target the same
  document/current revision, validate successfully and advance revision once. Stale/foreign edits,
  invalid payloads and exhausted integer revisions fail without changing either snapshot or input.
  A future server owner must serialize replacement of its current instance; this value API alone
  is not a concurrent registry, writer lease, operation deduplication service or undo history.
- [x] Resolve explicit utility references against an optional document catalog before loaded
  prototypes. Inline overrides retain priority, and absent explicit references retain inherited
  defaults. Names are scoped by document and utility kind, so gas/power may use the same local
  name. Editing a loaded asset creates a detached local override of that name; resolution without
  the catalog still sees the original global prototype. Theme utility defaults use the same path.
- [x] Replace inherited theme pack-reference objects and goal objects with detached immutable
  records and immutable collections. Mutating parent weights, clearing parent lists or editing
  goals after resolution cannot change an earlier resolved theme. Referenced material/entity
  pack bodies still need freezing into the full R08/R09 content snapshot.
- [x] Fix the new utility fixtures' failure reporting with shared `KsProcgenGameTest` teardown.
  The first mutable-list mutation exposed an assertion masked as a dirty-disposal skip. Repeating
  it after the helper rewrite produces one actual failed integration case, three passed and zero
  skipped. The revision/hash mutations produce four failed unit cases, fourteen passed and zero
  skipped. All deliberate source changes were restored byte-for-byte in `finally` blocks.
- [x] Verify Release, fresh Debug, focused unit/integration regressions and the actual client
  sandbox; demonstrate meaningful failures with deliberately broken source and restore it.
  Final evidence: `.ks_procgen_drafts_final_release.log` (265 existing warnings, zero errors;
  initial Release also passed with 512 existing warnings), `.ks_procgen_drafts_final_debug.log`
  (fresh Debug, 1,107 existing warnings, zero errors), `.ks_procgen_drafts_final_units.log`
  (596 passed), and `.ks_procgen_drafts_final_integration.log` (196 passed, including the actual
  `Content.IntegrationTests.Tests.Utility.SandboxTest.Test`). Both regression runs have zero failed
  and zero skipped cases. No new warnings, suppressions or sandbox whitelist changes were added.
  Mutation evidence is retained in `.ks_procgen_drafts_mutation_units.log` and
  `.ks_procgen_drafts_mutation_visible.log`; the first masked result is retained separately in
  `.ks_procgen_drafts_mutation_loaded.log`. The full repository test suite was not run.
  Scoped whitespace/encoding checks pass; the four private KS modules remain clean and 0/0
  against their locally recorded `origin/main` refs (no remote fetch in this batch).
- [ ] Finish full room/theme/assembly asset documents, area/domain/circuit assignments, dependency
  manifests and canonical request snapshots in R08/R15. Add server ownership, typed intentions,
  permissions, bounded upload parsing, writer leases, operation replay, undo/redo and persistence
  in R16. No client/network/import command accepts this helper directly yet.

### Atmosphere authoring and mixture foundation (2026-10-15, R08/R11)

This batch implements the atmosphere portion of the new contracts, not a live room fill.
Electrical profiles, gas-domain assignment/closure, the unified stage and UI remain open.
Existing geometry/content requests without explicit gas settings retain their supported behavior.

- [x] Add version-1 `KsProcgenAtmosphereSpec`/`ksProcgenAtmosphereProfile`, preserve/fill/vacuum,
  required/preferred and initial/stable policies, pressure/temperature/fraction tolerances and a
  bounded observation interval (1-3600 ticks for stable; zero for initial-only). Reject unknown
  versions, clamped/nonfinite states, incorrect gas identifiers and malformed fractions.
- [x] Resolve exactly named engine gas IDs, normalize only within the declared `1e-6` sum rounding
  tolerance, drop validated zero fractions, sort gas names and detach immutable snapshots. Keep
  source profile identity and versioned semantic hashes; editing source dictionaries cannot mutate
  an earlier snapshot. Preserve/vacuum remain distinct and cannot carry hidden replacement gases.
  Retain representation errors at or below `2^-24` rather than renormalizing already rounded
  floats on each draft reopen; a deterministic 512-mixture/four-roundtrip regression pins identity.
- [x] Add direct request `atmosphereProfile` references and detached `atmosphere` overrides; painted
  area and inherited theme defaults use `atmosphereProfile`. Snapshot area profiles into immutable
  resolved values, transport fresh editable gas specs per request, hash resolved gas state in
  area snapshot v6 and export it through area-review JSON schema 2. Existing owned-diagnostic JSON
  schema 1 remains unchanged. Inline selection replaces the whole profile, not individual gases.
- [x] Add ordinary air, nitrogen, vacuum and preserve prototypes, an opt-in nitrogen area and a
  nitrogen theme inheriting the office materials. Existing office defaults do not opt into gas writes.
- [x] Add a per-tile mixture proposal using the actual caller-supplied volume and engine pressure
  arithmetic; reject overflowing/underflowing or out-of-tolerance results without partial proposals.
  These detached mixture objects do not touch engine tiles or certify initial/stable atmospheres.
- [x] Reject explicit gas contracts at both primary content entry points with
  `AtmospherePlanningUnsupported` before layout work; reject theme gas defaults when selecting
  unsupported content. Unknown profiles and invalid schemas retain specific diagnostics. Explicit
  preserve profiles are also gated until preservation can be retained/verified by the unified plan.
- [x] Verify Release, fresh Debug, focused unit/content/serialization regressions and actual client
  sandbox loading; demonstrate failing assertions under deliberate restored-source mutations.
  Final restored-source Server Release passes with 512 existing permitted warnings and no errors.
  A subsequent sequential Debug integration `--no-incremental --no-restore` build passes with
  1107 existing warnings and no errors; no warning suppression or sandbox whitelist change was
  added. All 548 focused procgen unit cases and 184 selected loaded/integration cases pass with
  zero failures/skips, including `Content.IntegrationTests.Tests.Utility.SandboxTest.Test`.
  - New coverage comprises 35 focused unit cases and four loaded cases, including all four
    profile presets and all three modes, engine serialization, inherited defaults and explicit overrides,
    paused painted-grid snapshots, versioned review JSON, numeric overflow/underflow, invalid
    schema/gases, and both primary planners refusing unsupported gas contracts without allocation.
  - The repeated-draft regression initially caught normalization drift at deterministic mixture
    135; preserving representational roundoff fixes it. An initial paint fixture returned `NoOp`
    because the engine reparents markers off an empty grid; use the existing explicit
    `BindMarkerToGrid` authoring adapter. Final gates include both fixes. The loaded fixture logs
    original assertion exceptions so test-pool dirty disposal cannot hide them behind a skip.
  - Deliberately divide by tile volume instead of multiplying, expose a mutable fraction list,
    and drop gas data from `CreateRequest`: all three selected tests fail their protected
    assertions. Original source bytes are restored in `finally` and independently inspected;
    the Release/fresh Debug/final regression gates above ran afterward on restored source.
  - Logs: `.ks_procgen_atmosphere_final_release.log`, `.ks_procgen_atmosphere_final_debug.log`,
    `.ks_procgen_atmosphere_final_units.log`, `.ks_procgen_atmosphere_final_integration.log`, and
    `.ks_procgen_atmosphere_mutations.log`. TRX artifacts retain case-level results. Scoped diff
    and new-file whitespace checks pass. All four private KS modules remain clean and 0/0
    against their locally recorded `origin/main`; no remote fetch was performed.
  - These are focused gates; the full repository suite, real room gas initialization and an
    editor UI walkthrough remain unverified. The historical 75% estimate is not rescored here.
- [ ] Integrate request/area/theme precedence with immutable request-wide hard constraints,
  scoped room/domain assignments and the primary R09 plan. The standalone profile resolver accepts
  an already resolved lower-priority default; it does not yet enforce request-wide utility goals.
- [ ] Add actual tile mutation, engine domain facts, initial readback and scoped observation under
  R10/R11 before removing the unsupported-planning guards. Continue R08 electrical/draft contracts.

### Input-only slot loading and narrow-room follow-up (2026-10-22, R08/R09/R10, in progress)

The first broad Release run compiled after correcting a typed-prototype-ID analyzer error, but
only 675/682 integration cases passed. Seven failures exposed dependencies outside the focused
power tests; `.ks_procgen_wallmount_release_initial_integration.log` retains that red gate.
At that failed checkpoint the estimate remained 64/100. The restored complete gates below
subsequently establish the supported topology exit and the current 65/100 assessment.

- [x] Diagnose the two captured contained-room failures: restored `ItemSlots.Slots` declarations
  cause native `TryInsert` to play the slot sound, which creates an already MapInitialized Audio
  entity before the private map initializes. The earlier lost slot dictionaries had silently
  routed these insertions through generic containers. Do not erase slot data or relax lifecycle
  membership checks to make the scene pass.
- [x] Draft silent loading through native `TryGetSlot` / `CanInsert` and native container `Insert`.
  Keep eligibility, whitelist/lock checks, insertion events, callback cancellation and membership
  proof; loading is not a user interaction and must not play insertion audio. Ordinary native
  user interaction remains unchanged. Remove temporary diagnosis logging after locating the Audio.
- [x] Add a real owned-stage test requiring a retained non-null insertion sound, an occupied slot,
  no generated Audio entities and complete scene verification. This case and the previous captured
  contained-room cases pass in the focused follow-up; final broad checks remain below.
- [x] Make empty-content manifest fixtures explicitly request zero optional furnishing density.
  Their previous empty scenes depended on implicit strip-to-passage classification; dependency
  and unselected-variant manifest checks must remain independent of furniture packing.
- [x] Diagnose another real R09 inconsistency: legacy flat furnishing placed initial optional
  cores even at zero density, unlike the supported solver. Draft zero-density suppression after
  hard core/pack minima, and add two tests distinguishing optional omission from mandatory copies.
- [x] Update the two existing retry assertions to the actual narrow-room `RequiredPackCannotFit`
  result, including the failed furnishing stage. They still require complete configuration retry,
  retained work and a successful later configuration; no mandatory requirement was relaxed.
- [x] Verify both complete configurations after these fixes: normal Release and fresh Debug pass
  825 unit, 685 integration/sandbox and 31 native serialization cases each, retaining 820/659/23
  with the single deliberately replaced obsolete electrical-allocation guard recorded below.
  `.ks_procgen_wallmount_gates_summary.log` and `ks_wallmount_{release,final}_{units,integration,engine}.trx`
  retain the complete case comparison and actual `SandboxTest.Test`. Temporary diagnosis logging
  is removed; lifecycle/ownership and native insertion checks remain strict.
- [x] Correct the optional-content test fixtures after the zero-density fix: explicit small
  positive density preserves their intended optional competition, sparse outcomes, weighted retries
  and relation-path assertions. The explicit zero-density cases remain separate. The large historical
  content-load scenario passes after its one-cluster test uses low positive rather than zero density.
- [ ] R18 test structure rewrite: split `TinyShapeCanBeNormalizedInLoadedServerContent` into focused
  capability cases with shared fixtures. Its large mixed scenario hid later optional-density
  assumptions behind earlier assertion failures. Preserve every existing check and provide an
  explicit baseline mapping rather than dropping assertions during the split.
- [ ] Continue the ordinary legacy-pack joint-search rewrite: nonzero optional fill must retry
  coherent placements or sparsify without disconnecting thin rooms; forcing all narrow shapes
  back to passage is not a substitute. Preserve density, hard minima and shared work budgets.

### Electrical utility-room and routing amendment (2026-10-23, user steering, required rewrites)

Section 20.3a is now normative. Existing completed native mounting/topology tests retain their
narrow historical evidence, but do not establish dedicated closets or adequate cable coverage.
The current `HomeGroups`/`TryInfrastructure` code chooses general rooms and co-locates both
converter classes. Current MV joins endpoints and LV principally covers declared consumers.
These are explicit rewrite targets; do not add another disconnected post-processing generator.

- [ ] R08/R09/R12: version and freeze closet theme/pack palettes, 1x2/2x2 interior sizes, backbone
  and room-floor coverage intent, and strict LV 2x2 exception evidence. Migrate existing drafts,
  profiles, request snapshots, manifests, hashes, examples and future UI through shared contracts.
- [ ] R09/R14: allocate actual utility-room partitions and entrances jointly, respecting constant
  areas, writable hull/cable masks and requested entrance connectivity. Replace general-room
  substation homes and forced APC/substation co-location; keep APC wall support and both substation
  mount variants. Keep optional electrical clutter coherent without blocking tiny closet access.
- [ ] R12: build a generous MV service backbone with room-entry/APC branches and report unmet
  service targets independently of device topology. Count every proposed/reused route branch.
- [ ] R12: improve LV service coverage across usable room floors and actual receivers. Avoid new
  2x2 occupancy throughout route construction and joins; retain a necessity certificate for every
  exception, with exhaustive versus heuristic/budget status explicit. Revalidate native groups.
- [ ] R18: finish preserved/hosted wiring, inspected existing-hull reuse and complete pose/route
  portfolio controls. The later standalone batches below already verify both closet sizes and
  rotations, floor/wall substations, native wall APCs, generated-hull sharing, clutter/access,
  MV reach, LV coverage/range edges, block rejection/mandatory-terminal certificates and bounded
  failures. The old open-room showcase/co-location expectations are replaced. Keep this broader
  release task open until the remaining interfaces and operational/replay requirements pass.
- [ ] R16: expose room palettes, backbone/floor coverage overlays, uncovered receivers, forbidden
  or certified LV blocks and proof limitations in author tools, with real review/apply evidence.

### Showcase cable visibility correction (2026-10-26)

The requested correction concerns the rendered showcase images. Installed cable routes were
partly hidden by later wall and machine drawing. Wiring inspection must expose the complete route.

- [x] Draw the cable sprite pass after all floors, structures, lights and contents in the wiring PNG.
- [x] Draw SVG cable diagnostics last, retaining the existing cable-layer visibility control.
- [x] Regenerate sprite assets, SVGs and gallery; inspect the wiring PNG and verify source/output
  hashes. All five normal sprite PNGs and native map bytes remain unchanged.
- [x] Check native installed-wire persistence separately: seven targeted cases pass in each of
  Release and Debug,
  including initially paused and unpaused imports, 30 simulation ticks, all 32 anchored/snap-lattice
  cable segments, covered-floor hiding, converter node membership and native consumer backlinks.
  A consumer may select the APC's proximity provider directly; it need not select a cable entity.
- [x] Build Release and Debug and finish the native import regression in both configurations;
  results: `ks_wireplacement_cover_native.trx` and `ks_wireplacement_cover_debug_native.trx`.

This renderer adjustment earns no additional capability points. It does not certify operating
supply or general post-load/hosted topology. The subsequent typed-reference batch refreshes both browser
overviews from the current gallery; wiring PNG/SVG outputs are regenerated and visually inspected.

### Inheritance-parent dependency closure (2026-10-26, R08/R15/R17, verified parent subset)

Schema 10 extends the previous typed-reference subset to explicit inheritance links. Parent
relationships are discovered from the registered `IInheritingPrototype` contract and its known
`parent` declaration field, rather than by guessing which ordinary strings name prototypes.
The last completed assessment remains 68/100; this prerequisite adds no full-manifest point.

- [x] Follow both runtime parent arrays and resolved declaration parent scalars/arrays across
  entities, tiles and other registered kinds. Charge occurrences before deduplication and share
  the existing definition/reference/node/character/depth allowances across all ancestors.
- [x] Preserve abstract ancestor declarations and bounded reference-discovery images without
  granting placement authority. Add declaration-only tile records; direct abstract tile roots
  still reject and concrete tiles retain loaded/named/numeric binding checks.
- [x] Verify transitive and diamond ancestry, parent order, cycles, declaration-only drift beneath
  an unchanged child, mutable runtime parent arrays, missing/malformed parents, repeated edges,
  exact budgets and immutable images. Verify closure through abstract ancestor typed references.
- [x] Inspect partial abstract fields through registered validators without constructing fake
  complete instances or requiring omitted mandatory fields. Bind canonical inspection input and
  stable, bounded schema-gap codes into schema 10 identity. Unknown fields never become guessed
  prototype references; validation success alone is not complete reference-coverage evidence.
- [x] Verify the native `CarpetBase` correction and a concrete inherited threshold: its destruction
  behavior was incorrectly nested under `DamageTrigger`; move it to the threshold's behavior list.
  Keep this source repair separate from intentionally opaque legacy/client declaration fields.
- [ ] Resolve outstanding declared-schema gaps before claiming full content closure. The native
  `ClothingUniformSkirtBase` ancestry currently exposes legacy `femaleMask` data absent from the
  registered server `ClothingComponent` schema. This is the one observed office-catalog schema
  gap in `.ks_procgen_inheritance_native_catalog_audit.log`. Audit its intended current schema/client meaning;
  migrate author data or inspect the correct schema explicitly, rather than inventing references
  or disabling ancestor capture. Other captured gap codes remain concrete follow-up inputs.
- [x] Reconcile source-map override seed provenance and owned-stage retirement with ancestor drift.
- [x] Measure the native catalog: 337 entity/tile/declaration records and 312 other-kind records
  give 649 combined definitions, with 7,253 reference occurrences, 133,845 nodes and 2,987,554
  canonical characters. The existing 1,024-definition, 32,768-reference, 262,144-node and
  4,194,304-character defaults all fit. No default is raised and no ancestor is omitted.
- [x] Keep recognized entity/network/map references in abstract fields as hard failures rather
  than schema gaps; verify all three types without allocating or querying world entities.
- [x] Run six isolated compile-valid controls with exact byte restoration: disabling parent
  traversal causes seven assertion failures; omitting abstract entity or generic-kind reference
  observation causes one each; suppressing gap reporting causes one; reverting carpet threshold
  nesting causes one; softening recognized world-reference rejection causes three. All 14 failures
  are intended assertions. All six mutated source hashes match after restoration. Evidence:
  `.ks_procgen_inheritance_control_audit.log` and `ks_inheritance_control_*.trx`.
- [x] Normal Release passes 891 units, 895 integration/sandbox cases and 31 serialization cases,
  retaining all prior 891/862/31 cases. The 33 added cases comprise 32 inheritance cases and one
  source override ancestor case. The carpet test additionally verifies the destruction act.
- [x] Fresh Debug also passes 891/895/31 with zero failures/skips and identical inventories.
  All prior 891/862/31 cases remain, and all 12 source hashes (11 C# files plus the carpet YAML)
  stay unchanged across the six gates. Evidence: `.ks_procgen_inheritance_final_audit.log`,
  `.ks_procgen_inheritance_source_audit.log` and `ks_inheritance_{release,debug}_{units,integration,engine}.trx`.
- [x] Regenerate five native exports/save-load cases and install matching resources. Both ordinary
  office import cases pass against the refreshed map. All 72 sprite-source hashes and six output
  identities match; all six PNGs retain their preceding bytes, with cables drawn last. Six historical
  review artifacts remain byte-identical. Refresh and inspect both browser overviews: all five
  images, both view modes and all five overlay controls pass, including nonempty overlay groups.
  Temporary profiles/HTML copies are removed. Evidence: `.ks_procgen_inheritance_artifacts_audit.log`,
  `.ks_procgen_inheritance_browser_audit.log`, `ks_showcases.trx` and `ks_inheritance_installed_wiring.trx`.
- [ ] Continue remaining legacy/custom/client/resource dependencies, portable project assets and
  replay after this rewrite. Full content closure and general operational/publication flags stay
  false until their own complete exits pass.

Focused Release evidence: `ks_inheritance_gaps_native.trx` passes 30 cases;
`ks_inheritance_focused_native.trx` passes 245 dependency/manifest/source-map cases; and
`ks_inheritance_restored_precontrol_native.trx` passes all 248 after adding the three recognized
world-reference cases. These include concrete-parent runtime changes, masked entity/generic
references in partial abstract fields, source override provenance and selected/unselected owned
stage retirement. Full configuration and refreshed artifact/browser gates now pass; the wider R08/R15/R17 exits above remain open.

### Registered typed prototype dependency closure (2026-10-26, R08/R15/R17, verified typed subset)

The existing image followed entity wrappers but ignored other `ProtoId<T>` kinds. This rewrite
retains kind-qualified dependencies and follows their typed references back into concrete entities,
registered tiles or other registered prototype kinds. It is a prerequisite for complete manifests;
resource bytes, legacy string serializers, ignored client schemas and full replay remain open.

- [x] Introduce native image schema 9 with separate immutable kind/ID/runtime-type/body/declaration
  records and explicit dependency-root provenance. Keep entity/tile roots separate from referenced
  definitions; dependencies never acquire spawn authority merely by appearing in a manifest.
- [x] Follow registered typed kinds across nested collections and mapping keys/values. Reconcile
  typed tile references with the existing named/numeric tile-registration audit. Meter repeated
  references before deduplication and share definition, node, character and depth allowances.
- [x] Discover typed references in abstract templates without deserialization hooks, retaining only
  declaration authority. Do not register templates globally or make abstract entities spawnable.
- [x] Compare the complete dependency closure during fresh manifest checks and parent-placement
  checks. Existing owned stages capture all candidate definitions, including unselected variants;
  dependency drift must retire a stage rather than silently consult changed live defaults.
- [x] Verify transitive paths/cycles, kind collisions, abstract and nested references, source and
  runtime drift, exact budgets, immutable captures, malformed roots and atomic native retirement.
  Both configurations pass 29 direct typed-kind cases and six source-map kind cases.
- [x] Run three isolated compile-valid controls: ignoring non-entity typed references causes five
  assertion failures; bypassing dependency equality only at parent placement causes three failures
  with the entity-leaf case still passing; omitting source override kinds causes five failures.
  All three original source hashes match after byte-for-byte restoration. Evidence:
  `.ks_procgen_kindclosure_control_audit.log` and the three `ks_kindclosure_control_*.trx` files.
- [x] Normal Release and fresh Debug each pass 891 units, 862 integration/sandbox cases and 31
  serialization cases, with zero failures/skips and equal inventories. All prior 891/825/31 cases
  are retained; 35 typed-dependency/source-map cases and two imported-wire cases are added.
  All 14 C# source hashes remain unchanged across the six gates. Evidence:
  `.ks_procgen_kindclosure_final_audit.log`, `.ks_procgen_kindclosure_source_audit.log` and
  `ks_kindclosure_{release,debug}_{units,integration,engine}.trx`.
- [x] Regenerate five native exports/save-load cases and install byte-identical resource maps.
  Two additional ordinary import cases pass against the refreshed office, including paused and
  unpaused starts and 30 simulation ticks. All 72 source sprite hashes and six output/plan identities
  match; the six sprite PNGs retain their preceding geometry and the wire overlay stays above all
  layers. Six historical review files retain their exact hashes.
- [x] Refresh and inspect both browser overviews; all five images load, both view modes and all
  five overlay controls pass in each capture. Disposable profiles and HTML copies are removed.
  Native artifacts: `.ks_procgen_kindclosure_artifacts_audit.log`; browser evidence:
  `.ks_procgen_kindclosure_browser_audit.log`. The normal localization cache is refreshed after
  correcting stale electrical-preset comments. No general post-load or operating-supply flag is
  promoted from the two scoped import checks.

- [x] Track inheritance parents explicitly in the subsequent schema-10 closure rewrite. Parent arrays such as
  `AbstractPrototypeIdArraySerializer<TPrototype>` are string arrays with custom validation, not
  `ProtoId<T>` wrappers; retaining a child's resolved declaration does not capture every ancestor's
  own declaration/body. Follow registered-kind parent links, including abstract ancestors, under
  the shared budgets. Verify declaration-only ancestor drift even when the resolved child fields
  stay equal, cycles/deduplication, missing parents and source override provenance. Do not infer
  prototype links from arbitrary strings.
- [ ] Complete legacy/custom reference serializers, resource and ignored-client dependency capture,
  portable project imports and generator replay. `FullContentClosureVerified` remains false; this
  typed-closure rewrite alone does not complete R08/R15 or earn a full manifest/editor point.

The ordinary office catalog contains 238 entity/tile/declaration records and 300 other-kind
records: 538 combined definitions, 5,721 reference occurrences, 114,695 nodes and 2,589,282
canonical characters in the measured native catalog. Schema 9 therefore raises the default
combined definition allowance from 512 to 1,024; its hard maximum stays 4,096. No dependency kinds
are excluded to fit the old cap. Reference/node/character/depth limits remain unchanged. The
regression checks that the default captures the same content as an explicit larger allowance and
that an explicit 512-definition allowance fails atomically with `PrototypeManifestDefinitionBudget`.


### Generated hull sharing for electrical closets (2026-10-25, R09/R14/R18, verified standalone subset)

This continuation addresses the unnecessary interior wall band in the dedicated-room amendment.
It does not authorize reuse of existing or preserved hull. Complete shared/native/artifact
gates below retain the fixed 68/100 assessment; this extends already credited utility/layout scope.

- [x] Separate immutable reused envelope cells from partition-owned enclosure cells; bind them
  into reservation identity and native service-layout comparison. Keep thresholds and approaches
  on target floor, preserve full-band fallback, and reject overlapping partition ownership.
- [x] Enumerate hull-flush reservations with bounded ordinal retries and shared rejected-candidate
  work. Bump the schema-two allocator dependency to algorithm 3; preserve old no-reuse reservation
  hash encoding and the legacy schema-one electrical-context path.
- [x] Carry explicit structural corner claims through hull boundary inventory and material
  selection. Combine utility ownership with incident ordinary-room palette requirements rather
  than silently overriding a conflicting hull material. Do not invent diagonal exposure edges.
- [x] Verify narrow 1x4/4x1 and 4x4 reservations, impossible 1x3, missing/preserved/void/foreign
  target corners, immutable claims, canonical replay, exact budgets and partition-overlap denial.
- [x] Verify initialized native floor/wall converters, wall APCs, enclosure including corners,
  windows, generous MV targets, full usable-floor LV coverage, no uncertified blocks and rollback.
- [x] Run two isolated compile-valid controls: allocation denial causes three small-footprint
  unit assertion failures; removing explicit corner claims causes three geometry assertion
  failures and six native material-authority failures (`StaleUtilityHullMaterialClaim`). The
  latter guard rejects incomplete claims before spawning; this is not an operating-power test.
  Restore both mutated files byte-for-byte and confirm all nine changed C# hashes. All 34 focused
  utility units and 59 focused electrical/structural native cases pass after restoration.
- [x] Normal Release and fresh Debug each pass 891/825/31 with identical inventories, zero
  failures/skips and all previous 881/817/31 retained. Ten units and eight integration cases are
  new. Actual sandbox checks and current installed showcase maps pass in both configurations.
- [x] Regenerate five native exports/save-load checks and install matching resources. The office
  gains seven floor tiles by sharing seven enclosure cells with generated hull. It has 32 floors,
  23 walls, six windows, one door, five content members, 1 HV / 23 MV / 8 LV segments, 17 MV
  targets, 30/30 usable floor coverage and zero LV squares. All 72 RSI/PNG source hashes and six
  preview hashes/plan associations pass. The four other sprite previews and SVGs remain unchanged;
  all five native serialized exports are refreshed. Inspect the office sprites, exposed wiring
  and both refreshed browser overviews. Hidden Edge checks all five images, both view modes and
  all five toggles in both states; disposable profiles are removed.
- [ ] Add typed utility diagnostics/editor overlays for reused versus partition-owned walls and
  structural corner claims. Static exported enclosure data is not the section 21 authoring UI.
- [ ] Continue inspected existing-hull reuse, hosted/preserved interfaces, complete pose/route
  portfolios, operational supply and authoring-default migration as separate work.

### Joint utility reservations and automatic furnished defaults (2026-10-25, R09/R14/R18, verified full-band standalone subset)

This batch replaces first-reservation-only fitting in the standalone schema-two pipeline. The
weighted assessment is now 68/100 after the retained gates below; hull-boundary reuse, complete
hybrid fitting, global optional-content optimization and default-schema migration remain open.

- [x] Enumerate deterministic complete full-band reservations by ordinal, accounting for all
  discarded enclosures, door candidates and zone-fragment checks in the shared work allowance.
  Prefer enough interior space for the inspected hardware footprint, clean operation and desired
  loose contents; this is a soft fitting preference, never a minimum geometry exemption.
- [x] Retry complete geometry after later seam/content/device/route rejection using a bounded
  Cartesian frontier of independent per-network ordinals. Preserve already exhausted per-network
  bounds, deterministic ordering and all consumed graph/seam/furnishing/layout quotas. Retain only
  scalar choice diagnostics for failed candidates; exhaustion is unknown, not infeasibility.
- [x] Verify distinct/canonical choices, comfort versus small-shape fallback, copied independent
  network selections, invalid/missing ordinals and exact/one-less allocation work. Verify native
  furnished default generation without size or planning-budget overrides, masked-device relocation,
  shared whole-search limits and independent networks that do not advance in lockstep.
  All 881 procgen units and 67 selected native/showcase/sandbox integrations pass preflight.
  The two-island fixture initially requested one connected area; explicitly select PerIsland.
- [x] Rewrite the sparse-endpoint coverage regression to retain its first required-coverage failure
  and expect honest exhaustion when only one geometry probe is allowed. The old first-layout
  rejection is no longer a global conclusion after adding reservation retries. Never permit a
  successful candidate that omits required coverage.
- [x] Remove the office showcase's forced 2x2 override and use the normal named schema-two preset.
  Bind allocation algorithm version 2 into schema-two context identity while retaining historical
  schema-one identity and general owned planning/review versions 10/3.
- [x] Complete compile-valid negative controls, exact restoration, normal Release/fresh Debug
  retained gates, native re-exports, sprite/diagnostic audits and an updated fixed-weight assessment.
  Each configuration passes **881/817/31**, retains every **873/797/31** case and has an identical
  inventory with zero failures/skips. All 28 new positives pass in both final configurations;
  both actual sandbox cases and both ten-case installed-showcase map inventories pass.
  A combined compile-valid control disables authority comparisons, intrinsic reservation ordinal
  selection, comfortable sizing and geometry-frontier growth. Exactly **3 unit + 15 integration
  assertions** fail: thirteen fail at the intended policy/device audit, the default case reaches
  its size assertion, and the masked-device case cannot produce its required retried candidate.
  Twenty-one other reservation cases still pass. All four mutated sources restore byte-for-byte
  in `finally` before both complete positive configuration gates.
  Logs/TRX: `.ks_procgen_electricalauthority_{release,final}_*`, `_final_audit.log`,
  `_control_audit.log`, `_artifacts_audit.log` and
  `ks_electricalauthority_{release,final}_{units,integration,engine}.trx`.
- [ ] Reuse actual inspected/generated hull boundaries rather than always consuming a full
  enclosing band inside writable floor. Full-band retry support does not complete this requirement.
- [ ] Rank optional-content quality across multiple complete candidates when necessary. Current
  ordering uses frozen hardware footprint and desired content as a soft space estimate, then
  accepts the first complete valid result. This is not a global optimality guarantee.
- [ ] Add bounded route-order/APC-site/pose portfolios within a reservation when an earlier valid
  LV branch blocks a later goal. Whole-reservation retries cannot replace all within-room retries.

### Original electrical policy and converter authority (2026-10-25, R17, verified native subset)

Continue the authority rewrites before adding utility-layout behavior. This batch adds no weighted
points; the separate full-band fitting exit above raises the fixed assessment to 68/100.

- [x] Retain the effective immutable electrical profile selected before proposing, including
  theme fallback and explicit-request precedence, in the owned scene. Compilation rejects a
  missing/substituted routed profile; live inspection compares the complete original declaration.
  Service-domain reconstruction must use that original policy, not the routed plan as authority.
- [x] Reconstruct converter declarations from captured infrastructure bindings/poses and compare
  ID, room, prototype, tile, quarter turns, converter role and wall mounting with each claim.
  Inspect the real anchored grid parent, native position/rotation, prototype and mount component.
  MV terminal-domain seeds must come from captured poses. A connected bus does not prove a claimed
  converter coordinate or certify mandatory-terminal necessity at a different coordinate.
- [x] Verify same-network forged claims, omission/duplication, weakened policy/palette declarations,
  actual native position/rotation drift, theme-selected defaults, explicit precedence and exact
  shared audit work. Keep all prior native wiring tests. The first preflight passes 56/57 cases;
  its substation rotation fixture did not mutate a no-rotation entity. Correct it to move/re-anchor
  the real substation and assert the physical change before testing the audit; do not relax checks.
- [x] Run targeted compile-valid controls without coupled routing defects, restore exact bytes,
  complete normal Release/fresh Debug retained gates and audit unchanged/revised artifacts.
  Sixteen new cases cover eight converter claim substitutions/omissions/duplications, three policy
  changes, actual APC rotation and re-anchored substation movement, exact work and two theme/explicit
  precedence variants. The substation fixture correction retains actual physical-change assertions.
  Thirteen specific audit failures in the combined control reach `InspectPolicyAndDevices` and
  observe null instead of the required failure code; no unrelated routing failure is counted.

### Independent electrical domains and bounded route joins (2026-10-24, R09/R14/R17, verified standalone subset)

This batch completes prerequisite service-domain inspection and route work-bound repairs. Keep the
fixed assessment at 67/100: these strengthen already credited wiring rather than complete operating
power, hosted preservation, whole-layout retries or the authoring workspace.

- [x] Retain detached room floors, protected passages, utility reservations and door approaches
  separately from the electrical service witness when compiling the owned scene. Reconstruct
  allowed cable cells from compiled terrain and captured request masks, MV targets from room and
  circulation geometry, and eligible LV floor from room floors and captured blocked footprints.
  Native inspection must reject missing, substituted, duplicate or overclaimed domains/rooms.
- [x] Verify dropped cable/MV/floor/closet claims, same-count substitutions/duplicates, usable versus
  all-room floor, traversable coil supplies, copied input collections and exact shared audit work.
  First preflight passes all 23 electrical service integrations, including eleven new cases.
  The final input-freezing fixture clears every source room, nonempty protected passages, the
  outer room collection and door records, while the captured complete layout remains intact.
- [x] Replace ancestor-scanning route growth with shortest cardinal branches over the fixed wire
  state, precluding tiles that complete a square against existing wires. Independently inspect
  the entire proposed branch before publishing it. A branch never authorizes a new unproved block.
  This is a single-join guarantee, not global route/APC/closet ordering or necessity certification.
- [x] Verify every allowed/mandatory/absent assignment on a 3x3 grid against an independent
  exhaustive simple-path oracle, plus 1x1024/1024x1 routing within 100,000 shared work units.
  Freeze electrical routing algorithm version 2 into schema-two context identity; preserve
  schema-one identities and general owned planning/review versions 10/3.
- [x] Run compile-valid negative controls, restore sources exactly, complete normal Release/fresh
  Debug retained unit/integration/sandbox/serialization gates and regenerate/audit affected native
  showcases and actual-sprite associations. Update detailed evidence and current metadata.
  Final normal Release/fresh Debug each pass **873/797/31**, retaining all **870/786/31** baseline
  cases with identical inventories and zero failures/skips. Both actual sandbox cases and both
  ten-case installed-map inventories pass; all fourteen new positives are retained.
  The first combined control disables intrinsic square exclusions and domain comparisons. Its
  exhaustive oracle fails (domain 63, terminals 52, target 0); seven native cases fail earlier in
  generation because the route defect interferes, so those seven are NOT audit evidence. A second
  isolated domain control changes only comparison guards, producing seven specific expected-code
  versus null failures at `InspectServiceLayout`. Two source files restore exactly after the
  first control and the native file restores exactly after the second. Final positive gates follow
  restoration and the strengthened input-freezing fixture. Valid control evidence is eight
  invariant assertion failures, with ten unaffected route positives; do not count fifteen failures.
  Evidence: `.ks_procgen_servicedomains_{release,final}_*`, `_final_audit.log`,
  `_control_interference_audit.log`, `_domaincontrol_audit.log`, `_artifacts_audit.log` and
  `ks_servicedomains_{release,final}_{units,integration,engine}.trx`.
  The generator now accepts `-Configuration Release` as well as default Debug and forwards it
  consistently to build/test. Five native export/save/load cases and matching installed resources
  pass. All 68 sprite source hashes, six output plan/file associations and the 33-entry artifact
  manifest are audited. Visible gallery markup, six sprite images and five SVGs are unchanged;
  browser/overview evidence is retained on that exact-byte basis, not claimed as a fresh browser run.
- [x] Freeze the effective pre-planning policy and bind converter claims to captured/native poses.
  The 2026-10-25 authority batch above verifies theme fallback, request precedence, all descriptor
  fields, same-bus coordinate forgery, physical pose drift and original-policy domain reconstruction.
  Native MV terminal seeds now come from captured infrastructure poses.
- [x] Retry complete full-band closet arrangements after later hard rejection under shared quotas;
  the 2026-10-25 independent-network native batch above verifies this subset.
- [ ] Add within-reservation route/consumer-order and APC-site/pose alternatives when an earlier
  committed branch prevents a later goal. Single-join shortest-path correctness and complete
  reservation retries do not prove all alternate layouts impossible.
- [x] Add comfortable hardware/content-aware size ordering and full-band whole-reservation retries.
  The 2026-10-25 native default uses no size/planning-budget override and includes coherent supplies;
  the showcase now uses the normal preset. Continue hull reuse and optional-quality optimization
  under the explicit pending tasks above; first-complete-valid search is not global best-fit proof.

### Owned electrical service rewrite (2026-10-24, R09/R12/R14/R15/R18, verified standalone WiredOnly subset)

This batch connects the structural utility-room subset to the real owned planning/materialization
path. Normal Release and fresh Debug retained gates below verify this bounded subset. Weighted
credit is now 67/100, with two additional distinct utility capability exits. Sustained operational
supply, hosted network/preservation capture, hull reuse and complete post-rejection geometry
retries remain open.

- [x] Verify the same frozen closet catalog flows through native electrical/gas capture and the
  owned stage's full prototype manifest, including unselected structural/light/entity alternatives.
  Reject wrong/missing catalogs and unsupported nested electrical overrides. Shared candidate-ID
  enumeration is reused across these capture paths; late mutable catalog recapture is forbidden.
- [x] Verify a substation-only mandatory core in the reserved closet and an APC-only mandatory
  core in an ordinary served room. Respect real floor/wall converter variants, opaque support,
  clean interaction approaches and same-tile cable terminals; no shared converter-home shortcut.
  Coherent optional supplies use the normal relational furnishing path and may not defeat access.
- [x] Verify MV service targets include declared protected/circulation cells, closet approaches,
  room entrance branches and explicit room service trunks where no corridor exists, plus all
  converter inputs. Route these cardinally under the same write masks and graph allowance;
  accepted candidates retain every service target separately from converter topology.
- [x] Verify transactional cardinal LV route growth against actual same-tier occupied cells.
  Check root initialization and every added branch for 2x2 blocks, including path-internal tiles.
  Do not infer necessity from a failed heuristic or exhausted budget. The initial supported
  certificate is four fixed mandatory APC terminal cells, bound to candidate-domain and terminal
  hashes. `ForbidNew` rejects even that exception. Other exact/unique-coverage proofs remain open.
- [x] Verify full usable-room-floor service by default, configurable fraction/domain and separate
  covered/uncovered floor cells. Exclude normally blocking/vault-required footprints only in the
  usable-floor domain. Actual required consumers remain independently required even at zero floor
  coverage; inspect real provider/reception ranges and eligibility, including supported wall roots.
- [x] Verify immutable service witnesses bind targets/domains/ranges/room reservations/proofs into
  plan identity, and live inspection rechecks coverage, physical cable occupancy, mandatory-terminal
  certificates, native provider ranges and closet enclosure/entrance. Native groups and actual
  receiver membership remain the existing independent checks. Wired-only never certifies supply.
- [x] Verify route detours, no fake necessity, forced versus forbidden blocks, exact restoration,
  one-less-work atomicity, proof-budget exhaustion, input freezing and diagonal-only rejection.
  Exercise actual owned generation/initialization for closet sizes/mount variants, default schema-two
  settings, optional electrical supplies, independent real consumers, shared quotas and provider loss.
  Eight new unit cases and twelve new integration cases pass. Native variants cover floor 2x2,
  wall 1x2 and wall 2x2 substations with separate wall APCs and supplies. Frozen project-area
  generation still uses captured coil supplies after a loaded pack is changed to paper; missing
  captured closet content rejects atomically. A required denied MV cell and sparse endpoint-only
  cable mask cannot erase service requirements.
- [x] Complete normal Release and fresh Debug retained unit/integration/sandbox/native serialization
  gates, targeted negative controls, artifact/source audits and detailed updated capability scoring.
  Each configuration passes **870/786/31**, retains every **862/774/31** baseline case and has an
  identical inventory with zero failures/skips. The actual sandbox and ten installed showcase map
  cases pass. One compile-valid combined control permits an unproved LV block, drops denied MV
  targets and zeroes both producer/auditor floor requirements: **1 unit + 7 integration assertion
  failures** result, with twelve unaffected passes. Exact restoration of three source files occurs
  in `finally`; all twenty new positives pass again in both final configurations.
  Evidence: `.ks_procgen_electricalservice_{release,final}_*`, `_final_audit.log`, `_control_audit.log`
  and `ks_electricalservice_{release,final}_{units,integration,engine}.trx`.
- [ ] Finish hull-boundary reuse and within-reservation joint device/route alternatives. Complete
  full-band reservation retries now handle later hard rejection in the 2026-10-25 batch above.
  Preserve the remaining scope; candidate-family failure is not proof of impossibility.
- [x] Replace the old open-room/sparse-wiring native showcase with a real schema-two 2x2 closet,
  coherent HV/LV coil supplies and separate wall APC. Export all five native grids and request/content
  documents, install matching resources, render real sprites and regenerate both overviews. Office:
  25 floors, 23/23 usable-floor LV coverage, 14 MV targets, 24 tier segments (1 HV / 18 MV / 5 LV),
  no LV 2x2 blocks. All five export/save/load cases pass; sprite/source/output hashes are audited.
  Hidden headless Edge checks all five images, both modes and all five layer controls in both states.
  Full-bright sprite composites remain static previews, not native client-rendering certification.
- [x] Rewrite the two obsolete authoring-only unsupported-generation assertions to require frozen
  native schema-two context. Missing/wrong shared closet content still rejects explicitly with
  `ElectricalUtilityRoomContextRequired`. Legacy schema-one identity/primitive behavior stays
  historical; the revised supported path no longer rejects all schema-two requests.
- [ ] Activate/migrate new authoring defaults after hull reuse and complete joint layout retries.
  Preserve explicit legacy schema-one reading/identities. Complete source-interface/operating supply
  and author UI/replay work under existing weights; this subset does not complete UI or production.
- [ ] Add exact-domain/unique-coverage necessity certificates beyond the four mandatory terminal
  case. Add typed utility-room/service overlays to general owned diagnostics/editor UI; the new
  showcase JSON/coverage overlay is review evidence, not the complete section 21 workspace.
- [x] Independently reconstruct cable/MV/LV/closet domains from captured layout and write inputs.
  The subsequent validation/routing batch above verifies omitted/substituted/duplicate/overclaimed
  lists, exact shared audit work and complete input freezing; immutable omissions cannot pass.
- [x] Replace repeated ancestor scans with intrinsically filtered shortest branches and an atomic
  complete-branch square check. Exhaustive 3x3 path-oracle tests and long thin routes verify the
  single-join guarantee. The earlier path-state concern did not reproduce; the square constraint
  admits the shortest-path proof recorded in source. Global route/APC/closet alternatives remain
  explicit pending tasks above, rather than unsubstantiated claims about the previous BFS.
- [ ] Make full-page overview screenshot regeneration a supported tooling step. The normal showcase
  generator updates individual sprite PNGs and HTML/SVG, but the two browser overview screenshots
  were refreshed separately in this batch; regeneration must not silently leave old overviews.

### Mandatory utility-room partition rewrite (2026-10-24, R09/R14/R18, verified structural subset)

This is the geometry prerequisite for the revised electrical pipeline, not completed substation
layout, furnishing, routing or native operating-access certification. The fixed assessment stays
65/100 at this structural checkpoint. The later owned electrical service batch above supersedes
the temporary schema-two guard for the supported standalone WiredOnly path; broader exits remain open.

- [x] Verify automatic per-network `KsProcgenUtilityRoomAllocator` carving before seam/material
  selection through `KsProcgenGroupPartitionPlanner`'s distribution input. A typed immutable room
  reservation carries exact 1x2/2x2 interior cells, enclosing band, threshold, approaches and frozen
  theme reference. Quarter-turn equivalents are supported; the door is in the enclosing band,
  outside the stated interior dimensions. No manually placed prefab or room-position preset.
- [x] Verify the existing partition solver reserves the enclosing walls and clean approaches,
  creates only the designated entrance, and refuses a merge/open fallback that loses a mandatory
  room. Ordinary repairs may merge ordinary rooms; utility reservations remain distinct. Failed
  or exhausted proposals expose no reusable floor/wall/door artifact. Downstream consumers must
  reject the new explicit required-room rejection status.
- [x] Verify utility selection and rejected connectivity/carving/retries share existing graph and
  seam budgets, preserve protected/read-only cells, do not join separate entrance networks, and
  bind reservation identity into the partition hash without changing legacy no-reservation hashes.
- [x] Verify theme assignment uses actual final floor adjacencies for open interfaces. A closed
  wall without a door must not imply a theme union. Revalidate the exact enclosure/entrance and
  require the matching frozen closet catalog; never silently select the ordinary room theme.
- [x] Verify dimensions/rotations, independent networks, canonical replay, complete-budget replay,
  one-less-budget atomic failures, tiny-domain fallback, protected bands and malformed reservations.
  Test real selected closet/ordinary theme packs and missing/wrong catalog or removed wall/door.
- [x] Materialize the selected geometry's wall/door prototypes on an owned test map and inspect
  native initialized anchoring, hard wall fixtures and airtight wall components, then clean it up.
  This deliberately proves structural primitives only: no powered door, machine, substation,
  furnished-closet accessibility, gas closure or electrical coverage receipt is implied.
- [x] Corrected-source Debug preflight passes **16 new unit + 8 new integration** cases,
  including three initialized native structural variants (1x2, axis-swapped 2x1, 2x2). Device
  rotation, machinery, powered entrances and furnished access are separate unfinished work.
  The first fixtures omitted the empty entrance-domain declaration that enables standalone roots;
  their null-reference failures were fixture errors, corrected without weakening production checks.
- [x] One compile-valid combined control removes protected-wall enforcement, the hard-room open
  fallback guard, reservation hash binding and the floor-only open-interface test. Exactly
  **4 unit + 1 integration assertion failures** result. All three source files restore byte-for-byte;
  `.ks_procgen_utilityrooms_controls_summary.log` and `_restoration.log` retain the witnesses.
- [x] Normal Release and fresh Debug (`--no-incremental`, ordinary localization/build hooks)
  each pass **862 units / 774 integration+sandbox / 31 engine serialization** cases, zero failures
  or skips. Exact inventories retain all previous **846/766/31** and match across configurations.
  Both runs include all 24 new positives, the actual sandbox check and ten existing native showcase
  map-load/lint cases. All 33 showcase + 6 review artifact hashes stay unchanged; no sprite renderer,
  sprite resource or preview is altered. Planning/review versions remain 10/3; no earlier accepted
  schema-two generation existed. The weighted assessment stays 65/100 because the full electrical
  path remains gated. Logs/TRX: `.ks_procgen_utilityrooms_{release,final}_*`, `_gates_summary.log`,
  `_final_audit.log` and `ks_utilityrooms_{release,final}_{integration,units,engine}.trx`.
- [x] Extend full-band placement to share writable generated-envelope hull, including small
  shapes where another interior wall band is wasteful. The generated-hull batch above verifies
  explicit corner/material ownership, native small layouts, budgets and atomic failures. The
  outside remainder remains connected; a candidate-family shortfall is not global infeasibility.
- [x] Retry complete reservations after later seam/content/device/route rejection, retaining shared
  work and independent network choices. The two later 2026-10-25 batches above verify this subset.
- [ ] Add inspected existing-hull reuse with immutable captured support/airtightness/ownership
  witnesses, preserved interfaces and host-context validation. Writable generated-envelope reuse
  does not satisfy this exit. Within-reservation pose and route portfolios remain separate R09/R14
  tasks; heuristic exhaustion cannot establish geometry or square necessity.
- [x] Carry the frozen closet catalog and all unselected native entity/component candidates through
  context capture, owned theme planning and project-area native generation. The subsequent owned
  electrical service batch verifies this exit without late mutable pack recapture.
- [x] Replace shared converter homes in schema two with a reserved-closet substation and a served-room
  wall APC. The subsequent service batch verifies normal relational supplies, MV target/LV coverage
  routing, the narrow mandatory-terminal proof and revised real-sprite native showcase. Both obsolete
  unsupported-generation test bodies are rewritten; legacy schema-one cases retain their identities.
- [ ] Complete typed utility diagnostics/editor overlays, inspected existing-hull reuse and within-reservation pose/route portfolios,
  further exact-domain block certificates and default migration under the service rewrite above.

### Electrical distribution schema prerequisite (2026-10-23, R08/R09/R12/R15/R18, verified authoring subset)

Historical prerequisite evidence: the temporary schema-two generation guard and its original test
bodies are superseded by the verified owned electrical service subset above.

This batch implements the authoring/transport prerequisite for section 20.3a; it does not claim
closet layout, generous MV routing, LV floor coverage or LV necessity certificates are realized.
The fixed assessment remains 65/100. Complete the joint layout/routing rewrites before enabling
the revised profile in the owned generator or replacing the historical wiring showcases.

- [x] Verify electrical schema two: immutable canonical 1x2/2x2 interior size sets, a closet room
  theme (which carries tile/wall/light/entity packs), circulation backbone and room-entry branch
  intent, bounded floor-service fraction/domain, and strict forbid-or-certify LV square policy.
  Default schema-two settings require both closet sizes, the electrical supplies theme, both MV
  service target classes, full usable-room-floor service and checkable LV-square necessity.
- [x] Verify explicit detached `KsProcgenElectricalProfiles.TryUpgrade` from schema one to two,
  including unchanged source data, changed migrated identity, idempotence and no invalid-input
  laundering. Raw schema one plus new distribution fields is rejected, not silently ignored.
- [x] Verify shared hashes, request snapshots, painted blobs, utility drafts and native engine
  serialization retain all distribution fields without mutable aliases. Preserve historical
  schema-one identities; schema-two policy must alter every containing declaration identity.
- [x] Verify project collection follows closet themes from inline/named area electrical settings,
  theme-selected profiles and unused draft profiles, including their packs/assemblies. Validate
  each closet catalog using captured document assets. Imports with missing/broken closet assets
  must fail atomically. Declaration capture is not entity-component or live utility certification.
- [x] Verify real loaded schema-two `KsProcgenClosetElectrical` and `KsProcgenElectricalCloset`
  examples, with coherent optional HV/MV/LV coil supplies. These are authoring examples; they do
  not yet place a converter, route wires or prove native access in a tiny closet.
- [x] Verify shared and server electrical-context capture reject schema-two distribution with
  `ElectricalUtilityRoomPlanningUnsupported` until joint layout/routing supports all hard intent.
  An accepted editable document must never silently fall through to general-purpose converter homes.
- [x] Preflight: corrected-source Debug build and all **21 new unit / 10 new integration** cases
  pass with no failures/skips. These cover migration/defaults/identity/copies/invalid imports,
  five project selection paths, three broken dependency cases, engine roundtrips and rejection.
  Logs/TRX use `.ks_procgen_distribution_preflight_*` / `ks_distribution_preflight_*.trx`.
- [x] One compile-valid combined negative control removes distribution hash binding, the
  area-electrical closet dependency enqueue and both legacy-planner rejection checks. It causes
  exactly **2 unit + 3 integration assertion failures**, covering policy identity, shared/server
  rejection and inline/loaded area dependency capture. Restore all four source files byte-for-byte;
  `.ks_procgen_distribution_controls_summary.log` and `_restoration.log` retain exact witnesses.
- [x] Normal Release and fresh Debug (`--no-incremental`, ordinary localization/build hooks)
  each pass **846 procgen units / 766 integration+sandbox / 31 native serialization** cases,
  zero failures/skips. Exact class/test-name inventories retain all previous **825/756/31**;
  configuration inventories match. Both include all **21 new unit + 10 new integration** positives,
  all ten existing showcase map-load/lint cases and the actual sandbox assembly check. Source/
  restoration/format audits pass, and all **33 showcase + 6 review artifact hashes** remain unchanged.
  No sprite renderer, sprite resource or preview bytes change. Existing planning/review semantics
  remain version 10/schema 3; adding electrical profile schema 2 does not certify revised generation.
  `.ks_procgen_distribution_gates_summary.log`, `_final_audit.log` and
  `ks_distribution_{release,final}_{integration,units,engine}.trx` retain evidence.
  The first Release integration attempt used an overly broad fixture-class filter and selected
  unrelated full construction/damage/map suites. Its verified owned test host was stopped; this
  aborted attempt is not a completed gate or evidence of a procgen failure. Retain it separately
  as `.ks_procgen_distribution_aborted_broad_integration.log` / matching TRX when present.
  Correct filter: `FullyQualifiedName~KsProcgen|FullyQualifiedName~SandboxTest|FullyQualifiedName~ProcgenShowcases`.
  The successful unchanged normal Release build was reused for the corrected scope; fresh Debug
  then rebuilt normally. `.ks_procgen_distribution_filter_correction.log` records the correction.
- [ ] Finish joint closet partitioning, separate APC placement, MV service backbone and LV
  coverage/avoidance/proofs; replace both temporary schema-two planning rejection checks.
  `KsProcgenOwnedInteriorPlanner` currently partitions before group theme/material selection,
  then asks `TryInfrastructure` for converters after structure. Allocate mandatory utility rooms
  before group theme/material/door/window selection, carry their designation through the normal
  partition/scene audit, and select their frozen closet catalog instead of the ordinary room theme.
  Keep the existing shared graph/probe quotas and whole-candidate rollback; do not label a rectangle
  after structure or append an independent closet generator. `TryCaptureElectricalContext` currently
  captures only ordinary-theme candidates plus converters/cables: extend that same content-manifest
  path with the frozen closet catalog and its real entity/component candidates before planning,
  including unselected pack alternatives. Authoring theme/pack capture is not this native manifest.
  Recheck the designated room's actual interior dimensions/enclosure/entrance after every seam
  rejection, open-interface union and merge fallback: never silently merge a mandatory closet into
  the served room. Keep ordinary minimum-room-size goals separate from the allowed two-cell closet.
- [ ] Activate schema two as the authoring default and migrate the existing built-in profiles,
  drafts/examples/showcase manifests through the same upgrade. The existing field default and
  built-in schema-one profiles remain historical primitive compatibility during this prerequisite;
  their general-room output still does not comply with section 20.3a. Do not call this rollout done.

### Supported-member review and projection bounds (2026-10-23, R15/R18, verified review subset)

This batch addresses the remaining supported-member review prerequisite. Owned planning semantics
advance to version 10 for a prerequisite bounded-search repair; owned review changes to schema 3. Version 9 /
diagnostic schema 2 evidence remains historical; the gates below verify planning 10 / review 3.
Weighted credit stays 65/100; offline review is not the section 21 in-game authoring workspace.

- [x] Implement complete supported member rows with source-qualified identity, floor/wall/surface/
  container layer, exposed request-tile positions, facing, support and coordinate parents, local
  transforms, slots and footprints. Hidden contents have null world positions and no exposed
  footprint; parent-local metadata never becomes a fictitious world placement.
- [x] Implement operating access and relational witness rows, preserving cardinal path order, exact
  approaches, associated seats, severities/states, measured distances and explicit partial-path
  flags. Existing floor projection remains a compatibility view.
- [x] Implement one diagnostic projection budget for all metadata rows and geometry collections.
  Count repeated cell occurrences across collections, reserve parent entries before projecting
  children, and never consume geometry for omitted metadata. Scalar points remain entry-bounded.
  Source processing has its own bounded counter; exhaustion returns empty non-candidate review
  data with explicit reason, no semantic receipt and retained generation work.
- [x] Implement SVG supported-member layers, cardinal access/relation paths and facing. Hidden
  contents are metadata only. Validate finite positions, rotations, layer/exposure consistency,
  cardinal paths and bounded detail input; escape all labels and attributes.
- [x] Add sixteen loaded-server cases for all four layers, real support/container membership,
  native wall APCs, chair access, whole-report geometry/input bounds, omitted-entry accounting,
  invalid SVG data, escaping, byte limits and canonical repeat output. Retain actual initialization,
  verification and complete cleanup. Export a populated supported-room JSON/SVG sample explicitly.
- [x] Diagnose the composite fixture omission: floor cells alone did not grant writable exterior
  backing walls for its required APC. The fixed probe supplied such walls and was not equivalent.
  Grant the intended hull envelope. Early missing-wall-domain rejection preserves infeasibility
  without exhausting unrelated branches. Keep the independent fixed-layout/search measurements
  separate from evidence about the corrected owned fixture.
- [x] Implement required root adjacency/facing pruning and anchor/required-link traversal to reduce
  unnecessary child enumeration. Every attempted pose retains its charge; preferred relations
  remain soft, and complete final validation still runs. Verify these optimizations independently.
- [x] Compile and correct the draft. All 31 focused cases pass, including sixteen new cases.
  The full Debug regression passes 756 integration/sandbox cases, retaining the previous 740.
  This preflight precedes negative controls and final normal configuration gates.
- [x] Run four compile-valid controls: per-collection geometry reset causes two assertion failures;
  disabling required-root pruning, inventing a hidden item world position and projecting omitted
  metadata each cause one. Restore three files byte for byte; all sixteen positives pass before
  and after. Evidence: `.ks_procgen_supportedreview_controls_summary.log`, restoration hashes and TRX.
- [x] Normal Release and fresh Debug (`--no-incremental`) each pass 825 units, 756 integration/
  sandbox cases and 31 serialization cases, with no failures or skips. Exact inventories match
  and retain every previous 825/740/31 case. Both actual sandbox runs pass. Evidence:
  `.ks_procgen_supportedreview_gates_summary.log` and `ks_supportedreview_{release,final}_{units,integration,engine}.trx`.
- [x] Export the populated `supported-room.json` / `.svg`, render `.png` using hidden headless
  Edge and visually inspect all exposed layer types, operating rings/paths and the separate
  contained-paper listing. The hidden item has no world glyph. Remove the isolated browser profile.
  Regenerate the L-shape review under schema 3 and five native map exports; native copies match.
  Preserve all six game-sprite PNGs and both renderers byte for byte, with current plan associations.
  Reconcile artifact/source hashes in `.ks_procgen_supportedreview_final_audit.log`.
- [ ] Remaining R15/R16 delivery: complete replay/context manifests, document sessions, persistence,
  editors and verified apply. Projection counters do not certify complete generator work metering;
  geometry normalization retains its existing request bounds before diagnostic projection starts.

### Preference evidence and bounded review rewrite (2026-10-23, R09/R18, verified review subset)

This batch completes the immediate preference-audit and light-lookup follow-ups below. Owned
semantic version 9 and owned diagnostic schema 2 now have complete configuration gates.
Version 8 evidence remains historical. Weighted credit stays 65/100; these
changes do not implement full supported-content UI, hermetic replay or operational publication.

- [x] Retain detached selected source variants in each composed room, including actual anchors.
  Copy nested member footprints/turn lists and source collections; audit structural values rather
  than relying on reference equality or assuming assembly/variant IDs identify unique content.
- [x] Implement independent captured-content validation: reconstruct the composed room, verify actual
  pose identity and captured authored variant membership, rebuild canonical automatic intent from
  captured goals/minima, and recompute distances/penalties and authored relation totals from the
  independently revalidated access witness. Forged or stale evidence rejects the whole candidate.
- [x] Bind automatic-policy enablement, selected anchor provenance, measured outcomes and score
  totals into owned semantic identity version 9. Freeze returned root-distance collections.
- [x] Implement owned diagnostic schema 2 with per-room preference totals/truncation and distance
  rows carrying both endpoints' pack/core/copy/variant/source-member identity. Explicitly label
  Manhattan anchor distance; never label it a walkable-path distance or engine-success receipt.
  New arrays share the existing entry allowance and bounded JSON byte limit.
- [x] Replace the final relational-path/light nested lookup with one room-local light-cell set.
- [x] Add eleven integration cases: eight forged evidence/source mutations, structurally cloned
  and detached provenance, canonical bounded diagnostics and long-path light obstruction among
  twelve unrelated proposed fixtures. Preserve initialization, verification and cleanup checks.
  Caller changes to member, footprint and rotation lists cannot alter retained provenance.
- [x] Compile and correct the implementation. Eleven new cases and retained pipeline coverage
  pass. Correct the lighting fixture to supply its explicit empty standalone entrance domain;
  its initial invalid-input result did not exercise the intended path guard.
- [x] Run three compile-valid controls: removing observed distance checks causes three assertion
  failures, removing captured-source membership causes one, and removing relational-path/light
  rejection causes one. Restore both source files byte for byte; all eleven positives pass before
  and after. Evidence: `.ks_procgen_prefaudit_controls_summary.log`, restoration hashes and TRX.
- [x] Normal Release and fresh Debug (`--no-incremental`) each pass 825 units, 740 integration/
  sandbox cases and 31 serialization cases, with no failures or skips. Exact inventories match
  and retain every previous 825/729/31 case. The actual sandbox passes in both. Evidence:
  `.ks_procgen_prefaudit_gates_summary.log` and `ks_prefaudit_{release,final}_{units,integration,engine}.trx`.
- [x] Regenerate the schema-2 JSON/SVG sample and all five native showcase exports/save/load
  cases. Both configurations retain ten standard native map cases. Verify installed copies,
  all 33 showcase artifacts, 40 sprite sources and six current PNG/plan associations. Sprite PNGs
  and both renderers are unchanged, preserving earlier browser evidence for both views and five
  toggles. `.ks_procgen_prefaudit_final_audit.log` records the reconciliation. Weighted credit
  remains 65/100; these changes repair existing furnishing/review guarantees.
- [x] Supported wall/surface/container review follows in planning 10 / diagnostic schema 3 above.
  Full author editing workflows remain R16; preference rows alone never completed that UI exit.

### Joint furnishing preference rewrite (2026-10-23, R09/R18, verified placement subset)

This batch addresses the first-legal-pose limitation identified below. Owned semantic version 8
has complete configuration gates and regenerated examples; version 7 evidence remains historical.
Weighted credit remains 65/100: this restores previously credited furnishing intent rather than
completing the broader authoring, runtime and publication exits. R09/R18 remain open.

- [x] Implement a shared preference allowance for one room search tree. It activates only after a
  complete hard witness exists and meters additional choice/variant, pose, graph and landing work
  across nested searches. Existing hard allowances still apply and every attempted branch retains
  its ordinary work counters. Invalid/unsupported declarations cannot use soft fallback.
- [x] Implement lexicographic scoring of missed authored preferences and Manhattan group-distance
  penalties. Preserve seeded traversal order for exact ties and retain immutable pose/orientation
  snapshots for the best fully validated witness. Soft exhaustion reports truncation with that
  witness; exhaustion before any hard witness still fails with no partial placement.
- [x] Implement canonical coherence intent from captured theme goals: initial authored groups prefer
  a four-tile Manhattan radius around the dominant anchor, and repeated floor groups prefer at
  least three tiles of separation. Infrastructure is excluded. Explicit relational assemblies
  remain whole; automatic distances never authorize diagonal movement or cross-room references.
- [x] Implement measured distance outcomes, separate soft-search truncation from failed optional
  content exploration, and preserve final density/required-content scheduling. Per-call soft work
  is capped by a bounded fraction of available work to retain room for subsequent validation.
- [x] Add thirteen integration cases for preferred-corner improvement, three exhaustion domains,
  immutable retained poses, no invented hard witness, whole-variant/core-choice improvement,
  truncated whole-alternative retention and primary-entry coherence on/off with real initialization.
  Fixed-pose fixtures additionally cover operating-landing improvement, soft landing exhaustion,
  exact chosen-landing revalidation and rejection of a forged unreachable landing. Two wrapper
  cases reject visited fractional spatial content even after a valid unsatisfied witness exists
  and before a later satisfiable variant/core could hide the unsupported declaration.
- [x] Implement preference search over legal operating landings, preserving the first fully checked
  witness before spending additional work. Independent audits recompute the exact chosen landing
  rather than silently selecting another one. Lighting reserves relational paths; final interior
  validation rejects a light which would block a retained relational path.
- [x] Verify the earlier root/variant/choice draft: 9 new cases and all 725 retained/current
  integration/sandbox cases pass. This evidence predates the operating-landing extension; it does
  not close the current compilation or configuration gates.
- [x] Compile the new operating-landing extension: 11/11 new cases pass. After correcting
  unsupported-content propagation in both core wrappers, 13/13 current cases pass. Full retained
  coverage and normal configuration gates pass below.
- [x] Run retained supported/core/owned pipeline coverage without weakening hard access, mounts,
  minima, atomicity or shared quotas. Every existing case is retained in both configurations.
- [x] Run three compile-valid negative controls: first-legal stopping causes one assertion
  failure, deleting budget-retained witnesses causes three, and ignoring unsupported core/choice
  status causes two. All three mutated source files are restored byte for byte; all thirteen
  positives pass before and after. Evidence: `.ks_procgen_preferences_controls_summary.log`,
  `.ks_procgen_preferences_controls_restoration.log` and the corresponding TRX files.
- [x] Run normal Release and fresh Debug (`--no-incremental`) gates: each passes 825 units,
  729 integration/sandbox cases and 31 native serialization cases, with no failures or skips.
  Exact test inventories match between configurations and retain all previous 825/716/31 cases.
  The actual client sandbox passes in both. Evidence: `.ks_procgen_preferences_gates_summary.log`,
  the release/final logs and `ks_preferences_{release,final}_{units,integration,engine}.trx`.
- [x] R18 preference evidence follow-up: version 9 exposes bounded source-qualified preference
  rows, binds policy/anchor/measurement identity, and independently audits captured declarations,
  actual poses and arithmetic. Full supported-member review and author workflows remain separate.
- [x] R09/R18 light-lookup follow-up: version 9 uses one room-local cell set. A long retained
  relation path remains valid among twelve unrelated proposed lights; an obstructing extra light
  rejects. A compile-valid control proves the guard is exercised.
- [x] Regenerate version 8 native/sprite showcases: all five real export/save/load cases pass;
  the installed native grids match their exports. Both configuration runs retain the ten standard
  map checks. All six sprite PNGs and both rendering tools are unchanged, preserving prior browser
  evidence for five images, two view modes and five toggles. Verify 40 source hashes, current
  plan/PNG associations and all 33 artifact hashes. `.ks_procgen_preferences_final_audit.log`
  records the reconciled evidence. No additional weighted capability point is claimed.

### Unified furnishing scheduler rewrite (2026-10-23, R09, in progress)

The preceding whole-choice primitive is now being used to replace conflicting owned furnishing
paths. This batch changes owned semantics v6 -> v7; v6 exports remain historical until regeneration.
Do not count implementation drafts as verified capability or close the broader R09 transaction.

- [x] Draft a supported scheduler for all captured ordinary packs, preserving required core copies,
  pack minima and infrastructure. Required tasks solve jointly before any optional additions.
- [x] Draft initial optional-core scheduling in dominant/supporting order and bounded density
  repeats. Each new instance re-solves the whole accepted room; genuine fit rejection omits the new
  complete instance. A failed optional budget retains its work and the previous valid witness,
  reports truncation and stops further optional exploration. Hard requirements never become optional.
- [x] Keep per-core copy identities distinct, including alternatives still available to a required
  task, and derive separate pack-local cluster ordinals for flat projections. Derive density from
  the union of exposed footprints per instance, excluding contained
  members and duplicate surface XY. Wall-only groups create no artificial floor-density demand.
- [x] Route captured ordinary packs through supported search with connected cardinal clean floor,
  protected approaches and inspected wall roots. Add a pre-allocation compiler guard against any
  wall-mounted furnishing that reaches the legacy flat spawn list.
- [x] Verify primary-entry ordinary wall packs, optional initial cores, density repeats, supporting
  dependency, zero density with hard minima, rejected additions, work bounds and deterministic replay.
  Fourteen new cases pass in the full 716-case Debug integration/sandbox run. Wall-only groups do
  not satisfy floor demand; mixed groups and supported children have separate density assertions.
- [x] Audit existing fixtures that inherited nonzero density but asserted exactly one supported
  instance while the old supported path ignored repeats. Give isolated single-instance fixtures
  explicit low/zero optional density; retain separate meaningful density/repetition assertions.
  The first full run recorded 25 failures/677 passes. Single-instance fixtures now state their
  density, and physics/rotation/count checks follow actual assembly members, retaining their
  collision, sweep, foreign-child, quarter-turn and allocation-budget assertions.
- [x] Inspect regressions and finish any prerequisite support-search fixes they reveal. Do not
  weaken mounting, clean access, minimum counts or shared work limits for passing tests.
  Focused testing passed 44/45 after fixture fixes; the remaining separated-islands case exposed
  the independent audit's old global wall expectation. Both now derive the same canonical
  room-local support-wall mask, preserving exact equality checks rather than accepting subsets.
  New envelope fixtures use a real hull palette; the earlier fixture without hull material was
  rejected correctly and its repeated failing run was stopped, not counted as verification.
- [x] Run three compile-valid negative controls and exact source restoration. Disabling ordinary
  supported dispatch fails all three native mount cases; disabling density repeats fails the high
  density case while the low density case passes; erasing the accepted witness after a failed
  optional addition fails both required-room retention cases. All six failures are assertions,
  with no skipped cases. Restore exact bytes, rebuild, and all fourteen positives pass again.
  Evidence: `.ks_procgen_unified_controls_summary.log`, corresponding build/test logs/TRXs and
  `.ks_procgen_unified_controls_restoration.log` (SHA-256 of restored sources).
- [x] Run normal Release/fresh Debug and actual sandbox gates, preserving the complete current
  825/716/31 inventories and distinguishing added coverage. No engine, operational or editor credit
  follows from closing a prerequisite regression gate alone. Both configurations pass exactly
  825/716/31 with zero failures/skips, actual sandbox and exact multiset retention of the fourteen
  new cases plus all preceding 825/702/31 cases. Localization hooks run normally, Debug starts with
  `--no-incremental`, and restored source SHA-256 values match before and after the gates.
  Evidence: `.ks_procgen_unified_gates_summary.log`, release/final build/test logs and
  `ks_unified_{release,final}_{units,integration,engine}.trx`.
- [x] Follow-up R09/R18 placement subset: version 8 adds bounded joint preference scoring,
  stable ties, whole-room backtracking, canonical coherence intent and best-witness retention.
  Thirteen new cases and complete configuration gates are recorded in the preceding batch.
  Version 9 completes the bounded preference review/audit and light-lookup follow-ups above.
  Full supported-member review and the broader R09/R18 exits stay open.
- [x] Regenerate v7 showcases and update artifact hashes, installed maps and detailed evidence.
  Five actual exports/save/load cases pass; ten standard map checks pass again after regeneration.
  Debug also passes 825 procgen units and 31 native serialization cases. Current-source tests retain
  all previous 825/702/31 cases. Normal Release/fresh Debug and negative controls now pass above.
  Evidence: `.ks_procgen_unified_full_debug.log`, `ks_unified_full_debug.trx`, draft unit/engine
  logs/TRXs, `.ks_procgen_unified_showcases_generate.log` and `ks_unified_regenerated_maps.trx`.

User-requested sprite previews (2026-10-23):

- [x] Render all five actual exported layouts with repository PNG/RSI assets, including the fork's
  floor variants, cardinal machine frames, native corner masks and exposed HV/MV/LV cable frames.
  Add a separate under-floor wiring image, source texture hashes and RSI license/attribution data.
  This is a curated static renderer for these examples, not a generic client rendering adapter.
- [x] Make game sprites the gallery default, retain diagnostic view/overlays and reject stale
  sprite plan/file hashes. Regeneration invokes the Windows System.Drawing renderer; other
  platforms can explicitly skip sprite previews. No generated substitute artwork is used.
- [x] Render and inspect `overview-sprites.png`; hidden headless Edge verifies five image loads,
  both modes and all five overlays in both states (`.ks_procgen_sprites_ui_dom.log`). Native lighting,
  shaders, animation and runtime power appearance are not certified; offline previews earn no
  in-game editor/UI credit. Current native files still retain the mandatory wall-mounted APC.

Weighted assessment remains 65/100. This batch repairs previously credited furnishing guarantees;
the explicit preference rewrite and broader R08-R18 acceptance exits remain open.

### Whole-core choice rewrite (2026-10-23, R09, in progress)

This prerequisite repairs selection of a complete furnishing alternative before ordinary flat
packs can be migrated to supported whole-room search. Alternatives retain their actual pack,
core, assembly, variant and copy identities; a rejected core cannot leave individual members in
the accepted room. It does not complete the legacy density scheduler or arbitrary wall-pack routing.

- [x] Implement bounded ordered choice search around the existing joint variant/pose solver.
  Charge every visited choice, including duplicate-prefix pruning, and retain failed branches'
  variant, placement, graph and landing work in the shared allowance. Budget exhaustion returns
  no partial composition or placement; it is not evidence that all alternatives are impossible.
- [x] Integrate whole-core alternatives into layered owned-room selection. Keep authored core
  minima and infrastructure instances fixed. Try complete weighted pack alternatives before
  dropping an optional instance or rejecting an unmet pack minimum. Preserve the existing direct
  solver and work accounting when every choice is a singleton.
- [x] Version owned interior semantics v5 -> v6 for the changed choice policy. Previously exported
  v5 showcases remain historical evidence until regeneration and validation under this version.
- [x] Add seven integration cases covering optional and mandatory pack fallback from an oversized
  nine-cell table assembly to a fitting table/microwave assembly, exact choice-budget exhaustion,
  charged duplicate-prefix pruning across two instance tasks, and invalid duplicate/mixed-pack
  choice declarations. The first positive run passes all seven; full validation follows below.
- [x] Verify all seven focused positives and two compile-valid negative controls. Forcing only
  the first whole alternative produces five assertion failures/two unaffected passes; making
  duplicate-prefix skips free produces two assertion failures/five unaffected passes. Restore exact
  source bytes and rebuild: all seven positives pass again. The optional-room test now asserts its
  witness exists before reading it, so lost fallback produces an assertion rather than a missing
  sequence-element exception. Evidence: `.ks_procgen_corechoices_controls_summary.log`, the positive,
  first-only, free-duplicates and restored build/test logs and matching TRX files.
- [x] Run normal Release and fresh Debug procgen/engine/sandbox regression gates, retaining every
  previous case. The initial namespace-only integration filter omitted 16 generic prototype cases;
  baseline comparison rejected that run despite its green tests. The corrected `Procgen` filter
  includes them and ten standard loadability/save-state cases for the five showcase maps. Release
  and fresh Debug each pass 825/702/31 with zero failures/skips and actual sandbox. All previous
  825/685/31 cases are retained with multiplicity; seven new choice cases and ten additional native
  map cases account for the integration increase. Normal localization hooks run, and the first
  Debug build uses `--no-incremental`. Evidence: `.ks_procgen_corechoices_gates_summary.log`,
  release/final build/test logs and `ks_corechoices_{release,final}_{units,integration,engine}.trx`.
- [x] Regenerate all five actual showcase exports under v6: five export/save/load cases pass.
  Complete JSON scene witnesses are unchanged except for plan hashes; all five SVGs match the
  preceding inspected diagrams byte for byte, retaining the existing overview and overlay evidence.
  Fresh native files contain new export metadata, so do not assume their older file hashes remain
  valid. Ten standard loadability/save-state checks pass again after regeneration, installed native
  copies match byte for byte, and the manifest inventories all 25 artifacts with current hashes.
  The office retains a solid-wall APC, floor substation, eight cables and native receiver receipt;
  supply and post-load topology remain uncertified. Evidence: `.ks_procgen_corechoices_showcases_generate.log`,
  `ks_showcases.trx`, `.ks_procgen_corechoices_regenerated_maps.log`, `ks_corechoices_regenerated_maps.trx`
  and `.ks_procgen_corechoices_final_audit.log`. No additional weighted capability credit is claimed.
- [ ] Continue R09 legacy furnishing migration: retain hard minima, complete initial optional
  cores and bounded density repeats while using joint supported access, actual wall roots and
  coherent whole-core fallback. The new choice primitive alone does not close this rewrite.
  - [ ] Solve required core copies, pack minima and infrastructure jointly before soft contents.
    Pack minima may choose a complete alternative; authored per-core minima cannot substitute away.
  - [ ] Try each initial optional core in dominant/supporting order, jointly re-solving all accepted
    instances when adding a new instance. On a genuine fit rejection discard only the new whole
    instance; budget exhaustion must retain its work and be distinguished from an impossible fit.
  - [ ] Preserve dominant-pack dependency, report whole-core omissions and schedule bounded density
    repeats from actual exposed footprints. Do not reduce ordinary furnishing to one core per pack
    or count surface/container children as additional floor occupancy.
  - [ ] Keep copy identities unique across retries; reserve clean door and interaction approaches,
    require connected cardinal clean floor and use native wall-root capabilities for ordinary packs.
    Flat helpers may remain for equivalent supported cases, with explicit parity tests.

Assessment remains 65/100 until the batch has verified evidence against the existing weighted exits.

### Converter wall mounting rewrite (2026-10-22, R09/R12, in progress)

User correction: APCs require wall mounting. Substations have separate floor and wall-mounted
prototypes (`SubstationBasic` and `SubstationWallBasic`). Native node connectivity alone does
not establish a legal mounting position. The preceding floor-only electrical demonstrations
and passing wiring tests did not cover this requirement and earn no additional capability credit.

- [x] Inspect inherited native prototypes: APCs and wall substations carry `WallMount`, floor
  substations do not. This component describes interaction occlusion exemptions; procgen uses
  the inspected declaration to select a wall-root placement contract for these structures.
- [x] Draft a general wall placement layer in the supported assembly solver and capability hash,
  preserving separate floor roots, support trees, clean approaches, cardinal facing and budgets.
- [x] Draft wall-cell converter reservations and same-tile wire routing. Solid support must be
  an actual retained wall, not a window, door, merely adjacent wall or inferred boundary.
- [x] Draft initialized electrical support inspection against owned wall entities and anchoring.
- [x] Verify floor substations, mandatory wall APCs and explicitly selected wall substations,
  wall-facing clean approaches, support loss, permission masks and missing walls. All 825 Debug
  procgen unit cases pass, including four new cardinal mount/approach cases and a mounted gas
  barrier case (`.ks_procgen_wallmount_units.log`, `ks_wallmount_units.trx`). All 37 focused
  electrical/capture/native cases pass (`.ks_procgen_wallmount_electrical.log`,
  `ks_wallmount_electrical.trx`). This includes actual paused node/provider construction and
  read-only repeat checks; no operating-supply or general publication claim follows.
- [x] Exercise a compact 1x2 mounted utility layout and complete floor/wall variant fallback.
  `.ks_procgen_wallmount_narrow_primary.log` / `ks_wallmount_narrow_primary.trx` pass: a real
  computer, wall APC and wall substation share the two-cell interior with clean operating access.
  A strongly weighted floor substation is replaced by a feasible complete wall variant. The
  explicit minimum cell policy is respected; all 825 unit cases remain green after the rewrite.
  A 1x2 integration fixture exposed an earlier R09 classification shortcut: shapes with no 2x2
  square became passage even when the caller explicitly allowed one-tile rooms. Rewrite this to
  respect the explicit minimum cell count and let actual support/approach/packing checks decide
  feasibility. Do not fabricate a larger footprint or weaken clean movement requirements.
- [x] Rewrite previous electrical success fixtures to provide real support walls; retain a
  separate no-support rejection test. Correct enlarged shape envelopes and include supporting
  wall cells in device permissions. The original preflight recorded 26 failures/3 passes;
  final-audit and fixture fixes restore all 37 focused cases without relaxing mount constraints.
- [x] Version prototype capability hashes v3 -> v4, electrical context v1 -> v2, electrical plan
  v1 -> v2 and owned interior semantics v4 -> v5 for explicit wall-root/mount intent.
- [x] Regenerate all five showcases from the actual planner; the wired office now contains a
  real wall-mounted APC and eight native HV/MV/LV cables. Its paused topology/receiver receipt and
  native save/load tile/prototype-count checks pass. All five grids are installed in resources.
  `.ks_procgen_wallmount_showcases_generate.log` / `ks_showcases.trx`; hidden headless Edge renders
  the new overview and verifies all five overlays in both states
  (`.ks_procgen_wallmount_ui_dom.log`). Post-load topology and operating supply are not certified.
- [ ] Rewrite structure/device joint alternatives: reserve required mount capacity before soft
  window choices exhaust the available solid walls; retry another legal window pattern or relax
  only a permitted soft window target. Hard window percentages, doors and preserved cells remain
  immutable. A previously chosen window pattern must not imply all legal mount layouts were searched.
- [x] Finish R09 wall content routing outside infrastructure: ordinary captured pack/core members
  now use supported search and a compiler guard rejects wall mounts reaching flat materialization.
  The 2026-10-23 unified batch verifies APCs, floor/wall substations and room-local wall ownership,
  retaining positive controls and complete configuration gates. Operational interaction arcs,
  soft preference parity and hosted/hybrid geometry remain separate unfinished exits.
- [x] Run restored configuration/sandbox gates before updating weighted credit. Three compile-valid
  negative controls detect lost bootstrap nodes (five assertion failures), bypassed wall support
  (two) and skipped final post-gas wiring inspection (one; two unaffected cases pass). Source bytes
  are restored and all 42 focused electrical/capture cases pass afterward. The first late-inspection
  control was ineffective because unanchoring a wire was rejected elsewhere; the strengthened test
  changes receiver range in the gas callback and requires `OwnedElectricalReceiptDrift` explicitly.
  Evidence: `.ks_procgen_wallmount_mutations_summary.log`, restored build/log and
  `ks_wallmount_mutations_restored.trx`. Final normal Release/fresh Debug gates pass 825/685/31
  each, retaining all prior cases and actual sandbox. This closes the supported native topology
  subset for one utility point; wider R08-R18 exits above remain open.

### Native owned wiring transaction (2026-10-21, R10/R12, in progress)

This batch connects the preceding reservations to the complete owned stage. The initial scope
is standalone AutoDistribute/WiredOnly, shared Main, no existing interfaces; operational source,
load settlement, visibility/global callback isolation and hosted networks remain separate exits.

- [x] Extend the frozen scene with per-tier cable intents and exact consumer/converter bindings.
- [x] Include utility prototypes in the pre-allocation captured manifest and image/spawn/work checks.
- [x] Draft a bounded native electrical inspector for same-tile converter cables, native tier
  groups, actual receiver/provider references and bidirectional owned membership.
- [x] Draft one shared initialization electrical allowance, cancellation checks and deferred
  receipt commit after lifecycle/scene/gas checks; repeated verification must only inspect.
- [x] Identify a prerequisite R08 loss: ordinary serialization intentionally omits input-only
  (`readOnly`) fields, including `NodeContainer.Nodes`. Earlier captured images silently lost
  those bootstrap declarations; an empty reconstructed container could roundtrip against an
  equally incomplete image. This also requires revisiting other input-only defaults.
- [x] Draft an explicit prototype-only writer/equality context opt-in, preserving ordinary map
  serialization, and bump captured prototype images to schema/salt 8. Do not special-case power
  containers or mutate the shared prototype manager to conceal the loss.
- [x] Verify ordinary/contextual writer and equality behavior, nested/include input-only fields:
  all 31 selected native serialization cases pass (eight new input-only cases plus the preceding
  23 flag/context cases); `.ks_procgen_nativewiring_bootstrap_engine.log` and
  `ks_nativewiring_bootstrap_engine.trx`. Captured node-instance verification remains below.
- [x] Finish captured node declaration/type/group preservation and independent spawned instances.
  Corrected Debug capture/native suite passes all 16 cases in
  `.ks_procgen_nativewiring_boundaries.log` / `ks_nativewiring_boundaries.trx`: five prototype
  kinds, converter-only and furnished stages, cancellation, exact/one-short shared initialization
  work, cable anchor drift, receiver/provider range drift, callback movement/unpause and repeats.
  The later wall-mount correction supersedes floor-only placement coverage; final gates remain open.
- [x] Retain observed provider transfer and receiver reception ranges in immutable connection
  evidence. A range change must invalidate evidence even if native reconnection picks the same
  provider and the receiver remains within reach.
- [ ] Retain the deliberate old guard replacement in final baseline comparison.
  Initial native retry passes four stage cases, including the rewritten obsolete allocation
  guard: `PrimaryStageRejectsUnrealizedElectricalCandidateBeforeAllocation` is deliberately
  replaced by `PrimaryStageMaterializesElectricalCandidateWithDeferredEvidence`. The guard is
  no longer the intended behavior; the replacement requires real initialization and deferred
  topology evidence. `.ks_procgen_nativewiring_bootstrap_stage.log` records four passes and five
  capture-fixture failures, which are not counted as successful gates. Correct the fixture LV
  ID and retain live node declarations until ordinary shutdown so queued nodes are removed.
- [x] Compile and correct native integration; actual primary-stage cables, converters, node groups
  and receiver/provider links pass the 42 restored focused cases, including solid-wall supports.
  Native topology evidence remains explicitly separate from operating electrical supply.
- [x] Verify rollback for group callbacks, cancellation, anchor/provider/range drift, exact shared
  work exhaustion and foreign pending queues; preserve read-only repeats and historical receipts.
  Combined gas/wiring callbacks cannot publish either receipt on final electrical drift, and the
  whole stage leaves a foreign grid's actual native group identity and pending reflood unchanged.
  Common complete-scene terrain inspection remains part of each electrical inspection.
- [x] Add three compile-valid negative controls and restored positive configuration/sandbox gates:
  eight assertion failures, two unaffected passes, exact source restoration, 42 restored focused
  cases, then normal Release and fresh Debug 825/685/31 each with full baseline retention.
- [x] Update docs/showcases after native checks: five native grid exports, mounted office topology,
  current diagrams and artifact inventory. Preserve explicit unsupported behavior for operational
  supply and unimplemented source/host contracts. No post-load or operating certificate is implied.

### Owned electrical candidate integration (2026-10-21, R09/R12, in progress)

This batch integrates detached electrical reservations into the primary complete owned planning
entry. It does not certify native node membership, provider assignment, operating supply, or
materialize utility scenes. No extra weighted capability credit is claimed before verification.

- [x] Draft typed authored/infrastructure member origins, so generated converter roles cannot
  collide with authored pack/core IDs or satisfy their required copy counts accidentally.
- [x] Draft mandatory converter cores in the same supported room search as furniture; constrain
  every converter footprint cell by the device and cable permissions during placement.
- [x] Draft complete candidate retries over converter home rooms, sharing existing layout,
  furnishing and graph allowances, without reusing failed placements or refunding work.
- [x] Draft HV beneath substations, MV beneath both converter classes and cardinal MV routes,
  LV beneath APCs and cardinal LV routes to proposed circular receiver coverage.
- [x] Draft request/area/room/member consumer binding, selector minima, deduplication of physical
  consumers, explicit required/preferred failures and nullable inspected/declared load values.
- [x] Carry speculative electrical output through the primary candidate, journal and semantic
  identity. Include cable/converter initial declarations in combined gas nomination.
- [x] Add a pre-allocation owned-stage rejection for electrical candidates until native utility
  realization exists. A geometric coverage proposal cannot claim actual power delivery.
- [x] Correct named-profile/inline-copy declaration comparison without dropping profile fields.
- [x] Seed supported whole-room access from actual partition door thresholds/approaches as well
  as existing protected cells. Do not fabricate host entrances or a synthetic operating root.
- [x] Reject statically impossible front landings before enumerating child furniture placements.
- [x] Add optional whole-clean-floor connectivity to the joint supported search and enable it for
  complete owned rooms. Charge its cardinal floods to the shared graph allowance; backtrack an
  isolating converter pose instead of accepting it and failing only in the final audit.
- [x] Compile and correct the first integration, then add independent primary-entry tests for
  converter placement, authored requirements, masks, cardinal tier graphs, exact reach,
  utility-room retries, selectors, combined gas, budgets and atomic failure.
- [x] Reject unsupported gas-domain electrical selectors and hard source requirements explicitly;
  do not broaden a scope or downgrade required operational supply to wired-only.
- [x] Run compile-valid mutation controls, restored Release and fresh Debug/sandbox gates;
  retain prior test multiplicities and record exact evidence below.
- [ ] Follow-up R08/R09: replace positional composed-member selectors (`region/Ixx_Mxx`) with
  a canonical binding path containing authored pack/core/copy/source-member identity. The current
  exact candidate selector is deterministic but adding another core can renumber it. Preserve
  candidate-specific selector behavior during migration; never bind a missing path to another member.
- [ ] Follow-up R09: separate converter origin cable permission from device footprint permission.
  The current joint permission set conservatively intersects both masks for every footprint cell;
  multi-tile converters should require cable permission only at actual same-tile terminals while
  still requiring device permission for their entire occupied footprint. Add a multi-tile fixture
  spanning a cable-forbidden neighbor and verify that its permitted terminal origin remains legal.
- [ ] Follow-up R09/R14: widen utility siting beyond one colocated converter pair per owned
  network and document/report strategy exhaustion separately from proven infeasibility. Current
  complete home-room retries are bounded heuristics, not a complete placement optimizer.
- [ ] Follow-up R12: materialize every planned cable and converter through the retained owned
  transaction, validate scoped native nodes, inspect actual provider membership, preserve
  read-only repeats and roll back callback/cancellation failures. Operational supply and host
  interfaces still need their own live evidence and isolation gates.

Preflight evidence (not the final gate): the initial compile exposed one inaccessible Shared helper;
local bounded server sorting corrected it. New tests then found the omitted context argument,
named-profile/inline-copy provenance mismatch, missing door access roots, and a converter placement
that isolated ordinary floor. These were corrected rather than bypassing the independent audit.
The first complete run passed 653/654 selected integration/sandbox cases; the sole miss was the
utility-room retry case above. After adding joint whole-room connectivity, focused units pass
820/820, including five added origin/permission/connectivity cases. The corrected complete
preflight passes 654/654 integration/sandbox cases with zero failures/skips, retaining the previous
635 cases plus nineteen new electrical scenarios. Final mutation/configuration gates are recorded below.
The obsolete extended run was explicitly stopped
after known failures and is not a completion receipt.

Final electrical-batch evidence: five compile-valid controls test omission of mandatory converters
(two assertion failures), collapsing authored/infrastructure identity (one), bypassing footprint
permission (one), bypassing whole-room connectivity (one), and weakening required operational
supply (one). Thirteen unaffected control cases pass. Exact bytes are restored in `finally`.
`.ks_procgen_electricalplan_gates_summary.log` records each build/test exit and restoration.
Restored Release and fresh Debug builds pass with zero errors and the normal localization hook;
each passes 820 focused units, 654 selected integration/sandbox cases and 23 native serializer
contracts with no failures/skips. `.ks_procgen_electricalplan_final_audit.log` verifies all previous
815/635/23 cases and all new preflight cases with multiplicity and the actual sandbox definition.
Build/test logs use `.ks_procgen_electricalplan_{release,final}_*.log`; case-level TRXs use
`ks_electricalplan_*`. The expanded-scope estimate remains 64/100.

### Small generated showcases (2026-10-21, user-requested, in progress)

- [x] Prepare a repeatable native-planner export fixture for a tiny strip, L footprint, supported
  kitchenette with hidden contents, initial-air room with windows and an automatic electrical plan.
- [x] Add an offline SVG/HTML renderer with furniture, approach, voltage, gas and coverage overlays;
  use actual exported positions and clearly label planning-only electrical output.
- [x] Generate the examples, save/reload each materialized native grid, compare floor tile counts
  and prototype multiplicities, and clean up all disposable maps.
- [x] Save requests, room-pack declarations, JSON diagnostics and importable native grids; render
  and visually inspect the gallery. Include a repeatable regeneration command and loading notes.
- [x] Record exact artifact checks and focused verification; do not include the showcase fixture
  in the preceding electrical batch's already completed 820/654/23 gate counts.

Showcase evidence: all five `KsProcgenShowcaseTests` export cases pass; four native grids save/load
with matching filled-tile counts and entity prototype multiplicities, then every disposable map is
removed. The complete expanded Debug selection passes 659 cases with zero failures/skips, retaining
all preceding 654 cases and the actual sandbox definition. `.ks_procgen_showcases_final_audit.log`
checks native resource-copy equality, SVG/XML, seed/shape counts, 36 actual filled gas tiles near
101.325 kPa, hidden container content, circular LV reach and the overview PNG. Headless Edge renders
the offline gallery for visual inspection and checks each of the five overlay controls in both
states (`data-procgen-ui-check="Passed"` in `.ks_procgen_showcases_ui_dom.log`). Browser startup needed
an approved launch outside the filesystem sandbox because Windows blocked its IPC initialization.

Artifacts: `Klovn/Docs/Examples/Procgen/Showcases/index.html`, individual SVG/JSON/request/content
files, four native `.yml` grids, `overview.png`, README and a SHA-256 inventory `manifest.json`.
Identical map copies are in `Resources/Maps/_KS14/ProcgenShowcases/` for `loadgrid`.
`Klovn/Tools/_KS14/Procgen/generate_showcases.ps1` regenerates through the real integration fixture;
`render_showcases.py` only renders that exported data. The wired office is explicitly planning-only.
These files do not certify operating power, actor interaction, stable simulation, publication or
complete frozen-resource replay. No further expanded-scope points are awarded for this inspector.


### Electrical authoring and capability foundation (2026-10-15, R08/R12)

This batch extends the utility schema groundwork before unified planning or runtime wire placement.
It does not complete R08: draft documents, scoped consumer assignments, request-wide hard utility
constraints and full routing policy still need their contracts. R12 has prototype inspection and
an exact geometric range predicate; its electrical routing/operation exits remain open.

- [x] Add version-1 `KsProcgenElectricalSpec`/`ksProcgenElectricalProfile`, disabled/existing/automatic
  modes, operational/wired-only requirements, per-tier cable prototypes, bounded weighted converter
  palettes, shared/independent named circuits and explicit source-interface declarations. Capacity
  and source state are author declarations until independently inspected; no source is invented.
- [x] Validate bounded identifiers, enum values, finite weights/capacities, unique palette/interface/
  circuit IDs, interface circuit membership and coordinates before snapshot expansion. Current
  authoring bounds are 64 choices per converter palette, 64 circuits, 1024 interfaces, 0-10 demand
  margin, 0-3600 startup ticks and 1-3600 observation ticks. An optional initial-charge fraction is
  in [0,1]; omission retains the chosen prototype's starting charge. These are configurable data,
  not proof of source sufficiency, legal writes or an operating load scenario.
- [x] Deep-copy converter choices/interfaces/circuits into immutable records and canonically sorted
  lists; snapshot identity includes every supported field and source profile ID, with signed zero
  normalized. Every `CopySpec`/blob `CreateRequest` produces independent editable nested values.
- [x] Carry `electricalProfile` references and inline `electrical` overrides through requests, area
  profiles and inherited room themes. Bump area semantic snapshot to v7 and area-review JSON to
  schema 3; preserve atmosphere data. Owned geometry report JSON/SVG remains schema 1. Existing
  requests without an electrical declaration retain their supported behavior.
- [x] Add automatic, wired-only, validate-existing and disabled example profiles, plus opt-in powered
  office area/theme examples. Both primary content planners and per-room theme selection reject
  explicit electrical contracts with `ElectricalPlanningUnsupported` until R09/R12 retain them.
  Even an explicit disabled profile is gated so a declared policy cannot disappear from plan identity.
- [x] Add server-only read-only inherited prototype inspection for real cable types, matching
  cardinal cable/device node groups, enabled ordinary device ports, compatible charger/discharger/
  provider connectors, LV provider range, batteries, charge/supply/ramp/efficiency and APC breakers.
  Reject wrong-tier palettes or converter roles without partial reports. Terminal ports are not
  treated as ordinary same-tile inputs. Mounting/collision and live membership remain unverified.
- [x] Add `KsProcgenElectricalReach.WithinRange` using the same inclusive float Euclidean `Length`
  comparison as `ExtensionCableSystem`, with effective minimum provider/receiver range. It checks
  geometry only; it cannot establish same-grid eligibility, provider selection or operating power.
- [x] Verify Release, fresh Debug, focused regressions and the actual client sandbox; prove tests
  reject deliberately broken range, snapshot transport and capability checks, then restore sources.
  - Restored-source Server Release passes with 512 existing permitted warnings and zero errors.
    Sequential fresh Debug integration `--no-incremental --no-restore` passes with 1107 existing
    warnings and zero errors. No warning suppression or sandbox whitelist changes were added.
  - All 578 focused procgen unit cases and 192 selected loaded/integration cases pass with zero
    failures/skips, including `Content.IntegrationTests.Tests.Utility.SandboxTest.Test`. New coverage
    comprises 30 unit and eight loaded cases: nested immutable copies, canonical identity, malformed
    bounds/circuits/interfaces, precise LV range, real inherited cable/converter components,
    serialization, painted snapshots/JSON and unsupported primary-planner refusal.
  - Deliberately replacing inclusive reach with strict reach makes the 29-cell assertion observe
    25 cells; removing electrical request transport fails its non-null assertion. Mapping the HV
    inspector to MV cable/group semantics makes the wrong-prototype fixture observe `True` where
    `False` is required. All three protected assertions fail. Source bytes are restored in `finally`
    and checked before the final restored-source gates above. Mutations are not retained.
  - Logs: `.ks_procgen_electrical_final_release.log`, `.ks_procgen_electrical_final_debug.log`,
    `.ks_procgen_electrical_final_units.log`, `.ks_procgen_electrical_final_integration.log`,
    `.ks_procgen_electrical_mutation_units.log` and `.ks_procgen_electrical_mutation_loaded.log`.
    TRX retains case-level outcomes. Scoped diff/new-file whitespace checks pass. All four private
    KS modules remain clean and 0/0 against locally recorded `origin/main`; no fetch was performed.
  - This is focused evidence, not a full repository-suite run or actual utility generation proof.
    The expanded-scope completion estimate remains unscored; no percentage is awarded for tests.
- [ ] Add unified ownership/utility reservations and canonical request-wide utility/content identity
  under R09/R15, electrical route generation and source/load scenarios under R12, then the owned
  lifecycle and runtime topology/supply certificates. Do not remove existing refusal guards early.

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
- [ ] **T13 - Implement final engine validation and publication.** Depends on T07/T10-T12 and R10-R13/R17. Reconcile
  actual collision, operational doors, anchoring, and atmosphere with the plan, then publish once.
  Exit: A08-A10/A13-A14/A19/A24 pass against materialized entities; a visually correct but leaking or
  inaccessible result never receives success/publication.

### Phase E: degradation, author tools, and release

- [ ] **T14 - Complete ordered fallbacks and diagnostic reporting.** Planning follows T06-T09;
  materialization failure reporting completes with T10-T13. Named
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
- [ ] **T15a - Implement lighting packs and supply/coverage validation.** Plan after T09a; complete
  validation with R12-R13 and T13-T14 (a joint exit, not a circular scheduling dependency).
  Fixture selection, spacing/support, working-state checks, bounded coverage estimates, and explicit
  validation of any hard lighting metric. Exit: A29-A30 pass; no unpowered light is counted as
  working. A simple self-contained theme remains available, and a network-powered theme requests
  generated distribution through section 20 instead of assuming external supply.
  - Implemented: deterministic, bounded fixture proposals using the selected lighting pack;
    geometric floor-graph coverage estimates; exclusion of protected passages, furniture, and
    reserved interaction approaches; conservative clean-floor connectivity preservation for
    solid floor fixtures; request-wide fixture count, sparse/budget statuses, and
    semantic replay hashing. The planner never reports verified working coverage.
  - Remaining: inspect mounting/collision and actual supply at placement, measure engine light
    coverage after all entities spawn, validate hard targets, and exercise A29-A30 in live maps.
- [ ] **T15 - Implement coherent entity-pack furnishing.** Planning follows T04a/T09a and R02/R09;
  final operational/lighting exits are verified jointly with T13/T14/T15a. Dominant
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
- [ ] **T16 - Add preview, validation, replay, and overlays.** Depends on T14/T15a/T15 and R15-R16. Expose sections 15/21's
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
- [ ] **T19 - Run release validation and publish examples.** Depends on T01-T18 and R08-R18. Run the full matrix,
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
R01-R18 are mandatory amendments to that core for this revision; historical T completion or partial
geometry tests do not waive them. T19 also ships the combined A52 example and verifies A53-A66's
gas, power and editor workflows. Historical dependency cycles are resolved by R18's staged exits.

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
| Atmosphere | Explicit reusable gas profile; generic requests preserve/leave unspecified gas, station presets select ordinary air. No implicit life-support guarantee. |
| Electricity | Explicit profile; station preset uses automatic distribution from declared interfaces. No implicit generator or infinite battery. |
| LV coverage | Engine Euclidean distance, inclusive minimum of provider/receiver ranges; current defaults 3 world units (3 standard tiles). |
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
| Authoring | In-game draft workspace with visual pack/assembly/connection/utility editors; authoritative server validation before apply. |
| Tactical objectives | Optional, measured heuristics; never override required movement or pressure constraints. |

## 19. Atmosphere initialization and gas profiles

### 19.1 Scope and verified engine behavior

Generate an explicitly chosen **initial atmosphere** in each assigned room/gas domain. Permit air,
single-gas rooms, arbitrary supported mixtures, unusual temperatures/pressures and deliberate vacuum.
Do not equate "spaceproof," "has gas," and "breathable for this actor." Automatic vent/scrubber/pipe
networks and continuous life support are separate future features; initial filling is required now.

Repository facts inspected on 2026-10-15:

| Source | Consequence for the adapter |
| --- | --- |
| [GasMixture](../../Content.Shared/Atmos/GasMixture.cs), [Atmospherics](../../Content.Shared/Atmos/Atmospherics.cs) | `Pressure = TotalMoles * R * Temperature / Volume`; units are kPa, mol, K and liters. `R = 8.314462618`; standard pressure is 101.325 kPa, standard temperature 293.15 K, standard cell volume 2500 L. |
| [AtmosphereSystem.Utils](../../Content.Server/Atmos/EntitySystems/AtmosphereSystem.Utils.cs), `GetVolumeForTiles` | Current engine volume is `CellVolume * grid.TileSize * tileCount`. Read actual tile-mixture volumes; do not substitute a guessed cubic scaling law. Existing KS room staging supports unit tiles only. |
| [AtmosphereSystem.Processing](../../Content.Server/Atmos/EntitySystems/AtmosphereSystem.Processing.cs), `UpdateTileAir` | Fully blocked tiles with `NoAirWhenBlocked` have no air; map-atmosphere tiles use immutable mixtures. A drawn floor cell is not sufficient evidence of a writable gas volume. |
| [AtmosphereSystem.API](../../Content.Server/Atmos/EntitySystems/AtmosphereSystem.API.cs), `GetTileMixture` | May return a map mixture or immutable space fallback when a grid tile is missing. A non-null return is not evidence of an owned room mixture. |
| [AtmosphereSystem.Commands](../../Content.Server/Atmos/EntitySystems/AtmosphereSystem.Commands.cs), `RebuildGridAtmosphere` | Resets an entire grid to marker-selected roundstart mixes, rebuilding tile/group state. Do not use this admin repair command to fill one room or preserve neighboring atmospheres. |

Gas filling must use an owned grid-tile adapter with proper invalidation/activation, not global map
atmosphere replacement, copied admin command logic, a permanent gas spawner, or repeated per-tick
refilling. Resolve supported gas identifiers through the engine's current gas definitions. Never
serialize a positional mole array whose meaning changes when gases are added or reordered.

### 19.2 Authoring contract

Reusable `ksProcgenAtmosphereProfile` definitions implement the version-one authoring fields below.
The owned-stage path now implements InitialOnly filling/verification on closed private grids;
hosted/preserved context and stable-at-rest processing remain open. See section 17 for exact gates.

| Field | Contract |
| --- | --- |
| `mode` | `Preserve`, `Fill`, or `Vacuum`. Omitted generic profile means preserve existing gas and leave newly created gas unspecified in the result; it does not silently mean air or vacuum. |
| `temperatureKelvin` | Required for `Fill`/`Vacuum`; finite and inside the engine-supported range. Reject values that the engine would silently clamp. |
| `pressureKPa` | Finite positive total pressure for `Fill`; `Vacuum` specifies zero and no gas fractions. |
| `moleFractions` | Gas ID to fraction; nonnegative finite values summing to 1 within `1e-6`. Normalize only within that rounding tolerance; otherwise reject. Unlisted gases receive zero moles in a newly filled tile. A single gas uses fraction 1. |
| `requirement` | `Required` by default for an explicitly assigned fill/vacuum; optional decorative atmosphere may be `Preferred`. No fallback silently substitutes ordinary air. |
| `stateRequirement` | `InitialOnly` by default; `StableAtRest` additionally validates the declared bounded observation interval after initialization. Neither promises survival during arbitrary later gameplay. |
| `tolerances` | Defaults: pressure 0.1 kPa absolute, temperature 0.1 K absolute, each fraction 0.001 absolute. Vacuum checks pressure only; zero-mole fractions are undefined. Explicit overrides are finite/nonnegative and retained in the report. |
| `observationTicks` | Required finite, positive bound for `StableAtRest`; also record simulation tick duration, door states and active devices. No "wait until stable" without a work/time bound. |

Resolve one profile per assignment: explicit room/domain instance, then area profile, then request
default, then resolved room theme. Request-hard restrictions constrain every level. Conflicting
assignments of equal specificity fail with both IDs; declaration order does not choose a winner.
Inheritance replaces the mixture as a whole, not gas-by-gas merging that can retain unwanted gases.
Request schema 2 implements optional `utilities` (nested schema 1) as detached intent. Its
`gasAssignments` entries contain unique `id`, `scope` (`Request`, `Area`, `Room`, `GasDomain`),
optional `scopeId`, and `profileId` and/or a complete inline `profile`. `Request` has no target ID;
`Area` defaults to this request's ID; room/domain targets require stable planned IDs. At capture,
equal declared scope/target assignments must agree in every gas-state and requirement field;
source aliases alone may differ. Binding later determines overlapping room/domain assignments,
which share instance specificity and must not gain precedence merely from declaration order.
Loaded/draft references are pinned before transport. Capture does not prove the target exists.

The candidate gas integration under R09/R11 is in progress. The server planning entry captures
initial airtight/anchoring defaults from independent retained prototype images before choosing
a layout. The shared owned entry accepts this immutable gas planning context explicitly; legacy
entries without it retain their unsupported guards. Bindings belong to the complete candidate
transaction and cannot survive a discarded configuration or subdivision. Room scope IDs are the
existing stable themed-region IDs. Proposed gas-domain IDs use `Gas/<x>/<y>`, where `(x,y)` is
the first gas-volume cell in ascending Y, then X order, with invariant integer formatting. These
IDs describe the proposed topology; changing a separator can change the domain IDs and invalidate
an explicit assignment. The editor must expose these IDs and any resulting target failures.

Cardinal gas connectivity uses the nominated initial directional airtight defaults on both sides
of each edge. Multiple anchored blockers combine their directions. Fully blocked cells with a
no-air contributor have no proposed gas volume. Surface and container placements contribute no
anchored gas barrier. Callbacks, actual tile mixtures, exterior closure and later door/device states
still require engine inspection. An initial topology is not a closure or stable-at-rest certificate.
The owned scene allocator now accepts supported InitialOnly gas plans. Initialization revalidates
only the owned private grid and its immediate halo, reconciles actual gas-bearing cells and edges,
and rejects split domains or required fills with leaking boundaries before any explicit fill.
An optional fill with an open boundary is omitted for the entire domain, with an explicit
`PreferredGasDomainLeaks` outcome; it must not write gas into an unclosed volume. An optional
mixture that engine arithmetic cannot represent is likewise omitted for its entire domain
(`PreferredGasMixtureUnrepresentable`), without substituting an ordinary-air default.
It writes independent tile mixtures
using actual engine volume, activates/invalidate visuals through native hooks, and notifies devices
when their mixture reference changes. Actual pressure, temperature, fractions and request-hard gas
restrictions must pass before the shared lifecycle can commit a receipt. Unmet preferred initial
values produce `PreferredGasInitialStateUnmet`; request-hard limits still reject the transaction
even when a preferred fill was omitted. The receipt records its simulation tick/time and immutable,
deduplicated binding/runtime omissions. `RequiredInitialGasVerified` certifies the hard initial
conditions; `InitialGasVerified` additionally requires all preferred initial values to match.
An unsupported preferred stability interval produces `PreferredGasStabilityUnsupported` and does
not invalidate otherwise matched initial values. `AtmosphereAtRestVerified` remains false.
These are initial-state observations, not later-gameplay or publication certificates.
Repeated initialization and `Verify` inspect current state without creating or refilling mixtures.
Later optional drift within hard bounds is allowed and does not rewrite the historical receipt;
required drift or a hard-bound violation rejects verification. Callers must not treat an old receipt
as a fresh observation of the current simulation tick.
Callback corruption, grid movement, ownership loss or cancellation retire the transaction and cannot
commit its pending receipt. Required StableAtRest declarations and request-hard minimum observation
intervals remain explicitly unsupported in this subset; optional stability is reported as unmet.
Gas planning also rejects hosted entrances, constant regions and preserved cells until inspected
gas-transfer context is available for them; generic empty exterior facts cannot replace that context.

`gasRestrictions` retains optional `allowedModes`, `allowedGases`, `minimumPressureKPa`,
`maximumPressureKPa`, `minimumTemperatureKelvin`, `maximumTemperatureKelvin`, and
`minimumObservationTicks`. These are request-hard bounds, never defaults to merge into a profile.
After effective-profile resolution, every candidate must satisfy them or fail with the relevant
assignment and bound. Preserve mode requires inspected existing state rather than fabricated
pressure/fraction evidence. Candidate binding now checks nominal hard bounds and rejects unverified
preserve-state bounds. The supported owned initialization path checks actual pressure/fractions
and hard bounds after writing and on repeat verification. Observation-interval evidence and hosted/
preserved-state compatibility remain R11 obligations.
`gasWriteCells`, `cableWriteCells` and `deviceWriteCells` are separate optional narrowing masks:
null inherits normalized request write permission; an empty mask forbids all writes on its layer.
They cannot reclaim constants, preserved cells, voids or cells outside the owned area/envelope.

Preserved cells always take precedence over fill writes. A station preset SHOULD select ordinary
air (21% oxygen, 79% nitrogen, 101.325 kPa, 293.15 K); a generic request does not assume that preset.

The following profile loads through R08, binds through the owned planner, and initializes supported
closed owned rooms through the R11 adapter. It does not enable hosted or stable-at-rest generation:

```yaml
- type: ksProcgenAtmosphereProfile
  id: KsProcgenNitrogenRoom
  mode: Fill
  temperatureKelvin: 293.15
  pressureKPa: 101.325
  moleFractions:
    Nitrogen: 1
  requirement: Required
  stateRequirement: InitialOnly
```

The UI offers gas percentages for convenience and converts them to canonical fractions. Fractions
are mole fractions, not mass percentages. Human/species suitability is an optional separately
validated actor requirement; a deliberately authored nitrogen room must not be rejected merely
because it is unsuitable for an ordinary human.

### 19.3 Domains, ownership and mixing

Build gas domains from actual gas-transfer edges in the nominated resting door state. Movement
groups, room labels, marker channels and electrical circuits do not imply gas isolation. Include
door tiles that really hold gas; exclude null-air structure, void and immutable map atmospheres.
Every writable gas-bearing target tile receives exactly one assignment or an explicit unspecified
disposition. Threshold tiles use the parent domain's default unless explicitly assigned; conflicts
must be resolved before filling, without a gap of accidental vacuum under a door.

Rooms connected by an open passage share a gas domain. Compatible identical profiles can merge.
Conflicting hard fills in that domain reject the layout, even under `InitialOnly`: do not claim two
distinct room atmospheres that immediately mix. Try a permitted sealed partition/door arrangement,
another room selection or an explicitly authored common profile. Never invent a blocking wall
across a required route. Closed doors can separate initial mixtures; report that opening them
mixes gas. Traversal isolation does not guarantee atmosphere isolation, or vice versa.

Existing preserved gas is read-only context, including mixtures inside constant regions. Filling
an adjacent connected domain may not silently alter that context: match a verified compatible
state or require a sealed interface. Unknown preserved gas prevents a hard compatible-state claim.
No scalar "preserve room" flag grants permission to refill it. Per-island requests validate each
independent pressure domain, including disconnected blobs.

### 19.4 Initialization, verification and failure

1. Resolve profiles before layout search; reserve any required closure/separation. Reject unknown
   gases, invalid numbers, contradictory profiles and unsupported grid scales early.
2. Materialize floors, walls, windows and nominated door states in the owned stage. Complete tile
   and airtight revalidation through a supported lifecycle. Snapshot actual gas-bearing tiles and
   their volumes; reject missing, immutable, shared-with-context or unexpected mixture references.
3. For each writable tile of volume `V`, compute `n = pressureKPa * V / (R * temperatureKelvin)`;
   assign gas `i` the amount `n * fraction[i]`. Validate intermediate results for overflow/nonfinite
   values. Do not put a whole room's mole total into every tile. `Vacuum` clears moles without
   deleting the tile atmosphere. Preserve the engine-owned volume and independent mixture identity.
4. Apply the initial state exactly once via engine-supported mutation, preserving device references,
   archived-state rules and activation/overlay notifications. If no safe scoped API exists, add an
   engine adapter with tests; do not mutate private arrays/dictionaries or bypass access restrictions.
5. Read back every required tile's gas amounts, pressure and temperature; independently check gas
   closure under section 10. For `StableAtRest`, observe owned simulation for the declared interval,
   checking reactions, active devices, thermal changes and leakage against tolerances. Unsupported
   owned simulation leaves stability `Unverified`; a paused stage is not evidence of stability.
6. Report per-domain totals and worst per-tile deviations, untouched-context fingerprints, nominated
   door states, observation interval and separate `GasClosureVerified`/`InitialGasVerified`/
   `AtmosphereAtRestVerified` evidence. Planned mixture math cannot set runtime flags.

Initial filling may create authored reactive gas; report subsequent reactions honestly rather than
continuously restoring the starting mix. `InitialOnly` verification is taken at the defined
post-fill point before normal reaction processing, and says so. A required stable mixture that
reacts away within its observation interval fails that requirement.

Tiny shapes still follow the normal contract: 1x3 enclosed by a valid host can receive gas; a fully
solid strip has no gas-bearing interior and cannot satisfy a required room atmosphere. On failure
retry permitted structure/profile alternatives, omit a preferred fill with a report, or fail. Only
an explicitly named relaxation may drop a hard gas requirement. Rollback discards owned gas state
alongside geometry and entities. No failed attempt changes a live neighbor or global map mixture.

## 20. Automatic electrical distribution

### 20.1 Verified topology and exact LV range

The normal generated chain is:

```text
declared HV source -> HV cable -> substation input
    substation output -> MV cable -> APC input
        APC output -> LV cable providers -> eligible nearby receivers
```

Substations/APCs are battery-backed devices with limits and operating states; the engine does not
model them as lossless abstract voltage labels. Both cable layers must occupy the device's tile:
HV + MV under a substation, MV + LV under an APC. Mere cardinal adjacency to a device is insufficient.
Cable-to-cable adjacency is same-layer and cardinal (or same tile), never diagonal. Distinct layers
may share a tile without shorting; device behavior transfers energy between its distinct networks.

Verified source references (2026-10-15):

| Source | Behavior |
| --- | --- |
| [CableNode](../../Content.Server/Power/Nodes/CableNode.cs), [CableDeviceNode](../../Content.Server/Power/Nodes/CableDeviceNode.cs), [NodeGroupSystem](../../Content.Server/NodeContainer/EntitySystems/NodeGroupSystem.cs) | Anchored grid nodes; cardinal cable neighbors, same-tile cable-device links, matching node group IDs. Cable terminals can block a direction; simple Manhattan adjacency is insufficient around terminals. |
| [Substation prototypes](../../Resources/Prototypes/Entities/Structures/Power/substation.yml) | `input: HVPower`, `output: MVPower`; battery charger/discharger and supply/ramp limits. |
| [APC prototypes](../../Resources/Prototypes/Entities/Structures/Power/apc.yml), [ApcComponent](../../Content.Server/Power/Components/ApcComponent.cs) | `input: MVPower`, `output: Apc`; battery, main breaker and overload/trip state. APCs also have extension-provider capability themselves. |
| [Cable prototypes](../../Resources/Prototypes/Entities/Structures/Power/cables.yml) | `CableHV`, `CableMV`, `CableApcExtension` use `HVPower`, `MVPower`, `Apc`. LV has `PowerProvider` plus `ExtensionCableProvider`, without a range override. |
| [ExtensionCableProviderComponent](../../Content.Server/Power/Components/ExtensionCableProviderComponent.cs), [ExtensionCableReceiverComponent](../../Content.Server/Power/Components/ExtensionCableReceiverComponent.cs) | Both range defaults are **3**, not 2. Inherited/component overrides must be inspected. |
| [ExtensionCableSystem](../../Content.Server/Power/EntitySystems/ExtensionCableSystem.cs) | Same-grid anchored-cell search, followed by inclusive Euclidean distance between local entity positions, limited by `min(provider.TransferRange, receiver.ReceptionRange)`. No raycast or wall-occlusion check. |
| [PowerReceiverSystem](../../Content.Server/Power/EntitySystems/PowerReceiverSystem.cs), [PowerNetSystem](../../Content.Server/Power/EntitySystems/PowerNetSystem.cs) | Provider membership and actual received power determine operation; coverage alone does not establish power. Disabled receivers, unmet load and batteries require separate state checks. |

For standard tile-centered entities with both ranges at their default, coverage is
`dx*dx + dy*dy <= 9`: `(3,0)` and `(2,2)` are covered; `(3,1)` is not. This is a circle of radius
**3 world units**, equivalent to 3 tiles on the supported unit grid, including 29 tile centers.
It is not a square, Manhattan radius, flood along a walkway, or a radius around the APC covering
the whole LV network. Each cable/provider supplies its own local reach. Diagonal *coverage* is
valid; diagonal *wire connections* are not. Fractional device positions use actual positions.

Only eligible receivers can connect. The search enumerates anchored entities on the same grid;
receiver connection also depends on its connectable/physics/anchoring lifecycle. Contained items
or a loose tabletop machine are not automatically eligible. Proximity to LV cannot power an
arbitrary entity that lacks the relevant receiver. This must be checked for relational devices.

Provider assignment is stateful: existing links are retained; provider startup fills unassigned
receivers, while receiver discovery chooses the nearest candidate before checking its effective
range. A nearer short-range or unpowered provider can prevent the intended connection. Equal
distances and startup order are not a guaranteed circuit-selection policy. Verify the actual
provider/network and supply after initialization; do not assume nearest powered cable wins.
If disjoint circuits are required, walls do not isolate coverage. Avoid overlapping provider
influence or establish a supported assignment mechanism; inspect preserved providers too.

### 20.2 Electrical profile and interfaces

Add `ksProcgenElectricalProfile`, referenced at request/area/theme level with section 19.2's
precedence and hard-constraint rules. Explicit fields:

| Field | Contract |
| --- | --- |
| `mode` | `Disabled`, `ValidateExisting`, or `AutoDistribute`. Generic omission is disabled; station presets select automatic distribution. Disabled does not waive required operating lights/machines. |
| `cablePrototypes` | Validated HV/MV/LV prototype references with matching node and provider capabilities; defaults are the three standard IDs above. |
| `substationPrototypes`, `apcPrototypes` | Weighted eligible concrete device variants; inspect mounting, input/output groups, footprint, battery and load limits. Do not infer behavior from display names. |
| `interfaces` | Stable interface ID, grid-local cell, voltage, source/network identity, declared available watts, operating state and permitted connection action. References resolve to inspected context or an explicitly authored source in the owned stage. |
| `circuitPolicy` | `Shared` default; optional named independent circuits with explicit consumer assignments. Room/entrance separation does not implicitly demand electrical separation. |
| `supplyRequirement` | `Operational` default for requested power; `WiredOnly` deliberately allows an unenergized installation. Required working machines/lights still fail if no permitted supply exists. |
| `loadPolicy` | Per-consumer nominal/peak/load-state requirements, demand margin (default 20%), startup allowance and verification interval. Uninspectable dynamic demand needs an authored bound; it cannot be assumed zero. |
| `batteryPolicy` | Declare initial charge and whether temporary battery-only operation is allowed. A finite reserve never establishes sustained upstream supply. |
| `routingPolicy` | Allowed utility cells, prohibited cells, corridor/wall preferences, optional redundancy, device-count/cable-length goals and explicit reuse of existing compatible wires. |

The R08 version-1 authoring subset currently uses `highVoltageCable`, `mediumVoltageCable`,
`lowVoltageCable`, and `substations`/`apcs` lists of `{ prototype, weight }`. Interfaces contain
`id`, `cell`, `voltage` (`High`/`Medium`/`Low`), `networkKey`, `circuitId`, `availableWatts`,
`enabled` and `connection` (`ReuseExisting`/`JoinAtCell`). Shared circuits use the single ID `Main`;
independent circuits declare a bounded `circuitIds` list. `demandMargin`, `startupTicks`,
`observationTicks`, `allowBatteryOnly`, nullable `initialChargeFraction` and `reuseExistingCables`
are currently flat profile fields. Detailed load-state inspection and spatial routing policies
remain planned; the flat fields do not imply those missing features. `JoinAtCell` is only an
author intent, never permission to edit preserved cells or connect to live power speculatively.

Electrical **profile schema 2** adds `distribution` below. This is independent of the enclosing
request/project/draft schema. Omitting `distribution` in an explicit schema-two profile expands to
these defaults; canonical editable copies always include the expanded policy. It cannot be attached
to schema one. `KsProcgenElectricalProfiles.TryUpgrade` validates and returns a detached schema-two
copy, preserves other settings, and changes identity; it never mutates the old document or preview.
Schema-one declaration hashes remain historical. The temporary C# field default and old built-in
profiles stay schema one until the joint closet/routing rewrite activates the rollout; the revised
`KsProcgenClosetElectrical` preset is explicitly schema two. The primary standalone owned
AutoDistribute/Shared/WiredOnly path now captures the matching frozen closet catalog and generates
actual dedicated rooms, separated converters and required MV/LV service. Shared callers must supply
that inspected catalog; missing/wrong content rejects with `ElectricalUtilityRoomContextRequired`.
Hosted/preserved interfaces, operating supply, hull reuse and complete joint retries remain gated.
Explicit schema-one primitive behavior is historical compatibility, not the revised layout contract.

| `distribution` field | Schema-two contract/default |
| --- | --- |
| `closetInteriorSizes` | Set of one or both `1,2` and `2,2`; `2,1` is equivalent to `1,2`. Default both. Canonical copies sort dimensions/sets; duplicate rotated sizes, empty sets and other dimensions fail. Closets still need actual partitions, an entrance and clean access. |
| `closetTheme` | Room-theme reference, default `KsProcgenElectricalCloset`. Select tile/wall/light/entity packs through this ordinary theme; no separate miniature furnishing language. Project capture follows and validates its complete authoring dependencies, including unused utility-draft profiles. Component bodies/native access still require separate inspection. |
| `mediumVoltageCirculationBackbone` | Default `true`: service writable circulation/protected-spine targets and closet approaches. Core converter terminals remain mandatory if disabled. |
| `mediumVoltageRoomEntryBranches` | Default `true`: service room entries and an explicit room service target domain where no corridor exists. APC terminals remain mandatory if disabled. |
| `floorServiceFraction` | Finite fraction `[0,1]`, default `1`. Hard minimum geometric LV service over the selected floor domain, separate from all actual required consumer checks. Choosing zero does not waive consumers. Native range/eligibility and actual supply are independent checks. |
| `floorServiceDomain` | `UsableRoomFloor` default: generated room floor eligible for normal traversal/use, excluding walls, voids, preserved areas and normally blocking/vault-required footprints. `AllRoomFloor`: all generated room interior floor, including occupied furniture positions. Actual receivers use their own positions/reception ranges in either case. |
| `lowVoltageSquares` | `RequireNecessityCertificate` default, or stricter `ForbidNew`. Neither mode authorizes a heuristic exception. The former requires section 20.3a's retained checkable proof for every newly completed block; the latter allows no newly completed blocks. Constant inherited blocks stay preservation concerns. |

The loaded electrical-closet theme offers a coherent optional HV/MV/LV coil-supplies pack using
real prototypes with clear movement declarations. Its loose supplies are examples, not required
floor clutter; closet access wins. The supported standalone WiredOnly path now creates a room,
places converters and retains native service/topology evidence. A 1x2 room may omit optional
supplies when access/space cannot fit them; no operating-supply certificate is implied.

Request schema 2's `utilities.consumers` now carries unique `id`, `scope`/`scopeId`, `selector`
(`AllPowered`, `Prototype`, `Member`), optional `selectorId`, `circuitId` (default `Main`),
`requirement` (`Required`/`Preferred`), `supply` (`Operational`/`WiredOnly`), nullable finite
nonnegative `nominalWatts`/`peakWatts`, and `minimumMatches` (default 1). `AllPowered` has no
selector ID; the others require a concrete prototype or stable planned member ID. Known peak
watts cannot be below known nominal watts; unspecified values require inspection, not zero.
Explicit profile circuit references and receiving loaded prototype IDs are validated at capture.
Inherited circuits, member existence, receiver eligibility, minimum matches and physical demand
remain candidate-binding obligations. Never double-count one physical consumer selected more than
once; contradictory circuit requirements must fail with both selector IDs after binding.
`utilities.electricalRestrictions.requireOperationalSupply` and `forbidBatteryOnly` are request-hard
constraints across all effective profiles. No contract-capture result certifies power or eligibility.

Example loaded authoring preset (no source is implied, and runtime distribution remains gated):

```yaml
- type: ksProcgenElectricalProfile
  id: KsProcgenAutomaticElectrical
  mode: AutoDistribute
```

HV input creates a complete distribution tree. An existing compatible MV or LV interface may
satisfy downstream distribution without adding redundant upstream equipment; report the supplied
tiers. A missing source under `Operational` is an unmet requirement. Do not create a generator,
infinite battery or live-grid connection without an explicit source/interface contract.
Disconnected islands require a source or authorized cable route for each electrical network.

Substation/APC front access, facing and mounting use the same machine rules as other furnishings.
**APCs must occupy a solid wall tile**, with their interaction face directed toward a reachable
clean floor approach. A wall merely next to a floor-placed APC does not satisfy mounting.
Substations have distinct floor and wall-mounted prototypes: preserve the selected prototype's
mount contract (`SubstationBasic` on floor; `SubstationWallBasic` on a solid wall). Do not substitute
one variant silently. Windows and door openings do not count as wall supports. Shared wall mounts
cannot overlap one another, and support cells require device-write permission. Both cable tiers
needed by each converter still occupy that converter's exact tile, including wall tiles; extend
the cable route to those tiles with cardinal connections. Missing support produces a bounded
placement failure and candidate retry, never a floor-placement fallback. Recheck actual owned
support entities, anchoring and the unobstructed front approach before certifying initialized
wiring. A profile may list both `SubstationBasic` and `SubstationWallBasic` in its weighted
`substations` palette. Try complete alternatives within the shared placement allowance: a failed
floor variant may be replaced by a wall variant without mixing their members or consuming a clean
floor passage. Explicitly listing one prototype restricts the search to that mount kind.
Devices may be wall-mounted only where their actual prototype supports it. Cable placement can
coexist with a traversable passage or supporting furniture, subject to actual engine support;
the device itself must not block a reserved route or chair approach.

### 20.3 Routing and joint placement algorithm

1. Inspect source interfaces, existing cables/providers, terminals, allowed utility cells and every
   mandatory consumer, including doors, lights and relational assembly members. Record voltage,
   actual receiver range/eligibility and requested demand. Reinspect final spawned consumers later.
2. Allocate designated 1x2/2x2 substation utility rooms and feasible wall APC sites early, outside required movement/interaction space.
   Group consumers into supply zones by allowed circuit and demand. Sharing one APC across adjacent
   rooms is permitted; prefer fewer maintainable devices subject to capacity and required isolation.
3. Use a separate layered graph with vertices `(cell, voltage, circuit)`. Same-layer cardinal edges
   require valid cable placement and terminal behavior. Cross-tier transfer occurs only at selected
   device ports. Do not represent cables as floor obstacles or reject different layers at one tile.
4. Connect HV interfaces to substations, then MV outputs to APC inputs using bounded deterministic
   service-backbone paths. Meet section 20.3a coverage targets before minimizing length. In stable endpoint
   order, grow a connected tree from already routed cells; cycles are optional policy-controlled
   redundancy. Routing costs are finite/nonnegative and stable ties use cell/layer/ID order.
5. For each APC circuit, enumerate legal LV provider cells that cover still-unserved consumers using
   the exact distance/capability rule. Score candidates by newly covered required demand and route
   cost; choose deterministically and join each to the existing LV tree by cardinal routing. This
   must also serve the room-floor coverage domain and avoid LV 2x2 blocks under section 20.3a. This
   is a bounded connected set-cover heuristic, not a claim of optimal minimum wire length. Always
   include a valid LV cable on the APC tile, even where the APC itself could provide local coverage.
6. Count demand once per consumer; compare with all upstream device/source limits, charge-rate and
   ramp/breaker behavior under the authored load scenario. Move/add devices or reroute within the
   same request budget when overloaded or unreachable. Two touching same-tier circuits merge in the
   engine; reject accidental joins rather than relying on different logical IDs.
7. Include optional accepted content and lighting, then validate topology and load again. Failure of
   a mandatory device triggers whole-core/layout backtracking; failure of optional content can omit
   that complete core. Keep required gas boundaries and paths intact. Do not defer all infrastructure
   to a final pass after furniture has consumed every legal device tile.

Generated cable cells remain within `W`. Envelope use for utilities must be explicitly authorized
as a utility connector; closure-only envelope permission does not grant a cable route. A preserved
cell can supply an inspected compatible wire, but cannot receive new cables. An author needing a
new cable seam must explicitly place that seam outside the constant/preserved mask before generation;
an interface marker alone cannot override preservation. Existing cables are reused without edits.
Crossing a wall tile is allowed only if cable placement is supported and the tile/layer is writable;
never cut or replace a hull to reach a cable. Deduplicate same-tier cable intents on the same tile.

### 20.3a Dedicated substation rooms and service coverage (2026-10-23 amendment)

These requirements supersede the earlier general-purpose converter-home placement and sparse
endpoint-only wiring examples. They refine the existing electrical scope and keep its fixed
capability weight. Current implementation must be rewritten before claiming this amendment done.

- Every newly generated substation MUST occupy a designated electrical utility room with **1x2 or
  2x2 interior floor dimensions**, allowing quarter-turn equivalents. Dimensions exclude enclosing
  walls. The room needs actual walls, a usable entrance and a cardinal clean route to the device's
  operating tile. A small rectangle label inside a general-purpose room does not establish a closet.
  Allocate these rooms during joint partitioning, before ordinary contents consume the footprint.
- Prefer sharing legal generated exterior walls over consuming an additional complete wall band
  inside the target floor. A reservation must distinguish interior floor, partition-owned enclosure
  and reused generated-hull cells. Reuse requires explicit writable envelope ownership; it cannot
  borrow another target area, an uninspected existing wall, a constant cell or explicit void. Include
  diagonal enclosure corners as structural claims even where they have no cardinal floor exposure.
  Such non-exposed corners are not movement/atmosphere edges or exterior-window candidates. The entrance
  threshold and both approaches remain inside the target, with an uninterrupted cardinal route.
  All incident room palettes must agree on the shared hull-wall material. Recheck these claims after
  structural material/window selection and against the native enclosure. Fully internal closets
  remain a fallback when no legal generated hull can be shared.
- Closet contents MUST use a configurable coherent electrical theme/entity pack: electrical
  equipment, supplies and other appropriate paraphernalia. Resolve real loaded prototypes and
  mounting/support behavior. In a tight 1x2 room prefer wall/surface/container fill when floor
  props would block the remaining operating/entrance tile. Hard device and access requirements
  take priority; report sparse optional decoration rather than blocking the closet.
- Preserve floor versus wall substation variants. Wall variants need real solid support; floor
  variants need a legal interior footprint. APCs remain wall mounted in their served rooms or
  service zones. A substation and its downstream APC MUST NOT be forced into the same room by
  the current shared-home shortcut. Required same-tile HV/MV and MV/LV terminals still apply.
- MV routing MUST provide a generous service backbone, not only the shortest substation-to-APC
  connection. Default backbone targets include writable circulation/protected-spine cells,
  closet approaches and room service-entry branches, plus every required APC input. A network
  without a separate corridor still needs explicit room service coverage targets. Prefer existing
  compatible trunks and useful reach; shortest wire length is secondary to coverage. MV loops
  and redundancy are allowed by policy. This does not authorize foreign or constant-area writes.
- LV routing MUST verify each actual required consumer and SHOULD provide service to all usable
  generated room-floor tiles by default, including likely equipment positions. This is the current
  interpretation of adequate low-effort interior wiring. Make the coverage fraction and domain
  configurable, show uncovered cells and actual consumers separately, and use loaded native
  provider ranges. Each actual consumer must also meet its own reception range and eligibility.
  Coverage cannot be inferred from a Manhattan/square neighborhood or mere cable proximity.
- Newly generated LV cable occupancy MUST avoid fully occupied **2x2 blocks** unless they are
  proven necessary. Check all four same-tier tiles and the physical native adjacency graph;
  omitting a drawn edge does not remove a connection. HV/MV overlap at different tiers does not
  form an LV block. Reuse, route growth, branch joins and later extensions must all recheck this.
- Do not use "search could not avoid it" as proof of necessity. Accept an exception only with a
  retained checkable certificate, such as four individually forced LV cells (mandatory terminals
  or uniquely required coverage in the complete allowed candidate domain), or a completed bounded
  exact search proving equivalent required coverage impossible without the block. Freeze candidate
  permissions, receiver ranges, terminals and preserved cells into that evidence. Exhaustion is
  unknown, not proof: try other routes/APC sites/closet arrangements, or return the appropriate
  required-coverage failure/budget result. Never silently relax this rule after a heuristic stalls.
- Existing constant-area LV blocks may remain untouched; distinguish inherited geometry from a
  newly created block. Adding a generated tile that completes a block is a new block and needs
  the same proof. Existing inspected interfaces may serve the request without a newly generated
  substation; they cannot bypass preservation or invent an undesignated generated substation room.

Electrical profile/draft transport needs a versioned migration for closet palettes and sizes,
backbone targets, floor-service coverage and LV block policy. Old documents must receive explicit
new defaults or a supported migration failure; they must not silently retain open-room substations.
All geometry, packs, doors, cable tiers and coverage/proof work share request-wide quotas and
whole-candidate rollback. Small 1x1/1x3 pure generation remains representable; an impossible hard
electrical service request must report its limitation rather than breaking these placement rules.
The old wired-office showcase and tiny co-located converter fixtures are historical evidence for
native mounting/topology primitives. They do not meet this revised room/routing contract.

### 20.4 Runtime checks, budgets and fallbacks

Materialize devices and cables in a deterministic engine-supported order in the owned stage. Allow
node groups, anchoring, extension-provider linking and power state to initialize before checking.
Inspect actual same-tile input/output membership and cardinal paths, the selected provider for
every consumer, breaker/switch states, battery/source contribution, delivered load and the consumer's
operating state. Use the authored load/observation scenario, not a momentary charged battery or
idle machine, to establish operational supply. Optional surplus capacity is a soft goal unless hard.

Expose separate evidence: `ElectricalTopologyVerified`, `NativePowerLinksVerified`,
`ElectricalSupplyVerified`, per-consumer operation and light coverage. `NativePowerLinksVerified`
means that registered native solver member lists and component backlinks match the inspected owned
networks at initialization. It does not imply a solver tick, available source capacity, delivered
power or operating devices. Repeated verification inspects links and pending work without repairing
them; only the owning initialization transaction may establish the first receipt.
`WiredOnly` may pass topology and native membership while supply stays `Unverified` or
`Missed`; it cannot count as a working interior when operating power is required. Do not force
`Powered`, disable `NeedsPower`, bypass breakers or add invisible generators to pass validation.

Initial configurable utility limits: 65,536 distinct inspected utility/gas cells (also sharing the
request cell cap), 196,608 cable-layer vertices, 4,096 device-placement proposals, 1,000,000
receiver/provider coverage probes, 2,000,000 electrical path expansions and 4,096 consumers.
Cable spawns also share the request entity cap; a larger graph budget is not spawn authorization.
Count rejected placements and retries; build sparse graphs and query coverage spatially. Gas
readback is capped at the accepted tile count per sample; observation ticks, node-reflood work and
engine validation receive explicit limits as well. Budget exhaustion cannot skip required checks.

On failure, try legal routes/device sites, approved device variants, reduced optional demand or a
complete alternative layout. `AllowUnpoweredOutput` is an explicit scoped relaxation with a
`Degraded` result and affected consumers listed; it never silently waives a required traversable
powered door or another unrelated hard condition. 1x3 requests can be wired from external supply
or return explained failure when mandatory infrastructure does not fit. Never fake a diagonal join.
Cancellation/rollback must remove owned cable nodes and provider links as well as spawned entities,
without altering an existing live power network during speculative work.

## 21. In-game authoring UI and interactive workflow

### 21.1 Workspace and basic flow

Provide a localized procgen workspace inside the game, accessible through mapping/admin tools to
authorized authors. Use existing engine UI/placement/overlay services and the KS localization APIs.
Normal players do not gain map-edit permissions by opening the window. This is an editor workflow;
it does not require placing an in-world gameplay console. A future console may open the same UI
under an explicit permission policy.

The main layout has a searchable asset browser, central grid/preview viewport, selection inspector,
and diagnostics/history panel. Dockable or resizable panes are preferred; a compact layout must
remain usable at the supported minimum resolution. Provide keyboard navigation, tooltips, units,
text labels and patterns/icons in addition to colors. Author-facing labels explain behavior; internal
schema/hash details belong in an expandable diagnostic panel.

Quick workflow: select a grid, paint a target, pick a room theme and optional library, choose gas
and electrical presets/source interfaces, preview, inspect, then apply a verified result. Presets
fill advanced settings, which remain inspectable. Default automatic room selection must not turn
into mandatory manual room placement. Basic pure fill still needs only a mask and suitable theme.

### 21.2 Authoring tools and editors

| Editor/tool | Required interactions |
| --- | --- |
| Area painter | Brush, line, rectangle, bounded flood, erase, selection and move/copy. Separate target, void, envelope and constant layers. Preview exact cardinal blobs/holes and stable IDs; allow channel/profile assignment and identify merge/split conflicts. Painting an outline does not silently fill its center. |
| Entrance tool | Place/select a port on a threshold, edit its label, normal, span, approaches and destination policy. Highlight its bound blob; list orphan, duplicate and ambiguous ports immediately. Numbered convenience presets use the same string-label model. |
| Connection editor | Add alternative configuration cards. Drag ports into named groups and choose exact versus minimum connectivity and local versus host isolation. Graph and matrix views show required/forbidden joins. Import/export shorthand such as `1-3-4;9-8` with one alternative per line, preserving stable configuration IDs and weights. |
| Theme editor | Choose tile/wall/light/entity packs, primary/accent palettes, goals, counts, density, atmosphere/electrical profiles, inheritance and fallbacks. Show inherited versus overridden values and reset-to-inherited. Validate references/cycles and hard requirements while editing. |
| Room/library editor | Create packs from existing room assets or capture a selected authored area into a draft asset through supported map serialization. Edit eligible room types, tags, transforms, size mix and exterior preference. Inspect exact footprints/ports and copy-supported overrides; reject unsupported captures. Automatically fit library rooms without fixed target slots. |
| Layout family editor | Assemble alternatives such as one large, medium-room set, L trio and four-room set; edit complete-package weights, common exposed ports, claims, gaps and exclusions. Preview each alternative and why variants are incompatible. Never turn alternative members into an independent room pool. |
| Tile/wall/light pack editor | Add/remove weighted variants, preview appearance, mounting and compatible doors/windows; set light supply and goals. Display unresolved or unsupported content rather than silently deleting it. |
| Entity-pack editor | Search actual localized prototypes by name/ID/tags/capabilities; add singleton entries or assemblies, bind entity choices, set core minima/optional counts and activity coherence. Show effective inherited collision, movement, power, support/container and interaction capabilities with their verification status. |
| Assembly editor | Name members and bindings, connect typed relation edges (`OnSurface`, `InContainer`, `AdjacentTo`, `FacingTarget`, `UsesSeat`, `Near`, `AtCorner`, `FacingOpenSpace`), select targets/slots, multiplicities, severity and allowed rotations. Distinguish support dependencies from spatial cycles. Add complete variants; switch preview bindings without rewriting relations. |
| Assembly viewport | Drag a member only to author an explicit offset/constraint or test pose; do not quietly replace relational rules with fixed coordinates. Rotate members/assemblies, show actual facing and clean chair/device approaches, support layers and blockers. Test a corner table/device/chair arrangement against selected masks and actor profiles. |
| Atmosphere editor | Pick gases, edit percentages, temperature and pressure with units, choose preserve/fill/vacuum and initial/stable requirement. Assign profiles to rooms/blobs/defaults and preview actual gas domains, conflicts, expected volume/moles and leak witnesses. Warn about declared species incompatibility only when that actor requirement applies. |
| Electrical editor | Select cable/converter palettes and source interfaces, voltage/capacity, shared/separate circuits, required loads, battery policy and wired/operational requirement. Automatic routing is the default; optional pinned device sites/route waypoints become explicit constraints. Overlay each tier, same-tile dual cables, true circular coverage and unserved/incorrectly assigned consumers. |
| Constraints/seed panel | Edit hard versus preferred goals, tolerances, named permitted fallbacks and deterministic budgets. Reroll seed, lock authored assets and compare alternatives without implicitly pinning generated rooms. Show effective values and why a choice is unavailable. |

"Room pack" in the UI must distinguish a library of complete prefab rooms/layouts from an entity
pack furnishing a procedural room. Both can be created and configured in game. Provide a simple
new-room-theme wizard that chooses materials, lighting, coherent contents and utilities without
requiring raw YAML. An advanced structured/raw view may expose the same canonical data; it is not
the only way to author any required feature. Unsupported future fields remain visibly unavailable.

### 21.3 Drafts, assets and saving

Separate **authoring documents** from live ECS/prototype instances. A server-owned draft has a
stable document ID, schema version, monotonically increasing edit revision, content dependencies,
named assets and exact area/port/utility assignments. Editing a loaded prototype creates a draft
copy; it never mutates the shared prototype-manager object or hot-reloads the round globally.
Generated choices belong to a preview result; authors can explicitly convert selected choices to
constraints, constants or reusable assets with the resulting loss of automatic freedom shown.

Support new, duplicate, rename, delete with reference inspection, import, save, save-as and export
for themes, material/light/entity packs, assemblies, room libraries, layout families and profiles.
Renaming updates draft references atomically; deleting a referenced asset must resolve or retain
visible broken references, never silently substitute another asset. Captured rooms include portable
entity references/overrides and dependency manifests through the validated map adapter.

Persist drafts through an approved server resource/storage service and offer download/export through
existing tooling. Versioned assets can be used directly by that generation session after validation;
creating content does not require a server restart or editing repository files by hand. Persisted
project exports use supported YAML/map formats with dependency manifests for later repository use.
No client-supplied filesystem path, arbitrary code, console command or component deserialization
is accepted as an editing shortcut. Bound upload size, parsed depth, references and asset counts.

Undo/redo covers document edits, brush strokes, reference changes and profile assignments as atomic
transactions. Auto-save successful draft revisions and recover them after disconnect. Never treat
post-publication undo as arbitrary live-map rollback: before exposure a retained owned result may
be discarded, while replacing an active/occupied map remains outside version 1. Closing a window
does not silently apply or destroy an authored map.

#### Implemented interior draft subset (2026-10-16)

`KsProcgenContentDraftSpec` schema 1 is now an explicit, bounded authoring value containing
`documentId`, `revision`, and six dictionaries: `themes`, `tiles`, `walls`, `lights`, `entities`,
`assemblies`. Their values share the existing prototype authoring spec fields without a global
prototype `id`/`type`. Asset names are scoped by document and kind. A draft may override a loaded
name or inherit a loaded theme; an invalid local override must fail selected catalog capture and
must not silently use the loaded asset instead.

`TrySnapshot` validates structure and copies every nested body. Editing drafts can retain broken
references, incomplete choices and symbolic bindings; they are not validated generation content.
`KsProcgenThemeCatalog.TryCapture(..., assets: draft)` resolves and validates a selected theme's
complete candidate pools and assembly variants, producing the existing detached planning catalog.
It can also receive `draft: utilityDraft` for local gas/power defaults. Supply that captured catalog
to the primary owned planner. This does not expand the planner's currently supported relational
layers or enable pending utilities. No operation registers prototypes, imports arbitrary components,
spawns content, grants permissions or verifies physical operation.

Bounds are 128 combined assets, 32,768 cumulative collection elements, 1,048,576 text characters,
512 characters per string and per-kind member/choice/relation limits. Null structures, unsupported
enum values and nonfinite numbers fail atomically. Theme inheritance/fallback traversal is capped
at 128 levels, including loaded ancestors. Portable copies retain every authored field and ordered
alternative. Draft declaration identity sorts dictionary keys and preserves authoring list order;
it excludes document ID/revision and is distinct from semantic planning identity or a dependency
manifest. Full replacements require matching document/current revision and increment once; the
server owner still needs to serialize replacement, permissions and undo history. Persistence,
room maps/libraries/layout families, assignments and UI remain open. This subset does not complete
the project contract above or the R08/R15 manifest requirements.

#### Implemented planning project/library subset (2026-10-16)

`KsProcgenProjectSpec` schema 1 aggregates optional interior/utility/layout documents and named
`areas`. All provided child documents must match the project's document ID and revision. Each area
binds a canonical `request`, explicit `theme`, and optional `library`; its key must equal the request
ID. Procedural requests omit libraries; prefab/hybrid requests name one. `TryCapture` validates every
selected area's content and references atomically. It imports referenced theme inheritance/fallback,
pack, assembly and utility authoring definitions into portable project copies; no global prototype
registration is required. Unreferenced interior draft assets may retain broken semantic references
for editing, as described above. This is not the server-owned editing session or a full replay artifact.

`KsProcgenLayoutDraftSpec` schema 2 (with migration of schema 1 defaults) provides reusable room geometry, atomic layout families and
libraries. Room assets declare cells, facade edges, ports and optional paired source key/fingerprint
expectations. Source rooms also accept a positive map-local `sourceGridUid` and a native
`sourceOrigin` offset; see the 2026-10-18 atlas extraction ledger for the exact coordinate contract.
A family's complete alternatives share an exact reservation; each partitions it into
nonoverlapping transformed room members and explicit generated cells. Members store stable source
room IDs, local origins and quarter turns. Singleton library rooms compile into `Room/<asset>`
families; layout asset, option, member and port IDs exclude `/` to reserve that namespace. Global origins and family turns
remain discovered by the fitter. Port/facade directions transform cardinally with the room, and
immutable bindings retain family/option/member-to-source-room identity for future map adapters.

Project copies contain the pinned authoring definitions, canonical requests and source declarations;
loaded entity components, tile definitions, actual room-map contents and live host facts remain R15
manifest/context obligations. `ContentFingerprint` is an expectation, not an observed checksum.
Geometry-only rooms do not become real copied prefab contents. The existing geometry pipeline can
receive a captured catalog from a project; disconnected networks and unsupported gas/power contracts
remain failures. The primary owned planner still has its documented hybrid and layered-content gates.

Project capture bounds 64 areas, 256 combined child assets and 65,536 cumulative normalized target/
envelope/void mask cells. Layout drafts separately cap 256 assets, 32,768 collection elements, 65,536
authored mask cells and 65,536 expanded room-instance cells. Exact source masks reject duplicates;
mask/rotation set ordering and dictionary insertion order do not affect canonical identity. Complete alternative
ordering remains authored data. Failed capture returns no partial project. `TryRevise` checks project
identity/current revision, captures the replacement and increments the project and serialized children
once. Concurrent ownership, undo, persistence and UI remain R16 work; no project value grants a lease
or permission. Advanced layout void/seam/link/anchor/eligibility fields and room-map capture remain
explicit open R08/R09 requirements.

#### Implemented static room-map capture subset (2026-10-17)

The server can now call `KsProcgenRoomMapCaptureSystem.TryCapture(project, sourceBindings, ...)`
with its own symbolic-source-to-content-path catalog. It captures exact immutable map bytes,
checks observed SHA-256 against each room's expected fingerprint, and binds the canonical project
declaration and map/theme prototype bodies, following typed entity references, into a separate
manifest. Only content resources
under canonical `/Maps/` paths are read; user data and client-supplied paths are not consulted.
This API is not an editor import endpoint, a room loader or apply permission. It supports bounded
static inspection of formats 6/7 without creating entities. `CopyBytes()` returns a copy and
`OpenRead()` returns a read-only stream over the captured source, enabling a future loader to consume
the same bytes without reopening a mutable resource. Project/library source declarations remain
unverified geometry until the adapter reconciles actual masks/ports/transforms and validates room
operation. Full dependency/resource capture, source-map instantiation and portable replay remain
open; section 17 records the implementation and verification checklist.

The current source-manifest schema 8 also decodes bounded chunk payloads without engine grid
allocation. `TryReconcileRoomMasks` separately translates each room-local mask by `sourceOrigin`
and matches it to the explicit `sourceGridUid`, or a unique occupied unit grid when no selector
is supplied. This supports atlas submasks without moving the fitted room geometry. Empty/missing cells,
ambiguous grids, stale project declarations and shared probe exhaustion reject the whole result.
This remains occupied-tile evidence; physical ports, walkable floor, hull, transforms and entity
dependency extraction still need the complete map-copy adapter and operational checks.

`TryExtractRoomEntities(project, sourceManifest, ...)` now adds map-local entity locators and finite
grid-relative and rebased room-relative transform witnesses without spawning source content. It merges transform overrides
with verified prototype defaults, resolves ancestry independently of source ordering, and rejects
cuts through non-grid transform branches. Registered and server-ignored component override/removal
names remain tied to the original immutable source bytes. This is transform-hierarchy extraction;
it does not establish complete component/container/support relationships or authorize loading or
activation. Extraction schema 3 reads captured prototype images for transform defaults and
observes typed entity/network/map links through registered component validators. Observed links
must stay inside the selected room or name its selected grid/owning map context. Numeric-looking
ordinary fields are not interpreted as entity IDs. Skipped/unsuccessfully validated component
types are recorded as known inspection gaps; an empty gap list is not completeness evidence.

The base source manifest captures group/theme prototype roots and their typed dependencies.
Extraction's `Definitions` extends that snapshot with typed prototype references in overrides,
using separate reference seeds so abstract filter definitions need not become spawnable roots.
Prototype manifest schema is 10. Entity/tile roots, explicit other-kind roots and external typed
reference seeds retain distinct provenance. Referenced registered kinds close transitively through
nested typed wrappers, including references back to entities and registered tiles; abstract
reference-discovery images retain declaration authority only. Parent chains and declared-schema
inspection gaps are included; an opaque field is not guessed to be a prototype reference.
Extraction also retains complete immutable canonical override nodes, with cumulative selected-image
node/character budgets and a depth limit. It does not deserialize those bodies into live components.
Extraction identity binds those
definitions and the actual returned component bodies and mask/pose/reference/gap witnesses; base source hashes alone do
not freeze override dependencies.
Legacy/custom/client reference schemas, resources and actual component copying remain unfinished.
Both `ComponentReferencesVerified` and `CopyReady` remain false.

### 21.4 Preview, diagnostics and apply

All graphical map previews use the existing `Content.MapRenderer` and its native map/viewer
outputs. Do not implement a parallel procgen sprite compositor, SVG schematic renderer or custom
gallery. The Python/PowerShell procgen drawing pipeline and server SVG renderer are retired by the
2026-10-28 user-directed replacement. Historical implementation entries describe removed systems;
they are not requirements to restore them.

Planning, generation transactions, stage ownership/lifecycle, inspection, bounded diagnostic JSON
and native map export remain separate feature responsibilities. A rendered image cannot grant apply
permission or replace physical validation. Native renderer caches belong to their paint operation
and must be disposed after success or failure. The renderer processes native client appearance
updates before reading sprite layers. Its `--wires-last` option reveals installed cables through
native subfloor appearance and paints them after ordinary entities; loose cable coils retain their
normal treatment. Reuse the native tile, decal, sprite, direction and appearance pipelines rather
than adding curated procgen prototype-to-sprite mappings. Use its `--viewer` output for map viewing.

The utility palette inspector must display the captured mount requirement for each converter
prototype. Wall candidates, actual selected support walls, front interaction approaches and
same-tile HV/MV/LV terminals need separate preview overlays. Report missing solid support,
window/door conflicts and mount-cell write restrictions directly; an adjacent-wall floor placement
must never be shown as a valid APC. Show the selected floor/wall substation variant and any whole
variant replacement. This is required editor behavior; the headless mount solver is not a delivered
in-game control.

Use explicit operation states: `Draft`, `Validating`, `Planning`, `Planned`, `Staging`,
`Verifying`, `ReadyToApply`, `Failed`, `Cancelled`, `Applied`. Display independent evidence for
geometry, clean access, gas closure/fill, electrical topology/supply, light coverage and publication.
A geometry-only result is viewable as `Planned`; it cannot become `ReadyToApply` when hard runtime
checks remain unverified. Planned and measured overlays use different labels/styles.

* Preview uses an immutable draft/content/context snapshot. Progress reports stage, deterministic
  counters and current issue; cancel remains responsive. Debounce interactive edits and bound work
  per user/server. A new revision invalidates an old preview rather than retagging it as current.
* Selecting a diagnostic focuses its room, cell, relation, gas boundary or circuit and highlights
  the witness: blocked path, incompatible variants, leaking edge, missing cable layer, wrong
  provider, insufficient source or failed load. Offer an explicit edit action where appropriate;
  never auto-relax a hard condition merely to make the display green.
* Overlay toggles include ownership/constants/void, alternatives, entrance groups/routes, support
  layers/facing/approaches, gas domains/composition/pressure, HV/MV/LV, converter terminals,
  consumer radius/provider links/load, operating light coverage and tactical samples. Clicking an
  entity exposes both authored declarations and inspected engine facts.
* Compare previews using the same seed and changed settings, or different seeds with identical
  content. Report changed rooms, package selections, gas assignments, cable length/capacity,
  omitted contents and hard/soft outcomes. Retain bounded replay artifacts and failing seeds.
* Apply is a deliberate action on one validated snapshot. Server checks editor permission,
  current draft revision, content/context fingerprints, stage ownership and all required evidence
  again; stale results require regeneration. One operation publishes once, and duplicate requests
  return the same outcome. Partial multi-blob publication is disabled by default.

### 21.5 Client/server protocol and acceptance

Clients send typed editing intentions with document ID, expected revision and operation ID. Server
validates permissions, references, numeric bounds and writable context, then acknowledges the
new revision or returns a conflict. Preview/apply messages reference server-issued snapshot tokens;
clients cannot assert their own verification flags, UIDs, watts or writable masks. Validate those
claims against permitted context. Use revisioned bounded/paged asset lists, diagnostics and overlay
geometry; report truncation and fetch details on demand without creating one UI control per tile.

Start with one writer per draft and read-only collaborators, with visible writer identity and an
explicit takeover/release action. Rejected stale edits preserve local unsaved input for recovery.
Disconnect, cancellation, timeout and server shutdown dispose unretained stages and release leases.
UI preview throttling affects scheduling, not the selected seed outcome. Expensive validation runs
as a cancellable job; pure client validation is advisory and uses the same schema semantics.

Implement schema-driven reusable fields/pickers where practical, plus dedicated graph/paint/layer
controls where a plain form cannot explain the spatial rules. The server generator remains usable
headlessly through the same draft/request contracts. Required UI exits are A62-A66: authors can
create and save a coherent room theme, library alternatives and a relational assembly, configure
gas and power, diagnose failures, preview and apply without touching source files or raw YAML.

Revisions to these defaults should update the contracts, fixture expectations, and task acceptance
criteria together. The final implementation must make its limits visible through results and
diagnostics, especially when a requested shape cannot physically support every requested property.
