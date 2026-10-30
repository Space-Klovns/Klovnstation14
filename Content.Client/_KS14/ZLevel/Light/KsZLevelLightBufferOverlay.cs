using System.Numerics;
using Content.Client.Light;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Light;
using Content.Shared._KS14.ZLevel.Transit;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
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

        var target = _overlayManager.GetOverlay<BeforeLightTargetOverlay>()
            .GetCachedForViewport(viewport)
            .EnlargedLightTarget;

        // Reported rather than worked out in the viewport because this is the only place holding both
        //      targets. Taken before the bail below, so that the first capture is already the right size
        //      rather than a frame of fade on arrival.
        _lightBufferSystem.CaptureOversize =
            (Vector2)target.Size / (Vector2)viewport.LightRenderTarget.Size;

        var hasLightFromAbove = TryGetLightFromAbove(mapUid, out var aboveCapture, out var aboveDim);
        var hasLightFromBelow = TryGetLightFromBelow(mapUid, out var belowCapture, out var belowDim);

        if (!hasLightFromAbove && !hasLightFromBelow)
            return;

        // The light target is not the viewport's resolution, so world-space geometry drawn into it needs the
        //      eye scale corrected by the difference. Same correction every light overlay makes.
        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var localMatrix = target.GetWorldToLocalMatrix(eye, scale);

        var debugFlat = _lightBufferSystem.DebugFlat;
        var shader = _shader;

        // Both can land on one pass - a platform partway up a shaft is lit by the floor it left and by the
        //      one it is heading for - and the shader is additive, so the two simply sum.
        if (hasLightFromAbove)
            Composite(aboveCapture, aboveDim, maskWeight: 1f);

        if (hasLightFromBelow)
            Composite(belowCapture, belowDim, maskWeight: 0f);

        // One shader instance serves both, because RenderInRenderTarget flushes the render queue around the
        //      action it is handed (Clyde.HLR.cs): each composite's uniforms are consumed before the next
        //      one sets its own.
        void Composite(KsZLevelLightCapture capture, float dim, float maskWeight)
        {
            // Drawn over the world area it covered when it was taken, which is what lines it up against a
            //      pass rendered at a different depth scale - the matrix above does the rest.
            var bounds = capture.WorldBounds;
            var lightTexture = capture.Light.Texture;

            shader.SetParameter("MASK_TEXTURE", capture.Colour.Texture);
            shader.SetParameter("dim", dim);
            shader.SetParameter("maskWeight", maskWeight);

            worldHandle.RenderInRenderTarget(
                target,
                () =>
                {
                    worldHandle.SetTransform(localMatrix);

                    // Proves the compositing on its own: if a flat wash does not brighten the z-level,
                    //      nothing about the masking or the attenuation is worth debugging yet.
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
    }

    /// <summary>
    ///     The captured light map of the z-level a gap map is crossing away from, and how much of it survives
    ///         the climb.
    /// </summary>
    /// <remarks>
    ///     Only a gap map is ever lit from below, and it is the one pass that has to be. A z-level is a floor
    ///         with lights standing on it; a gap is empty air with one grid floating in it, so the only thing
    ///         that can light that grid's underside is the room underneath it.
    ///     Without this the grid is not merely dim, it is invisible for as long as the viewer's FOV is on.
    ///         The engine strips light from an occluder's own tiles - fov-lighting.swsl samples the near face
    ///         of the depth map, unlike the hard FOV pass, which samples the far one - and hands it back by
    ///         bleeding light from the lit pixels beside it (Clyde.LightRendering.cs, BlurOntoWalls and
    ///         MergeWallLayer). A hull with nothing lit around it therefore bleeds black and disappears,
    ///         outer walls and all, while everything on an ordinary z-level has a lit floor next to it.
    /// </remarks>
    private bool TryGetLightFromBelow(EntityUid mapUid, out KsZLevelLightCapture capture, out float dim)
    {
        capture = default!;
        dim = 0f;

        if (!_entityManager.TryGetComponent<KsZLevelGapComponent>(mapUid, out var gapComponent))
            return false;

        if (!_entityManager.TryGetComponent<MapComponent>(gapComponent.LowerZLevel, out var anchorMapComponent))
            return false;

        if (!_lightBufferSystem!.TryGetCapture(anchorMapComponent.MapId, out capture))
            return false;

        // How far the grid has actually climbed, rather than its progress: the same real distance the
        //      downward leak attenuates over, so a platform just off the floor is bright and one near the
        //      ceiling is not.
        dim = KsZLevelLightLeak.GetHoleFactor(
            SharedKsZLevelGapSystem.GetPlaneAltitude((mapUid, gapComponent)) * _levelHeight,
            _referenceRadius,
            DefaultFalloff,
            DefaultCurveFactor
        ) * _energyMultiplier;

        return dim > 0f;
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
