using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The panel of a <c>TabContainer</c>, which also paints the dark bar behind the tabs. The container hands a panel only
///         the area below its tabs and offers no box for the row itself, so this reaches back up above that area.
/// </summary>
public sealed class KsTguiTabPanelStyleBox : StyleBox
{
    /// <summary>
    ///     The bar's colour, <c>--tabs-container-background</c>, which is the theme's Section.
    /// </summary>
    public Color BarColor = KsTguiPalettes.Section;

    /// <summary>
    ///     The height of the tab row, in virtual pixels.
    /// </summary>
    public float BarHeight;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        handle.DrawRect(new UIBox2(box.Left, box.Top - BarHeight * uiScale, box.Right, box.Top), BarColor);
    }
}
