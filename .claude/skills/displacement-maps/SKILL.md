---
name: displacement-maps
description: How displacement maps work in this repo and how to create, apply and debug one - the R/G offset plus alpha-mask encoding, the displacement.swsl shader, load.srgb false, DisplacementData and DisplacementMapSystem.TryAddDisplacement (clothing, hands, markings, species), the manual form (shader DisplacedDraw on a layer plus a map layer with copyToShaderParameters, GenericVisualizer state swaps, animated maps), KsAlwaysDisplaced, generating maps with Python, and checking them. Use when making a sprite layer warp, fit a body shape, dissolve or fade by a mask, adding a displacement .rsi, wiring a displacement into a prototype, or debugging a displacement that does nothing, is offset the wrong way, looks gamma-washed, shows neighbouring sprite pixels, or vanishes.
---

# Displacement maps

A displacement map is a second sprite that tells a shader, per pixel, where to read the first sprite from. Used to
bend clothing around a body shape, and (this fork) to cut a sprite away with an animated alpha mask. Upstream docs:
docs.spacestation14.com `art/displacement-maps`. Line numbers below are checked against this tree.

Nothing headless draws, so a displacement is only verified once someone has seen it in game (see [Testing](#testing)).

## 1. Encoding

The shader (`Resources/Textures/Shaders/displacement.swsl:11-15`; `displacement_unshaded.swsl` is identical plus
`light_mode unshaded;` on line 1):

```glsl
highp vec4 displacementSample = texture2D(displacementMap, displacementUVOut);
highp vec2 displacementValue = (displacementSample.xy - vec2(128.0 / 255.0)) / (1.0 - 128.0 / 255.0);
COLOR = zTexture(UV + displacementValue * TEXTURE_PIXEL_SIZE * displacementSize * vec2(1.0, -1.0));
COLOR.a *= displacementSample.a;
```

| Channel | Meaning |
| --- | --- |
| R | Horizontal offset. **128 = none.** 255 = +1.0, 1 = -1.0. |
| G | Vertical offset, same scale. Sampled with a y flip (`vec2(1, -1)`), so G is in image rows, not GL v. |
| B | Ignored. Handy for marking edited pixels in an editor. |
| A | **Multiplicative mask** on the layer's own alpha. 0 removes the pixel, 255 leaves it, in between fades. |

- `displacementSize` is 127 in all three shader prototypes (`Resources/Prototypes/Shaders/displacement.yml:10,17,24`),
  so **one step from 128 is one sprite pixel**: R = 128+n reads from n px to the right of the output pixel, which
  makes the content appear to move **left** by n. Per upstream docs, G above 128 reads from below (content moves up).
  *Not verified by rendering here; confirm the sign of G with a tiny test map before authoring a big one.*
- It is a read position, not a move: pixel X reads from Y. Reads that land outside the sprite are the
  [atlas bleed](#5-pitfalls-that-fail-silently) trap.
- The map is sampled over the **target layer's quad** (`tCoord2` mixed between `displacementUV` corners, `.swsl:8`),
  so a map of any size stretches to cover the layer. Match sizes anyway (1 map px per sprite px), or offsets blur.
- RGB is read even where alpha is 0. Keep RGB at 128 under transparent pixels so a masked edge doesn't
  pick up a stray offset if the map is ever filtered (filtering behaviour of the map texture: not checked).

## 2. The RSI must not be sRGB

The map's `meta.json` needs `"load": { "srgb": false }`. `TextureLoadParameters` defaults `Srgb = true`
(`RobustToolbox/Robust.Shared/Graphics/TextureLoadParameters.cs:53`, and `RsiLoading.cs:306` `RsiJsonLoad(bool Srgb = true)`).
The shader reads the map with raw `texture2D` (`displacement.swsl:12`), so an sRGB-loaded map is gamma-decoded
by the GPU: 128 no longer reads as 0.5, the whole sprite shifts and smears, and alpha mask edges shift too. No
error, and `Tools/_KS14/validate_rsis.py` doesn't check it (no `srgb` in it). *The decode being done by the GPU on sample is inferred, not read.*

```json
{ "version": 1, "license": "CC-BY-SA-3.0", "copyright": "...", "size": { "x": 32, "y": 32 },
  "load": { "srgb": false },
  "states": [ { "name": "dissolve", "directions": 1, "delays": [ [ 0.1, 0.1, 0.1, 0.1, 0.1, 0.1 ] ] } ] }
```
(`Resources/Textures/_KS14/Effects/foam_dissolve_displacement.rsi/meta.json`)

## 3. Two ways to apply one

Both end up identical: the **target layer** carries a displacement shader, and a **map layer** earlier in the layer
list copies its current frame into that shader's `displacementMap`/`displacementUV` uniforms every draw.

### 3a. The helper: `DisplacementData` + `TryAddDisplacement`

Used for anything the code builds layers for. YAML shape (`Content.Shared/DisplacementMap/DisplacementData.cs:5-19`):

```yaml
displacement:                 # the field name depends on the owner, see below
  sizeMaps:                   # required
    32:                       # key = the target layer's RSI width in px; 32 is REQUIRED
      sprite: Mobs/Species/Human/displacement.rsi
      state: jumpsuit-female
  # shaderOverride: DisplacedDraw                  (default)
  # shaderOverrideUnshaded: DisplacedDrawUnshaded  (default)
```
Real use: `Resources/Prototypes/Body/Species/human.yml:58-63` (`femaleDisplacements: { jumpsuit: { sizeMaps ... } }`).

`DisplacementMapSystem.TryAddDisplacement(data, sprite, index, key, out displacementKey)`
(`Content.Client/DisplacementMap/DisplacementMapSystem.cs:32`):
1. Removes any old `"<key>-displacement"` layer (`:44`, `:111-118`).
2. Sets the shader on layer `index` (`:46-56`): `ShaderOverride`, or the unshaded variant if the layer is
   currently `unshaded`. Set `shaderOverride: null` to leave the shader alone.
3. Fills `copyToShaderParameters` defaults, `displacementMap`/`displacementUV` (`:59-67`), so YAML doesn't repeat them.
4. Fails with an error log and returns false unless `sizeMaps` has key 32 (`:69-73`).
5. Picks the map whose key equals the layer RSI's width, else 32 (`:75-90`); warns on non-square RSIs.
6. Inserts the copy layer **at `index`** and maps it as `"<key>-displacement"` (`:99-100`). It sits before the
   target, as required, and the target moves to `index + 1`. Callers that keep iterating must bump their index
   (`ClientClothingSystem.cs:335-339` does `index++`; `VisualBodySystem.cs:210` passes `index + i + 1`).

Who calls it, and where the data comes from:

| Caller | `DisplacementData` comes from |
| --- | --- |
| `ClientClothingSystem.cs:335` | `InventoryComponent.Displacements` / `FemaleDisplacements` / `MaleDisplacements`, keyed by slot (`Content.Shared/Inventory/InventoryComponent.cs:41-53`) |
| `HandsSystem.cs:329` | `HandsComponent.HandDisplacement` / `LeftHandDisplacement` / `RightHandDisplacement` (`HandsComponent.cs:86-98`) |
| `VisualBodySystem.cs:210` | `VisualOrganMarkingsComponent.MarkingsDisplacement` (per body part, only for markings with `CanBeDisplaced`) |
| `KsAlwaysDisplacedSystem.cs:33` | `KsAlwaysDisplaced.displacement` (fork, below) |

**`KsAlwaysDisplaced`** (`Content.Client/_KS14/DisplacementMap/`): client-only component, `displacement:` is a
`DisplacementData`; at `ComponentStartup` it displaces **every layer the sprite has at that moment**, using the layer
index as the key (`"0"`, `"1"`...), and removes them on shutdown. Layers added later (equipment) are not displaced.
No prototype in `Resources/Prototypes` uses it today.

`CloneLocalVisualsComponent.Displacement` (`Content.Shared/_KS14/CloneLocalVisuals/`, used by
`_KsModule/.../nightmare.yml:78-83`) is a third consumer, but it doesn't use sprite layers: the overlay composites
the clone to a render target and applies the unshaded displacement shader itself, scaling `displacementSize` by the viewport zoom
(`CloneLocalVisualsOverlay.cs:216-220, 398-420`). Not a place to copy from for ordinary entities.

### 3b. The manual layer form

For a fixed entity (pods, effects) there is no helper to call. Write both layers in the prototype. The map layer is an ordinary RSI
layer with `copyToShaderParameters`; the engine never draws it (`SpriteSystem.Render.cs:159-163`):

```yaml
- type: Sprite
  layers:
  - state: darkpod
  - map: [ "paint_displacement" ]       # the map layer: BEFORE its target. No state yet, see visualizer below
  - state: syndicate
    shader: DisplacedDraw               # the target layer: needs a displacement shader, else nothing happens
    map: [ "paint" ]
```
(`Resources/Prototypes/_KS14/Entities/Objects/SupplyPod/cargopods.yml:18-21`)

A static map is just the map layer carrying its data directly:

```yaml
  - sprite: _KS14/Effects/foam_dissolve_displacement.rsi
    state: dissolve
    copyToShaderParameters:
      layerKey: paint                   # the target layer's map key
      parameterTexture: displacementMap
      parameterUV: displacementUV
```

How it renders (`SpriteSystem.Render.cs:200-222`): look up the target by `layerKey`; **return silently if the target has
no shader** (`:205-206`); duplicate the shader if it is shared/immutable (`:208-209`); set `displacementMap` to the map layer's
*current animation frame* and `displacementUV` to that frame's atlas rectangle. The engine's own remark: layers run in
order, so the copying layer must come **before** the target or you get a one-frame delay (`SpriteLayerData.cs:45-47`).

**Swapping maps at runtime** with `GenericVisualizer` (`cargopods.yml:44-58`): the visualizer sets the map layer's sprite/state, and
**must restate `copyToShaderParameters` every time**, because applying layer data without it sets the layer's
`CopyToShaderParameters` back to null (`SpriteComponent.cs:634-645`). Forgetting it turns the map layer into a normal
visible layer and the target loses its map. (The existing `# TODO LCDC` in `supplypod.yml:133` is this trap.)

```yaml
  - type: GenericVisualizer
    visuals:
      enum.StorageVisuals.Open:
        paint_displacement:
          True:  { sprite: _KS14/Objects/SupplyPod/displacement.rsi, state: open,   copyToShaderParameters: { layerKey: paint, parameterTexture: displacementMap, parameterUV: displacementUV } }
          False: { sprite: _KS14/Objects/SupplyPod/displacement.rsi, state: closed, copyToShaderParameters: { layerKey: paint, parameterTexture: displacementMap, parameterUV: displacementUV } }
```
Until the first visualizer write the map layer is blank, which `RenderLayer` skips (`:129`), so no map is ever copied. What the
shader sees in that case (an unset `displacementMap`) is not verified here; set an initial state if it matters.

**Animated maps.** The map layer has its own animation timeline, advanced like any visible layer in
`SpriteSystem.FrameUpdate` (`SpriteSystem.cs:204-229`), and only while the sprite is being drawn (`Render.cs:62-63`; a hidden,
off-screen or overlay-drawn sprite freezes). Consequences:
- The target's own animation and the map's are independent. Sync them yourself by giving both the same delays.
- **Setting a state restarts the animation** (`SpriteSystem.LayerSetters.cs:601-603`, `SpriteComponent.cs:548-550`), so a visualizer or
  `LayerSetState` that sets the map's state starts a one-shot from frame 0. Re-setting the *same* state through a visualizer: not verified whether it restarts.
- One-shot: `loop: false` on the map layer (`SpriteLayerData.cs:52`, honored at `SpriteComponent.cs:622` and
  `AdvanceFrameAnimation`, `:1694+`). It then **freezes on the last frame** (`:1737`), so make the last frame the resting
  look (a dissolve ends fully transparent; a "close" ends neutral). Looping maps need nothing.
- The map layer must be `visible` (default true); hidden layers neither render nor animate (`FrameUpdate` skips `!layer.Visible`).

## 4. Recipes

### Offset / warp (clothing fit)
Author a map the size of the clothing RSI frame (32x32 for humans). Fill R,G with 128, alpha 255. Wherever
the garment should be shifted left by 1 px, set R = 129; wherever it should be pushed in, offset toward the body.
Keep alpha 255 so nothing is masked. Aseprite scripts for upstream's workflow are in `Tools/SS14 Aseprite Plugins/`
(`Displacement Map Shift/Flip/Visualizer.lua`). Add keys to `sizeMaps` for other RSI widths (e.g. 64) and always 32.

### Alpha mask / dissolve (worked example)
`Resources/Textures/_KS14/Effects/foam_dissolve_displacement.rsi/dissolve.png`: 6 frames of 32x32 (laid out 3x2),
R=G=B=128 everywhere, alpha = the original dissolve animation's alpha divided by the base sprite's alpha, so
`base.a * map.a` reproduces it. The one frame where the original had content shifted 1 px got R=127. Built by
a script (copy of the idea):

```python
import numpy as np
from PIL import Image
base = np.array(Image.open("foam.png").convert("RGBA")).astype(float)          # the sprite being displaced
target = np.array(Image.open("foam-dissolve.png").convert("RGBA")).astype(float)  # what each frame should look like
out = np.zeros((64, 96, 4), np.uint8); out[..., :3] = 128                       # neutral RGB, also under alpha 0
for k in range(6):
    r, c = divmod(k, 3); f = target[r*32:(r+1)*32, c*32:(c+1)*32]
    m = np.full((32, 32, 4), 128, np.uint8)
    m[..., 3] = np.clip(np.round(f[..., 3] / np.maximum(base[..., 3], 1) * 255), 0, 255).astype(np.uint8)
    out[r*32:(r+1)*32, c*32:(c+1)*32] = m
Image.fromarray(out).save("dissolve.png")
```
Not yet referenced by any prototype in this tree (grep of `Resources/Prototypes`). To use it: section 3b with `loop: false`
on the map layer, the target layer `shader: DisplacedDraw`.

### Check a map by simulating the shader
Reproduce lines 13-15 offline instead of guessing (nearest sampling, clamped; the real shader reads atlas neighbours instead of clamping):

```python
def simulate(sprite_rgba, map_rgba):                         # both 32x32x4 uint8
    h, w = map_rgba.shape[:2]
    dx = (map_rgba[..., 0].astype(float) - 128)              # size 127 => value-128 == pixels
    dy = (map_rgba[..., 1].astype(float) - 128)              # + = read from BELOW (per upstream docs)
    xs = np.clip(np.arange(w)[None, :] + dx, 0, w-1).round().astype(int)
    ys = np.clip(np.arange(h)[:, None] + dy, 0, h-1).round().astype(int)
    out = sprite_rgba[ys, xs].astype(float)
    out[..., 3] *= map_rgba[..., 3] / 255.0
    return out
```
Composite on a checkerboard at 4x and compare against the intended frame. Scratch tooling like this belongs in the
scratchpad, not the repo.

## 5. Pitfalls that fail silently

| Symptom | Cause |
| --- | --- |
| Nothing happens at all | The target layer has no displacement shader (`HandleShaderLayer` returns, `Render.cs:205-206`). Manual form: add `shader: DisplacedDraw`. Also check `layerKey` names the *target*, and that the target has a `map:` key (`LayerMap[...]` lookup, `:203`). |
| Shader added but nothing displaced | Shader without a map layer: the uniform is unset. Always add both halves. |
| Whole sprite shifted, smeared or washed | `load.srgb: false` missing on the map RSI (section 2). |
| One-frame lag or never applies | The map layer is *after* its target in the layer list. Map layer index must be lower. |
| Displacement vanishes after a state/visualizer change | The layer data lacked `copyToShaderParameters`, which nulls it (`SpriteComponent.cs:634-645`). |
| Edge pixels show bits of neighbouring sprites | Offsets read outside the frame. The shader samples the **atlas** (`UV` covers the sub-rectangle of a shared atlas; `TEXTURE` is the whole atlas, `Klovn/Docs/shaders.md:83-88`), so a read past the edge hits the adjacent frame, not transparent. Keep offsets pointing inside the content, or leave a transparent margin in the target art. |
| Sprite fades unexpectedly | `COLOR.a *= map.a`: the map's alpha multiplies the layer's own alpha, and layer `color` alpha on top. Opaque maps want alpha 255. |
| Wrong map picked / looks stretched | `sizeMaps` keyed by the layer RSI **width**, falling back to 32 (`DisplacementMapSystem.cs:75-90`); non-square RSIs warn. Map size is not checked against the layer, it is stretched over it. |
| "no 32x32 default" error, displacement skipped | `sizeMaps` lacks key 32 (`:69-73`). |
| Displacement lands on the wrong layer, or on the map layer | Layer `index` shifts when layers are inserted before it. The helper's `index` must be the target's index at call time, and the target is `index + 1` afterwards. `KsAlwaysDisplaced` walks back-to-front for this reason. |
| Unshaded layer still lit, or lit layer gone dark | The helper picks `DisplacedDrawUnshaded` only if the layer is `unshaded` *at that moment*; set `unshaded` before adding the displacement, not after. |
| Animated map frozen | The sprite is not being drawn, or the map layer is `visible: false`. |
| Displacement ignored for late-added layers | `KsAlwaysDisplaced` handles only layers present at startup. |
| Layer with `DisplacedDraw` also needs a second shader | Not supported: one shader per layer (the helper's own TODO, `DisplacementMapSystem.cs:48-51`). `DisplacedStencilDraw` exists (`displacement.yml:1-10`) but nothing in this tree uses it. |

## 6. Testing

`ClydeHeadless` never renders (see the `rendering` and `integration-testing` skills), so `HandleShaderLayer` never runs: you cannot
assert that `displacementMap` was set, or what the sprite looks like. What a connected-client test can check
(client sprite state is built by `ComponentStartup`/visualizers, so tick before asserting):

- Helper path: after equipping / spawning, the sprite has a layer mapped `"<key>-displacement"`
  (`SpriteSystem.LayerMapTryGet(..., logMissing: false)`), it is **before** the target's index, its
  `CopyToShaderParameters.LayerKey` equals the key, and the target's `ShaderPrototype` is `DisplacedDraw`
  (or the unshaded one for an `unshaded` layer).
- Manual path: target layer has the shader; after the visualizer fires, the map layer has the expected state and still has `CopyToShaderParameters`.
- A bad `sizeMaps` (no 32) logs an error, which fails the test, so assert it positively rather than ignoring it.
- Prove the test red by deleting the `shader:` line or the key-32 entry and watching it fail (CONTRIBUTING §6).
- Always report the look, direction of the offset, animation timing and lighting as **unverified until seen in game**.
