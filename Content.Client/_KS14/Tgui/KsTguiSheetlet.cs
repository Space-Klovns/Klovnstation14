using System.Numerics;
using Content.Client.Chat.UI;
using Content.Client.ContextMenu.UI;
using Content.Client.Examine;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Controls.FancyTree;
using Content.Client.UserInterface.Systems.Actions.Windows;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The tgui theme's rules. Fills Baron's slot in the stylesheets, after every upstream sheetlet, so it only has to name
///         what <see cref="KsTguiPalettes"/> can't reach. Exact sizes and colours are tgui-core's, as resolved at 12px.
/// </summary>
/// <remarks>
///     Three things this leans on, none of which fail loudly:
///     Upstream modulates many panels, so a panel rule here sets <c>.Modulate(Color.White)</c> or its colour is multiplied.
///     Buttons are a white box coloured by the palette's per-state <c>modulate-self</c> from <c>ButtonSheetlet</c>.
///     A property written in XAML or code beats anything here, permanently, on that control.
/// </remarks>
public sealed partial class KsTguiSheetlet : Sheetlet<PalettedStylesheet>
{
    /// <summary>
    ///     A button's normal colour, for <see cref="KsTguiFeelUIController"/> to fade from. It doesn't vary by pseudo-class,
    ///         so it's still readable once a button has gone to its hover style, and a control without it isn't faded.
    /// </summary>
    public const string StylePropertyNormalColor = "ks-tgui-normal";

    /// <summary>
    ///     Added to an <c>OptionButton</c>'s entries by <see cref="KsTguiFeelUIController"/>. They are plain buttons inside an
    ///         unclassed box, so no rule can reach them through their parents.
    /// </summary>
    public const string StyleClassDropdownEntry = "KsTguiDropdownEntry";

    /// <summary>
    ///     On a focused input. A <c>LineEdit</c> has no pseudo-class for it.
    /// </summary>
    public const string StyleClassInputFocused = "KsTguiInputFocused";

    /// <summary>
    ///     The body of a window, as distinct from every other use of <see cref="StyleClass.BackgroundPanel"/>.
    /// </summary>
    public const string StyleClassWindowBackground = "KsTguiWindowBackground";

    /// <summary>
    ///     The row holding a <c>FancyWindow</c>'s title and buttons, so each theme can set its margin.
    /// </summary>
    public const string StyleClassHeaderRow = "KsFancyWindowHeaderRow";

    public static readonly ResPath TextureRoot = new("/Textures/_KS14/Interface/Tgui");

    /// <summary>
    ///     The width of a scrollbar: tgui's thin scrollbar. tgui's default is wider, but SS14's scrollbars lie on top of the
    ///         content, not beside it.
    /// </summary>
    private const float ScrollBarWidth = 11f;

    private const float ScrollBarMinLength = 20f;

    /// <summary>
    ///     tgui's tab is 27 tall: its 15px line of text, and this above and below it.
    /// </summary>
    private const float TabVerticalMargin = 6f;

    private const float TabHeight = 27f;

    private static readonly Color Clear = Color.Black.WithAlpha(0f);
    private static readonly Color White50 = KsTguiPalettes.Translucent(Color.White, 0.5f, KsTguiPalettes.Base);
    private static readonly Color White75 = KsTguiPalettes.Translucent(Color.White, 0.75f, KsTguiPalettes.Base);
    private static readonly Color Dim = KsTguiPalettes.TextDim;
    private static readonly Color Border = KsTguiPalettes.Border;
    private static readonly Color MenuColor = KsTguiPalettes.Translucent(Color.FromHex("#181818"), 0.85f, KsTguiPalettes.Base);
    private static readonly Color TabWash = KsTguiPalettes.Translucent(Color.FromHex("#606060"), 0.4f, KsTguiPalettes.Base);
    private static readonly Color TabActiveText = Color.FromHex("#DCE6EF");
    private static readonly Color InputColor = Color.FromHex("#0D0D0D");
    private static readonly Color TooltipColor = KsTguiPalettes.Translucent(InputColor, 0.85f, KsTguiPalettes.Base);
    private static readonly Color Hovered = KsTguiPalettes.Translucent(KsTguiPalettes.Wash, 0.25f, KsTguiPalettes.Base);
    private static readonly Color Pressed = KsTguiPalettes.Translucent(KsTguiPalettes.Wash, 0.3f, KsTguiPalettes.Base);
    private static readonly Color Selected = KsTguiPalettes.Translucent(KsTguiPalettes.Wash, 0.4f, KsTguiPalettes.Base);
    private static readonly Color InputBorderIdle = Color.FromHex("#6392C2");
    private static readonly Color ScrollThumb = Color.FromHex("#3E3E3E");

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var rules = new List<StyleRule>();

        AddButtonRules(rules);
        AddToggleRules(rules);
        AddInputRules(rules);
        AddWindowRules(rules, sheet);
        AddPanelRules(rules);
        AddTextRules(rules, sheet);
        AddRangeRules(rules);
        AddListRules(rules);
        AddTooltipRules(rules);
        AddMenuRules(rules);
        AddScrollBarRules(rules);
        AddControlLibraryRules(rules);
        AddSurfaceRules(rules);
        AddChatPanelRules(rules);
        AddPortedWindowRules(rules);

        return rules.ToArray();
    }

    private static MutableSelectorElement CButton()
    {
        return E<ContainerButton>().Class(ContainerButton.StyleClassButton);
    }

    private static ShaderInstance GlyphShader(string id)
    {
        return IoCManager.Resolve<IPrototypeManager>().Index<ShaderPrototype>(id).Instance();
    }

    private Texture Icon(string name)
    {
        return ResCache.GetTexture(TextureRoot / name);
    }

    private static KsTguiStyleBox ButtonBox(float radius = 2f)
    {
        var box = new KsTguiStyleBox(Color.White, radius);

        // tgui's button is 20 tall: 15 of text, and this either side. Margins and not a minimum height, so a button that
        //      states its own height keeps it.
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2.5f);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        return box;
    }

    private static KsTguiStyleBox OpenLeftBox()
    {
        var box = ButtonBox();
        box.RadiusTopLeft = box.RadiusBottomLeft = 0f;
        return box;
    }

    private static KsTguiStyleBox OpenRightBox()
    {
        var box = ButtonBox();
        box.RadiusTopRight = box.RadiusBottomRight = 0f;
        return box;
    }

    private void AddButtonRules(List<StyleRule> rules)
    {
        var compactBox = ButtonBox();
        compactBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 0f);
        compactBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 3f);

        rules.AddRange(new StyleRule[]
        {
            CButton().Box(ButtonBox()),
            CButton().Class(StyleClass.ButtonOpenLeft).Box(OpenLeftBox()),
            CButton().Class(StyleClass.ButtonOpenRight).Box(OpenRightBox()),
            CButton().Class(StyleClass.ButtonOpenBoth).Box(ButtonBox(radius: 0f)),
            CButton().Class(StyleClass.ButtonSquare).Box(ButtonBox(radius: 0f)),
            CButton().Class(StyleClass.ButtonSmall).Box(compactBox),

            // tgui's selected button. Upstream's button palette is a step darker: that shade is tgui's held colour.
            CButton().PseudoPressed().Modulate(KsTguiPalettes.SelectedElement),

            // Fades start from here.
            CButton().Prop(StylePropertyNormalColor, KsTguiPalettes.Primary.Element),
            CButton().Class(StyleClass.Positive).Prop(StylePropertyNormalColor, KsTguiPalettes.Positive.Element),
            CButton().Class(StyleClass.Negative).Prop(StylePropertyNormalColor, KsTguiPalettes.Negative.Element),

            CButton().PseudoDisabled().ParentOf(E<Label>()).FontColor(Dim),
            CButton().PseudoDisabled().ParentOf(E()).ParentOf(E<Label>()).FontColor(Dim),

            // The top bar's buttons never get the button class, so none of the above reaches them.
            E<MenuButton>().Box(ButtonBox()).Prop(MenuButton.KsStylePropertyChildColor, Color.White),
            E<MenuButton>().Class(StyleClass.ButtonOpenLeft).Box(OpenLeftBox()),
            E<MenuButton>().Class(StyleClass.ButtonOpenRight).Box(OpenRightBox()),
            E<MenuButton>().Class(StyleClass.ButtonOpenBoth).Box(ButtonBox(radius: 0f)),
            E<MenuButton>().Class(StyleClass.ButtonSquare).Box(ButtonBox(radius: 0f)),
            E<MenuButton>().PseudoPressed().Modulate(KsTguiPalettes.SelectedElement),
            E<MenuButton>().Prop(StylePropertyNormalColor, KsTguiPalettes.Primary.Element),
        });

        // Transparent buttons. Monotone ones never have the button class either.
        var transparentBox = ButtonBox();
        transparentBox.BackgroundColor = Clear;
        var transparentHoverBox = ButtonBox();
        transparentHoverBox.BackgroundColor = Border;

        foreach (var variant in new[] { null, StyleClass.ButtonOpenLeft, StyleClass.ButtonOpenRight, StyleClass.ButtonOpenBoth })
        {
            rules.Add(E<MonotoneButton>().MaybeClass(variant).Box(transparentBox));
            rules.Add(E<MonotoneButton>().MaybeClass(variant).PseudoHovered().Box(transparentHoverBox));
            rules.Add(E<MonotoneButton>().MaybeClass(variant).PseudoPressed().Box(transparentHoverBox));
        }

        // Window controls outside the chrome, as on a BaseWindow with its own frame.
        rules.AddRange(WindowGlyphRules(DefaultWindow.StyleClassWindowCloseButton, "cross_small.png"));
        rules.AddRange(WindowGlyphRules(FancyWindow.StyleClassWindowHelpButton, "question_small.png"));
    }

    private StyleRule[] WindowGlyphRules(string styleClass, string texture)
    {
        return
        [
            E<TextureButton>().Class(styleClass).Prop(TextureButton.StylePropertyTexture, Icon(texture)),
            E<TextureButton>().Class(styleClass).PseudoNormal().Modulate(White50),
            E<TextureButton>().Class(styleClass).PseudoHovered().Modulate(Color.White),
            E<TextureButton>().Class(styleClass).PseudoPressed().Modulate(White75),
            E<TextureButton>().Class(styleClass).PseudoDisabled().Modulate(Border),
        ];
    }

    private void AddToggleRules(List<StyleRule> rules)
    {
        // Shapes drawn by a shader, so they stay sharp at any UI scale. The placeholder only holds the 16px of layout.
        var slot = Icon("glyph_slot.png");
        var box = GlyphShader("KsTguiGlyphBox");
        var tick = GlyphShader("KsTguiGlyphTick");

        rules.AddRange(new StyleRule[]
        {
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox)
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, box)
                .Modulate(White50),
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox).Class(CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, tick)
                .Modulate(KsTguiPalettes.SelectedHovered),
            E<BoxContainer>().Class(CheckBox.StyleClassCheckBox).Prop(BoxContainer.StylePropertySeparation, 4),

            E<TextureRect>().Class(MonotoneCheckBox.StyleClassMonotoneCheckBox)
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, box)
                .Modulate(White50),
            E<TextureRect>().Class(MonotoneCheckBox.StyleClassMonotoneCheckBox).Class(CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, tick)
                .Modulate(KsTguiPalettes.SelectedHovered),

            // The fork draws a SwitchButton as this one texture, so it alone has to show the state.
            E<SwitchButton>().Prop(SwitchButton.StylePropertySeparation, 4),
            E<SwitchButton>()
                .ParentOf(E<TextureRect>().Class(SwitchButton.StyleClassTrackOutline))
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, box)
                .Modulate(White50),
            E<SwitchButton>().PseudoPressed()
                .ParentOf(E<TextureRect>().Class(SwitchButton.StyleClassTrackOutline))
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, tick)
                .Modulate(KsTguiPalettes.SelectedHovered),
        });
    }

    private static KsTguiStyleBox InputBox(Color border)
    {
        var box = new KsTguiStyleBox(InputColor, 2f)
        {
            BorderColor = border,
            BorderThickness = new Thickness(1f),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2f);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 5f);
        return box;
    }

    private void AddInputRules(List<StyleRule> rules)
    {
        var inputBox = InputBox(InputBorderIdle);
        var focusedBox = InputBox(KsTguiPalettes.InputBorder);

        rules.AddRange(new StyleRule[]
        {
            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, inputBox),
            E<LineEdit>().Class(StyleClassInputFocused).Prop(LineEdit.StylePropertyStyleBox, focusedBox),
            E<LineEdit>().Class(ActionsWindow.StyleClassActionSearchBox).Prop(LineEdit.StylePropertyStyleBox, inputBox),
            E<LineEdit>().Class(LineEdit.StyleClassLineEditNotEditable).Prop("font-color", Dim),
            E<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder).Prop("font-color", Dim),
            E<TextEdit>().Pseudo(TextEdit.StylePseudoClassPlaceholder).Prop("font-color", Dim),

            E<TextureRect>().Class(OptionButton.StyleClassOptionTriangle)
                .Prop(TextureRect.StylePropertyTexture, Icon("glyph_slot.png"))
                .Prop(TextureRect.StylePropertyShader, GlyphShader("KsTguiGlyphChevron"))
                .Modulate(Color.White),
            E<Label>().Class(OptionButton.StyleClassOptionButton).AlignMode(Label.AlignMode.Left),

            // A heading's own chevron can't follow its state, so its style box draws one and the stock is hidden.
            E<CollapsibleHeading>().Box(new Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox
                { Shader = GlyphShader("KsTguiGlyphChevron") }),
            E<CollapsibleHeading>().PseudoPressed().Box(new Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox
                { Open = true, Shader = GlyphShader("KsTguiGlyphChevron") }),
            E<CollapsibleHeading>().ParentOf(E<BoxContainer>().ParentOf(E<TextureRect>().Class(OptionButton.StyleClassOptionTriangle)))
                .Modulate(Color.Transparent),
            E<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground).Panel(MenuBox()).Modulate(Color.White),
        });

        var entryBox = ButtonBox(radius: 3f);
        entryBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 2f);

        rules.AddRange(new StyleRule[]
        {
            CButton().Class(StyleClassDropdownEntry).Box(entryBox).Prop(StylePropertyNormalColor, Clear),
            CButton().Class(StyleClassDropdownEntry).PseudoNormal().Modulate(Clear),
            CButton().Class(StyleClassDropdownEntry).PseudoHovered().Modulate(Hovered),
            CButton().Class(StyleClassDropdownEntry).PseudoPressed().Modulate(Selected),
            CButton().Class(StyleClassDropdownEntry).PseudoDisabled().Modulate(Clear),
        });
    }

    private static KsTguiStyleBox MenuBox()
    {
        return new KsTguiStyleBox(MenuColor, 6f)
        {
            BorderColor = Border,
            BorderThickness = new Thickness(1f),
        };
    }

    private void AddWindowRules(List<StyleRule> rules, PalettedStylesheet sheet)
    {
        var windowBackground = new KsTguiWindowBackgroundStyleBox(Icon("nanotrasen_logo.png"));
        var titleBar = new KsTguiTitleBarStyleBox(KsTguiPalettes.TitleBar);
        var alertTitleBar = new KsTguiTitleBarStyleBox(KsTguiPalettes.NoticeBad);

        var borderedBox = new KsTguiStyleBox(KsTguiPalettes.Base)
        {
            BorderColor = Border,
            BorderThickness = new Thickness(1f),
        };

        var titleFont = sheet.BaseFont.GetFont(14);

        rules.AddRange(new StyleRule[]
        {
            E<PanelContainer>().Class(StyleClass.BackgroundPanel).Class(StyleClassWindowBackground)
                .Panel(windowBackground)
                .Modulate(Color.White),
            E().Class(DefaultWindow.StyleClassWindowPanel).Panel(windowBackground),
            E().Class(DefaultWindow.StyleClassWindowHeader).Panel(titleBar),
            E().Class(StyleClass.AlertWindowHeader).Panel(alertTitleBar),
            E().Class(StyleClass.BorderedWindowPanel).Panel(borderedBox),

            E<PanelContainer>().Class("KsBaronPanelGrey1").Panel(titleBar),
            E<PanelContainer>().Class("WindowHeadingBackground").Panel(titleBar),
            E<BoxContainer>().Class(StyleClassHeaderRow).Margin(0f),

            E<Label>().Class(DefaultWindow.StyleClassWindowTitle).Font(titleFont).FontColor(KsTguiPalettes.TextTitle),
            E<Label>().Class("FancyWindowTitle").Font(titleFont).FontColor(KsTguiPalettes.TextTitle),
            E<Label>().Class("windowTitleAlert").Font(titleFont).FontColor(Color.White),
        });
    }

    private static KsTguiStyleBox SectionBox()
    {
        return new KsTguiStyleBox(KsTguiPalettes.Section);
    }

    private static void AddPanelRules(List<StyleRule> rules)
    {
        var solidBox = new KsTguiStyleBox(KsTguiPalettes.Base);
        var lightBox = new KsTguiStyleBox(KsTguiPalettes.TitleBar);

        var sectionTitleBox = new KsTguiStyleBox(Clear)
        {
            BorderColor = KsTguiPalettes.PrimaryBase,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        sectionTitleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        sectionTitleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        sectionTitleBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 8f);

        var stripeBox = new KsTguiNoticeStyleBox(KsTguiPalettes.Notice);
        stripeBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 4f);
        stripeBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);

        rules.AddRange(new StyleRule[]
        {
            // Solid: these fill whole surfaces, such as the vote window and the main menu, with nothing behind them.
            E<PanelContainer>().Class(StyleClass.BackgroundPanel).Panel(solidBox).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.BackgroundPanelOpenLeft).Panel(solidBox).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.BackgroundPanelOpenRight).Panel(solidBox).Modulate(Color.White),
            E<PanelContainer>().Class("BackgroundDark").Panel(solidBox).Modulate(Color.White),

            // tgui's Section.
            E<PanelContainer>().Class(StyleClass.BackgroundPanelDark).Panel(SectionBox()).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.PanelDark).Panel(SectionBox()).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.PanelLight).Panel(lightBox).Modulate(Color.White),

            // The separated chat's sidebar.
            E<PanelContainer>().Class("KsBaronPanelDark1").Panel(solidBox).Modulate(Color.White),

            E<PanelContainer>().Class(StyleClass.Positive).Panel(new KsTguiNoticeStyleBox(KsTguiPalettes.NoticeGood)),
            E<PanelContainer>().Class(StyleClass.Negative).Panel(new KsTguiNoticeStyleBox(KsTguiPalettes.NoticeBad)),
            E<PanelContainer>().Class(StyleClass.Highlight).Panel(new KsTguiNoticeStyleBox(KsTguiPalettes.NoticeInfo)),

            E<NanoHeading>().ParentOf(E<PanelContainer>()).Panel(sectionTitleBox),
            E<StripeBack>().Prop(StripeBack.StylePropertyBackground, stripeBox).Prop(StripeBack.KsStylePropertyFlat, true),

            // The chat panel's class is this literal string, a leftover of upstream's. ChatUIController reads the colour.
            E<PanelContainer>().Class("StyleNano.StyleClassChatPanel")
                .Panel(new StyleBoxFlat(KsTguiPalettes.Base.WithAlpha(221f / 255f))),

            E<Button>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(ButtonBox()),
            E<ContainerButton>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(ButtonBox()),

            // As Baron has them: emotes and the fancy bubble's wrapper are see-through.
            E<PanelContainer>().Class("speechBox", "emoteBox").Panel(new StyleBoxEmpty()),
            E<PanelContainer>().Class(SpeechBubble.StyleClassOuterPanel).Panel(new StyleBoxEmpty()),
        });
    }

    private static void AddTextRules(List<StyleRule> rules, PalettedStylesheet sheet)
    {
        rules.AddRange(new StyleRule[]
        {
            E<Label>().Class(StyleClass.LabelHeading).Font(sheet.BaseFont.GetFont(14, FontKind.Bold)),
            E<Label>().Class(StyleClass.LabelHeadingBigger).Font(sheet.BaseFont.GetFont(16, FontKind.Bold)),
            E<Label>().Class(StyleClass.LabelKeyText).Font(sheet.BaseFont.GetFont(12)).FontColor(KsTguiPalettes.Label),
            E<Label>().Class(StyleClass.LabelSubText).FontColor(KsTguiPalettes.Label),
            E<Label>().Class(StyleClass.LabelWeak).FontColor(KsTguiPalettes.Label),

            E<Label>().Class(StyleClass.StatusGood).FontColor(KsTguiPalettes.Good),
            E<Label>().Class(StyleClass.StatusOkay).FontColor(KsTguiPalettes.Average),
            E<Label>().Class(StyleClass.StatusWarning).FontColor(KsTguiPalettes.Average),
            E<Label>().Class(StyleClass.StatusBad).FontColor(KsTguiPalettes.Bad),
            E<Label>().Class(StyleClass.StatusCritical).FontColor(KsTguiPalettes.Bad),

            E<Label>().Class(Placeholder.StyleClassPlaceholderText).FontColor(Dim),

            // NoticeBox text.
            E<StripeBack>().ParentOf(E<Label>()).Font(sheet.BaseFont.GetFont(12, FontKind.BoldItalic)),

            // Baron's fonts lack some of the player list's glyphs, hence this class. The tgui stack doesn't.
            E<Label>().Class("KsBaronDefaultFont").Font(sheet.BaseFont.GetFont(12)),
        });
    }

    private static KsTguiStyleBox RangeBackgroundBox()
    {
        var box = new KsTguiStyleBox(Clear, 2f)
        {
            BorderColor = KsTguiPalettes.PrimaryElement,
            BorderThickness = new Thickness(1f),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 10f);
        return box;
    }

    private static void AddRangeRules(List<StyleRule> rules)
    {
        var fillBox = new StyleBoxFlat(KsTguiPalettes.PrimaryElement);
        fillBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 10f);

        var grabberBox = new KsTguiSliderGrabberStyleBox();
        grabberBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 1f);

        var tabActiveBox = new KsTguiStyleBox(TabWash)
        {
            BorderColor = TabActiveText,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
            RadiusTopLeft = 3f,
            RadiusTopRight = 3f,
        };
        tabActiveBox.SetContentMarginOverride(StyleBox.Margin.Vertical, TabVerticalMargin);
        tabActiveBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8f);

        var tabInactiveBox = new KsTguiStyleBox(Clear);
        tabInactiveBox.SetContentMarginOverride(StyleBox.Margin.Vertical, TabVerticalMargin);
        tabInactiveBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8f);

        rules.AddRange(new StyleRule[]
        {
            E<ProgressBar>()
                .Prop(ProgressBar.StylePropertyBackground, RangeBackgroundBox())
                .Prop(ProgressBar.StylePropertyForeground, fillBox),

            E<Slider>()
                .Prop(Slider.StylePropertyBackground, RangeBackgroundBox())
                .Prop(Slider.StylePropertyForeground, new StyleBoxEmpty())
                .Prop(Slider.StylePropertyFill, fillBox)
                .Prop(Slider.StylePropertyGrabber, grabberBox),

            E<TabContainer>()
                .Prop(TabContainer.StylePropertyPanelStyleBox, new KsTguiTabPanelStyleBox { BarHeight = TabHeight })
                .Prop(TabContainer.StylePropertyTabStyleBox, tabActiveBox)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabInactiveBox)
                .Prop(TabContainer.stylePropertyTabFontColor, TabActiveText)
                .Prop(TabContainer.StylePropertyTabFontColorInactive, Dim),
        });
    }

    private static StyleBoxFlat ItemBox(Color color)
    {
        var box = new StyleBoxFlat(color);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2f);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 4f);
        return box;
    }

    private static void AddListRules(List<StyleRule> rules)
    {
        var treeHovered = new StyleBoxFlat(Hovered);
        var treeSelected = new StyleBoxFlat(Selected);

        rules.AddRange(new StyleRule[]
        {
            E<ItemList>()
                .Prop(ItemList.StylePropertyBackground, SectionBox())
                .Prop(ItemList.StylePropertyItemBackground, ItemBox(Clear))
                .Prop(ItemList.StylePropertyDisabledItemBackground, ItemBox(Clear))
                .Prop(ItemList.StylePropertySelectedItemBackground, ItemBox(Selected)),

            E<TreeItem>().Panel(new StyleBoxEmpty()),
            E<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton)
                .Class(TreeItem.StyleClassEvenRow).Box(new StyleBoxEmpty()),
            E<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton)
                .Class(TreeItem.StyleClassOddRow).Box(new StyleBoxEmpty()),
            E<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton)
                .Class(TreeItem.StyleClassSelected).Box(treeSelected),
            E<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton)
                .PseudoHovered().Box(treeHovered),
            E<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton)
                .Class(TreeItem.StyleClassSelected).PseudoHovered().Box(treeSelected),

            // The list's button draws a stylebox override, tinted by the rows below. White leaves the tint as it's written.
            E<ContainerButton>().Class(ListContainer.StyleClassListContainerButton)
                .Prop(ListContainerButton.KsStylePropertyBacking, Color.White)
                .Prop(StylePropertyNormalColor, Clear),
            E<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).PseudoNormal().Modulate(Clear),
            E<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).PseudoHovered().Modulate(Hovered),
            E<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).PseudoPressed().Modulate(Selected),
            E<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).PseudoDisabled().Modulate(Clear),
        });
    }

    private static void AddTooltipRules(List<StyleRule> rules)
    {
        var tooltipBox = new KsTguiStyleBox(TooltipColor, 4f);
        tooltipBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9f);
        tooltipBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 6f);

        rules.AddRange(new StyleRule[]
        {
            E<Tooltip>().Prop(Tooltip.StylePropertyPanel, tooltipBox).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.TooltipPanel).Panel(tooltipBox).Modulate(Color.White),
            E<PanelContainer>().Class(ExamineSystem.StyleClassEntityTooltip).Panel(tooltipBox),
        });
    }

    /// <summary>
    ///     Context and verb menus. tg has no equivalent, since it uses native OS popups, so this is tgui's Dropdown menu
    ///         rather than a port. Remove this method to leave them on the palette.
    /// </summary>
    private static void AddMenuRules(List<StyleRule> rules)
    {
        var menuBox = MenuBox();
        menuBox.SetContentMarginOverride(StyleBox.Margin.All, ContextMenuElement.ElementMargin);

        rules.AddRange(new StyleRule[]
        {
            E<PanelContainer>().Class(ContextMenuPopup.StyleClassContextMenuPopup).Panel(menuBox),

            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoNormal().Modulate(Clear),
            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoHovered().Modulate(Hovered),
            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoPressed().Modulate(Pressed),
            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoDisabled().Modulate(Clear),

            E<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).PseudoNormal().Modulate(Clear),
            E<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).PseudoHovered().Modulate(Hovered),
            E<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).PseudoPressed().Modulate(Pressed),
            E<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).PseudoDisabled().Modulate(Clear),
        });
    }

    private static void AddScrollBarRules(List<StyleRule> rules)
    {
        var verticalThumb = new StyleBoxFlat(ScrollThumb)
        {
            ContentMarginLeftOverride = ScrollBarWidth,
            ContentMarginTopOverride = ScrollBarMinLength,
        };
        var horizontalThumb = new StyleBoxFlat(ScrollThumb)
        {
            ContentMarginTopOverride = ScrollBarWidth,
            ContentMarginLeftOverride = ScrollBarMinLength,
        };

        rules.AddRange(new StyleRule[]
        {
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, verticalThumb),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, verticalThumb),
            E<VScrollBar>().Pseudo(ScrollBar.StylePseudoClassGrabbed).Prop(ScrollBar.StylePropertyGrabber, verticalThumb),
            E<HScrollBar>().Prop(ScrollBar.StylePropertyGrabber, horizontalThumb),
            E<HScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, horizontalThumb),
            E<HScrollBar>().Pseudo(ScrollBar.StylePseudoClassGrabbed).Prop(ScrollBar.StylePropertyGrabber, horizontalThumb),
        });
    }
}
