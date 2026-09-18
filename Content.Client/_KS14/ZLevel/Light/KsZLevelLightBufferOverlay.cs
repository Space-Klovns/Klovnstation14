using System.Numerics;
using Content.Client.Light;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Light;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Light;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.ZLevel.Light;

/// <summary>
///     Composites the finished light map of the z-level above into the one being drawn, through the gaps in
///         the floor between them.
/// </summary>
/// <remarks>
///     Drawn into <see cref="BeforeLightTargetOverlay"/>'s enlarged target rather than straight into the
///         viewport's light target, because that enlarged copy is what actually reaches the screen: it is
///         blurred at one ZIndex above this and cropped over the real light target at the one above that.
///         Writing to the real target here would simply be overwritten a moment later.
///     Sitting just under the blur is deliberate and is half the effect - light coming through a gap should
///         arrive soft, and that is a pass the engine is already running.
/// </remarks>
public sealed partial class KsZLevelLightBufferOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private static readonly ProtoId<ShaderPrototype> BleedShader = "KsZLevelLightBleed";

    /// <summary>
    ///     The last thing to add light before the blur, so that what it adds gets blurred.
    /// </summary>
    /// <remarks>
    ///     This ties with <see cref="TileEmissionOverlay"/>, which is harmless: both only add light, and
    ///         addition does not care which order it happens in.
    /// </remarks>
    public const int ContentZIndex = LightBlurOverlay.ContentZIndex - 1;

    /// <summary>
    ///     <see cref="SharedPointLightComponent"/>'s own defaults, which have no named constants to borrow.
    ///     The overwhelming majority of station lights never override either.
    /// </summary>
    private const float DefaultFalloff = 6.8f;
    private const float DefaultCurveFactor = 0f;

    private readonly ShaderInstance _shader;

    private KsZLevelLightBufferSystem? _lightBufferSystem;
    private KsZLevelSystem? _zLevelSystem;

    private float _levelHeight;
    private float _referenceRadius;
    private float _energyMultiplier;

    public KsZLevelLightBufferOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ContentZIndex;

        // Unique because the dim and the mask differ per z-level pair, and a shared instance would have every
        //      pass fighting over the same uniforms.
        _shader = _prototypeManager.Index(BleedShader).InstanceUnique();

        // Named handlers rather than lambdas so they can be taken off again: this overlay is added and removed
        //      every time the mode changes, and an anonymous subscription would outlive each one of them.
        _configurationManager.OnValueChanged(KsCCVars.ZLevelLightLeakHeight, OnLevelHeightChanged, true);
        _configurationManager.OnValueChanged(KsCCVars.ZLevelLightBufferReferenceRadius, OnReferenceRadiusChanged, true);
        _configurationManager.OnValueChanged(KsCCVars.ZLevelLightLeakEnergy, OnEnergyMultiplierChanged, true);
    }

    private void OnLevelHeightChanged(float value) => _levelHeight = value;
    private void OnReferenceRadiusChanged(float value) => _referenceRadius = value;
    private void OnEnergyMultiplierChanged(float value) => _energyMultiplier = value;

    protected override void DisposeBehavior()
    {
        _configurationManager.UnsubValueChanged(KsCCVars.ZLevelLightLeakHeight, OnLevelHeightChanged);
        _configurationManager.UnsubValueChanged(KsCCVars.ZLevelLightBufferReferenceRadius, OnReferenceRadiusChanged);
        _configurationManager.UnsubValueChanged(KsCCVars.ZLevelLightLeakEnergy, OnEnergyMultiplierChanged);

        _shader.Dispose();

        base.DisposeBehavior();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye != null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Copied out of the ref struct before anything is captured in a lambda below.
        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        var mapUid = args.MapUid;

        if (viewport.Eye is not { } eye)
            return;

        _lightBufferSystem ??= _entityManager.System<KsZLevelLightBufferSystem>();
        _zLevelSystem ??= _entityManager.System<KsZLevelSystem>();

        if (!_lightBufferSystem.WantsCaptures)
            return;

        if (!TryGetLightFromAbove(mapUid, out var capture, out var dim))
            return;

        var target = _overlayManager.GetOverlay<BeforeLightTargetOverlay>()
            .GetCachedForViewport(viewport)
            .EnlargedLightTarget;

        // The light target is not the viewport's resolution, so world-space geometry drawn into it needs the
        //      eye scale corrected by the difference. Same correction every light overlay makes.
        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var localMatrix = target.GetWorldToLocalMatrix(eye, scale);

        // Drawn over the world area it covered when it was taken, which is what lines it up against a pass
        //      rendered at a different depth scale - the matrix above does the rest.
        var bounds = capture.WorldBounds;
        var debugFlat = _lightBufferSystem.DebugFlat;
        var lightTexture = capture.Light.Texture;
        var shader = _shader;

        shader.SetParameter("MASK_TEXTURE", capture.Colour.Texture);
        shader.SetParameter("dim", dim);

        worldHandle.RenderInRenderTarget(
            target,
            () =>
            {
                worldHandle.SetTransform(localMatrix);

                // Proves the compositing on its own: if a flat wash does not brighten the z-level, nothing
                //      about the masking or the attenuation is worth debugging yet.
                if (debugFlat)
                {
                    worldHandle.DrawRect(bounds, Color.Magenta.WithAlpha(0.35f));
                    return;
                }

                worldHandle.UseShader(shader);
                worldHandle.DrawTextureRect(lightTexture, bounds);
                worldHandle.UseShader(null);
            },
            null
        );
    }

    /// <summary>
    ///     The captured light map of the z-level directly above this one, and how much of it survives the drop.
    /// </summary>
    private bool TryGetLightFromAbove(EntityUid mapUid, out KsZLevelLightCapture capture, out float dim)
    {
        capture = default!;
        dim = 0f;

        if (!_zLevelSystem!.TryGetZLevelAbove(mapUid, out var aboveEntity))
            return false;

        if (!_entityManager.TryGetComponent<MapComponent>(aboveEntity.Value.Owner, out var aboveMapComponent))
            return false;

        if (!_lightBufferSystem!.TryGetCapture(aboveMapComponent.MapId, out capture))
            return false;

        // Asked rather than read off either z-level directly, because which of the two Depths is crossed on
        //      the way down is exactly the thing this gets wrong if you guess.
        if (!_zLevelSystem.TryGetDepthBelow(aboveEntity.Value.Owner, mapUid, out var depth))
            return false;

        // A light map is every light on that z-level already summed, so there is no one radius to attenuate
        //      against - the reference radius stands in for the lights that made it.
        dim = KsZLevelLightLeak.GetHoleFactor(
            depth * _levelHeight,
            _referenceRadius,
            DefaultFalloff,
            DefaultCurveFactor
        ) * _energyMultiplier;

        return dim > 0f;
    }
}
