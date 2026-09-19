# Chemical fires and tile fire arbitration

This document explains chemfires - tile-locked, YAML-configurable fire entities - and the arbiter that
decides when anything standing on a tile is told that the tile is on fire or has stopped burning.

## Background

Upstream SS14 has exactly one kind of tile fire: an atmospherics *hotspot*, which is tile data owned by
`GridAtmosphereComponent` rather than an entity. Hotspots only exist where the gas on the tile is both fuel
and oxidiser, they are coloured and shaped by `GasTileFireOverlay` with no per-effect configuration, and
nothing else can produce one.

Chemfires add a second, independent kind of tile fire: an anchored entity that sits on a tile, burns for a
set duration, heats and optionally eats the air there, and renders as a two-layer colour-modulated flame. A
chemical reaction, a grenade, a thermite charge or an admin command can put one anywhere - including on a
tile with no burnable atmosphere at all, where a hotspot could never exist.

Two kinds of fire on one tile creates a coordination problem, which is what `KsTileFireSystem` exists for.

## Chemfires

### Placing one

Chemfires are hidden from the spawn menu (`EntityCategory("ChemicalFire")`) and are not meant to be spawned
with `spawn`. The supported entry point is `SharedChemicalFireSystem.SpawnChemicalFire`, which takes a
prototype id plus a grid and tile, `EntityCoordinates`, or a `TileRef`, and an optional duration override:

```csharp
_chemicalFireSystem.SpawnChemicalFire("ChemicalFire", coordinates, TimeSpan.FromSeconds(3));
```

The call is an *ensure*, not a spawn - hence the `ensurechemfire <prototype> [seconds]` admin command rather
than `spawnchemfire`. What it does depends on what already holds the tile, keyed by **connection key**
(`ChemicalFireComponent.ConnectionKey`, defaulting to the prototype id):

| Tile state | Result |
|---|---|
| No chemfire with that key | A new one is spawned and anchored. |
| The same prototype | The existing one is retuned from the prototype and its lifetime restarts. No second entity. |
| A different prototype sharing the key | The incumbent is deleted and replaced. |
| Chemfires with other keys | Ignored - they stack freely on the same tile. |

Connection keys therefore do double duty: they decide what may share a tile, and which chemfires smooth into
each other visually.

`SetDuration` restarts a live chemfire's lifetime, and `ExtinguishChemicalFire` ends one early.

### The grid cache

Every live chemfire registers itself into `ChemicalFireGridComponent` on the grid it sits on - a
`Dictionary<Vector2i, TileChemicalFireData>` keyed by tile, then by connection key. Deduplication, lateral
smoothing and the "is anything burning this tile" question are all O(1) dictionary lookups against it, with
no entity lookup in any hot path. It is maintained purely off component startup/shutdown and dirtied only on
add/remove, never per heat tick.

The cache is the reason a chemfire caches `LocalGridUid`/`LocalTile` on itself: deleting an entity detaches
it to nullspace *before* shutting its components down, so the transform can no longer answer "what tile was
I on?" by the time the fire needs to deregister and announce that it has gone out.

### Burning

Every `HeatInterval` (0.5s by default) a live chemfire raises `ChemicalFireHeatTileEvent` on itself, carrying
the grid, tile and elapsed seconds. Everything a chemfire does to its tile hangs off that one event:

- **Ignition** - `AtmosphereSystem.HotspotExpose` at the fire's `Temperature`/`ExposedVolume`. This already
  no-ops unless the tile mixture is both oxidiser and fuel, which is exactly "ignite whatever is burnable
  here", so no gas checks of our own are needed.
- **Air heating** - `HeatPower` joules per second poured into the tile mixture, capped so the air is never
  pushed past the fire's own `Temperature`, and skipped for immutable or near-zero-heat-capacity mixtures.
  This is what lets a chemfire warm a room it has nothing to set alight in.
- **Gas consumption** - `ChemicalFireGasConsumerComponent` eats moles per second off the tile and may put
  back other gases as *ratios* of what it actually consumed, so mole count is conserved. With
  `extinguishWhenDepleted` (the default) the fire dies once its fuel gas is gone.

Setting things on the tile alight is not part of that list: it happens through the tile fire events below.

### Extinguishers

A fire extinguisher sprays water; water carries `ExtinguishTileReaction`; that reaction gates on
`AtmosphereSystem.IsHotspotActive` and then calls `HotspotExtinguish`. Chemfires answer
`IsHotspotActiveMethodEvent` so that a tile holding one reads as burning even with nothing flammable in the
air - otherwise the reaction would bail out before it could put anything out. `HotspotExtinguish` then
reaches the chemfires through the arbiter (below), and `Extinguishable: false` opts a chemfire out of being
doused at all, for thermite and similar.

Because our own handler makes `IsHotspotActive` answer for chemfires too, code that needs to know whether
there is an actual *gas* fire on a tile must use `AtmosphereSystem.HasGasHotspot` instead.

### Rendering

The art is a 32x48 greyscale RSI split into an `under` and an `over` half, both 5-frame, so flames can render
both behind and in front of whatever is standing in them:

- The `under` half is the entity's own sprite layer (`drawdepth: Objects`, `snapCardinals: true`, offset a
  quarter tile up), modulated with `ChemicalFireComponent.Color`, unshaded.
- The `over` half is drawn by `ChemicalFireOverlay` at `DrawDepth.Effects`, unshaded, reading its animation
  frame straight off the `under` layer so the two halves cannot drift apart.
- `TileEmissionComponent` is added in shared, coloured to match, for the glow on the tile.

Sprite variation is `HashCode.Combine(gridNetId, tile.X, tile.Y) % SpriteVariations`, so client and server
agree without networking it. Lateral smoothing picks `-west`/`-east`/`-full` states by looking up the
neighbouring tiles in the grid cache and comparing connection keys.

Smoothing follows the **rendered** orientation, not the entity's true rotation. Because the sprite is
`snapCardinals`, its on-screen orientation is
`spriteRotation - (spriteRotation + localEyeRotation).Reduced().FlipPositive().RoundToCardinalAngle()`, where
`localEyeRotation` is the eye rotation plus the grid's world rotation. `ChemicalFireVisualsSystem` replicates
that maths to work out which tile offset "east" currently points at, and re-dirties every fire when the eye
rotates. Without this, rotating the eye leaves fires smoothing into neighbours that are no longer beside them
on screen.

Everything is predicted: spawning goes through `KsSharedPredictedSpawnSystem.PredictedSpawnAttachedTo`,
removal through `PredictedQueueDel`, `EndTime` is networked and `[AutoPausedField]`, and the derived visual
state is recomputed identically on both sides.

## Tile fire arbitration

`TileFireEvent` and `TileExtinguishEvent` are how anything on a tile learns that it is in a fire -
`FlammableSystem` turns them into fire stacks, atmos monitors turn them into fire alarms. Upstream raised
them inline from the hotspot code. With two kinds of fire able to share a tile, that produces two problems in
opposite directions, so **`KsTileFireSystem` is now the only place either event is raised**, and it applies
one rule each:

- **Fires are announced by whoever is burning.** A hotspot and a chemfire on one tile both act on what is
  standing there; neither goes quiet because of the other. (`FlammableSystem` keeps only the hottest event
  per tick before converting it to a fire-stack target, so overlapping fires do not stack damage - the hotter
  one wins that tick.)
- **An extinguishing is announced only once nothing else is burning the tile.** A chemfire that lit a gas
  fire and then burned out would otherwise report the tile as out while it was still alight, and the hotspot
  would report it out a second time later. One fire ending must not read as two.

The second rule needs to know what else is burning a tile, and that is answered by the sources themselves:
`KsGetTileFireSourcesEvent` is raised on the grid, carrying the tile and one source to ignore, and every kind
of fire reports for itself. The hotspot's answer lives in the arbiter because a hotspot is tile data with
nothing of its own to hang a subscription off - the grid entity stands in for it. Chemfires answer from the
grid cache.

A source calls `RaiseTileExtinguish` **while it is still registered**, passing itself as the ignored source,
so the question is exactly "is anything *other than me* still burning this?".

### Putting a tile out

`HotspotExtinguish` no longer raises `TileExtinguishEvent` itself; it calls `KsTileFireSystem.ExtinguishTile`,
which raises `KsExtinguishTileFireSourcesEvent` on the grid - asking every source to stop, which is how an
extinguisher reaches chemfires - and then tries to announce the tile as out. Sources are *asked to stop*
rather than told they have stopped, and each announces its own end. A chemfire only dies at the end of the
tick, so it still counts as burning when the hotspot asks, and the single announcement comes from the
chemfire's own shutdown a moment later.

### Adding a new kind of tile fire

Answer `KsGetTileFireSourcesEvent` while burning, call `RaiseTileFire` when the fire starts and
`RaiseTileExtinguish` when it ends, and handle `KsExtinguishTileFireSourcesEvent` if it should be dousable.
Nothing existing needs to learn about it.

### Announcement cadence

A chemfire announces its tile as burning when it starts, and again on every `HeatInterval` for as long as it
lives - the same shape as a hotspot, which announces once per atmos cycle. Both halves matter: the startup
announcement sets the tile alight immediately instead of after a whole interval, and the repeats mean anything
that *walks onto* a burning tile catches fire, and that a chemfire sharing a tile with a cooler fire keeps
setting the pace rather than being outvoted after the first tick.

Repeating is cheap and safe because every consumer treats the event as a level rather than an increment:
`FlammableSystem` collapses a tick's events down to the hottest one and moves fire stacks *towards* a target
derived from it, and `AtmosMonitoringSystem` just sets a flag. The per-announcement cost is one unenlarged
broadphase lookup of the tile.

## Key files

| File | Role |
|---|---|
| `Content.Shared/_KS14/Atmos/ChemicalFire/ChemicalFireComponent.cs` | All chemfire tuning: duration, colour, temperature, heat power, connection key, sprite states. |
| `Content.Shared/_KS14/Atmos/ChemicalFire/ChemicalFireGridComponent.cs` | Per-grid, per-tile cache of live chemfires, keyed by connection key. |
| `Content.Shared/_KS14/Atmos/ChemicalFire/SharedChemicalFireSystem.cs` | Lifetime, the `SpawnChemicalFire` ensure-semantics API, the grid cache, and the heat tick. |
| `Content.Shared/_KS14/Atmos/ChemicalFire/SharedChemicalFireSystem.Networking.cs` | Hand-written grid component state - the wire carries `NetEntity`, both sides hold `Entity<ChemicalFireComponent>`. |
| `Content.Server/_KS14/Atmos/ChemicalFire/ChemicalFireSystem.cs` | Server half: hotspot exposure, air heating, the tile fire source answers, dousing. |
| `Content.Server/_KS14/Atmos/ChemicalFire/ChemicalFireGasConsumerSystem.cs` | Eats (and optionally replaces) gas on the tile, off the same heat tick. |
| `Content.Server/_KS14/Atmos/ChemicalFire/EnsureChemicalFireCommand.cs` | `ensurechemfire <prototype> [seconds]`, the only hand-driven way in. |
| `Content.Server/_KS14/Atmos/TileFire/KsTileFireSystem.cs` | The arbiter: the only place `TileFireEvent`/`TileExtinguishEvent` are raised. |
| `Content.Server/_KS14/Atmos/TileFire/KsTileFireEvents.cs` | `KsGetTileFireSourcesEvent` and `KsExtinguishTileFireSourcesEvent`. |
| `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Klovn.TileFire.cs` | Marked upstream-namespace partial: `HasGasHotspot`, plus the arbiter dependency. |
| `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Hotspot.cs` | Marked upstream edit: `PerformHotspotExposure` announces through the arbiter. |
| `Content.Server/Atmos/EntitySystems/AtmosphereSystem.GridAtmosphere.cs` | Marked upstream edit: `GridHotspotExtinguish` calls `ExtinguishTile`. |
| `Content.Server/Atmos/EntitySystems/AtmosphereSystem.API.cs` | Marked upstream edit: `IsHotspotActiveMethodEvent` made public so chemfires can answer it. |
| `Content.Client/_KS14/Atmos/ChemicalFire/ChemicalFireVisualsSystem.cs` | Sprite variation, rotation-aware lateral smoothing, dirty-queue resmoothing. |
| `Content.Client/_KS14/Atmos/ChemicalFire/ChemicalFireOverlay.cs` | Draws the `over` flame half above the effects layer, frame-synced to the entity sprite. |
| `Resources/Prototypes/_KS14/Entities/Effects/ChemicalFire/` | `BaseChemicalFire` plus the shipped fires (plain, plasma-fed, frost, thermite). |
| `Content.IntegrationTests/Tests/_KS14/ChemicalFire/ChemicalFireTest.cs` | Duration, stacking, ignition, extinguishing and arbitration, on test-injected prototypes. |
| `Content.IntegrationTests/Tests/_KS14/ChemicalFire/ChemicalFireEventListenerSystem.cs` | Counts tile fire events per entity; nothing in content otherwise observes `TileExtinguishEvent`. |

## Tests

`ChemicalFireTest` runs entirely on `[TestPrototypes]` fires carrying `heatPower: 0`, no gas consumer and
none of the shipped prototypes' ignition effects, so the tile fire events are the only thing that could set
the test flammable alight. It covers prototype duration and the duration override, refresh-in-place, stacking
by differing key and replacement on a shared key, ignition, extinguishers (including the `Extinguishable`
opt-out), the expiry cascade guard, and both arbitration rules - a hotspot and chemfire on one tile both
announcing, and a chemfire expiring over a live hotspot staying quiet.

Two things worth knowing before adding to it: `[TestPrototypes]` blocks are discovered assembly-wide, so ids
must be globally unique, and `pair.CreateTestMap()` gives a single tile open to space whose air equalises into
vacuum within a few ticks - anything needing a real hotspot must call `SetMapAtmosphere` with a burnable
mixture first.
