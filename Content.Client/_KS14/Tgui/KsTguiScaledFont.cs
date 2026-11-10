using System.Numerics;
using System.Text;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A font drawn at a fraction of the scale it is asked for, so that its size in pixels can be one the engine's whole
///         points can't make. The engine sizes fonts in whole points at 96 DPI, so 14px (10.5pt) isn't available, but
///         21pt at half the DPI is the same 14 pixels per em. Optionally reports a browser's line box instead of
///         FreeType's.
/// </summary>
/// <remarks>
///     FreeType rounds a font's ascent up and its descent down, so Verdana at 12px is 13 + 3 = 16px tall. A browser rounds
///         each to nearest, which makes tgui's line 15px, and that is the line every tgui control is sized around. The
///         difference is taken off the ascent, so the baseline moves up and no glyph is touched.
/// </remarks>
public sealed class KsTguiScaledFont : Font
{
    public readonly Font Inner;

    /// <summary>
    ///     What the scale is multiplied by on its way to <see cref="Inner"/>.
    /// </summary>
    public readonly float Factor;

    private readonly bool _browserLineBox;

    public KsTguiScaledFont(Font inner, float factor, bool browserLineBox)
    {
        Inner = inner;
        Factor = factor;
        _browserLineBox = browserLineBox;
    }

    /// <summary>
    ///     How many pixels the line box is shorter than the font's own height.
    /// </summary>
    private int Trim(float innerScale)
    {
        if (!_browserLineBox)
            return 0;

        var height = Inner.GetHeight(innerScale);
        var lineHeight = Inner.GetLineHeight(innerScale);
        return lineHeight > 0 && lineHeight < height ? height - lineHeight : 0;
    }

    public override int GetAscent(float scale)
    {
        var innerScale = scale * Factor;
        return Math.Max(0, Inner.GetAscent(innerScale) - Trim(innerScale));
    }

    public override int GetHeight(float scale)
    {
        var innerScale = scale * Factor;
        return Inner.GetHeight(innerScale) - Math.Min(Trim(innerScale), Inner.GetAscent(innerScale));
    }

    public override int GetDescent(float scale)
    {
        return Inner.GetDescent(scale * Factor);
    }

    public override int GetLineHeight(float scale)
    {
        return Inner.GetLineHeight(scale * Factor);
    }

    public override float DrawChar(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale, Color color, bool fallback = true)
    {
        return Inner.DrawChar(handle, rune, baseline, scale * Factor, color, fallback);
    }

    public override float DrawCharOutline(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale, TextOutline outline, bool fallback = true)
    {
        return Inner.DrawCharOutline(handle, rune, baseline, scale * Factor, outline with { Thickness = outline.Thickness / Factor }, fallback);
    }

    public override CharMetrics? GetCharMetrics(Rune rune, float scale, bool fallback = true)
    {
        return Inner.GetCharMetrics(rune, scale * Factor, fallback);
    }
}
