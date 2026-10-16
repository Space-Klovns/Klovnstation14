---
name: npc-ranged-combat
description: How ranged NPC combat and turrets work in this repo - the per-tick NPCCombatSystem.UpdateRanged pipeline, GunOperator and NPCRangedCombatComponent, turret prototypes and their HTN compounds, and the KS14 turret features (KsNpcAimTracking, KsTargetLockOn, KsLaserSight). Use when adding, tuning or debugging a turret or any NPC that shoots - rotation speed, accuracy, lock-on delay, laser sight, "why won't it fire", "why can it be outrun" - or writing tests for them.
---

# NPC ranged combat and turrets

A shooting NPC is an HTN plan that reaches `GunOperator`; that operator hands the fight to `NPCCombatSystem.UpdateRanged`,
which runs every tick. HTN decides *whether and at whom* to shoot (see the npc-htn skill for compounds, operators,
queries); `UpdateRanged` decides *how and when* (LOS, aim, fire). Turrets are ordinary NPCs with a gun, a slow
`RotateSpeed`, and optionally the three KS14 components below.

Files:

| What | Path |
| --- | --- |
| Pipeline (upstream, with `// KS14` hooks) | `Content.Server/NPC/Systems/NPCCombatSystem.Ranged.cs` |
| KS14 hooks (aim, lock, laser) | `Content.Server/NPC/Systems/NPCCombatSystem.Klovn.TurretTracking.cs` |
| Per-fight state | `Content.Server/NPC/Components/NPCRangedCombatComponent.cs` |
| Operator that owns that component | `Content.Server/NPC/HTN/PrimitiveTasks/Operators/Combat/Ranged/GunOperator.cs` |
| Rotation | `Content.Shared/Interaction/RotateToFaceSystem.cs` (`TryRotateTo`) |
| Aim tracking (server-only) | `Content.Server/_KS14/NPC/Components/KsNpcAimTrackingComponent.cs` |
| Lock-on (shared) | `Content.Shared/_KS14/LockOn/` (`KsTargetLockOnComponent`, `SharedKsTargetLockOnSystem`) |
| Laser sight (shared) | `Content.Shared/_KS14/LaserSight/` (`KsLaserSightComponent`, `KsLaserSightSystem`) |
| Client visuals | `Content.Client/_KS14/LaserSight/`, `Content.Client/_KS14/LockOn/` |
| Turret prototypes | `Resources/Prototypes/Entities/Objects/Weapons/Guns/Turrets/turrets_{base,ballistic}.yml`, `Resources/Prototypes/_KS14/Entities/Objects/Weapons/Guns/Turrets/turrets_{ballistic,laser}.yml` |
| HTN | `Resources/Prototypes/NPCs/root.yml` (`TurretCompound`, `EnergyTurretCompound`), `Resources/Prototypes/_KS14/NPCs/turret_tasks.yml` (`LaserTurretCompound`), `utility_queries.yml` (`NearbyGunTargets`) |
| Tests | `Content.IntegrationTests/Tests/_KS14/NPC/KsNpcTurretTrackingTest.cs`, `KsNpcCombatTuningTest.cs` |

## 1. The `UpdateRanged` pipeline

Runs for every entity with `NPCRangedCombatComponent` (queried with `TransformComponent`). `comp` below is that
component. Steps in order; "exit" means `continue` to the next NPC unless it says otherwise.

| # | Step | Result / knob |
| --- | --- | --- |
| 1 | `Status == Unspecified` | `KsStopTargeting`, exit. Set at startup when the NPC has no `CombatModeComponent`, and by step 6 (out of ammo). A turret in this state never recovers by itself; `GunOperator` fails and removes the component. |
| 2 | Steering has `NoPath` | `Status = TargetUnreachable`, `ShootAccumulator = 0`, `KsStopTargeting`, exit. |
| 3 | Target has no transform/physics, or is on another map | same as 2. |
| 4 | Combat mode forced on (if the NPC has `CombatModeComponent`) | |
| 5 | `_gun.TryGetGun` fails | `Status = NoWeapon`, `KsStopTargeting`, exit. |
| 6 | `GetAmmoCountEvent` count 0 | If the gun has `RechargeBasicEntityAmmoComponent`: `KsUpdateSight(uid, target, false)` then `continue` (no status change; laser off and the lock grace applies, as for lost sight). Otherwise `Status = Unspecified`, `KsStopTargeting`, exit. |
| 7 | LOS cadence: `LOSAccumulator -= frameTime`; when `< 0`, add `UnoccludedCooldown` (const `0.2f`) and re-raycast with `_interaction.InRangeUnobstructed(uid, target, distance + 0.1f, group)` into `comp.TargetInLOS` | Between raycasts the old answer is reused. `group` = `CollisionGroup.Opaque` if `comp.UseOpaqueForLOSChecks` (YAML `opaqueKey: true` on `GunOperator`), else `Impassable \| InteractImpassable` (glass and grilles block). |
| 8 | `KsUpdateSight(uid, target, TargetInLOS)` | Every tick (not only on raycast ticks): `KsTargetLockOn.UpdateLock`; if `KsLaserSight.OnlyWhileTargeting`, `SetEnabled(TargetInLOS)`; if not in LOS, tracking `LastGoalAngle = null`. |
| 9 | `!TargetInLOS` | `ShootAccumulator = 0`, `Status = NotInSight`, `steering.ForceMove = true`, exit. |
| 10 | Newly in LOS and `SoundTargetInLOS != null` | `PlayPvs` the sound (blackboard key `SoundTargetInLOS`). |
| 11 | `ShootAccumulator += frameTime` | |
| 12 | KS14: if the NPC has `KsNpcAimTracking` with `RotateWhileWaiting`, run `KsTryAim` now (aim every tick in LOS) | See section 3. |
| 13 | `ShootAccumulator < ShootDelay` | exit (upstream: no rotation happened). `ShootDelay` is set by `GunOperator.Startup` to `1 / gun.FireRate`; default `0.2`. |
| 14 | `KsTryAim` (unless step 12 already ran it): lead target, compute goal angle, `TryRotateTo` | Returns false while further than the tolerance from the goal: exit. |
| 15 | `!Enabled \|\| !_gun.CanShoot(gun)` | exit. `Enabled` is the system-wide public toggle (`NPCCombatSystem.Enabled`). |
| 16 | `!_lockOnSystem.IsLocked(uid, target)` | exit. Missing `KsTargetLockOn` counts as locked. **Aim (step 14) has already happened**; lock-on only delays firing. |
| 17 | Build `targetCordinates` from the lead spot (grid-local if the target is over a grid) | |
| 18 | `Status = Normal` | Only reached once aimed, able to shoot and locked. |
| 19 | `gun.NextFire > CurTime` | **`return`, not `continue`** (verified still present). Ends the whole `UpdateRanged` pass, so NPCs later in the enumeration get no update that tick. Upstream quirk, left alone; keep in mind when profiling or debugging "other turrets lag". |
| 20 | `_gun.AttemptShoot(uid, gun, targetCordinates, target)` | `ShootAccumulator` is deliberately **not** reset (KS14 comment): it is a LOS-acquired delay, not a fire-rate cap. Fire rate comes from the gun. |

Where each knob comes from:

| Knob | Source |
| --- | --- |
| `Target` | `GunOperator.targetKey` blackboard entity, re-read every `Update` |
| `RotationSpeed` | blackboard `RotateSpeed` (`NPCBlackboard.RotateSpeed`, a `float`, rad/s) copied at `GunOperator.Startup`. Absent = instant turning. Turret YAML sets `3.141` in `BaseWeaponTurret`. |
| `AccuracyThreshold` | field default 30 degrees; no YAML or blackboard source. Override per-entity with `KsNpcAimTracking.accuracyThreshold`. |
| `UseOpaqueForLOSChecks` | `GunOperator.opaqueKey` |
| `SoundTargetInLOS` | blackboard `SoundTargetInLOS` (`!type:SoundPathSpecifier`) |
| `ShootDelay` | `1 / GunComponent.FireRate` at Startup |
| `Status` | written by the pipeline; read by `GunOperator.Update` |
| Range | blackboard `RangedRange` (default `10f` in `NPCBlackboard`), used by the HTN preconditions, not by `UpdateRanged` |
| `ShootSpeed` | hardcoded const `20f` in `NPCCombatSystem.Ranged.cs` |

`CombatStatus` (declared in `NPCMeleeCombatComponent.cs`) -> `GunOperator.Update`: `TargetUnreachable` fails; `NotInSight`
fails if `requireLOS: true`, else continues; `Normal` continues; anything else (`Unspecified`, `NoWeapon`) fails. A
target whose `MobState` is above `targetState` (default `Alive`) finishes the task.

Consequence worth knowing: `Status` is only reset to `Normal` at step 18. After sight is regained, a turret that is
still turning or still locking keeps `Status = NotInSight`. With `requireLOS: true` that fails the task on the next
HTN tick, which removes `NPCRangedCombatComponent` (and so clears the lock via the shutdown hook). In practice the
`LoseLockGrace` only matters for compounds with `requireLOS: false` (`EnergyTurretCompound`).

### `GunOperator` lifecycle

`Startup` does `EnsureComponent<NPCRangedCombatComponent>`, copies target, `opaqueKey`, `RotateSpeed`,
`SoundTargetInLOS`, and sets `ShootDelay`. `ConditionalShutdown` turns combat mode off, **removes the component**, and
removes the target key. So the component exists only while the NPC is in the shooting task: it is server-only (no
networking, not `[NetworkedComponent]`), and a test or tool that wants a shooter without HTN must
`EnsureComponent<NPCRangedCombatComponent>(shooter).Target = target` itself (see section 6). The shutdown hook
(`OnRangedShutdown` -> `KsOnRangedShutdown`) clears the lock, turns off an `OnlyWhileTargeting` laser, and nulls
`LastGoalAngle` and `LastTarget`. It returns early if the entity is terminating.

## 2. Turret anatomy

Prototype chain (all in `Resources/Prototypes/Entities/Objects/Weapons/Guns/Turrets/` unless noted):

| Prototype | Role |
| --- | --- |
| `BaseWeaponTurret` (abstract, `turrets_base.yml`) | Structure with `CombatMode` (`toggleMouseRotator: false`), `Damageable`/`Destructible`, `HTN` rootTask `TurretCompound`, blackboard `RotateSpeed: 3.141` and `SoundTargetInLOS` (double_beep), `NoRotateOnInteract`, `NoRotateOnMove`, `EmpAffectedNpc`. |
| `BaseWeaponBallisticTurret` | + `ContainerContainer` `ballistic-ammo`, `Gun` (fireRate 6, FullAuto), `BallisticAmmoProvider` (caseless rifle, 500). |
| `BaseWeaponEnergyTurret` | + sentry sprite, `Gun` projectileSpeed 15 / fireRate 1.5, `BatteryAmmoProvider` + `ApcPowerReceiverBattery`, rootTask `EnergyTurretCompound`. |
| `WeaponTurretSyndicate` (`turrets_ballistic.yml`) | Syndicate-faction ballistic turret (`NpcFactionMember`). |
| `BaseWeaponTurretDisposable` / `KsWeaponTurretNanoTrasenDisposable` (`_KS14/.../turrets_ballistic.yml`) | Disposable pistol-ammo turret that explodes; NT faction, comes from a flatpack. |
| `WeaponTurretSyndicateLaser` (`_KS14/.../turrets_laser.yml`) | Self-charging laser turret (`BatterySelfRecharger`, `RedMediumLaser`), rootTask `LaserTurretCompound`. Its `HTN` entry only sets `rootTask`; component entries merge field by field with the parent's, so `BaseWeaponTurret`'s `blackboard` (`RotateSpeed`) is inherited. Redefining `blackboard:` in a child replaces the whole dictionary, so repeat every key you still need. |
| `KsWeaponTurretSyndicateTracking`, `KsWeaponTurretSyndicateLaserTracking` | Concrete examples with all three KS components (section 4). |

Notes on components:

- Faction (`NpcFactionMember`) decides who `NearbyHostilesQuery` finds. A turret with no faction attacks nobody.
- `MouseRotator` and `Input` on `BaseWeaponTurret` are inherited from the player-controlled-turret days; **no NPC code
  uses `MouseRotator`**. Rotation is done by `RotateToFaceSystem` on the transform. Do not tune `rotationSpeed` there.
- `NoRotateOnMove` / `NoRotateOnInteract` stop other systems from re-facing the turret.
- Aim, lock and laser components are added **in the entity's YAML**; nothing adds them at runtime.

### HTN compounds

All three: `UtilityOperator NearbyGunTargets` writes `Target`, then a `GunOperator` task guarded by
`KeyExists Target`, `TargetInRange` (`RangedRange`) and `TargetInLOS`, with a `UtilityService RangedService` re-running
`NearbyGunTargets` to retarget. Fallback branch: `IdleSpinCompound` (random rotations at `RotateSpeed`).

| Compound | File | `TargetInLOSPrecondition.opaqueKey` | `GunOperator.opaqueKey` | `GunOperator.requireLOS` |
| --- | --- | --- | --- | --- |
| `TurretCompound` | `NPCs/root.yml` | default `true` (precondition's code default) | false (code default) | `true` |
| `EnergyTurretCompound` | `NPCs/root.yml` | `true` | `true` | false (code default) |
| `LaserTurretCompound` | `_KS14/NPCs/turret_tasks.yml` | `true` | `true` | `true` |

"Opaque" means only opaque things (walls, closed doors) block sight; glass and grilles do not. Without it, glass blocks
the raycast. Energy and laser turrets shoot through windows; the ballistic compound's operator does not (its
precondition defaults to opaque, so the turret may plan a shot the operator's own LOS check then rejects: `NotInSight`
-> task fails -> replans). Match `opaqueKey` between precondition and operator in anything new.

`NearbyGunTargets` (`utility_queries.yml`): `NearbyHostilesQuery` then considerations alive, not crit, not stunned,
`TurretTargetingCon` (marker consideration with no fields), distance, health, accessible, `TargetInLOSOrCurrentCon`.

Blackboard keys turrets care about:

| Key | Type | Effect |
| --- | --- | --- |
| `RotateSpeed` | float (rad/s) | Base turning speed, copied into `RotationSpeed`. Remove it for instant turning. |
| `SoundTargetInLOS` | `SoundSpecifier` | Played at the turret when LOS to the target is first gained. |
| `RangedRange` | float, default 10 | Engagement range in the preconditions. |

Set them in the HTN component:

```yaml
- type: HTN
  rootTask:
    task: LaserTurretCompound
  blackboard:
    RotateSpeed: !type:Single
      2.0
    RangedRange: !type:Single
      14
```

(Overriding `blackboard` on a child prototype replaces keys by name; re-state `SoundTargetInLOS` only if you want to
change it. Verify merged values in VV on the `HTN` component.)

## 3. The KS14 components

Sources: server hooks in `NPCCombatSystem.Klovn.TurretTracking.cs`; components are independent, each optional.

### `KsNpcAimTracking` (server only, `Content.Server._KS14.NPC.Components`)

| Field | Type | Default | Effect |
| --- | --- | --- | --- |
| `angularVelocityCompensation` | float | `1` | Multiplier on the target's angular velocity (rad/s, seen from the shooter) added to the base speed. `1` = once caught up, strafing cannot out-run it. |
| `maxRotationSpeed` | Angle? | null | Hard cap on the final speed per second. Plain YAML number = degrees, `3.14 rad` for radians (`AngleSerializer`). null = none. |
| `rotateWhileWaiting` | bool | `true` | Aim every tick in LOS, including during `ShootDelay` and lock-on. `false` = upstream behaviour (turn only after the delay). |
| `accuracyThreshold` | Angle? | null | Replaces `NPCRangedCombat.AccuracyThreshold` (30 degrees) as the tolerance in step 14. Plain number = degrees (examples use `5`). |
| `LastGoalAngle`, `LastTarget` | VV only | | Runtime state. |

Math in `KsTryAim`, per tick:

1. `targetSpot = targetPos + targetVelocity * distance / ShootSpeed` (`ShootSpeed = 20`, hardcoded: **not** the gun's
   `projectileSpeed`; hitscan and slow projectiles are led by the same fixed amount).
2. `goal = (targetSpot - worldPos).ToWorldAngle()`; `base = RotationSpeed ?? double.MaxValue`; `tolerance = AccuracyThreshold`.
3. If the shooter has tracking: on a new target reset `LastGoalAngle`. If `base < float.MaxValue` (i.e. a finite
   `RotateSpeed`; instant turners are never compensated):
   `omega = min(|ShortestDistance(LastGoalAngle, goal)| / frameTime, 4*pi)` (the clamp, `MaxTrackedAngularVelocity`,
   treats bigger jumps as teleports), `speed = base + omega * compensation`, then `min(speed, MaxRotationSpeed)`.
   Store `LastGoalAngle = goal`. Apply the `AccuracyThreshold` override.
4. `RotateToFaceSystem.TryRotateTo(uid, goal, frameTime, tolerance, speed, xform)`: turns at most `speed * frameTime`
   per call and returns true only when within `tolerance` of the goal (instant turners always return true).

Resets: `LastGoalAngle = null` on target change, on any tick with no LOS (`KsUpdateSight`), in `KsStopTargeting`
and on shutdown. The first tick after a reset adds no compensation (no spike).

### `KsTargetLockOn` (shared, networked, `[Access(typeof(SharedKsTargetLockOnSystem))]`)

Put it on the shooter. The shooter must hold a lock for `lockOnTime` before step 16 lets it fire. It delays firing
only; aiming continues.

| Field | Type | Default | Effect |
| --- | --- | --- | --- |
| `lockOnTime` | TimeSpan (seconds number) | `1.5` | Time from acquiring a visible target to being allowed to fire. |
| `loseLockGrace` | TimeSpan | `0.5` | Sight lost for longer than this clears the lock (full `lockOnTime` again on reacquire); shorter keeps progress. |
| `indicatorColor` | Color | Red | Reticle colour drawn on the target's screen. |
| `lockingSound` | SoundSpecifier? | null | Played by the server from the **turret** (`PlayPvs`, so everyone in range hears it) when a lock starts. Not networked. |
| `lockedSound` | SoundSpecifier? | null | Same, when the lock completes. |
| `Target`, `LockStartTime`, `LockEndTime` | networked state | | Runtime; times are `[AutoPausedField]`. |
| `LostSightTime` | TimeSpan? | | Server-only bookkeeping, not networked. |

API (`SharedKsTargetLockOnSystem`): `UpdateLock(entity, target, visible)` (called by the pipeline every tick; a new
or different visible target starts a lock; same visible target clears `LostSightTime`; invisible sets it and clears
once past the grace), `IsLocked(entity, target)` (true if no component, else `Target == target && CurTime >=
LockEndTime`), `GetLockProgress(entity)` (0 to 1), `ClearLock(entity)`. Write only through these; the component is
`[Access]`-restricted.

### `KsLaserSight` (shared, networked, `[Access(typeof(KsLaserSightSystem))]`)

Generic: any entity with it draws a beam along its world facing. Not NPC-specific, but the server toggles it for NPCs.

| Field | Type | Default | Effect |
| --- | --- | --- | --- |
| `enabled` | bool | `true` | Drawn now. Server-written; use `KsLaserSightSystem.SetEnabled` (early-outs if unchanged, dirties). |
| `onlyWhileTargeting` | bool | `false` | Pipeline sets `enabled = TargetInLOS` each tick and `false` on stop/shutdown. `KsLaserSightSystem` forces `enabled = false` on `MapInit` when this is set, so the beam is dark while idle; the example prototypes also say `enabled: false` for clarity. |
| `color` | Color | Red | Beam colour (alpha honoured). |
| `width` | float | `0.05` | Core width, metres. |
| `glowWidth` | float | `0.2` | Faint wide quad; `0` disables. |
| `glowAlpha` | float | `0.25` | Glow opacity multiplier. |
| `intensity` | float | `2.2` | Emissive bloom intensity (light-target pass). |
| `maxLength` | float | `12` | Metres. |
| `fadeStart` | float | `0.5` | Sets a constant fade length `(1 - fadeStart) * maxLength`: the beam is opaque until that far before its end and reaches alpha 0 exactly at its end, i.e. at `maxLength` when free or at the hit point when cut by an obstacle (fade starts at `max(0, hit - fadeLength)`). `1` = no fade. Drawn as a per-vertex-alpha triangle strip (`KsLaserSightMath.GetFadeStrip`) in all passes. |
| `offset` | Vector2 | `0,0` | Origin in the entity's local frame (rotates with it): line it up with the barrel. |
| `angleOffset` | Angle | `0` | Added to facing for sprites whose barrel is not "forward". |
| `stopAtObstacles` | bool | `true` | Client raycasts (opaque) and shortens the beam. |
| `blinkWhileLocking` | bool | `true` | While this entity's `KsTargetLockOn` has a target and is not yet locked, beam blinks. |
| `blinkFrequency` | float | `6` | Hz. Solid once locked. |

Client side (describe-level only; may be changing): `KsLaserSightOverlaySystem` resolves each enabled laser
once per frame (culled to the eye's world viewport enlarged by `maxLength`; the obstacle raycast is cached in the client-only `KsLaserSightClientStateComponent` for up to 0.1 s while origin and direction hardly change) from these fields (position, facing, offset, length/raycast, blink state from the entity's
`KsTargetLockOn`) and feeds two overlays: a world-space coloured beam and an emissive pass (`Emissive` shader) so it
glows in the dark. `SharedKsTargetLockOnSystem.UpdateLock` (server-only caller) plays `lockingSound` / `lockedSound` from the turret with
`PlayPvs`, once per lock (`LockedSoundPlayed`); `KsLockOnIndicatorOverlay` draws
shrinking corner brackets (colour `indicatorColor`, progress from `GetLockProgress`) around the local player for each
shooter locking onto them.

### How they interact

- Pipeline order for one tick of a tracking turret: LOS -> `KsUpdateSight` (lock, laser) -> accumulate -> aim (every
  tick with `rotateWhileWaiting`) -> `ShootDelay` -> `CanShoot` -> `IsLocked` -> fire. A turret can be fully aimed
  and still silent because the lock is unfinished; the laser blinks during exactly that window.
- `KsStopTargeting` (steps 1, 2, 3, 5, 6-nonrecharge): clears the lock, switches off an `OnlyWhileTargeting` laser, nulls
  `LastGoalAngle`. It is not called for no-LOS (step 8 handles that through `UpdateLock` grace); the recharging-empty case goes through `KsUpdateSight` with `visible = false`.
- Dropping the lock on LOS loss is graceful (`loseLockGrace`), the laser is not (it goes off immediately), and the aim
  history resets immediately.
- Without any KS components, behaviour is identical to upstream except `rotateWhileWaiting` code path being skipped
  and the always-true `IsLocked`.

## 4. Recipes

### New turret, Synthetik style

Copy the example prototypes. Ballistic (`_KS14/.../turrets_ballistic.yml`):

```yaml
- type: entity
  parent: WeaponTurretSyndicate
  id: KsWeaponTurretSyndicateTracking
  suffix: Syndicate, Tracking
  components:
  - type: KsLaserSight
    onlyWhileTargeting: true
  - type: KsTargetLockOn
    lockOnTime: 1.2
    lockingSound:
      path: /Audio/Machines/quickbeep.ogg
    lockedSound:
      path: /Audio/Machines/twobeep.ogg
  - type: KsNpcAimTracking
    accuracyThreshold: 5
    angularVelocityCompensation: 1
```

The laser variant (`KsWeaponTurretSyndicateLaserTracking`, parent `WeaponTurretSyndicateLaser`) is identical with
`lockOnTime: 1.6`. For a new turret: pick the parent (`BaseWeaponBallisticTurret`, `BaseWeaponEnergyTurret`,
`WeaponTurretSyndicateLaser`), set faction, add the three components, put the file under
`Resources/Prototypes/_KS14/Entities/Objects/Weapons/Guns/Turrets/`, give the id a `Ks` prefix. Add
`offset`/`angleOffset` on `KsLaserSight` if the beam does not come out of the barrel. Check the beam, the blink, the lock sound and the
fire delay in-game or with the test pattern in section 6.

### Mob NPC with a laser sight

Same idea, no HTN changes: add `KsLaserSight` with `onlyWhileTargeting: true` (and `enabled: false` so it is dark
while idle) to the mob prototype. The mob needs a gun and a `GunOperator` task; the beam follows the mob's world
facing, so it only looks right if the mob actually faces its target (instant turners do; mobs with a `RotateSpeed`
turn at that rate). Use `offset` to place it at the hand. Omit `KsTargetLockOn` to avoid a firing delay.

### Turret too easy to outrun / too accurate

| Symptom | Change |
| --- | --- |
| Strafing target escapes | Add `KsNpcAimTracking` with compensation `1`. Check `maxRotationSpeed` is not capping it. Check the turret has a finite `RotateSpeed` (instant turners never need it). |
| Still outrun at close range | Angular velocity grows as distance shrinks: raise `angularVelocityCompensation` above `1` (the `4*pi` omega clamp is a code constant, not tunable). |
| Hits too easily | Raise `lockOnTime`; `ShootDelay` is not YAML-tunable (it is `1/FireRate`), so lower the gun's `fireRate`; tighten `accuracyThreshold`; add a gun `minAngle`/`maxAngle` spread. Reduce `maxRotationSpeed`. |
| Fires while turning | Lower `accuracyThreshold` (default 30 degrees fires almost any time it is roughly facing). |
| Never fires | Lock-on stuck (LOS flapping resets it after the grace), `accuracyThreshold` too tight for the speed, gun not `CanShoot`, empty ammo (step 6), or no faction. |
| Leading is off | `ShootSpeed` is a hardcoded 20; a slow projectile will miss a moving target. Change the const only knowingly, it affects all NPC shooters. |

## 5. Gotchas

- `NPCRangedCombatComponent` is server-only and exists only while `GunOperator` runs. Don't query it from shared or
  client code; don't expect it to exist between targets. Reading it in a test requires it to be added by hand.
- Target leading uses the hardcoded `ShootSpeed = 20f`, not `GunComponent.ProjectileSpeed`.
- The `Ammo`/`NextFire` early-exit is `return`, not `continue` (step 19). One rate-limited gun skips the rest of the
  frame's NPCs.
- Upstream does not rotate during the `ShootAccumulator < ShootDelay` wait. Only `rotateWhileWaiting` (default true on
  `KsNpcAimTracking`) changes that.
- `AccuracyThreshold` defaults to 30 degrees, wide enough that an untuned turret fires while still turning.
- The LOS raycast runs at most every `UnoccludedCooldown` = 0.2 s; `TargetInLOS` is stale in between. Tests that move a
  target out of sight must wait more than 0.2 s.
- `MouseRotator` and `Input` on turrets are unused by NPC code.
- `[Access]` on `KsTargetLockOn` and `KsLaserSight`: write through `SharedKsTargetLockOnSystem` /
  `KsLaserSightSystem`, otherwise `RA0002`. `KsNpcAimTracking` has no `[Access]` (server-side, written by
  `NPCCombatSystem`).
- Opaque mismatch between precondition and operator: see section 2.
- `NPCCombatSystem.Enabled = false` stops all NPC firing but not aiming, locking or the laser.
- In tests, `WeaponPistolMk58` spawns with an empty chamber (`KsOperativeHtnRootTest.StageArmed` chambers a round via
  `SharedGunSystem.UseChambered`), so a freshly spawned one will not fire. `KsNpcTurretTrackingTest` uses
  `WeaponLaserCarbine` instead, which is loaded.
- Tests that must not fire use a lock that never completes (`lockOnTime: 1000`): firing runs the gun out of ammo,
  and step 6 then ends the combat update (`Status = Unspecified`).
- Pooled tests with `PsDisconnected` never start client systems, so the visuals (overlays, sounds) cannot be tested
  there; test the server-side state (`KsTargetLockOnComponent.Target`, `KsLaserSightComponent.Enabled`).

## 6. Testing

Patterns from `KsNpcTurretTrackingTest` (`GameTest`, `PoolSettings => PsDisconnected`) and `KsNpcCombatTuningTest`:

- Mobs and helpers come from `KsNpcSquadTestHelpers` (`SpawnAt`, `MakeGrid`, `SyndicateMob` = `KsSquadTestMobSyndicate`,
  `NanoTrasenMob`). Define test prototypes in a `[TestPrototypes] const string`, parenting
  `KsSquadTestMobSyndicate` and adding `Hands` and `CombatMode`.
- Open grid: `CreateTestMap`, `MakeGrid(..., new Vector2i(-5,-5), new Vector2i(12,5))`, `SpawnAt` shooter at `(0,0)` and
  target at `(5,0)`.
- Arm a shooter: `AddHand(shooter, "right", HandLocation.Right)`, spawn `WeaponLaserCarbine`, `TryPickup`.
- Skip HTN: `EnsureComponent<NPCRangedCombatComponent>(shooter).Target = target`, and set `RotationSpeed` and
  `AccuracyThreshold` directly. Give the target godmode (`SharedGodmodeSystem.EnableGodmode`) so it survives.
- Ammo check: raise `GetAmmoCountEvent` on the gun and compare counts.
- Run in ticks: `Pair.RunTicksSync((int)Math.Ceiling(seconds * tickRate))`.
- Lock-on without a fight: call `UpdateLock` directly with a visible flag and `RunTicksSync` between calls to cross
  the grace.
- Tracking: move a target on a circle each tick with `SetWorldPosition`, settle then catch up for a few seconds, then
  record the max angular error between `GetWorldRotation(shooter)` and the target bearing. Run the same scene with
  and without `KsNpcAimTracking` as a `[TestCase(true/false)]` pair (the two cases must each pin different outcomes
  so neither passes vacuously).
- For HTN operators in isolation, construct the operator, `InjectDependencies(op, oneOff: true)`, and drive
  `Startup`/`Update` with a hand-built `NPCBlackboard` (`KsNpcCombatTuningTest`).

`TestLaserLifecycle(true/false)` covers the laser: dark after MapInit, lit with a target, dark and lock cleared after the target is deleted or the combat component removed.

Run: `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~KsNpcTurretTrackingTest"`.

### Prove the test fails

Break the thing, rebuild, see red, restore (CLAUDE.md section 6). Breaks that each test should catch:

| Test | Break |
| --- | --- |
| `TestLockOnGatesFiring` | Delete the `IsLocked` gate (step 16) in `UpdateRanged`: the ammo-unchanged assertion at 2.5 s goes red. |
| `TestLockDroppedAfterGrace` | Make `UpdateLock` ignore `LoseLockGrace` (never clear, or clear immediately): the within-grace or past-grace assertion fails. |
| `TestAimTrackingKeepsUp(true)` | Zero the compensation in `KsTryAim` (or `angularVelocityCompensation: 0`): `maxError` exceeds the threshold. |
| `TestLaserLifecycle` | Remove the `MapInit` handler in `KsLaserSightSystem` (idle assert red); remove the laser-off from `KsStopTargeting` and `KsOnRangedShutdown` (end assert red). |
| `TestAimTrackingKeepsUp(false)` | Give the plain shooter `KsNpcAimTracking`: it no longer falls behind. |

The first three were run when the tests were written and each went red: ammo 16 -> 12 before the lock completed;
`Target` expected null but was a uid; max error 3.1 rad. One trap found doing so: a 1 s lock checked at 0.5 s stayed
green with the gate removed, because the gun had not reached its first shot yet anyway. Keep the lock (3 s) and the
check (2.5 s) well past the gun's first possible shot. Re-run the breaks after modifying a test.
