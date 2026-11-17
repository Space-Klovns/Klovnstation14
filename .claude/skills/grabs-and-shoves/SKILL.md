---
name: grabs-and-shoves
description: How tgstation-style shoves, grab levels, fireman carry and throw mode work in this repo - ComplexShoveSystem's resolution order (grab pin, chain paralyze, KsShoveObstacle, wall ray, push and stagger), KsShoveCollideEvent and the no-chain KsShoveParalyzed rule, the ksGrabStage prototype and KsGrabber ladder, how ctrl-click is intercepted (KsPullToggleAttemptEvent in PullingSystem.TogglePull), hand items and what ends a pull, resisting with the seeded predicted random, server-only strangle damage, throwing a grabbed victim through BeforeThrowEvent, KsFiremanCarrySystem's parented carry and its cleanup, and KsThrowModeSystem's click interception. Use when changing shoves, pulls, grabs, carrying or throwing, adding a grab level or a shove obstacle, tuning their numbers, or debugging a shove that does nothing, a grip that won't tighten or let go, a victim stuck immobile, a carried mob deleted with its carrier, or a click that throws or doesn't.
---

# Shoves, grabs, carrying and throw mode

A port of tgstation's combat "subintents" (`living_defense.dm` `disarm()`, `grippedby()`, `resist_grab()`,
`human.dm` `fireman_carry()`). Player-facing description: guidebook entry `KsGrabsAndShoves`
(`Resources/ServerInfo/_KS14/Guidebook/NewPlayer/GrabsAndShoves.xml`). Design notes and numbers:
`Klovn/Docs/grabs_and_shoves.md`.

Everything is shared, predicted code except strangle damage (server only) and two client systems (carry draw depth,
throw-mode click).

| Feature | Code | Data |
| --- | --- | --- |
| Shove | `Content.Shared/_KS14/ComplexShove/` | `ComplexShove` on the shover, `KsShoveObstacle` on tables (`TableBase`) and people (`BaseSpeciesMob`) |
| Grab levels | `Content.Shared/_KS14/Grab/` | `Resources/Prototypes/_KS14/Grab/stages.yml`, alerts in `_KS14/Alerts/grab.yml` |
| Fireman carry | `Content.Shared/_KS14/Carrying/`, `Content.Client/_KS14/Carrying/` | `KsFiremanCarrier` on `BaseSpeciesMob` |
| Throw mode | `Content.Shared/_KS14/ThrowMode/`, `Content.Client/_KS14/ThrowMode/` | `KsThrowMode` on `BaseSpeciesMob`, key `KsToggleThrowMode` (unbound by default) |
| Resist | `Content.Shared/_KS14/Resist/`, NPCs: `Content.Server/_KS14/Grab/KsNpcGrabResistSystem.cs` | — |
| HUD buttons | `Content.Client/_KS14/Hud/KsCombatHudButtons.cs`, placed in `HotbarGui.xaml` after `MainHotbar` | `_KS14/Interface/Hud/combat_buttons.rsi` (tg's) |
| Combat mode sounds | `KS14` edit in `SharedCombatModeSystem.OnActionPerform`, `Content.Client/_KS14/CombatMode/KsCombatModeSoundSystem.cs` | `KsEnableSound`/`KsDisableSound` on `CombatModeComponent` |

## Shoves

The disarm pipeline is stock and predicted: `SharedMeleeWeaponSystem.DoDisarm` raises `DisarmAttemptEvent`, rolls a
seeded chance, then raises `DisarmedEvent` on the target, only on the first predicted run. `ComplexShoveSystem`
subscribes `DisarmedEvent` on `StandingStateComponent` (so only things that can fall over), and acts only when the
shover has `ComplexShoveComponent`. There is no same-tile rule: standing over someone, you can still shove them. Anything else leaves `DisarmedEvent` unhandled, and `DoDisarm` falls back to a
`PushForce` impulse, which NPC closet-pushing (`NpcPushSystem`) relies on.

Resolution order in `ResolveShove`:

0. **Out of the fight**: a target incapacitated (crit, dead) or in stamina crit is only pushed. Nothing below runs.
1. **`KsBeforeShoveEvent`** on the target: a handler can take the whole shove (a grab pinning its victim).
2. **Target already down** (`KickWhileDown`), always resolved here, never knocked into anything again:
   - With no `KsShoveParalyzedComponent`: paralyze for `ChainParalyzeTime` and add the marker, unless they're
     already stunned by something else.
   - With the marker but not `Disarmed`, and only if `CanDisarmAgainst` says so: drop everything in their hands
     (virtual items excepted) and set `Disarmed`. `CanDisarmAgainst` probes behind the target like a standing
     shove: an obstacle gives its `DisarmsAgainst`, a wall `ComplexShoveComponent.DisarmAgainstWalls`, and
     `KsShoveDisarmCheckEvent` on the target can overrule either. Nothing behind them means no disarm.
   - Otherwise: a push, with no stagger.

   The marker is removed on `StoodEvent`. That is the no-chain, no-extend rule: one kick and one disarm per time on
   the floor.
3. **Obstacle**: entity lookup at `target + direction * ProbeDistance`, radius `ProbeRadius`, highest `Priority`
   wins. `KnockIntoObstacle` raises `KsShoveCollideEvent` on the obstacle first; handle it to replace the default
   (glass table, locker, disposals). The default knocks the target down for `KnockdownTime`, the obstacle for
   `CollateralKnockdownTime` if set, and climbs the target onto it with `ClimbSystem.Climb` if `PlaceOnto`.
4. **Wall**: a static raycast (`CollisionMask`) the same length. `KnockIntoWall` knocks down for
   `BlockedKnockdownTime`.
5. **Free**: the old impulse push plus `KsStatusEffectStaggered` through
   `MovementModStatusSystem.TryUpdateMovementSpeedModDuration` (refreshes, never stacks).

`KnockIntoObstacle` and `KnockIntoWall` are public and take a popup prefix, so anything that sends someone flying
(a thrown grab victim, below) knocks them over the same way.

`KsShovedEvent` reports the outcome afterwards. Shoves do **no stamina damage**: the stamina handler for
`DisarmedEvent` in `SharedStaminaSystem` is commented out with a `KS14` marker.

Popups: when the shove system shows its own (obstacle, wall, kick), it sets `DisarmedEvent.KsPopupHandled`, and
`DoDisarm` skips the generic "shoves" popups. A plain push keeps `PopupPrefix = "disarm-action-shove-"`.
Three-audience popups go through `KsPopupSystem.PopupUserTargetOthers`.

**Add an obstacle** by giving its prototype `KsShoveObstacle` (edit the original YAML with `# KS14` markers).
**Add special behaviour** by handling `KsShoveCollideEvent`.

## Grab levels

A grab *is* a pull. `PullingSystem` still owns the joint, the alerts and every way a pull ends. `KsGrabberComponent`
on the puller adds a `Stage` index into its `Stages` list of `ksGrabStage` prototypes. Only targets with
`KsGrabbableComponent` climb past stage 0; objects stay plain pulls.

`BaseSpeciesMob` has `Puller needsHands: false`, so a pull takes no hand. A hand is taken only by a stage with
`TakesHand`, as a virtual item spawned for the victim (`SharedVirtualItemSystem.TrySpawnVirtualItemInHand`).
Dropping or deleting that item ends the pull through the stock `PullingSystem.OnVirtualItemDeleted`.

### Ctrl-click

`SharedInteractionSystem.HandleTryPullObject` calls `PullingSystem.TogglePull`. A `KS14` edit there raises
`KsPullToggleAttemptEvent` on the puller when they ctrl-click what they already pull; `KsGrabSystem` handles it for
grabbable targets and calls `TryTighten` instead of letting go. Another `KS14` edit in `HandleTryPullObject` lets go
on a ctrl-click of empty floor (invalid uid, or an anchored non-pullable), but only outside combat mode.

`TryTighten`:
- needs the grabber in **combat mode** (popup `ks-grab-need-combat`)
- checks `UpgradeCooldown`, which `OnPullStarted` also starts, so a fresh pull can't become aggressive for a second
- checks that the next stage's hand needs can be met (`CanReach`)
- enters a stage with no `UpgradeTime` at once
- otherwise starts a `KsGrabUpgradeDoAfterEvent` with `BreakOnMove`, carrying `FromStage` so a grip that changed
  meanwhile isn't skipped ahead, and shows the "starts to tighten" popups

`EnterStage` applies everything a stage says:
- the hand item
- stops the victim's own pull if the stage `BlocksVictimMovement`
- `UpdateCanMove`
- knocks the victim down if it `FloorsVictim`
- refreshes the grabber's speed
- swaps the victim's pulled alert for the stage's `VictimAlert` (category `KsGrabbed`)
- sound, popups and an admin log
- raises `KsGrabStageChangedEvent`

`PullStoppedMessage` resets everything.

What a stage does to the victim is enforced live, from the stage, so changing the stage changes it at once:
- `UpdateCanMoveEvent` is cancelled while `BlocksVictimMovement`
- `StandAttemptEvent` is cancelled while `FloorsVictim`, or while the grabber's `Pinned` is set
- hands are never blocked (a design choice: the victim can fight back)

### Pinning

`ComplexShoveSystem` raises `KsBeforeShoveEvent` on the target before resolving any shove. `KsGrabSystem` handles it
when the shover holds the target at a stage that `BlocksVictimMovement`, the target is standing, and `Pinned` isn't
set yet: it knocks them down and sets `KsGrabberComponent.Pinned`. While it is set the victim can't stand, and
`PullStoppedMessage` clears it, so the pin lasts until they break free or are let go. Later shoves fall through to
the usual downed rules.

### Resisting

`TryResist` is reached four ways:
- the stage alert (`KsResistGrabAlertEvent`)
- the pulled alert: the victim's own `TryStopPull` is cancelled in `AttemptStopPullingEvent` and turned into a resist
- a move key (`MoveInputEvent`, which fires only when the held keys change). So that a held direction keeps
  struggling, `KsGrabSystem.Update` (server) also calls `TryResist` for anyone held in place with a direction held
  (`InputMoverComponent.HasDirectionalMovement`) once their cooldown is up. `KsFiremanCarrySystem.Update` does the
  same for the carried, restarting the escape do-after when none is running.
- the HUD resist button: the client raises `KsResistRequestEvent`, `KsResistSystem` raises `KsResistAttemptEvent` on
  the player, and the first restraint holding them (grab, then carry) handles it

Nobody incapacitated or stunned resists. A stage that doesn't block movement just lets go. Otherwise:
- `ResistCooldown` applies
- `KsGrabResistAttemptEvent` lets other systems change the chance
- the roll uses `KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(victim, grabber, tick)`, so the
  predicting client gets the server's result
- a failure deals `ResistFailStaminaDamageMin..Max` stamina damage
- a success calls `TryStopPull` with **no user**, because a user equal to the victim would be blocked again

**NPCs** (`HTNComponent`) struggle by themselves: `KsNpcGrabResistSystem` (server) calls `KsResistSystem.TryResist`
every half second for each NPC carried, or held at a stage that `BlocksVictimMovement`. Not a plain pull: with the
one-second delay before a pull can tighten, an NPC fighting pulls could never be gripped at all. The cooldowns decide
how often an attempt lands.

### Strangling

`KsGrabSystem.Update` deals the stage's `Damage` every `DamageInterval`, **server only**: predicting damage would only
flicker the health bar, as with respiration.

### Closeness

A stage's `HoldDistance` reels the victim in: `KsGrabSystem.Update` (both sides, predicted, like the grappling gun's
reel) shortens the pull's `DistanceJoint` `MaxLength` at `ReelRate` until it reaches the hold distance. It then wakes
both bodies, because a shorter joint doesn't wake a sleeping body. `RequiredDistance` gates entering the stage (popup
`ks-grab-too-far`) and dealing its damage.

### Throwing a victim

`BeforeThrowEvent` (raised by `PredictedHandsSystem.ThrowHeldItem` before it drops the thrown item) on the grabber:
if the thrown item is the grab's virtual item and the stage `AllowsThrow`, the victim must be **down**. If they're
standing, the throw is cancelled with `ks-grab-throw-need-down` (pin them first). Otherwise it swaps `ItemUid` to the
victim, scales `ThrowSpeed`, and calls `MarkThrown` (adds `KsGrabThrownComponent`). The drop that follows deletes the virtual item synchronously, which ends
the pull, so the victim flies free.

The first `ThrowDoHitEvent` on a `KsGrabThrown` victim calls `ComplexShoveSystem.KnockIntoObstacle` for an obstacle
(a person, a table) or `KnockIntoWall` for any static body, with the `ks-grab-thrown-into-`/`ks-grab-thrown-wall-`
popups. The marker goes on that hit, on `LandEvent` or on `StopThrowEvent`. Mobs don't collide with each other
(`MobMask` lacks `MobLayer`), but a thrown entity gets the non-hard throwing fixture, which hits people's hard
fixtures, so this works for people too.

Impact damage is data on the thrower's `KsGrabber`: `ThrowImpactDamage` to the thrown (walls and people) and
`ThrowCollateralDamage` to a person hit (an obstacle with `CollateralKnockdownTime`, so not a table). Both default to
10 Blunt and are dealt by the server only (`DealImpactDamage`).

### Adding a grab level

Add a `ksGrabStage` to `stages.yml` (or a species-specific file) and list it in a `KsGrabber.stages`. Every effect is a
field: `upgradeTime`, `takesHand`, `blocksVictimMovement`, `floorsVictim`, `escapeChance`,
`resistFailStaminaDamageMin/Max`, `grabberSpeedModifier`, `proneDragSpeedModifier`, `damage`/`damageInterval`/
`damagePopup`, `allowsThrow`/`throwSpeedModifier`, `victimAlert`, `sound`, and start/enter popups (each
gets `grabber` and `victim`). Anything a field can't express goes in a handler for `KsGrabStageChangedEvent` or
`KsGrabResistAttemptEvent`, not a new system.

## Fireman carry

Two ways in, both through `TryStartCarry`:
- **Alt-click**: an `AlternativeVerb` on `KsGrabbable` at priority 10, so it is what alt-click does.
- **Drag onto yourself**: `CanDropDraggedEvent`/`DragDropDraggedEvent` on the dragged entity. The strip menu listens to
  the same events, so `KsFiremanCarrySystem` subscribes manually with `before: [typeof(SharedStrippableSystem)]` and
  marks the event handled when a carry is possible.

A carry is possible (`CanCarry`) when the following hold. No grip is needed: tgstation needs an aggressive grab, this
server doesn't. Any pull on the victim is stopped when the carry starts.
- the victim is down and the carrier isn't
- neither is in a container or carried
- the carrier has `HandsNeeded` (2) hands that are empty or hold the grab, which the carry frees

After `CarryTime` the system:
- stops the pull
- adds `KsCarrying`/`KsBeingCarried`
- spawns a virtual item in **both** carrier hands
- turns off the carried entity's collision
- **parents the carried entity to the carrier**

The carried can't move or stand, and the carrier is slowed.

**Throwing**: `BeforeThrowEvent` on `KsCarryingComponent` swaps a carry hand's virtual item for the carried and calls
`KsGrabSystem.MarkThrown`. The drop that follows deletes the hand item, which ends the carry (unparent, collision back
on) before the throw.

Ending is all in one place, `OnCarriedShutdown`: `StopCarrying` only removes `KsBeingCarriedComponent`. Anything that
removes it or deletes either side cleans up the same way. Triggers:
- the hand item is deleted
- the `KsCarrying` alert
- the carrier is downed, inserted into a container or removed
- the carried is inserted into a container
- an escape do-after started from the `KsCarried` alert, a move key or the resist button (`KsResistAttemptEvent`).
  `CanWriggle` refuses it to anyone incapacitated (crit, dead), asleep or stunned, and the do-after's `ExtraCheck`
  cancels a wriggle under way when they go down. `RequireCanInteract` is off because someone carried is knocked down;
  without `CanWriggle` that let crit people wriggle free.

The client raises draw depth while carried (`KsFiremanCarryVisualsSystem`) and restores it on `KsCarryEndedEvent`.

## Throw mode

There is no action. It is toggled by the HUD throw button (`KsCombatHudButtons` raises `KsToggleThrowModeEvent`) and the
`KsToggleThrowMode` key, which has no default binding (it's in the controls options). Ctrl+Q stays the instant
throw, `ThrowItemInHand`. The click itself can't be a shared `EngineKeyFunctions.Use` handler: the client runs shared handlers when the
key is pressed *and* again while predicting, and returning true from the first run stops the input reaching the
server. So `KsThrowModeInputSystem` (client, `outsidePrediction: true`, bound before `SharedInteractionSystem` and
`DragDropSystem`) swallows the click and raises a predicted `KsThrowModeThrowEvent`. This is the pattern
`ActionUIController` uses for targeted actions. The shared handler calls `ThrowHeldItem`. Throw mode
stays on until toggled off, so every click throws.

## HUD buttons

`KsCombatHudButtons` is a vertical `BoxContainer` of two `TextureButton`s, throw above resist. The icons are cropped to
tg's 32x16 label band and scaled by `SlotControl.DefaultButtonSize / 32`, so the pair is exactly one slot. They sit in `HotbarGui.xaml`
(a `KS14` edit) after `MainHotbar`, so right of the suit storage slot on every screen that uses the hotbar. It reads
the local entity's `KsThrowModeComponent` each frame to light the throw button, and hides for anything without one
(ghosts, borgs). Only the buttons hide, never the container: a hidden control gets no `FrameUpdate`, so it could never
show itself again. That was the first version's bug; `KsCombatHudButtonsTest` checks it on a real game screen. Buttons raise predicted network events, as the action UI does. Nothing headless draws, so its look
is only checked in game.

## Combat mode sounds

`SharedCombatModeSystem.OnActionPerform` (a `KS14` edit) raises `KsCombatModeToggledEvent` on the performer. The client's
`KsCombatModeSoundSystem` plays tgstation's `ui_togglecombat.ogg` / `ui_toggleoffcombat.ogg` globally to the local
player only, on the first prediction, at tg's 25% times the **interface volume** setting (`CCVars.InterfaceVolume`),
the gain `AudioUIController` gives UI clicks.
NPC toggles go through `SetInCombatMode` and make no sound.

## Traps hit while building this

- **`[SubscribeLocalEvent]` only takes `Entity<T>` with `ref`.** For class events (`PullStartedMessage`,
  `DownedEvent`, `StandAttemptEvent`, do-after events, alert events) use `(EntityUid uid, TComp comp, TEvent args)`.
  The analyzer error is RA0054.
- **One subscription per (component, event) across all systems.** The client visuals and the shared carry system both
  subscribing `ComponentShutdown` on `KsBeingCarriedComponent` crashes startup ("Duplicate Subscriptions"). Raise
  your own event from the one handler instead (`KsCarryEndedEvent`).
- **Children are deleted with their parent.** A parented carried mob would die with a gibbed carrier. Handle
  `EntityTerminatingEvent` on the carrier: it runs before the children are flagged, so unparenting there saves them.
- **Guard shutdown cleanup with `IGameTiming.ApplyingState`.** A component removed by server state must not redo
  networked work (transform, collision, alerts, other components) on the client.
- **A do-after finishes a few ticks after its delay.** In tests, wait the delay plus a margin.
- **A do-after with `BreakOnMove` breaks when the user-to-target distance changes**, not only when the user moves.
  While a grip is reeling the victim in, a carry or the next upgrade started on them is cancelled. Keep `ReelRate`
  fast, and in tests let the reel settle before starting one.
- **Overriding `PoolSettings` resets `Connected`.** A test using `RaisePredictiveEvent` needs `Connected = true`, or
  the client has no local session.
- `DeleteVirtualItem` raises `VirtualItemDeletedEvent` synchronously, so a pull ends inside the drop that removes
  its hand.

## Tests

- `Content.IntegrationTests/Tests/_KS14/ComplexShove/KsShoveTest.cs`: every shove outcome through
  `AttemptDisarmAttack`, shields included.
- `Tests/_KS14/Grab/KsGrabTest.cs`: the ladder through `TogglePull`, hand use, resists with rigged test stages
  (escape chance 0 and 1), release, throw.
- `Tests/_KS14/Grab/KsFiremanCarryTest.cs`: lift, requirements, put down, wriggle free, carrier deleted.
- `Tests/_KS14/Grab/KsThrowModeTest.cs`: a real client sends the throw request, in and out of throw mode.

The floor ctrl-click release and the move-key resist go through input handlers no test drives yet. Check them in
game.
