# Perks

A perk is a `ksPerk` prototype: a name, a description, an icon, an optional alert, and lists of things given to a body
while it holds the perk. Field Commander defenders pick one per round; the framework itself knows nothing of the mode.

| Piece | Where |
| --- | --- |
| `KsPerkPrototype` (`ksPerk`) | `Content.Shared/_KS14/Perks/KsPerkPrototype.cs` |
| `KsPerkHolderComponent`, grant record, `KsPerkAddedEvent` / `KsPerkRemovedEvent` | `Content.Shared/_KS14/Perks/KsPerkHolderComponent.cs` |
| `SharedKsPerkSystem` (give, take, ask whether held) | `Content.Shared/_KS14/Perks/SharedKsPerkSystem.cs` |
| Picker: `KsPerkChoiceComponent`, `SharedKsPerkChoiceSystem`, `KsPerkChosenEvent` | `Content.Shared/_KS14/Perks/` |
| Picker window and BUI | `Content.Client/_KS14/Perks/UI/` |
| Field Commander storage, `ks_fcperk` | `Content.Server/_KS14/FieldCommander/*Perks.cs`, `KsFieldCommanderPerkCommand.cs` |
| Perks, alerts, icons | `Resources/Prototypes/_KS14/Perks/perks.yml`, `Resources/Prototypes/_KS14/Alerts/perks.yml`, `Resources/Textures/_KS14/Interface/Perks/perks.rsi` (placeholders) |
| Text | `Resources/Locale/{en-US,ru-RU}/_KS14/perks/perks.ftl` |
| Tests | `Content.IntegrationTests/Tests/_KS14/Perks/` (`KsPerkTest`, `KsPerkDamageTest`, `KsPerkTankTest`, `KsPerkHealingTest`, `KsPerkSupportTest`), `Companions/`, `MedicalBeam/` |

## What a perk grants

```yaml
- type: ksPerk
  id: KsPerkTank
  name: ks-perk-tank-name
  description: ks-perk-tank-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: tank }
  alert: KsPerkTank          # one alert per perk; its name and description are the perk's
  components:                # added unless the body already has one
  - type: KsLivingShieldAbility
    capacity: 150
  statusEffects:             # made permanent (null duration)
  - StatusEffectSomething
  actions:                   # one instance each, through SharedActionsSystem.AddAction
  - ActionKsLivingShield
  items:                     # spawned into the hands, or at the feet
  - KsSomeItem
```

`SharedKsPerkSystem.TryAddPerk(body, perk)` records exactly what it added in `KsPerkHolderComponent.Grants`, and
`RemovePerk` undoes only that:

- **Components** the body already had are skipped and never removed.
- **Status effects**: one the perk started is removed; one that was already running on a timer is made permanent and
  later goes back to its own end time; one already permanent is left alone.
- **Actions** are removed and deleted; **items** are deleted wherever they are.
- Something two held perks both list stays until the last of them goes.

Giving and taking are server-only (they return false on the client). `HasPerk` works on both sides: the held list is
networked.

## Adding an ability

1. A component with the ability's data, in `Content.Shared/_KS14/Perks/<Perk>/` (or the feature's own folder), and a
   system acting on it. Set it up on `ComponentStartup`/`MapInit` and undo it on `ComponentShutdown`, because removing
   the perk removes the component (CONTRIBUTING §6).
2. List the component in the perk's `components`, with its tuning there rather than in C#.
3. Shared and predicted where it can be (fire rate, stamping projectiles, toggles); server-only for spawning, injecting,
   explosions.
4. A mechanism test with its own `[TestPrototypes]` perk: add it with `TryAddPerk`, check the effect, `RemovePerk`,
   check it is gone.

If an ability needs to know about the perk rather than its component, subscribe to `KsPerkAddedEvent` /
`KsPerkRemovedEvent` on the body.

## Abilities

| Perk | Ability | Component | Notes |
| --- | --- | --- | --- |
| Damage | Ramp | `KsDamageRampAbility` | +`bonusPerHit` per hit on a living mob, up to `maxBonus`; bullets carry it from when they were fired, beams get it as they land. Lost on a reload (a new magazine, or more rounds than last shot left), running dry, dropping or switching guns. Hits are counted on the server. |
| Damage | Close-range bonus | `KsCloseRangeBonusAbility` | Full `maxBonus` inside `fullBonusRange`, none past `noBonusRange`, by the distance the shot flew. |
| Damage | Falloff | `KsRangeFalloffAbility` | Full damage to `startRange`, down to `minMultiplier` at `endRange`. |
| Damage | Magazine auto-drop | `KsMagazineAutoDropAbility` | Ejects an empty magazine with a beep, for guns without `AutoEject`. |
| Damage | Explosive last rounds | `KsExplosiveLastRoundsAbility` | The last `fraction` (at least `minRounds`) of a magazine that went in full explode on hit (server); tracer tinted and an extra fire sound. |
| Tank | Crit auto-injector | `KsCritAutoInjectorAbility` | Injects `injection` on going critical; cooldown drawn over `cooldownAlert`. |
| Tank | Living Shield | `KsLivingShieldAbility`, `ActionKsLivingShield` | Soaks damage up to `capacity` (server), slows, blocks firing; breaks loudly; `cooldown` starts when it goes down. |
| Tank | Draw Fire | `KsDrawFireAbility`, `ActionKsDrawFire` | Sets the server's `KsNpcTargetPriorityComponent` for `duration`, read by `KsTargetPriorityCon` (wired into the public `NearbyGunTargets` and `NearbyMeleeTargets` by a partial); the previous priority is put back. |
| Tank | Better armour | `ksRequiredPerk` on a `CMAutomatedVendor` entry | Checked where the vend is validated; greyed out in the window for anyone else. |
| Healing | Faster metabolism | `KsMetabolismRateAbility` | Divides the metabolic interval multiplier (`GetMetabolicMultiplierEvent`) by `rateMultiplier`. |
| Healing | Medical HUD | `ShowHealthBars`, `ShowHealthIcons` | The upstream HUD components, on the body. |
| Healing | Lifesteal | `KsLifestealAbility` | Heals `fraction` of the damage dealt to a living, non-friendly mob (`DamageDealtEvent` on the target, `KsKillCreditSystem.GetAttacker`, or the holder of what dealt it); server. |
| Healing | No bleeding | `KsNoBleedingAbility` | Closes any bleed after damage opens it, and zeroes bleed ticks. |
| Healing | Medibot | `KsCompanions` with `KsMobCompanionMedibot` | A companion (below); `KsCombatMedic` beeps for `windup`, then injects the worst hurt friendly in reach. |
| Healing | Medigun | item `KsMedicalBeamGun`, `KsMedicalBeamGun` | Port of tg's medbeam: a cancelled shot locks a beam on a friendly in range and sight, healing each `tickInterval`, with `healEffect` spawned on a hurt patient (riding on them); breaks at once with a popup and buzz when range or sight is lost. Fired at no friendly it lets go quietly; a clicked mob is never passed on to aim assist (`aimAssistRadius` only helps a click on no mob). Drawn from the holder's centre by `KsMedicalBeamOverlay`. |
| Support | Ammo drop | `KsAmmoDropAbility`, `KsAmmoPickup` | A kill by the holder (a held gun's, or a companion's, counts) drops `pickup` with `chance` (server). Any living mob sharing an NPC faction the dropper had, within `radius`, gets the active-hand gun's magazine (or own ballistic store) filled with its own rounds, or its battery recharged; nothing to fill and it stays. Despawns by `TimedDespawn`. |
| Support | Shoulder turret | `KsCompanions` with `KsMobCompanionShoulderTurret`, mode `Attached` | Rides at `offset` on the owner, bobbing (`FloatingVisuals`, weightless). An NPC turret (`KsShoulderTurretCompound`: `EnergyTurretCompound` without the idle spin) with its own gun, energy bolts from `BasicEntityAmmoProvider` and `RechargeBasicEntityAmmo`, and no `KsTargetLockOn`. Its only hard fixture is on `BulletImpassable`, so shots and throws hit it and nothing walks into it; `Injurable` or it takes no damage. |
| Support | Rally | `KsRallyAbility`, `ActionKsRally`, `KsRallyZone`, `KsRallyBuff` | The call spawns `zone` (a ring drawn by `KsRallyZoneOverlay`, filling in over `delay`) with a whistle; then every living mob within `radius` sharing an NPC faction with the caller (not companions) gets `KsRallyBuff` for `duration`: speed, fire rate (through the holder-side raise of `GunRefreshModifiersEvent` in `SharedGunSystem.RefreshModifiers`) and gun damage (projectiles at fire time, hitscan through `KsHitscanDamageModifyEvent`). Start: chime, flash, popup and the `KsRallied` alert counting down; end: popup and beep. Buff shared, zone and timing server. |
| Support | Mark Target | `KsMarkTargetAbility`, `ActionKsMarkTarget`, `KsMarkedTarget` | A living mob not friendly to the holder gets `KsMarkedTarget` for `duration` (server): positive damage from anything times `damageTakenMultiplier` (`DamageModifyEvent`, so not `ignoreResistances` damage). Outlined in `outlineColor` (`KsMarkedTargetVisualsSystem`, its own post shader) for the marker and anyone sharing one of the marker's NPC factions. |

A holder-side ability changes its held guns' stats by answering `GunRefreshModifiersEvent` on the holder (raised
there after the gun, `// KS14` in `SharedGunSystem.RefreshModifiers`) and calling `RefreshModifiers` on its held guns
when its say changes, and on `DidEquipHandEvent` / `DidUnequipHandEvent` (`KsRallyBuffSystem`).

Gun abilities hear the shot through `KsShooterFiredGunEvent` (raised on the shooter after each shot), the shot's pre-fire
state through `SelfBeforeGunShotEvent`, and hitscan hits through `KsHitscanDamageModifyEvent` and
`KsShooterHitscanHitEvent`. Distance-scaled ones stamp `KsPerkShotOriginComponent` on the bullet and answer
`KsModifyPerkShotRangeDamageEvent` on the shooter.

## Companions

`KsCompanionsComponent` (`Content.Shared/_KS14/Companions/`) on an owner lists companions; the server's
`KsCompanionSystem` (`Content.Server/_KS14/Companions/`) keeps them. A perk gives the component like any other:

```yaml
- type: KsCompanions
  companions:
  - prototype: KsMobCompanionMedibot
    mode: Follow             # or Attached
    offset: 0, 0             # Attached only: where on the owner, in its frame
    respawnDelay: 60
    materialiseDuration: 1.5
    dissolveDuration: 1.5
    destroyedPopup: ks-companion-destroyed   # $companion, $seconds
    returnedPopup: ks-companion-returned     # $companion
```

- **Spawning**: on the component's map init (the perk being given) each entry is spawned at the owner and materialises
  (`KsDissolveSystem.Materialise`, the dissolve played backwards). It gets `KsCompanionComponent` (owner, index, mode,
  offset; networked), the owner's NPC factions (replacing its own) and `KsFactionCollisionShooter`. It never keeps a
  `ScenarioFactionMember`: scenario rules count those as a side's people (Field Commander's defenders, wave budget,
  round end), and a companion isn't one; enemies still fight it through its NPC factions.
- **Follow**: the companion's HTN blackboard `FollowTarget` is the owner; give it an HTN that runs `FollowCompound`.
  `SetFollowOverride(companion, target)` sends it after something else for now, `null` back to the owner.
- **Attached**: spawned parented to the owner at `offset`, and put back there whenever it is moved. Give it no hard
  fixture on the owner's layers and no mover; it turns with the owner.
- **Destroyed** (dead, or deleted without being let go): a popup to the owner, the wreck dissolves, and it comes back
  after `respawnDelay`.
- **Owner**: critical, nothing changes; dead, every companion dissolves and no timer runs; revived, they all come back.
  A new body gets the perk, so its own companions. Removing the component (the perk) deletes them.
- **API**: `TryGetCompanion(owner, index, out companion)`, `TryGetOwner(companion, out owner)`, `SetFollowOverride`.
- Tests: `Content.IntegrationTests/Tests/_KS14/Companions/KsCompanionTest.cs`.

## The picker

`SharedKsPerkChoiceSystem.RequestChoice(body, options, source)` opens a radial picker on the body for its player. It has
no close button or click-away, `Close()` is refused while it waits, and the server opens it again if the interface
closes anyway. The client's pick is checked against the options on the server. A valid one raises `KsPerkChosenEvent`
(on the body and broadcast) with the `source`; whoever asked handles it, and if nobody does the perk is just given.
`CancelChoice` stops asking.

## Field Commander

- `KsFieldCommanderRuleComponent.PerkOptions` lists the perks offered; `ChosenPerks` keeps each player's pick (by
  `NetUserId`) for the round.
- `PrepareDefender` (every ticker spawn, roundstart or latejoin, and defenders already there when the rule starts) calls
  `ApplyStoredPerk` if the player has picked, otherwise `RequestPerk`. They are separate so a pick made another way
  (before joining, later) only needs `ChosenPerks` set before the spawn.
- `SetChosenPerk` (behind `ks_fcperk <player> <perk>`) replaces a pick and swaps it onto the current body.
