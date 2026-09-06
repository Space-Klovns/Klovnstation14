using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._KS14.Trail;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Trail;

/// <summary>
///     Draws every <see cref="KsTrailComponent"/> as a run of tiles along the entity's local
///         +Y axis, in its parent's frame. Trails asking for blur take a detour through a
///         render target so the engine's gaussian can chew on them.
/// </summary>
public sealed partial class KsTrailOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private KsTrailSystem _trailSystem = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public const int ConstZIndex = (int)Shared.DrawDepth.DrawDepth.Effects;

    private readonly OverlayResourceCache<OverlayResources> _resources = new();

    /// <summary>
    ///     Trails deferred to the blur pass, collected during the cheap pass so we only ever
    ///         touch a render target when something actually wants blurring.
    /// </summary>
    private readonly List<(KsTrailComponent TrailComponent, Matrix3x2 LocalMatrix, float? SourceDistance)> _blurredTrails = new();

    public KsTrailOverlay()
    {
        ZIndex = ConstZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return false;

        return _entityManager.EntityQueryEnumerator<KsTrailComponent>().MoveNext(out _, out _);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        var curTime = _gameTiming.CurTime;

        // OverlayDrawArgs is a ref struct, so nothing from it survives into the render target
        // lambda in the blur pass. Pull out what that pass needs while we still can.
        var worldBounds = args.WorldBounds;

        worldHandle.UseShader(null);
        _blurredTrails.Clear();

        var eqe = _entityManager.EntityQueryEnumerator<KsTrailComponent, TransformComponent>();
        while (eqe.MoveNext(out var trailComponent, out var transformComponent))
        {
            if (trailComponent.Color.A <= 0f || _trailSystem.GetDrawLength(trailComponent) <= 0)
                continue;

            var trailMatrix = GetTrailMatrix(transformComponent);
            var sourceDistance = GetSourceDistance(trailComponent, transformComponent);

            if (trailComponent.Blur > 0f)
            {
                _blurredTrails.Add((trailComponent, trailMatrix, sourceDistance));
                continue;
            }

            DrawTrail(worldHandle, trailComponent, trailMatrix, curTime, sourceDistance);
        }

        if (_blurredTrails.Count > 0)
            DrawBlurredTrails(viewport, worldHandle, worldBounds, curTime);

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }

    /// <summary>
    ///     Trail-local -> world. Baking the entity's rotation into the transform is what lets
    ///         <see cref="DrawingHandleWorld.DrawTextureRect"/>, which only takes an axis-aligned
    ///         box, draw rotated tiles. Going through the parent's matrix is what makes the trail
    ///         ride its grid instead of sitting in worldspace.
    /// </summary>
    private Matrix3x2 GetTrailMatrix(TransformComponent transformComponent)
    {
        var localMatrix = Matrix3Helpers.CreateTransform(transformComponent.LocalPosition, transformComponent.LocalRotation);

        if (!transformComponent.ParentUid.IsValid())
            return localMatrix;

        return Matrix3x2.Multiply(localMatrix, _transformSystem.GetWorldMatrix(transformComponent.ParentUid));
    }

    /// <summary>
    ///     How far along the trail axis the source currently is, or null when there is no source
    ///         left to ask. Reading the source's live transform is what keeps the reveal glued to
    ///         it - a clock-driven reveal drifts, because the source's own descent animation only
    ///         starts once its state reaches the client.
    /// </summary>
    private float? GetSourceDistance(KsTrailComponent trailComponent, TransformComponent trailTransformComponent)
    {
        if (trailComponent.SourceEntity is not { } sourceUid ||
            !_transformQuery.TryGetComponent(sourceUid, out var sourceTransformComponent))
            return null;

        // Both are expected to hang off the same parent, which makes a plain local-space delta
        // enough. Anything else has no meaningful axis to project onto.
        if (sourceTransformComponent.ParentUid != trailTransformComponent.ParentUid)
            return null;

        var delta = sourceTransformComponent.LocalPosition - trailTransformComponent.LocalPosition;
        var axis = trailTransformComponent.LocalRotation.RotateVec(Vector2.UnitY);

        return Vector2.Dot(delta, axis);
    }

    private void DrawTrail(
        DrawingHandleWorld worldHandle,
        KsTrailComponent trailComponent,
        Matrix3x2 matrix,
        TimeSpan curTime,
        float? sourceDistance)
    {
        worldHandle.SetTransform(matrix);

        var length = _trailSystem.GetDrawLength(trailComponent);
        for (var index = 1; index <= length; index++)
        {
            var alpha = _trailSystem.GetTileAlpha(trailComponent, index, curTime, sourceDistance);
            if (alpha <= 0f)
                continue;

            var spriteSpecifier = index == 1 && trailComponent.StartSprite != null
                ? trailComponent.StartSprite
                : trailComponent.Sprite;

            var texture = _spriteSystem.GetFrame(spriteSpecifier, curTime);

            // Native size, never stretched - the trail loops the sprite instead of resizing it.
            var worldSize = (Vector2)texture.Size / (float)EyeManager.PixelsPerMeter;
            var box = Box2.CenteredAround(_trailSystem.GetTileOffset(trailComponent, index), worldSize);

            worldHandle.DrawTextureRect(texture, box, trailComponent.Color.WithAlpha(alpha));
        }
    }

    private void DrawBlurredTrails(IClydeViewport viewport, DrawingHandleWorld worldHandle, Box2Rotated worldBounds, TimeSpan curTime)
    {
        var resources = _resources.GetForViewport(viewport, static _ => new());

        // BlurRenderTarget draws its quad at viewport.Size while setting the GL viewport from
        // target.Size, so these two get sized off the viewport, not off its render target.
        var targetSize = viewport.Size;
        if (resources.TrailTarget?.Size != targetSize)
        {
            resources.Dispose();
            resources.TrailTarget = _clyde.CreateRenderTarget(targetSize, new(RenderTargetColorFormat.Rgba8Srgb), name: "ks-trail-overlay");
            resources.BlurBuffer = _clyde.CreateRenderTarget(targetSize, new(RenderTargetColorFormat.Rgba8Srgb), name: "ks-trail-overlay-blur");
        }

        var trailTarget = resources.TrailTarget!;
        var blurBuffer = resources.BlurBuffer!;

        var scale = viewport.RenderScale / (Vector2.One / (trailTarget.Size / (Vector2)viewport.Size));
        var invMatrix = trailTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);

        foreach (var (trailComponent, localMatrix, sourceDistance) in _blurredTrails)
        {
            // World -> render target space. Without this the trail gets drawn in worldspace
            // inside a target that is in local space, i.e. nowhere near where it belongs.
            var targetMatrix = Matrix3x2.Multiply(localMatrix, invMatrix);

            worldHandle.RenderInRenderTarget(trailTarget,
                () => DrawTrail(worldHandle, trailComponent, targetMatrix, curTime, sourceDistance),
                Color.Transparent);

            _clyde.BlurRenderTarget(
                viewport,
                trailTarget,
                blurBuffer,
                viewport.Eye!,
                trailComponent.Blur / EyeManager.PixelsPerMeter * trailComponent.BlurScale
            );

            worldHandle.SetTransform(Matrix3x2.Identity);
            worldHandle.DrawTextureRect(trailTarget.Texture, worldBounds);
        }

        _blurredTrails.Clear();
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        base.DisposeBehavior();
    }

    private sealed class OverlayResources : IDisposable
    {
        public IRenderTexture? TrailTarget;
        public IRenderTexture? BlurBuffer;

        public void Dispose()
        {
            TrailTarget?.Dispose();
            TrailTarget = null;

            BlurBuffer?.Dispose();
            BlurBuffer = null;
        }
    }
}
