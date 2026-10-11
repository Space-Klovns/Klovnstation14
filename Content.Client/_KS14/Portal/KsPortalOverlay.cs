using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Profiling;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Portal;

/// <summary>
///     Draws each captured portal view over the portal's window.
/// </summary>
/// <remarks>
///     World space, so it lands after lighting: the destination was lit in its own pass and must not be lit
///         a second time by the source side.
///     Draws nothing unless the viewport being drawn is the one the captures were made for and no portal
///         pre-pass is running; that is what stops a portal inside a portal view from showing a nested view.
///     The window (the portal's aperture plus its shadow from the viewer) is masked into the stencil buffer, the capture is drawn only where the stencil matches
///         (over the part of the main eye's world area the window touches, which is where its pixels belong),
///         and the stencil is cleared again.
/// </remarks>
public sealed partial class KsPortalOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ProfManager _prof = default!;

    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawShader = "StencilEqualDraw";
    private static readonly ProtoId<ShaderPrototype> StencilClearShader = "StencilClear";

    private readonly ShaderInstance _stencilMaskShader;
    private readonly ShaderInstance _stencilEqualDrawShader;
    private readonly ShaderInstance _stencilClearShader;

    private KsPortalRenderSystem? _portalSystem;

    public KsPortalOverlay()
    {
        IoCManager.InjectDependencies(this);

        _stencilMaskShader = _prototypeManager.Index(StencilMaskShader).Instance();
        _stencilEqualDrawShader = _prototypeManager.Index(StencilEqualDrawShader).Instance();
        _stencilClearShader = _prototypeManager.Index(StencilClearShader).Instance();
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

        worldHandle.SetTransform(Matrix3x2.Identity);

        foreach (var draw in draws)
        {
            // Z-level passes of other maps reuse this viewport; the window only exists on its own map.
            if (draw.MapId != args.MapId)
                continue;

            worldHandle.UseShader(_stencilMaskShader);
            worldHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, draw.Triangles, Color.White);

            worldHandle.UseShader(_stencilEqualDrawShader);
            worldHandle.DrawTextureRectRegion(draw.Texture, draw.Quad, subRegion: draw.SubRegion);

            worldHandle.UseShader(_stencilClearShader);
            worldHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, draw.Triangles, Color.White);
        }

        worldHandle.UseShader(null);
    }
}
