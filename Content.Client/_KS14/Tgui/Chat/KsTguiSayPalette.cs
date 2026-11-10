using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Chat;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     The colours of tgui-say (<c>tgui-say/styles/colors.scss</c>): the channel buttons, the window around the bar and its
///         text each take their channel's colour, and the button its two greys.
/// </summary>
public static class KsTguiSayPalette
{
    public static readonly Color Button = KsTguiColors.FromHsl(0f, 0f, 12.2f);
    public static readonly Color ButtonHovered = KsTguiColors.FromHsl(0f, 0f, 22.2f);

    /// <summary>
    ///     The bar's inner background, which is black however the channel is coloured.
    /// </summary>
    public static readonly Color Content = Color.Black;

    // tgui's 'Say', 'Me', 'Radio', 'OOC', 'Admin', and so on, by name. The channels it lacks borrow its closest.
    private static readonly Color Say = KsTguiColors.FromHsl(213.6f, 37.9f, 74.1f);
    private static readonly Color Whisper = KsTguiColors.FromHsl(213.6f, 25f, 56f);
    private static readonly Color Me = KsTguiColors.FromHsl(227f, 63.5f, 60.2f);
    private static readonly Color Radio = KsTguiColors.FromHsl(132.8f, 74.4f, 45.9f);
    private static readonly Color Ooc = KsTguiColors.FromHsl(47.9f, 100f, 40f);
    private static readonly Color Looc = KsTguiColors.FromHsl(202f, 83.6f, 64.1f);
    private static readonly Color Dead = KsTguiColors.FromHsl(271.6f, 91.7f, 76.5f);
    private static readonly Color Admin = KsTguiColors.FromHsl(300f, 100f, 86.7f);
    private static readonly Color Console = KsTguiColors.FromHsl(176.1f, 20f, 45.1f);

    /// <summary>
    ///     The colour of a channel; a radio channel brings its own, as it does on the stock selector.
    /// </summary>
    public static Color ForChannel(ChatSelectChannel channel, Color? radioColor = null)
    {
        if (channel == ChatSelectChannel.Radio && radioColor is { } own)
            return own;

        return channel switch
        {
            ChatSelectChannel.Whisper => Whisper,
            ChatSelectChannel.Emotes => Me,
            ChatSelectChannel.Radio => Radio,
            ChatSelectChannel.OOC => Ooc,
            ChatSelectChannel.LOOC => Looc,
            ChatSelectChannel.Dead => Dead,
            ChatSelectChannel.Admin => Admin,
            ChatSelectChannel.Console => Console,
            _ => Say,
        };
    }

    /// <summary>
    ///     tgui-say's <c>color.adjust($color, $lightness: n%)</c>, which moves HSL lightness by percentage points.
    /// </summary>
    public static Color Lighten(Color color, float points)
    {
        var (hue, saturation, lightness) = ToHsl(color);
        return KsTguiColors.FromHsl(hue, saturation * 100f, Math.Clamp(lightness * 100f + points, 0f, 100f));
    }

    private static (float Hue, float Saturation, float Lightness) ToHsl(Color color)
    {
        var max = MathF.Max(color.R, MathF.Max(color.G, color.B));
        var min = MathF.Min(color.R, MathF.Min(color.G, color.B));
        var lightness = (max + min) / 2f;
        if (max - min < 0.0001f)
            return (0f, 0f, lightness);

        var delta = max - min;
        var saturation = lightness > 0.5f ? delta / (2f - max - min) : delta / (max + min);

        float hue;
        if (max == color.R)
            hue = (color.G - color.B) / delta + (color.G < color.B ? 6f : 0f);
        else if (max == color.G)
            hue = (color.B - color.R) / delta + 2f;
        else
            hue = (color.R - color.G) / delta + 4f;

        return (hue * 60f, saturation, lightness);
    }
}
