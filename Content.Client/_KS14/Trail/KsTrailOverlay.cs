using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.Trail;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;

namespace Content.Client._KS14.Trail;

public sealed class KsTrailOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public const int ConstZIndex = (int)Shared.DrawDepth.DrawDepth.Effects;
    private const float Scale = 1f;
    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(Scale, Scale));
    public static readonly Vector2 HalfNegativeVector2PerPixel = new(-0.5f / EyeManager.PixelsPerMeter, -0.5f / EyeManager.PixelsPerMeter);

    private OverlayResourceCache<OverlayResources> _resources = new();
    private readonly List<(SupplyPodDoorDrawerComponent, Matrix3x2)> _drawDataCache = new();

    public KsTrailOverlay(ShaderInstance stencilMaskShader, ShaderInstance stencilDrawShader)
    {
        ZIndex = ConstZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _entityManager.EntityQuery<SupplyPodDoorDrawerComponent>(includePaused: false).Any();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        worldHandle.SetTransform(Matrix3x2.Identity);

        var renderTarget = viewport.RenderTarget;

        var resources = _resources.GetForViewport(viewport, static _ => new());
        var targetSize = viewport.RenderTarget.Size;
        if (resources.MaskTarget?.Size != targetSize)
        {
            resources.MaskTarget?.Dispose();
            resources.MaskTarget = _clyde.CreateRenderTarget(targetSize, new(RenderTargetColorFormat.Rgba8Srgb), name: "supplypod-door-overlay-mask");
        }

        var scale = viewport.RenderScale / (Vector2.One / (renderTarget.Size / (Vector2)viewport.Size));
        var rotationMatrix = Matrix3Helpers.CreateRotation(-viewport.Eye?.Rotation ?? Angle.Zero);

        var invMatrix = renderTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);

        var eqe = _entityManager.EntityQueryEnumerator<KsTrailComponent, TransformComponent>();
        while (eqe.MoveNext(out var trailComponent, out var transformComponent))
        {
            var invGridMatrix = _transformSystem.GetWorldMatrix(transformComponent);

            // var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, Matrix3Helpers.CreateTranslation(_transformSystem.GetWorldPosition(transformComponent)));
            // var worldMatrix = Matrix3x2.Multiply(rotationMatrix, scaledWorld);
            // // Apply the inverse matrix to transform to render target space. otherwise, we would be rendering in worldspace
            // var renderTargetMatrix = Matrix3x2.Multiply(worldMatrix, invMatrix);
            // worldHandle.SetTransform(invGridMatrix);

            // var offset = HalfNegativeVector2PerPixel * doorTexture.Size;
            // worldHandle.DrawTexture(doorTexture, offset, modulate: Color.Black);

            // _drawDataCache.Add((supplyPodComponent, invGridMatrix));
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }

    private bool TryGetTexture(PrototypeLayerData datum, [NotNullWhen(true)] out Texture? texture)
    {
        if (datum == null ||
            !TryGetLayerDatumState(datum, out var state))
        {
            texture = null;
            return false;
        }

        texture = state.Frame0;
        return true;
    }

    private bool TryGetLayerDatumState(PrototypeLayerData datum, [NotNullWhen(true)] out RSI.State? state)
    {
        if (!_resourceCache.TryGetResource<RSIResource>(datum.RsiPath!, out var rsiResource) ||
            !rsiResource.RSI.TryGetState(datum.State, out state))
        {
            state = null;
            return false;
        }

        return true;
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        base.DisposeBehavior();
    }

    private sealed class OverlayResources : IDisposable
    {
        public IRenderTexture? MaskTarget;

        public void Dispose()
        {
            MaskTarget?.Dispose();
        }
    }
}
