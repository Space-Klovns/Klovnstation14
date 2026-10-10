using System.Numerics;
using Content.Client.Light;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.LaserSight;

/// <summary>
///     Draws every laser sight into the light target with the <c>Emissive</c> shader, so it glows in darkness.
///         Follows <see cref="Content.Client._KS14.Emissive.EmissiveOverlay"/>.
/// </summary>
public sealed partial class KsLaserSightEmissiveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "Emissive";

    // Shares EmissiveOverlay's slot (+2): +3 tied with LightBlurOverlay (-8), and the overlay sort is unstable on
    //      ties, so the glow could be drawn after the blur. Strictly between Roof (-10) and LightBlur (-8).
    public const int ContentZIndex = BeforeLightTargetOverlay.ContentZIndex + 2;

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private KsLaserSightOverlaySystem _laserSightOverlaySystem = default!;

    /// <summary>
    ///     A shader parameter change would apply to the whole batch, so there is one instance per intensity.
    /// </summary>
    private readonly Dictionary<float, ShaderInstance> _shaders = [];

    private readonly KsLaserSightBeamDrawer _beamDrawer = new();

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    public KsLaserSightEmissiveOverlay()
    {
        ZIndex = ContentZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye != null && _laserSightOverlaySystem.HasSegmentsOn(args.MapId);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        var viewport = args.Viewport;

        var lightOverlay = _overlayManager.GetOverlay<BeforeLightTargetOverlay>();
        var bounds = lightOverlay.EnlargedBounds;
        var target = lightOverlay.GetCachedForViewport(viewport).EnlargedLightTarget;

        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var eye = viewport.Eye!;
        var mapId = args.MapId;

        var segments = _laserSightOverlaySystem.Segments;

        worldHandle.RenderInRenderTarget(target,
            () =>
            {
                worldHandle.SetTransform(target.GetWorldToLocalMatrix(eye, scale));

                for (var i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    if (segment.MapId != mapId)
                        continue;

                    var coreWidth = MathF.Max(segment.Width, 0.01f);
                    var drawGlow = segment.GlowWidth > coreWidth && segment.GlowAlpha > 0f;
                    var glowWidth = drawGlow ? segment.GlowWidth : coreWidth;
                    var glowBox = KsLaserSightOverlaySystem.GetBeamBox(segment, glowWidth);

                    if (!bounds.CalcBoundingBox().Intersects(glowBox.CalcBoundingBox()))
                        continue;

                    if (!_shaders.TryGetValue(segment.Intensity, out var shader))
                    {
                        shader = _prototypeManager.Index(Shader).InstanceUnique();
                        shader.SetParameter("bloom_intensity", segment.Intensity);
                        _shaders[segment.Intensity] = shader;
                    }

                    worldHandle.UseShader(shader);

                    if (drawGlow)
                        _beamDrawer.Draw(worldHandle, segment, glowWidth, segment.Color.WithAlpha(segment.Color.A * segment.GlowAlpha));

                    _beamDrawer.Draw(worldHandle, segment, coreWidth, segment.Color);
                }

                worldHandle.UseShader(null);
                worldHandle.SetTransform(Matrix3x2.Identity);
            },
            null);
    }

    protected override void DisposeBehavior()
    {
        foreach (var shader in _shaders.Values)
            shader.Dispose();

        _shaders.Clear();

        base.DisposeBehavior();
    }
}
