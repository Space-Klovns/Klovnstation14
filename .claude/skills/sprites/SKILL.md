---
name: sprites
description: How sprites and sprite animation work in this repo - SpriteComponent layers and map keys (enum keys in YAML), what AnimationPlayer can and cannot animate, driving layer scale, rotation and colour from FrameUpdate, smooth client timing with CurTime plus TickRemainder, KsSpriteFadeOut, GenericVisualizer, draw depth, and testing layer state headlessly. Use when animating a sprite or one of its layers, scaling or rotating a layer over time, making an effect fade, spin or shrink, syncing a client visual to a server timer, or when a sprite animation stutters, steps at the tick rate or does nothing.
---

# Sprites

Index of sprite work in this repo. The engine mechanics that sit underneath are in the `rendering` skill (Sprites
section: when layers animate, pixel snapping, draw order). Warping a layer with a mask is `displacement-maps`. Glow
is `client-lighting`.

## Layers and keys

- Give a layer a `map:` key in YAML and address it from code with the same key. An enum works in both: YAML
  `map: [ enum.SupplyPodLandingZoneLayers.Zone ]`, C# `SupplyPodLandingZoneLayers.Zone`. The enum must be
  `[Serializable, NetSerializable]` and live in Shared.
- A layer has its own `scale:` (`scale: 1.5, 1.5`), `rotation`, `offset`, `color`, `visible`, `shader`.
- `GenericVisualizer` swaps layer state, visibility, sprite or rotation from `AppearanceComponent` data. Use it when
  the server decides the look. Use a client system when the look is a continuous function of time.
- Draw depth uses the `DrawDepth` vocabulary (`HighFloorObjects`, `Objects`, ...), never raw ints.

## Animating

- **`AnimationPlayerSystem` reaches only a few things.** `AnimationTrackComponentProperty` can animate any
  `[Animatable]` property of the component (`SpriteComponent.Scale`, `Rotation`, `Offset`, `Color`, or
  `TransformComponent.LocalPosition`). For layers, `SpriteComponent.SetAnimatableProperty` only knows
  `layer/<index>/texture`, `/state` and `/color`. There is no per-layer scale or rotation track.
- **So for per-layer scale or rotation, write it each frame** from a client system's `FrameUpdate`, with
  `SpriteSystem.LayerSetScale` / `LayerSetRotation` (they take an index, string key or enum key). Keep the system's
  query on a component that exists only while the effect runs, so the loop is short.
- Compute the value from a networked start time and duration, not from accumulated `frameTime`. A client that
  receives the entity late then lands on the right frame (`SupplyPodLandingZoneSystem`).
- **Whole-sprite effects** (position, colour fade) are fine with the animation player: `SupplyPodDescentSystem`.
  Fading a sprite out over a server-given time is already a component: `KsSpriteFadeOut`.
- **Rotation sign.** Negative `Angle` turns a layer clockwise on screen, matching BYOND's `Turn(positive)`. Check by
  eye for anything that isn't 4-fold symmetric.
- **Porting from DM.** `animate(src, transform = matrix()*0.01, time = t)` is a linear scale tween, and
  `matrix().Turn(90)` a clockwise quarter turn over `t`. `time` is in deciseconds in DM; check before copying a number.

## Smooth timing on the client

`IGameTiming.CurTime` steps at the tick rate, so anything driven by it alone looks choppy at frame rate. Add the
fraction of the current tick:

```csharp
var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;
```

Use that in `FrameUpdate`. Use plain `CurTime` for logic that must agree with the server (timers, cooldowns).

## Testing

A headless client runs `FrameUpdate`, so layer scale, rotation and colour are real and can be read back. Assert
mid-animation values within a range. See the `integration-testing` skill ("Mechanics that cost a lookup") for the
reading code. Whether it looks right is unverified until seen in game.
