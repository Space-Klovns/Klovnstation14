using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     The semantic colours tgui picks by name: <c>color="good"</c>, ranges, label text.
/// </summary>
public enum KsTguiTone : byte
{
    Default,
    Good,
    Average,
    Bad,
    Label,
}

/// <summary>
///     tgui's default ("Nanotrasen") theme, from tgui-core <c>styles/colors.scss</c> and the component styles.
///     tgui derives most shades by moving a token's HSL lightness: backgrounds are the token minus 5 points,
///         coloured text is the token plus 5, hover is the background plus 10, and a held button minus 7.5.
/// </summary>
public static class KsTguiTheme
{
    public const string FontRegularPath = "/Fonts/NotoSans/NotoSans-Regular.ttf";
    public const string FontBoldPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";

    /// <summary>
    ///     tgui's 1rem. Verdana isn't shipped, so text uses Noto Sans at the same size.
    /// </summary>
    public const int FontSize = 12;

    public const float ButtonHeight = 20f;
    public const float TitleBarHeight = 32f;

    // Tokens.
    public static readonly Color Primary = Color.FromHex("#48739E");
    public static readonly Color Good = Color.FromHex("#5BA626");
    public static readonly Color Average = Color.FromHex("#F2890D");
    public static readonly Color Bad = Color.FromHex("#DC2323");
    public static readonly Color Label = Color.FromHex("#8093A6");
    public static readonly Color Red = Color.FromHex("#D92626");
    public static readonly Color Yellow = Color.FromHex("#FCD203");
    public static readonly Color Green = Color.FromHex("#1FAD4E");
    public static readonly Color Blue = Color.FromHex("#2A79C8");

    // Surfaces.
    public static readonly Color WindowTop = Color.FromHex("#2B2B2B");
    public static readonly Color WindowBottom = Color.FromHex("#212121");
    public static readonly Color TitleBar = Color.FromHex("#393939");
    public static readonly Color Section = new(0f, 0f, 0f, 0.33f);
    public static readonly Color InputBackground = Color.FromHex("#0D0D0D");
    public static readonly Color Watermark = new(0f, 0f, 0f, 0.33f);

    // Text and lines.
    public static readonly Color Text = Color.White;
    public static readonly Color TextTranslucent = Color.White.WithAlpha(0.5f);
    public static readonly Color TitleText = Color.White.WithAlpha(0.75f);
    public static readonly Color BorderPrimary = new(128 / 255f, 191 / 255f, 1f, 0.75f);
    public static readonly Color BorderFocused = Color.FromHex("#80BFFF");

    /// <summary>
    ///     Moves <paramref name="color"/>'s HSL lightness by <paramref name="points"/> percentage points, keeping alpha.
    /// </summary>
    public static Color AdjustLightness(Color color, float points)
    {
        var hsl = Color.ToHsl(color);
        hsl.Z = Math.Clamp(hsl.Z + points / 100f, 0f, 1f);
        return Color.FromHsl(hsl);
    }

    /// <summary>
    ///     tgui's <c>bg()</c>: the fill a token gets on buttons and progress bars.
    /// </summary>
    public static Color Background(Color token)
    {
        return AdjustLightness(token, -5f);
    }

    /// <summary>
    ///     tgui's <c>color-*</c> text class, which is the token lightened by 5.
    /// </summary>
    public static Color Foreground(Color token)
    {
        return AdjustLightness(token, 5f);
    }

    public static Color Token(KsTguiTone tone)
    {
        return tone switch
        {
            KsTguiTone.Good => Good,
            KsTguiTone.Average => Average,
            KsTguiTone.Bad => Bad,
            KsTguiTone.Label => Label,
            _ => Primary,
        };
    }

    /// <summary>
    ///     Text in a tone. <see cref="KsTguiTone.Default"/> is plain white, as uncoloured tgui text is.
    /// </summary>
    public static Color ToneText(KsTguiTone tone)
    {
        return tone == KsTguiTone.Default ? Text : Foreground(Token(tone));
    }

    /// <summary>
    ///     tgui's <c>ranges</c> for a bar where more is better: good from <paramref name="goodFrom"/> up, average from
    ///         <paramref name="averageFrom"/>, bad below that.
    /// </summary>
    public static KsTguiTone ToneAbove(float value, float goodFrom, float averageFrom)
    {
        if (value >= goodFrom)
            return KsTguiTone.Good;

        return value >= averageFrom ? KsTguiTone.Average : KsTguiTone.Bad;
    }

    public static Font RegularFont(IResourceCache resourceCache, int size = FontSize)
    {
        return resourceCache.GetFont(FontRegularPath, size);
    }

    public static Font BoldFont(IResourceCache resourceCache, int size = FontSize)
    {
        return resourceCache.GetFont(FontBoldPath, size);
    }

    /// <summary>
    ///     A flat box with the given content margins, in virtual pixels.
    /// </summary>
    public static StyleBoxFlat FlatBox(Color background, float horizontal = 0f, float vertical = 0f)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            ContentMarginLeftOverride = horizontal,
            ContentMarginRightOverride = horizontal,
            ContentMarginTopOverride = vertical,
            ContentMarginBottomOverride = vertical,
        };
    }
}
