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
  or believes is hiding in a locker, and keeps it in `NpcPerceptionComponent.Contacts`. It asks for a replan when a
  target appears, vanishes, is reacted to or is called out. HTN reads it through `PerceivedContactsQuery`,
  `HasContactPrecondition` and `ContactVisiblePrecondition`. No operator ever raycasts for sight itself.
- **`NpcSquadTacticsSystem`** (`Content.Server/_KS14/NPC/Squad/Tactics/`) is the same shape one level up. It reads
  every member's perception, decides what each should do about a hostile the squad has lost (watch, stack up,
  breach, search, hold) or about nothing happening (regroup), and keeps the answer in `NpcOrderComponent`. HTN enters
  an order through `HasOrderPrecondition`, reads it with `GetOrderOperator`, and every task carrying it out rechecks
  `OrderCurrentPrecondition`. A changed order gets a new id and a `RequestReplan`, so it is taken up on the next tick.
- **`NpcSquadCoverSystem`** works out which member covers which way into a room. **`NpcLineOfSightSystem`** is the
  one allocation-free line-of-sight check that perception and tactics share, so they never disagree about what can
  be seen.

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

- Gate on the system's state instead of a key. `HasOrderPrecondition { withStorage: true }` reads the order itself,
  rather than checking whether `OrderStorage` happens to be on the blackboard.
- Fold "work out the value" and "act on it" into one operator that writes the key in `Plan` and removes it in
  `TaskShutdown`. `DiveOperator` replaced a `GetDivePoint` → `DoWorldAction` chain for exactly this reason.

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

1. Healing
2. Grenades
3. Ranged, then Melee
4. Follow
5. Search open, then Search cover
6. Orders: Investigate, Watch, Stage, Breach, Search locker, Search spot, Hold area, Regroup
7. Sensors
8. Join squad
9. Disengage
10. Squad hold

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

## Voice sets

`SpeakOperator` speaks from the line set its task names, through the speaker's voice set if it has one:
`NpcVoiceSet { set }` names an `npcVoiceSet` prototype, which maps line sets to replacements. One HTN can then serve
several factions that each sound like themselves. Write lines against the standard set, and give the other factions
a voice set. A voice set can also list line sets under `silenced:`, for lines that are wrong for that faction
altogether. The task then succeeds without a word.

Server-only prototype types (`npcMeter`, `npcVoiceSet`, `htnCompound`, ...) must be ignored on the client
(`RegisterIgnore` in `Content.Client/Entry/EntryPoint.cs`), since the client loads the same prototype files.

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
  needs `Hands` (`CanOpen` returns false without them).
- **Pathfinding ignores unanchored entities.** A free-standing locker sits on a room tile. A wall locker (anchored,
  on the wall's tile) does not. Test the case your rule is actually for.
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
- **Measure allocations** with `GC.GetAllocatedBytesForCurrentThread()` around a few hundred updates after a
  warm-up, and assert a bound. To find *what* allocates, see the `EventListener` approach in CONTRIBUTING.md §6.
- **Build `Release` too.** A member named like an inherited one (`Paused` on an `EntitySystem`) is only a warning in
  Debug, and an error in Release.

## In game

`ks_huntdebug [squad or NPC]` shows what squad tactics work out for a hunt, as they work it out:
- the room's floor, red where unseen and green once seen;
- its doorways;
- each door's waiting spot and breach point;
- each stager's route round the room;
- lockers and spots to check, and who is on each;
- sweep targets and orders;
- a label with the phase, its timers and how much of the room has been seen.

Like `ks_tacticalposdebug`, it builds nothing on its own. `NpcSquadTacticsSystem` asks `NpcHuntDebugSystem.IsTracking`
after updating a hunt, and hands over a frame of what it just computed only if someone is watching. A new debug view
of NPC reasoning should work the same way, intercepting the real computation rather than recomputing it on a timer.

`ks_squaddebug` draws (and every meter a member has, as a labelled bar under it):

- every squad: leader, members, cover thresholds and assignments, and the threat;
- each member's beliefs: a line to a visible hostile, a ring where a lost one was with its guessed path, a square
  on a locker it is believed to hide in;
- the hunt, in brief: a diamond coloured by phase, triangles at door staging points, search spots (hollow once
  searched), and a line from each member to its order.
