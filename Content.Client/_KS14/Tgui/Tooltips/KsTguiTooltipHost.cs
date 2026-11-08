using System.Numerics;
using Content.Client._KS14.Tgui.Backdrop;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Tooltips;

/// <summary>
///     Holds a window's tooltip in the popup layer: draws tg's shadow under it and scales it as it opens and closes, which
///         a control can't do to itself.
/// </summary>
/// <remarks>
///     While it opens and closes (scale or fade below 1) the tooltip is rendered at full size into a target and drawn scaled
///         about <see cref="Pivot"/> at subpixel precision (<see cref="KsTguiScaledRaster"/>), so nothing steps in whole
///         pixels as the scale changes. The host itself stays at its final, whole-pixel position throughout.
/// </remarks>
public sealed class KsTguiTooltipHost : Control
{
    /// <summary>
    ///     tgui-core's <c>box-shadow: 0.1em 0.1em 1.25em -0.1em hsla(0, 0%, 0%, 0.5)</c>, in px.
    /// </summary>
    private const float ShadowOffset = 1.2f;
    private const float ShadowBlur = 15f;
    private const float ShadowSpread = -1.2f;
    private const float ShadowAlpha = 0.5f;

    /// <summary>
    ///     tgui-core's <c>--tooltip-border-radius</c>.
    /// </summary>
    private const float Radius = 4f;

    private const int ShadowLayers = 12;

    private static readonly float[] LayerAlphas = BuildLayerAlphas();

    private readonly KsTguiStyleBox _shadowRing = new() { BackgroundColor = Color.Black.WithAlpha(0f) };

    public KsTguiTooltipHost(Control tooltip, Control target)
    {
        Target = target;
        Tooltip = tooltip;
        MouseFilter = MouseFilterMode.Ignore;
        AddChild(tooltip);
    }

    /// <summary>
    ///     The control whose tooltip this is.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    ///     The tooltip the engine made. The engine takes it out of the host when it hides it.
    /// </summary>
    public Control Tooltip { get; }

    public KsTguiTooltipPlacement Placement;

    /// <summary>
    ///     How far through opening, 0 to 1. Closing runs it back down.
    /// </summary>
    public float Progress;

    public bool Closing;

    public float Scale = 1f;

    /// <summary>
    ///     What stays still as it scales, as fractions of its size: the middle of the edge nearest the control it points at.
    ///         Set by the placement.
    /// </summary>
    public Vector2 Pivot = new(0.5f, 0f);

    /// <summary>
    ///     Where the blur behind the tooltip comes from; null for none. <c>backdrop-filter</c> on tgui's tooltip.
    /// </summary>
    public KsTguiBackdropUIController? Backdrop { get; init; }

    private KsTguiBackdropBlur? _blur;
    private readonly KsTguiBackdropBackground _background = new();
    private KsTguiScaledRaster? _raster;
    private bool _rasterized;

    /// <summary>
    ///     Whether the last frame drew the contents through the scaling texture, for tests.
    /// </summary>
    public bool Rasterized => _rasterized;

    /// <summary>
    ///     The tooltip panel's background, which the backdrop pass paints while it runs. For tests.
    /// </summary>
    public KsTguiBackdropBackground Background => _background;

    /// <summary>
    ///     Lets go of the blur's render targets. Called when the host is done with.
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

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2)PixelSize * Scale;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        // The host is at its final place, and the scaled tooltip is drawn about the pivot inside it.
        var pivotShift = KsTguiScaledRaster.PivotShift(PixelSize, Pivot, Scale);
        var tooltipBox = UIBox2.FromDimensions(pivotShift, size);
        var radius = Radius * UIScale * Scale;

        // CSS paints an outer shadow only outside the box, and the tooltip is see-through, so each layer is a ring around it.
        var offset = ShadowOffset * UIScale * Scale;
        var shadowBox = Inflate(UIBox2.FromDimensions(pivotShift + new Vector2(offset), size), ShadowSpread * UIScale * Scale);

        for (var i = 0; i < ShadowLayers; i++)
        {
            var extent = LayerExtent(i) * UIScale * Scale;
            var outer = Inflate(shadowBox, extent);

            var left = MathF.Max(0f, tooltipBox.Left - outer.Left);
            var top = MathF.Max(0f, tooltipBox.Top - outer.Top);
            var right = MathF.Max(0f, outer.Right - tooltipBox.Right);
            var bottom = MathF.Max(0f, outer.Bottom - tooltipBox.Bottom);
            if (left <= 0f && top <= 0f && right <= 0f && bottom <= 0f)
                continue;

            _shadowRing.BorderColor = Color.Black.WithAlpha(LayerAlphas[i]);
            _shadowRing.BorderThickness = new Thickness(left, top, right, bottom);
            _shadowRing.Radius = radius + MathF.Max(MathF.Max(left, top), MathF.Max(right, bottom));
            _shadowRing.Draw(handle, outer, 1f);
        }
    }

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
            var composited = _blur.Draw(args.Handle, this, position, box, PixelSize, Radius * UIScale * Scale, args.Modulate,
                tint: tint);

            // The pass paints the background, exactly, so the panel doesn't.
            _background.Apply(composited && tint != null);
        }

        if (Backdrop == null || !KsTguiScaledRaster.ShouldRasterize(Scale, args.Modulate.A))
            return;

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

    private static UIBox2 Inflate(UIBox2 box, float amount)
    {
        return new UIBox2(box.Left - amount, box.Top - amount, box.Right + amount, box.Bottom + amount);
    }

    /// <summary>
    ///     How far past the shadow's box layer <paramref name="index"/> reaches, in px. The layers end every
    ///         <c>3σ / n</c>, which is as far as a Gaussian blur shows.
    /// </summary>
    private static float LayerExtent(int index)
    {
        return 3f * (ShadowBlur / 2f) * (index + 1) / ShadowLayers;
    }

    /// <summary>
    ///     Alphas for the stacked rings, so that what shows at each distance is the blurred edge's: half the shadow's alpha at
    ///         the edge, falling as the normal distribution's tail. A point is covered by every layer that reaches past it.
    /// </summary>
    public static float[] BuildLayerAlphas()
    {
        var sigma = ShadowBlur / 2f;
        var alphas = new float[ShadowLayers];
        var outsideCoverage = 0f;
        for (var i = ShadowLayers - 1; i >= 0; i--)
        {
            // The band this layer is the outermost of, sampled at its middle.
            var inner = i == 0 ? 0f : LayerExtent(i - 1);
            var wanted = ShadowAlpha * (1f - NormalCdf((inner + LayerExtent(i)) / 2f / sigma));
            alphas[i] = Math.Clamp(1f - (1f - wanted) / (1f - outsideCoverage), 0f, 1f);
            outsideCoverage = wanted;
        }

        return alphas;
    }

    /// <summary>
    ///     Zelen and Severo's approximation, good to 1e-7, which is far below what 8-bit colour shows.
    /// </summary>
    private static float NormalCdf(float x)
    {
        var t = 1f / (1f + 0.2316419f * MathF.Abs(x));
        var density = 0.3989423f * MathF.Exp(-x * x / 2f);
        var tail = density * t * (0.3193815f + t * (-0.3565638f + t * (1.781478f + t * (-1.821256f + t * 1.330274f))));
        return x >= 0f ? 1f - tail : tail;
    }
}
