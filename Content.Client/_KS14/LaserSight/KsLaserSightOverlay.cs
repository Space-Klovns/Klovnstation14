using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._KS14.LaserSight;

/// <summary>
///     Draws the on-screen colour of every laser sight, unshaded, in world space. The glow in darkness comes from
///         <see cref="KsLaserSightEmissiveOverlay"/>.
/// </summary>
public sealed partial class KsLaserSightOverlay : Overlay
{
    [Dependency] private KsLaserSightOverlaySystem _laserSightOverlaySystem = default!;

    private readonly KsLaserSightBeamDrawer _beamDrawer = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _laserSightOverlaySystem.HasSegmentsOn(args.MapId);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        var worldBounds = args.WorldBounds;

        worldHandle.UseShader(null);

        var segments = _laserSightOverlaySystem.Segments;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.MapId != args.MapId)
                continue;

            var coreWidth = MathF.Max(segment.Width, 0.01f);

            var widest = KsLaserSightOverlaySystem.GetBeamBox(segment, MathF.Max(coreWidth, segment.GlowWidth));
            if (!worldBounds.CalcBoundingBox().Intersects(widest.CalcBoundingBox()))
                continue;

            if (segment.GlowWidth > coreWidth && segment.GlowAlpha > 0f)
            {
                var glowColor = segment.Color.WithAlpha(segment.Color.A * segment.GlowAlpha);
                _beamDrawer.Draw(worldHandle, segment, segment.GlowWidth, glowColor);
            }

            _beamDrawer.Draw(worldHandle, segment, coreWidth, segment.Color);
        }
    }
}
