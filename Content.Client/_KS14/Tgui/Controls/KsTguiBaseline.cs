using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     Where the first line of text sits in a table cell, for the baseline alignment CSS gives <c>td</c> (reset.scss) and so
///         tgui's LabeledList and Table. A cell is measured before it is arranged, so this reads font metrics instead of a
///         laid out child.
/// </summary>
internal static class KsTguiBaseline
{
    /// <summary>
    ///     The font a label draws with.
    /// </summary>
    public static Font FontOf(Label label)
    {
        if (label.FontOverride != null)
            return label.FontOverride;

        return label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var font)
            ? font
            : label.UserInterfaceManager.ThemeDefaults.LabelFont;
    }

    /// <summary>
    ///     The distance from the top of a line of this font to its baseline, in layout units.
    /// </summary>
    public static float Ascent(Font font, float uiScale)
    {
        return (float)font.GetAscent(uiScale) / uiScale;
    }

    public static float Height(Font font, float uiScale)
    {
        return (float)font.GetHeight(uiScale) / uiScale;
    }

    /// <summary>
    ///     The baseline of a cell's content, measured from its top. A label has its font's. A box takes its first visible
    ///         child's. Anything else (a button, an input) has its text in the middle, so the baseline is where a line of the
    ///         reference font would sit if centred in it.
    /// </summary>
    public static float Of(Control content, float height, Font referenceFont, float scale)
    {

        if (content is Label label)
            return Ascent(FontOf(label), scale);

        if (content is BoxContainer)
        {
            foreach (var child in content.Children)
            {
                if (child.Visible)
                    return Of(child, child.DesiredSize.Y, referenceFont, scale);
            }

            return 0f;
        }

        return height / 2f + Ascent(referenceFont, scale) - Height(referenceFont, scale) / 2f;
    }
}
