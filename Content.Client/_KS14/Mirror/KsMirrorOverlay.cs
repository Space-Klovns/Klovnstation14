using System.Numerics;
using System.Runtime.CompilerServices;
using Content.Client.Graphics;
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
using Content.Shared._KS14.Mirror;
using System.Linq;
//using CollectionExtensions = Robust.Shared.Utility.Extensions;

namespace Content.Client._KS14.Mirror;

/*
    СПАСИ МЕНЯ
*/

/// <summary>
///     Renders things reflecting off the ground.
/// </summary>
public sealed partial class KsMirrorOverlay : Overlay
{
    private readonly ShaderInstance _mirrorShader;
    private readonly ShaderInstance _whiteShader;
    private readonly ShaderInstance _stencilMaskShader;
    private readonly ShaderInstance _stencilDrawShader;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;

    [Dependency] private EntityQuery<KsMirrorReflectorComponent> _reflectorQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    private static readonly Vector2 Vector2Two = new(2f, 2f);
    private static readonly Vector2 Vector2Point5 = new(0.5f, 0.5f);
    private static readonly Angle Angle180Deg = Angle.FromDegrees(180d);

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    public const int OverlayZIndex = (int)Shared.DrawDepth.DrawDepth.HighFloorObjects; // right above puddles, under everything else
    private const LookupFlags OverlayLookupFlags = LookupFlags.Approximate | LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained;
    public static readonly Color DrawColor = new(1f, 1f, 1f, a: 0.5f);

    private readonly RefList<TransientReflectDatum> _transientReflectData = [];
    private readonly HashSet<Entity<SpriteComponent>> _reflectableEntities = [];
    private readonly HashSet<Entity<KsMirrorReflectorComponent>> _stencilEntities = [];

    /// <summary>
    ///     Cache of states and their offset, in metres. Populated asynchronously; see <see cref="FindFirstDistanceFromOccupiedRowFromBottom"/>.
    /// </summary>
    private readonly Dictionary<SpriteStateDatum, float> _textureSpriteOffsetCache = [];

    /// <summary>
    ///     States whose pixel readback is in flight, mapped to the <see cref="_drawCount"/> it was requested on - so we
    ///     don't queue the same readback every frame, and so one that never comes back doesn't wedge that state at zero.
    /// </summary>
    private readonly Dictionary<SpriteStateDatum, int> _pendingSpriteOffsetStates = [];

    private int _drawCount;
    private const int PendingReadbackRetryDraws = 60;
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

        _drawCount++;
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
            var (_, gridRotation, worldMatrix, invWorldMatrix) = _transformSystem.GetWorldPositionRotationMatrixWithInv(grid);
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
                // Identifies what the sprite currently looks like, so the measured bottom gap can be cached per
                // appearance. HashCode rather than XOR: XOR is order-insensitive and self-cancelling, so two layers
                // that hashed the same (very common - several layers off one RSI) used to annihilate each other.
                var animHashCode = new HashCode();
                foreach (var layer in spriteComponent.AllLayers)
                {
                    if (!layer.Visible)
                        continue;

                    pixelSize = Vector2i.ComponentMax(pixelSize, layer.PixelSize);

                    animHashCode.Add(layer.ActualRsi); // ActualRsi, not Rsi - the latter is null whenever the layer inherits the sprite's BaseRSI
                    animHashCode.Add(layer.RsiState.Name);
                    animHashCode.Add(layer.AnimationFrame);
                    animHashCode.Add(layer.Texture);
                    animHashCode.Add(layer.Rotation);
                    animHashCode.Add(layer.Scale);
                }

                var uid = entity.Owner;

                if (!mirrorTargetDict.TryGetValue(uid, out var mirrorTarget))
                {
                    mirrorTarget = _clyde.CreateRenderTarget(pixelSize, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "mirror-copy-target-" + uid.ToString());
                    mirrorTargetDict[uid] = mirrorTarget;
                }

                var worldEntRotation = gridRotation + transformComponent.LocalRotation;
                animHashCode.Add(worldEntRotation.GetDir());
                animHashCode.Add(spriteComponent.Scale);
                var animHash = animHashCode.ToHashCode();

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

                // How much empty space the sprite leaves at the bottom of its render target, in metres.
                var emptyBottomDistance = FindFirstDistanceFromOccupiedRowFromBottom(mirrorTarget, animHash);

                var position = transformComponent.LocalPosition;
                var size = (Vector2)pixelSize / EyeManager.PixelsPerMeter; // cast, otherwise this is an integer division and sprites that aren't a whole number of tiles get squashed
                var halfHeight = size.Y / 2f;

                // Everything below is in grid-local space and has to undo what the pipeline adds on the way to the
                // screen - the grid's matrix, then the eye's, i.e. localEyeRotation. Same idiom as EmissiveOverlay and
                // KsShadowOverlay, which counter-rotate by -localEyeRotation to keep a texture screen-aligned.
                var localEyeRotation = eyeRotation + gridRotation;
                var screenUp = (-localEyeRotation).RotateVec(Vector2.UnitY) /* grid-local direction that points up on screen */;

                // DrawEntity applies the eye rotation with the opposite sign to the real viewport (see the "Maaaaybe
                // this is meant to have a minus sign" in Clyde.RenderHandle.DrawEntity), so the sprite is baked into
                // the render target at worldRotation - eyeRotation while the real one is on screen at
                // worldRotation + eyeRotation. Add the eye rotation back twice to make up the difference.
                var eyeRotationCorrection = new Angle(eyeRotation.Theta * 2d);
                var quadRotation = Angle180Deg - localEyeRotation + eyeRotationCorrection;

                // Mirror around the sprite's bottom edge, raised by the empty space the sprite leaves below itself, so
                // the reflection looks mirrored from the true sprite with no blank space inbetween.
                var pivot = position + screenUp * (emptyBottomDistance - halfHeight);
                // Box2Rotated spins the whole AABB about the pivot, so the box has to sit where quadRotation will
                // throw it screen-below the pivot - which is the inverse of everything but the 180 degree flip.
                var bounds = Box2.CenteredAround(pivot + (-eyeRotationCorrection).RotateVec(new Vector2(0f, halfHeight)), size);

                ref var datum = ref _transientReflectData.AllocAdd();
                datum.Matrix = worldMatrix;
                datum.Texture = mirrorTarget.Texture;
                datum.Box = new Box2Rotated(bounds, quadRotation, pivot);
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

    /// <summary>
    ///     Measures how much empty space a sprite leaves below itself inside <paramref name="renderTexture"/>,
    ///     i.e. the gap between the lowest non-transparent pixel row and the bottom edge of the texture.
    ///     The reflection is mirrored around the bottom edge of that texture, so this gap has to be subtracted
    ///     from the reflection's position, otherwise the reflection floats away from the sprite's feet.
    /// </summary>
    /// <remarks>
    ///     Reading the pixels back from the GPU is expensive (indexing the texture directly costs ~68% of render
    ///     time, CopyPixelsToMemory ~30%), so results are cached per
    ///     <see cref="SpriteStateDatum"/> and looked up from a dictionary on subsequent frames.
    ///
    ///     Note that the readback is <b>asynchronous</b> on any GPU that supports PBOs + fence sync (i.e. nearly all
    ///     of them): the callback runs some frames later, off the back of Clyde's transfer queue. So the first few
    ///     frames a given state is seen we return 0 (no offset) and only fill the cache once the pixels actually
    ///     arrive. Reading a field that the callback writes into (as this used to do) just reads whatever unrelated
    ///     sprite's readback happened to finish last, which is why some sprites got a nonsense offset baked into the
    ///     cache forever.
    /// </remarks>
    /// <returns>The distance from the bottom of the texture to the lowest non-transparent row, in metres.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float FindFirstDistanceFromOccupiedRowFromBottom(IRenderTexture renderTexture, int animHash)
    {
        var renderTextureTextureSize = renderTexture.Texture.Size;
        var state = new SpriteStateDatum(renderTextureTextureSize, HashCode.Combine(renderTextureTextureSize, animHash));

        if (_textureSpriteOffsetCache.TryGetValue(state, out var cachedDistance))
            return cachedDistance;

        // Readback already queued for this state, don't queue a second one; 0 until it lands.
        if (_pendingSpriteOffsetStates.TryGetValue(state, out var queuedOnDraw) &&
            _drawCount - queuedOnDraw < PendingReadbackRetryDraws)
            return 0f;

        _pendingSpriteOffsetStates[state] = _drawCount;

        // The pixels are snapshotted now, but handed to us later - so everything the callback needs must be captured.
        renderTexture.CopyPixelsToMemory<Rgba32>(image =>
        {
            _pendingSpriteOffsetStates.Remove(state);
            _textureSpriteOffsetCache[state] = MeasureEmptyRowsBelowSprite(image);
            image.Dispose();
        });

        return 0f;
    }

    /// <returns>The number of fully transparent pixel rows below the sprite in <paramref name="image"/>, in metres.</returns>
    private static float MeasureEmptyRowsBelowSprite(Image<Rgba32> image)
    {
        var pixelSpan = image.GetPixelSpan();
        var width = image.Width;
        var height = image.Height;

        if (width <= 0 || height <= 0)
            return 0f;

        // Rows are stored top to bottom, so walk the buffer backwards to go bottom to top.
        for (var i = pixelSpan.Length - 1; i > -1; i--)
        {
            // Not bright enough to count as part of the sprite.
            if (pixelSpan[i].A <= 50)
                continue;

            var occupiedRowIndex = i / width;
            // -1 because a sprite sitting on the very last row (index height - 1) has nothing below it.
            return (float)(height - 1 - occupiedRowIndex) / EyeManager.PixelsPerMeter;
        }

        return 0f;
    }

    // dont remove, maybe used in future
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

    /// <param name="Hash">Some esoteric random hash combined with <see cref="Size"/>.</param>
    private readonly record struct SpriteStateDatum(Vector2i Size, int Hash) : IEquatable<SpriteStateDatum>
    {
        // Hash is already combined with Size
        public override int GetHashCode() => Hash;
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
