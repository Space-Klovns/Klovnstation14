using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's ColorBox: a 1em (12px) square of <see cref="BoxColor"/>, used inline as a swatch.
/// </summary>
public sealed class KsTguiColorBox : Control
{
    private const float SwatchSize = 12f;

    /// <summary>
    ///     The swatch colour.
    /// </summary>
    public Color BoxColor { get; set; } = Color.White;

    public KsTguiColorBox()
    {
        VerticalAlignment = VAlignment.Center;
        HorizontalAlignment = HAlignment.Left;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(SwatchSize, SwatchSize);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, BoxColor);
    }
}
