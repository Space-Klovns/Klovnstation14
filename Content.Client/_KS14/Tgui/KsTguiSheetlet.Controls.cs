using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
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
    }

    private static void AddSectionRules(List<StyleRule> rules)
    {
        // tgui's section title: 6px around the text and a 2px line under it, which counts in the bottom margin.
        var titleBox = new KsTguiStyleBox(Clear)
        {
            BorderColor = KsTguiPalettes.PrimaryBase,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        titleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        titleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        titleBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 8f);

        // A nested section's title text starts at the outer body's edge: tgui pulls it out over the outer padding.
        var nestedTitleBox = new KsTguiStyleBox(Clear)
        {
            BorderColor = KsTguiPalettes.PrimaryBase,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        nestedTitleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        nestedTitleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 0f);
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

    private static void AddNoticeBoxRules(List<StyleRule> rules)
    {
        var colors = new (KsTguiNoticeColor Name, Color Color)[]
        {
            (KsTguiNoticeColor.Default, KsTguiPalettes.Notice),
            (KsTguiNoticeColor.Info, KsTguiPalettes.NoticeInfo),
            (KsTguiNoticeColor.Success, KsTguiPalettes.NoticeGood),
            (KsTguiNoticeColor.Warning, NoticeWarning),
            (KsTguiNoticeColor.Danger, KsTguiPalettes.NoticeBad),
        };

        foreach (var (name, color) in colors)
        {
            // tgui's notice box is padded 4 by 6.
            var box = new KsTguiNoticeStyleBox(color);
            box.SetContentMarginOverride(StyleBox.Margin.Vertical, 4f);
            box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);

            rules.Add(E<PanelContainer>().Class(KsTguiNoticeBox.StyleClassNoticeBox).Class(KsTguiNoticeBox.ColorClass(name))
                .Panel(box)
                .Modulate(Color.White));
        }

        rules.Add(E<RichTextLabel>().Class(KsTguiNoticeBox.StyleClassNoticeText)
            .Font(IoCManager.Resolve<KsTguiFonts>().GetFont(12, FontKind.BoldItalic))
            .FontColor(Color.White));
    }

    private static void AddColoredButtonRules(List<StyleRule> rules)
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

    private static void AddButtonColorRules(List<StyleRule> rules, string styleClass, KsTguiColor color, Color text)
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

    private static void AddTransparentButtonRules(List<StyleRule> rules)
    {
        var styleClass = KsTguiColors.ButtonClass(KsTguiColor.Transparent);

        rules.AddRange(new StyleRule[]
        {
            CButton().Class(styleClass).Prop(StylePropertyNormalColor, Clear),
            CButton().Class(styleClass).PseudoNormal().Modulate(Clear),
            CButton().Class(styleClass).PseudoHovered().Modulate(Border),
            CButton().Class(styleClass).PseudoPressed().Modulate(Border),
            CButton().Class(styleClass).PseudoDisabled().Modulate(Clear),

            // The text is the colour: white at .5, and solid on hover.
            CButton().Class(styleClass).ParentOf(E<Label>()).FontColor(White50),
            CButton().Class(styleClass).PseudoHovered().ParentOf(E<Label>()).FontColor(Color.White),
            CButton().Class(styleClass).PseudoPressed().ParentOf(E<Label>()).FontColor(Color.White),
        });
    }
}
