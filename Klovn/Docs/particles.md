# Particles

A data-driven particle system for client-side effects: smoke, steam, embers, sparks, droplets, pollen. A
functional superset of BYOND's `/particles` as /tg/station uses it, in SS14 units, with the simulation on the
client only. Issue #446.

## How it is put together

| Piece | Where | Role |
| --- | --- | --- |
| `KsParticlesPrototype` (`type: ksParticles`) | `Content.Shared/_KS14/Particles/` | The effect: what spawns, how each particle moves and looks, where it draws. |
| `KsParticlesComponent` | shared, networked | "This entity emits that prototype", plus `emitting`, `color`, `offset`, `scale`. Nothing else crosses the network. |
| `KsSharedParticlesSystem` | shared | The spawn helpers and the setters. |
| `KsParticleEmitterComponent` | `Content.Client/_KS14/Particles/` | The client's simulation: the live particles, a struct array. Added to any entity carrying the shared component. |
| `KsParticlesSystem` (client) | client | Steps every emitter in `FrameUpdate`, at the prototype's fixed tick rate. |
| `KsParticlesOverlay` and its four subclasses | client | Draw each layer in `WorldSpaceEntities`, interpolated between steps. |

It works like audio entities. The entity, wherever it came from, is only a position and a few fields; the
particles are made and owned by each client that can see it. So all three ways of making one work the same:

| You want | Call | What happens |
| --- | --- | --- |
| An effect only this client sees | `SpawnLocal(prototype, coordinates)` | Client: a client-side entity. Server: nothing, returns null. |
| An effect from a player's own input, in shared code | `SpawnPredicted(prototype, coordinates, user)` | The user's client spawns and keeps its own copy at once; the server's copy is hidden on that client and shown to everyone else (`KsSharedPredictedSpawnSystem`). With no user, only the server spawns. |
| An effect everyone in range sees | `SpawnPvs(prototype, coordinates)` | Server: a networked entity. Client: nothing, returns null. |
| A thing that smokes | `- type: KsParticles` in its prototype, or `EnsureComp` on the server | The component rides on the thing itself. |

`SpawnPredicted` has the rule every user-mode spawn has: call it only on the user's own predicted path. From a
server-only path, pass no user.

The spawn helpers make the bare entity `KsParticles`; give it a `TimedDespawn` for a one-shot burst, or delete
it yourself. `SetEmitting(entity, false)` stops spawning and lets the live particles finish.

## Writing a prototype

```yaml
- type: ksParticles
  id: KsSmoke
  size: 3.125, 3.125        # the box about the emitter it is expected to fill, metres: for culling only
  count: 1000                   # most alive at once
  spawning: 40                  # per second; fractions accumulate
  lifespan: 1.5                 # seconds; a bare number is a constant
  fade: 1                       # seconds of fade-out at the end
  sprites:
  - sprite:
      sprite: /Textures/_KS14/Effects/Particles/smoke.rsi
      state: smoke_1
  - sprite:
      sprite: /Textures/_KS14/Effects/Particles/smoke.rsi
      state: smoke_3
    weight: 2                   # picked twice as often
  velocity: [ 0, 0.125 ]        # a list is a constant vector; z defaults to 0
  position: [ 0.1875, 0 ]
  drift:                        # a mapping is a generator, re-sampled every step
    kind: sphere
    a: 0
    b: 0.0625
    dist: normal
  friction: 0.89                # fraction of velocity lost per second
  gravity: 0, 2.97          # metres per second squared, y up
  grow: 0.5                     # scale per second; a scalar broadcasts to both axes
```

Prototypes inherit with `parent:`, so a family is written once and varied, as tg's subtypes are
(`KsSteamMild` is `KsSteam` with three fields changed). A child that restates `sprites` or `gradient` replaces
the parent's, as BYOND's `icon_state` and `gradient` do; a child that leaves them out inherits them.

### Generators

Every per-particle field takes a **generator**: BYOND's `generator()`. Three shapes are accepted:

```yaml
lifespan: 1.5                                   # constant
scale: [ 0.5, 0.5 ]                             # constant vector
color: "#FBAF4D"                                # constant colour (hex or a named colour)
spin: { kind: num, a: -200, b: 200, dist: normal }
velocity: { kind: circle, a: 1, b: 1, dist: square }   # a ring: speed 1 m/s, any direction
position: { kind: box, a: [ -0.4, -0.5 ], b: [ 0.4, 0.5 ] }
color: { kind: color, a: "#FBAF4D", b: "#FD481C" }     # anywhere between two colours
```

| `kind` | Gives |
| --- | --- |
| `num` | A number between `a` and `b`. On a vector field, the same number on every component. |
| `vector` | A point on the straight line from `a` to `b`: one fraction for every component. tg uses it for uniform scales. |
| `box` | A point in the box with corners `a` and `b`: each component drawn on its own. |
| `circle` | A point in the plane at distance `a`..`b` from the origin, in any direction. |
| `sphere` | The same in three dimensions; the z spreads the draw order. |
| `square`, `cube` | A point on the edge of a square, or the surface of a cube, of half-size `a`..`b`. |
| `color` | `vector` over two colours. |

| `dist` | The fraction between `a` and `b` is |
| --- | --- |
| `uniform` (default) | Flat. |
| `normal` | A bell curve about the middle, clamped. |
| `linear` | Rising linearly towards `b`. On a `circle`, points spread evenly over the area. |
| `square` | Rising with the square towards `b`. On a `sphere`, evenly through the volume. |

A missing `b` equals `a`. Operands follow the field: a scalar on a vector field means the same value on every
axis, a two-number list leaves z at 0.

### Fields

Emitter-wide, no generators (BYOND's "affect entire set" group):

| Field | Default | Meaning |
| --- | --- | --- |
| `count` | 100 | Most particles alive at once. |
| `spawning` | 10 | Particles per second. |
| `burst` | 0 | Spawned all at once when the emitter starts. |
| `emitDuration` | none | Seconds the emitter spawns for; then it stops and the live particles finish. |
| `size` | 3 × 3 | Culling box about the emitter, metres. Not a clip: particles outside it still draw. The live particles' real extent is tracked as well. |
| `bounds` | none | `Box2` about the emitter; particles outside it die (`bound1`/`bound2`). |
| `gravity` | 0 | Acceleration, m/s². |
| `gradient` | none | `colors`, optional `positions`, `loop`, `space: Rgb|Hsl|Hsv`. With one, `color` is an index into it. |
| `transform` | identity | `Matrix3x2` applied to the whole emitter. |
| `tickRate` | 20 | Simulation steps per second. |
| `space` | `Local` | `Local`: particles move with the emitter. `World`: they stay where emitted. |
| `followRotation` | false | Whether the emitter turns with its entity. |
| `layer` | `Effects` | `Floor`, `BelowMobs`, `Effects`, `Top`: where it sits among sprites. |
| `unshaded` | false | Ignore lighting (sparks, embers). |
| `animated` | true | Play an animated sprite from the particle's birth. |
| `interpolate` | true | Draw between steps. Off, particles move in steps, as BYOND's do. |
| `pixelSnap` | false | Nudge the emitter onto a whole screen pixel, as grids are. |

Per particle, all generators, sampled at spawn (BYOND's "applied on spawn"), except `drift`:

| Field | Default | Meaning |
| --- | --- | --- |
| `sprites` | none | Weighted `{ sprite, weight }` list. None draws a white pixel, tinted and scaled. |
| `lifespan` | 1 | Seconds. 0 or less lives until killed by `bounds` or shrinking away. |
| `fade`, `fadeIn` | 0 | Seconds of fade at the end, at the start. |
| `color` | white | Multiplied into the sprite, alpha included. With a `gradient`, an index into it. |
| `colorChange` | 0 | Gradient index per second. |
| `position` | 0 | Metres from the emitter. Z orders particles against each other. |
| `velocity` | 0 | m/s. |
| `scale` | 1 | Per axis. |
| `grow` | 0 | Scale per second. Shrinking to 0 on either axis kills the particle. |
| `rotation` | 0 | Degrees, clockwise positive as in BYOND. |
| `spin` | 0 | Degrees per second. |
| `friction` | 0 | Fraction of velocity lost per second, 0..1. |
| `drift` | 0 | m/s, sampled afresh every step: a random walk. |

The component adds a `color` multiplied over everything, an `offset` in metres, a `scale` that scales
positions, velocities and sizes together, and `emitting`.

## Porting a /tg/ particle

BYOND measures in pixels and deciseconds (its particles tick ten times a second whatever `world.tick_lag`
is). tg's `SECONDS` macro makes deciseconds, so `lifespan = 1.5 SECONDS` is 15 of them.

| BYOND | Here | Factor |
| --- | --- | --- |
| `width`, `height`, `position`, `bound1/2` (px) | `size`, `position`, `bounds` (m) | ÷ 32 |
| `velocity`, `drift` (px/ds) | m/s | × 10 ÷ 32 = × 0.3125 |
| `gravity` (px/ds²) | m/s² | × 100 ÷ 32 = × 3.125 |
| `lifespan`, `fade`, `fadein` (ds) | s | ÷ 10 |
| `spawning` (per ds) | per s | × 10 |
| `grow` (per ds), `spin` (°/ds), `color_change` (per ds) | per s | × 10 |
| `friction` *f* (per ds) | per s | 1 − (1 − *f*)¹⁰ |
| `rotation` (°), `scale`, `count`, `color`, `gradient` | the same | 1 |
| `icon` + `icon_state = list("a" = 1, "b" = 2)` | `sprites` with `weight` | |
| `generator(GEN_CIRCLE, 0, 16, NORMAL_RAND)` | `{ kind: circle, a: 0, b: 0.5, dist: normal }` | operands converted as above |
| `lifespan = null` | `lifespan: 0` | lives until killed |

Worked example, tg's `/particles/smoke`:

```dm
/particles/smoke
	icon = 'icons/effects/particles/smoke.dmi'
	icon_state = list("smoke_1" = 1, "smoke_2" = 1, "smoke_3" = 2)
	width = 100
	height = 100
	count = 1000
	spawning = 4
	lifespan = 1.5 SECONDS
	fade = 1 SECONDS
	velocity = list(0, 0.4, 0)
	position = list(6, 0, 0)
	drift = generator(GEN_SPHERE, 0, 2, NORMAL_RAND)
	friction = 0.2
	gravity = list(0, 0.95)
	grow = 0.05
```

becomes `KsSmoke` in `Resources/Prototypes/_KS14/Particles/smoke.yml` (the prototype shown above): 100 px →
3.125 m, 4/ds → 40/s, 0.4 px/ds → 0.125 m/s, 6 px → 0.1875 m, 2 px/ds → 0.0625 m/s, 0.2 → 0.89, 0.95 px/ds² →
2.97 m/s², 0.05/ds → 0.5/s. Convert the `.dmi` with `/dmi-to-rsi` into `Resources/Textures/_KS14/Effects/Particles/`.

tg's `particle_holder` is the `KsParticles` entity attached to a parent; its `atom.particles` is the
component on the atom itself; its `shared_particle_holder` has no equivalent yet.

### Where it deliberately differs from BYOND

- **No clip plane.** `size` only decides whether the emitter is on screen; particles outside it are still
  drawn. Use `bounds` to kill them instead.
- **A particle that shrinks to zero dies.** BYOND lets the scale go negative and mirrors the sprite.
- **Rotation follows the engine.** `rotation` and `spin` are still clockwise-positive degrees as written, and
  converted on the way in.
- **Interpolated by default.** Set `interpolate: false` for BYOND's stepped look.
- **Additions:** `burst`, `emitDuration`, `space: World`, `followRotation`, `layer`, `unshaded`, `animated`,
  `pixelSnap`, prototype inheritance, and the component's `color`, `offset`, `scale`.

## Trying it in game

`ksparticles KsSmoke` attaches an emitter to you; `ksparticles KsEmbers 1234` to net entity 1234;
`ksparticles KsSparks -t 3` for three seconds. `ksparticles_clear` deletes every bare emitter entity. Both need
the Debug admin flag.

Client cvars: `klovn.particles.enabled`, `klovn.particles.density` (0..1, scales `count` and `spawning`) and
`klovn.particles.max_alive` (a budget across every emitter; spawning stops at it).

## Performance

Particles are structs in one array per emitter, dense and swap-removed, grown by doubling up to `count`.
Generators are sampled through a switch, never a delegate. The simulation runs in fixed steps, at most five per
frame, and only for emitters with a prototype that are not hidden copies; drawing interpolates between steps.
Each frame the client system sorts live emitters into one list per layer; each overlay walks only its list,
culls each emitter against the pass's view with a box the simulation keeps up to date, and draws its particles
with `DrawTextureRect`, which Clyde batches while the atlas and shader stay the same. The shader changes at
most once per layer per pass. Z-sorting happens only for prototypes that can produce a z. Sprite states and
frame timings are resolved once per prototype and shared.

## Testing

`Content.IntegrationTests/Tests/_KS14/Particles/`. The headless client steps emitters but draws nothing, so the
tests check the state the simulation leaves: the generator and serializer, counts, motion against the closed
form, lifespans, bursts, emitting, spaces, and that the three spawn modes put an emitting copy exactly where
they should. What it looks like is unverified until seen in game.
