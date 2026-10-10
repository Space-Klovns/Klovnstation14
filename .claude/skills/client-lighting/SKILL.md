---
name: client-lighting
description: How the client light pipeline works in this repo and how to make something glow - the BeforeLighting overlay chain (BeforeLightTarget, Roof, Emissive, TileEmission, LightBlur, AfterLightTarget) with exact ZIndex values, drawing into the enlarged light target, the EmissiveLayers component for glowing sprite layers from YAML, the bloom shader (Emissive), and unshaded sprites. Use when adding a glowing effect or emissive overlay, making sprite layers glow, adding an overlay to the light chain, choosing a ZIndex between light overlays, or debugging glow that is missing, unblurred, in the wrong place, or dark in the dark. Read the overlays skill first for general overlay mechanics.
---

# Client lighting and emissive glow

General overlay API, spaces, frames and DI are in the **overlays** skill. This one covers `OverlaySpace.BeforeLighting` and the content light chain in `Content.Client/Light/` and `Content.Client/_KS14/Emissive/`.

## 1. Pick the right tool

| Want | Use |
| --- | --- |
| A sprite layer on a prototype that glows and lights up its surroundings | `EmissiveLayers` component (section 4). No C#. |
| A sprite layer that just ignores darkness (no light spill) | `shader: unshaded` on the layer (section 6). |
| A marker/beam visible in the dark, not lighting anything | `WorldSpace` overlay (above lighting and FOV), overlays skill section 3. |
| Custom C# glow (beam, shape) that blooms and lights its surroundings | A `BeforeLighting` overlay drawing into the enlarged light target (section 3). |

## 2. The pipeline

Everything below is `OverlaySpace.BeforeLighting`, which Clyde runs inside `DrawLightsAndFov` (`Clyde.LightRendering.cs`) while `viewport.LightRenderTarget` is bound and freshly cleared (transparent; `PlanetLightSystem` answers the clear-colour event), before FOV is applied to the light buffer and before point lights are added on top. Order is ascending `ZIndex`. Registration: `PlanetLightSystem` adds BeforeLightTarget, Roof, TileEmission, LightBlur, SunShadow, AfterLightTarget; `EmissiveOverlaySystem`, `KsLaserSightOverlaySystem` and the z-level system add theirs.

**Skipped entirely** (so none of this draws) when the light manager is disabled, the eye has `DrawLight == false`, the map has lighting disabled, or the eye is in nullspace. Do not hang non-glow logic off these overlays.

ZIndex chain (arithmetic from the constants; `ContentZIndex` fields):

| Overlay | Defined as | Value |
| --- | --- | --- |
| `BeforeLightTargetOverlay` | `-11` (KS14 changed upstream -10) | -11 |
| `RoofOverlay` | Before + 1 | -10 |
| `EmissiveOverlay` | Before + 2 | -9 |
| `TileEmissionOverlay` | Roof + 1 | -9 |
| `KsLaserSightEmissiveOverlay` | Before + 2 | -9 |
| `KsZLevelLightBufferOverlay` | LightBlur - 1 | -9 |
| `LightBlurOverlay` | TileEmission + 1 | -8 |
| `AfterLightTargetOverlay` | LightBlur + 1 | -7 |
| `AmbientOcclusionOverlay`, `SunShadowOverlay` | After + 1 | -6 |

Rules that fall out of it:

- **A glow overlay must sit strictly between `BeforeLightTarget` (-11, which clears the enlarged target) and `LightBlur` (-8, which blurs it): ZIndex in [-10, -9].** At or above -8 it is drawn after the blur (unblurred) or, worse, after `AfterLightTarget` (-7, which has already copied the target into the light RT) and does nothing.
- Ties are unstable (`List.Sort`, overlays skill section 1). The -9 tie between the glow overlays is harmless because they all add light into the same target. A tie with `LightBlur` is not: `KsLaserSightEmissiveOverlay` used to be `Before + 3 = -8` and tied with `LightBlurOverlay`, so the laser glow could run after the blur. It is now `+ 2` (-9).
- Express your ZIndex relative to the constants (`BeforeLightTargetOverlay.ContentZIndex + 2`), and keep it below `LightBlurOverlay.ContentZIndex`. If you need strict ordering against another glow overlay you must re-space the chain, not rely on a tie.

### What each stage does

- **`BeforeLightTargetOverlay`** (`Content.Client/Light/BeforeLightTargetOverlay.cs`): per viewport, creates (or resizes) `EnlargedLightTarget`, an `Rgba8Srgb` render target of size `LightRenderTarget.Size + 2 m * 32 px`, cached in an `OverlayResourceCache`, and **clears** it to the map clear colour. Publishes `EnlargedBounds = args.WorldBounds.Enlarged(skirting / 2)` (skirting = 2 m). The skirt exists so blur at the viewport edge has off-screen data to pull from.
- **`RoofOverlay`, `TileEmissionOverlay`, `EmissiveOverlay`, laser/z-level overlays**: draw light-emitting stuff into `EnlargedLightTarget`.
- **`LightBlurOverlay`**: `_clyde.BlurRenderTarget(viewport, target, blurTarget, eye, 14f * 5f)` blurs the enlarged target in place.
- **`AfterLightTargetOverlay`**: `RenderInRenderTarget(viewport.LightRenderTarget, ..., Color.Transparent)` (this **clears** the real light RT) and draws the cropped enlarged texture into it. The engine then adds point lights on top.

## 3. Drawing your own glow

```csharp
public sealed partial class KsExampleGlowOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.BeforeLighting;
    public const int ContentZIndex = BeforeLightTargetOverlay.ContentZIndex + 2;   // -9; must stay < LightBlur (-8)

    public KsExampleGlowOverlay() { ZIndex = ContentZIndex; }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // gather + cull here (overlays skill section 2); cull against EnlargedBounds, not args.WorldBounds
        return args.Viewport.Eye != null /* && have something to draw */;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;                  // copy out of the ref struct: lambdas cannot capture args
        var viewport = args.Viewport;
        var eye = viewport.Eye!;

        var lightOverlay = OverlayManager.GetOverlay<BeforeLightTargetOverlay>();   // throws if not registered
        var target = lightOverlay.GetCachedForViewport(viewport).EnlargedLightTarget; // throws if its Draw has not run for this viewport

        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);                // = RenderScale * lightScale
        var worldToTarget = target.GetWorldToLocalMatrix(eye, scale);                 // metres -> enlarged-target pixels

        worldHandle.RenderInRenderTarget(target, () =>
        {
            worldHandle.SetTransform(worldToTarget);          // map-frame drawing
            worldHandle.UseShader(_bloomShader);              // Emissive InstanceUnique(), optional
            worldHandle.DrawRect(glowBox, glowColor);         // or DrawTextureRect(tex, Box2Rotated, modulate)
            worldHandle.UseShader(null);
            worldHandle.SetTransform(Matrix3x2.Identity);
        }, null);                                             // null: do NOT clear, that would wipe earlier glow
    }
}
```

Points that matter:

- Inside `RenderInRenderTarget` the handle works in the target's **pixel** space; the `SetTransform(target.GetWorldToLocalMatrix(eye, scale))` is what lets you keep drawing in metres. Without it everything lands at a few pixels in a corner.
- The scale is `viewport.RenderScale * (LightRenderTarget.Size / viewport.Size)`: the light map is lower resolution than the viewport (`_lightResolutionScale`, default 0.5). Using the plain `RenderScale` makes glow the wrong size or offset. Used identically in `EmissiveOverlay`, `TileEmissionOverlay`, `KsLaserSightEmissiveOverlay`.
- **Pass `null` as the clear colour.** A colour clears the target and erases glow drawn by earlier overlays.
- **Per-grid vs world transform.** Map-frame shapes: `worldToTarget` alone. Grid-local entities (everything on a grid): compose the grid's world matrix first, `Matrix3x2.Multiply(_transformSystem.GetWorldMatrix(grid.Owner), worldToTarget)`, then draw in grid-local coordinates (`EmissiveOverlay`). When rotating sprites by the eye, subtract the grid's inverse rotation as `EmissiveOverlay` does (`eyeRotation - gridInvMatrix.Rotation()`).
- Cull against `lightOverlay.EnlargedBounds`, otherwise glow just outside the screen vanishes before it can blur in.
- `OverlayManager` is the inherited property of `Overlay`; do not declare a second `IOverlayManager` field (CONTRIBUTING section 4). Declare other `[Dependency]` fields (transform system, queries) via the hook pattern in the overlays skill.
- Shader parameters are read at flush, so one `InstanceUnique()` per distinct intensity (`KsLaserSightEmissiveOverlay` keeps a dictionary keyed by intensity). `RenderInRenderTarget` flushes, but several draws inside one callback share a batch.
- Per-viewport state: the enlarged target is per viewport, and `BeforeDraw`/`Draw` run per viewport and per z-level pass.

## 4. `EmissiveLayers` from YAML

`Content.Shared/_KS14/Emissive/EmissiveLayersComponent.cs`, drawn by `EmissiveOverlay`. Marks sprite layers that are also drawn into the light target with the bloom shader. **The layers must exist on the `Sprite`** (keyed by the map key or `enum.` key).

| Field | Default | Meaning |
| --- | --- | --- |
| `layers` | `[]` | Layer keys to make glow. |
| `glowRadius` | `0.25` | Metres the glow quad is enlarged by around the layer; the main source of visible spread. `0` gives no spread. |
| `intensity` | `2.2` | `bloom_intensity` of the shader (one shader instance per distinct value). |
| `offset` | `(0,0)` | Offset of the glow's centre. |
| `useSpriteTransform` | `true` | Apply the sprite's and layer's rotation/offset when drawing the glow. |
| `onlyRotateTexture` | `false` | Use rotation only for the texture lookup, not for rendering. For direction-state sprites (light tubes). |

```yaml
- type: EmissiveLayers   # in-tree uses are marked "# KS14: emissive"
  glowRadius: 1
  intensity: 3
  layers:
  - enum.ToggleableVisuals.Layer
```

Examples: `Resources/Prototypes/Entities/Objects/Tools/flare.yml`, `Misc/land_mine.yml`, `Fun/pai.yml` (`screen`, radius 0.06), `Specific/Medical/healthanalyzer.yml` (radius 0, intensity 0.01, a near-no-op marker). Emissive mapping only sees entities on grids (`EmissiveOverlay` gathers per grid).

## 5. The bloom shader

Prototype `Emissive` (`Resources/Prototypes/_KS14/Shaders/emissives.yml`, `kind: source`) -> `Resources/Textures/_KS14/Shaders/bloom.swsl`. `light_mode unshaded;`, uniforms `bloom_intensity` (2.2) and `threshold` (0.15).

Fragment: `base = texture(TEXTURE, UV)`; 3x3 gaussian (1 2 1 / 2 4 2 / 1 2 1)/16 around the pixel at offsets of **exactly one texel of the bound texture**; each sample is masked by `step(0.15, luminance)`; `blur = pow(blur, 0.7)`; `COLOR = base + blur * bloom_intensity`. Then the engine multiplies by vertex colour (`MODULATE`).

Constraints:

1. **Needs a texture.** The blur reaches one texel, so spread comes from the sprite's own soft edges, `glowRadius` enlarging the quad, and the later `LightBlurOverlay` blur.
2. **The 0.15 luminance threshold is on the texture RGB**, not on the vertex tint. Texels darker than 0.15 contribute no bloom (they still output `base`). Tinting via modulate does not change the mask.
3. **Plain `DrawRect` (1x1 white texture) has no spatial bloom**: every tap is white, so the result is just `white * (1 + bloom_intensity) * vertexColor`, an intensity multiplier. Hence `KsLaserSightEmissiveOverlay` fakes spread with a wider, alpha-scaled rect rather than relying on the shader.
4. `bloom_intensity` is per instance: one `InstanceUnique()` per intensity.
5. The colour and alpha you pass to the draw call is the emitted colour.

## 6. "Unshaded" alternatives

- `light_mode unshaded;` in a `.swsl` header, or `shader: unshaded` on a prototype sprite layer (e.g. `backpacks.yml`), makes a sprite ignore the light RT: bright in the dark, but it does **not** light neighbouring tiles and gets no blur. Use for screens/indicators where spill does not matter.
- For overlays, `WorldSpace` is already unaffected by lighting and FOV (overlays skill section 3).
- Combine: an unshaded layer for the crisp sprite plus `EmissiveLayers` on the same layer key for the spill.

## 7. Debugging checklist

- Nothing draws: lighting off for the map/eye (BeforeLighting is skipped); `GetOverlay<BeforeLightTargetOverlay>()` threw because it is not registered; or `GetCachedForViewport` threw because BeforeLightTarget's `Draw` has not yet run for that viewport (ZIndex not above -11).
- Glow appears but sharp: your ZIndex is >= `LightBlur` (-8), or a tie flipped.
- Glow wrong size or offset: missing the `lightScale` factor, or the transform is not applied inside the `RenderInRenderTarget` callback.
- Earlier glow disappears: you passed a clear colour to `RenderInRenderTarget`.
- Glow clipped at the screen edge: you culled with `args.WorldBounds` instead of `EnlargedBounds`.
- Glow on a turned grid misaligned: map-frame drawing for grid-local data, see overlays skill section 5.
- A second, offset/scaled copy of your glow (or world-pass shape) beside the real one: z-level mode re-runs BeforeLighting/World overlays per level with a depth-scaled eye; tag cached data with its `MapId` and filter by `args.MapId` (overlays skill section 1, `HasSegmentsOn`). The `GetWorldToLocalMatrix(eye, scale)` for world-coordinate drawing is correct as is.
- Per-vertex alpha/gradients: `DrawPrimitives(TriangleStrip, Texture.White, DrawVertexUV2DColor[])` ignores `Modulate` and wants linear colour (`Color.FromSrgb`); the default UV (0.5, 0.5) is flat white, so the Emissive shader still works (`KsLaserSightBeamDrawer`).
- Intensity of one glow changes another's: shared shader instance, make it unique per value.
- Verify behaviour at runtime is UNVERIFIED for the tie analysis in section 2 (derived from constants and the unstable sort, not observed).
