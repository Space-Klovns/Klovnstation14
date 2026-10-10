---
name: overlays
description: How client-side Overlays work in this repo - lifecycle (BeforeDraw/Draw/FrameUpdate), OverlaySpace render order, ZIndex, drawing handles, shaders, map-vs-grid frames, direction conventions, dependency injection for overlays, and the BeforeDraw-gathers/Draw-draws pattern. Use when writing, changing or debugging anything that derives from Overlay (world markers, beams, HUD indicators, screen shaders, debug overlays), when something draws in the wrong place, at a slant, 180 degrees off, behind/above the wrong layer, or flickers, or when an overlay leaks or is not removed. For glow that ignores darkness see also the client-lighting skill.
---

# Client overlays

An `Overlay` is a client-only object that Clyde calls during rendering. Engine sources: `RobustToolbox/Robust.Client/Graphics/Overlays/` (`Overlay.cs`, `OverlayManager.cs`, `OverlayDrawArgs.cs`), `.../Graphics/Drawing/`, `.../Graphics/Clyde/Clyde.HLR.cs` (the render loop). Glow/emissive work is in the **client-lighting** skill; this one is everything before that.

## 1. Lifecycle and API

```csharp
public abstract class Overlay {
    public virtual OverlaySpace Space => OverlaySpace.ScreenSpace;   // default is ScreenSpace; [Flags]
    public virtual bool RequestScreenTexture { get; set; }           // copy viewport RT into ScreenTexture just before Draw
    public Texture? ScreenTexture;                                    // FIELD, valid only inside Draw, only if requested
    public int? ZIndex { get; set; }
    public virtual bool OverwriteTargetFrameBuffer => false;         // true => current RT is cleared before Draw
    protected internal virtual bool BeforeDraw(in OverlayDrawArgs args) => true;
    protected internal abstract void Draw(in OverlayDrawArgs args);
    protected internal virtual void FrameUpdate(FrameEventArgs args) { }
    protected virtual void DisposeBehavior() { Disposed = true; }    // call base
}
```

- There is no `Visible` / `IsOnScreen`. To hide: return `false` from `BeforeDraw`, or remove the overlay.
- The base ctor resolves only `IOverlayManager` (exposed as `protected OverlayManager`). Do not redeclare it (CONTRIBUTING section 4).
- Exceptions in `BeforeDraw`/`Draw` are caught and logged to sawmill `clyde.overlay`; the overlay keeps running next frame. A silently missing overlay: check the log first.
- `OverlayDrawArgs` is a `readonly ref struct`. It cannot be captured by a lambda (e.g. a `RenderInRenderTarget` callback): copy `args.WorldHandle`, `args.Viewport`, `args.WorldBounds`, `args.MapUid` to locals first.
- `args.WorldHandle` / `args.ScreenHandle` are unchecked casts. `ScreenSpace` and `ScreenSpaceBelowWorld` get a `DrawingHandleScreen`, every other space gets `DrawingHandleWorld`; the wrong one throws `InvalidCastException`.

`OverlayDrawArgs` fields: `Space` (the single space being rendered), `ViewportControl` (screen spaces only, else null), `Viewport` (`.Eye` nullable, `.RenderTarget`, `.LightRenderTarget`, `.Size`, `.RenderScale`, `.WorldToLocal()`), `MapUid`, `MapId`, `WorldAABB`, `WorldBounds` (`Box2Rotated`, rotated by minus the eye rotation), `ViewportBounds`. Guard `args.Viewport.Eye == null` and `args.MapId == MapId.Nullspace`.

### IOverlayManager

| Call | Notes |
| --- | --- |
| `AddOverlay(o)` | One instance per overlay **runtime type** (dictionary keyed by `GetType()`); returns `false` if that type is already present. |
| `RemoveOverlay(o)` / `RemoveOverlay(Type)` / `RemoveOverlay<T>()` | **Does not call `Dispose()`.** Dispose render targets and unique `ShaderInstance`s yourself (system `Shutdown`, or `DisposeBehavior`). |
| `TryGetOverlay<T>(out T?)`, `HasOverlay<T>()` | Safe. |
| `GetOverlay<T>()` | Throws `KeyNotFoundException` if not registered. |
| `AllOverlays` | Sorted by ZIndex. |

Overlays are normally added and removed by an `EntitySystem` (`Initialize` / `Shutdown`), see section 7.

### ZIndex

- Draw order within one space is ascending `ZIndex`. `null` sorts as `0`.
- Sorting uses `List<T>.Sort`, which is **unstable: equal ZIndex has no defined order** (the engine doc says "random"). Never rely on a tie. A tie that "works" today can flip when another overlay is added (see client-lighting section 2 for a real one).
- Sorting only happens on Add/Remove. Changing `ZIndex` after `AddOverlay` does nothing until the next resort.
- **`WorldSpaceEntities` special case: `ZIndex == null` draws before ALL sprites, not at 0.** Always set an explicit ZIndex there (`(int)DrawDepth.Effects`, as `ChemicalFireOverlay` and `KsTrailOverlay` do). See section 2.

### FrameUpdate

`OverlayManager.FrameUpdate` calls every registered overlay's `FrameUpdate` **once per frame**, in dictionary order (not Space/ZIndex order), independent of viewports, maps and spaces. It runs in `GameController.Update` after entity `FrameUpdate` and before the frame renders. Use it (or an `EntitySystem.FrameUpdate`) for once-per-frame work. It runs even when nothing draws the overlay.

## 2. BeforeDraw: gather there, draw in Draw (house pattern)

**`BeforeDraw` runs immediately before `Draw`, on the same (render) thread, with the same `args`, with nothing in between** except the optional screen-texture copy and framebuffer clear. Call sites: `Clyde.HLR.cs` `RenderSingleWorldOverlay` (`BeforeDraw` ~:152, `Draw` ~:164) for world spaces and `RenderOverlaysDirect` (~:237 / ~:249) for screen spaces. Therefore all per-viewport work belongs in `BeforeDraw`:

- culling against `args.WorldAABB` / `args.WorldBounds` / `args.MapId`,
- enumerating entities, reading interpolated transforms, computing progress / alpha / transforms,
- converting to viewport pixels (`args.Viewport.WorldToLocal`).

Store the result **transiently in reusable scratch fields** on the overlay, return `false` when nothing is to be drawn, and let `Draw` only draw from that data. Benefits: `Draw` stays tiny and allocation-free; returning `false` skips `Draw` **and** the `RequestScreenTexture` copy / `OverwriteTargetFrameBuffer` clear (the expensive parts); the cull is done for exactly the viewport being drawn.

```csharp
public sealed partial class KsExampleOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    // Scratch: cleared at the top of every BeforeDraw, read only by the Draw that immediately follows.
    private readonly List<(Vector2 Position, Angle Rotation, float Alpha)> _visible = [];

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _visible.Clear();                                   // runs several times per frame; never accumulate

        if (args.Viewport.Eye == null || args.MapId == MapId.Nullspace)
            return false;

        var enumerator = _entityManager.EntityQueryEnumerator<KsExampleComponent, TransformComponent>(); // EntityQuery<T> itself only does lookups (TryComp/HasComp), it does not enumerate
        // ... for each: skip other maps, skip if !args.WorldAABB.Contains(position), compute alpha, _visible.Add(...)

        return _visible.Count > 0;                          // false => Draw (and any screen copy) skipped
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        foreach (var (position, rotation, alpha) in _visible)
        {
            worldHandle.SetTransform(Matrix3Helpers.CreateTransform(position, rotation));
            worldHandle.DrawRect(Box2.CenteredAround(Vector2.Zero, Vector2.One), Color.Red.WithAlpha(alpha));
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }
}
```

Caveats (all verified in `Clyde.HLR.cs` / the z-level viewport):

1. **The pair runs once per viewport render per matching space flag.** A `WorldSpace | ScreenSpace` overlay gets a separate `BeforeDraw`+`Draw` pair per flag, with `args.Space` set to that single space; the stash from one pass is overwritten by the next. Branch on `args.Space`.
2. **Several times per frame in z-level mode.** `ScalingViewport.TryDrawZLevels` (`Content.Client/Viewport/ScalingViewport.Klovn.ZLevel.cs`, ~:239 and ~:336) calls `_viewport.Render()` once per z-level pass with `Viewport.Eye` swapped. Extra `AutomaticRender` viewports multiply it too. So `BeforeDraw` must be cheap and idempotent.
   Consequence: data cached outside the pass (per-frame segments, markers) must carry its `MapId` and be filtered by `args.MapId` in `BeforeDraw`/`Draw`, because each level map is rendered with a different, depth-scaled eye. Unfiltered, every pass draws all of it, giving a drifting second copy beside the real one (the laser's "stray secondary beam"). Resolve for all maps near the eye's x/y, tag, filter (`KsLaserSightOverlaySystem.HasSegmentsOn`).
3. **Do not put once-per-frame work in `BeforeDraw`/`Draw`**: raycasts shared between two overlays, sounds, state transitions, advancing timers, CVar-driven flips. Do those in `FrameUpdate` or an `EntitySystem.FrameUpdate` (`KsLaserSightOverlaySystem` resolves beam segments in `FrameUpdate` and both laser overlays read them). Counter-example not to copy: `BlindOverlay.BeforeDraw` toggles `_lightManager.Enabled` (a side effect per viewport pass).
4. Treat stashed state as valid only inside the immediately following `Draw`. Do not read scratch from `FrameUpdate` or other overlays.
5. Do not call `viewport.Render()` from `Draw`: `WorldSpaceEntities` overlays iterate a shared mutable overlay list across the whole sprite loop, a nested render corrupts it.
6. Per-viewport GPU resources (render targets) are created/resized lazily in `BeforeDraw` or `Draw`, keyed by `args.Viewport.Id` (`OverlayResourceCache<T>`, `Content.Client/Graphics/OverlayResourceCache.cs`; used by `GasTileHeatBlurOverlay`).

Existing users of the pattern: `SingularityOverlay` (world-to-fragment coordinates computed in BeforeDraw), `KsShockwaveOverlay`, `RadiationPulseOverlay`, `FtlArrivalOverlay`, `KsShaderStatusEffectOverlay`, `DrunkOverlay`, `DrowsinessOverlay`. Pure gating (no stash): `CombatModeIndicatorsOverlay`, `ParallaxOverlay`, `FlashOverlay`, `BlurryVisionOverlay` (checks `args.Viewport.Eye` is the local player's eye).

## 3. OverlaySpace and render order

`[Flags] enum OverlaySpace` (`RobustToolbox/Robust.Shared/Enums/OverlaySpaces.cs`). Order within one viewport render (`Clyde.HLR.cs` `RenderViewport`):

| # | Space | Draws | Lit by lights? | Under hard FOV? |
| --- | --- | --- | --- | --- |
| 1 | `BeforeLighting` | into the **light** render target, inside `DrawLightsAndFov` (client-lighting skill) | n/a (is the light) | n/a |
| 2 | `WorldSpaceBelowWorld` | below grids, entities, everything | yes | yes |
| 3 | `WorldSpaceGrids` | after each grid; overlay must implement `IGridOverlay` or it is skipped with an error | yes | yes |
| 4 | `WorldSpaceBelowEntities` | above grids, below all sprites | yes | yes |
| 5 | `WorldSpaceEntities` | interleaved with sprites by ZIndex vs `DrawDepth` | yes | yes |
| 6 | `WorldSpaceBelowFOV` | above sprites, then FOV black is painted over it | yes | yes |
| 7 | `WorldSpace` | after FOV, after `_lightingReady=false` | **no** | **no** |
| 8 | `ScreenSpaceBelowWorld` | UI-phase, screen coords, behind the viewport blit | no | no |
| 9 | `ScreenSpace` | UI-phase, screen coords, above the viewport | no | no |

Lighting is not a final blit: lit shaders sample the light RT per draw, and only while `_lightingReady` is true (spaces 2-6). "Lit" therefore means "uses the default canvas shader or any shader without `light_mode unshaded;`". Practical rules:

- **Glow / marker visible in the dark / ignores FOV => `WorldSpace`** (above lighting and FOV). Cheap, and right for HUD-like markers, debug overlays and beams (`KsLockOnIndicatorOverlay`, `KsLaserSightOverlay`, `SquadDebugOverlay`). It will also draw through walls and over everything, which is usually the point.
- To make something actually **light up its surroundings** (bloom, lights other things), use `BeforeLighting` and draw into the enlarged light target: client-lighting skill.
- **An ordinary in-world effect that should be dark in the dark, hidden by FOV, and sorted among sprites => `WorldSpaceEntities`** with an explicit ZIndex (`DrawDepth` value). With `null` it lands under every sprite.
- A sprite-like effect that should be dark but under everything entity-ish: `WorldSpaceBelowEntities`.
- An unshaded shader (`light_mode unshaded;`) in spaces 2-6 is not darkened but still gets covered by hard FOV (space 6) or not (5 is also covered since FOV paints after). Only `WorldSpace` escapes FOV.
- `BeforeLighting` overlays are skipped entirely when lighting is off for the eye/map, `!eye.DrawLight`, or nullspace.

WorldSpaceEntities interleave rule (`Clyde.HLR.cs` ~:327-375): each overlay draws before the first sprite whose `DrawDepth >= overlay.ZIndex`; overlays whose ZIndex exceeds every sprite draw after all sprites; `null` ZIndex draws before all sprites.

## 4. Drawing APIs (`DrawingHandleWorld`, units = metres, Y up)

| Call | Behaviour / trap |
| --- | --- |
| `DrawRect(Box2, Color, filled: true)` / `DrawRect(in Box2Rotated, ...)` | Filled = `DrawTextureRect` of the engine's 1x1 **white texture**, colour from vertex modulate. Unfilled = 4 `DrawLine`s (1 px). |
| `DrawLine(from, to, color)` | GL line list, **fixed ~1 px, no width, does not scale with zoom**. For thickness draw a filled rotated rect (`KsLaserSightMath.GetBeamBox`). |
| `DrawCircle(pos, radius, color, filled: true)` | `max(16, radius*16)` segments; filled circles issue a `DrawPrimitives` per segment, which breaks batching. Fine for a few. |
| `DrawTextureRect(tex, Box2 \| Box2Rotated, modulate)` | Preferred way to draw sprites/icons at a given size. |
| `DrawTexture(tex, pos[, angle], modulate)` | Size is `tex.Size / 32` metres (`EyeManager.PixelsPerMeter`). |
| `DrawRects(ReadOnlySpan<WorldRect>)` | Batched rects. |
| `DrawPrimitives(topology, verts, color)` | White texture, calls `FinishBatch()`: breaks batching. `DrawVertexUV2DColor` overloads **ignore Modulate and want linear colours**. |
| `SetTransform(in Matrix3x2)` | Applied to vertices on the CPU as they are queued; cheap to set per marker. |
| `UseShader(ShaderInstance?)` | `null` = default shader. Throws `ArgumentException` for a disposed instance. |
| `RenderInRenderTarget(target, Action, Color? clear)` | Flushes, binds `target`, **clears only if `clear != null`**. Inside the callback coordinates are the target's **pixel** space; draw world things after `SetTransform(target.GetWorldToLocalMatrix(eye, scale))`. Nestable inside Draw. |

The engine resets the model transform to identity and the shader to default **before and after every overlay**, but not between your own uses inside one `Draw`. Always `SetTransform(Matrix3x2.Identity)` and `UseShader(null)` after use. `DrawingHandleScreen` is the pixel-space equivalent (`DrawString`, `DrawTextureRect(UIBox2)`, ...).

### Shaders

- `ShaderPrototype.Instance()`: one shared, **immutable** instance per prototype. `SetParameter` on it throws. `InstanceUnique()`: a mutable duplicate you own.
- **Parameters are read at batch flush time, not at `Draw` time.** Draw commands store only the instance handle. Changing a parameter on the same instance between two draws in one flush makes all of them use the last value. Rule: **one `InstanceUnique()` per distinct parameter set drawn in the same flush** (dictionary keyed by entity or value; `EmissiveOverlay` keys by entity, `KsLaserSightEmissiveOverlay` by intensity). `RenderInRenderTarget` flushes, so parameters are safe to change between separate calls to it.
- Dispose unique instances yourself (`DisposeBehavior`, or prune unseen ones each frame as `EmissiveOverlay` does). Never dispose the shared `Instance()`. A disposed instance: `UseShader` throws `ArgumentException`, `SetParameter` throws `ObjectDisposedException`.
- Screen-reading shader overlays (`DrunkOverlay` etc.): `RequestScreenTexture => true`, then in `Draw`: `shader.SetParameter("SCREEN_TEXTURE", ScreenTexture); handle.UseShader(shader); handle.DrawRect(args.WorldBounds, Color.White); handle.UseShader(null);`. Gate in `BeforeDraw` so the copy is skipped when idle. The screen buffer is shared, it "does not consider viewports".
- Shader pixel coordinates from world positions: `args.Viewport.WorldToLocal(worldPos)` (viewport pixels, Y from top), as `SingularityOverlay` / `KsShockwaveOverlay` do. Do not use `IViewportControl.WorldToScreen`.
- `.swsl` header `light_mode unshaded;` makes a shader ignore lighting.

## 5. Frames, interpolation, directions

### Map frame vs grid frame

Render matrices, `args.WorldBounds` and `args.WorldAABB` are in the **map frame**. A handle with identity transform draws square to the map's axes. **Read CONTRIBUTING.md section 6, "World-space overlays draw square to the map, not the grid"** before drawing any shape with corners or a direction (rects, textures, primitives, offset-built lines, text placement). Summary of the rule: on a turned grid, anything built from `position + offset` or a `Box2` sits at a slant to the grid it marks; only circles, lone points and lines between two already-mapped points are safe. Draw in the grid's frame instead:

```csharp
// Content.Client/_KS14/NPC/SquadDebugOverlay.cs
private static Matrix3x2 GetMarkerTransform(Vector2 position, Angle gridRotation)
    => Matrix3Helpers.CreateTransform(position, gridRotation);

worldHandle.SetTransform(GetMarkerTransform(markerPosition, gridRotation));  // origin = marker, axes = grid's
worldHandle.DrawRect(Box2.CenteredAround(Vector2.Zero, size), color, filled: false);
worldHandle.SetTransform(Matrix3x2.Identity);
```

`position` is the marker's **map** position; `gridRotation` is the grid's world rotation (0 off-grid). Shapes are authored around the origin. For text, compute the screen position by pushing a point through the same marker matrix (`SquadDebugOverlay` ~:334). For whole grids/tiles use `SetTransform(_transformSystem.GetWorldMatrix(gridUid))` and grid-local coordinates (`TileEmissionOverlay`). For a rotated beam/quad that is intentionally map-aligned (a ray in a given direction), use `Box2Rotated` instead.

Common grid-local entity loop (`EmissiveOverlay`, `ChemicalFireOverlay`): `_mapSystem.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true)`; per grid `localBounds = _transformSystem.GetInvWorldMatrix(grid).TransformBox(bounds)`; `_lookupSystem.GetLocalEntitiesIntersecting(grid.Owner, localBounds, set)`. Entities not parented to a grid are missed by this loop (`KsShadowOverlay`, `DodgingEffectOverlay` accept that; `RoofOverlay` passes `includeMap: true`).

### Interpolation

Client `TransformSystem.FrameUpdate` writes lerped values straight into `TransformComponent`. There is no separate getter: after `FrameUpdate`, ordinary `GetWorldPosition`, `GetWorldPositionRotation`, `GetWorldMatrix`, `GetMapCoordinates` return smooth values, and entity `FrameUpdate` runs before rendering. So reading transforms in `BeforeDraw`/`Draw` is smooth. Values cached from a tick `Update` are **not** interpolated. Do not read `NextPosition`/`PrevPosition`. Whether a system's `FrameUpdate` runs after `TransformSystem.FrameUpdate` is UNVERIFIED; if exact smoothness matters, read transforms in `BeforeDraw`.

### Direction conventions (`Robust.Shared.Maths/Angle.cs`, `Direction.cs`)

World rotation zero faces **south** (0, -1). Two parallel families, easy to mix up:

| Pair | Zero points | Use for |
| --- | --- | --- |
| `Angle.ToWorldVec()` and `Vector2.ToWorldAngle()` (`Angle.FromWorldVec`) | south (0,-1) | entity facing, gun aim, beam directions. **Exact inverses of each other.** |
| `Angle.ToVec()` and `new Angle(vec)` | east (1,0) (plain cos/sin) | raw maths only. |

`ToWorldVec()` is `(this - PiOver2).ToVec()`; `FromWorldVec(v)` is `new Angle(v) + PiOver2`. The 180-degree bug pattern: aiming with `(target - pos).ToWorldAngle()` but converting back with `ToVec()` (or the reverse). Rule: **entity rotation <-> vector is always the `World` pair.** Positive angles turn the same way entity rotation does (counter-clockwise in the Y-up world frame). Helper: `Content.Shared/_KS14/LaserSight/KsLaserSightMath.cs` (`GetDirection`, `GetOrigin` via `RotateVec`, `GetBeamBox`: an unrotated quad runs -Y, matching rotation zero facing south).

**Pitfall (the laser 180-degree bug):** the maths was right and the drawn quad was wrong. The beam `Box2Rotated` was built from `Origin.Y` to `Origin.Y + Length` (+Y is north at rotation 0) and then rotated by the entity's rotation, so the quad pointed opposite to the ray. Rotation zero faces south, so **build "forward" shapes along -Y (or position them with `ToWorldVec()`) before rotating**. `KsLaserSightMath.GetBeamBox` does this, and `Content.IntegrationTests/Tests/_KS14/LaserSight/KsLaserSightMathTest.cs` pins it. Use the helper for anything beam-like and derive both the raycast and the drawn quad from the same call so they cannot disagree.

### Raycasts for beams

`SharedPhysicsSystem.IntersectRay(..., returnOnFirstHit: true)` returns the **first fixture the broadphase tree visits, not the nearest**: the query callback does `if (returnOnFirstHit && results.Count > 0) return true;` (`SharedPhysicsSystem.Queries.cs`, `IntersectRayWithPredicate`). The list is only sorted by distance at the end, so with `false` element 0 is the nearest. The beam then overshoots a closer obstacle by whatever the tree order allowed. For beams, sights and anything that wants the nearest hit, use `returnOnFirstHit: false` and take the minimum (`KsLaserSightOverlaySystem`). Distances are to the fixture proxy AABB ("TODO: Shape raycast"), so a non-box fixture hits slightly early.

## 6. Local player vs everyone, culling, screen-space

- **Local player only:** compare with `_playerManager.LocalEntity` (and for eye-based effects check that `args.Viewport.Eye` is the local player's eye, as `BlurryVisionOverlay` does). In the z-level build the eye is swapped per pass, so an eye check also keeps you out of passes you do not belong in.
- **Everyone:** enumerate in `BeforeDraw`, cull with `args.WorldAABB.Contains(position)` or `args.WorldBounds.Contains` and `MapId`. Cull by an extent, not only the origin, for long things (beams: test the segment).
- **Screen-space HUD indicators:** `ScreenSpace` with `DrawingHandleScreen`. Convert a world point with `args.ViewportControl` / `IEyeManager.WorldToScreen` for an on-screen position; screen-space text glyphs are upright but their start position must still be worked out in the grid frame (CONTRIBUTING section 6). Shader overlays use viewport-local pixels instead (section 4).
- `ScreenSpace` draws above the UI-phase viewport blit; it is not clipped by FOV or darkness.

## 7. Dependency injection and registration

Two patterns, do not mix them in one overlay:

1. **`IoCManager.InjectDependencies(this)` in the ctor.** Global IoC collection: only registered services (`IPrototypeManager`, `IClyde`, `IConfigurationManager`, `IEntityManager`, `IGameTiming`, `IEyeManager`, `IPlayerManager`, ...). Systems and `EntityQuery<T>` are **not** available; fetch with `entManager.System<T>()` / `GetEntityQuery<T>()` (`RoofOverlay`, `TileEmissionOverlay`). Used by the upstream light overlays.
2. **KS14 `SystemCollectionHookManager`** (preferred for new overlays). Gives the entity-system dependency collection, which also resolves `EntitySystem`s and `EntityQuery<T>`. The overlay declares `[Dependency]` fields (and is `partial`); its ctor does **not** call `IoCManager.InjectDependencies`.

```csharp
// Content.Client/_KS14/Emissive/EmissiveOverlaySystem.cs
public sealed partial class EmissiveOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new EmissiveOverlay();
        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<EmissiveOverlay>();   // add overlay.Dispose() if it owns targets/shaders
    }
}
```

`HookAction` runs immediately if the collection exists, otherwise it is queued and fired by the client `KsEntryPoint` on `PlayerJoinedServer`; `Reset()` clears hooks on shutdown. One hook may add several overlays (`KsLaserSightOverlaySystem`). Overlay's base already injects `OverlayManager`: use it, do not declare `IOverlayManager` in the overlay (the system above declares its own because `EntitySystem` has none).

**Lifetime:** CVar and event subscriptions made by an overlay are not auto-removed (no `Subs`). Hold the handler in a field and unsubscribe in `DisposeBehavior`, and dispose the overlay when its owning system shuts down (`RemoveOverlay` will not). Full rationale and code: CONTRIBUTING section 6, "CVars: unsubscribe from anything that does not live as long as the process". Overlays swapped per round otherwise leak with all their targets and buffers.

## 8. Sandbox

Rendering code is client/shared content and is type-checked at assembly load against `Sandbox.yml`. Do not use `stackalloc`, `Span<T>` over stack memory, `CollectionsMarshal`, `Unsafe`, `MemoryMarshal`, `"..."u8` literals, or non-empty collection expressions targeting `List<T>` (compiles to `CollectionsMarshal.SetCount`). Plain `Dictionary`, `Span` over arrays, `MathF`, `System.Numerics` `Vector2`/`Matrix3x2` are fine. Ordinary integration tests do **not** run the sandbox checker; run `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~SandboxTest"` whenever you use an API content has not used before (CONTRIBUTING section 6).

## 9. Existing examples

| Path (under `Content.Client/`) | Demonstrates |
| --- | --- |
| `_KS14/NPC/SquadDebugOverlay.cs` | `WorldSpace` markers in the grid frame (`GetMarkerTransform`), screen-space text, scratch shape arrays. |
| `_KS14/LaserSight/KsLaserSightOverlay.cs`, `KsLaserSightOverlaySystem.cs` | `WorldSpace` rotated-rect beam; segments resolved once per frame in system `FrameUpdate`, shared by two overlays; one hook adding two overlays. |
| `_KS14/LaserSight/KsLaserSightEmissiveOverlay.cs` | `BeforeLighting` glow into the enlarged light target; one unique shader per intensity. |
| `_KS14/LockOn/KsLockOnIndicatorOverlay.cs` | `WorldSpace` lock-on brackets plus one chevron per locking shooter in the grid frame (map-space direction rotated by minus the grid rotation), all gathered into reusable scratch in BeforeDraw. |
| `_KS14/Trail/KsTrailOverlay.cs` | `WorldSpaceEntities` with explicit `DrawDepth.Effects` ZIndex; `BeforeDraw` existence gating. |
| `_KS14/Emissive/EmissiveOverlay.cs`, `EmissiveOverlaySystem.cs` | Per-entity unique shaders with pruning, grid loop, `SystemCollectionHookManager` registration. |
| `_KS14/Explosion/Shockwave/KsShockwaveOverlay.cs`, `Singularity/SingularityOverlay.cs` | BeforeDraw computes viewport-local shader coordinates into arrays, Draw feeds the shader. |
| `_KS14/ShaderStatusEffect/KsShaderStatusEffectOverlay.cs`, `Drunk/DrunkOverlay.cs` | `RequestScreenTexture` full-screen shaders gated by BeforeDraw. |
| `_KS14/Atmos/ChemicalFire/ChemicalFireOverlay.cs` | `WorldSpaceEntities` + grid-local entity loop. |
| `Atmos/Overlays/GasTileHeatBlurOverlay.cs` | Per-viewport render targets via `OverlayResourceCache`, recreated when size changes. |
| `Light/*.cs`, `_KS14/ZLevel/Light/KsZLevelLightBufferOverlay.cs` | The `BeforeLighting` chain (client-lighting skill). |
