using Content.Client._KS14.BaronUI;
using Content.Client._KS14.UserInterface.Styles;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     The global tgui restyle, on while <see cref="KsCCVars.TguiTheme"/> is. It re-dresses the stock pieces every
///         window is built from in tgui's colours: window chrome, buttons, text, inputs, panels, tabs, scrollbars,
///         progress bars and check boxes. Registered after the Baron sheetlet so equal selectors land on top of it,
///         and matches the Baron and upstream selectors' specificity wherever they add classes or pseudo-classes.
///         The ported tgui windows draw their own look and don't depend on this.
/// </summary>
public sealed class KsTguiSheetlet : Sheetlet<PalettedStylesheet>
{
    private const string NanoTextureRoot = "/Textures/Interface/Nano/";

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        // Sheetlets get no injection.
        if (!IoCManager.Resolve<IConfigurationManager>().GetCVar(KsCCVars.TguiTheme))
            return [];

        var regular = ResCache.GetFont(KsTguiTheme.FontRegularPath, KsTguiTheme.FontSize);
        var regularSmall = ResCache.GetFont(KsTguiTheme.FontRegularPath, KsTguiTheme.FontSize - 2);
        var regularLarge = ResCache.GetFont(KsTguiTheme.FontRegularPath, KsTguiTheme.FontSize + 2);
        var bold = ResCache.GetFont(KsTguiTheme.FontBoldPath, KsTguiTheme.FontSize);
        var boldHeading = ResCache.GetFont(KsTguiTheme.FontBoldPath, 14);
        var boldHeadingBigger = ResCache.GetFont(KsTguiTheme.FontBoldPath, 16);
        var title = ResCache.GetFont(KsTguiTheme.FontRegularPath, 14);

        var labelColor = KsTguiTheme.ToneText(KsTguiTone.Label);
        var buttonBackground = KsTguiTheme.Background(KsTguiTheme.Primary);

        var windowBackground = new KsStyleBoxGradient(KsTguiTheme.WindowTop, KsTguiTheme.WindowBottom, KsGradientDirection.TopToBottom);
        var titleBar = new StyleBoxFlat
        {
            BackgroundColor = KsTguiTheme.TitleBar,
            BorderColor = Color.Black.WithAlpha(0.33f),
            BorderThickness = new Thickness(0f, 0f, 0f, 1f),
        };
        var section = new StyleBoxFlat { BackgroundColor = KsTguiTheme.Section };
        var secondary = new StyleBoxFlat { BackgroundColor = KsTguiTheme.TitleBar };

        var input = new StyleBoxFlat
        {
            BackgroundColor = KsTguiTheme.InputBackground,
            BorderColor = KsTguiTheme.BorderPrimary,
            BorderThickness = new Thickness(1f),
        };
        input.SetContentMarginOverride(StyleBox.Margin.Horizontal, 4f);
        input.SetContentMarginOverride(StyleBox.Margin.Vertical, 2f);

        var tabSelected = new StyleBoxFlat
        {
            BackgroundColor = new Color(96 / 255f, 96 / 255f, 96 / 255f, 0.4f),
            BorderColor = Color.FromHex("#DCE6EF"),
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        tabSelected.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8f);
        tabSelected.SetContentMarginOverride(StyleBox.Margin.Vertical, 4f);
        var tabInactive = new StyleBoxFlat { BackgroundColor = Color.Black.WithAlpha(0f) };
        tabInactive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8f);
        tabInactive.SetContentMarginOverride(StyleBox.Margin.Vertical, 4f);

        var scrollGrabber = new StyleBoxFlat(KsTguiTheme.ScrollbarThumb)
        {
            ContentMarginLeftOverride = 10,
            ContentMarginTopOverride = 10,
        };
        var scrollGrabberHovered = new StyleBoxFlat(KsTguiTheme.AdjustLightness(KsTguiTheme.ScrollbarThumb, 10f))
        {
            ContentMarginLeftOverride = 10,
            ContentMarginTopOverride = 10,
        };

        var progressBackground = new StyleBoxFlat
        {
            BackgroundColor = KsTguiTheme.InputBackground,
            BorderColor = buttonBackground,
            BorderThickness = new Thickness(1f),
        };
        var progressFill = new StyleBoxFlat { BackgroundColor = buttonBackground };

        var closeTexture = ResCache.GetTexture(NanoTextureRoot + "cross.svg.png");
        var checkboxUnchecked = ResCache.GetTexture(NanoTextureRoot + "checkbox_unchecked.svg.96dpi.png");
        var checkboxChecked = ResCache.GetTexture(NanoTextureRoot + "checkbox_checked.svg.96dpi.png");

        var rules = new List<StyleRule>
        {
            // Text: Noto Sans in place of tgui's Verdana, over Baron's typeface.
            E<Label>().Font(regular),
            E<RichTextLabel>().Font(regular),
            E().Class(StyleClass.FontSmall).Font(regularSmall),
            E().Class(StyleClass.FontLarge).Font(regularLarge),
            E<Label>().Class(StyleClass.LabelHeading).Font(boldHeading).FontColor(KsTguiTheme.Text),
            E<Label>().Class(StyleClass.LabelHeadingBigger).Font(boldHeadingBigger).FontColor(KsTguiTheme.Text),
            E<Label>().Class(StyleClass.LabelKeyText).Font(bold),
            E<Label>().Class(StyleClass.LabelSubText).Font(regularSmall).FontColor(labelColor),
            E<Label>().Class(StyleClass.LabelWeak).FontColor(labelColor),

            // Window chrome: a grey title bar over tgui's dark gradient.
            E().Class(DefaultWindow.StyleClassWindowPanel).Panel(windowBackground),
            E().Class(DefaultWindow.StyleClassWindowHeader).Panel(titleBar),
            E<Label>().Class(DefaultWindow.StyleClassWindowTitle).Font(title).FontColor(KsTguiTheme.TitleText),
            E<Label>().Class("FancyWindowTitle").Font(title).FontColor(KsTguiTheme.TitleText),
            E<PanelContainer>().Class(KsBaronUiSheetlet.PanelGrey1).Panel(titleBar),
            E<PanelContainer>().Class("WindowHeadingBackground").Panel(titleBar),
            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton)
                .Prop(TextureButton.StylePropertyTexture, closeTexture),
            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).PseudoNormal().Modulate(KsTguiTheme.TextTranslucent),
            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).PseudoHovered().Modulate(KsTguiTheme.Red),
            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).PseudoPressed().Modulate(KsTguiTheme.Red),

            // Panels: windows are the gradient, anything inside is a translucent section.
            E<PanelContainer>().Panel(section),
            E<PanelContainer>().Class(StyleClass.BackgroundPanel).Panel(windowBackground),
            E<PanelContainer>().Class(StyleClass.BackgroundPanelDark).Panel(section),
            E<PanelContainer>().Class(StyleClass.PanelDark).Panel(section),
            E<PanelContainer>().Class(StyleClass.PanelLight).Panel(secondary),

            // Inputs.
            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, input),

            // Tabs: a dark strip, the open tab lit with a pale underline.
            E<TabContainer>()
                .Font(regular)
                .Prop(TabContainer.StylePropertyPanelStyleBox, section)
                .Prop(TabContainer.StylePropertyTabStyleBox, tabSelected)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabInactive)
                .Prop(TabContainer.stylePropertyTabFontColor, Color.FromHex("#DCE6EF"))
                .Prop(TabContainer.StylePropertyTabFontColorInactive, KsTguiTheme.TextTranslucent),

            // Scrollbars.
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, scrollGrabber),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, scrollGrabberHovered),
            E<HScrollBar>().Prop(ScrollBar.StylePropertyGrabber, scrollGrabber),
            E<HScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, scrollGrabberHovered),

            // Progress bars.
            E<ProgressBar>()
                .Prop(ProgressBar.StylePropertyBackground, progressBackground)
                .Prop(ProgressBar.StylePropertyForeground, progressFill),

            // Check boxes.
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox)
                .Prop(TextureRect.StylePropertyTexture, checkboxUnchecked)
                .Modulate(KsTguiTheme.TitleText),
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox).Class(CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, checkboxChecked)
                .Modulate(KsTguiTheme.Foreground(KsTguiTheme.Good)),
        };

        AddButtonRules(rules, KsTguiTheme.Primary, null);
        AddButtonRules(rules, KsTguiTheme.Good, StyleClass.Positive);
        AddButtonRules(rules, KsTguiTheme.Red, StyleClass.Negative);
        foreach (var shape in new[] { StyleClass.ButtonOpenLeft, StyleClass.ButtonOpenRight, StyleClass.ButtonOpenBoth, StyleClass.ButtonSquare })
        {
            AddButtonRules(rules, KsTguiTheme.Primary, shape);
        }

        return rules.ToArray();
    }

    /// <summary>
    ///     Flat tgui buttons in the token's background shade: lighter on hover, green while pressed or toggled on
    ///         (tgui's <c>selected</c>), and faded when disabled. ModulateSelf is reset to white in every state, or the
    ///         upstream palettes would tint the flat boxes.
    /// </summary>
    private static void AddButtonRules(List<StyleRule> rules, Color token, string? styleClass)
    {
        var background = KsTguiTheme.Background(token);
        var normal = ButtonBox(background);
        var hovered = ButtonBox(KsTguiTheme.AdjustLightness(background, 10f));
        var pressed = ButtonBox(KsTguiTheme.Background(KsTguiTheme.Green));
        var disabled = ButtonBox(background.WithAlpha(0.5f));

        rules.AddRange([
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).MaybeClass(styleClass).Box(normal).Modulate(Color.White),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).MaybeClass(styleClass).PseudoNormal().Box(normal).Modulate(Color.White),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).MaybeClass(styleClass).PseudoHovered().Box(hovered).Modulate(Color.White),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).MaybeClass(styleClass).PseudoPressed().Box(pressed).Modulate(Color.White),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).MaybeClass(styleClass).PseudoDisabled().Box(disabled).Modulate(Color.White),
        ]);
    }

    private static StyleBoxFlat ButtonBox(Color background)
    {
        var box = new StyleBoxFlat { BackgroundColor = background };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2f);
        return box;
    }
}
