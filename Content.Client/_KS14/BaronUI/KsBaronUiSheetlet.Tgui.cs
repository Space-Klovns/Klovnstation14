using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Lathe;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.BaronUI;

/// <summary>
///     Baron's rules for the tgui control library. Baron opens the ported tgui windows too, and without these their
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
    public static readonly Color LabelColor = Color.FromHex("#A9B0B6");
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

        // tgui's text sits at the left of a button; upstream centres every button label.
        rules.Add(E<Label>().Class(Button.StyleClassButton).Class(KsTguiSheetlet.StyleClassButtonLabelLeft)
            .AlignMode(Label.AlignMode.Left));

        // Coloured: button_3 tinted so that its face is the tgui colour itself, so the chrome stays Baron's and the button is
        //      as bright as tgui's. A hue tint over button_3's dark brown face could only darken it, to about a third: a
        //      red Fire came out a thin dark strip inside the texture's black rim.
        for (var i = 0; i < KsTguiColors.Count; i++)
        {
            var color = (KsTguiColor)i;
            if (color is KsTguiColor.Default or KsTguiColor.Transparent)
                continue;

            AddFaceTintedButtonRules(rules, color);
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

    /// <summary>
    ///     A coloured button: each state's face is tgui's colour for that state, and disabled dims the text as tgui does.
    /// </summary>
    private static void AddFaceTintedButtonRules(List<StyleRule> rules, KsTguiColor color)
    {
        var styleClass = KsTguiColors.ButtonClass(color);
        rules.AddRange(new StyleRule[]
        {
            E<KsTguiButton>().Class(styleClass).PseudoNormal().Modulate(FaceTintOf(KsTguiColors.Element(color))),
            E<KsTguiButton>().Class(styleClass).PseudoHovered().Modulate(FaceTintOf(KsTguiColors.Hovered(color))),
            E<KsTguiButton>().Class(styleClass).PseudoPressed().Modulate(FaceTintOf(KsTguiColors.Pressed(color))),
            E<KsTguiButton>().Class(styleClass).PseudoDisabled().Modulate(FaceTintOf(KsTguiColors.Disabled(color))),
            E<KsTguiButton>().Class(styleClass).ParentOf(E<Label>()).FontColor(KsTguiColors.Text(color)),
            E<KsTguiButton>().Class(styleClass).PseudoDisabled().ParentOf(E<Label>()).FontColor(KsTguiColors.DisabledText(color)),
        });
    }

    /// <summary>
    ///     The face of button_3, where it is flat: what <see cref="FaceTintOf"/> divides by.
    /// </summary>
    public static readonly Color ButtonFace = Color.FromHex("#564D45");

    /// <summary>
    ///     The modulate that turns button_3's face into <paramref name="face"/>: the face divided out in linear light, where the
    ///         engine multiplies, then encoded back. Above 1 where the colour is brighter than the texture, which the shader
    ///         takes as it is; the rim and its shading scale with it.
    /// </summary>
    public static Color FaceTintOf(Color face)
    {
        return new Color(Ratio(face.R, ButtonFace.R), Ratio(face.G, ButtonFace.G), Ratio(face.B, ButtonFace.B));

        static float Ratio(float wanted, float texture)
        {
            return Encode(Decode(wanted) / Decode(texture));
        }

        static float Decode(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        static float Encode(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

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
        // Above the box rules for plain and compact buttons, which would otherwise put button_3 back under the text.
        foreach (var compact in new[] { false, true })
        {
            var horizontal = compact ? CompactMarginHorizontal : ButtonMarginHorizontal;
            foreach (var (pseudo, color) in new[]
                     {
                         (ContainerButton.StylePseudoClassNormal, normal),
                         (ContainerButton.StylePseudoClassHover, hovered),
                         (ContainerButton.StylePseudoClassPressed, pressed),
                         (ContainerButton.StylePseudoClassDisabled, normal),
                     })
            {
                var selector = E<KsTguiButton>().Class(ContainerButton.StyleClassButton).Class(styleClass);
                if (compact)
                    selector = selector.Class(KsTguiButton.StyleClassCompact);

                // ModulateSelf would tint the flat box, so each state puts it back to white.
                rules.Add(selector.Pseudo(pseudo).Box(FlatBox(color, horizontal)).Modulate(Color.White));
            }
        }
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
                .Prop(KsTguiSheetlet.StylePropertyCandystripe, Color.White.WithAlpha(0.012f))
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
            // The lathe's categories: Baron's hover grey, and its brown for the selected one's bar in place of tg's blue.
            E<KsTguiFabricatorTab>()
                .Prop(KsTguiSheetlet.StylePropertyTabHoverWash, Color.FromHex("#3A3F45"))
                .Prop(KsTguiSheetlet.StylePropertyTabIndicator, Color.FromHex("#A08A6E"))
                .Prop(KsTguiSheetlet.StylePropertyTabSelectedText, Color.White),
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
            E<KsTguiLabeledListItem>().Prop(KsTguiSheetlet.StylePropertyLabelColor, LabelColor),
            E<KsTguiBlockQuote>().Prop(KsTguiSheetlet.StylePropertyLabelColor, LabelColor),
        });
    }

    private StyleBoxTexture ButtonBox(float horizontal)
    {
        var box = MakeBox("button_3.png", 8, 0);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, ButtonMarginVertical);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        return box;
    }

    private static StyleBoxFlat FlatBox(Color color, float horizontal)
    {
        var box = new StyleBoxFlat(color);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, ButtonMarginVertical);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
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
