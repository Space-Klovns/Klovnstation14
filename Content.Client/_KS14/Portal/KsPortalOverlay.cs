using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Profiling;

namespace Content.Client._KS14.Portal;

/// <summary>
///     Draws each captured portal view over the portal's window.
/// </summary>
/// <remarks>
///     World space, so it lands after lighting: the destination was lit in its own pass and must not be lit
///         a second time by the source side.
///     Draws nothing unless the viewport being drawn is the one the captures were made for and no portal
///         pre-pass is running; that is what stops a portal inside a portal view from showing a nested view.
///     The window (the portal's aperture plus its shadow from the viewer) is drawn as its own textured
///         triangles, each vertex sampling the capture at its own place. That draws the window's shape exactly
///         without the stencil buffer, which the viewer's FOV pass has already written to by the time
///         world-space overlays run.
/// </remarks>
public sealed partial class KsPortalOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private ProfManager _prof = default!;

    private KsPortalRenderSystem? _portalSystem;

    public KsPortalOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _portalSystem ??= _entityManager.System<KsPortalRenderSystem>();

        return !KsPortalRenderSystem.PassActive &&
               _portalSystem.ActiveViewport == args.Viewport &&
               _portalSystem.ActiveDraws.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        using var _ = _prof.Group("KsPortal overlay");

        var worldHandle = args.WorldHandle;
        var draws = _portalSystem!.ActiveDraws;

        worldHandle.UseShader(null);
        worldHandle.SetTransform(Matrix3x2.Identity);

        foreach (var draw in draws)
        {
            // Z-level passes of other maps reuse this viewport; the window only exists on its own map.
            if (draw.MapId != args.MapId)
                continue;

            worldHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, draw.Texture, draw.Vertices);
        }
    }
}
