using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.Chat.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.BaronUI;

/// <summary>
/// Provides the Baron UI chrome without extending the upstream stylesheet configuration interfaces.
/// The textures are temporary assets imported from brapmine's Baron UI implementation.
/// </summary>
public sealed class KsBaronUiSheetlet : Sheetlet<PalettedStylesheet>
{
    public const string ButtonStyle1 = "KsBaronButtonStyle1";
    public const string ButtonStyle2 = "KsBaronButtonStyle2";
    public const string ButtonStyle3 = "KsBaronButtonStyle3";
    public const string ButtonStyle4 = "KsBaronButtonStyle4";
    public const string ButtonStyle5 = "KsBaronButtonStyle5";

    public const string PanelDark1 = "KsBaronPanelDark1";
    public const string PanelDark1Clean = "KsBaronPanelDark1Clean";
    public const string PanelDark2 = "KsBaronPanelDark2";
    public const string PanelDark3 = "KsBaronPanelDark3";
    public const string PanelGrey1 = "KsBaronPanelGrey1";
    public const string PanelGrey2 = "KsBaronPanelGrey2";
    public const string PanelScreen1 = "KsBaronPanelScreen1";
    public const string PanelScreen2 = "KsBaronPanelScreen2";
    public const string PanelScreenClean = "KsBaronPanelScreenClean";
    public const string SidebarButton = "KsBaronSidebarButton";

    private static readonly ResPath TextureRoot = new("/Textures/_KS14/Interface/BaronUI");

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var regularFont = ResCache.GetFont("/Fonts/_KS14/BaronUI/FontStack-Regular.TTF", 12);
        var boldFont = ResCache.GetFont("/Fonts/_KS14/BaronUI/FontStack-Bold.ttf", 12);
        var regularFontSmall = ResCache.GetFont("/Fonts/_KS14/BaronUI/FontStack-Regular.TTF", 10);
        var regularFontLarge = ResCache.GetFont("/Fonts/_KS14/BaronUI/FontStack-Regular.TTF", 14);
        var boldFontLarge = ResCache.GetFont("/Fonts/_KS14/BaronUI/FontStack-Bold.ttf", 16);
        var boldFontLarger = ResCache.GetFont("/Fonts/_KS14/BaronUI/FontStack-Bold.ttf", 20);
        var button1 = MakeBox("button_1.png", 10, 10);
        var button2 = MakeBox("button_2.png", 8, 8);
        // KS14: use the lighter shared button chrome for the general UI treatment.
        var buttonBase = MakeBox("button_3.png", 8, 8);
        var button3 = MakeBox("button_3.png", 8, 8);
        var button4 = MakeBox("button_4.png", 16, 16);
        var button5 = MakeBox("button_5.png", 12, 12);

        var panelDark1 = MakeBox("panel_dark_1.png", 24, 8);
        var panelDark1Clean = MakeBox("panel_dark_1_clean.png", 20, 8);
        var panelDark2 = MakeBox("panel_dark_2.png", 28, 24);
        var panelDark3 = MakeBox("panel_dark_3.png", 16, 18, bottomPatch: 10, bottomContent: 12);
        var panelGrey1 = MakeBox("panel_grey_1.png", 18, 12, topPatch: 14, bottomPatch: 28,
            topContent: 14, bottomContent: 28);
        var panelGrey2 = MakeBox("panel_grey_2.png", 8, 8);
        var panelScreen1 = MakeBox("panel_screen_1.png", 24, 20, topPatch: 36, bottomPatch: 20);
        var panelScreen2 = MakeBox("panel_screen_2.png", 24, 20, leftPatch: 32, topPatch: 36,
            bottomPatch: 20);
        var panelScreenClean = MakeBox("panel_screen_clean.png", 24, 20, leftPatch: 32, topPatch: 36,
            bottomPatch: 20);
        var input = MakeBox("panel_grey_2.png", 8, 6);
        var closeIcon = ResCache.GetTexture(TextureRoot / "cross.png");
        var helpIcon = ResCache.GetTexture(TextureRoot / "help.png");
        var checkboxButton = ResCache.GetTexture(TextureRoot / "button_32x32.png");
        var sliderFillBox = MakeSliderBox("slider_fill.png");
        sliderFillBox.Modulate = Color.FromHex("#5AAE63");
        var sliderBackBox = MakeSliderBox("slider_fill.png");
        sliderBackBox.Modulate = Color.FromHex("#2D3033");
        var sliderOutlineBox = MakeSliderBox("slider_outline.png");
        var sliderGrabBox = MakeSliderBox("slider.png");
        var scrollIdle = new StyleBoxFlat(Color.Gray.WithAlpha(0.05f))
        {
            ContentMarginLeftOverride = 10,
            ContentMarginTopOverride = 10,
        };
        var scrollHover = new StyleBoxFlat(Color.Gray.WithAlpha(0.35f))
        {
            ContentMarginLeftOverride = 10,
            ContentMarginTopOverride = 10,
        };

        return
        [
            E<Label>().Font(regularFont),
            E<Label>().Class("FancyWindowTitle").Font(boldFont),
            E<Label>().Class(DefaultWindow.StyleClassWindowTitle).Font(boldFont),
            // Apply Baron typography to formatted UI text such as examine entries.
            E<RichTextLabel>().Font(regularFont),
            E<Label>().Class(StyleClass.LabelHeading).Font(boldFontLarge),
            E<Label>().Class(StyleClass.LabelHeadingBigger).Font(boldFontLarger),
            E<Label>().Class(StyleClass.LabelSubText).Font(regularFontSmall),
            E<Label>().Class(StyleClass.LabelKeyText).Font(boldFont),
            E<Label>().Class(StyleClass.LabelMonospaceText).Font(regularFont),
            E<Label>().Class(StyleClass.LabelMonospaceSubHeading).Font(boldFont),
            E<Label>().Class(StyleClass.LabelMonospaceHeading).Font(boldFontLarge),
            E<Label>().Class("WindowFooterText").Font(regularFontSmall),
            E().Class(StyleClass.FontSmall).Font(regularFontSmall),
            E().Class(StyleClass.FontLarge).Font(regularFontLarge),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Box(buttonBase).Modulate(Color.White),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).PseudoHovered().Box(buttonBase).Modulate(Color.FromHex("#F5F5F5")),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).PseudoPressed().Box(buttonBase).Modulate(Color.FromHex("#D8D8D8")),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).PseudoDisabled().Box(buttonBase).Modulate(Color.FromHex("#999999")),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonOpenLeft).Box(buttonBase),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonOpenRight).Box(buttonBase),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonOpenBoth).Box(buttonBase),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonSquare).Box(buttonBase),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.Positive).Box(buttonBase),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.Negative).Box(buttonBase),
            //TODO SOOT OR LCDC: temporary checkbox button placeholder until a dedicated Baron checkbox is available.
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox)
                .Prop(TextureRect.StylePropertyTexture, checkboxButton)
                .Modulate(Color.FromHex("#C94F4F")),
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox).Class(CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, checkboxButton)
                .Modulate(Color.FromHex("#5AAE63")),
            //TODO SOOT OR LCDC: temporary checkbox button placeholder for the legacy switch control.
            E<SwitchButton>().Prop(SwitchButton.StylePropertySeparation, 10),
            E<SwitchButton>()
                .ParentOf(E<TextureRect>().Class(SwitchButton.StyleClassTrackOutline))
                .Prop(TextureRect.StylePropertyTexture, checkboxButton)
                .Modulate(Color.FromHex("#C94F4F")),
            E<SwitchButton>().PseudoPressed()
                .ParentOf(E<TextureRect>().Class(SwitchButton.StyleClassTrackOutline))
                .Prop(TextureRect.StylePropertyTexture, checkboxButton)
                .Modulate(Color.FromHex("#5AAE63")),
            E<MenuButton>().Box(buttonBase).Modulate(Color.White),
            E<MenuButton>().PseudoHovered().Box(buttonBase).Modulate(Color.FromHex("#F5F5F5")),
            E<MenuButton>().PseudoPressed().Box(buttonBase).Modulate(Color.FromHex("#D8D8D8")),
            E<MenuButton>().PseudoDisabled().Box(buttonBase).Modulate(Color.FromHex("#999999")),
            E<MenuButton>().Class(SidebarButton).Box(buttonBase).Modulate(Color.White),
            E<MenuButton>().Class(SidebarButton).PseudoHovered().Box(buttonBase).Modulate(Color.FromHex("#F5F5F5")),
            E<MenuButton>().Class(SidebarButton).PseudoPressed().Box(buttonBase).Modulate(Color.FromHex("#D8D8D8")),
            E<MenuButton>().Class(SidebarButton).PseudoDisabled().Box(buttonBase).Modulate(Color.FromHex("#999999")),
            E<ContainerButton>().Class(ButtonStyle1).Box(button1),
            E<ContainerButton>().Class(ButtonStyle2).Box(button2),
            E<ContainerButton>().Class(ButtonStyle3).Box(button3),
            E<ContainerButton>().Class(ButtonStyle4).Box(button4),
            E<ContainerButton>().Class(ButtonStyle5).Box(button5),

            // KS14: make the Baron texture the default PanelContainer chrome across menus.
            E<PanelContainer>().Panel(panelDark1Clean).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.BackgroundPanel).Panel(panelDark1).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.BackgroundPanelDark).Panel(panelDark1Clean).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.PanelDark).Panel(panelDark1Clean).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.PanelLight).Panel(panelGrey2).Modulate(Color.White),
            E<PanelContainer>().Class("WindowHeadingBackground").Panel(panelGrey1),
            E<PanelContainer>().Class("WindowContentsContainer").Panel(panelDark1Clean),
            E().Class(DefaultWindow.StyleClassWindowPanel).Panel(panelDark1),
            E().Class(DefaultWindow.StyleClassWindowHeader).Panel(panelGrey1),
            E<PanelContainer>().Class(PanelDark1).Panel(panelDark1),
            E<PanelContainer>().Class(PanelDark1Clean).Panel(panelDark1Clean),
            E<PanelContainer>().Class(PanelDark2).Panel(panelDark2),
            E<PanelContainer>().Class(PanelDark3).Panel(panelDark3),
            E<PanelContainer>().Class(PanelGrey1).Panel(panelGrey1),
            E<PanelContainer>().Class(PanelGrey2).Panel(panelGrey2),
            E<PanelContainer>().Class(PanelScreen1).Panel(panelScreen1),
            E<PanelContainer>().Class(PanelScreen2).Panel(panelScreen2),
            E<PanelContainer>().Class(PanelScreenClean).Panel(panelScreenClean),
            // KS14: make tab panels and their headers inherit Baron chrome instead of the stock treatment.
            E<TabContainer>()
                .Font(regularFont)
                .Prop(TabContainer.StylePropertyPanelStyleBox, panelDark1Clean)
                .Prop(TabContainer.StylePropertyTabStyleBox, buttonBase)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, panelDark1Clean)
                .Prop(TabContainer.stylePropertyTabFontColor, Color.White)
                .Prop(TabContainer.StylePropertyTabFontColorInactive, Color.White),
            // KS14: use Baron slider textures imported from the Baron UI reference commit.
            E<Slider>()
                .Prop(Slider.StylePropertyBackground, sliderBackBox)
                .Prop(Slider.StylePropertyForeground, sliderOutlineBox)
                .Prop(Slider.StylePropertyGrabber, sliderGrabBox)
                .Prop(Slider.StylePropertyFill, sliderFillBox),
            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, input),
            E<TextureButton>()
                .Class(DefaultWindow.StyleClassWindowCloseButton)
                .Prop(TextureButton.StylePropertyTexture, closeIcon),
            E<TextureButton>()
                .Class(FancyWindow.StyleClassWindowHelpButton)
                .Prop(TextureButton.StylePropertyTexture, helpIcon),
            // Only the otherwise-unclassified fancy-bubble wrapper is transparent; its inner speech panels retain vanilla styling.
            E<PanelContainer>().Class(SpeechBubble.StyleClassOuterPanel).Panel(new StyleBoxEmpty()),
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, scrollIdle),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, scrollHover),
            E<HScrollBar>().Prop(ScrollBar.StylePropertyGrabber, scrollIdle),
            E<HScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, scrollHover),
        ];
    }

    private StyleBoxTexture MakeBox(
        string texture,
        float patch,
        float content,
        float? leftPatch = null,
        float? topPatch = null,
        float? bottomPatch = null,
        float? topContent = null,
        float? bottomContent = null)
    {
        var box = new StyleBoxTexture
        {
            Texture = ResCache.GetTexture(TextureRoot / texture),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };

        box.SetPatchMargin(StyleBox.Margin.Left, leftPatch ?? patch);
        box.SetPatchMargin(StyleBox.Margin.Top, topPatch ?? patch);
        box.SetPatchMargin(StyleBox.Margin.Right, patch);
        box.SetPatchMargin(StyleBox.Margin.Bottom, bottomPatch ?? patch);
        box.SetPadding(StyleBox.Margin.All, 1);
        box.SetContentMarginOverride(StyleBox.Margin.Left, content);
        box.SetContentMarginOverride(StyleBox.Margin.Top, topContent ?? content);
        box.SetContentMarginOverride(StyleBox.Margin.Right, content);
        box.SetContentMarginOverride(StyleBox.Margin.Bottom, bottomContent ?? content);
        return box;
    }

    private StyleBoxTexture MakeSliderBox(string texture)
    {
        var box = new StyleBoxTexture
        {
            Texture = ResCache.GetTexture(TextureRoot / texture),
            Mode = StyleBoxTexture.StretchMode.Stretch,
        };

        // KS14: the 24px slider textures need an interior stretch region and native-height content area.
        box.SetPatchMargin(StyleBox.Margin.All, 4);
        box.SetPadding(StyleBox.Margin.All, 1);
        box.SetContentMarginOverride(StyleBox.Margin.All, 12);
        return box;
    }
}
