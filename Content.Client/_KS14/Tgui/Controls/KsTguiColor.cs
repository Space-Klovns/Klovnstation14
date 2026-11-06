namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     Every colour name tgui-core's components take (<c>colors.scss</c>'s <c>$color-map</c>), plus the button-only
///         ones. <see cref="Default"/> is tgui's primary blue.
/// </summary>
public enum KsTguiColor : byte
{
    Default,
    Caution,
    Danger,
    Transparent,
    Primary,
    Good,
    Average,
    Bad,
    Label,
    Red,
    Orange,
    Yellow,
    Olive,
    Green,
    Teal,
    Blue,
    Violet,
    Purple,
    Pink,
    Brown,
    Gold,
    Black,
    White,
    Grey,
    LightGrey,
}

/// <summary>
///     The shades tgui derives from a <see cref="KsTguiColor"/>: a Button's background is the token at lightness -5, hover
///         is +5 on the token, and pressed -12.5 (<c>vars-values.scss</c>: <c>--adjust-color</c>, <c>--adjust-hover</c>,
///         <c>--adjust-active</c>).
/// </summary>
public static class KsTguiColors
{
    public const int Count = (int)KsTguiColor.LightGrey + 1;

    private static readonly (float Hue, float Saturation, float Lightness)[] Tokens = MakeTokens();
    private static readonly string[] ButtonClasses = MakeButtonClasses();

    /// <summary>
    ///     The class a <see cref="KsTguiButton"/> of this colour carries, for the sheetlet's rules.
    /// </summary>
    public static string ButtonClass(KsTguiColor color)
    {
        return ButtonClasses[(int)color];
    }

    /// <summary>
    ///     A button, progress bar or tab's colour at rest.
    /// </summary>
    public static Color Element(KsTguiColor color)
    {
        return Shade(color, -5f);
    }

    public static Color Hovered(KsTguiColor color)
    {
        return Shade(color, 5f);
    }

    public static Color Pressed(KsTguiColor color)
    {
        return Shade(color, -12.5f);
    }

    /// <summary>
    ///     The element at CSS opacity .5 over the window, which is how tgui dims a disabled button.
    /// </summary>
    public static Color Disabled(KsTguiColor color)
    {
        var element = Element(color);
        var backdrop = KsTguiPalettes.Base;
        return new Color(
            (element.R + backdrop.R) / 2f,
            (element.G + backdrop.G) / 2f,
            (element.B + backdrop.B) / 2f);
    }

    /// <summary>
    ///     The text colour on a button of this colour: black on the low-contrast colours, white otherwise.
    /// </summary>
    public static Color Text(KsTguiColor color)
    {
        return color is KsTguiColor.Yellow or KsTguiColor.White or KsTguiColor.Caution ? Color.Black : Color.White;
    }

    /// <summary>
    ///     The selected tab's text colour: the token at lightness +17.5 (<c>Tabs.scss</c>).
    /// </summary>
    public static Color Bright(KsTguiColor color)
    {
        return Shade(color, 17.5f);
    }

    /// <summary>
    ///     The token with its HSL lightness moved by this many percent, as tgui's <c>hsl(from ... calc(l + n))</c>.
    /// </summary>
    public static Color Shade(KsTguiColor color, float lightnessShift)
    {
        var (hue, saturation, lightness) = Tokens[(int)color];
        return FromHsl(hue, saturation, Math.Clamp(lightness + lightnessShift, 0f, 100f));
    }

    /// <summary>
    ///     CSS's <c>hsl()</c>, with saturation and lightness in percent.
    /// </summary>
    public static Color FromHsl(float hue, float saturation, float lightness)
    {
        var s = saturation / 100f;
        var l = lightness / 100f;
        var chroma = (1f - MathF.Abs(2f * l - 1f)) * s;
        var h = hue / 60f % 6f;
        var x = chroma * (1f - MathF.Abs(h % 2f - 1f));
        var m = l - chroma / 2f;

        var (r, g, b) = (int)MathF.Floor(h) switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };

        return new Color(r + m, g + m, b + m);
    }

    private static (float, float, float)[] MakeTokens()
    {
        var tokens = new (float, float, float)[Count];
        tokens[(int)KsTguiColor.Default] = (210f, 37.5f, 45f);
        tokens[(int)KsTguiColor.Primary] = (210f, 37.5f, 45f);
        tokens[(int)KsTguiColor.Caution] = (50f, 97.5f, 50f);
        tokens[(int)KsTguiColor.Danger] = (0f, 70f, 50f);
        tokens[(int)KsTguiColor.Transparent] = (0f, 0f, 50f);
        tokens[(int)KsTguiColor.Good] = (95f, 62.5f, 40f);
        tokens[(int)KsTguiColor.Average] = (32.5f, 90f, 50f);
        tokens[(int)KsTguiColor.Bad] = (0f, 72.5f, 50f);
        tokens[(int)KsTguiColor.Label] = (210f, 17.5f, 57.5f);
        tokens[(int)KsTguiColor.Red] = (0f, 70f, 50f);
        tokens[(int)KsTguiColor.Orange] = (25f, 90f, 50f);
        tokens[(int)KsTguiColor.Yellow] = (50f, 97.5f, 50f);
        tokens[(int)KsTguiColor.Olive] = (70f, 75f, 45f);
        tokens[(int)KsTguiColor.Green] = (140f, 70f, 40f);
        tokens[(int)KsTguiColor.Teal] = (180f, 100f, 35f);
        tokens[(int)KsTguiColor.Blue] = (210f, 65f, 47.5f);
        tokens[(int)KsTguiColor.Violet] = (260f, 60f, 50f);
        tokens[(int)KsTguiColor.Purple] = (290f, 60f, 50f);
        tokens[(int)KsTguiColor.Pink] = (325f, 70f, 50f);
        tokens[(int)KsTguiColor.Brown] = (25f, 47.5f, 45f);
        tokens[(int)KsTguiColor.Gold] = (40f, 90f, 50f);
        tokens[(int)KsTguiColor.Black] = (0f, 0f, 0f);
        tokens[(int)KsTguiColor.White] = (0f, 0f, 100f);
        tokens[(int)KsTguiColor.Grey] = (0f, 0f, 50f);
        tokens[(int)KsTguiColor.LightGrey] = (0f, 0f, 66.6f);
        return tokens;
    }

    private static string[] MakeButtonClasses()
    {
        var classes = new string[Count];
        for (var i = 0; i < Count; i++)
            classes[i] = "KsTguiButton" + ((KsTguiColor)i).ToString();

        return classes;
    }
}
