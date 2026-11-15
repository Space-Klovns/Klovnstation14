using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.BaronUI;

/// <summary>
///     Baron's rules for the tgui control library. Since #480 Baron opens the ported tgui windows too, and without these their
///         controls fall through to Baron's global defaults: every button the same brown, a selected one indistinguishable,
///         notice boxes opaque dark panels, list selection invisible. Everything here is Baron-only: the tgui sheet never
///         contains these rules, so the tgui look is untouched.
/// </summary>
public sealed partial class KsBaronUiSheetlet
{
    /// <summary>
    ///     Set by this sheet alone on the title row of a <see cref="KsTguiSection"/>, which then lets the sheet draw it rather
    ///         than drawing nothing (<see cref="KsSectionTitle"/>). Stock windows that only wrap a heading in a
    ///         <see cref="KsSectionTitle"/> carry no <see cref="KsTguiSection.StyleClassTitleRow"/> and stay as they were.
    /// </summary>
    public const string StylePropertySectionTitle = "ks-baron-section-title";

    // Baron's own colours: the slider and checkbox green, the slider track, the panel and input greys and the brown of button_3.
    public static readonly Color Green = Color.FromHex("#5AAE63");
    public static readonly Color Track = Color.FromHex("#2D3033");
    public static readonly Color InputBackground = Color.FromHex("#1F2226");
    public static readonly Color InputBorder = Color.FromHex("#4A5056");
    public static readonly Color Label = Color.FromHex("#A9B0B6");
    public static readonly Color Brown = Color.FromHex("#6E6153");

    /// <summary>
    ///     A button's content margins under Baron in a ported window: button_3's 9-patch drawn round a tgui-sized label, so the
    ///         windows keep tgui's row heights rather than Baron's 8px all round.
    /// </summary>
    private const float ButtonMarginVertical = 2f;
    private const float ButtonMarginHorizontal = 6f;
    private const float CompactMarginHorizontal = 3f;

    private static readonly Color Clear = Color.Black.WithAlpha(0f);

    private List<StyleRule> TguiControlRules(Font regularFont, Font boldFont, Font smallFont)
    {
        var rules = new List<StyleRule>();
        AddTguiButtonRules(rules, smallFont);
        AddTguiNoticeRules(rules, boldFont);
        AddTguiSurfaceRules(rules, boldFont);
        AddTguiPropertyRules(rules);
        return rules;
    }

    private void AddTguiButtonRules(List<StyleRule> rules, Font smallFont)
    {
        var box = ButtonBox(ButtonMarginHorizontal);
        var compactBox = ButtonBox(CompactMarginHorizontal);

        // tgui's 2px gaps between buttons, which KsTguiButton puts in itself once a sheet asks it to.
        rules.Add(E<KsTguiButton>().Prop(KsTguiSheetlet.StylePropertyRestyleChildren, true));

        // Every state: Baron's own hovered/pressed rules would otherwise put the 8px box back, and the button would jump.
        //      A class and a deeper type than Baron's ContainerButton rules, so these win at each level.
        rules.Add(E<KsTguiButton>().Class(ContainerButton.StyleClassButton).Box(box));
        foreach (var pseudo in Pseudos)
            rules.Add(E<KsTguiButton>().Class(ContainerButton.StyleClassButton).Pseudo(pseudo).Box(box));

        // Compact: the engine's small-button texture and 8pt label are what upstream gives ButtonSmall; Baron keeps button_3.
        rules.Add(E<KsTguiButton>().Class(ContainerButton.StyleClassButton).Class(KsTguiButton.StyleClassCompact).Box(compactBox));
        foreach (var pseudo in Pseudos)
        {
            rules.Add(E<KsTguiButton>().Class(ContainerButton.StyleClassButton).Class(KsTguiButton.StyleClassCompact).Pseudo(pseudo)
                .Box(compactBox));
        }

        rules.Add(E<KsTguiButton>().Class(ContainerButton.StyleClassButton).Class(KsTguiButton.StyleClassCompact)
            .ParentOf(E<Label>()).Font(smallFont));

        // Coloured: button_3 tinted towards the tgui colour's hue, so the chrome stays Baron's and the meaning stays tgui's.
        for (var i = 0; i < KsTguiColors.Count; i++)
        {
            var color = (KsTguiColor)i;
            if (color is KsTguiColor.Default or KsTguiColor.Transparent)
                continue;

            AddTintedButtonRules(rules, KsTguiColors.ButtonClass(color), TintOf(KsTguiColors.Element(color)));
        }

        // Selected: Baron's green, a hue no plain button has, with brighter text so it reads at a glance.
        AddTintedButtonRules(rules, KsTguiSheetlet.StyleClassButtonSelected, SelectedTint);
        rules.Add(E<KsTguiButton>().Class(KsTguiSheetlet.StyleClassButtonSelected).ParentOf(E<Label>()).FontColor(SelectedText));

        AddTransparentButtonRules(rules);
    }

    /// <summary>
    ///     The tint for the selected state: button_3 at full green and two-thirds red and blue lands on a clear dark green.
    /// </summary>
    private static readonly Color SelectedTint = new(0.45f, 1f, 0.5f);

    private static readonly Color SelectedText = Color.FromHex("#D6F5DA");

    private static readonly string[] Pseudos =
    [
        ContainerButton.StylePseudoClassNormal,
        ContainerButton.StylePseudoClassHover,
        ContainerButton.StylePseudoClassPressed,
        ContainerButton.StylePseudoClassDisabled,
    ];

    private static void AddTintedButtonRules(List<StyleRule> rules, string styleClass, Color tint)
    {
        rules.AddRange(new StyleRule[]
        {
            E<KsTguiButton>().Class(styleClass).PseudoNormal().Modulate(tint),
            E<KsTguiButton>().Class(styleClass).PseudoHovered().Modulate(Brighten(tint, 0.2f)),
            E<KsTguiButton>().Class(styleClass).PseudoPressed().Modulate(Scale(tint, 0.85f)),
            E<KsTguiButton>().Class(styleClass).PseudoDisabled().Modulate(Scale(tint, 0.6f)),
        });
    }

    /// <summary>
    ///     A list row, and any button tgui leaves see-through: no chrome, a faint wash on hover, and when selected a green wash
    ///         with green text, as Baron's checkbox green.
    /// </summary>
    private static void AddTransparentButtonRules(List<StyleRule> rules)
    {
        var transparent = KsTguiColors.ButtonClass(KsTguiColor.Transparent);
        var selected = KsTguiSheetlet.StyleClassButtonSelectedTransparent;

        AddFlatButtonRules(rules, transparent, Clear, Color.White.WithAlpha(0.08f), Color.White.WithAlpha(0.04f));
        AddFlatButtonRules(rules, selected, Green.WithAlpha(0.22f), Green.WithAlpha(0.32f), Green.WithAlpha(0.16f));

        rules.AddRange(new StyleRule[]
        {
            E<KsTguiButton>().Class(transparent).ParentOf(E<Label>()).FontColor(Color.FromHex("#C4C8CC")),
            E<KsTguiButton>().Class(transparent).PseudoHovered().ParentOf(E<Label>()).FontColor(Color.White),
            E<KsTguiButton>().Class(selected).ParentOf(E<Label>()).FontColor(Color.FromHex("#8EDB97")),
        });
    }

    private static void AddFlatButtonRules(List<StyleRule> rules, string styleClass, Color normal, Color hovered, Color pressed)
    {
        rules.AddRange(new StyleRule[]
        {
            // ModulateSelf would tint the flat box, so each state puts it back to white.
            E<KsTguiButton>().Class(styleClass).PseudoNormal().Box(FlatBox(normal)).Modulate(Color.White),
            E<KsTguiButton>().Class(styleClass).PseudoHovered().Box(FlatBox(hovered)).Modulate(Color.White),
            E<KsTguiButton>().Class(styleClass).PseudoPressed().Box(FlatBox(pressed)).Modulate(Color.White),
            E<KsTguiButton>().Class(styleClass).PseudoDisabled().Box(FlatBox(normal)).Modulate(Color.White),
        });
    }

    /// <summary>
    ///     tgui's notice colours, flat, inside Baron's dark outline. Flat and not a tinted texture because the box's text
    ///         colour is set in code (black on yellow and white), so the background must keep tgui's lightness to stay readable.
    /// </summary>
    private static void AddTguiNoticeRules(List<StyleRule> rules, Font boldFont)
    {
        foreach (var name in Enum.GetValues<KsTguiNoticeColor>())
        {
            var color = KsTguiNoticeBox.BackgroundOf(name, KsTguiPalettes.Notice);
            var box = new StyleBoxFlat
            {
                BackgroundColor = color,
                BorderColor = Color.Black.WithAlpha(0.6f),
                BorderThickness = new Thickness(1f),
            };
            box.SetContentMarginOverride(StyleBox.Margin.Vertical, 4f);
            box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);

            rules.Add(E<PanelContainer>().Class(KsTguiNoticeBox.StyleClassNoticeBox).Class(KsTguiNoticeBox.ColorClass(name))
                .Panel(box)
                .Modulate(Color.White));
        }

        rules.Add(E<RichTextLabel>().Class(KsTguiNoticeBox.StyleClassNoticeText).Font(boldFont));
    }

    private static void AddTguiSurfaceRules(List<StyleRule> rules, Font boldFont)
    {
        // A section's title: tgui's padding, and a line under it in button_3's brown where tgui has its blue.
        var titleBox = new StyleBoxFlat
        {
            BackgroundColor = Clear,
            BorderColor = Brown,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        titleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        titleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        titleBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 8f);

        rules.AddRange(new StyleRule[]
        {
            E<PanelContainer>().Class(KsSectionTitle.StyleClassSectionTitle).Class(KsTguiSection.StyleClassTitleRow)
                .Prop(StylePropertySectionTitle, true)
                .Panel(titleBox)
                .Modulate(Color.White),

            // The title is the window's 12pt bold, not Baron's 16pt heading, which would crowd the tgui sizes.
            E<PanelContainer>().Class(KsSectionTitle.StyleClassSectionTitle).Class(KsTguiSection.StyleClassTitleRow)
                .ParentOf(E()).ParentOf(E<Label>()).Font(boldFont),

        });

        // Striped rows: lighter, as the section body under Baron is already near black. Only the control library's own
        //      striped controls: the admin lists read the same property as "tgui is on" and keep their stock rows under Baron.
        foreach (var striped in new[] { typeof(KsTguiCandystripe), typeof(KsTguiLabeledList), typeof(KsTguiTable), typeof(KsTguiTabs) })
        {
            rules.Add(new MutableSelectorElement { Type = striped }.Class(KsTguiSheetlet.StyleClassCandystripe)
                .Prop(KsTguiSheetlet.StylePropertyCandystripe, Color.White.WithAlpha(0.045f))
                .Prop(KsTguiSheetlet.StylePropertyCandystripeEven, Clear));
        }
    }

    /// <summary>
    ///     What the self-drawing controls read in place of their tgui fallbacks.
    /// </summary>
    private static void AddTguiPropertyRules(List<StyleRule> rules)
    {
        rules.AddRange(new StyleRule[]
        {
            E<KsTguiProgressBar>()
                .Prop(KsTguiSheetlet.StylePropertyProgressColor, Green)
                .Prop(KsTguiSheetlet.StylePropertyProgressBackdrop, Track),
            E<KsTguiSlider>()
                .Prop(KsTguiSheetlet.StylePropertyProgressColor, Green)
                .Prop(KsTguiSheetlet.StylePropertyProgressBackdrop, Track)
                .Prop(KsTguiSheetlet.StylePropertyInputBackground, InputBackground),
            E<KsTguiTabItem>()
                .Prop(KsTguiSheetlet.StylePropertyTabSelectedText, Color.White)
                .Prop(KsTguiSheetlet.StylePropertyTabSelectedWash, Brown)
                .Prop(KsTguiSheetlet.StylePropertyTabHoverWash, Color.FromHex("#3A3F45")),
            E<KsTguiNumberInput>()
                .Prop(KsTguiSheetlet.StylePropertyInputBackground, InputBackground)
                .Prop(KsTguiSheetlet.StylePropertyInputBorder, Green)
                .Prop(KsTguiSheetlet.StylePropertyInputBorderIdle, InputBorder),
            E<KsTguiTextArea>()
                .Prop(KsTguiSheetlet.StylePropertyInputBackground, InputBackground)
                .Prop(KsTguiSheetlet.StylePropertyInputBorder, Green)
                .Prop(KsTguiSheetlet.StylePropertyInputBorderIdle, InputBorder),
            E<KsTguiKnob>()
                .Prop(KsTguiSheetlet.StylePropertyGaugeRing, InputBorder)
                .Prop(KsTguiSheetlet.StylePropertyInputBackground, InputBackground),
            E<KsTguiRoundGauge>().Prop(KsTguiSheetlet.StylePropertyGaugeRing, InputBorder),
            E<KsTguiLabeledListItem>().Prop(KsTguiSheetlet.StylePropertyLabelColor, Label),
            E<KsTguiBlockQuote>().Prop(KsTguiSheetlet.StylePropertyLabelColor, Label),
        });
    }

    private StyleBoxTexture ButtonBox(float horizontal)
    {
        var box = MakeBox("button_3.png", 8, 0);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, ButtonMarginVertical);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        return box;
    }

    private static StyleBoxFlat FlatBox(Color color)
    {
        var box = new StyleBoxFlat(color);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, ButtonMarginVertical);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, ButtonMarginHorizontal);
        return box;
    }

    /// <summary>
    ///     A modulate that turns button_3 towards a colour's hue: the colour scaled so its brightest channel is 1, then a
    ///         little of the texture's own brown let back in. Greys tint by their lightness alone.
    /// </summary>
    public static Color TintOf(Color color)
    {
        var max = MathF.Max(color.R, MathF.Max(color.G, color.B));
        var min = MathF.Min(color.R, MathF.Min(color.G, color.B));
        if (max - min < 0.1f)
        {
            var level = Math.Clamp(0.45f + max * 0.55f, 0.45f, 1f);
            return new Color(level, level, level);
        }

        var hue = new Color(color.R / max, color.G / max, color.B / max);
        return Color.InterpolateBetween(hue, Color.White, 0.15f);
    }

    private static Color Brighten(Color color, float amount)
    {
        return Color.InterpolateBetween(color, Color.White, amount);
    }

    private static Color Scale(Color color, float factor)
    {
        return new Color(color.R * factor, color.G * factor, color.B * factor, color.A);
    }
}
