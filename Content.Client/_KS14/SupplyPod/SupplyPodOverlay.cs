using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.SupplyPod;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.SupplyPod;

/*
    Please another overlay that just draws entities oh boy
*/

public sealed class SupplyPodOverlay : Overlay
{
    private readonly ShaderInstance _cutoutShader;

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    private const float Scale = 1f;
    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(Scale, Scale));

    public static readonly Vector2 HalfNegativeVector2PerPixel = new(-0.5f / EyeManager.PixelsPerMeter, -0.5f / EyeManager.PixelsPerMeter);

    public const int ConstZIndex = (int)Shared.DrawDepth.DrawDepth.LargeObjects; // Under ghosts and fire, above mostly everything else

    public SupplyPodOverlay(ShaderInstance cutoutShader)
    {
        _cutoutShader = cutoutShader;
        ZIndex = ConstZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _entityManager.EntityQuery<SupplyPodComponent>(includePaused: false).Any();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        worldHandle.SetTransform(Matrix3x2.Identity);

        var renderTarget = viewport.RenderTarget;
        var scale = viewport.RenderScale / (Vector2.One / (renderTarget.Size / (Vector2)viewport.Size));

        var invMatrix = renderTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);
        worldHandle.UseShader(_cutoutShader);

        // All ts does is
        var eqe = _entityManager.EntityQueryEnumerator<SupplyPodDoorDrawerComponent, TransformComponent>();
        while (eqe.MoveNext(out var supplyPodComponent, out var transformComponent))
        {
            if (!TryGetTexture(supplyPodComponent.DoorData, out var doorTexture) ||
                !TryGetTexture(supplyPodComponent.DecalData, out var decalTexture))
                continue;

            var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, Matrix3Helpers.CreateTranslation(_transformSystem.GetWorldPosition(transformComponent)));
            var worldMatrix = Matrix3x2.Multiply(Matrix3Helpers.CreateRotation(supplyPodComponent.Rotation), scaledWorld);
            // Apply the inverse matrix to transform to render target space. otherwise, we would be rendering in worldspace
            var renderTargetMatrix = Matrix3x2.Multiply(worldMatrix, invMatrix);
            worldHandle.SetTransform(renderTargetMatrix);

            var offset = HalfNegativeVector2PerPixel * doorTexture.Size;

            // If there are multiple supplypods with different size and non-similar door textures i think this gets fucked
            _cutoutShader.SetParameter("maskTexture", doorTexture);
            worldHandle.DrawTexture(decalTexture, offset);
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }

    private bool TryGetTexture(PrototypeLayerData? datum, [NotNullWhen(true)] out Texture? texture)
    {
        if (datum == null ||
            !TryGetLayerDatum(datum, out var rsi, out var state))
        {
            texture = null;
            return false;
        }

        texture = _spriteSystem.GetFrame(new SpriteSpecifier.Rsi(rsi.Path, state.ToString()!), _gameTiming.CurTime);
        return true;
    }

    private bool TryGetLayerDatum(PrototypeLayerData datum, [NotNullWhen(true)] out RSI? rsi, [NotNullWhen(true)] out RSI.State? state)
    {
        if (!_resourceCache.TryGetResource<RSIResource>(datum.RsiPath!, out var rsiResource) ||
            !rsiResource.RSI.TryGetState(datum.State, out state))
        {
            rsi = null;
            state = null;
            return false;
        }

        rsi = rsiResource.RSI;
        return true;
    }
}
