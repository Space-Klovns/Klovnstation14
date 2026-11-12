---
name: physics-queries
description: Traps in physics contacts, collision layers and spatial queries in this repo - sleeping bodies keep their contacts after a teleport, items never touch puddles (layer and mask mismatch, KsPuddleTouchSystem and the flying-mob trap), IntersectRay's returnOnFirstHit is not the nearest hit, occluder ray queries along a tile edge report walls that aren't there (KsSegmentBounds), and reading a tile's air (grid, then map; GetGridTileMixture, IsMapSpace). Use when tracking collisions or contacts, making items or mobs touch puddles or other floor things, casting rays for sight lines, beams or line of fire, checking occlusion, reading a tile's gas, or when a collision event never fires, a ray overshoots or flickers, everything in line looks blocked, or a planet tile reads as vacuum.
---

# Physics queries, contacts and tile air

Each of these produced code that compiled, ran, and was wrong with nothing to say so.

## Sleeping bodies get no contact updates

Physics updates contacts only for awake bodies. Move a sleeping entity with `SetCoordinates` and it keeps every contact
it had: no `EndCollideEvent` for what it left, no `StartCollideEvent` for what it's on now, until something wakes it.
Walking wakes a body, so in game this rarely shows; in a test that teleports an entity to check collision tracking it
looks exactly like the tracking being broken. Wake it after moving it:

```csharp
transformSystem.SetCoordinates(mobUid, outsideCoordinates);
physicsSystem.WakeBody(mobUid);
```

## An item lying on a puddle isn't touching it

An item's fixture has mask `ItemMask` and no layer; a puddle's `slipFixture` is on `SlipLayer` with mask `ItemMask`.
Neither mask matches the other's layer, so a dropped item never makes a contact with the puddle under it. A mob does
touch it, which makes a test on a mob look like proof that items work.

- Put the fixture on the side the feature is about: for a puddle feature, a non-hard fixture on the puddle whose layer
  matches what items collide with, not one on every item. Keep it apart from `slipFixture` and filter collide handlers
  on `args.OurFixtureId`, so each toucher counts once.
- Items have no layer, so the fixture can't pick them out by mask, and whatever layer it uses is hit by anything masking
  it. `FlyingMobMask` is exactly `ItemMask`, and step triggers and speed modifiers check only that the *other* fixture
  is hard, not which puddle fixture it touched. Unfiltered, bats and carp start slipping and getting slowed by sticky
  puddles. `KsPuddleTouchSystem` cancels `PreventCollideEvent` between the touch fixture and a mob's hard fixture; a mob
  still touches the puddle through its non-hard `flammable` fixture, which is all a hot-thing check needs.
- Items do reach the step trigger, but `SlipperySystem.CanSlip` rejects anything that can't be stunned. Non-hard
  fixtures don't block a tile (`TurfSystem.IsTileBlocked`), and neither ray queries nor projectile hits count them.

## `IntersectRay` with `returnOnFirstHit: true` is not the nearest hit

It stops at whichever fixture the broadphase tree visits first (`SharedPhysicsSystem.Queries.cs`,
`IntersectRayWithPredicate`): often, not always, the closest. A beam, sight line or view length then ends beyond a nearer
obstacle only sometimes, which reads as flicker or a rare overshoot. Results are sorted by distance only at the end, so
use `false` and take the minimum:

```csharp
foreach (var result in _physicsSystem.IntersectRay(mapId, ray, length, ignoredEnt: uid, returnOnFirstHit: false))
    length = MathF.Min(length, result.Distance);
```

Distances are to the fixture proxy's AABB, so a non-box fixture is hit slightly early. Beam-specific caching is in the
overlays skill (*Raycasts for beams*).

## Occluder ray queries along a tile edge report walls that aren't there

The occluder tree's ray queries (`OccluderSystem.IntersectRay` and everything built on it) test boxes with
`Ray.Intersects`. For an axis-parallel ray lying exactly on a box's edge, as one along a tile edge does, that returns a
NaN or zero distance for every box on the line, including those behind the start and past the end. NaN never compares
greater than the maximum length, so every wall in line counts as in the way. Only exact edges do it, so a
random-position test almost never lands on one.

Throw out hits the segment can't reach, with `KsSegmentBounds.Touches` against the occluder's world bounds. It only ever
removes impossible hits, so normal rays are unchanged:

```csharp
// as ExamineSystemShared.Klovn.Occlusion.cs does
if (!KsSegmentBounds.Touches(start, end, worldBounds.Enlarged(0.01f)))
    continue;
```

`ExamineSystemShared.InRangeUnOccluded`, `NpcLineOfSightSystem` and `KsLosSensorSystem` do this. The engine's
`OccluderSystem.InRangeUnoccluded` doesn't, so use the examine one.

## A tile's air: ask the grid, then the map

`AtmosphereSystem.GetTileMixture(grid, map, tile, excite)` gives the grid's air on a tile it simulates, and on any other
tile the air of the map you pass, or space if you pass `null`. A tile with no floor, or one atmos never simulated, then
reads as vacuum on a planet. Use the KS14 wrappers, which find the grid's map themselves:

- `AtmosphereSystem.GetGridTileMixture(gridUid, tile, excite)` and `IsGridTileSpace(gridUid, tile)` on the server;
- `SharedAtmosphereSystem.GetMapMixture(mapUid)` and `IsMapSpace(mapUid)` on either side, for what is off every grid.
  The client simulates no gas, so this is all it knows.

A map's air is immutable: code that changes air keeps checking `GasMixture.Immutable`, and code that only reads it (a
boiling point, a fire's temperature) gets the planet's air instead of vacuum. Don't test for `MapAtmosphereComponent` to
decide whether somewhere is space, since a map can have one and still be space; ask `IsMapSpace`.
