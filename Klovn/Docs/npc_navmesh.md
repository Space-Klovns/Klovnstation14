# NPC Tactical Position Query (dynamic camping/retreat/advance)

This document explains the dynamic, navmesh-based system NPCs use to pick camping, retreat, and
advance destinations, and how it relates to the older marker-based system it supplements.

## Background

Klovnstation 14's operative AI historically picked camping/retreat/advance destinations only from
manually-placed marker entities (`KsMarkerNpcCamping`, tagged with the (misnamed)
`NpcCampingSpotComponent`). Mappers place these by hand on maps, and NPCs query for the nearest
suitable one via a `UtilityOperator` running a `ComponentQuery`-based `utilityQuery` prototype.

This has two costs:

- Every combat zone needs hand-placed, hand-tuned markers before NPCs behave well there.
- Markers are static: they don't gain or lose tactical value when the map changes (a wall gets
  destroyed, a door opens), even though the game's own pathfinding graph already tracks that live.

The dynamic system adds a **fallback**: when no marker scores well, the NPC instead queries the
existing navmesh-like poly graph maintained by `PathfindingSystem` for reachable candidate
positions near a reference point, scores them with the same style of utility curves already used
for markers, and picks the best one - subject to a reservation table so concurrent NPCs don't
converge on the same spot. Markers are left fully functional and are always tried first, so mapper
intent still wins wherever markers exist, and no map files need to change.

## Algorithm

**Navmesh-based tactical position query**, reusing `PathfindingSystem`'s poly graph instead of
inventing a new spatial structure:

1. A pathfinding request (`TacticalPathRequest`) floods the poly graph (Dijkstra-style, mirroring
   the existing `UpdateBFSPath`) from a reference coordinate out to a max range, and returns the
   visited `PathPoly` nodes as candidates (capped count) instead of collapsing to one random route
   like `GetRandomPath` does.
2. Each candidate's coordinate is scored inline: distance-from-reference, LOS/occlusion via
   `ExamineSystemShared.InRangeUnOccluded`, an optional facing-cone (FOV) check, random jitter, and
   a claim-table penalty - combined via the same running-geometric-mean approach
   `NPCUtilitySystem` already uses for markers, for consistency.
3. The highest-scoring candidate wins; a claim is registered against it so other concurrent
   queries avoid/penalize nearby positions for a short time.

This directly solves "markers don't react to environment": the poly graph is already
reachability-aware and already updates as the map changes, so no new environment-tracking code was
needed - only a new way to enumerate it.

Requests go through `PathfindingSystem`'s existing time-sliced queue (`_pathRequests`, a shared
3ms/tick budget across all pathfinding work), the same mechanism `PickAccessibleOperator`/
`GetRandomPath` already use, so this does not introduce a new source of per-tick cost spikes.

## Key files

| File | Role |
|---|---|
| `Content.Server/_KS14/NPC/Pathfinding/TacticalPathRequest.cs` | `PathRequest` subclass that collects flood-fill candidates instead of reconstructing one route. |
| `Content.Server/NPC/Pathfinding/PathfindingSystem.Klovn.Tactical.cs` | Upstream-namespace partial file adding `GetTacticalCandidates(...)` and the BFS-style flood (`UpdateTacticalPath`). Marked `// KS14: added in this fork` since it extends an upstream class outside `_KS14/`. |
| `Content.Server/_KS14/NPC/Systems/NpcTacticalPositionClaimSystem.cs` | The reservation/claim table preventing NPCs from converging on the same dynamically-picked spot. |
| `Content.Server/_KS14/NPC/HTN/PrimitiveTasks/Operators/TacticalPositionOperator.cs` | The single YAML-tunable `HTNOperator`: tries the marker query first, falls back to the dynamic algorithm. |
| `Content.Server/NPC/Pathfinding/PathfindingSystem.cs` | Marked upstream edit: dispatches `TacticalPathRequest` in `Update()`'s switch. |
| `Content.Server/NPC/Systems/NPCUtilitySystem.cs` | Marked upstream edit: `GetScore`/`GetAdjustedScore` changed `private -> internal` so the operator reuses the exact same curve-evaluation math instead of duplicating it. |
| `Resources/Prototypes/_KsModule/NPCs/Operative/operative_hostile.yml`, `operative_retreat.yml`, `operative_advance.yml` | Camping/retreat/advance HTN tasks, each configuring `TacticalPositionOperator` with behavior-specific reference keys and curves. Existing marker `utilityQuery` prototypes are untouched and still used for the marker phase. |
| `Resources/Prototypes/_KsModule/Entities/Mobs/Operative/base.yml` | Per-mob blackboard tuning (`CampingTime`, `AdvanceTime`, `RetreatClaimDuration`, etc.) that sizes claim durations. |

## How `TacticalPositionOperator` works

`TacticalPositionOperator` is a single operator reused (with different `[DataField]`s) for
camping, retreat, and advance - not three separate C# classes. Its `Plan()` runs in two phases:

1. **Marker phase** (optional, via `MarkerProto`): runs the existing marker `utilityQuery`
   unmodified through `NPCUtilitySystem.GetEntities`. If it finds a valid highest-score marker,
   that wins immediately - mapper-placed spots always take priority over computed ones. No claim
   is registered for marker picks (anti-stacking is out of scope for the marker pool; mappers
   already space markers out by hand).
2. **Dynamic phase** (fallback): reads `ReferenceCoordinatesKey` from the blackboard (e.g.
   `LastTargetCoordinates` for camping, `OwnerCoordinates` for retreat/advance), floods the poly
   graph via `PathfindingSystem.GetTacticalCandidates`, and scores every candidate with whichever
   of the following considerations are configured:
   - **Distance** (always applied) - via `DistanceCurve` against `ReferenceCoordinatesKey`.
   - **LOS** (optional, via `LosReferenceCoordinatesKey`/`LosRadius`/`LosCurve`) - occlusion check
     between the candidate and a reference point. Wrap `LosCurve` in an `InverseBoolCurve` in YAML
     to prefer concealment instead of visibility.
   - **FOV** (optional, via `FovReferenceCoordinatesKey`/`FovAngle`/`FovCurve`) - is the candidate
     within a facing cone from the owner toward a reference point.
   - **Random jitter** (optional, via `RandomProbability`) - adds variety so NPCs don't always
     pick the literal highest-scoring tile.
   - **Claim penalty** (always applied) - queried from `NpcTacticalPositionClaimSystem`.

   The winning candidate's coordinates are written to `KeyCoordinates` (feeding directly into
   `MoveToOperator.TargetKey`, following the existing task-local key convention, e.g.
   `CampingTargetCoordinates`/`RetreatTargetCoordinates`), and a claim is registered for it.

`TacticalPositionOperator` also implements `IHtnConditionalShutdown` and overrides
`TaskShutdown`, both releasing the NPC's claim early when its task ends - a fast path on top of
the claim table's own TTL sweep.

## The claim/reservation table

`NpcTacticalPositionClaimSystem` prevents NPCs from converging on the same computed spot - a
problem the old marker system didn't have (a finite, mapper-spread pool of markers naturally
spread NPCs out; a "pick the best point" algorithm without this table would repeatedly return the
same optimal tile to every NPC asking).

- One claim per owning NPC (`Dictionary<EntityUid, TacticalClaim>`), so claiming/releasing is O(1).
- `GetClaimPenalty` linearly scans live claims on the same map, discouraging (not hard-excluding)
  candidates within a claim's clearance radius - bounded by how many NPCs are concurrently
  camping/retreating/advancing, not total NPC count.
- TTL is derived per-call from the calling operator's own duration blackboard key (`CampingTime`,
  `AdvanceTime`, or the retreat-specific `RetreatClaimDuration`) plus a small safety buffer, so the
  claim system itself stays behavior-agnostic.
- Expired claims are swept in `Update()`; `TacticalPositionOperator`'s shutdown hooks release
  claims immediately when a task ends normally, so the TTL sweep is only the safety net for
  ungraceful termination (NPC deleted mid-task, plan aborted before shutdown hooks run).
- Not thread-safe by design: every read/write happens from main-thread HTN callbacks
  (`Plan`/`ConditionalShutdown`/`TaskShutdown`) or the system's own `Update` - never from
  `PathfindingSystem`'s parallel path-processing.

## Adding a new behavior that uses this system

To wire a new HTN behavior onto the dynamic algorithm:

1. Add a `!type:TacticalPositionOperator` task in the relevant `htnCompound`/`htnPrimitiveTask`
   YAML, same as the existing camping/retreat/advance tasks.
2. Set `referenceCoordinatesKey` to whatever blackboard coordinate the new behavior should search
   around.
3. Optionally set `markerProto` to an existing (or new) marker `utilityQuery` prototype if the
   behavior should still prefer mapper-placed spots first.
4. Tune `distanceCurve`/`losReferenceCoordinatesKey`/`fovReferenceCoordinatesKey`/
   `randomProbability` to taste - these are the same considerations described above.
5. Point a following `MoveToOperator`'s `targetKey` at the same `keyCoordinates` value.
6. Give the operator a `claimDurationKey` pointing at a blackboard float that represents roughly
   how long the NPC will occupy that spot, so the reservation table sizes itself sensibly.

## Debugging

Run the `ks_tacticalposdebug` console command (`[AdminCommand(AdminFlags.Debug)]`,
`Content.Server/_KS14/NPC/Commands/TacticalPositionDebugCommand.cs`) to toggle a per-player debug
overlay of the dynamic phase: every scored candidate (colored red-to-green by score), the chosen
candidate (yellow outline), and live reservation-table claims (orange clearance-radius circles).

Run it bare (`ks_tacticalposdebug`) to track every NPC using the query, or pass an entity ID
(`ks_tacticalposdebug 1234`, tab-completable against entities with `HTNComponent`) to scope the
overlay to just that one NPC. Running the command again with the same scope (no argument twice, or
the same entity twice) turns it back off; passing a different entity switches the scope instead of
disabling. The shell output always states the resulting scope ("tracking all entities" /
"tracking <entity> only" / "disabled").

This is deliberately lazy: `NpcTacticalPositionDebugSystem.IsTracking(owner)` gates whether
`TacticalPositionOperator` does any of the extra work needed to report a debug frame (recording
every candidate's coordinates/score, snapshotting the claim table via
`NpcTacticalPositionClaimSystem.GetAllClaimsForDebug`) for that particular NPC. While nobody is
tracking a given NPC - either because no one has the overlay toggled on at all, or because every
subscriber is scoped to a *different* entity - none of that bookkeeping happens for it; only the
actual candidate scoring the NPCs need to function is ever computed. The command tracks a
`Dictionary<ICommonSession, EntityUid?>` server-side (`NpcTacticalPositionDebugSystem`, null value
= tracking everything); frames are only broadcast to sessions whose scope matches the reporting
NPC, and get pruned client-side after ~2 seconds of no update
(`Content.Client/_KS14/NPC/TacticalPositionDebugSystem.cs`, `TacticalPositionDebugOverlay.cs`).
