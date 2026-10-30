using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Emissive;

/// <summary>
///     Bloom for emissive layers: blurs the glow <see cref="EmissiveOverlay"/> drew this frame and adds it over the lit
///         world, so glowing things are brighter than their own colour and spread a halo, even in darkness. Draws
///         nothing of its own, so it costs no CPU work per entity.
/// </summary>
public sealed partial class KsEmissiveBloomOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "KsAdditive";

    // Below FOV, so the bloom is hidden with everything else out of view.
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private ShaderInstance? _shader;

    private bool _enabled;
    private float _strength;
    private float _radius;

    private Action<bool>? _enabledHandler;
    private Action<float>? _strengthHandler;
    private Action<float>? _radiusHandler;

    public void SubscribeCVars()
    {
        _enabledHandler = value => _enabled = value;
        _strengthHandler = value => _strength = value;
        _radiusHandler = value => _radius = value;
        _configurationManager.OnValueChanged(KsCCVars.EmissiveBloom, _enabledHandler, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.EmissiveBloomStrength, _strengthHandler, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.EmissiveBloomRadius, _radiusHandler, invokeImmediately: true);
    }

    /// <summary>
    ///     Whether bloom is drawn, so <see cref="EmissiveOverlay"/> fills the bloom target only when it is.
    /// </summary>
    public bool Active => _enabled && _strength > 0f;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Active && args.Viewport.Eye != null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!OverlayManager.TryGetOverlay<EmissiveOverlay>(out var emissiveOverlay)
            || !emissiveOverlay.TryGetGlow(args.Viewport, out var target, out var blurBuffer, out _))
            return;

        _clyde.BlurRenderTarget(args.Viewport, target, blurBuffer, args.Viewport.Eye!, _radius);

        _shader ??= _prototypeManager.Index(Shader).InstanceUnique();
        _shader.SetParameter("strength", _strength);

        // The target is the enlarged light target's size: the viewport's light target plus a fixed skirt of pixels,
        //      which isn't a fixed distance in the world. So, as AfterLightTargetOverlay does, crop the skirt off and
        //      stretch the middle over the viewport's own bounds, or the glow drifts from its source with zoom.
        var lightTargetSize = args.Viewport.LightRenderTarget.Size;
        var halfDiff = (target.Size - lightTargetSize) / 2;
        var subRegion = new UIBox2i(halfDiff.X, halfDiff.Y, lightTargetSize.X + halfDiff.X, lightTargetSize.Y + halfDiff.Y);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        worldHandle.DrawTextureRectRegion(target.Texture, args.WorldBounds, subRegion: subRegion);

        worldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        if (_enabledHandler != null)
            _configurationManager.UnsubValueChanged(KsCCVars.EmissiveBloom, _enabledHandler);
        if (_strengthHandler != null)
            _configurationManager.UnsubValueChanged(KsCCVars.EmissiveBloomStrength, _strengthHandler);
        if (_radiusHandler != null)
            _configurationManager.UnsubValueChanged(KsCCVars.EmissiveBloomRadius, _radiusHandler);

        _shader?.Dispose();
        base.DisposeBehavior();
    }
}
