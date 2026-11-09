using Robust.Client.Graphics;

namespace Content.Client.Stylesheets.Fonts;

// Partial so the tgui theme can stand behind every BaseFont.GetFont caller without any of them changing.
public sealed partial class NotoFontFamilyStack
{
    /// <summary>
    ///     When set, <see cref="GetFont"/> returns this instead of a Noto font.
    /// </summary>
    public Func<int, FontKind, Font>? KsFontOverride;
}
