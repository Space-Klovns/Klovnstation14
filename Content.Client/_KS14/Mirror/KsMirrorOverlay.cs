using System.Numerics;
using System.Runtime.CompilerServices;
using Content.Client.Graphics;
using Content.Shared.Fluids.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;
using Color = Robust.Shared.Maths.Color;
using Content.Shared.Atmos;
using Content.Shared._KS14.Mirror;
using System.Linq;
//using CollectionExtensions = Robust.Shared.Utility.Extensions;

namespace Content.Client._KS14.Mirror;

/*
    СПАСИ МЕНЯ
*/

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

    [Dependency] private readonly EntityQuery<KsMirrorReflectorComponent> _reflectorQuery = default!;
    [Dependency] private readonly EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    private static readonly Vector2 Vector2Two = new(2f, 2f);
    private static readonly Vector2 Vector2Point5 = new(0.5f, 0.5f);
    private static readonly Angle Angle180Deg = Angle.FromDegrees(180d);

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    public const int OverlayZIndex = (int)Shared.DrawDepth.DrawDepth.HighFloorObjects; // right above puddles, under everything else
    private const LookupFlags OverlayLookupFlags = LookupFlags.Approximate | LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained;
    public static readonly Color DrawColor = new(1f, 1f, 1f, a: 0.5f);

    private Image<Rgba32> _transientImage = null!;
    private readonly RefList<TransientReflectDatum> _transientReflectData = [];
    private readonly HashSet<Entity<SpriteComponent>> _reflectableEntities = [];
    private readonly HashSet<Entity<KsMirrorReflectorComponent>> _stencilEntities = [];
    /// <summary>
    ///     Cache of states and their offset.
    /// </summary>
    private readonly Dictionary<SpriteStateDatum, float> _textureSpriteOffsetCache = [];
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

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _entityManager.EntityQuery<KsMirrorReflectorComponent>().Any();
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
                var spriteComponent = entity.Comp;
                if (!spriteComponent.Visible ||
                    spriteComponent.DrawDepth < OverlayZIndex)
                    continue;

                if (_reflectorQuery.HasComponent(entity.Owner) ||
                    !transformQuery.TryGetComponent(entity.Owner, out var transformComponent))
                    continue;

                var pixelSize = Vector2i.Zero;
                var animHash = 0;
                foreach (var layer in spriteComponent.AllLayers)
                {
                    if (!layer.Visible)
                        continue;

                    pixelSize = Vector2i.ComponentMax(pixelSize, layer.PixelSize);
                    animHash ^= layer.AnimationFrame ^ layer.Rsi?.GetHashCode() ?? layer.RsiState.GetHashCode();
                }

                var uid = entity.Owner;

                if (!mirrorTargetDict.TryGetValue(uid, out var mirrorTarget))
                {
                    mirrorTarget = _clyde.CreateRenderTarget(pixelSize, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "mirror-copy-target-" + uid.ToString());
                    mirrorTargetDict[uid] = mirrorTarget;
                }

                var worldMatrixRotation = worldMatrix.Rotation();
                var worldEntRotation = worldMatrixRotation + transformComponent.LocalRotation;
                animHash ^= (int)worldEntRotation.GetDir();

                worldHandle.RenderInRenderTarget(mirrorTarget,
                    () =>
                    {
                        renderHandle.DrawEntity(
                            uid,
                            pixelSize / Vector2Two,
                            spriteComponent.Scale,
                            worldEntRotation,
                            eyeRotation: eyeRotation,
                            sprite: spriteComponent,
                            xform: transformComponent,
                            xformSystem: _transformSystem
                        );
                    }, Color.Transparent);

                // Scan for first empty row starting from bottom
                var firstEmptyRowIndex = FindFirstDistanceFromOccupiedRowFromBottom(mirrorTarget, animHash);

                var sum = transformComponent.LocalPosition;
                var bounds = Box2.CenteredAround(
                    sum + new Vector2(0f, (float)firstEmptyRowIndex / EyeManager.PixelsPerMeter),
                    pixelSize / EyeManager.PixelsPerMeter
                );

                ref var datum = ref _transientReflectData.AllocAdd();
                datum.Matrix = worldMatrix;
                datum.Texture = mirrorTarget.Texture;
                datum.Box = new Box2Rotated(bounds, Angle180Deg, new(bounds.Center.X, bounds.Bottom));
            }
        }

        if (_transientReflectData.Count == 0)
        {
            worldHandle.UseShader(null);
            return;
        }

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
        worldHandle.UseShader(null);
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

    /// <returns>The index (y-coordinate), div by PixelsPerMeter, of the first non-empty row, starting from the bottom.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float FindFirstDistanceFromOccupiedRowFromBottom(IRenderTexture renderTexture, int animHash)
    {
        // If i use normal index ([]) on texture it will use.. ~68% of cpu-time when rendering?
        // CopyPixelsToMemory isn't very good either
        // So, dict lookup here we go

        var state = new SpriteStateDatum(renderTexture.Texture.Size, animHash);
        if (!_textureSpriteOffsetCache.TryGetValue(state, out var cachedDist))
        {
            renderTexture.CopyPixelsToMemory<Rgba32>(image => _transientImage = image);
            if (_transientImage == null)
                return 0;

            var pixelSpan = _transientImage.GetPixelSpan();

            cachedDist = 0f; // 0 is the default
            var width = _transientImage.Width;
            var height = _transientImage.Height;

            Rgba32 rgba = default;
            // Iterate backwards; we are going bottom to top
            for (var i = pixelSpan.Length - 1; i > -1; i--)
            {
                pixelSpan[i].ToRgba32(ref rgba);

                // If bright enough, return the inverse y-coordinate (because we are iterating upwards, not downwards), and in metres
                if (rgba.A > 50)
                {
                    cachedDist = (float)(height - i / width) / EyeManager.PixelsPerMeter;
                    break;
                }
            }

            _textureSpriteOffsetCache[state] = cachedDist;
        }

        return cachedDist;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    private readonly record struct SpriteStateDatum(Vector2i Size, int Hash) : IEquatable<SpriteStateDatum>
    {
        public override int GetHashCode()
            => HashCode.Combine(Size, Hash);
    }

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
