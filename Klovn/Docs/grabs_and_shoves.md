# Shoves, grab levels, fireman carry and throw mode

This document describes Klovnstation's port of tgstation's combat-mode "subintents": what a shove does, the four
grab levels, fireman carrying and throw mode. It covers where each number comes from, where we left tgstation's
behaviour on purpose, and how the pieces fit together. The working reference for changing the code is the
`grabs-and-shoves` skill (`.claude/skills/grabs-and-shoves/SKILL.md`). The player-facing description is the
guidebook entry *Shoving and Grabbing*, under Controls.

## Background

Upstream SS14 shoves drain stamina: a shove deals a share of the target's stamina-crit threshold, and enough of them
stun the target. Klovnstation's earlier `ComplexShove` kept that model and added a heavier "wallshove". Upstream
pulling has no levels: ctrl-click pulls, ctrl-click again lets go, and a pull always takes a hand.

tgstation does neither. A shove never tires anyone out; what it does depends on what is behind the target. A pull is
the bottom rung of a grab ladder that ends in strangling. This port follows tgstation's structure, with numbers
chosen by the server's design brief where it gave them, and tgstation's otherwise. Sources, in `tgstation-master`:

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
  for tables and people), and `KsShoveDisarmCheckEvent` for anything else that should count
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
  - `KsShoveCollideEvent`: glass tables, lockers, disposals
  - `KsShovedEvent`: react to any shove outcome
  - `KsGrabStageChangedEvent`: martial arts, pummelling
  - `KsGrabResistAttemptEvent`: grab-resistant species, staggered victims
  - `KsPullToggleAttemptEvent`: anything else ctrl-click on a pulled thing should do

## Not ported

- tgstation's table slams with a grab: aggressive slams, and neck-grip limb smashes.
- Bear hugs, and the punch bonus against grappled targets.
- Grabbing a specific limb or your own limb.
- Shoving someone off you while they ride you.
