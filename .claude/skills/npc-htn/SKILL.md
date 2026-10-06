---
name: npc-htn
description: How NPC behaviour is built in this repo - HTN compounds, operators, preconditions, utility queries, and the EntitySystems that feed them (perception, squads, squad tactics, sensors). Use when adding or changing NPC behaviour, writing an HTN operator, precondition, query or consideration, editing operative HTN YAML (Resources/Prototypes/_KsModule/NPCs), or testing any of it.
---

# NPC behaviour: HTN and the systems around it

An NPC's behaviour is an HTN plan, rebuilt every 0.45s from a root compound, driven by facts that `EntitySystem`s
work out and keep on components. Most bugs in this area come from putting work in the wrong layer, or from the
planner's replacement rule. Both are covered below, followed by how to test what you build.

## The four layers

Each NPC feature is split four ways, and each part has one job:

| Layer | Lives in | Does |
| --- | --- | --- |
| **Sense** | an `EntitySystem`, on a throttled `Update` | Works out facts and keeps them on a component. The only layer that does lookups and raycasts on a schedule. |
| **Expose** | `UtilityQuery`, `UtilityConsideration`, `HTNPrecondition`, an operator's `Plan` | Reads that component through the system's public API. Changes nothing. |
| **Act** | an operator's `Startup`, `Update`, `TaskShutdown` | Every side effect on the world or on a system. |
| **Notify** | the sensing system | Tells the planner something changed: `NpcSensorSystem.RequestReplan(uid)`, and nothing else. |

### Worked examples

- **`NpcPerceptionSystem`** (`Content.Server/_KS14/NPC/Perception/`) decides what each operative can see, has lost,
  or believes is hiding in a locker, and keeps it in `NpcPerceptionComponent.Contacts`. Deaths are beliefs too
  (`NpcPerceptionSystem.Deaths.cs`): a hostile is known dead (`KnownDead`) only once seen dead, killed by this NPC,
  or reported by a squadmate that knows, so nothing should test `MobStateSystem.IsDead` on a hostile to decide what
  an NPC knows. Ask `IsKnownDead`. It asks for a replan when a
  target appears, vanishes, is reacted to or is called out. HTN reads it through `PerceivedContactsQuery`,
  `HasContactPrecondition` and `ContactVisiblePrecondition`. No operator ever raycasts for sight itself.
- **`NpcSquadTacticsSystem`** (`Content.Server/_KS14/NPC/Squad/Tactics/`) is the same shape one level up. It reads
  every member's perception, decides what each should do about a hostile the squad has lost (watch, stack up,
  go in, search, hold) or about nothing happening (regroup), and keeps the answer in `NpcOrderComponent`. HTN enters
  an order through `HasOrderPrecondition`, reads it with `GetOrderOperator`, and every task carrying it out rechecks
  `OrderCurrentPrecondition`. A changed order gets a new id and a `RequestReplan`, so it is taken up on the next tick.
- **`NpcSquadCoverSystem`** works out which member covers which way into a room. **`NpcLineOfSightSystem`** is the
  one allocation-free line-of-sight check that perception and tactics share, so they never disagree about what can
  be seen. Use it for every NPC sight check, never `ExamineSystem.InRangeUnOccluded`: it walks the tiles a ray
  crosses and reads the occluders anchored there live, about three times faster than the occluder tree, and it is
  right about rays lying exactly along a tile edge, which the tree finds blocked by any wall on that line. Occluders
  the walk cannot find (unanchored, or wider than their tile) carry `NpcIrregularOccluderComponent` and are tested on
  their own. `KsNpcLineOfSightTest` checks it against the tree on thousands of rays across a real station. To check
  many targets from one viewpoint, build an `NpcSightField` (`BuildSightField`) and ask it instead: it gathers the
  occluders in range once, by direction, and gives the same answers as rays. It pays off for a short range and many
  targets (a room sweep at 7 tiles: about three times faster), not for a long range and few targets (exposure at 15
  tiles with 32 spots is slower than rays).

When a new feature needs the NPC to *know* something, it gets a system and a component. When it needs the NPC to
*do* something, it gets operators. When it needs a *decision* spanning several NPCs, it gets a system that writes
per-NPC results (like orders) for HTN to read.

## Rules that follow from the layers

### `Plan` has no side effects

The planner runs `Plan` for every branch it considers, every 0.45s, and throws most of those plans away. An operator
that reports to a squad or starts a timer in `Plan` does it for plans that never run. Do it in `Startup`.

The one exception is `SquadCoverOperator`, whose `Plan` passes the threat on before reading back the cover plan built
around it. That is allowed only because restating a threat is idempotent, and the operator says so.

A pure read in `Plan` is fine, including an on-demand raycast that only that operator needs. `DiveOperator` does
this: it works out where a dive would land when planning. Sense-layer systems are for facts that must be kept up to
date whether or not anything asks.

### The blackboard holds the plan's working values, not world state

The chosen target and where to go belong on the blackboard. What the NPC knows belongs on a component.

"Where something was" is a snapshot. `UtilityOperator` writes its target's coordinates as `EntityCoordinates(target, 0)`,
which follow the target wherever it goes, through walls included. Copy them with `CopyKeyOperator`'s `snapshot: true`,
or take `believedCoordinates` from the perception system, whenever they mean "last seen here".

### Positions and angles belong to their grid, not the world

Grids move and turn: shuttles, and anything else not anchored to a map. Keep anything a system holds on to for
longer than one update relative to its grid: `EntityCoordinates` on the grid, tile indices, and angles relative to
the grid. Put them into world space only at the moment of use, with the grid's current transform.

- A cached `MapCoordinates` or world position is wrong as soon as the grid moves. Hunts keep search points as
  coordinates and tiles, and convert them each update.
- An angle meant as "into the room" is grid-relative. `NpcOrder.Facing` is relative to its coordinates' parent,
  and `RotateToOrderFacingOperator` adds the parent's world rotation every update while it turns. The upstream
  `RotateToTargetOperator` takes a world angle written once at plan time, which is stale by the time a member that
  walked somewhere first comes to turn.
- To work over many tiles at once (a coverage sweep, say), take the grid's world and inverse matrices once per
  update, and transform points between grid and world with them.

### Blackboard keys go stale

Nothing removes a key when the plan that wrote it ends, unless an operator does so in `TaskShutdown`. So
`KeyExistsPrecondition` on a key some earlier plan wrote can pass on a leftover value. Two ways out:

- Gate on the system's state instead of a key. `HasOrderPrecondition { withTarget: true }` reads the order itself,
  rather than checking whether `OrderTarget` happens to be on the blackboard.
- Fold "work out the value" and "act on it" into one operator that writes the key in `Plan` and removes it in
  `TaskShutdown`. `DiveOperator` replaced a `GetDivePoint` → `DoWorldAction` chain for exactly this reason.

### Everything NPCs tell each other has a switch

How smart a squad is should be tunable per NPC, and most of what makes one smart is what its members pass on. So each
way an NPC shares something with its squad gets a `[DataField]` bool on the component of the NPC doing the telling or
the listening, defaulting to on: `NpcPerception.callsOutContacts`, `hearsCallouts` and `sharesKills`, and
`NpcDoorUser.warnsSquad`. Acting on what it was told gets its own switch on the listener
(`NpcSquadMember.respondsToCallouts`, `calloutResponseRange`, read by `AnswersCalloutPrecondition`). A new way of
sharing gets the same treatment, and a row in the operative README's *Squad talk* table.

Sharing is an event. The caller checks its own switch, builds a `[ByRefEvent]` and hands it to
`NpcSquadSystem.CallOut`, which raises it on every other member. Each listening system subscribes to it and checks
the listener's switch (`NpcContactCalloutEvent`, `NpcKillCalloutEvent`, `NpcDoorRefusedCalloutEvent`). Nothing loops
over squad members by hand to tell them something. The same event instance goes to each member in turn, so a handler
can leave an answer on it for the caller, as `NpcKillCalloutEvent.News` does.

### A running task does not notice the world change unless it asks

Preconditions are checked when a task is planned, never while it runs, and a plan only ends early if a replan beats
it (see below). A task that must stop the moment a fact stops holding sets `recheckPreconditions: true`. That re-runs
its preconditions every update and fails the task, replanning at once, when one breaks. Keep those preconditions
cheap: they run every tick.

Rechecks run against the **live** blackboard, not the planner's copy. Plan effects are applied when their task
starts (`applyEffectsOnStartup`, on by default), so a key written by an earlier task in the same plan is there. A key
an earlier task only wrote with `applyEffectsOnStartup: false` is not, and the task fails at once.

Typical uses: `GunOperator` rechecks `ContactVisiblePrecondition` and stops shooting at a target that has ducked out
of sight; every order task rechecks `OrderCurrentPrecondition`; `WaitForOrderChangeOperator` returns `Continuing`
forever and only ever ends through its recheck.

**A rechecked precondition must also hold while planning.** The planner checks it like any other precondition, and
at plan time nothing earlier in the plan has *run*: only plan effects exist. So "until a cooldown that an earlier task
starts runs out" never plans, because the cooldown has not started yet. Write the moment as a plan effect instead:
`SetTimeOperator { key, offset }` (its `Plan` returns the time as an effect), then recheck
`KeyTimePassedPrecondition { key, invert: true }`. That holds while planning and stops holding `offset` later.

**Giving way on a timer.** That is also how a long-running task makes room for a newer version of its own plan,
which a replan onto the same branch never replaces. Fighting from cover (`gun.yml`) keeps a firing spot for 4s this
way. The gun task's recheck ends it, the next plan picks a new spot, and the branch is gated on the same time, so the
costly spot search only runs when one is due. Give such a timer a `jitter`: NPCs that start together otherwise stay
in step for good, and their costly searches all land on the same tick.

### A background move shares the NPC with the tasks after it

A `MoveToOperator` with `shutdownState: PlanFinished` or `Never` reports itself finished at once and keeps steering
while the plan carries on: shooting while closing in, reloading while retreating. Anything steering does on its own -
opening, prying or forcing a door - then happens while those tasks use the same hands, and the two fight over them: an
access breaker came out mid-reload and was dropped by the reload. The operator marks such a move with
`NpcBackgroundMoveComponent`; check it (`NpcDoorSystem.IsMovingInForeground`) before steering takes anything into its
hands, and leave hand work to moves that are the task at hand.

## The planner: what wins

### First branch that plans, top to bottom

A compound takes the first branch whose preconditions hold and whose tasks all plan. It does not fall through on
anything else. So a branch that **always** plans makes every branch after it dead. Examples are a compound whose last
branch has no preconditions, or a primitive whose `Plan` always returns valid, such as the sensors branch's final
`HandleSensorsOperator`.

Before placing a root branch, trace every branch above it, including inside nested compounds, for an unconditional
catch-all. A branch that only pulls or handles data should be gated on there being data; see
`HasPendingSensorDataPrecondition`.

### Replacement: lower at *any* index

Every replan (each 0.45s, or at once after `RequestReplan` or a failed task) produces a branch traversal record: the
branch index taken at each compound, outermost first. `HTNSystem` replaces the running plan if the new record is
lower than the old one **at any index**, not lexicographically:

```csharp
for (var i = 0; i < oldMtr.Count; i++)
{
    if (i < mtr.Count && oldMtr[i] > mtr[i])
        newPlanBetter = true;
}
```

Consequences:

- **Nesting depth is load-bearing.** Adding or removing a compound level shifts every index after it. Every wrapper
  compound in the operative YAML carries a comment saying why it exists. Do not collapse one without checking what
  that does to the record.
- **Two branches of one compound can interrupt things they should not.** A running plan on branch 1 of compound X
  is beaten by any replan that took branch 0 at the same depth, even if that replan sits lower in the root. Squad
  orders hit this: as branches of one `KsOperativeOrderCompound`, a Watch order (branch 1) lost to the room hold
  (branch 0). The fix was one root branch per order kind, so the kind is decided at index 0, where root order means
  what it says. The search compounds split into two root branches for the same reason.
- **A replan onto the same branch never replaces the running plan.** Its record is equal, not lower. If the running
  plan must give way to a newer version of itself (a new order, a new target), use `recheckPreconditions`.
- **Optional tasks.** `optional: true` on a primitive skips it when its preconditions *or its `Plan`* fail. On a
  compound, it skips the whole compound when no branch plans, and a skipped compound adds nothing to the record. So
  an optional compound makes the record a different length depending on whether it planned. Prefer optional
  primitives.

### Root order (operatives)

`Resources/Prototypes/_KsModule/NPCs/Operative/root.yml` (private submodule) runs:

1. Play dead
2. Healing
3. Grenades
4. Target down (a one-line callout, from `NpcPerceptionSystem`; interrupts a fight for a tick)
5. Ranged, then Melee
6. Support: peek, then push (backing up a squadmate fighting a hostile this one can't see; see `support.yml`)
7. Follow
8. Search open, then Search cover
9. Door refused (a one-line callout; see Doors below)
10. Orders: Investigate, Watch, Stage, Breach, Enter, Search locker, Search spot, Hold area, Regroup
11. Sensors
12. Join squad
13. Disengage
14. Squad hold

The header of that file lists every blackboard key and who writes it. Keep it current when you add one.

Every tuning setting for operatives is listed in `README.md` in the same folder. That covers component fields,
blackboard keys, values set inline in the HTN files, cvars, and what is fixed in code. When you add or change a
setting, update that file too.

## Meters

A meter (`NpcMeterPrototype`, prototype type `npcMeter`) is a value from 0 to a maximum that something raises and
time brings steadily back down: caution, say. It is lazy, like a battery's charge. `NpcMetersComponent` holds, per
meter, a value and when it was set, and `NpcMeterSystem.GetValue` works out the decay since. Nothing ticks meters,
and there is no update loop to add to. A meter never raised reads 0.

Opting in is all YAML, by meter id:

| To | Use |
| --- | --- |
| Raise one when a squadmate goes down | `NpcMeterOnSquadLoss { meter, critical, dead }` on the mob |
| Raise or lower one in a plan | `AddMeterOperator { meter, amount }` (acts in `Startup`, not `Plan`) |
| Gate a branch or task on one | `MeterPrecondition { meter, min, max }` |
| Weigh a utility choice by one | `MeterCon { meter }`, scored as a share of the max |
| Have a system read one | a `ProtoId<NpcMeterPrototype>?` datafield on its settings, as `NpcSquadTacticsSettings.CautionMeter` |

A new source of a meter is a component and a system that call `NpcMeterSystem.Add`. Reacting to a squad member going
down goes through `NpcSquadMemberDownedEvent`, which the squad system raises before it takes the member out. Don't
subscribe to `MobStateChangedEvent` on squad members yourself: the squad system already does, and only one system
may. `ks_squaddebug` draws every meter a member has as a bar under it.

## Kill zones

`NpcKillZoneSystem` remembers where NPCs' own went down, per faction, on the grid it happened on. A zone is flooded
once when it is made (`NpcSquadCoverSystem.FloodTiles`: walkable tiles, never through walls, stopping at doorways),
so reading one is a tile lookup with no distance maths, and it covers the room someone fell in and not the next.
`NpcKillZoneOnDown` on a mob makes zones. Anything picking a position opts in to avoiding them by scaling its score
with `GetDanger`: `TacticalPositionOperator.killZoneAvoidance` and the squad `cover:` setting of the same name. Expired
zones are skipped on read and pruned on the next add, so nothing ticks them.

## Exposure

`NpcExposureSystem` answers how hidden a spot is from a threat's **approach**, not just from where the threat stands.
`GetApproachProbes` samples the approach once: the threat's position, and floor it could walk to within a few steps
(`FloodTiles` with `stopAtDoors: false`), spread from near to far. `GetExposure` is the share of those probes with a
line of sight to a spot. A spot just round a corner is hidden from the threat and seen from one step on; deep cover
is hidden from all of it. `TacticalPositionOperator` opts in with `exposureReferenceCoordinatesKey` (plus reach,
probes, radius and a curve), working the probes out once per plan rather than per candidate. Retreats use it to find
real cover. The gun branch uses it to pick firing spots that see the target from as few of its angles as possible.

It is the dearest consideration by far (a line of sight check per probe per candidate, about 2µs each), so it is
kept in check two ways:
- **Pruned.** Exposure can only lower a score, so the operator scores every candidate on everything else first, then
  weighs exposure best first and stops once no candidate left could beat the best so far.
- **Budgeted.** Every check comes out of a budget shared by all NPCs and refilled each tick
  (`klovn.npc.exposure_ray_budget`). A search that runs out part way keeps its best so far. One that cannot afford
  even one candidate either fails, to be tried on a later replan (`deferWhenOverBudget`, for searches that can wait),
  or picks without exposure (for ones that cannot, like a retreat).

The same shape - prune what cannot win, cap what is left per tick - fits any new on-demand query that is cheap alone
and dear in bulk.

## Voice sets

`SpeakOperator` speaks from the line set its task names, through the speaker's voice set if it has one:
`NpcVoiceSet { set }` names an `npcVoiceSet` prototype, which maps line sets to replacements. One HTN can then serve
several factions that each sound like themselves. Write lines against the standard set, and give the other factions
a voice set. A voice set can also list line sets under `silenced:`, for lines that are wrong for that faction
altogether. The task then succeeds without a word.

Server-only prototype types (`npcMeter`, `npcVoiceSet`, `htnCompound`, ...) must be ignored on the client
(`RegisterIgnore` in `Content.Client/Entry/EntryPoint.cs`), since the client loads the same prototype files.

## Doors

`NpcDoorSystem` keeps two things apart: what an NPC **believes** about a door, and what the door would **do**.
- **Belief** (`GetDoorAccess`) goes by what anyone can see: the access list the door was built with
  (`AccessReaderComponent.AccessListsOriginal`, what examining shows), emergency access, bolts, a weld, power. So a
  door whose access was changed behind the NPC's back fools it.
- **Forcing** (`TryGetBreachTool`) uses the game's own checks: `BeforePryEvent` raised on the door for prying tools,
  and the access breaker's charges and cooldown.
- **Being fooled** is noticed where it happens: steering's door handling (`NPCSteeringSystem.Obstacles.cs`) asks for
  the belief before `TryOpen`, and reports a failure on a door it believed `Openable` (`ReportRefused`). The door
  becomes a no-go in each squad member's own `NpcDoorUserComponent`, never on the squad.

**Words.** "Breach" means forcing a door with a tool and nothing else; going in is "entry" (`NpcHuntPhase.Entry`,
`NpcOrderKind.Enter`). Keep them apart in new code.

**Speaking from a system.** A line the system wants said at once can't go through sensors: sensor data reaches the
blackboard only when the sensors branch runs, and that is below orders. Do it like perception instead: a flag on a
component (`NpcDoorUserComponent.RefusedAt`), a precondition reading it (`DoorRefusedPrecondition`), a root
branch placed where it should interrupt, and an operator clearing it in `Startup` (`AcknowledgeDoorRefusedOperator`).

**Taking something out.** Use `NpcHandsSystem` (`CanTakeOut`, `TryTakeOut`, `PutBack`); don't write your own. A
hand holding a virtual item counts as free, because wielding fills the other hand with one. Freeing it means
*dropping* the virtual item, which empties the hand at once. Unwielding instead only queues the virtual item's
deletion for the end of the tick, so a following `TryGetEmptyHand` finds nothing. That is how a breacher used to
unwield and wield again every tick without drawing, and was left with an orphaned virtual item and no plan that could
run.

**Tools in hand.** A breach is one job owned by `NpcDoorSystem` (`TryStartBreach`, `StopBreach`), with its progress
on `NpcBreachingComponent`: draw and use, then the system's `Update` ends it when the door opens, the tool fails or time
runs out, and puts the tool back with the weapon in hand. Two things start one: `BreachDoorOperator` for a Breach
order (it stops the breach in `TaskShutdown` if cut short), and steering, for a door in the way that will not open
(`TryBreachBlockingDoor`, off with `NpcDoorUser.breachWhenBlocked: false`); a breach steering started also ends when
steering does. Not a chain of draw, use and stow tasks: one cut short halfway would leave the tool in hand with nothing
to put it away. Note `TrySetActiveHand` returns false for a hand that is already active.

**Pathfinding** flags bolted and welded doors (`PathfindingBreadcrumbFlag.Bolted`, `.Welded`) as walls, and rebuilds
the chunk when bolts, welds or emergency access change (`PathfindingSystem.Klovn.Access.cs`). A crumb where a door
shares the space with anything else anchored and solid (a window under shutters) loses its door flags, so it isn't a
doorway to paths or to room detection, which takes doorways from `Door`. The navmesh is shared, so per-NPC knowledge
goes on the request instead. When steering gives up at a door it could neither open nor force, it reports it
(`NpcDoorSystem.ReportBlocked`), and that NPC's later path requests carry the door's tile as a wall
(`PathRequest.AvoidedTiles`, `PathfindingSystem.Klovn.Avoid.cs`). Path requests run on worker threads, so anything
`GetTileCost` reads has to be captured on the request when it is made.

**Testing doors.** Test grids have no power, and an unpowered airlock opens for nobody by hand: spawn doors with
`SpawnPoweredDoorAt`. A wall meant to force a path through a door has to run the whole width of the grid.

## Long paths

A* runs out of nodes (`klovn.npc.path_node_limit`) long before a path across a station when it only knows the
straight-line distance. `PathfindingSystem.Klovn.Hierarchy.cs` gives it a far better estimate: per 8x8 navmesh chunk
and per path profile (collision and `PathFlags`), a coarse map of its walkable regions and the polys on the chunk's
edge, with what it costs to walk between them. Each request searches back from its goal over those, towards its
start, before A* begins, which says what is left to walk from anywhere near the way. So A* walks almost straight down
its path (at most 238 expansions on Box), and a goal that cannot be reached is given up on without searching. Coarse
maps are built when first needed and dropped when their chunk or a neighbour is rebuilt.

Two things it does not know: an NPC's own avoided tiles (doors it has found it cannot get through), which only make the
walk longer than it thinks, so it stays a lower bound and paths stay the shortest; and anything that changes the
navmesh without marking the chunk dirty, which the pathfinder misses with or without the coarse maps. Deleting a wall
used to be one: its move to nullspace was looked up on the grid it had already left. Note that an unanchored wall still
blocks its tile: the navmesh counts any hard body there, anchored or not.

## Moving somewhere without going through somewhere

The pathfinder only knows the shortest way, and it cannot be told to avoid a set of tiles: the `HtnPathfindingModifier`
hook is a stub. When the way matters, as for a member stacking up on the door on the far side of a room, which
the shortest path reaches straight through the room, the tactics system routes it itself.
`NpcSquadCoverSystem.TryFindRouteAround` floods the same tile classification as room detection, keeping off the
avoided tiles. It then cuts the corners off the route, leaving only the turns. The member is ordered to each turn in
turn, and moved on to the next shortly before arriving so it rounds corners rather than stopping at them. Between two
turns the straight line is clear, so the pathfinder's shortest way there is the intended one. Routes are worked out
once, when the hunt sets up, and also decide which member takes which door (the shortest walk round, not the
straight-line distance).

## Writing the pieces

- **Operators** subclass `HTNOperator`. `[Dependency]` works on them through the system dependency collection, so
  systems and `EntityQuery<T>` inject. They have no `ProtoMan`; declare `IPrototypeManager` if needed.
- **Preconditions** subclass `HTNPrecondition` and implement `IsMet(blackboard)`. Read the owner with
  `blackboard.GetValue<EntityUid>(NPCBlackboard.Owner)`.
- **Utility queries:** a custom query overrides `UtilityQuery.AddEntities`, the way a consideration overrides
  `UtilityConsideration.GetScore`, so a new one needs no case in `NPCUtilitySystem`.
- **Voicelines:** `SpeakOperator` ignores `cooldown` without a `cooldownID`. A line said once per squad uses
  `InSquadPrecondition { leaderOnly: true }`, or-ed with `InSquadPrecondition { invert: true }` for NPCs on their own.
- **Actions:** `DoActionOperator` and `DoWorldActionOperator` only check that their target key exists at plan time.
  A world-targeted jump (`KsGravityJumpWorldEvent`) throws the performer all the way to its target, uncapped, so
  clamp the target yourself (see `DiveOperator`). Gate "only some NPCs do this" on the action existing
  (`TryGetValidAction` in `Plan`), not on a blackboard flag.
- **Settings blocks on components** (a class of `[DataField]`s held in one field, like `NpcSquadMemberComponent.Cover`)
  get `[AlwaysPushInheritance]`. Without it, a child prototype that sets one field of the block replaces the parent's
  whole block, and every field it left out silently goes back to the C# default.
- **Throttled systems:** spread first updates across the interval at `MapInit`, so NPCs spawned together do not all
  update on one tick. Keep scratch collections as fields and reuse them; a system running per NPC several times a
  second should allocate nothing in steady state.

## Testing

Tests live in `Content.IntegrationTests/Tests/_KS14/NPC/`, with shared grids and test mobs in
`KsNpcSquadTestHelpers`.

- **Test through the root.** A unit test of a system can pass while the HTN branch that should use it is dead. Add
  at least one test that plans the real root compound and asserts which branch the plan takes
  (`KsOperativeHtnRootTest.PlanOperative`), and one for which plans may interrupt which, using the same rule as
  `HTNSystem` (`Replaces`).
- **The operative HTN is private.** Check `protoManager.HasIndex(root)` and `Assert.Ignore` when it is absent. Never
  name a private prototype in a `[TestPrototypes]` string or a static `ProtoId` field: the first fails to load
  without the submodule, the second fails the YAML linter. To run an entity on the operative root, swap
  `HTNComponent.RootTask` at runtime.
- **Drive systems by hand.** Test mobs keep HTN disabled and are asleep, so systems that only update active NPCs
  leave them alone. Move things along explicitly:
  - `NpcPerceptionSystem.UpdateNow`, and `SetContact` to stage a belief without staging the scene;
  - `NpcSquadTacticsSystem.UpdateNow`, `UpdatesPaused`, and `IssueOrder` to give an order by hand.
  - A pause like `UpdatesPaused` must reset on `RoundRestartCleanupEvent`, or it leaks into the next test on the
    same pooled server.
- **`[Access]` reaches through members.** Writing `component.Tactics.WatchTime` from a test is an `RA0002` write to
  `Tactics`. Use a test prototype with the setting instead.
- **Components a system silently requires:** NPC ranged combat skips any NPC without `CombatMode`. Opening a locker
  needs `Hands` (`CanOpen` returns false without them). Steering, and so juking and moving at all, only runs for NPCs
  with `InputMover`. The shared test mobs have none of these, so a live test of movement or shooting needs its own.
- **Test mobs lack the operative blackboard.** Without `VisionRadius`/`AggroVisionRadius`, `TacticalPositionOperator`
  scores every candidate's distance as the far end of its curve, which with a near-preferring curve is 0: no spot
  is ever found. Stage the keys the branch reads (`KsOperativeHtnRootTest.StageArmed`).
- **A live fight ends for its own reasons.** A pistol empties in about four seconds, and an NPC out of ammo plans
  something else entirely. A test of what happens later in a fight has to keep the gun loaded (`KeepLoaded`) and
  the target alive (godmode).
- **Loose things block paths as anchored ones do.** The navmesh counts every hard body that blocks mobs, anchored or
  not, on the tile its centre is on, and blocks a whole tile one leaves too narrow for a mob
  (`PathfindingSystem.BlockNarrowGaps`). So a free-standing locker blocks a floor tile, and a wall locker (anchored, on
  the wall's tile) blocks nothing new. Test the case your rule is actually for.
- **Prove the test fails.** Break the rule it covers, rebuild, and watch it go red, one test per run. In a batch
  run, a test that fails inside `WaitAssertion` after another test dirtied its pooled pair can be reported as
  "NotExecuted: Test was dirty-disposed" rather than failed. The pool's `Assert.Warn` replaces the outcome, and
  the assertion message is gone from the trx and the console alike. Rerun the test alone. If it only fails in the
  full suite, temporarily wrap the body in a `try`/`catch` that appends the exception to a file, since console output
  is not shown either, and rerun until it shows.
- **Do not let a test depend on which member leads.** Squad leadership among identical test mobs is not fixed from
  run to run. A test that treats "the leader" and "the member" differently, while the code treats them by role, such as
  which door each was sent to, passes or fails by chance. Pick members by the role the assertion is about.
- **Test geometry the rule is for.** A plain box room is entirely in view from its door, so it cannot tell a
  thorough search from a shallow one; `KsNpcSquadTacticsTest` uses an L-shaped room entered along its long side. A
  facing test must turn the grid *before* the order is given as well as after, or a world-space facing issued at 0°
  passes too.
- **When `bin/` is locked** (a client or server running from it), build and test into another folder two levels
  under the repo, where the content root is still found as `../../`:
  `dotnet test ... -c Debug -p:OutputPath="<repo>/bin/kstest/"`.
- **Measure allocations** with `GC.GetAllocatedBytesForCurrentThread()` around a few hundred updates after a
  warm-up, and assert a bound. To find *what* allocates, see the `EventListener` approach in CONTRIBUTING.md §6.
- **Build `Release` too.** A member named like an inherited one (`Paused` on an `EntitySystem`) is only a warning in
  Debug, and an error in Release.

## In game

`ks_huntdebug [squad or NPC]` shows what squad tactics work out for a hunt, as they work it out:
- the room's floor, red where unseen and green once seen;
- its doorways;
- each door's waiting spot and entry point;
- each stager's route round the room;
- lockers and spots to check, and who is on each;
- sweep targets and orders;
- a label with the phase, its timers and how much of the room has been seen.

Like `ks_tacticalposdebug`, it builds nothing on its own. `NpcSquadTacticsSystem` asks `NpcHuntDebugSystem.IsTracking`
after updating a hunt, and hands over a frame of what it just computed only if someone is watching. A new debug view
of NPC reasoning should work the same way, intercepting the real computation rather than recomputing it on a timer.

`ks_setmeter <NPC> <meter> <value>` sets a meter on an NPC and everyone in its squad, to see how they act at a value
without having to get them there.

`ks_squaddebug` draws (and every meter a member has, as a labelled bar under it):

- every squad: leader, members, cover thresholds and assignments, and the threat;
- each member's beliefs: a line to a visible hostile, a ring where a lost one was with its guessed path, a square
  on a locker it is believed to hide in;
- the hunt, in brief: a diamond coloured by phase, triangles at door staging points, search spots (hollow once
  searched), and a line from each member to its order.
