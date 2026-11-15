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
  go in, search, hold) or about nothing happening (regroup), and keeps the answer in `NpcOrderComponent`. While anyone
  is fighting, it decides only what the squad does together in the fight (`NpcSquadTacticsSystem.Fight.cs`: a push, a
  flank), as orders over combat; everything else waits for the fight to end. HTN enters
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

### How the systems feed each other

The links below cross system and file boundaries, and nothing in any one file shows the whole chain. Before changing
one end of a chain, check the other end. An arrow means "is read by" or "calls".

- **Light, sight and aim.** `NpcLightDetectionSystem.GetLightLevel` caches the level per target per tick.
  - It goes by the ambient light a player sees, not the server's: a map's day and night (`LightCycleComponent`) is
    only applied to its colour on clients, and roofs (`RoofComponent` tiles, and every grid that is not the map,
    `ImplicitRoofComponent`) only shut it out of the client's lighting. It swaps the engine's ambient for that one.
  - `NpcPerceptionSystem.CanSee` reads it. With the observer's dark vision applied, it is kept on the contact as
    `NpcContact.SeenLight`.
  - `GetSeenLight` reads it from there for:
    - reaction time;
    - `GetNoticeRange` (support peek and push);
    - dark aim.
  - Dark aim: `NPCCombatSystem.UpdateRanged` → `KsApplyDarkAim`. It turns the aim point before
    `NpcSquadFireLaneSystem.TryGetClearShot`, so the friendly-fire checks judge the shot as it will really go. Don't
    move dark aim after that call.
- **Losing a hostile.** `LoseSight` (`NpcPerceptionSystem.Containers.cs`) keeps `GetTraits` on the contact: armed,
  flashable, breathes, sees through smoke.
  - `NpcSquadTacticsSystem` reads the traits when choosing what to soften a room with (`Plan.cs` `FindThrowers`,
    `Smoke.cs`).
  - A new trait needs both ends: work it out in `GetTraits`, read it in the softener choice.
- **Squad plans over combat.** Push and flank (`Fight.cs`), and the squad plan's steps (`Plan.cs`), give orders inside
  `using (GivePlanOrders())`, which marks them `OverCombat`. Only the `squad_plan.yml` root branch, above Ranged and
  Melee, carries those out. Ordinary orders sit below combat, so the same order given outside that block never beats a
  fight.
- **Flank routes.** `Fight.cs` `StartFlankProbes` → `PathfindingSystem.GetPathAvoiding`. That runs on worker threads,
  with the tiles by the hostile and the squad's own front, between each fighter and the hostile, as walls (not as a
  cost: a route through them is no route). Each probe needs its own tile set, because the path request adds the
  flanker's own avoided tiles to it and then reads it on a worker thread.
- **Pushing on a blast and on a pause in fire.**
  - `NpcGrenadeSystem` → `NpcSquadTacticsSystem.NoteBlast`, for the thrower's squad.
  - `NpcGunfireSystem` keeps `NpcRecentShooterComponent.LastShotAt` for every shooter. `Fight.cs` reads it to tell a
    hostile that has stopped firing (reloading) from one still shooting.
- **Lines of fire.** `NpcGunfireSystem` keeps every shooter's last shot, from anyone, on `NpcRecentShooterComponent`:
  - when it was fired;
  - where it was fired from;
  - where it was aimed.

  That makes a line of fire, to the first wall, for a second. Ask it from one NPC's side, `NpcFireSide.Friendly` or
  `Hostile`, where hostile means anyone not friendly:
  - `CrossesFire` for a path;
  - `IsUnderFire` for where it stands. `NpcPinnedDownSystem` (`Content.Server/_KS14/NPC/Pinning/`) asks it, on a 0.25s
    throttle and only for mobs with `NpcPinnable`, and raises the `KsSuppressed` meter: operatives read it in `gun.yml`
    (go to real cover, hold it longer) and `root.yml` (peek less). Elites have `gain: 0`. See the operative README's
    *Pinned down*.

  `DiveOperator` uses it now:
  - it never dives across friendly fire;
  - it avoids hostile fire when it can;
  - when straight ahead is blocked by fire (not by a wall), it turns aside to whichever side lands nearer the
    destination.

  Shooters don't allow for dives (`NpcSquadFireLaneSystem`); the diver does.
- **Grenades in paths.** `PathfindingSystem.Klovn.Blast.cs` makes every tile a primed explosive's blast would reach a
  `Blast` tile danger, around its landing spot while it's in the air, and every tile in range of a primed flash a
  `Flash` one. It reads `NpcGrenadeSystem.TryGetLanding` and `ExplosionSystem.IntensityToRadius`. A flash is no danger
  to an NPC it cannot blind (`SharedFlashSystem.CanFlash`, asked once per NPC per refresh).
  - Operatives avoid it through `NpcAvoidsTileDanger`.
  - The root's leave-danger branch is filtered to `dangers: Fire`, so getting out of a blast stays with the grenade
    branch.
  - Steering never refuses a step onto a danger the NPC is already standing in.
- **Cooked grenades.** A thrower with `NpcGrenadeThrower.cooks` lights a frag and holds it (`NpcGrenadeSystem.Cooking.cs`).
  - The hold belongs to the system, not to the task. `ThrowGrenadeOperator` only starts it and renews it each update, and
    `TaskShutdown` ends it. The system ends it too: time up, no renewal for 0.5s (death, sleep and a removed HTN end no
    task), or the NPC dead or unable to throw. Ending is always throwing it at the spot aimed for, or dropping it if it
    cannot be thrown. Never leave a lit grenade in a hand with only a task to end it.
  - The blast tile dangers skip what is in a container, so `NpcGrenadeSystem.TryGetLanding` answers for a cooked one too:
    where it is going. `TargetDangerRangeCon` and `grenade.yml` then have the squad leave the spot as the cook starts.
    The voice callout is made when the grenade is thrown, not when the cook starts, and not if it is dropped.
- **Shooting out lights.** `NpcLightShooterSystem` picks the light (two moments: the staging room's lit lights, or the light
  on a hostile who cannot see in the dark) and keeps it on `NpcLightShooterComponent`. `LightPickPrecondition` and
  `LightPickQuery` expose it, and `light_shoot.yml` shoots it with `GunOperator`, a light being a ranged target like any
  other (`NPCCombatSystem.Klovn.LightTargets.cs` aims at its fixture, not the entity's own spot, which is mid-tile). So the
  shot goes through `NpcSquadFireLaneSystem.TryGetClearShot` like every shot. A wall light breaks only when it is the
  target (`RequireProjectileTarget`), and any damage breaks the bulb. The root branch is above the squad plan and ranged
  combat, but `NpcLightShooterSystem` never picks a light while a hostile in sight can be hit, so that hostile is shot
  instead. Settings are on `NpcLightShooter`, listed in the operative README.
- **Man down.** `NpcSquadSystem` raises `NpcSquadMemberDownedEvent` on critical, dead, or gibbed while not already
  dead.
  - `NpcMeterOnSquadLossSystem` reads it (caution).
  - So does `NpcCallsignSystem`. It picks the nearest squadmate that saw it go down, within 12 tiles and in line of
    sight, and, 0.6s later, has `NpcVoiceSystem` say the callsign's line, or the plain man-down callout. It won't say it
    again for that member until it is alive.
- **Voices.** `SpeakOperator` and every callout go through `NpcVoiceSystem`.
  - `npcVoiceSet` maps an `NpcCallout` to line sets.
  - `npcVoiceRecording` maps each line's `LocId` to a clip. A line set picked in `SpeakOperator` must hand on the id,
    not the localised text, or no clip is found.
- **Healing in a closet.** In `healing.yml`:
  - `ClosetHealChancePrecondition` rolls;
  - `HidingPlaceCon` (→ `NpcHidingSystem.IsHidingPlace`) picks a closet;
  - `EnterEntityStorageOperator` (→ `TryHideIn`) gets in;
  - meds are used;
  - `HideOperator` waits.

  `NpcHidingSystem.Update` (4 Hz) decides when it comes out: healed, a hostile right outside, or in too long. The
  operator only ends when it does, and `StopHiding` runs however it ends.
- **Executions.** `ExecuteOperator` → `NpcExecutionSystem`. A cancelled do-after marks the victim with
  `NpcExecutionRefusedComponent`. `TargetAwaitsExecutionCon` then rules it out, so it is shot instead, until it is
  back on its feet.
- **A gun in the other hand.** `TakeUpGunOperator` (`gun.yml`, before "Pick up a gun") →
  `NpcHandsSystem.TryTakeUpGunInOtherHand`, for a magazine swap that was cut short.

## Rules that follow from the layers

### `Plan` has no side effects

The planner runs `Plan` for every branch it considers, every 0.45s, and throws most of those plans away. An operator
that reports to a squad or starts a timer in `Plan` does it for plans that never run. Do it in `Startup`.

The one exception is `SquadCoverOperator`, whose `Plan` passes the threat on before reading back the cover plan built
around it. That is allowed only because restating a threat is idempotent, and the operator says so.

A pure read in `Plan` is fine, including an on-demand raycast that only that operator needs. `DiveOperator` does
this: it works out where a dive would land when planning. Sense-layer systems are for facts that must be kept up to
date whether or not anything asks.

The same goes for anything `Plan`, a precondition or a consideration raises. To ask the game something, raise a
`Can...Event`, never an `...AttemptEvent`, whose handlers may respond to a try that never happened. See *Events* in
CONTRIBUTING.

### A fact worked out once is read, not worked out again

Nothing in HTN remembers anything between calls:
- every precondition runs again for each branch that has it;
- every consideration runs again for each candidate;
- a plan can span several ticks.

Perception (every 0.2s), planning (every 0.45s) and combat (every tick) each run on their own schedule, so a cache
that holds for one tick, like `NpcLightLevelCacheComponent`, rarely helps the second caller. So when a sense-layer
system has worked something out on the way to a decision, it keeps it on its component, and everything after reads it
from there:
- **How lit a target looks:** perception keeps it on the contact as `NpcContact.SeenLight` when it decides it can see
  it. Ask `NpcPerceptionSystem.GetSeenLight`, which uses that and only works it out for a target not in sight. Dark aim,
  reaction time and notice range all do.
- **Line of sight to a visible contact:** perception has just found it, with smoke counted (`ContactInLOSCon` checks
  only the distance for one).
- **What a hostile is like:** `GetTraits` is worked out once per target per tick, however many ask.

Two answers to one question can also disagree, which is worse than slow: a stored value up to one perception interval
old beats a second working out. A new fact more than one place needs goes the same way: work it out where the
decision is made, keep it on the component, and give readers one method that falls back to working it out.

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

A key can also outlive what it names. A target gets gibbed, or a grid is deleted, while its uid or coordinates still sit
on the blackboard. `NPCBlackboard.TryGetValue`, `GetValueOrDefault` and so `TryGetEntity` treat an entity (or
coordinates on one) that is deleted or being deleted as missing. Read entities through them, not through
`GetValue`/`_blackboard` directly. Ask whether a key is there with `ContainsKey(key, entityManager)`, which agrees
with them, as `KeyExistsPrecondition` and `KeyNotExistsPrecondition` do. The one-argument `ContainsKey(key)` still
says yes for a deleted entity: a branch gated on it plans, its operator fails on the read, the failure replans at
once onto the same branch, and everything below it is starved.

Two cases still need their own check, because a system method given a deleted uid throws (`EnsureComp`, transforms),
and one throw in an operator aborts the NPC update for every NPC after it, every tick:
- an entity a system holds itself across ticks;
- an entity that may be deleted between the read and the use.

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

A plan effect on a key the blackboard works out for itself (`ActiveHand`, `ActiveHandFree`, `FreeHands`, `Inventory`,
`OwnerCoordinates`, `CanMove`, `Access`; see `NPCBlackboard.IsComputedKey`) is for the planner only. It is never stored
on the blackboard the NPC runs on, since a stored value would hide the real one for good.

### A check made while planning decides nothing between squadmates

Squadmates plan side by side, so a precondition or consideration reading what another member has done (a claim, a shared
cooldown) passes for all of them until one acts. Anything only one of them may do needs a check-and-set in `Update`, on
the main thread, ahead of the act itself, that fails the rest: `ClaimTargetOperator`, or `CooldownOperator` with
`failIfRunning`. Keep the planning check too: it is what steers the losers elsewhere on their next plan.

The same goes for a chance. `ProbabilityPrecondition` rolls on every plan, so on its own it comes up within a second or
two. For a chance per occasion, give it a branch and start a cooldown in that branch and in the branch after it, as
`suppression.yml` does: the roll then stands until the cooldown is up.

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

### Steering only walks straight at a near target if the way is clear

Within `RepathRange` (1.5 tiles) steering skips the path and walks straight at its target. It does that only while a ray
says nothing anchored is in the way (`NPCSteeringSystem.KsIsBeelineClear`, rechecked every 0.3s; what the goal's
coordinates are on does not count, so an anchored goal is not in its own way). Otherwise it paths:
- a target just over a railing or table would otherwise have it pressed against that for good;
- a climb (`ClimbingComponent.NextTransition`) is waited out, not taken for being stuck.

Climbing costs `PathfindingSystem.ClimbCost` on top of walking, so climbers walk round anything short; operatives have
`NavClimb`. `KsNpcRailingTest` covers railings at every rotation.

### A hunt's clock is the loss, not the hunt

A squad often only gets round to hunting a few seconds after the hostile ducked out of sight: it was suppressing, pushing,
or keeping a flank on (`KeepFlankWhileUnseen`). So `NpcHunt.LostAt`, the last sighting, is what the watch counts from,
and the guess at where it went (`PredictedCoordinates`) is worked out again every update of the watch, from the spotter's
contact. Only the hunt's own timeout counts from `StartedAt`. Callouts pass on when the caller saw it
(`NpcContactCalloutEvent.LastSeen`), and a listener keeps a sighting of its own that is as fresh. A hostile suspected in a
locker beside where it vanished starts a hunt like a lost one. `KsNpcHuntTimingTest` covers all of this.

### Navmesh flags are per point, and door avoidance is per tile

A tile's navmesh points each take the flags (Door, Access, Climb, Pushable) of only what covers that point
(`PathfindingSystem.Grid.cs`). They used to share one value per tile, so a windoor on one edge made the tile's open side
a Door too. Steering lets doors through its collision avoidance while its next poly is a Door, so it would walk into the
closed windoor it was beside. That avoidance now lets through only doors on the next poly's own tile
(`NPCSteeringSystem.KsIsOnPolyTile`): a closed door beside the one being gone through is a wall.

### A background move shares the NPC with the tasks after it

A `MoveToOperator` with `shutdownState: PlanFinished` or `Never` reports itself finished at once and keeps steering
while the plan carries on: shooting while closing in, reloading while retreating. Anything steering does on its own -
opening, prying or forcing a door - then happens while those tasks use the same hands, and the two fight over them: an
access breaker came out mid-reload and was dropped by the reload. The operator marks such a move with
`NpcBackgroundMoveComponent`; check it (`NpcDoorSystem.IsMovingInForeground`) before steering takes anything into its
hands, and leave hand work to moves that are the task at hand.

A move with `shutdownState: Never` also outlives its plan, and so do its keys: `NpcMoveToCleanupSystem` removes them only
once steering stops. Every branch gated on such a key (the operatives' "don't interrupt retreats") stays shut until then,
plan or no plan. If what the move was for ends sooner, end the key with the plan: `SetNullOperator { key, onPlanEnd: true }`
removes it however the plan ends, even if it fails before getting that far. A reload's retreat does this: it used to
send an operative up to ten tiles off with nothing planned the whole way, and its squadmate fighting alone.

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

1. Stunned (does nothing until it can act: nearly everything below starts with a line, so anything else repeats itself)
1a. In a closet (hid to heal: the meds, then staying in until `NpcHidingSystem` lets it out; nothing below can get it out early; see `healing.yml`)
2. Out of the fire (see `danger.yml`)
3. Play dead
4. Healing
5. Grenades (getting away from a live one)
6. Recovering: fall back (waiting for meds to work and found; see `recovering.yml`)
7. Target down (a one-line callout, from `NpcPerceptionSystem`; interrupts a fight for a tick)
8. Throw grenade (opt-in; at a hostile in the fight or out of sight; interrupts a fight for a tick; see `grenade_throw.yml`)
8a. Shoot out a light (opt-in, `NpcLightShooter`; never with a hostile in sight it can hit; above the squad plan, whose orders a squad that lost a hostile is on, or it would never get a turn; see `light_shoot.yml`)
9. Squad plan orders (one branch above combat, for orders with `OverCombat`; see `squad_plan.yml`)
10. Ranged, then Melee
11. Execution (opt-in; being below combat is what "nothing left to fight" means; see `execution.yml`)
12. Suppressive fire (opt-in; see `suppression.yml`)
13. Grenade push (opt-in; going in on a squad grenade that just went off; see `grenade_throw.yml`)
14. Buddy aid (opt-in; see `buddy_aid.yml`)
15. Tactical reload (opt-in, through `TacticalReload` on the blackboard)
16. Support: peek (two root branches, not pinned down and pinned down, the latter every 15s rather than every 6s), then push (never when pinned down), backing up a squadmate fighting a hostile this one can't see; see `support.yml`
16a. Peek a lost hostile (the same two branches again; a hostile this one lost 1.5s to 8s ago, with nobody calling it out: step out to a spot that sees where it went, look, step back; see `peek.yml`). Both peeks are gated on `PeekDueAt` / `PeekPinnedDueAt`, which the peek sets when it finds a spot, and stop at a `PeekRange` of a quarter tile, not `CampingRange`, which stopped short behind the corner.
17. Follow
18. Recovering: hold (nothing below runs until the meds have worked; see `recovering.yml`)
19. Search open, then Search cover
20. Door refused (a one-line callout; see Doors below)
21. Orders: Investigate, Watch, Stage, Breach, Enter, Search locker, Search spot, Hold area, Regroup
22. Sensors
23. Join squad
24. Disengage
25. Squad hold

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

## Tile dangers

Fires start and go out too often for the navmesh, which every NPC shares and which is dear to rebuild, so dangers sit
beside it (`PathfindingSystem.Klovn.Danger.cs`), in a small array per burning chunk. They raise what a poly costs a path
request that avoids them, and are never walls, so an NPC with no other way still gets through. Tiles touching a danger
cost a little more (`marginCost`), so a path leaves a gap where that is a short way round.

- **What is dangerous:** an entity with `NpcTileDanger` makes its whole tile so. Gas fires have no entity and come from
  `AtmosphereSystem.GetGasFires`. Primed explosives and flashes make every tile they would reach a `Blast` or `Flash`
  danger (`PathfindingSystem.Klovn.Blast.cs`). A new sort of danger is a value in `NpcTileDangerKind`.
- **Who cares:** only an NPC with `NpcAvoidsTileDanger`, for the sorts it lists. Everything else ignores all of them,
  which is what a firebot needs, and an NPC that fire cannot hurt leaves `Fire` out.
- **Cost:** worked out on demand, at most every 0.5s, and only for an NPC that avoids something, so it is nothing with
  none about, and otherwise what is burning rather than the size of the map.
- **Steering:** a straight-line chase takes a path instead while there are dangers about, and a path looks ahead for a
  fire that started on or beside it after it was found. Only one that started after: the dangers a path already passes
  when it comes back (`NPCSteeringComponent.DangerOnPath`) are no news, or an NPC walking past a long fire would search
  for the same path twice a second. That holds only if the dangers did not change while it was searched for
  (`PathfindingSystem.TileDangerVersion`), and never for a search that gave up on minding them (below). `TacticalPositionOperator` is never offered a tile that poses one,
  nor one touching it unless there is nothing else.
  `GetRandomPath` does not take dangers into account, though steering still stops short of one at the end of it.
- **Never stopped on one:** crossing is allowed, stopping is not. Steering calls a move done when all that is left of the
  way is dangerous, so a goal in a fire is walked up to and no further. A juke takes no path, so steering rules out the
  directions that would step onto a danger. A danger can still start under an NPC, so `StandingInDangerPrecondition` gates
  a branch at the top of the root (`danger.yml` for operatives) that goes to the nearest safe tile. **An NPC with
  `NpcAvoidsTileDanger` needs that branch in its own root.** The precondition does not count an NPC following a path: turned
  back mid-crossing, it would never get to the far side of a fire.
- **Running out of nodes:** the coarse maps know nothing of dangers, so a fire across open floor can flood a search to the
  node limit. A search that does starts again ignoring dangers, as `NoPath` three times over stops an NPC for good, and
  steering looks again as it gets nearer, where a search fits.

## Line of fire

Seeing a hostile and being able to hit it are different things, and an NPC that confuses them stands there aiming at a
window. Sight goes by what blocks light. A shot goes by what that shot collides with: glass stops bullets and lets through
lasers and some energy bolts, and a locker stops both while hiding nothing. `NpcLineOfFireSystem` reads what the next shot
from what the NPC is holding collides with, and answers from that.

Anything deciding about shooting goes through it, not `NpcLineOfSightSystem` and not `InRangeUnobstructed`'s default
mask: `NPCCombatSystem` firing, jukes staying on target, `TacticalPositionOperator.lineOfFire` for a spot to shoot from,
`MoveToOperator.lineOfFire` for "close enough to shoot", and `LineOfFirePrecondition`. Mobs in the way do not count as
barriers.

A contact stays `Visible` behind glass, as it should: the NPC can see it. So a gun branch must not assume that visible means
shootable. `GunOperator` with no shot waits, which is right while something is walking it to one and wrong if nothing is. A
plan with neither a shot nor a way to one must fail, so that the rest of the root gets a turn: see the last branch of
`KsOperativeGunCombatMovementCompound`.

## Squad plans

A hostile the squad can see and nobody can hit is a fight, so hunts stay out of it, and no member can do anything about it
alone: each walks up to the door and stops. `tactics.planning` has a squad make a plan for that. It lives in
`NpcSquadTacticsSystem.Plan.cs` and runs before the fight check in `UpdateGroup`.

- **`NpcSquadGoap` only picks an order of steps.** It is pure: facts in, costs in, steps out. To add an action, add a row to
  `Actions` with what it needs and adds, a cost, and a case in `RunPlanSteps`. Facts live in `NpcSquadFact`.
- **Carrying it out is the hunt's code.** A plan keeps an `NpcHunt` of its own, never on `NpcHuntComponent`, and drives
  `UpdateStage`, `UpdateBreach` and `UpdateEntry` a step at a time, with no leader and no target so that `Exhaust` touches
  nothing.
- **Plan orders come before combat.** `NpcOrder.OverCombat` is set on every order given during a step, and one root branch
  above combat picks those up. Orders below combat would never beat a running gun plan. So a plan has to end the moment
  anyone has a shot: `LookForShot` runs first on every update, and combat replans once the order goes.
- **No plan while a way is free.** It asks for a path first, and a member walks a free one by itself.
- **Make the choice depend on the situation, or it needs no planner.** A new action earns its place if its cost comes from
  what the squad can see: `Enter` costs more on an armed hostile (`planRushRisk`), which is the only reason a flashbang
  ever beats walking in. Costs that never change give one fixed plan, and a list would do that.
- **A second goal.** With no way to `HasShot`, `TryMakePlan` tries `OutOfSight`, which takes `Threatened`. `ShouldWithdraw`
  starts a plan for that goal in a fight the squad is losing, which ignores having a shot.
- **A plan that fails bans its action** and plans again. One that can do nothing cools down for `planCooldown`, so a squad
  does not spend its time finding that out twice a second. Idle squads pay for one `TryComp`, as the component only
  exists from when nobody has a shot until the cooldown ends. Banning is also how a step with a time limit hands over:
  `HoldExits` is banned when its wait is up, and the plan moves on to going in.
- **A fact that only blocks when there is something to do about it.** `Patched` and `Loaded` are true unless someone could
  usefully see to that, so `Enter` can require them and a squad with no meds never dead-ends. There is no negation in
  the search, so state a precondition this way round.
- **First found wins a tie**, so the order of `Actions` is the order of steps that could go either way round. `PatchUp` and
  `TopUp` come before anything that opens a door for that reason.
- **Charges have one runner and many sites.** `RunCharge` does not care what it is blowing: a site is an `NpcChargeSite`, and
  a new kind only needs something that finds one, as `FindDoorCharge` and `FindWallCharge` do. How one goes off comes from
  the item (`NpcChargeSystem.TryGetCharge`), so nothing is configured per NPC. An ordinary hunt stays out of it. With
  `tactics.huntCharges`, a hunt shut out of a room goes through `NpcHuntPhase.Blast` instead, which drives `RunCharge` with a
  plan nobody else sees, then goes in and searches.

Never give an order for somewhere nobody can get to: a task that cannot find a path fails to plan, and the NPC just stands
there with the order. A hunt checks first (`TryPrepareSealedRoom`, a walk over the floor from the hostile, through doors), and
one that is cut off goes through `ExhaustShutOut`, which holds at the nearest place they can reach. That goes for squads too
bold to hunt a disturbance: they would otherwise go through Squad hold, which has the same problem.

`ks_squadplandebug` shows all of it, and `ks_lineoffiredebug` shows any NPC's shot.

## Stop, drop and roll

`NpcStopDropRollSystem` has every awake NPC that can resist fire do what a player does with the fire alert, once it has
`klovn.npc.fire_roll_min_stacks` stacks. It is a reflex in a system, not a branch, so that it needs nothing from each root,
and the stun that comes with it interrupts whatever the plan was doing, like any other stun. It waits while the NPC is on
a burning tile, for any NPC and not only one that avoids fire, and while it is walking away from one it is touching. So
an operative gets out of a fire through `danger.yml`, and rolls once it is clear. `NpcStopDropRoll` changes how it goes:
`keepsHold` keeps what is in its hands, as otherwise a lighter disarms it, and `busyKey` is a blackboard key that means it
has somewhere more urgent to be.

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
- **The other way round:** a door it believes is locked is only tried once it is the only way, except by an NPC that
  pries or smashes (`PathFlags.Prying`/`Smashing`), which goes through as it always has.
- **Standing still:** steering stops the NPC while it forces a door with a tool, and while its own steering do-after runs
  (prying with its own claws, climbing). Those do-afters break on moving, and walking on into the door it was prying
  used to cancel the pry every time, so NPCs that pry for themselves gave up at every locked door. Until then steering goes
  round it as it would a really locked door (`TryDetourAroundDoor` with `believedLocked`), so access added behind its
  back is found out by trying the door, never known. Nothing in steering may ask the door itself (`IsAllowed`): collision
  avoidance goes by `HasAdvertisedAccess` too.
- **Doors no hand opens:** shutters and blast doors (no `clickOpen`, no `bumpOpen`) open from their buttons. The navmesh
  flags them `PryOnly` and `Sealed`, so paths treat them as walls, not as public doors. Shutters are a way through for
  an NPC that pries for itself (`PathFlags.Prying`), or carries something that pries and forces doors in its way
  (`PathFlags.PryingTool`, from `NpcDoorSystem.PriesDoorsInItsWay`). Nothing pries a blast door, so only smashing gets
  through one.

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

The estimate for a cluster the search from the goal never settled is only the least it could cost, which in open ground
is exactly what the best walk costs: A* ties with the real path and floods the field (a 100 poly detour around a wall took
3418 expansions, 5787 on the Field Commander map, worse than no coarse maps). So that search goes a little past the best
walk (`GetSearchLimit`, over A*'s own tie-break) and everything unsettled loses to the path: expansions equal the path's polys
(`KsNpcOpenFieldPathTest`). A steered search's node limit grows with the best walk (`GetNodeLimit`, up to 8192), as it needs
a poly a tile and a path of 600 tiles would run out of the usual 512.

Two things it does not know: an NPC's own avoided tiles (doors it has found it cannot get through), which only make the
walk longer than it thinks, so it stays a lower bound and paths stay the shortest; and anything that changes the
navmesh without marking the chunk dirty, which the pathfinder misses with or without the coarse maps. Deleting a wall
used to be one: its move to nullspace was looked up on the grid it had already left. Note that an unanchored wall still
blocks its tile: the navmesh counts any hard body there, anchored or not.

## Loose things in the way

An NPC with `NavPush` on its blackboard shoves loose things - closets, crates - out of its way (`NpcPushSystem`). It is
asked twice whether something can be pushed, with the pure `NpcPushableAttemptEvent`:

- **When the navmesh is built, with no pusher.** A tile blocked only by things that can be pushed gets
  `PathfindingBreadcrumbFlag.Pushable`, and pushing paths cross it at a cost. Not during the path search, which would
  ask again at every expansion. Both the search and the navmesh build run on worker threads, where raising events is
  unsafe, so the answers are collected on the main thread just before the build (`CollectPushableBodies`), and the build
  only reads them. Anything else the build wants to ask a system belongs there too.
- **When the NPC reaches it, with the NPC.** If it cannot push it (a handler says no, or it has no shove), it remembers
  the thing (`NpcPusherComponent`) and drops its path for one round it.

To push, steering raises `NpcPushObstacleEvent` on the thing. Unhandled, the NPC shoves it the way a player would,
away from itself, which needs combat mode that can disarm.

For an NPC that pushes, steering looks at what is on the next tile of its path when it gets there
(`HasLooseBlocker`), not only at the navmesh's flags. What it pushes moves on ahead of it: down a one-tile maintenance
corridor barricaded with closets, every shove puts one on a tile that was free when the path was made. Going by the
navmesh, it walked into them and waited to be found stuck at each one, and did not get through a seven-tile corridor
with two closets in a minute; looking, it takes a few seconds (`KsNpcPushTest.TestBarricadedCorridorIsPushedThrough`).
`TryHandleFlags` deals with that before anything else, as its navmesh data may say the tile is free.

It gives up on one thing after `NpcPusher.giveUpAfter` (10s) of pushing it without it budging, as on something it
cannot push. Steering doesn't count an NPC handling an obstacle as stuck, so without that a closet wedged against a wall
would hold it for ever. Each time the thing moves, the wait starts again, so a closet pushed steadily down a long
corridor is not given up on.

What it does not do: choose which way to shove (always straight on, so a closet in a long corridor is shoved down it),
or notice a handler's answer changing without the tile being rebuilt.

## Moving somewhere without going through somewhere

The pathfinder only knows the shortest way. It can be told to treat a set of tiles as walls
(`PathfindingSystem.GetPathAvoiding`, which flank routes use), but not to prefer one way over another. When the way
matters, as for a member stacking up on the door on the far side of a room, which
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
  `blackboard.GetValue<EntityUid>(NPCBlackboard.Owner)`. One with an `invert` flag subclasses `KsInvertiblePrecondition`
  and implements `IsMetRaw` instead (`CooldownPrecondition`, `InSquadPrecondition`): the flip happens in one place, on
  every path. `KeyExistsPrecondition` is upstream and keeps its own `inverted`.
- **Shared pieces** (look here before writing a new one): `Content.Server/_KS14/NPC/Utility/` has `NpcThrottle` (a
  system's update interval), `NpcExpiringMap` (entries that lapse), `NpcBestOf` (best candidate so far), `NpcCoordinates`
  and `NpcGridSystem` (grid-relative positions and tiles). `HTN/NpcBlackboardExtensions` has the blackboard helpers.
  `KsInstantOperator` is the base for an operator that does one thing when its task starts and is done at once. A
  consideration about believed contacts or a whitelist subclasses `KsBelievedContactCon` or `KsWhitelistCon`.
- **Picking an entity is a `utilityQuery`.** Anything that gets some entity, whether a target, a squadmate or an item, does
  so through a YAML query and `UtilityOperator`. Don't write an operator or system method that picks one: an operator acts
  on the key `UtilityOperator` wrote. YAML shows what is being chosen and why, and a balance pass can change it without
  touching C#.
  - **Reuse a query and narrow it with considerations.** A score of 0 drops a candidate, so most "which ones" questions are
    an existing query plus considerations: hostiles that are down are `PerceivedContactsQuery` with `TargetIsCritCon`, not a
    new query or a new field on it. A new `UtilityQuery` inheritor is only for entities no existing query can supply, like
    `SquadmatesQuery`. It overrides `UtilityQuery.AddEntities`, the way a consideration overrides
    `UtilityConsideration.GetScore`, so it needs no case in `NPCUtilitySystem`.
  - **One consideration, one question.** If a consideration's `GetScore` asks two things, it is two considerations. Compose
    them in YAML with `KsLogicAndCon` and `KsLogicOrCon`, which short-circuit, so put the cheapest and most decisive first.
    Each child has its own curve, and the logic consideration's curve applies to the result: `KsLogicAndCon` with an
    `InverseBoolCurve` is "not both".
  - **Only for some NPCs:** a query shared by NPCs that differ goes by the owner with `FitsWhitelistCon { checkOwner: true }`
    on a marker component. Read the owner first, so NPCs without it pay for nothing else:
    ```yaml
    - !type:KsLogicAndCon # one that is down is left for walking up to, by those that do that
      curve: !type:InverseBoolCurve
      considerations:
      - !type:FitsWhitelistCon
        checkOwner: true
        whitelist:
          components:
          - NpcExecutioner
        curve: !type:BoolCurve
      - !type:TargetIsCritCon
        curve: !type:BoolCurve
    ```
  - **A distance curve ranks, it does not reject.** It scores above 0 everywhere inside the query's reach, so anything that
    needs a cut-off needs a consideration that returns 0. That was how operatives ran from grenades 20 tiles away, until
    `TargetDangerRangeCon`.
  - **Hidden hostiles go by belief.** `TargetDistanceCon` and `TargetInLOSCon` read where a hostile really is, which is
    cheating for one out of sight. Use `ContactDistanceCon`, `ContactInLOSCon` and `ContactClearOfSquadCon`, and
    `UtilityOperator`'s `believedCoordinates: true`.
  - **`InventoryQuery` walks worn slots, not hands.** For "the gun in hand, or else something carried", use two compound
    branches: `GetActiveHeldItemOperator`, then `UtilityOperator`.
- **Voicelines:** `SpeakOperator` ignores `cooldown` without a `cooldownID`. A line said once per squad uses
  `InSquadPrecondition { leaderOnly: true, orAlone: true }`: the leader, or an NPC in no squad. `invert` flips the whole
  answer, `orAlone` included.
- **Actions:** `DoActionOperator` and `DoWorldActionOperator` only check that their target key exists at plan time.
  A world-targeted jump (`KsGravityJumpWorldEvent`) throws the performer all the way to its target, uncapped, so
  clamp the target yourself (see `DiveOperator`). Gate "only some NPCs do this" on the action existing
  (`TryGetValidAction` in `Plan`), not on a blackboard flag.
- **Settings blocks on components** (a class of `[DataField]`s held in one field, like `NpcSquadMemberComponent.Cover`)
  get `[AlwaysPushInheritance]`. Without it, a child prototype that sets one field of the block replaces the parent's
  whole block, and every field it left out silently goes back to the C# default.
- **No loop where none is needed:** something that only expires - a cooldown, a claim, a remembered door - is checked
  against its end time when read, and dropped when the next is written (`NpcGenericCooldownSystem`,
  `NpcTacticalPositionClaimSystem`). Something that must act on a timer gets a component that exists only while it is
  in progress, and the loop runs over that (`NpcActiveRangedAttackComponent`), not over every NPC that could.
- **Throttled systems:** spread first updates across the interval at `MapInit`, so NPCs spawned together do not all
  update on one tick (`NpcThrottle`). Keep scratch collections as fields and reuse them; a system running per NPC several times a
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
  Damage only lands on an entity with `Injurable`, which they lack too: `Damageable` alone throws it away with no error, so
  bullets and executions do nothing to them. `DamageableSystem.SetDamage` still works, so staged damage hides this.
- **Test mobs lack the operative blackboard.** Without `VisionRadius`/`AggroVisionRadius`, `TacticalPositionOperator`
  scores every candidate's distance as the far end of its curve, which with a near-preferring curve is 0: no spot
  is ever found. Stage the keys the branch reads (`KsOperativeHtnRootTest.StageArmed`).
- **A live fight ends for its own reasons.** A pistol empties in about four seconds, and an NPC out of ammo plans
  something else entirely. A test of what happens later in a fight has to keep the gun loaded (`KeepLoaded`) and
  the target alive (godmode).
- **Loose things block paths as anchored ones do.** The navmesh counts every hard body that blocks mobs, anchored or
  not, on the tile its centre is on, and blocks a whole tile one leaves too narrow for a mob
  (`PathfindingSystem.BlockNarrowGaps`). So a free-standing locker blocks a floor tile, and a wall locker (anchored, on
  the wall's tile) blocks nothing new. Unless the NPC pushes (`NavPush`, see "Loose things in the way"), when it shoves
  the free-standing one aside. Mobs never block: the navmesh skips them, `movement.mob_pushing` or not. Test the case your
  rule is actually for.
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
  warm-up, and assert a bound. To find *what* allocates, see the `EventListener` approach in CONTRIBUTING.md §4 (*Engine helpers whose behaviour isn't in their name*).
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
Sessions (who is watching what) are kept by `NpcDebugSessions` in `Content.Server/_KS14/NPC/Debug/`, and a command
scoped to a squad or NPC derives from `NpcScopedDebugCommand`.

`ks_setmeter <NPC> <meter> <value>` sets a meter on an NPC and everyone in its squad, to see how they act at a value
without having to get them there.

`ks_squaddebug` draws (and every meter a member has, as a labelled bar under it):

- every squad: leader, members, cover thresholds and assignments, and the threat;
- each member's beliefs: a line to a visible hostile, a ring where a lost one was with its guessed path, a square
  on a locker it is believed to hide in;
- the hunt, in brief: a diamond coloured by phase, triangles at door staging points, search spots (hollow once
  searched), and a line from each member to its order.
