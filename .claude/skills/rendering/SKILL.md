---
name: rendering
description: How the client draws a frame in this repo - Clyde's viewport pipeline, where lighting and FOV are applied, overlay spaces and how ZIndex interleaves with sprite DrawDepth, the stencil buffer, the drawing APIs, sprites and animation, lights and masks, and the fork's extra render passes (z-levels, view portals). Use when writing or debugging an Overlay, anything drawn in world space, a render pass or eye of your own, sprite draw order, lights or light masks, or when something is drawn in the wrong order, lit wrongly, shows through walls, vanishes, or doesn't animate.
---

# Rendering

What the engine does when it draws, as read from `RobustToolbox` v291, so you don't have to work it out again.
Shaders themselves (SWSL, built-ins, post-shaders, uniform leaks) are in `Klovn/Docs/shaders.md`. Read that too if
you write one.

Nothing headless draws anything (see [Testing](#testing)). Every statement here was read from the engine, but your
change is only verified once someone has looked at it in game. Say so when you report it.

## Where to look

| What | File |
| --- | --- |
| Frame and viewport pipeline, overlay dispatch, sprite/overlay interleave | `Robust.Client/Graphics/Clyde/Clyde.HLR.cs` |
| Light map, FOV, `ApplyFovToBuffer` | `Clyde/Clyde.LightRendering.cs` |
| Per-draw lighting and stencil state, batching | `Clyde/Clyde.Rendering.cs` (`DrawCommandBatch`, `ActivateShaderInstance`) |
| Sprite gathering and sort, pixel snap | `Clyde/Clyde.Sprite.cs` |
| Tiles, and the grid pixel snap | `Clyde/Clyde.GridRendering.cs` |
| Drawing handle internals, UV conversion | `Clyde/Clyde.RenderHandle.cs`, `Graphics/Drawing/DrawingHandleBase.cs` |
| Overlay registration and order | `Graphics/Overlays/OverlayManager.cs`, `Overlay.cs`, `Robust.Shared/Enums/OverlaySpaces.cs` |
| Sprite animation | `Robust.Client/GameObjects/EntitySystems/SpriteSystem.cs`, `SpriteSystem.Render.cs` |
| What a test client has instead | `Clyde/ClydeHeadless.cs` |
| Our main viewport and its extra passes | `Content.Client/Viewport/ScalingViewport.cs`, `.Klovn.ZLevel.cs`, `.Klovn.Portal.cs` |
| Draw depths | `Content.Shared/DrawDepth/DrawDepth.cs` |

## A frame

`Clyde.Render()` renders every viewport with `AutomaticRender`, then draws the UI. Our `ScalingViewport` renders
its own viewport from its `Draw`, during the UI pass, so it can run extra passes first:

```
ScalingViewport.Draw
  DrawPortalPrePass            one full RenderViewport per visible portal, into the portal's own viewport
  TryDrawZLevels               one full RenderViewport per z-level, deepest first, into the main viewport
    or _viewport.Render()      the ordinary single pass
  RenderScreenOverlaysBelow    ScreenSpaceBelowWorld overlays
  draw the viewport texture
  RenderScreenOverlaysAbove    ScreenSpace overlays
```

One `RenderViewport`, in order (`Clyde.HLR.cs`):

1. **`DrawLightsAndFov`.** Renders the light map into `viewport.LightRenderTarget` and the FOV depth texture.
   Nothing is drawn into the viewport yet. Sets `_lightingReady`.
2. **`WorldSpaceBelowWorld`** overlays.
3. **Grids** (tiles), each grid's matrix pixel-snapped.
4. **`DrawEntities`.** `WorldSpaceBelowEntities` overlays, then every sprite in draw order, with
   `WorldSpaceEntities` overlays slotted in between (see below).
5. **`WorldSpaceBelowFOV`** overlays.
6. **`ApplyFovToBuffer`.** Only if lighting is enabled, `DrawHardFov` is on, the eye has `DrawLight` and `DrawFov`,
   and the map has `LightingEnabled`. Paints FOV-hidden pixels with the occlude colour, and marks them in the
   stencil buffer (see [Stencil](#the-stencil-buffer)).
7. **`_lightingReady = false`.**
8. **`WorldSpace`** overlays.

Every overlay runs once per `RenderViewport`. That includes every portal capture, every z-level pass, and other
viewports such as camera monitors. `args.Viewport` tells you which one, and `args.MapId` which map.

## Lighting is per draw, not a pass

There is no "apply lighting" step. Each batch whose shader is not `unshaded` multiplies by the light map, while
`_lightingReady` is true (`DrawCommandBatch`). Otherwise it samples a white texture. So the same draw call comes
out differently depending on when it runs:

| Overlay space | `UseShader(null)` draws |
| --- | --- |
| `WorldSpaceBelowWorld`, `WorldSpaceBelowEntities`, `WorldSpaceEntities`, `WorldSpaceBelowFOV` | **lit** |
| `WorldSpace` | **unlit**: lighting is already switched off |

- For something unlit in the earlier spaces, use an unshaded shader (upstream's `unshaded` prototype, or
  `light_mode unshaded` in your own).
- An image that was already lit, such as a capture of another viewport, has to be drawn unlit. Otherwise it is lit
  twice. That's why `KsPortalOverlay` is `WorldSpace`.

## Overlays

**One instance per type.** `OverlayManager.AddOverlay` keys on `overlay.GetType()` and returns `false` for a second
one. Two layers of one effect need two subclasses (`ChemicalFireUnderOverlay` and `ChemicalFireOverOverlay` share
an abstract base).

**Order within a space** is ascending `ZIndex`, with `null` sorting as 0. Ties come out in no particular order.

**Interleaving with sprites (`WorldSpaceEntities`).** Sprites are sorted by `DrawDepth`, then `RenderOrder`, then
how low they are on screen (lower draws later), then uid. Before each sprite, every remaining overlay with
`ZIndex <= sprite.DrawDepth` is drawn. So an overlay with `ZIndex = Z`:

- draws **above** every sprite with `DrawDepth < Z`;
- draws **below** every sprite with `DrawDepth >= Z`, including those **equal** to it;
- with `ZIndex == null`, draws before the first sprite at all, because `null > depth` is false.

To sit between depths A and B, use `ZIndex = (int)B`. Other overlays count too: visible gas is
`GasTileVisibleGasOverlay` at `Gasses` (Default+11), and fire is `GasTileFireOverlay` at `Effects` (Default+12).
Anything at a depth above `Gasses` draws over the gas.

An overlay that draws many things is one draw in this order. It is not sorted against sprites item by item, so it
has to y-sort its own items, back to front (`ChemicalFireOverlay` does).

**State.** Clyde resets the transform and shader before and after each overlay's `Draw`. Set what you use anyway,
and set them back. A handle shared through a helper won't know.

**Exceptions are swallowed.** An exception in `BeforeDraw` or `Draw` is caught and logged to `clyde.overlay` as an
error, and the frame carries on. "My overlay draws nothing" is often an exception in the client log.

**`BeforeDraw` is the cheap gate.** It runs for every viewport and every pass, so return false early there rather
than in `Draw`.

## Our extra passes: decide what each overlay belongs to

The portal pre-pass and the z-level loop are full `RenderViewport`s, so every world overlay gets asked to draw into
them. For each overlay you write or touch, decide:

- **About the viewer** (damage vignette, noir, combat border, melee arc, gun spread, station AI static, night
  vision, mining scanner): it must not draw inside a portal's view. Gate it in `BeforeDraw`:
  `return !KsPortalRenderSystem.PassActive;`. In an upstream file, fully qualify it and mark it, per CONTRIBUTING §3.
- **About the world** (gas, stains, HUD icons, SSD indicators, anything attached to an entity): it **should** draw
  in a portal's view. Do not gate it. Gating these was the "HUD doesn't show through portals" bug.
- **Per map** (z-levels): each pass has its own `args.MapId`. Filter per-map data on it, as `KsPortalOverlay` does.

`PassActive` is a static flag set for the whole portal pre-pass (`ScalingViewport.Klovn.Portal.cs`). Search for it to
see every overlay that already has a gate.

### Rendering a viewport through your own eye

```csharp
private readonly Robust.Shared.Graphics.Eye _passEye = new Robust.Shared.Graphics.Eye() { DrawFov = false };

_passEye.Position = new MapCoordinates(centre, mapId);
_passEye.Offset = Vector2.Zero;          // the centre already includes any offset you want
_passEye.Rotation = rotation;
_passEye.Scale = mainEye.Scale;
_passEye.DrawLight = mainEye.DrawLight;
viewport.Eye = _passEye;
viewport.Render();
viewport.Eye = null;                     // so nothing else renders it through a stale eye
```

(The portal pre-pass in `ScalingViewport.Klovn.Portal.cs` is this, with capture reuse around it.)

- **FOV is computed from the eye's own position.** A pass whose eye isn't the viewer's needs `DrawFov = false`,
  or it shadows the scene from the wrong spot.
- **A pass costs about a whole frame**, lights included. Keep passes rare, small (`KsPortalRenderSystem.ResolutionScale`),
  or skipped when unseen.
- **Viewports and render targets are GPU memory.** Pool and release them; `ScalingViewport.Klovn.Portal.cs` keeps
  idle ones for two seconds.
- **The eye maths is pinned in tests.** An eye with rotation θ maps a world point P to view space as
  `Rot(+θ)(P - centre)` (`Eye.GetViewMatrix`), and its world bounds are a box turned by **−θ**
  (`Clyde.CalcWorldBounds`). `KsPortalTransform` and `KsPortalTransformTest` check this against the engine's own
  matrices; reuse them rather than working the signs out again.

## The stencil buffer

What it holds when:

| Point in the pass | Stencil |
| --- | --- |
| Start of a viewport | cleared to 0 (`RenderInRenderTarget` clears colour and stencil) |
| After `ApplyFovToBuffer` | **1 on every FOV-hidden pixel**, 0 on visible ones. The FOV shader `discard`s visible pixels, so only hidden ones get written. |
| No FOV this pass (ghost, `DrawFov = false`, lighting off) | still all 0 |

- **A shader sets its own stencil state for its draw.** `ActivateShaderInstance` enables the test only if the
  instance has stencil settings, and then applies `writeMask`, `func`/`ref`/`readMask`, and `op`. `op` happens only
  when the test passes (`Keep, Keep, op`). After the FOV pass, writes are masked off until a shader asks for them.
- **Upstream's `StencilMask` writes ref 1, the same value FOV wrote.** `StencilMask` → `StencilEqualDraw`, used in
  `WorldSpace`, matches every FOV-hidden pixel as well as your mask.

**Case study: the portal window was a rectangle in a mob and a wedge as a ghost.** The composite masked the wedge
with `StencilMask`, then drew the capture over the wedge's bounding rectangle with `StencilEqualDraw`. As a ghost
there is no FOV, so only the wedge had 1. In a mob, every FOV-hidden pixel in that rectangle had 1 as well. The fix
was to draw the shape as geometry (below), not to pick a different stencil value.

**Prefer geometry to the stencil for a shape.** `DrawPrimitives` with per-vertex UVs draws exactly the shape, in one
draw, with no state to clean up.

**`WorldSpace` overlays draw over FOV darkness.** They come after the FOV pass, so without a stencil test they
show through walls. To draw only where the viewer can see, test for 0, which is a stencil-`Equal` shader with
`ref: 0`.

## Drawing API facts

- **The world handle is in world units, Y up**, under the transform you set. Draw in a grid's frame by setting the
  grid's world matrix (CONTRIBUTING §6, "World-space overlays draw square to the map").
- **Texture coordinates in world-space draws** (`WorldTextureBoundsToUV`): U runs left to right, V from **0 at the
  bottom** to 1 at the top. The quad's bottom-left vertex samples (0, 0) of a whole texture. A render-target texture
  drawn this way comes out upright. `subRegion` arguments are in pixels with a top-left origin, and the engine
  flips them for you.
- **`DrawPrimitives(topology, texture, ReadOnlySpan<DrawVertexUV2D>)` takes raw UVs and only plain textures.** It
  throws `ArgumentException("Texture must be a basic texture.")` for an `AtlasTexture`, which is what RSI frames and
  most loaded textures are. Render-target textures are `ClydeTexture`s and work. Since the exception is swallowed,
  this shows as an overlay that draws nothing. For a sprite frame, draw quads with `DrawTextureRect` or
  `DrawTextureRectRegion`.
- **The sandbox forbids `stackalloc` and `CollectionsMarshal`** in content (CONTRIBUTING §6). Keep a pooled
  `DrawVertexUV2D[]` and pass it, since an array converts to a span (`ScalingViewport.Klovn.Portal.cs`,
  `_portalVertexPool`).
- **`RequestScreenTexture`** copies the whole screen before your `Draw`. That costs a full copy per overlay per
  pass, so avoid it in anything that runs in every portal and z-level pass.

## Sprites

- **Layers animate only while the engine draws them.** `RenderSprite` queues the sprite. `SpriteSystem.FrameUpdate`
  advances only queued, non-inert, unpaused sprites. A sprite that is hidden, off screen, or drawn by an overlay
  instead freezes on its frame. If an overlay replaces a sprite, animate from time instead: read the RSI state's
  frames and delays and pick by real time plus a per-entity phase (`ChemicalFireOverlay`, `AnimationPhase`).
- **Pixel snapping.** The engine nudges each grid's tiles and sprites onto whole screen pixels
  (`Clyde.GetPixelSnapOffset`, applied to the grid matrix). It is `internal`, so `ChemicalFireOverlay` keeps a copy.
  Anything drawn on a grid without it shimmers against the floor while the eye moves.
- **Draw order** is `DrawDepth`, then `RenderOrder`, then screen height, then uid (`Clyde.Sprite.cs`). Put depth
  changes in `DrawDepth.cs`'s vocabulary, not raw ints.

**Case study: the bottom half of chemical fires vanished.** The fire's lower half was a sprite and its upper half
an overlay. Visible gas (`Gasses`) drew between them, covering the lower half on tiles with gas. Both halves are now
overlays, at `Objects` and `Overdoors`, so both sit below gas and draw in a known order.

## Lights

- **`PointLightComponent.Rotation` is writable only by the engine's light system.** To aim a mask, set `autoRot:
  true` and rotate the entity (`light_transfer.yml`).
- **A mask at rotation 0 has its top along world +Y**, turning counter-clockwise from there
  (`KsPortalLightTransfer.GetMaskRotation`). Masks are `lightMask` prototypes with a texture and `lightCones`.
- **Spawn a stand-in light disabled**, then enable it once it is placed and sized. Otherwise it flashes at full
  radius for a frame.
- **Lights are rendered per viewport**, for that viewport's eye. Light from somewhere the eye can't reach (another
  z-level, the far side of a portal) needs a stand-in light that the eye can reach. `KsZLevelLightLeakSystem` and
  `KsPortalLightTransferSystem` are the two working examples, and the latter only runs near the viewer.

## View portals, in brief

The rendering half lives in `Content.Client/_KS14/Portal/`, `ScalingViewport.Klovn.Portal.cs` and
`Content.Shared/_KS14/Portal/KsPortalTransform.cs`.

1. The pre-pass renders each visible portal's destination through a mapped eye (`GetPortalEye`) into a pooled
   viewport, at reduced resolution.
2. `KsPortalTransform.TryGetVisibleWindow` gives the window: the doorway plus its shadow from the viewer, closed
   at the far end by an arc of points around the viewer, as a triangle fan. A single straight far edge was the bug
   behind "the window gets cut short when I stand near the doorway's line": the wedge opens to nearly 180° there,
   and a straight edge between its far corners runs close along the doorway.
3. `GetCaptureUV` gives each corner of the window the capture pixel it lands on.
4. `KsPortalOverlay` (`WorldSpace`, unlit) draws those triangles textured with the capture.

The mapping is a two-sided doorway: P' = B + R(rotB − rotA)(P − A). Portals turned 180° apart, facing each other,
therefore send you out the back of the far one; that is the mapping, not a bug.

**Facing doorways** (`KsPortalViewComponent.FacesViewer`, which player-made portals such as the hand teleporter's
have): the doorway has no direction of its own. When drawn, it is turned square to the viewer; when crossed, square
to where the traveller first came near it, kept per traveller in `KsPortalTravellerComponent.DoorwayRotations`;
for light transfer, square to each light. Only the doorway turns. The mapping still uses the portals' real
rotations, which for two unrotated portals is a translation, and that is why the view is right from every side.
`KsCCVars.PortalPlayerViews` switches player-made portals back to stock touch-to-teleport portals.

**A portal the viewer can't see draws nothing.** The candidate pass culls any portal without line of sight from the
viewer (`ExamineSystemShared.InRangeUnOccluded`, in `CollectPortalCandidates`). The window itself has no stencil
test, so it does draw over FOV darkness: a visible doorway's window can cover areas the viewer's own FOV hides,
which is intended, since the far side's walls are what should occlude it.

## Testing

The test client uses `ClydeHeadless`: `Render()` does nothing, render targets and viewports are dummies, and no
overlay `Draw`, control `Draw` or sprite render ever runs. `FrameUpdate` does run. So:

- **Put geometry in pure static functions and test those**, pinned against the engine's own matrices
  (`KsPortalTransformTest`). That's where sign errors and swapped axes are caught.
- **Put "what should be drawn" in `FrameUpdate` state** and assert on it from a connected client
  (`KsPortalLightTransferTest` asserts on stand-in lights the client spawned).
- **Anything that depends on draw order, stencil, lighting or the look of it is untested** until someone looks.
  Report it that way. Don't let a passing test stand in for "it renders correctly".
- **`SandboxTest`** whenever you reach for an API that content doesn't already use. Rendering code is where
  `stackalloc`, `CollectionsMarshal` and `MemoryMarshal` tempt you.

When it looks wrong in game, ask which pass and which space it is in before changing numbers. The stencil table
and the lit/unlit table above explain most of these bugs.
