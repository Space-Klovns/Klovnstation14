using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets.Palette;
using Content.Shared._KS14.Tgui;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     One tgui window theme's colours, worked out from the tokens its <c>themes/*.scss</c> sets the way tgui-core works them
///         out from <c>vars-colors.scss</c>, <c>vars-components.scss</c> and <c>vars-values.scss</c>. Nothing here is a value
///         read off a screenshot: the Nanotrasen instance is checked against <see cref="KsTguiPalettes"/>, which holds that
///         theme's values by hand, so a derivation that is wrong shows up as a test failure.
/// </summary>
public sealed class KsTguiThemePalette
{
    /// <summary>
    ///     An HSL token as tgui writes it: degrees, then percentages.
    /// </summary>
    public readonly record struct Hsl(float Hue, float Saturation, float Lightness)
    {
        public Color ToColor()
        {
            // A browser draws 8 bits a channel, so a token is rounded as one. The hand-written hex of KsTguiPalettes is too, and
            //      anything comparing the two colours exactly (a fade ending on a palette colour) needs them to agree.
            var color = KsTguiColors.FromHsl(Hue, Saturation, Math.Clamp(Lightness, 0f, 100f));
            return new Color(Quantise(color.R), Quantise(color.G), Quantise(color.B));
        }

        private static float Quantise(float value)
        {
            // Nearest, with an exact half going down: the hover colour's 127.5 is 127 on screen, as the sampled hex has it.
            return MathF.Ceiling(value * 255f - 0.5f - 0.0001f) / 255f;
        }

        public Hsl WithLightness(float lightness)
        {
            return this with { Lightness = lightness };
        }

        public Hsl ShiftLightness(float amount)
        {
            return this with { Lightness = Lightness + amount };
        }
    }

    /// <summary>
    ///     What a theme's stylesheet sets, with tgui's own defaults for what it leaves out.
    /// </summary>
    private readonly record struct Tokens
    {
        // vars-colors.scss
        public Hsl Base { get; init; }
        public Hsl Primary { get; init; }
        public Hsl? Secondary { get; init; }
        public float? SecondarySaturation { get; init; }
        public float SecondaryLightnessAdjustment { get; init; }
        public float BaseGradientSpread { get; init; }
        public Hsl? BorderPrimary { get; init; }
        public Hsl? CandystripeOdd { get; init; }
        public Hsl? CandystripeEven { get; init; }

        // vars-components.scss
        public Hsl ButtonSelected { get; init; }
        public Hsl ButtonCaution { get; init; }
        public Hsl ButtonDanger { get; init; }
        public Hsl NoticeBox { get; init; }
        public bool ProgressBarBackdrop { get; init; }
        public float TooltipLightness { get; init; }
        public Hsl? TooltipBackground { get; init; }

        public string Logo { get; init; }
    }

    // tgui-core's defaults.
    private static readonly Hsl DefaultBase = new(0f, 0f, 15f);
    private static readonly Hsl DefaultPrimary = new(210f, 37.5f, 45f);
    private static readonly Hsl ColorGreen = new(140f, 70f, 40f);
    private static readonly Hsl ColorYellow = new(50f, 97.5f, 50f);
    private static readonly Hsl ColorRed = new(0f, 70f, 50f);

    private static readonly Tokens NanotrasenTokens = new()
    {
        Base = DefaultBase,
        Primary = DefaultPrimary,
        SecondaryLightnessAdjustment = 7.5f,
        BaseGradientSpread = 2f,
        ButtonSelected = ColorGreen,
        ButtonCaution = ColorYellow,
        ButtonDanger = ColorRed,
        NoticeBox = new Hsl(37.5f, 55f, 57.5f),
        TooltipLightness = 5f,
        Logo = "nanotrasen_logo.png",
    };

    // themes/syndicate.scss
    private static readonly Tokens SyndicateTokens = NanotrasenTokens with
    {
        Base = new Hsl(0f, 96f, 17f),
        Primary = new Hsl(120f, 34f, 35f),
        SecondaryLightnessAdjustment = 11.5f,
        BaseGradientSpread = 6f,
        ButtonSelected = new Hsl(0f, 90f, 37.5f),
        ButtonCaution = new Hsl(28f, 87f, 39f),
        ButtonDanger = new Hsl(61f, 100f, 30f),
        NoticeBox = new Hsl(0f, 98f, 28f),
        ProgressBarBackdrop = true,
        TooltipLightness = 25f,
        Logo = "syndicate_logo.png",
    };

    // themes/wizard.scss
    private static readonly Tokens WizardTokens = NanotrasenTokens with
    {
        Base = new Hsl(205f, 40f, 20f),
        Primary = new Hsl(190f, 80f, 40f),
        Secondary = new Hsl(134f, 71f, 31f),
        BorderPrimary = new Hsl(225f, 25f, 40f),
        BaseGradientSpread = 6f,
        ButtonSelected = new Hsl(227f, 40f, 44f),
        ButtonCaution = new Hsl(28f, 89f, 39f),
        ButtonDanger = new Hsl(0f, 91f, 35f),
        NoticeBox = new Hsl(340f, 58f, 41f),
        ProgressBarBackdrop = true,
        TooltipBackground = new Hsl(134f, 61f, 42f),
        Logo = "wizard_logo.png",
    };

    // themes/admin.scss. --primary-hue 200 keeps the default saturation and lightness, --secondary-saturation 0 greys the
    //      title bar (its hue is the base's, and the adjustment is 1.75), and both rows of a stripe are blue at .25.
    private static readonly Tokens AdminTokens = NanotrasenTokens with
    {
        Base = new Hsl(205f, 17f, 20f),
        Primary = DefaultPrimary with { Hue = 200f },
        SecondarySaturation = 0f,
        SecondaryLightnessAdjustment = 1.75f,
        BaseGradientSpread = 6f,
        CandystripeOdd = new Hsl(200f, 70f, 15f),
        CandystripeEven = new Hsl(210f, 70f, 45f),
        Logo = "admin_logo.png",
    };

    public static readonly KsTguiThemePalette Nanotrasen = new(KsTguiWindowTheme.Nanotrasen, NanotrasenTokens);
    public static readonly KsTguiThemePalette Syndicate = new(KsTguiWindowTheme.Syndicate, SyndicateTokens);
    public static readonly KsTguiThemePalette Wizard = new(KsTguiWindowTheme.Wizard, WizardTokens);

    public static readonly KsTguiThemePalette Admin = new(KsTguiWindowTheme.Admin, AdminTokens);

    public static KsTguiThemePalette For(KsTguiWindowTheme theme)
    {
        return theme switch
        {
            KsTguiWindowTheme.Syndicate => Syndicate,
            KsTguiWindowTheme.Wizard => Wizard,
            KsTguiWindowTheme.Admin => Admin,
            _ => Nanotrasen,
        };
    }

    /// <summary>
    ///     The palette a stylesheet's config asks for, the default theme's for one that doesn't.
    /// </summary>
    public static KsTguiThemePalette For(object? config)
    {
        return config is KsTguiThemeConfig themeConfig ? For(themeConfig.Theme) : Nanotrasen;
    }

    public KsTguiWindowTheme Theme { get; }

    /// <summary>
    ///     The sheet's texture file for this theme's logo, under <see cref="KsTguiSheetlet.TextureRoot"/>.
    /// </summary>
    public string LogoTexture { get; }

    /// <summary>
    ///     <c>--color-base</c>, and the gradient of the window between <see cref="BaseStart"/> and <see cref="BaseEnd"/>.
    /// </summary>
    public Color Base { get; }

    public Color BaseStart { get; }
    public Color BaseEnd { get; }

    /// <summary>
    ///     <c>--color-secondary</c>, which is the title bar's background.
    /// </summary>
    public Color TitleBar { get; }

    /// <summary>
    ///     <c>--color-section</c>, black at CSS opacity .33.
    /// </summary>
    public Color Section { get; }

    /// <summary>
    ///     <c>--candystripe-odd</c>, over the first, third and so on row.
    /// </summary>
    public Color Candystripe { get; }

    /// <summary>
    ///     <c>--candystripe-even</c>, over the second, fourth and so on row: clear unless a theme sets it.
    /// </summary>
    public Color CandystripeEven { get; }
    public Color Border { get; }

    /// <summary>
    ///         <c>--dimmer-background</c>: the base's hue at lightness 5, at CSS opacity .75 over a Section.
    /// </summary>
    public Color Dimmer { get; }

    /// <summary>
    ///     <c>.Window__dimmer</c>: the base at lightness +15, at CSS opacity .25 over the window.
    /// </summary>
    public Color WindowDimmer { get; }

    /// <summary>
    ///     <c>Window__dimmer</c> as CSS has it (the base at +15 lightness, .25), for the backdrop pass to blend in sRGB.
    /// </summary>
    public KsTguiTint WindowDimmerTint { get; }

    /// <summary>
    ///     <c>--dropdown-menu-background</c> as CSS has it.
    /// </summary>
    public KsTguiTint MenuTint { get; }

    /// <summary>
    ///     <c>--tooltip-background</c> as CSS has it; the knob's and slider's popups use the same.
    /// </summary>
    public KsTguiTint TooltipTint { get; }

    public Color TextDim { get; }
    public Color TextTitle { get; }

    /// <summary>
    ///     <c>--color-label</c> as text: the primary's hue at saturation 17.5 and lightness 57.5, plus <c>--adjust-color</c> (5), which
    ///         tgui's <c>color-label</c> class adds to every text colour to make it readable.
    /// </summary>
    public Color Label { get; }

    /// <summary>
    ///     <c>--color-primary</c>, and the colour under a section's title.
    /// </summary>
    public Color PrimaryBase { get; }

    /// <summary>
    ///     The selected tab's text: the primary's hue at lightness 90 (<c>--tab-color-selected</c>).
    /// </summary>
    public Color TabSelectedText { get; }

    /// <summary>
    ///     <c>--color-border-secondary</c>: an input's border when focused, and a NumberInput's text.
    /// </summary>
    public Color InputBorder { get; }

    /// <summary>
    ///     <c>--color-border-primary</c> over the input's own background, which is its idle border.
    /// </summary>
    public Color InputBorderIdle { get; }

    /// <summary>
    ///     <c>--input-background</c>: the base's hue and saturation at lightness 5.
    /// </summary>
    public Color InputBackground { get; }

    /// <summary>
    ///     A Dropdown's menu: the base at lightness -5.5, at CSS opacity .85.
    /// </summary>
    public Color Menu { get; }

    /// <summary>
    ///     <c>--tooltip-background</c>.
    /// </summary>
    public Color Tooltip { get; }

    /// <summary>
    ///     <c>--color-scrollbar-thumb</c>: the base at lightness +9.25.
    /// </summary>
    public Color ScrollThumb { get; }

    /// <summary>
    ///     The grey wash of a selected tab, <c>--tab-background-selected</c>, and its hover.
    /// </summary>
    public Color TabSelectedWash { get; }

    public Color TabHoverWash { get; }

    public Color Hovered { get; }
    public Color Pressed { get; }
    public Color Selected { get; }

    /// <summary>
    ///     <c>--notice-box-background</c> as a NoticeBox renders it: oklch lightness x0.825 and chroma x0.75.
    /// </summary>
    public Color Notice { get; }

    /// <summary>
    ///     <c>--progress-bar-background</c>, which is clear unless a theme gives bars a backdrop.
    /// </summary>
    public Color ProgressBarBackdrop { get; }

    public ButtonShades Primary { get; }
    public ButtonShades SelectedButton { get; }
    public ButtonShades Caution { get; }
    public ButtonShades Danger { get; }
    public ButtonShades LabelButton { get; }

    /// <summary>
    ///     Whether this theme's caution button is tgui's own yellow, which wants black text.
    /// </summary>
    public bool CautionIsYellow { get; }

    public ColorPalette PrimaryPalette { get; }
    public ColorPalette SecondaryPalette { get; }
    public ColorPalette PositivePalette { get; }
    public ColorPalette NegativePalette { get; }
    public ColorPalette HighlightPalette { get; }

    /// <summary>
    ///     A Button's shades: the token at -5, hover at +10 over that, pressed at -7.5 under it (<c>Button.scss</c>), and
    ///         disabled as the element at half opacity over the window.
    /// </summary>
    public readonly record struct ButtonShades(Color Element, Color Hovered, Color Pressed, Color Disabled);

    private KsTguiThemePalette(KsTguiWindowTheme theme, Tokens tokens)
    {
        Theme = theme;
        LogoTexture = tokens.Logo;

        var baseHsl = tokens.Base;
        Base = baseHsl.ToColor();
        BaseStart = baseHsl.ShiftLightness(tokens.BaseGradientSpread).ToColor();
        BaseEnd = baseHsl.ShiftLightness(-tokens.BaseGradientSpread).ToColor();
        TitleBar = (tokens.Secondary ?? (baseHsl with { Saturation = tokens.SecondarySaturation ?? baseHsl.Saturation })
            .ShiftLightness(tokens.SecondaryLightnessAdjustment)).ToColor();

        Section = KsTguiPalettes.Translucent(Color.Black, 0.33f, Base);
        var stripeBackdrop = Scale(Base, 0.67f);
        Candystripe = KsTguiPalettes.Translucent(tokens.CandystripeOdd?.ToColor() ?? Color.Black, 0.25f, stripeBackdrop);
        CandystripeEven = tokens.CandystripeEven is { } evenStripe
            ? KsTguiPalettes.Translucent(evenStripe.ToColor(), 0.25f, stripeBackdrop)
            : Color.Black.WithAlpha(0f);
        Border = KsTguiPalettes.Translucent(Color.White, 0.1f, Base);
        Dimmer = KsTguiPalettes.Translucent(baseHsl.WithLightness(5f).ToColor(), 0.75f, Section);
        WindowDimmerTint = new KsTguiTint(baseHsl.ShiftLightness(15f).ToColor(), 0.25f);
        WindowDimmer = WindowDimmerTint.Approximate(Base);
        TextDim = Flatten(Color.White, 0.5f, Base);
        TextTitle = Flatten(Color.White, 0.75f, TitleBar);

        var primary = tokens.Primary;
        PrimaryBase = primary.ToColor();
        Label = new Hsl(primary.Hue, 17.5f, 62.5f).ToColor();
        TabSelectedText = primary.WithLightness(90f).ToColor();

        InputBackground = baseHsl.WithLightness(5f).ToColor();
        if (tokens.BorderPrimary is { } borderPrimary)
        {
            InputBorder = borderPrimary.ToColor();
            InputBorderIdle = InputBorder;
        }
        else
        {
            // hsl(from primary h 100 75 / .75), and --color-border-secondary is the same at alpha 1.
            InputBorder = new Hsl(primary.Hue, 100f, 75f).ToColor();
            InputBorderIdle = Flatten(InputBorder, 0.75f, InputBackground);
        }

        MenuTint = new KsTguiTint(baseHsl.ShiftLightness(-5.5f).ToColor(), 0.85f);
        Menu = MenuTint.Approximate(Base);
        TooltipTint = tokens.TooltipBackground is { } tooltipBackground
            ? new KsTguiTint(tooltipBackground.ToColor(), 1f)
            : new KsTguiTint(baseHsl.WithLightness(tokens.TooltipLightness).ToColor(), 0.85f);
        Tooltip = tokens.TooltipBackground?.ToColor() ?? TooltipTint.Approximate(Base);
        ScrollThumb = baseHsl.ShiftLightness(9.25f).ToColor();

        // --color-action is 50% grey; hover, active and selected are it at .25, .3 and .4, and a tab's are at lightness x0.75.
        var wash = KsTguiPalettes.Wash;
        Hovered = KsTguiPalettes.Translucent(wash, 0.25f, Base);
        Pressed = KsTguiPalettes.Translucent(wash, 0.3f, Base);
        Selected = KsTguiPalettes.Translucent(wash, 0.4f, Base);
        var tabWash = new Hsl(0f, 0f, 37.5f).ToColor();
        TabHoverWash = KsTguiPalettes.Translucent(tabWash, 0.25f, Base);
        TabSelectedWash = KsTguiPalettes.Translucent(tabWash, 0.4f, Base);

        Notice = OklchScale(tokens.NoticeBox.ToColor(), 0.825f, 0.75f);
        ProgressBarBackdrop = tokens.ProgressBarBackdrop
            ? KsTguiPalettes.Translucent(Color.Black, 0.5f, Base)
            : Color.Black.WithAlpha(0f);

        Primary = Shades(primary, Base);
        SelectedButton = Shades(tokens.ButtonSelected, Base);
        Caution = Shades(tokens.ButtonCaution, Base);
        Danger = Shades(tokens.ButtonDanger, Base);
        LabelButton = Shades(new Hsl(primary.Hue, 17.5f, 57.5f), Base);
        CautionIsYellow = tokens.ButtonCaution == ColorYellow;

        // The same five palettes upstream's sheetlets read, in the shape they want.
        var primaryElement = Primary.Element;
        PrimaryPalette = new ColorPalette(
            Base: PrimaryBase,
            LightnessShift: 0f,
            ChromaShift: 0f,
            Element: primaryElement,
            HoveredElement: Primary.Hovered,
            PressedElement: Primary.Pressed,
            DisabledElement: Primary.Disabled,
            Background: Base,
            BackgroundLight: TitleBar,
            BackgroundDark: Section,
            Text: Color.White,
            TextDark: TextDim);

        SecondaryPalette = new ColorPalette(
            Base: TitleBar,
            LightnessShift: 0f,
            ChromaShift: 0f,
            Element: TitleBar,
            HoveredElement: Hovered,
            PressedElement: Pressed,
            DisabledElement: Flatten(TitleBar, 0.5f, Base),
            Background: Base,
            BackgroundLight: TitleBar,
            BackgroundDark: Section,
            Text: Color.White,
            TextDark: Border);

        var green = Shades(ColorGreen, Base);
        PositivePalette = KsTguiPalettes.Positive with
        {
            Element = green.Element,
            HoveredElement = green.Hovered,
            PressedElement = green.Pressed,
            DisabledElement = green.Disabled,
        };

        var red = Shades(ColorRed, Base);
        NegativePalette = KsTguiPalettes.Negative with
        {
            DisabledElement = red.Disabled,
        };

        HighlightPalette = PrimaryPalette with { TextDark = Label };
    }

    /// <summary>
    ///     The shades of a button of this token, over a window of this base.
    /// </summary>
    public static ButtonShades Shades(Hsl token, Color window)
    {
        var element = token.ShiftLightness(-5f);
        var elementColor = element.ToColor();
        return new ButtonShades(
            elementColor,
            element.ShiftLightness(10f).ToColor(),
            element.ShiftLightness(-7.5f).ToColor(),
            Flatten(elementColor, 0.5f, window));
    }

    /// <summary>
    ///     What CSS gets from <paramref name="color"/> at this opacity over an opaque backdrop: a plain blend of the sRGB
    ///         values, which is what a browser does, and what the engine's alpha is not.
    /// </summary>
    public static Color Flatten(Color color, float cssOpacity, Color backdrop)
    {
        return new Color(
            color.R * cssOpacity + backdrop.R * (1f - cssOpacity),
            color.G * cssOpacity + backdrop.G * (1f - cssOpacity),
            color.B * cssOpacity + backdrop.B * (1f - cssOpacity));
    }

    private static Color Scale(Color color, float factor)
    {
        return new Color(color.R * factor, color.G * factor, color.B * factor);
    }

    /// <summary>
    ///     CSS's <c>oklch(from color calc(l * lightness) calc(c * chroma) h)</c>, as <c>NoticeBox.scss</c> darkens its colour.
    ///         Björn Ottosson's OKLab, with each channel clamped to sRGB afterwards.
    /// </summary>
    public static Color OklchScale(Color srgb, float lightnessFactor, float chromaFactor)
    {
        var r = ToLinear(srgb.R);
        var g = ToLinear(srgb.G);
        var b = ToLinear(srgb.B);

        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

        var lightness = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
        var a = (1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s) * chromaFactor;
        var bb = (0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s) * chromaFactor;
        lightness *= lightnessFactor;

        var l2 = lightness + 0.3963377774f * a + 0.2158037573f * bb;
        var m2 = lightness - 0.1055613458f * a - 0.0638541728f * bb;
        var s2 = lightness - 0.0894841775f * a - 1.2914855480f * bb;
        l2 = l2 * l2 * l2;
        m2 = m2 * m2 * m2;
        s2 = s2 * s2 * s2;

        return new Color(
            FromLinear(4.0767416621f * l2 - 3.3077115913f * m2 + 0.2309699292f * s2),
            FromLinear(-1.2684380046f * l2 + 2.6097574011f * m2 - 0.3413193965f * s2),
            FromLinear(-0.0041960863f * l2 - 0.7034186147f * m2 + 1.7076147010f * s2));
    }

    private static float ToLinear(float value)
    {
        return value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    private static float FromLinear(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
    }
}

/// <summary>
///     What a window theme's stylesheet is built from, handed through <c>BaseStylesheet</c>'s config so that every sheetlet
///         can read it.
/// </summary>
public sealed class KsTguiThemeConfig(KsTguiWindowTheme theme)
{
    public KsTguiWindowTheme Theme { get; } = theme;
}
