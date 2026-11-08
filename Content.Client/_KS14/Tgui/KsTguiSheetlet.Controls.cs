using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Tgui;

public sealed partial class KsTguiSheetlet
{
    /// <summary>
    ///     Set on a <see cref="KsSectionPanel"/> by this sheet alone: its presence is what turns the panel into a tgui Section
    ///         body, its absence (Baron) what leaves it flat.
    /// </summary>
    public const string StylePropertySection = "ks-tgui-section";

    /// <summary>
    ///     As <see cref="StylePropertySection"/>, for a <see cref="KsSectionTitle"/>.
    /// </summary>
    public const string StylePropertySectionTitle = "ks-tgui-section-title";

    /// <summary>
    ///     On a <see cref="KsTguiButton"/> that is selected, in place of its colour's class.
    /// </summary>
    public const string StyleClassButtonSelected = "KsTguiButtonSelected";

    /// <summary>
    ///     On a <see cref="KsTguiButton"/> that is selected and transparent: tgui keeps it clear and turns its text green.
    /// </summary>
    public const string StyleClassButtonSelectedTransparent = "KsTguiButtonSelectedTransparent";

    /// <summary>
    ///     On a <see cref="KsTguiButton"/> with tgui's <c>circular</c>.
    /// </summary>
    public const string StyleClassButtonCircular = "KsTguiButtonCircular";

    /// <summary>
    ///     On a control that stripes its rows; the tgui sheet gives it <see cref="StylePropertyCandystripe"/>, so Baron draws none.
    /// </summary>
    public const string StyleClassCandystripe = "KsTguiCandystripe";

    /// <summary>
    ///     The colour of the dark rows of a candystripe, set by the tgui sheet alone.
    /// </summary>
    public const string StylePropertyCandystripe = "ks-tgui-candystripe";

    /// <summary>
    ///     The NoticeBox colour <c>warning</c> (<c>--color-orange</c> at oklch lightness x0.825, chroma x0.75); the others are
    ///         already in <see cref="KsTguiPalettes"/>.
    /// </summary>
    private static readonly Color NoticeWarning = Color.FromHex("#B6571F");

    /// <summary>
    ///     Rules for the controls in <c>Controls/</c>.
    /// </summary>
    private void AddControlLibraryRules(List<StyleRule> rules)
    {
        AddSectionRules(rules);
        AddNoticeBoxRules(rules);
        AddColoredButtonRules(rules);
        AddButtonVariantRules(rules);
        rules.Add(E().Class(StyleClassCandystripe).Prop(StylePropertyCandystripe, _palette.Candystripe));
    }

    private void AddSectionRules(List<StyleRule> rules)
    {
        // tgui's section title: 6px around the text and a 2px line under it, which counts in the bottom margin.
        var titleBox = new KsTguiStyleBox(Clear)
        {
            BorderColor = _palette.PrimaryBase,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        titleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        titleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        titleBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 8f);

        // A nested section sits 6px out over the outer padding, so its title text still starts at the outer body's edge and its
        //      line runs edge to edge.
        var nestedTitleBox = new KsTguiStyleBox(Clear)
        {
            BorderColor = _palette.PrimaryBase,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        nestedTitleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        nestedTitleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        nestedTitleBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 8f);

        rules.AddRange(new StyleRule[]
        {
            E<PanelContainer>().Class(KsSectionTitle.StyleClassSectionTitle).Class(KsTguiSection.StyleClassNestedTitle)
                .Prop(StylePropertySectionTitle, true)
                .Panel(nestedTitleBox)
                .Modulate(Color.White),
            E<PanelContainer>().Class(KsSectionPanel.StyleClassSectionPanel)
                .Prop(StylePropertySection, true)
                .Panel(SectionBox())
                .Modulate(Color.White),
            E<PanelContainer>().Class(KsSectionTitle.StyleClassSectionTitle)
                .Prop(StylePropertySectionTitle, true)
                .Panel(titleBox)
                .Modulate(Color.White),
        });
    }

    private void AddNoticeBoxRules(List<StyleRule> rules)
    {
        foreach (var name in Enum.GetValues<KsTguiNoticeColor>())
        {
            // The five the windows have used keep their exact colours; the rest are derived as NoticeBox.scss does.
            var color = name switch
            {
                KsTguiNoticeColor.Default => _palette.Notice,
                KsTguiNoticeColor.Info => KsTguiPalettes.NoticeInfo,
                KsTguiNoticeColor.Success => KsTguiPalettes.NoticeGood,
                KsTguiNoticeColor.Warning => NoticeWarning,
                KsTguiNoticeColor.Danger => KsTguiPalettes.NoticeBad,
                _ => KsTguiNoticeBox.BackgroundOf(name, _palette.Notice),
            };

            // tgui's notice box is padded 4 by 6.
            var box = new KsTguiNoticeStyleBox(color, whiteStripes: name == KsTguiNoticeColor.Black);
            box.SetContentMarginOverride(StyleBox.Margin.Vertical, 4f);
            box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);

            rules.Add(E<PanelContainer>().Class(KsTguiNoticeBox.StyleClassNoticeBox).Class(KsTguiNoticeBox.ColorClass(name))
                .Panel(box)
                .Modulate(Color.White));

            // The stock StripeBack is tgui's notice too, and takes the same colours (StripeBack.KsNoticeColor).
            rules.Add(E<StripeBack>().Class(KsTguiNoticeBox.ColorClass(name))
                .Prop(StripeBack.StylePropertyBackground, box)
                .Prop(StripeBack.KsStylePropertyFlat, true));
        }

        rules.Add(E<RichTextLabel>().Class(KsTguiNoticeBox.StyleClassNoticeText)
            .Font(IoCManager.Resolve<KsTguiFonts>().GetFont(12, FontKind.BoldItalic))
            .FontColor(Color.White));
    }

    private void AddColoredButtonRules(List<StyleRule> rules)
    {
        // Selected: tgui's green, whatever the button's colour.
        AddButtonColorRules(rules, StyleClassButtonSelected, KsTguiColor.Green, KsTguiColors.Text(KsTguiColor.Green));

        for (var i = 0; i < KsTguiColors.Count; i++)
        {
            var color = (KsTguiColor)i;

            // Default is what upstream's button rules already draw.
            if (color == KsTguiColor.Default)
                continue;

            if (color == KsTguiColor.Transparent)
                AddTransparentButtonRules(rules);
            else
                AddButtonColorRules(rules, KsTguiColors.ButtonClass(color), color, KsTguiColors.Text(color));
        }
    }

    private void AddButtonColorRules(List<StyleRule> rules, string styleClass, KsTguiColor color, Color text)
    {
        rules.AddRange(new StyleRule[]
        {
            // Where hover fades start from, as for every button.
            CButton().Class(styleClass).Prop(StylePropertyNormalColor, KsTguiColors.Element(color)),
            CButton().Class(styleClass).PseudoNormal().Modulate(KsTguiColors.Element(color)),
            CButton().Class(styleClass).PseudoHovered().Modulate(KsTguiColors.Hovered(color)),
            CButton().Class(styleClass).PseudoPressed().Modulate(KsTguiColors.Pressed(color)),
            CButton().Class(styleClass).PseudoDisabled().Modulate(KsTguiColors.Disabled(color)),
            CButton().Class(styleClass).ParentOf(E<Label>()).FontColor(text),
        });
    }

    private void AddTransparentButtonRules(List<StyleRule> rules)
    {
        var styleClass = KsTguiColors.ButtonClass(KsTguiColor.Transparent);

        // tgui: hover turns the text solid and washes the background white .1; :active is the same at opacity .75.
        var pressedWash = KsTguiPalettes.Translucent(Color.White, 0.075f, _palette.Base);
        var pressedText = KsTguiPalettes.Translucent(Color.White, 0.75f, _palette.Base);

        // Disabled: --color-bad text on a --color-bad wash at .2, never dimmed.
        var bad = KsTguiColors.Shade(KsTguiColor.Bad, 0f);
        var disabledWash = KsTguiPalettes.Translucent(bad, 0.2f, _palette.Base);

        rules.AddRange(new StyleRule[]
        {
            CButton().Class(styleClass).Prop(StylePropertyNormalColor, Clear),
            CButton().Class(styleClass).PseudoNormal().Modulate(Clear),
            CButton().Class(styleClass).PseudoHovered().Modulate(Border),
            CButton().Class(styleClass).PseudoPressed().Modulate(pressedWash),
            CButton().Class(styleClass).PseudoDisabled().Modulate(disabledWash),

            // The text is the colour: white at .5, and solid on hover.
            CButton().Class(styleClass).ParentOf(E<Label>()).FontColor(White50),
            CButton().Class(styleClass).PseudoHovered().ParentOf(E<Label>()).FontColor(Color.White),
            CButton().Class(styleClass).PseudoPressed().ParentOf(E<Label>()).FontColor(pressedText),
            CButton().Class(styleClass).PseudoDisabled().ParentOf(E<Label>()).FontColor(bad),
        });

        AddSelectedTransparentButtonRules(rules, disabledWash, bad);
    }

    /// <summary>
    ///     tgui's <c>Button.Checkbox</c> look: a clear button whose text is the selected colour at +5 lightness. Hover washes
    ///         it with that colour at .1, a step lighter.
    /// </summary>
    private void AddSelectedTransparentButtonRules(List<StyleRule> rules, Color disabledWash, Color bad)
    {
        var styleClass = StyleClassButtonSelectedTransparent;
        var text = _palette.SelectedButton.Hovered;
        var hoverWash = KsTguiPalettes.Translucent(KsTguiFeelUIController.ShiftLightness(text, 0.1f), 0.1f, _palette.Base);
        var pressedWash = KsTguiPalettes.Translucent(KsTguiFeelUIController.ShiftLightness(text, -0.075f), 0.075f, _palette.Base);
        var pressedText = KsTguiPalettes.Translucent(text, 0.75f, _palette.Base);

        rules.AddRange(new StyleRule[]
        {
            CButton().Class(styleClass).Prop(StylePropertyNormalColor, Clear),
            CButton().Class(styleClass).PseudoNormal().Modulate(Clear),
            CButton().Class(styleClass).PseudoHovered().Modulate(hoverWash),
            CButton().Class(styleClass).PseudoPressed().Modulate(pressedWash),
            CButton().Class(styleClass).PseudoDisabled().Modulate(disabledWash),

            CButton().Class(styleClass).ParentOf(E<Label>()).FontColor(text),
            CButton().Class(styleClass).PseudoPressed().ParentOf(E<Label>()).FontColor(pressedText),
            CButton().Class(styleClass).PseudoDisabled().ParentOf(E<Label>()).FontColor(bad),
        });
    }

    private void AddButtonVariantRules(List<StyleRule> rules)
    {
        // tgui's circular: a radius of 99999px, which the box clamps to half its height.
        rules.Add(CButton().Class(StyleClassButtonCircular).Box(ButtonBox(radius: 99999f)));
    }
}
