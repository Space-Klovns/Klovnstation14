---
name: dreammaker
description: How to read DreamMaker (DM / BYOND) code from tgstation and other SS13 codebases, and turn it into SS14 behaviour - type paths and overrides, `. = ..()`, Initialize and `new` argument order, defines, 1-indexed lists, deciseconds, pixel offsets, matrix and animate() semantics, timers and callbacks, and a method for porting a DM effect with a worked example (pod_landingzone). Use when porting an SS13 effect, item, system or animation, when a DM snippet is unclear, or when the DM documentation is not available.
---

# Reading DreamMaker

The official BYOND reference is offline, so this is the working knowledge needed to read tgstation source without
it. DM looks like C with Python-style indentation, and most of the surprises are in a few places listed below.
Sprites: `/dmi-to-rsi` converts the art. The SS14 side of animation is in the `sprites` skill.

**What is verified here and what is not.** Everything about this repo's side was read from the code. The DM
semantics below are standard BYOND behaviour, written from knowledge rather than from the (dead) reference. Where a
port depends on one of them, say so in your report, and ask the user if the answer changes what you build. A port is
only "faithful" once someone has seen it running next to the original. Never claim 1:1 from reading alone.

## Finding your way

- Source is under `code/`. Shared constants are in `code/__DEFINES/`. Search for `#define NAME` to resolve any
  ALL_CAPS word you don't recognise. Do this for every one that affects a number: `POD_TRANSIT` is just an index.
- `grep -rn "/proc/name"` finds a proc's definition, and `grep -rn "/type/name("` finds an override. A proc is often
  defined, overridden in several subtypes, and extended in another file. Read the parent chain, not just the file
  you landed in.
- `code/modules/<feature>/` holds most features. `icons/` holds `.dmi` art, referenced as `'icons/obj/x.dmi'`.
- Comments are `//` and `/* */`. A line ending in `\` continues. Docs comments start `///`.

## Types and procs

```dm
/obj/effect/pod_landingzone                       // defines a type. Path = inheritance: /obj/effect/ is its parent
	name = "Landing Zone Indicator"               // a var override, indented inside the type, no `var/`
	var/obj/structure/closet/supplypod/pod        // a new var of that type (the type is only a hint)
	var/list/smoke_effects = new /list(13)        // a list of 13 null slots

/obj/effect/pod_landingzone/proc/begin_launch()   // a proc on that type
/obj/effect/pod_landingzone/Initialize(mapload, podParam)   // overriding a built-in proc: no `proc/` word
	. = ..()                                      // call parent, store its result in `.`
```

- **Indentation is the block structure**, and the source uses tabs.
- **`.` is the return value variable.** `. = ..()` calls the parent and keeps its result. A proc ends by returning
  whatever `.` holds unless it uses `return x`. `Initialize` returns a hint (`INITIALIZE_HINT_QDEL` etc.), so
  `return INITIALIZE_HINT_QDEL` there means "delete me after init".
- **`..()` is the parent proc.** It runs the next definition up the type path. A subtype that omits it silently drops
  everything the parent did.
- **`src`** is the object the proc runs on (`this`). Its vars can be used bare (`pod` means `src.pod`). **`usr`** is
  whoever triggered the call; avoid reading meaning into it.
- **`initial(x)`** is the default value from the type definition, not the current value.
- **`istype(a, /path)`** is an instance check including subtypes. **`ispath(a, /path)`** is the same for a type value
  itself (this is how a pod "style" is compared: `ispath(pod.style, /datum/pod_style/invisible)`).
- A type nobody ever instantiates is usually a base class. `/datum` is the root of non-world things, `/atom` has a
  position and appearance, `/atom/movable` can move, `/obj` is an item or structure, `/mob` a creature,
  `/turf` a floor tile, `/area` a region. `/obj/effect` is visual or utility only.

## Creating things: `new`

`new /type(loc, a, b)`: the first argument is the location. For an `/atom`, the rest, preceded by `mapload`
(`FALSE` at runtime), go to `Initialize(mapload, a, b)`. So `new /obj/effect/pod_landingzone(destination, src)`
calls `Initialize(mapload, podParam = src)` with the effect placed on `destination`.
`helper = new (drop_location(), pod)` infers the type from the variable's declared type. If the argument is a
`/path`, the code may also instantiate it itself (`podParam = new podParam()`). Read for `ispath` checks.

## Lists, loops, and values

- **Lists are 1-indexed.** `list[1]` is the first element. `for (var/i in 1 to length(x))` is inclusive of both ends.
  Off-by-one errors in ports come from here.
- `for (var/mob/living/M in pod)` loops over `pod`'s contents, **filtered by type**. The declared type in the loop
  is a filter, not a cast. Loops over `get_turf(src)` iterate that tile's contents.
- `TRUE`/`FALSE` are `1`/`0`. `null` and `0` are false. Flags are OR-ed bitfields (`a |= (X | Y)`).
- `x ? a : b` exists. `x ||= y` assigns if falsy. `A in list` tests membership. `prob(30)` is a 30% roll,
  `rand(a, b)` is an inclusive integer.
- `/list` and `/datum` values are references. `new /list(13)` makes 13 empty slots.
- `get_turf(x)` is the tile an object is on. `drop_location()` is where it would drop (its tile, or its container's).
  `forceMove(loc)` moves without checks. `qdel(x)` deletes, `QDEL_NULL(var)` deletes and nulls, `QDEL_IN(x, t)`
  deletes after `t`.

## Time

- **All durations are in deciseconds** (1/10 s) unless the code says otherwise. `time = 20` is 2 seconds. `SECONDS`
  and `MINUTES` defines convert. A DM number you copy into SS14 needs dividing by 10 unless it was already
  multiplied by `SECONDS`.
- `addtimer(CALLBACK(src, PROC_REF(proc_name), arg), delay)` runs the proc after `delay`. `PROC_REF` and
  `TYPE_PROC_REF` are just safe proc-name wrappers. `TIMER_CLIENT_TIME` means the delay is measured in client time,
  which only matters for exact sync, and is not something to reproduce.
- `sleep(t)` and `spawn(t)` pause a proc or run a block later. Procs that `sleep` are asynchronous, so another call
  can interleave.
- A chain of `addtimer`s in one proc is a timeline. Write it out as a table of time, event before porting.

## Visuals

- **Pixels.** A tile is 32 px. `pixel_x` and `pixel_y` offset the drawing without moving the object. In tgstation,
  `pixel_z` is a vertical (height) offset layered on top of that. Divide by 32 for tiles.
- **`transform`** is a 2x3 matrix. `matrix()` is the identity. `matrix() * 1.5` scales. `matrix().Turn(deg)`
  rotates **clockwise** by degrees. `matrix().Translate(x, y)` shifts. Assigning `transform` changes the look
  immediately. Reading it back gives the matrix.
- **`alpha`** is 0 to 255 opacity. `alpha = 0` is invisible but the object still exists and runs its code.
- **`layer` and `plane`** set draw order: plane first, then layer within it. The `*_LAYER` and `*_PLANE` constants
  are defines. Look up the number, then map it to an SS14 `DrawDepth`.
- **`icon` and `icon_state`** pick the `.dmi` file and the named state in it. The `.dmi` stores per-frame delays in
  deciseconds, and the `.rsi` stores them in seconds.
- **`light_range` / `light_power` / `light_color`** are the light, in tiles.
- **`add_filter("name", priority, gauss_blur_filter(size = 4))`** adds a shader-like filter. SS14 has no direct
  equivalent. Check how the existing trail does blur (`KsTrail`).

### `animate()`

```dm
animate(src, transform = matrix()*0.01, time = 15)                      // from the current value to the target
animate(pod, pixel_z = -1, pixel_x = 3, time = 10, easing = LINEAR_EASING)
animate(smoke, alpha = 0, time = 20, flags = ANIMATION_PARALLEL)
```

- The first `animate(obj, ...)` starts an animation **from the object's current value** to the given one over `time`.
  Set the start value on the line before it (`transform = matrix() * 1.5` then `animate(... 0.01 ...)`).
- A further `animate(...)` with no object is chained: it runs after the previous step. With `ANIMATION_PARALLEL`
  it runs alongside it. A new `animate(obj, ...)` on an object that is already animating, without parallel,
  **replaces** the running animation.
- Default interpolation is linear. `easing =` changes it (`CUBIC_EASING|EASE_OUT`, `SINE_EASING`, ...). When an
  easing is not given, assume linear and say so.
- `animate` can target a filter (`animate(obj.get_filter("blur"), size = 6, ...)`).
- The animation is purely client-side drawing. The object's real vars (like `transform`) are already at the target.

### Angles and trig

`sin()` and `cos()` take **degrees**. A tgstation helper like `get_pixel_angle` is a custom arctangent. Open it,
don't guess its axis convention. Rotation through `Turn()` is clockwise, whereas SS14's `Angle` is
counter-clockwise for positive values in world space, so a sign flip is the usual correction.
`pixel_x = cos(a)*d`, `pixel_z = sin(a)*d` places a point on a circle of radius `d` pixels around the object.

## Porting method

1. **Read the whole chain.** The type, every proc it calls, its parents' relevant overrides, and the code that
   creates it (`new /obj/effect/pod_landingzone(...)` is in another file).
2. **Resolve every define and every helper proc** that feeds a number or a decision.
3. **Write the timeline**: at t = 0 what appears, what each timer does, when it is deleted. Convert to seconds.
4. **Separate what is visual from what is logic.** Visual (animate, filters, light) goes on the client. Logic
   (stun, moving mobs, sounds, spawning) goes on the server. DM mixes them in one proc.
5. **Map each piece to what the codebase already has** before writing anything. Search for the same concept: here,
   supply pods already had a glow component, launch and landed events, and a trail. The port hooked into them.
6. **Port, then list every deliberate difference** to the user: dropped features, changed units or directions,
   things with no SS14 equivalent. "Faithful" is the user's judgement after watching both.
7. **Read the original once more against your result**, line by line, ticking each line as ported, replaced or
   dropped.

## DM to SS14 table

| DM | SS14 here |
| --- | --- |
| `/obj/effect` with an icon | entity with `Sprite`, `HideSpawnMenu` category |
| `Initialize` | `MapInit` / `ComponentStartup` / a spawn call from the owning system |
| `addtimer`, `QDEL_IN` | a networked end time plus a system check, or `TimedDespawn` |
| `animate(transform = ...)` | client `FrameUpdate` writing sprite layers (`sprites` skill) |
| `animate(alpha = ...)` | `KsSpriteFadeOut` or an animation track on `SpriteComponent.Color` |
| `pixel_x/y` | `Sprite.Offset` (divide by 32), or entity position |
| `light_range = N` | `PointLight` with `radius: N` |
| `layer` / `plane` | `drawdepth` |
| `vis_contents` | a child entity parented to the thing it follows |
| `qdel(src)` | `QueueDel` (or a despawn component) |
| `playsound(...)` | `AudioSystem.PlayPvs` |
| `var/list` in a type | a `[DataField]` list on a component |
| a var on a subtype | a component field in the prototype |
| a boolean var that turns off a feature (`effectStealth`) | omit the component |

## Worked example: `pod_landingzone`

Source: `tgstation/code/modules/cargo/supplypod.dm`, lines 661 to 790.

| DM | Meaning | SS14 |
| --- | --- | --- |
| `icon_state = "LZ"`, `alpha = 0`, then `alpha = 255` if `!pod.effectStealth` | A crosshair shown only for non-stealth pods | A `Zone` layer. No component on the pod means stealth. |
| `animate(src, transform = matrix().Turn(90), time = arrival_time)` | Crosshair turns a clockwise quarter turn over the whole countdown | `ZoneTurn = -90` degrees, linear in `FrameUpdate` |
| `helper = new (drop_location(), pod)` with `LZ_Slider`, `transform = matrix() * 1.5` | A second object, a ring, starts at 1.5 times scale | A `Slider` layer on the same entity, `scale: 1.5, 1.5` |
| `animate(src, transform = matrix()*0.01, time = arrival_time)` | The ring shrinks to nearly nothing | `SliderEndScale = 0.01`, linear |
| `arrival_time = delays[POD_TRANSIT] + delays[POD_FALLING]` | Total countdown. Look up both defines to see their units. | `Duration = FallDuration` (no transit here) |
| `light_range = 2` | Light of two tiles | `PointLight radius: 2` |
| `layer = PROJECTILE_HIT_THRESHHOLD_LAYER` | Above floor clutter, below objects | `DrawDepth.HighFloorObjects` (an approximation) |
| `QDEL_NULL(helper)` and `qdel(src)` in `end_launch()` | Both removed when the pod lands | Indicator deleted on `SupplyPodLandedEvent` |
| `Stun`, `reset_perspective`, falling sound | Gameplay around the zone | Not ported. Listed to the user. |

What was *not* verified in that port: the draw layer, the rotation direction, and whether the 1.5-to-0.01 shrink is
visually identical, since none of it was seen running. The unit tests check that the values change over time. They
do not check that they look right.

## Questions worth asking the user

- "What does this define expand to?" when it is not in the checkout.
- "Is this timing in deciseconds, and is the effect meant to be visible to everyone or only the target?"
- "Do you want the gameplay part (stuns, camera) ported, or only the visual?"
- "Should the effect run on pods that don't opt in?"
