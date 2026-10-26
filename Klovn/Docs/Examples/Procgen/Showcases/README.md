# Small KS procgen showcases

Open [the offline gallery](index.html) or [the sprite overview](overview-sprites.png).
Game sprites are the default view. These previews composite the repository's actual PNG/RSI
assets at generated positions, with cardinal frames and corner smoothing. They use full-bright
static frames; native client lighting, shaders and runtime appearance are not reproduced.
Choose **Diagnostics** for the original SVGs and their furniture, path, cable, gas and LV overlays.
The [diagnostic overview](overview.png) remains available.

Individual sprite previews: [strip](tiny-strip-sprites.png), [L shape](l-shape-sprites.png),
[kitchenette](kitchenette-sprites.png), [gas room](gas-room-sprites.png),
[office](wired-office-sprites.png), and [exposed wiring](wired-office-wiring.png).
Normal views hide under-floor cables; the wiring preview draws them last above every scene layer. Contained
items stay hidden. [Sprite sources, hashes and attribution](sprite-assets.json) record the assets
used, including the curated fixture mappings' scope and appearance limitations.

All five examples use seed **17**.

| Example | Generated output | Native grid |
| --- | --- | --- |
| [Tiny strip](tiny-strip.svg) | Three floor tiles, exercising tile-scale generation without a usable interior. | [tiny-strip.yml](tiny-strip.yml) |
| [L-shaped footprint](l-shape.svg) | 27 floor tiles with automatically fitted exterior walls/windows. | [l-shape.yml](l-shape.yml) |
| [Kitchenette](kitchenette.svg) | Table, supported microwave and paper inside its container; declared operating approach. | [kitchenette.yml](kitchenette.yml) |
| [Initial atmosphere](gas-room.svg) | 36 floor tiles, six windows and an observed initial air fill of approximately 101.33 kPa. | [gas-room.yml](gas-room.yml) |
| [Wired office](wired-office.svg) | 2x2 substation closet with electrical supplies, separate wall APC and computer; 32 HV/MV/LV segments, 17 MV service targets and all 30 usable floor cells within LV reach. No LV 2x2 blocks. Native three-tile LV radius. | [wired-office.yml](wired-office.yml) |

Each example includes JSON diagnostics and a `.request.yml` input. Furnished examples also include
`.content.yml` room-pack drafts. The gallery links all of these files. Container contents appear
in its contents list, rather than as floor occupants.

## Load a native grid

The five native `.yml` grids are also installed under
`Resources/Maps/_KS14/ProcgenShowcases/`. In a local development server, choose an unused map ID
and use, for example:

```text
loadgrid 200 /Maps/_KS14/ProcgenShowcases/kitchenette.yml
```

The command creates the map if needed. Use the mapping/admin map selector to inspect it.
These are initialized, paused development previews. Initial gas values describe the verified
generation-time state; stable simulation and operational machine behavior remain separate gates.
The wired example verifies paused native topology and receiver/provider links before saving.
Save/reload checks compare tiles and prototype counts; operating supply and post-load topology
are separate verification requirements.

## Regenerate

Run from the repository root after restoring the normal solution dependencies:

```powershell
& ./Klovn/Tools/_KS14/Procgen/generate_showcases.ps1
```

Use `-SkipBuild` when the selected integration-test assembly is already built. Debug is the default;
use `-Configuration Release` to export the current Release build. The script
preserves the previous `KS_PROCGEN_SHOWCASE_DIR` environment value, runs the actual server planner,
initializes each materialized scene, saves/reloads its native grid, compares filled-tile counts
and entity prototype multiplicities, and cleans up the disposable maps. It then renders the
gallery and copies the five native grids into the resource directory. On Windows it also uses
System.Drawing to render the actual sprite assets; no extra Python packages are required. Use
`-SkipSpritePreviews` on platforms without System.Drawing. Stale sprite plan/file hashes are not
shown as current previews. To rerender existing exported layouts without running the engine:

```powershell
& ./Klovn/Tools/_KS14/Procgen/render_sprite_showcases.ps1
python ./Klovn/Tools/_KS14/Procgen/render_showcases.py ./Klovn/Docs/Examples/Procgen/Showcases
```

The request and content YAML files are authoring documents, not native maps. Reproduction uses
the showcase fixture's retained room packs and planning budgets; these files do not freeze every
referenced resource or certify full engine replay.

## Verification

Normal Release and fresh Debug each pass **891 procgen units, 825 integration/sandbox cases and
31 native serialization cases**, retaining all previous 881/817/31 with identical inventories and
zero failures/skips. Ten units and eight integration cases are new. Both actual sandbox checks and
the updated installed native showcase maps pass. Two isolated compile-valid controls produce six
unit and six native material-authority assertion failures. Both source files restore exactly before
34 focused unit / 59 focused native positives and the complete gates; all nine changed C# hashes
match their captured values. Earlier control/gate counts remain historical.

The office uses the normal named schema-two preset and automatically selects a furnished 2x2
closet without a forced size override. It shares seven enclosure cells with generated hull, gaining
seven floor tiles over the previous full interior wall band. Its substation and coherent coil supplies
occupy the closet; a separate wall APC faces the served ordinary room. There are 32 floor tiles,
23 walls, six windows, one door and five content members. The 32 cable segments comprise 1 HV,
23 MV and 8 LV. All 17 MV targets are occupied and all 30 usable floor cells are within the native
inclusive circular three-tile provider radius. There are no LV 2x2 blocks or necessity exceptions.
Required native consumer links and full converter claims remain separately audited.

The same generator initializes 1x4/4x1 strips with wall substations, separate wall APCs, complete
enclosures and LV coverage, plus a 4x4 floor-substation layout. A 1x3 strip still cannot invent a
mandatory two-tile closet, threshold and outside approach. Missing, preserved, void or foreign target
corners cannot be borrowed. Only explicit writable generated envelope is reused; inspected existing
walls and hosted/preserved map interfaces remain separate work. Reused diagonal enclosure corners
without cardinal exposure are structural claims, not fictitious movement/atmosphere/window edges.
Native removal of such a corner invalidates the whole owned stage.

Five native exports regenerate and pass save/load checks; every installed native file matches its
export. All 72 repository sprite-source hashes and six preview hashes/plan associations are checked.
The four other sprite previews and SVGs are unchanged; the office sprites/diagnostics and both full
browser overviews are refreshed. Hidden headless Edge checks all five loaded images, both view modes
and all five diagnostic toggles in both states. The normal generator updates individual previews,
HTML/SVG and maps; automatic browser-overview generation remains tracked separately.

General planning/review versions remain 10/3. Schema-two context identities bind utility allocation
algorithm 3 and LV routing algorithm 2; legacy schema-one identities remain unchanged. Four fixed
mandatory APC terminals currently provide the only implemented square-necessity certificate;
heuristic exhaustion is never proof. Complete reservations retry with independent network ordinals
under shared hard limits. Within-reservation route/pose portfolios, global optional-quality ranking,
default-schema migration, hosted networks and operational supply remain open.

The [supported-room review](../supported-room.svg) and [JSON](../supported-room.json) remain historical
supported-member review artifacts. These full-bright composites and paused native checks do not
certify client lighting, working machines, stable atmospheres, operating electrical supply,
post-load topology, publication or the section 21 in-game authoring workspace. The expanded-scope
capability estimate stays **68% implemented / 32% remaining**, with the 80% goal still active.

### Wiring preview layer order (2026-10-26)

The sprite wiring PNG and SVG diagnostics draw installed cables last, above floors, walls,
furniture and machines, so complete routes remain visible. This is an inspection overlay; normal
sprite previews still hide underfloor cables. Loose coils in the closet are spare supply items.
All five native maps and normal sprite previews are unchanged by this renderer adjustment.

### Typed dependency verification (2026-10-26)

Native prototype manifest schema 9 follows registered typed references across prototype kinds and
captures override-only reference seeds separately from spawn roots. The office catalog has 538
combined definitions and fits the revised default limit of 1,024. Inheritance parents represented
as strings, legacy/custom/client references, resource bytes and portable replay remain open.

Release and fresh Debug each pass 891 procedural units, 862 integration/sandbox cases and 31
serialization cases; all preceding 891/825/31 cases are retained. Three isolated negative controls
produce 13 intended assertion failures before exact source restoration. All five native exports are
regenerated and match their installed resources. Two additional refreshed-office import cases keep
all 32 cables anchored and under floor cover after normal ticks, with converter membership and
consumer backlinks checked. These scoped cases do not certify general post-load topology or
operating supply. All 72 sprite sources and six preview/plan associations are checked; normal sprite
geometry and the cable-overlay PNG are unchanged. Both browser overviews are refreshed, with five
images, two modes and five layer controls verified. Six historical review files remain unchanged.

The expanded-scope assessment remains **68% implemented / 32% remaining**; the 80% goal is active.

### Parent dependency verification (2026-10-26)

Manifest schema 10 tracks runtime and declared parent links across registered kinds, including
abstract entity/tile ancestors. Partial abstract declarations use schema validation without fake
instances or required-field substitution; unknown declared fields retain bounded inspection gaps.
Known entity/network/map references still fail capture. The native office has 649 definitions and
one explicit declared-schema gap (`ClothingUniformSkirtBase` / `femaleMask`), within unchanged limits.
The base carpet's destruction behavior is now correctly attached to its threshold.

Release and fresh Debug each pass 891 units, 895 integration/sandbox cases and 31 serialization
cases, retaining all prior 891/862/31 cases. Six controls produce 14 intended assertion failures
before exact restoration. All five refreshed native exports match installed maps; two refreshed
office import cases pass. All 72 sprite sources and six output identities match, with all six PNG
bytes unchanged. Both browser overviews are refreshed and checked for five loaded images, both
modes and all five nonempty overlay groups/toggles. Six historical review artifacts are unchanged.
Full content closure, general post-load topology, operating supply and publication remain open.
The fixed estimate remains **68% implemented / 32% remaining**, with the 80% goal active.
