using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client.Graphics;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Light;

/// <summary>
/// Applies ambient-occlusion to the viewport.
/// </summary>
public sealed partial class AmbientOcclusionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawShader = "StencilEqualDraw";

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _cfgManager = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private List<Entity<MapGridComponent>> _cachedGrids = new();
    private readonly List<Entity<OccluderComponent, TransformComponent>> _cachedOccluders = new();
    private readonly List<Vector2> _aoVertices = new(4096);
    private readonly List<ushort> _aoIndices = new(6144);

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    public AmbientOcclusionOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = AfterLightTargetOverlay.ContentZIndex + 1;
        SubscribeCVars(); // KS14: cache cvars rather than reading them every frame
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        /*
         * tl;dr
         * - we draw a black square on each "ambient occlusion" entity.
         * - we blur this.
         * - We apply it to the viewport.
         *
         * We do this while ignoring lighting because it will wash out the actual effect.
         * In 3D ambient occlusion is more complicated due top having to calculate normals but in 2D
         * we don't have a concept of depth / corners necessarily.
         */

        var viewport = args.Viewport;
        var mapId = args.MapId;
        var worldBounds = args.WorldBounds;
        var worldAabb = args.WorldAABB; // KS14: for the item and structure lookup
        var worldHandle = args.WorldHandle;
        var color = Color; // KS14: Color.FromHex(_cfgManager.GetCVar(CCVars.AmbientOcclusionColor)) -> cached
        //var color = Color.Red;
        var target = viewport.RenderTarget;
        var aoTargetSize = GetAmbientOcclusionTargetSize(target.Size); // KS14: AO renders at reduced resolution
        var lightScale = aoTargetSize /* KS14: target.Size -> aoTargetSize */ / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var maps = _entManager.System<SharedMapSystem>();
        var lookups = _entManager.System<EntityLookupSystem>();
        var query = _entManager.System<OccluderSystem>();
        var xformSystem = _entManager.System<SharedTransformSystem>();
        var turfSystem = _entManager.System<TurfSystem>();
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();

        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());

        if (res.AOTarget?.Texture.Size != aoTargetSize /* KS14: target.Size -> aoTargetSize */)
        {
            res.AOTarget?.Dispose();
            res.AOTarget = _clyde.CreateRenderTarget(aoTargetSize /* KS14: target.Size -> aoTargetSize */, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "ambient-occlusion-target");
        }

        if (res.AOBlurBuffer?.Texture.Size != aoTargetSize /* KS14: target.Size -> aoTargetSize */)
        {
            res.AOBlurBuffer?.Dispose();
            res.AOBlurBuffer = _clyde.CreateRenderTarget(aoTargetSize /* KS14: target.Size -> aoTargetSize */, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "ambient-occlusion-blur-target");
        }

        // KS14: the tiles are drawn straight into the stencil below, so no target is needed for them
        /*
        if (res.AOStencilTarget?.Texture.Size != target.Size)
        {
            res.AOStencilTarget?.Dispose();
            res.AOStencilTarget = _clyde.CreateRenderTarget(target.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "ambient-occlusion-stencil-target");
        }
        */

        // Draw the texture data to the texture.
        args.WorldHandle.RenderInRenderTarget(res.AOTarget,
            () =>
            {
                worldHandle.UseShader(_proto.Index(UnshadedShader).Instance());
                worldHandle.SetTransform(Matrix3x2.Identity);
                var worldToTargetMatrix = res.AOTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);

                _cachedOccluders.Clear();
                query.QueryAabb(_cachedOccluders, mapId, worldBounds);

                foreach (var entry in _cachedOccluders)
                {
                    DebugTools.Assert(entry.Comp1.Enabled);
                    var matrix = xformSystem.GetWorldMatrix(entry.Comp2);
                    var localToTargetMatrix = Matrix3x2.Multiply(matrix, worldToTargetMatrix);
                    AppendAmbientOcclusionPolygon(worldHandle, entry.Comp1.Polygon, localToTargetMatrix);
                }

                FlushAmbientOcclusionPolygons(worldHandle);

                DrawEntityAmbientOcclusion(worldHandle, mapId, worldAabb, worldToTargetMatrix, viewport.Eye!.Rotation, xformSystem); // KS14: items and structures
            }, Color.Transparent);

        _clyde.BlurRenderTarget(viewport, res.AOTarget, res.AOBlurBuffer, viewport.Eye!, 14f);

        // KS14 start: the floor is drawn straight into the stencil, as cached rectangles per chunk, saving a
        //      full-resolution target, a pass, and a rectangle per tile per frame
        // Need to do stencilling after blur as it will nuke it.
        // Draw stencil for the grid so we don't draw in space.
        worldHandle.UseShader(_proto.Index(StencilMaskShader).Instance());

        var stencilSystem = _entManager.System<Content.Client._KS14.Light.KsAmbientOcclusionStencilSystem>();
        _cachedGrids.Clear();
        maps.FindGridsIntersecting(mapId, worldBounds, ref _cachedGrids);
        foreach (var grid in _cachedGrids)
        {
            var (_, _, gridWorldMatrix, gridInvWorldMatrix) = xformSystem.GetWorldPositionRotationMatrixWithInv(grid.Owner);
            worldHandle.SetTransform(gridWorldMatrix);
            stencilSystem.DrawFloor(worldHandle, grid, gridInvWorldMatrix.TransformBox(worldBounds));
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        // KS14 end

        // KS14: replaced by the block above
        /*
        args.WorldHandle.RenderInRenderTarget(res.AOStencilTarget,
            () =>
            {
                // Don't want lighting affecting it.
                worldHandle.UseShader(_proto.Index(UnshadedShader).Instance());

                _cachedGrids.Clear();
                maps.FindGridsIntersecting(mapId, worldBounds, ref _cachedGrids);
                foreach (var grid in _cachedGrids)
                {
                    var transform = xformSystem.GetWorldMatrix(grid.Owner);
                    var worldToTextureMatrix = Matrix3x2.Multiply(transform, invMatrix);
                    var tiles = maps.GetTilesEnumerator(grid.Owner, grid, worldBounds);
                    worldHandle.SetTransform(worldToTextureMatrix);
                    while (tiles.MoveNext(out var tileRef))
                    {
                        if (turfSystem.IsSpace(tileRef))
                            continue;

                        var bounds = lookups.GetLocalBounds(tileRef, grid.Comp.TileSize);
                        worldHandle.DrawRect(bounds, Color.White);
                    }
                }

            }, Color.Transparent);

        // Draw the stencil texture to depth buffer.
        worldHandle.UseShader(_proto.Index(StencilMaskShader).Instance());
        worldHandle.DrawTextureRect(res.AOStencilTarget!.Texture, worldBounds);
        */

        // Draw the Blurred AO texture finally.
        worldHandle.UseShader(_proto.Index(StencilEqualDrawShader).Instance());
        worldHandle.DrawTextureRect(res.AOTarget!.Texture, worldBounds, color);

        // KS14: after the composite, so the table pass can borrow the AO's targets, which are idle until the next frame
        DrawSurfaceAmbientOcclusion(worldHandle, viewport, res.AOTarget, res.AOBlurBuffer, scale, lookups);

        args.WorldHandle.SetTransform(Matrix3x2.Identity);
        args.WorldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        _surfaceResources.Dispose(); // KS14
        UnsubscribeCVars(); // KS14

        base.DisposeBehavior();
    }

    private void AppendAmbientOcclusionPolygon(
        DrawingHandleWorld worldHandle,
        ReadOnlySpan<Vector2> polygon,
        Matrix3x2 localToTargetMatrix)
    {
        if (polygon.Length < 3)
            return;

        // Keep indices representable as ushort for DrawingHandleBase.DrawPrimitives().
        if (_aoVertices.Count + polygon.Length > ushort.MaxValue)
            FlushAmbientOcclusionPolygons(worldHandle);

        var indexBase = (ushort) _aoVertices.Count;

        for (var i = 0; i < polygon.Length; i++)
        {
            _aoVertices.Add(Vector2.Transform(polygon[i], localToTargetMatrix));
        }

        for (var i = 1; i < polygon.Length - 1; i++)
        {
            _aoIndices.Add(indexBase);
            _aoIndices.Add((ushort) (indexBase + i));
            _aoIndices.Add((ushort) (indexBase + i + 1));
        }
    }

    private void FlushAmbientOcclusionPolygons(DrawingHandleWorld worldHandle)
    {
        if (_aoVertices.Count == 0)
            return;

        worldHandle.DrawPrimitives(
            DrawPrimitiveTopology.TriangleList,
            CollectionsMarshal.AsSpan(_aoIndices),
            CollectionsMarshal.AsSpan(_aoVertices),
            Color.White);

        _aoVertices.Clear();
        _aoIndices.Clear();
    }

    private sealed class CachedResources : IDisposable
    {
        public IRenderTexture? AOTarget;
        public IRenderTexture? AOBlurBuffer;

        // Couldn't figure out a way to avoid this so if you can then please do.
        /* public IRenderTexture? AOStencilTarget; */ // KS14: removed, the tiles are drawn straight into the stencil

        public void Dispose()
        {
            AOTarget?.Dispose();
            AOBlurBuffer?.Dispose();
            /* AOStencilTarget?.Dispose(); */ // KS14: removed with the field
        }
    }
}
