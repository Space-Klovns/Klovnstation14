using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._KS14.Mirror;
using Content.Shared.Fluids.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Mirror;

public sealed class KsMirrorOverlay : Overlay
{
    private readonly ShaderInstance _mirrorShader;
    private readonly ShaderInstance _whiteShader;
    private readonly ShaderInstance _stencilMaskShader;
    private readonly ShaderInstance _stencilDrawShader;

    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;

    [Dependency] private readonly EntityQuery<PuddleComponent> _reflectorQuery = default!;
    [Dependency] private readonly EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    private static readonly Vector2 Vector2Two = new(2f, 2f);
    private static readonly Vector2 Vector2Point5 = new(0.5f, 0.5f);
    private static readonly Angle Angle180Deg = Angle.FromDegrees(180d);

    private readonly RefList<TransientReflectDatum> _transientReflectData = [];

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    public const int OverlayZIndex = (int)Shared.DrawDepth.DrawDepth.HighFloorObjects; // right above puddles, under everything else
    private const LookupFlags OverlayLookupFlags = LookupFlags.Approximate | LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained;
    public static readonly Color DrawColor = new(1f, 1f, 1f, a: 0.5f);

    private readonly HashSet<Entity<SpriteComponent>> _reflectableEntities = [];
    private readonly HashSet<Entity<PuddleComponent>> _stencilEntities = [];
    private List<Entity<MapGridComponent>> _grids = [];
    private List<(Entity<MapGridComponent> Entity, Box2 LocalAABB, Matrix3x2 WorldMatrix)> _gridCache = [];

    public KsMirrorOverlay(ShaderInstance mirrorShader, ShaderInstance whiteShader, ShaderInstance stencilMaskShader, ShaderInstance stencilDrawShader)
    {
        _mirrorShader = mirrorShader;
        _whiteShader = whiteShader;
        _stencilMaskShader = stencilMaskShader;
        _stencilDrawShader = stencilDrawShader;

        ZIndex = OverlayZIndex;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var viewport = args.Viewport;

        var res = _resources.GetForViewport(viewport, static _ => new CachedResources());
        var mirrorTargetDict = res.MirrorTargets;
        var target = viewport.RenderTarget;

        if (res.PuddleMonoTarget?.Texture.Size != target.Size)
        {
            res.PuddleMonoTarget?.Dispose();
            res.PuddleMonoTarget = _clyde.CreateRenderTarget(target.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "mirror-stencil-target");

            res.ReflectionTarget?.Dispose();
            res.ReflectionTarget = _clyde.CreateRenderTarget(target.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "mirror-reflection-target");
        }

        var worldHandle = args.WorldHandle;
        var renderHandle = args.RenderHandle;

        var eyeRotation = args.Viewport.Eye?.Rotation ?? new();
        var worldBounds = args.WorldBounds;

        _grids.Clear();
        _gridCache.Clear();
        _mapManager.FindGridsIntersecting(args.MapId, worldBounds, ref _grids, approx: true);

        if (_grids.Count == 0)
            return;

        var scale = viewport.RenderScale / (Vector2.One / (target.Size / (Vector2)viewport.Size));

        var transformQuery = _entityManager.TransformQuery;
        _transientReflectData.Clear();

        foreach (var grid in _grids)
        {
            var (_, _, worldMatrix, invWorldMatrix) = _transformSystem.GetWorldPositionRotationMatrixWithInv(grid);
            var gridBounds = invWorldMatrix.TransformBox(worldBounds) /* world bounds -> grid bounds */;

            _gridCache.Add((grid, gridBounds, worldMatrix));

            _reflectableEntities.Clear();
            _entityLookupSystem.GetLocalEntitiesIntersecting(grid.Owner, invWorldMatrix.TransformBox(worldBounds) /* world bounds -> grid bounds */, _reflectableEntities, flags: OverlayLookupFlags);
            if (_reflectableEntities.Count == 0)
                continue;

            foreach (var entity in _reflectableEntities)
            {
                if (_reflectorQuery.HasComponent(entity.Owner) ||
                    !transformQuery.TryGetComponent(entity.Owner, out var transformComponent))
                    continue;

                var spriteComponent = entity.Comp;
                var pixelSize = GetPixelSize(spriteComponent);
                if (pixelSize == Vector2i.Zero)
                    continue;

                var uid = entity.Owner;

                if (!mirrorTargetDict.TryGetValue(uid, out var mirrorTarget))
                {
                    mirrorTarget = _clyde.CreateRenderTarget(pixelSize, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "mirror-copy-target-" + uid.ToString());
                    mirrorTargetDict[uid] = mirrorTarget;
                }

                var worldMatrixRotation = worldMatrix.Rotation();
                worldHandle.RenderInRenderTarget(mirrorTarget,
                    () =>
                    {
                        renderHandle.DrawEntity(
                            uid,
                            pixelSize / Vector2Two,
                            spriteComponent.Scale,
                            worldMatrixRotation + transformComponent.LocalRotation,
                            eyeRotation: eyeRotation,
                            sprite: spriteComponent,
                            xform: transformComponent,
                            xformSystem: _transformSystem
                        );
                    }, Color.Transparent);

                var texture = mirrorTarget.Texture;
                // Scan for first empty row starting from bottom
                var firstEmptyRowIndex = FindFirstOccupiedRowFromBottom(texture);

                var sum = transformComponent.LocalPosition;
                var bounds = Box2.CenteredAround(
                    sum + new Vector2(0f, (float)firstEmptyRowIndex / EyeManager.PixelsPerMeter),
                    pixelSize / EyeManager.PixelsPerMeter
                );

                ref var datum = ref _transientReflectData.AllocAdd();
                datum.Matrix = worldMatrix;
                datum.Texture = texture;
                datum.Box = new Box2Rotated(bounds, Angle180Deg, new(bounds.Center.X, bounds.Bottom));
            }
        }

        if (_transientReflectData.Count == 0)
            return;

        var worldToScreenMatrix = viewport.RenderTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);

        // render puddles as stencil mask
        worldHandle.UseShader(_whiteShader);
        worldHandle.RenderInRenderTarget(res.PuddleMonoTarget!,
            () =>
            {
                foreach (var gridDatum in _gridCache)
                {
                    _stencilEntities.Clear();
                    _entityLookupSystem.GetLocalEntitiesIntersecting(gridDatum.Entity.Owner, gridDatum.LocalAABB, _stencilEntities);
                    if (_stencilEntities.Count == 0)
                        continue;

                    foreach (var entity in _stencilEntities)
                    {
                        if (!_spriteQuery.TryGetComponent(entity.Owner, out var spriteComponent))
                            continue;

                        worldHandle.SetTransform(gridDatum.WorldMatrix * worldToScreenMatrix);
                        var position = _entityManager.TransformQuery.GetComponent(entity.Owner).LocalPosition - Vector2Point5;
                        worldHandle.DrawTexture(GetLayerTexture(spriteComponent, (SpriteComponent.Layer)spriteComponent[0], Angle.Zero), position);
                    }
                }
            }, Color.Transparent);

        // render reflections as stencil target
        worldHandle.RenderInRenderTarget(res.ReflectionTarget!,
            () =>
            {
                worldHandle.UseShader(_mirrorShader);
                foreach (var datum in _transientReflectData)
                {
                    worldHandle.SetTransform(datum.Matrix * worldToScreenMatrix);
                    worldHandle.DrawTextureRect(datum.Texture, datum.Box, modulate: DrawColor);
                }
            }, Color.Transparent);

        // Time to draw everything
        worldHandle.SetTransform(Matrix3x2.Identity);

        worldHandle.UseShader(_stencilMaskShader);
        worldHandle.DrawTextureRect(res.PuddleMonoTarget!.Texture, worldBounds);

        worldHandle.UseShader(_stencilDrawShader);
        worldHandle.DrawTextureRect(res.ReflectionTarget!.Texture, worldBounds);

        worldHandle.UseShader(null);
    }

    /// <returns>The index (y-coordinate) of the first non-empty row, starting from the bottom.</returns>
    private static int FindFirstOccupiedRowFromBottom(Texture texture)
    {
        var width = texture.Size.X;
        var height = texture.Size.Y;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = texture[x, y];

                if (c.A <= 0.2f)
                    return y;
            }
        }

        return 0;
    }

    private static Vector2i GetPixelSize(SpriteComponent spriteComponent)
    {
        var pixelSize = Vector2i.Zero;
        foreach (var layer in spriteComponent.AllLayers)
        {
            if (!layer.Visible)
                continue;

            pixelSize = Vector2i.ComponentMax(pixelSize, layer.PixelSize);
        }

        return pixelSize;
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        base.DisposeBehavior();
    }

    private sealed class CachedResources : IDisposable
    {
        public IRenderTexture? PuddleMonoTarget = null;
        public IRenderTexture? ReflectionTarget = null;

        public Dictionary<EntityUid, IRenderTexture> MirrorTargets = [];

        public void Dispose()
        {
            PuddleMonoTarget?.Dispose();
            ReflectionTarget?.Dispose();

            foreach (var (_, target) in MirrorTargets)
                target.Dispose();

            MirrorTargets.Clear();
            MirrorTargets.TrimExcess();
        }
    }

    private record struct TransientReflectDatum(Matrix3x2 Matrix, Texture Texture, Box2Rotated Box);

    private Texture GetLayerTexture(SpriteComponent spriteComponent, SpriteComponent.Layer layer, Angle rotation)
    {
        var state = layer.ActualState;
        var dir = state == null ? RsiDirection.South : SpriteComponent.Layer.GetDirection(state.RsiDirections, rotation);

        Direction? overrideDirection = spriteComponent.EnableDirectionOverride ? spriteComponent.DirectionOverride : null;
        if (overrideDirection != null && state != null)
            dir = overrideDirection.Value.Convert(state.RsiDirections);

        dir = dir.OffsetRsiDir(layer.DirOffset);

        return state?.GetFrame(dir, layer.AnimationFrame) ?? layer.Texture ?? _spriteSystem.GetFallbackTexture();
    }
}
