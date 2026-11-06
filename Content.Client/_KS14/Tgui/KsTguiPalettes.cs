using Content.Client.Stylesheets.Palette;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tgui's colours, in the shape upstream's sheetlets read them, so those sheetlets come out tgui without being restyled
///         one by one. Values are tgui-core's HSL tokens flattened to hex: Element is a token at lightness -5, Hovered at
///         +5, Pressed at -12.5 (<c>tgui-core/styles/colors.scss</c>, <c>--adjust-hover</c>, <c>--adjust-active</c>).
/// </summary>
public static class KsTguiPalettes
{
    public static readonly Color Base = Color.FromHex("#262626");
    public static readonly Color TitleBar = Color.FromHex("#393939");
    public static readonly Color Section = Translucent(Color.Black, 0.33f, Base);

    /// <summary>
    ///     tgui's <c>--candystripe-odd</c>, black at CSS opacity .25, over a Section body (the window darkened .33).
    /// </summary>
    public static readonly Color Candystripe = Translucent(Color.Black, 0.25f, new Color(Base.R * 0.67f, Base.G * 0.67f, Base.B * 0.67f));

    public static readonly Color Label = Color.FromHex("#8F9FB0");
    public static readonly Color Good = Color.FromHex("#67BA2B");
    public static readonly Color Average = Color.FromHex("#F49525");
    public static readonly Color Bad = Color.FromHex("#DF3939");

    /// <summary>
    ///     tgui's primary, and the blue under section titles.
    /// </summary>
    public static readonly Color PrimaryBase = Color.FromHex("#48739E");

    public static readonly Color PrimaryElement = Color.FromHex("#40668C");
    public static readonly Color SelectedElement = Color.FromHex("#1B9844");
    public static readonly Color SelectedHovered = Color.FromHex("#22C358");

    /// <summary>
    ///     The grey behind tgui's hover and selected washes.
    /// </summary>
    public static readonly Color Wash = Color.FromHex("#808080");

    /// <summary>
    ///     The input border colour, and the number colour of a NumberInput.
    /// </summary>
    public static readonly Color InputBorder = Color.FromHex("#80BFFF");

    public static readonly Color Notice = Color.FromHex("#9D7D49");
    public static readonly Color NoticeGood = Color.FromHex("#278440");
    public static readonly Color NoticeBad = Color.FromHex("#A22824");
    public static readonly Color NoticeInfo = Color.FromHex("#275D95");

    /// <summary>
    ///     White at CSS opacity .5 over the window, flattened. Text can be solid, so it needn't be worked out per surface.
    /// </summary>
    public static readonly Color TextDim = Color.FromHex("#929292");

    /// <summary>
    ///     White at CSS opacity .75 over the title bar, flattened.
    /// </summary>
    public static readonly Color TextTitle = Color.FromHex("#CECECE");

    /// <summary>
    ///     tgui's <c>--color-border</c> and Divider: white at CSS opacity .1.
    /// </summary>
    public static readonly Color Border = Translucent(Color.White, 0.1f, Base);

    public static readonly ColorPalette Primary = new(
        Base: PrimaryBase,
        LightnessShift: 0f,
        ChromaShift: 0f,
        Element: PrimaryElement,
        HoveredElement: Color.FromHex("#507FAF"),
        PressedElement: Color.FromHex("#345372"),
        DisabledElement: Color.FromHex("#334659"),
        Background: Base,
        BackgroundLight: TitleBar,
        BackgroundDark: Section,
        Text: Color.White,
        TextDark: TextDim);

    public static readonly ColorPalette Secondary = new(
        Base: TitleBar,
        LightnessShift: 0f,
        ChromaShift: 0f,
        Element: TitleBar,
        HoveredElement: Translucent(Wash, 0.25f, Base),
        PressedElement: Translucent(Wash, 0.3f, Base),
        DisabledElement: Color.FromHex("#303030"),
        Background: Base,
        BackgroundLight: TitleBar,
        BackgroundDark: Section,
        Text: Color.White,
        TextDark: Border);

    public static readonly ColorPalette Positive = new(
        Base: Color.FromHex("#5BA626"),
        LightnessShift: 0f,
        ChromaShift: 0f,
        Element: SelectedElement,
        HoveredElement: SelectedHovered,
        PressedElement: Color.FromHex("#157736"),
        DisabledElement: Color.FromHex("#205F35"),
        Background: NoticeGood,
        BackgroundLight: NoticeGood,
        BackgroundDark: NoticeGood,
        Text: Good,
        TextDark: Color.FromHex("#509121"));

    public static readonly ColorPalette Negative = new(
        Base: Color.FromHex("#DC2323"),
        LightnessShift: 0f,
        ChromaShift: 0f,
        Element: Color.FromHex("#C32222"),
        HoveredElement: Color.FromHex("#DD3C3C"),
        PressedElement: Color.FromHex("#A31D1D"),
        DisabledElement: Color.FromHex("#742424"),
        Background: NoticeBad,
        BackgroundLight: NoticeBad,
        BackgroundDark: NoticeBad,
        Text: Bad,
        TextDark: Color.FromHex("#C62020"));

    public static readonly ColorPalette Highlight = Primary with
    {
        TextDark = Label,
    };

    /// <summary>
    ///     The colour to draw with to get what CSS gets from <paramref name="color"/> at this opacity. A browser blends in sRGB
    ///         and this engine in linear light, so the same alpha comes out quite differently: white at .1 over the window
    ///         should be <c>#3C3C3C</c> and comes out about <c>#616161</c>. This works out the colour CSS would arrive at
    ///         and the alpha that reaches it here, which holds over this backdrop and is near enough on others.
    /// </summary>
    public static Color Translucent(Color color, float cssOpacity, Color backdrop)
    {
        var overlay = Color.FromSrgb(color).G;
        var under = Color.FromSrgb(backdrop).G;
        var wanted = Color.FromSrgb(new Color(0f, color.G * cssOpacity + backdrop.G * (1f - cssOpacity), 0f)).G;

        return color.WithAlpha(Math.Clamp((wanted - under) / (overlay - under), 0f, 1f));
    }
}
