---
name: prediction
description: How predicted spawns work in this repo - the engine's PredictedSpawn stand-ins versus KsSharedPredictedSpawnSystem's kept copies (user mode, BeginUserPrediction, sprays), one-shot effects, IsFirstTimePredicted for kept copies, PredictedQueueDelDeferrable and the state-reset contact crash, placeholders (KsTilePlaceholderSystem, Supersede) for what a kept copy spawns, never touching networked state from a kept copy, and predicted vapors and tile reactions (ShouldReact, client reaction classes, ordering before physics). Use when spawning anything from predicted shared code, making bullets, sparks, sprays or vapors feel instant, predicting puddles, foam or tile reactions, or when a predicted entity teleports, flickers, replays its animation, vanishes a round trip after spawning, doubles up, or crashes the client during state application.
---

# Predicted spawns

A predicted spawn makes two copies: the predicting client's and the server's. The two APIs differ in **which copy the
predicting client ends up showing**. Pick by that.

| | Engine `PredictedSpawn*` (`EntityManager`/`EntitySystem`) | `KsSharedPredictedSpawnSystem` with a `user` |
| --- | --- | --- |
| Client's copy | A stand-in, deleted on every applied state (`ClientGameStateManager.ResetPredictedEntities`) and re-created only while its tick is re-predicted: gone about a round trip after spawning. | Kept for its whole life, on that user's client only. |
| Server's copy | Replaces the stand-in for everyone. | Hidden and inert on that user's client (no sprite, light, collisions or predicted physics); shown to everyone else. |
| Use for | Anything long-lived or that gameplay reads: fires, foam blankets. | Short-lived movers the user caused: their bullets, sparks, vapors. |

- Without a `user`, the KS system is the engine spawn plus predicted physics, which only matters for something that
  moves (a spark). Anything anchored or static uses the engine spawn.
- **A one-shot animated effect (an EMP pulse, a flash) is neither**: each re-created stand-in restarts its animation, so
  it replays for a round trip. Spawn it on the server only, as `SharedEmpSystem` does.
- User mode exists because the engine's handover is a visible jump for fast movers: a lag-compensated bullet's server
  copy isn't where the client's is. Showing both is a desync.

## User-mode rules nothing enforces

- **Pass `user` only on that user's own predicted path**, code their client also runs for their own input. On a
  server-only path it hides the server's copy from a client that made none, so the user sees nothing. Teleports,
  damage effects, triggers and spells pass none.
  - Guns open a `BeginUserPrediction` scope in `SharedGunSystem.OnShootRequest`, and only projectiles spawned inside it
    are marked: `PlayerShotProjectileEvent` also fires for server-only shots (spells, autofire).
  - Sprays pass one only from their click and activate-in-world handlers (`SharedSpraySystem.Klovn.Predicted.cs`); a
    verb, action or test sprays the server's vapors alone.
- **The kept copy never hears from the server again**, so it can't follow anything done to the real one. That's why
  gameplay-relevant entities don't use this mode.
- **Gate a kept copy's one-off effects on `KsSharedPredictedSpawnSystem.IsFirstTimePredicted(uid)`**, not
  `IGameTiming.IsFirstTimePredicted`. Its physics steps only on first-time ticks, but its contacts are found by the next
  pass, usually a re-predicted tick, so the timing gate drops its impact effect.
- **Hide or mark things through a networked component, never a network event naming the entity**: the event can arrive
  before the entity's state. `KsPredictedSpawnComponent.PredictingUser` does this. `PredictedDel` of a networked entity
  on the client is only a predicted detach, which the next state restores, so it can't hide one.

The client system does three things, each of which looks fine until it doesn't: removes `PredictedSpawnComponent` so the
state reset spares the copy; blocks its physics on re-predicted ticks, since nothing rolls a client-only entity back;
and restores its simulated position before each step, since the transform lerp writes the rendered position over the
real one and only networked entities get that overwritten. `KsPredictedSpawnTest.KeptCopySurvivesStatesAndMovesAtItsOwnSpeed`
fails without any of them.

## Deleting a kept copy from a collision handler

Use **`PredictedQueueDelDeferrable`**, not `PredictedQueueDel`. The state reset raises `StartCollideEvent` and
`EndCollideEvent` from a contact list gathered beforehand (`PhysicsSystem.ResetContacts` → `UpdateIsTouching`,
`Robust.Client/Physics/PhysicsSystem.Predict.cs`) and never checks whether an earlier event destroyed a later entry.
Queue-deleting a client-side entity destroys its contacts at once (`SharedPhysicsSystem.OnContactEntityQueueDel`),
returning them to the pool with their entities cleared to `EntityUid.Invalid`, and the next entry throws
`KeyNotFoundException: Entity 0 does not have a component of type TransformComponent` from `ResetPredictedEntities`.
Only kept copies hit this, as engine stand-ins are deleted before `ResetContacts`. The deferrable form flags the copy
during state application and deletes it before the next physics step. The same goes for anything else a collision
handler does that destroys contacts, such as `SetCanCollide(false)`. `KsPredictedSpawnTest.KeptProjectileHitByTheStateReset`
reproduces it.

## What a kept copy spawns: placeholders

The engine re-creates its stand-ins only by re-running the code that spawned them, and nothing re-runs a kept copy. So
whatever a kept copy spawns with `PredictedSpawn*` is deleted by the next state reset and never returns; that's how the
extinguisher shot's blankets vanished from every tile but the first. Anything it spawns that must last until the
server's own copy arrives is a plain client-side entity, with a handover:

- **Mark it**: spawn with `KsTilePlaceholderSystem.Spawn` (client), which adds `KsTilePlaceholderComponent` and an expiry.
  Until handed over it's the real thing on that client: a placeholder blanket keeps chemfires out, a placeholder puddle
  is slippery.
- **Hand over**: when the server's copy for the tile arrives, call `KsTilePlaceholderSystem.Supersede`. It hides the
  placeholder at once, so the tile never shows two, and deletes it in `FrameUpdate`, outside state application (the
  contact reason above). Each feature decides what "arrived" means: the blanket cache taking the server's blanket, or a
  networked puddle starting up on the tile (`KsPredictedSpillSystem`).
- **Time out**: if the server's copy never comes (its shot hit something sooner), the placeholder deletes itself on expiry.

**Never change a networked entity's state from a kept copy.** The next state puts it back, the server's change arrives a
state later, and it flickers. A kept vapor spilling into a puddle the server already sent leaves the solution alone: it
adds a client-only `KsPredictedPuddleTopUpComponent`, which only changes the drawn volume, until a state shows the
server's spill has landed.

A kept bullet's hit is the same: `PredictedProjectileSystem.DoHit` works out what it would deal with
`DamageableSystem.PreviewDamage` (the steps of `ChangeDamage`, minus the write) for the impact and for going through,
and raises `ProjectileHitEvent` with `KsPreviewOnly`, which a handler that changes the target (stamina) returns on.
Dealt by the kept copy, damage was put back by the next state and dealt again by the server's hit: health flickered on
every shot. What the shooter sees at once goes two other ways: the amount is shown as **pending damage**
(`KsPendingDamageSystem`, `KsPendingDamageComponent`), kept beside the target's damage and never in it, so no state has
anything to put back. `DamageableSystem`'s getters (`GetTotalDamage`, `GetAllDamage`, `GetDamagePerGroup`) add it
themselves, so whatever asks for an entity's damage shows it with no code of its own; not while a state is being
applied, when what handles the server's damage (thresholds, mob states) must read the server's alone, or a mob is put
down on this client by a hit the server never makes and stays down. Each entry is one shot's
(`KsKeptPredictedSpawnComponent.ShotId`), dropped when the server's `KsPredictedSpawnImpactEvent` for that shot and
target arrives, which the engine hands over once the state of its tick (with the damage in it) is applied, and taken
off once if it never does. And what plays off a hit (blood) plays off `KsPreviewedDamageEvent`, since no
`DamageChangedEvent` is raised for it; what shows damage without asking every frame (damage visuals) hears
`KsPendingDamageChangedEvent`. The getters that feed healing (`GetPositiveDamage`, `TryGetDamageGreaterThan`) leave
pending damage out.

**A decal from a kept copy's hit is a kept decal**, not `TryAddDecal`: that writes into the networked
`DecalChunkComponent`, which the next state puts back, and nothing runs a kept copy's hit again to write it again, so
the stain showed for a tick, went, and came back with the server's. `SharedDecalSystem.KsAddKeptPredictedDecal` keeps it
on the grid in a client-only component the overlay also draws, until a server decal of the same id and colour turns up
close by, or it expires. **And what a kept copy's hit rolls is seeded by the shot, not the tick**
(`KsSharedPredictedSpawnSystem.GetShotSeed`, `BloodSpraySystem.BeginHit`): the kept copy and the server's copy hit on
different ticks, so a tick-seeded layout differs, and the stains changed when the server's arrived
(`ShootersBloodStainsStayPutAndMatchTheServers`). A projectile the client does not predict at all (anyone else's, the hidden server copy of its own) hits
nothing on it, and `KsRayCollisionSystem` does not look ahead of it. `KsGunHitPredictionTest`.

**The server takes a player's shot against where each mob was when their client fired**, by ping: bullets through
`LagCompProjectileSystem`'s rewound ghosts, hitscan through `KsHitscanLagCompensationSystem`, which corrects what the
ray crossed (`KsModifyHitscanRaycastResultsEvent`). A client predicts against what it sees, so without it a shot seen
to land on a moving target missed on the server. Pooled test channels report a ping of zero: pass the lag in
(`CompensateProjectile`, `Compensate`, `LagDurationOverride`), and keep the target moving, as a position is only
recorded on a move (`KsHitscanLagCompensationTest`).

**A field of a `fieldDeltas` component the server never `DirtyField`s is never sent**, and every state reset puts the
client's back to what it was sent last: `GunComponent.CurrentAngle` was written each shot and not dirtied, so the
client's spread restarted on every state (`SpreadBuildsTheSameOnClientAndServer`); the burst fields did the same (#415).
Whatever a predicted path writes, it dirties, and what it reads back next time (`LastFire`) is networked too.

`KsPredictedFoamShotTest.FiredShotLaysBlanketsThatLastUntilTheServersArrive` and
`KsPredictedSpillTest.SprayLaysPuddlesThatLastUntilTheServersArrive` fail with engine spawns or without the handover.

## NPC factions are not on the client

`NpcFactionMemberComponent.Factions` is never sent: sent, it would tell a client who is on a hidden side. A client has
each entity's prototype factions, which for humanoids are all alike, so predicted code that compares factions decides
wrongly, and the same way every time: a defender's own bullet went through every enemy the server's then hit. Send each
player the answer about themselves instead, in a per-player state (`ComponentGetState.Player`):
`NpcFactionMemberComponent.KsLocalPlayerShotsPass` for whose shots pass through whom (`KsFactionCollisionSystem`),
`KsAmmoPickupComponent.LocalPlayerMayTake` for a pickup. What the answer depends on changing (the player's side or
body) means sending it again. `KsFactionCollisionPredictionTest`. `SendOnlyToOwner`, `SessionSpecific` and
per-player states are compared in CONTRIBUTING §4, *Component state that differs per player*.

## Vapors and tile reactions

Vapors are shared code (`SharedVaporSystem`). A player's own spray (`SharedSpraySystem`) or solution gun shot is a kept
copy, and their client runs its tile walk. The server simulates every vapor; a client only its own kept copies.

- **What the client predicts**: a reagent's `tileReactions` reach the client through `KsTileReactionListSerializer`,
  which keeps only the types the client has. Predict a reaction by adding a client class with exactly the server class's
  name (`Content.Client/_KS14/Chemistry/TileReactions/`), or by moving a fork-owned one to Shared
  (`KsDeployFoamBlanketTileReaction`). Everything else (extinguishing, flammability, CO2 vaporising) stays server-only,
  with no YAML change. `KsFirefightingTest.TestClientReadsOnlyThePredictedTileReactions` checks every reagent.
- **Gate each client reaction on `KsPredictedTileReactionSystem.ShouldReact`**: only a kept vapor's walk opens the scope
  that makes it true. Other shared code runs tile reactions on the client too (a mop's absorbent, every re-predicted
  tick), and without the gate each re-prediction spawns another puddle.
  `KsPredictedSpillTest.ReactionsOutsideAKeptVaporDoNothingOnTheClient` checks it.
- **Order before physics.** A vapor reacts with the tile the last tick left it on, so the server never reacts with a tile
  it enters on the tick it despawns. A client that reacts after physics lays one tile too many, whose placeholder waits
  for a server copy that never comes and vanishes on expiry.

## Objects held on prototypes outlive the client's systems

A client makes its systems anew on every connection but keeps its prototypes, and the tile effects and reactions on
them. One injected once, at load, still holds the old systems after a reconnect: it runs and acts on nothing. Re-check on
every use: `KsTileEffectSystem.EnsureInitialized` re-injects an effect injected by an older `KsTileEffectSystem`
(`KsTileEffect.IsInitializedFor`). In tests it shows only after a pooled client pair reconnected earlier in the run, so
it fails in a batch and passes alone.
