using System.Numerics;
using Content.Client._KS14.Tgui.Backdrop;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Scales what it holds about the middle of its top edge, as tgui's Floating does to a menu opening below its button. A
///         control can't scale itself, so this stands between a popup and its contents.
/// </summary>
/// <remarks>
///     With <see cref="Backdrop"/> set it also blurs what is behind the menu, as tgui's <c>backdrop-filter</c> does, drawn
///         under the contents at the scale and fade the host has. A menu that is closing is held by
///         <see cref="KsTguiDropdownGhost"/>, in the popup layer, so it is in the tree throughout.
/// </remarks>
public sealed class KsTguiScaleHost : Control
{
    /// <summary>
    ///     <c>--dropdown-menu-border-radius</c> is <c>--border-radius-large</c>.
    /// </summary>
    public const float MenuRadius = 6f;

    public float Scale = 1f;

    /// <summary>
    ///     The corner radius of the backdrop, in virtual pixels.
    /// </summary>
    public float Radius { get; set; } = MenuRadius;

    /// <summary>
    ///     Scale about the middle of the bottom edge instead of the top, for a popup above its control: Floating's
    ///         <c>data-position="top"</c> has <c>transform-origin: bottom</c>.
    /// </summary>
    public bool PivotBottom { get; set; }

    /// <summary>
    ///     Where the blur comes from; null for none.
    /// </summary>
    public KsTguiBackdropUIController? Backdrop { get; set; }

    private KsTguiBackdropBlur? _blur;
    private readonly KsTguiBackdropBackground _background = new();
    private KsTguiScaledRaster? _raster;
    private bool _rasterized;

    /// <summary>
    ///     The menu panel's background, which the backdrop pass paints while it runs. For tests.
    /// </summary>
    public KsTguiBackdropBackground Background => _background;

    /// <summary>
    ///     Lets go of the blur's render targets. They are made again if the host is drawn with a blur.
    /// </summary>
    public void ReleaseBackdrop()
    {
        _blur?.Dispose();
        _blur = null;
        _background.Release();
        _raster?.Dispose();
        _raster = null;
        _rasterized = false;
    }

    /// <summary>
    ///     What stays still as it scales, as fractions of its size: the middle of the top edge, or of the bottom.
    /// </summary>
    public Vector2 Pivot => new(0.5f, PivotBottom ? 1f : 0f);

    /// <summary>
    ///     Whether the last frame drew the contents through the scaling texture, for tests.
    /// </summary>
    public bool Rasterized => _rasterized;

    protected override void PreRenderChildren(ref ControlRenderArguments args)
    {
        base.PreRenderChildren(ref args);

        _rasterized = false;

        if (Backdrop != null)
        {
            _blur ??= new KsTguiBackdropBlur(Backdrop);

            // Not args.Position: the engine leaves that unset for PreRenderChildren, so it is always the screen's corner.
            var position = GlobalPixelPosition;
            var box = KsTguiScaledRaster.ScaledBox(position, (Vector2)PixelSize, Pivot, Scale);
            var tint = _background.FindTint(this);
            var composited = _blur.Draw(args.Handle, this, position, box, PixelSize, Radius * UIScale * Scale,
                args.Modulate, tint: tint);

            // The pass paints the background, exactly, so the menu's panel doesn't.
            _background.Apply(composited && tint != null);
        }
        else
        {
            _background.Apply(composited: false);
        }

        if (Backdrop == null || !KsTguiScaledRaster.ShouldRasterize(Scale, args.Modulate.A))
            return;

        // While it opens or closes the contents are drawn scaled from a texture, about the pivot, at subpixel precision. The
        //      host stays where it will end up: scaling through the coordinate transform steps in whole pixels.
        _raster ??= new KsTguiScaledRaster(Backdrop);
        _raster.Draw(this, in args, GlobalPixelPosition, Pivot, Scale);
        _rasterized = true;
    }

    protected override void RenderChildOverride(ref ControlRenderArguments args, int childIndex, Vector2i position)
    {
        // Drawn through the texture already.
        if (!_rasterized)
            base.RenderChildOverride(ref args, childIndex, position);
    }
}
