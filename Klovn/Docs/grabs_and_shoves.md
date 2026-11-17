# Shoves, grab levels, fireman carry and throw mode

This document describes Klovnstation's port of tgstation's combat-mode "subintents": what a shove does, the four
grab levels, fireman carrying and throw mode. It covers where each number comes from, where we left tgstation's
behaviour on purpose, and how the pieces fit together. The first half is the design; [Implementation](#implementation)
is the working reference for changing the code. The player-facing description is the guidebook entry *Shoving and
Grabbing* (`KsGrabsAndShoves`, `Resources/ServerInfo/_KS14/Guidebook/NewPlayer/GrabsAndShoves.xml`), under Controls.

## Background

Upstream SS14 shoves drain stamina: a shove deals a share of the target's stamina-crit threshold, and enough of them
stun the target. Klovnstation's earlier `ComplexShove` kept that model and added a heavier "wallshove". Upstream
pulling has no levels: ctrl-click pulls, ctrl-click again lets go, and a pull always takes a hand.

tgstation does neither. A shove never tires anyone out; what it does depends on what is behind the target. A pull is
the bottom rung of a grab ladder that ends in strangling. This port follows tgstation's structure, with numbers
chosen by the server's design brief where it gave them, and tgstation's otherwise.

Everything said of tgstation here, and every path below, is as of
[tgstation/tgstation](https://github.com/tgstation/tgstation/) at commit
[`58ab9a26cfae872ca48ef6ec4635b576b869b358`](https://github.com/tgstation/tgstation/tree/58ab9a26cfae872ca48ef6ec4635b576b869b358). Sources:

| Mechanic | tgstation source |
|---|---|
| Shove | `code/modules/mob/living/living_defense.dm` (`disarm`, `get_shove_flags`), `carbon_defense.dm`, `human_defense.dm` (`disarm_collision`), `code/datums/elements/table_smash.dm` |
| Shove chain rule | `code/datums/status_effects/debuffs/staggered.dm` (`dazed`, `no_side_kick`) |
| Grab levels | `living_defense.dm` (`grippedby`), `living.dm` (`start_pulling`, `resist_grab`), `atoms_movable.dm` (`setGrabState`) |
| Strangling | `code/modules/mob/living/carbon/life.dm` (`losebreath` at `GRAB_KILL`) |
| Throwing a grabbed mob | `code/modules/mob/living/living_item_handling.dm` |
| Fireman carry | `code/modules/mob/living/carbon/human/human.dm` (`fireman_carry`) |

## Shoves

A shove is a right-click in combat mode (the stock disarm). It never misses for species (`disarmFailChance: 0`) and
never deals stamina damage. The outcome, checked in this order:

| Situation | Result | Our value | tgstation |
|---|---|---|---|
| Target already down, first shove | Kicked onto their side: paralyzed | 3 s | 3 s (`SHOVE_CHAIN_PARALYZE`) |
| Target already down, next shove against a wall | Drops everything they hold | — | tgstation drops the active item from anyone lying down |
| Target in crit or stamina crit | Only pushed: no knockdown, kick, disarm or pin | — | — |
| Target already down, after that | Only pushed; no stagger | — | — |
| A table behind them | Put onto the table and knocked down | 3 s | 2 s (`SHOVE_KNOCKDOWN_TABLE`) |
| A person behind them | Both knocked down | 3 s, person behind 1.1 s | 2 s, person behind 0.1 s |
| A wall or anything else solid | Knocked down | 3 s | 2 s (`SHOVE_KNOCKDOWN_SOLID`) |
| Nothing behind them | Pushed away and staggered (×0.85 speed) | 3 s | 3 s, capped at 10 s |

"Behind" means within about a tile past the target, along the line from the shover through the target. Tables and
people are found with an entity lookup for `KsShoveObstacleComponent`; anything else solid with a static raycast.
The raycast catches walls, airlocks, windows and machines. Someone already down is never knocked into anything
again, and never staggered.

**The chain rule.** tgstation stops a target being kicked over and over with two status effects: `dazed`, applied by
the first knockdown and cleared by any paralysis, and `no_side_kick`, a 3.6 s immunity. We use one marker,
`KsShoveParalyzedComponent`, added with the paralysis and removed when the target stands up. While someone has it:
- the paralysis can't be renewed or extended
- the next shove disarms them, once (`Disarmed` on the marker), but only against a wall. What counts is data:
  `ComplexShoveComponent.DisarmAgainstWalls` for walls, `KsShoveObstacleComponent.DisarmsAgainst` for an obstacle (off
  for tables and people), and `KsCanShoveDisarmEvent` for anything else that should count
- every shove after that only pushes

Ours is stricter than tgstation's: a target who stays on the floor after the paralysis has no more kicks coming
until they get up.

**Sounds.** The plain shove keeps the existing `thudswoosh.ogg` (tgstation's `shove.ogg` has no SS14 counterpart,
and thudswoosh is what tgstation plays for grabs). A table slam plays tgstation's `tableslam.ogg`, ported to
`Resources/Audio/_KS14/Effects/Shove/`. A wall plays the existing `MetalThud` collection, and a person
`hit_kick.ogg`.

## Grabs

| Level | Reached by | Hand | Victim | Breaking free | Grabber speed | Extra |
|---|---|---|---|---|---|---|
| Passive (pull) | ctrl-click | no | can walk off | always | ×1, ×0.7 dragging someone lying down | — |
| Aggressive | ctrl-click again in combat mode, a second after the pull | yes | can't walk; hands free | 60% | ×0.85 | throw, carry |
| Neck | ctrl-click again, 3 s | yes | can't walk, pinned to the floor | 30% | ×0.75 | harder throw (×1.5), carry |
| Strangle | ctrl-click again, 3 s | yes | can't walk, pinned to the floor | 20% | ×0.65 | 1.5 Asphyxiation/s, harder throw |

Every value above lives in `Resources/Prototypes/_KS14/Grab/stages.yml`.

- **Hands.** Pulling no longer takes a hand for species (`Puller needsHands: false` on `BaseSpeciesMob`); an
  aggressive grip or tighter does. A grabber with both hands full can't go past a pull. Dropping the hand that holds
  the victim lets go.
- **Victim's hands stay free.** tgstation blocks the victim's hands from an aggressive grab up. We only immobilise,
  so a grabbed person can still fight back, by design.
- **Upgrade timing.** Anything past a pull needs combat mode on. A pull becomes aggressive no sooner than a second
  after it starts (tgstation allows it at once), and each step after that takes 3 s of standing still, as in
  tgstation. There is also a 1 s cooldown between attempts (`CLICK_CD_GRABBING`). The upgrade shows a do-after bar and
  tells everyone nearby that the grip is tightening.
- **Pinning.** Shoving someone you hold at aggressive or tighter, while they stand, puts them on the floor instead of
  shoving them, and keeps them there until they break free or are let go. tgstation has nothing quite like it; it is
  the way to bring someone down for a throw or a carry.
- **Resisting** works from the red grab alert, a move key, or the resist button on the HUD. Nobody stunned or
  incapacitated can struggle. NPCs struggle by themselves against a grip or a carry, about as often as the cooldown allows, but let a plain pull
  happen, so they can still be gripped.
- **Escape chances.** tgstation divides a base 60% by an "effective grab state" that rises when the victim lies
  down. Neck and strangle grips floor the victim, so tgstation's real odds are 60% / 20% / 15%. The brief set
  strangling at 20%, so we use flat chances: 60% / 30% / 20%. A failed attempt costs 12–23 stamina (tgstation's
  `rand(7,13)` plus a human arm's `rand(5,10)`). Attempts are 2 s apart (`CLICK_CD_RESIST`).
- **Speed.** tgstation adds a fixed delay per tile, which at SS14's movement speeds would be far harsher than intended
  (+0.3 s per tile on a 0.15 s base is a third of normal speed). The multipliers above keep the order and are meant to
  be tuned.
- **Closeness.** Each grip reels the victim in by shortening the pull joint, quickly but not instantly: to 0.6 m
  between centres when aggressive, and 0.4 m from the neck grip on. This stands in for tgstation's 12 px and 16 px
  pull offsets, and its neck grip moving the victim onto the grabber's tile. The neck grip needs the victim within
  0.8 m and the strangle within 0.6 m, and the strangle only chokes while they stay that close.
- **Strangling** deals Asphyxiation directly, about tgstation's 3 oxyloss per 2 s breath, instead of starving the
  lungs, so it works the same on species that don't breathe oxygen.
- **Throwing** a grabbed person needs them on the floor first: a standing victim can't be thrown, so pin them.
  tgstation lets you throw anyone held aggressively.
- **Throwing someone into something** knocks them over, as a shove into the same thing would. Into a person, both
  go down; into a table, they land on it; into a wall or anything solid, they go down. Slamming into a wall or a
  person also deals 10 Blunt to the thrown person, and 10 Blunt to the person hit (tgstation's carbon impact), both
  set on the thrower's `KsGrabber` (`throwImpactDamage`, `throwCollateralDamage`). Only the first thing they hit
  counts.
- **Letting go.** H, the pulling alert, the "stop pulling" verb, dropping the hand, or ctrl-clicking empty floor
  outside combat mode. tgstation has no floor-click release; the brief asked for it.

## Fireman carry

Anyone lying down within reach can be carried: alt-clicking them (or dragging their sprite onto your own) lifts them
onto your back after 5 s (tgstation's `carrydelay`), replacing any grab on them. tgstation needs an aggressive grab
first; this server doesn't. It needs both hands free, a hand holding them counting as free. The carried person:
- rides along parented to the carrier, with no collision
- can't move or stand
- is drawn above the carrier

The carrier moves at ×0.8 and uses both hands (tgstation's `CARRIER_NEEDS_ARM` takes one). Throwing a carrying hand
throws the carried person off your back, and they land as any thrown grab victim does.

It ends when the carrier:
- drops a carrying hand, or throws them
- clicks the carrying alert
- falls over or enters a container
- is deleted (the carried person is put down first, so a gib doesn't take them along)

It also ends when the carried person wriggles free, which takes 2 s and starts from their alert, a move key or the
resist button. Carried NPCs wriggle by themselves. Nobody in crit, asleep or stunned can wriggle, and a wriggle under way
stops if they go down.

tgstation also has piggyback rides, carry traits and carrying someone onto a table. None are ported.

## Throw mode

tgstation throws with a throw-mode toggle; SS14 has Ctrl+Q, which throws at the cursor at once, and keeps it. Throw
mode comes on top: its HUD button, or its own key (in the controls options, unbound by default). While it is on,
every left click throws the active hand's item at the click instead of interacting, until it is toggled off. Unlike
tgstation, it doesn't switch itself off after a throw. With an empty active hand the
click works normally. Throwing the hand that holds a grabbed or carried person throws the person.

## HUD buttons

tgstation's throw and resist buttons sit to the right of the suit storage slot, stacked, throw on top. The throw
button toggles throw mode and lights up while it is on; resist struggles against whatever holds you (a grip, a
carry). Their icons are tgstation's midnight HUD's `act_throw`/`act_resist`, in
`Resources/Textures/_KS14/Interface/Hud/combat_buttons.rsi`. There is no throw-mode action on the action bar.

## Combat mode sounds

Turning combat mode on or off plays tgstation's `ui_togglecombat.ogg` / `ui_toggleoffcombat.ogg`, heard only by the
player who toggled it, at tgstation's 25% scaled by the interface volume setting.

## Extending it

- **A new grab level, or a species-specific ladder.** Add `ksGrabStage` prototypes and list them in that species'
  `KsGrabber.stages`.
- **Something new to be shoved into.** Give it `KsShoveObstacle` with its own popups, knockdown and priority.
- **Special shove or grab behaviour.** Handle the events rather than adding systems:
  - `KsShoveAttemptEvent`: take a shove over entirely, as the grab's pin does
  - `KsShoveCollideEvent`: glass tables, lockers, disposals
  - `KsCanShoveDisarmEvent`: something else that a downed target can be disarmed against
  - `KsShovedEvent`: react to any shove outcome
  - `KsGrabStageChangedEvent`: martial arts, pummelling
  - `KsGrabResistAttemptEvent`: grab-resistant species, staggered victims
  - `KsPullToggleAttemptEvent`: anything else ctrl-click on a pulled thing should do

## Not ported

- tgstation's table slams with a grab: aggressive slams, and neck-grip limb smashes.
- Bear hugs, and the punch bonus against grappled targets.
- Grabbing a specific limb or your own limb.
- Shoving someone off you while they ride you.

## Implementation

Everything is shared, predicted code except strangle damage (server only) and two client systems (carry draw depth,
throw-mode click).

| Feature | Code | Data |
| --- | --- | --- |
| Shove | `Content.Shared/_KS14/ComplexShove/` | `ComplexShove` on the shover, `KsShoveObstacle` on tables (`TableBase`) and people (`BaseSpeciesMob`) |
| Grab levels | `Content.Shared/_KS14/Grab/` | `Resources/Prototypes/_KS14/Grab/stages.yml`, alerts in `_KS14/Alerts/grab.yml` |
| Fireman carry | `Content.Shared/_KS14/Carrying/`, `Content.Client/_KS14/Carrying/` | `KsFiremanCarrier` on `BaseSpeciesMob` |
| Throw mode | `Content.Shared/_KS14/ThrowMode/`, `Content.Client/_KS14/ThrowMode/` | `KsThrowMode` on `BaseSpeciesMob`, key function `KsToggleThrowMode` (`KsKeyFunctions.ToggleThrowMode`, unbound by default) |
| Resist | `Content.Shared/_KS14/Resist/`, NPCs: `Content.Server/_KS14/Grab/KsNpcGrabResistSystem.cs` | — |
| HUD buttons | `Content.Client/_KS14/Hud/KsCombatHudButtons.cs`, placed in `HotbarGui.xaml` after `MainHotbar` | `_KS14/Interface/Hud/combat_buttons.rsi` (tg's) |
| Combat mode sounds | `KS14` edit in `SharedCombatModeSystem.OnActionPerform`, `Content.Client/_KS14/CombatMode/KsCombatModeSoundSystem.cs` | `KsEnableSound`/`KsDisableSound` on `CombatModeComponent` |

### Shoves

The disarm pipeline is stock and predicted: `SharedMeleeWeaponSystem.DoDisarm` raises `DisarmAttemptEvent`, rolls a
seeded chance, then raises `DisarmedEvent` on the target, only on the first predicted run. `ComplexShoveSystem`
subscribes `DisarmedEvent` on `StandingStateComponent` (so only things that can fall over), and acts only when the
shover has `ComplexShoveComponent`. There is no same-tile rule: standing over someone, you can still shove them. Anything else leaves `DisarmedEvent` unhandled, and `DoDisarm` falls back to a
`PushForce` impulse, which NPC closet-pushing (`NpcPushSystem`) relies on.

Resolution order in `ResolveShove`:

0. **Out of the fight**: a target incapacitated (crit, dead) or in stamina crit is only pushed. Nothing below runs.
1. **`KsShoveAttemptEvent`** on the target: a handler can take the whole shove (a grab pinning its victim).
2. **Target already down** (`KickWhileDown`), always resolved here, never knocked into anything again:
   - With no `KsShoveParalyzedComponent`: paralyze for `ChainParalyzeTime` and add the marker, unless they're
     already stunned by something else.
   - With the marker but not `Disarmed`, and only if `CanDisarmAgainst` says so: drop everything in their hands
     (virtual items excepted) and set `Disarmed`. `CanDisarmAgainst` probes behind the target like a standing
     shove: an obstacle gives its `DisarmsAgainst`, a wall `ComplexShoveComponent.DisarmAgainstWalls`, and
     `KsCanShoveDisarmEvent` on the target can overrule either. Nothing behind them means no disarm. The probe
     only runs at this step, when a disarm is still possible.
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

`KnockIntoObstacle` and `KnockIntoWall` are public and take the popups to show, so anything that sends someone
flying (a thrown grab victim, below) knocks them over the same way.

`KsShovedEvent` reports the outcome afterwards. Shoves do **no stamina damage**: the stamina handler for
`DisarmedEvent` in `SharedStaminaSystem` is commented out with a `KS14` marker.

Popups: `OnDisarmed` decides from the outcome, in one place. A plain push (`KsShoveOutcome.Pushed`) keeps the stock
`PopupPrefix = "disarm-action-shove-"`. Every other outcome has shown its own, so it sets
`DisarmedEvent.KsPopupHandled` and `DoDisarm` skips the generic "shoves" popups.

A set of three-audience popups is a `KsAudiencePopups` (`Content.Shared/_KS14/KsPopup/`): a `user`, a `target` and
an `others` locale id, any of which can be left out. `KsPopupSystem.PopupUserTargetOthers` shows one, and shoves,
grabs and carries all go through it. As a `[DataField]` the YAML linter checks every id, so the sets that vary are
data: `Popups` on `KsShoveObstacle`, `BlockedPopups`/`BlockedResistedPopups`/`ParalyzePopups`/`DisarmPopups` on
`ComplexShove`, `StartPopups`/`EnterPopups`/`ThrowPopups` on a grab stage, and the thrown-victim sets on `KsGrabber`.
YAML replaces a whole set: an audience left out of the mapping gets no message, not the default one.

**Add an obstacle** by giving its prototype `KsShoveObstacle` (edit the original YAML with `# KS14` markers).
**Add special behaviour** by handling `KsShoveCollideEvent`.

### Grab levels

A grab *is* a pull. `PullingSystem` still owns the joint, the alerts and every way a pull ends. `KsGrabberComponent`
on the puller adds a `Stage` index into its `Stages` list of `ksGrabStage` prototypes. Only targets with
`KsGrabbableComponent` climb past stage 0; objects stay plain pulls.

`BaseSpeciesMob` has `Puller needsHands: false`, so a pull takes no hand. A hand is taken only by a stage with
`TakesHand`, as a virtual item spawned for the victim (`SharedVirtualItemSystem.TrySpawnVirtualItemInHand`).
Dropping or deleting that item ends the pull through the stock `PullingSystem.OnVirtualItemDeleted`.

#### Ctrl-click

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

#### Pinning

`ComplexShoveSystem` raises `KsShoveAttemptEvent` on the target before resolving any shove. `KsGrabSystem` handles it
when the shover holds the target at a stage that `BlocksVictimMovement`, the target is standing, and `Pinned` isn't
set yet: it knocks them down and sets `KsGrabberComponent.Pinned`. While it is set the victim can't stand, and
`PullStoppedMessage` clears it, so the pin lasts until they break free or are let go. Later shoves fall through to
the usual downed rules.

#### Resisting

`TryResist` is reached four ways:
- the stage alert (`KsResistGrabAlertEvent`)
- the pulled alert: the victim's own `TryStopPull` is cancelled in `AttemptStopPullingEvent` and turned into a resist
- a move key (`MoveInputEvent`, which fires only when the held keys change). So that a held direction keeps
  struggling, `KsGrabSystem.Update` (server) also calls `TryResist` for the victim of each grip that holds them in
  place, if they hold a direction (`InputMoverComponent.HasDirectionalMovement`) and their cooldown is up. `KsFiremanCarrySystem.Update` does the
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

**NPCs** (`HTNComponent`) struggle by themselves. `KsNpcGrabResistSystem` (server) never scans for them: it marks
an NPC with `KsNpcResistingComponent` when `KsGrabStageChangedEvent` puts it in a stage that `BlocksVictimMovement`,
or `KsCarryStartedEvent` lifts it, and only marked NPCs are updated. Each calls `KsResistSystem.TryResist` every
`Interval` (half a second), and loses the marker once nothing holds it. A plain pull isn't fought: with the one-second
delay before a pull can tighten, an NPC fighting pulls could never be gripped at all. The cooldowns decide how often an
attempt lands.

#### Strangling

`KsGrabSystem.Update` deals the stage's `Damage` every `DamageInterval`, **server only**: predicting damage would only
flicker the health bar, as with respiration. The victim is told with `DamagePopup` on a `DamagePopupChance` roll.

`Update` is one pass over the grabbers who pull someone: reel, then (server) the held-direction struggle, then the
damage. It never walks every mob.

#### Closeness

A stage's `HoldDistance` reels the victim in: `KsGrabSystem.Update` (both sides, predicted, like the grappling gun's
reel) shortens the pull's `DistanceJoint` `MaxLength` at `KsGrabber.ReelRate` until it reaches the hold distance. It then wakes
both bodies, because a shorter joint doesn't wake a sleeping body. `RequiredDistance` gates entering the stage (popup
`ks-grab-too-far`) and dealing its damage.

#### Throwing a victim

`BeforeThrowEvent` (raised by `PredictedHandsSystem.ThrowHeldItem` before it drops the thrown item) on the grabber:
if the thrown item is the grab's virtual item and the stage `AllowsThrow`, the victim must be **down**. If they're
standing, the throw is cancelled with `ks-grab-throw-need-down` (pin them first). Otherwise it swaps `ItemUid` to the
victim, scales `ThrowSpeed`, and calls `MarkThrown` (adds `KsGrabThrownComponent`). The drop that follows deletes the virtual item synchronously, which ends
the pull, so the victim flies free.

The first `ThrowDoHitEvent` on a `KsGrabThrown` victim calls `ComplexShoveSystem.KnockIntoObstacle` for an obstacle
(a person, a table) or `KnockIntoWall` for any static body. The marker goes on that hit, on `LandEvent` or on
`StopThrowEvent`. Mobs don't collide with each other
(`MobMask` lacks `MobLayer`), but a thrown entity gets the non-hard throwing fixture, which hits people's hard
fixtures, so this works for people too.

What the slam does is data on the thrower's `KsGrabber`, so a thrower without one (deleted mid-flight, or a carrier
that can't grab) leaves the victim to land as any thrown thing does:
- `ThrowImpactDamage` to the thrown (walls and people) and `ThrowCollateralDamage` to a person hit (an obstacle with
  `CollateralKnockdownTime`, so not a table). Both default to 10 Blunt and are dealt by the server only
  (`DealImpactDamage`).
- `ThrowWallKnockdownTime`, `ThrowWallSound`, `ThrowWallPopups` and `ThrowWallResistedPopups` for a wall.
- `ThrowObstaclePopups`, shown instead of the obstacle's own.

#### Thrown into an open closet

Not part of the grab: `KsEntityStorageCatchThrownComponent` (`Content.Shared/_KS14/Storage/`) goes on any
`EntityStorage` entity, and `ClosetBase` has it. Anything thrown that passes its `whitelist` (mobs, by default) and
hits the storage while it stands open ends up inside, with the door shut: a thrown grab victim, someone thrown off a
carrier's back, or a mob thrown by anything else. `KsEntityStorageCatchThrownSystem` (server, like upstream's
`ThrowInsertContainerSystem`) handles `ThrowHitByEvent` on the storage:
- An open closet's fixture keeps `BulletImpassable`, which the non-hard throwing fixture's `ThrownItem` mask hits,
  though nobody walking is stopped by it. That is why the event fires at all.
- It closes the storage first, then inserts. An open `EntityStorage` reports that anything can be inserted and
  `Insert` only drops things beside it, so size, capacity and the storage's own whitelist can only be asked once it is
  shut. Something that doesn't fit is left outside the shut door.
- `ThrowHitByEvent` is raised on the target before `ThrowDoHitEvent` on the thrown, so `KsGrabSystem.OnThrownHit`
  sees a victim already in a container and drops the `KsGrabThrown` marker without a slam.

#### Adding a grab level

Add a `ksGrabStage` to `stages.yml` (or a species-specific file) and list it in a `KsGrabber.stages`. Every effect is a
field: `upgradeTime`, `takesHand`, `blocksVictimMovement`, `floorsVictim`, `escapeChance`,
`resistFailStaminaDamageMin/Max`, `grabberSpeedModifier`, `proneDragSpeedModifier`, `damage`/`damageInterval`/
`damagePopup`/`damagePopupChance`, `allowsThrow`/`throwSpeedModifier`/`throwPopups`, `victimAlert`, `sound`,
`holdDistance`, `requiredDistance`, and `startPopups`/`enterPopups` (each gets `grabber` and `victim`). What isn't
per stage is on `KsGrabber`: `floorKnockdownTime`, `reelRate`, `upgradeCooldown` and the thrown-victim fields above. Anything a field can't express goes in a handler for `KsGrabStageChangedEvent` or
`KsGrabResistAttemptEvent`, not a new system.

### Fireman carry

Two ways in, both through `TryStartCarry`:
- **Alt-click**: an `AlternativeVerb` on `KsGrabbable` at priority 10, so it is what alt-click does.
- **Drag onto yourself**: `CanDropDraggedEvent`/`DragDropDraggedEvent` on the dragged entity. The strip menu listens to
  the same events, so `KsFiremanCarrySystem` subscribes manually with `before: [typeof(SharedStrippableSystem)]` and
  marks the event handled when a carry is possible.

A carry is possible (`CanCarry`) when the following hold. No grip is needed: tgstation needs an aggressive grab, this
server doesn't. Any pull on the victim is stopped when the carry starts.
- the victim is down and the carrier isn't
- neither is in a container or carried
- the carrier has `KsFiremanCarrier.HandsNeeded` (2) hands that are empty or hold the grab, which the carry frees

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
The depth to restore is kept on the carried entity, in the client-only `KsCarriedVisualsComponent`.

### Throw mode

There is no action. It is toggled by the HUD throw button (`KsCombatHudButtons` raises `KsToggleThrowModeEvent`) and the
`KsToggleThrowMode` key, which has no default binding (it's in the controls options). Ctrl+Q stays the instant
throw, `ThrowItemInHand`. The click itself can't be a shared `EngineKeyFunctions.Use` handler: the client runs shared handlers when the
key is pressed *and* again while predicting, and returning true from the first run stops the input reaching the
server. So `KsThrowModeInputSystem` (client, `outsidePrediction: true`, bound before `SharedInteractionSystem` and
`DragDropSystem`) swallows the click and raises a predicted `KsThrowModeThrowEvent`. This is the pattern
`ActionUIController` uses for targeted actions. The shared handler calls `ThrowHeldItem`. Throw mode
stays on until toggled off, so every click throws.

### HUD buttons

`KsCombatHudButtons` is a vertical `BoxContainer` of two `TextureButton`s, throw above resist. The icons are cropped to
tg's 32x16 label band and scaled by `SlotControl.DefaultButtonSize / 32`, so the pair is exactly one slot. They sit in `HotbarGui.xaml`
(a `KS14` edit) after `MainHotbar`, so right of the suit storage slot on every screen that uses the hotbar. It reads
the local entity's `KsThrowModeComponent` each frame to light the throw button, and hides for anything without one
(ghosts, borgs). Only the buttons hide, never the container: a hidden control gets no `FrameUpdate`, so it could never
show itself again. That was the first version's bug; `KsCombatHudButtonsTest` checks it on a real game screen. Buttons raise predicted network events, as the action UI does. Nothing headless draws, so its look
is only checked in game.

### Combat mode sounds

`SharedCombatModeSystem.OnActionPerform` (a `KS14` edit) raises `KsCombatModeToggledEvent` on the performer. The client's
`KsCombatModeSoundSystem` plays tgstation's `ui_togglecombat.ogg` / `ui_toggleoffcombat.ogg` globally to the local
player only, on the first prediction, at tg's 25% times the **interface volume** setting (`CCVars.InterfaceVolume`),
the gain `AudioUIController` gives UI clicks.
NPC toggles go through `SetInCombatMode` and make no sound.

### Traps hit while building this

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
  While a grip is reeling the victim in, a carry or the next upgrade started on them is cancelled. Keep `reelRate`
  fast, and in tests let the reel settle before starting one.
- **Overriding `PoolSettings` resets `Connected`.** A test using `RaisePredictiveEvent` needs `Connected = true`, or
  the client has no local session.
- `DeleteVirtualItem` raises `VirtualItemDeletedEvent` synchronously, so a pull ends inside the drop that removes
  its hand.

### Tests

- `Content.IntegrationTests/Tests/_KS14/ComplexShove/KsShoveTest.cs`: every shove outcome through
  `AttemptDisarmAttack`, shields included.
- `Tests/_KS14/Grab/KsGrabTest.cs`: the ladder through `TogglePull`, hand use, resists with rigged test stages
  (escape chance 0 and 1), release, throw.
- `Tests/_KS14/Grab/KsFiremanCarryTest.cs`: lift, requirements, put down, wriggle free, carrier deleted.
- `Tests/_KS14/Grab/KsThrowModeTest.cs`: a real client sends the throw request, in and out of throw mode.
- `Tests/_KS14/Storage/KsEntityStorageCatchThrownTest.cs`: an open closet catches a thrown person and shuts; a closed
  one doesn't, and an open one lets a thrown item be.

The floor ctrl-click release and the move-key resist go through input handlers no test drives yet. Check them in
game.
