using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A tgui title bar: flat, with a thin dark line along the bottom and a soft shadow falling onto the window below.
/// </summary>
public sealed class KsTguiTitleBarStyleBox : StyleBox
{
    /// <summary>
    ///     tgui's <c>box-shadow: 0 0 6px -1px</c> measured under the bar on the window colour: it darkens the first five rows
    ///         by 5, 3, 3, 1 and 1 (of 255) and nothing after. These are the black alphas that darken #2B2B2B by that
    ///         much in this engine's linear blending, one per row.
    /// </summary>
    private static readonly float[] ShadowRows = [0.198f, 0.124f, 0.124f, 0.04f, 0.04f];

    private readonly Color _borderColor;

    public Color BackgroundColor;

    public KsTguiTitleBarStyleBox(Color backgroundColor)
    {
        BackgroundColor = backgroundColor;
        _borderColor = KsTguiPalettes.Translucent(Color.Black, 0.33f, backgroundColor);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        handle.DrawRect(box, BackgroundColor);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - uiScale, box.Right, box.Bottom), _borderColor);

        for (var row = 0; row < ShadowRows.Length; row++)
        {
            var top = box.Bottom + row * uiScale;
            handle.DrawRect(new UIBox2(box.Left, top, box.Right, top + uiScale), Color.Black.WithAlpha(ShadowRows[row]));
        }
    }
}
