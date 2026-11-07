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
///     The scale is applied to the whole subtree through the render transform and the child coordinate transform, so text,
///         boxes and sprites all shrink together. It's linear, so the host is moved by the caller to keep the pivot still.
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
    ///     Where the blur behind the tooltip comes from; null for none. <c>backdrop-filter</c> on tgui's tooltip.
    /// </summary>
    public KsTguiBackdropUIController? Backdrop { get; init; }

    private KsTguiBackdropBlur? _blur;

    /// <summary>
    ///     Lets go of the blur's render targets. Called when the host is done with.
    /// </summary>
    public void ReleaseBackdrop()
    {
        _blur?.Dispose();
        _blur = null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2)PixelSize * Scale;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var tooltipBox = new UIBox2(Vector2.Zero, size);
        var radius = Radius * UIScale * Scale;

        // CSS paints an outer shadow only outside the box, and the tooltip is see-through, so each layer is a ring around it.
        var offset = ShadowOffset * UIScale * Scale;
        var shadowBox = Inflate(UIBox2.FromDimensions(new Vector2(offset), size), ShadowSpread * UIScale * Scale);

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

        if (Backdrop != null)
        {
            _blur ??= new KsTguiBackdropBlur(Backdrop);
            var box = UIBox2.FromDimensions(args.Position, (Vector2)PixelSize * Scale);
            _blur.Draw(args.Handle, this, args.Position, box, PixelSize, Radius * UIScale * Scale, args.Modulate);
        }

        if (Scale >= 1f)
            return;

        // The handle scales what each control draws, and the coordinate transform where each one sits.
        var scale = Matrix3x2.CreateScale(Scale);
        args.CoordinateTransform = scale;
        args.Handle.DrawingHandleScreen.SetTransform(scale);
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
