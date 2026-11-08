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
    /// <summary>
    ///     Gives a panel a Section body's translucent background in place of the solid one of its other classes.
    /// </summary>
    public const string StyleClassSectionBackground = "KsTguiSectionBackground";

    public const string StyleClassDropdownEntry = "KsTguiDropdownEntry";

    /// <summary>
    ///     On a focused input. A <c>LineEdit</c> has no pseudo-class for it.
    /// </summary>
    public const string StyleClassInputFocused = "KsTguiInputFocused";

    /// <summary>
    ///     On the <c>TextEdit</c> inside a <c>KsTguiTextArea</c>, which has no stylebox property of its own to be styled with.
    /// </summary>
    public const string StyleClassTextArea = "KsTguiTextArea";

    /// <summary>
    ///     A scroll bar drawn as CSS <c>scrollbar-width: thin</c>: narrower, with no arrows.
    /// </summary>
    public const string StyleClassThinScrollBar = "KsTguiScrollBarThin";

    /// <summary>
    ///     tgui's <c>Input--monospace</c>: the input's text in <c>--font-family-mono</c>. On a <c>LineEdit</c> or <c>TextEdit</c>.
    /// </summary>
    public const string StyleClassMonospace = "KsTguiMonospace";

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
    ///     tgui's tab is 27 tall: its 15px line of text, and this above and below it.
    /// </summary>
    private const float TabVerticalMargin = 6f;

    private const float TabHeight = 27f;

    private static readonly Color Clear = Color.Black.WithAlpha(0f);

    /// <summary>
    ///     The theme this sheet is built for, set by <see cref="GetRules"/>. A sheetlet is made anew for every sheet, so it is
    ///         the sheet's own.
    /// </summary>
    private KsTguiThemePalette _palette = KsTguiThemePalette.Nanotrasen;

    private Color White50;
    private Color White75;
    private Color Dim;
    private Color Border;
    private Color MenuColor;
    private Color TabWash;
    private Color TabActiveText;
    private Color InputColor;
    private Color TooltipColor;
    private Color Hovered;
    private Color Pressed;
    private Color Selected;
    private Color InputBorderIdle;
    private Color ScrollThumb;

    private void UsePalette(KsTguiThemePalette palette)
    {
        _palette = palette;
        White50 = KsTguiPalettes.Translucent(Color.White, 0.5f, palette.Base);
        White75 = KsTguiPalettes.Translucent(Color.White, 0.75f, palette.Base);
        Dim = palette.TextDim;
        Border = palette.Border;
        MenuColor = palette.Menu;
        TabWash = palette.TabSelectedWash;
        TabActiveText = palette.TabSelectedText;
        InputColor = palette.InputBackground;
        TooltipColor = palette.Tooltip;
        Hovered = palette.Hovered;
        Pressed = palette.Pressed;
        Selected = palette.Selected;
        InputBorderIdle = palette.InputBorderIdle;
        ScrollThumb = palette.ScrollThumb;
    }

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        UsePalette(KsTguiThemePalette.For(config));

        var rules = new List<StyleRule>();

        AddButtonRules(rules, sheet);
        AddToggleRules(rules);
        AddInputRules(rules);
        AddWindowRules(rules, sheet);
        AddPanelRules(rules);
        AddTextRules(rules, sheet);
        AddRangeRules(rules);
        AddListRules(rules, sheet);
        AddTooltipRules(rules);
        AddMenuRules(rules);
        AddScrollBarRules(rules);
        AddControlLibraryRules(rules);
        AddSurfaceRules(rules);
        AddChatPanelRules(rules);
        AddPortedWindowRules(rules);
        AddThemeRules(rules);

        return rules.ToArray();
    }

    /// <summary>
    ///     tgui's <c>--space-xs</c>, the right and bottom margin of every Button.
    /// </summary>
    private static readonly Thickness ButtonGap = new(0f, 0f, 2f, 2f);

    /// <summary>
    ///     Set on every button by this sheet alone: <see cref="Content.Client._KS14.Tgui.Controls.KsRestylingButton"/> restyles its children only under tgui.
    /// </summary>
    public const string StylePropertyRestyleChildren = "ks-tgui-restyle-children";

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

    private void AddButtonRules(List<StyleRule> rules, PalettedStylesheet sheet)
    {
        var compactBox = ButtonBox();
        compactBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 0f);
        compactBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 3f);

        rules.AddRange(new StyleRule[]
        {
            CButton().Box(ButtonBox()),

            // tg has no joined buttons: the open variants are plain rounded buttons. The top bar's MenuButtons below keep theirs.
            CButton().Class(StyleClass.ButtonOpenLeft).Box(ButtonBox()),
            CButton().Class(StyleClass.ButtonOpenRight).Box(ButtonBox()),
            CButton().Class(StyleClass.ButtonOpenBoth).Box(ButtonBox()),
            CButton().Class(StyleClass.ButtonSquare).Box(ButtonBox(radius: 0f)),
            CButton().Class(StyleClass.ButtonSmall).Box(compactBox),

            // The engine's small button has an 8pt label; tgui's compact button has the usual 12px one.
            CButton().Class(StyleClass.ButtonSmall).Class(Content.Client._KS14.Tgui.Controls.KsTguiButton.StyleClassCompact)
                .ParentOf(E<Label>()).Font(sheet.BaseFont.GetFont(12)),

            // tg's Button has margin-right and margin-bottom of --space-xs (2px). A Button, so KsTguiButton too; dropdown
            //      entries, lists and tabs are other ContainerButtons and get none. The chat filter sits in a row of its own.
            E<Button>().Class(ContainerButton.StyleClassButton).Margin(ButtonGap),
            E<Button>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Margin(0f),

            // A child of a button does not restyle when the button's pseudo-class changes; KsRestylingButton reads this.
            CButton().Prop(StylePropertyRestyleChildren, true),

            // tgui's selected button. Upstream's button palette is a step darker: that shade is tgui's held colour.
            CButton().PseudoPressed().Modulate(_palette.SelectedButton.Element),

            // Fades start from here.
            CButton().Prop(StylePropertyNormalColor, _palette.Primary.Element),
            CButton().Class(StyleClass.Positive).Prop(StylePropertyNormalColor, _palette.PositivePalette.Element),
            CButton().Class(StyleClass.Negative).Prop(StylePropertyNormalColor, _palette.NegativePalette.Element),

            CButton().PseudoDisabled().ParentOf(E<Label>()).FontColor(Dim),
            CButton().PseudoDisabled().ParentOf(E()).ParentOf(E<Label>()).FontColor(Dim),

            // The top bar's buttons never get the button class, so none of the above reaches them.
            E<MenuButton>().Box(ButtonBox()).Prop(MenuButton.KsStylePropertyChildColor, Color.White),
            E<MenuButton>().Class(StyleClass.ButtonOpenLeft).Box(OpenLeftBox()),
            E<MenuButton>().Class(StyleClass.ButtonOpenRight).Box(OpenRightBox()),
            E<MenuButton>().Class(StyleClass.ButtonOpenBoth).Box(ButtonBox(radius: 0f)),
            E<MenuButton>().Class(StyleClass.ButtonSquare).Box(ButtonBox(radius: 0f)),
            E<MenuButton>().PseudoPressed().Modulate(_palette.SelectedButton.Element),
            E<MenuButton>().Prop(StylePropertyNormalColor, _palette.Primary.Element),
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
                .Modulate(_palette.SelectedButton.Hovered),
            E<BoxContainer>().Class(CheckBox.StyleClassCheckBox).Prop(BoxContainer.StylePropertySeparation, 4),

            E<TextureRect>().Class(MonotoneCheckBox.StyleClassMonotoneCheckBox)
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, box)
                .Modulate(White50),
            E<TextureRect>().Class(MonotoneCheckBox.StyleClassMonotoneCheckBox).Class(CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, slot)
                .Prop(TextureRect.StylePropertyShader, tick)
                .Modulate(_palette.SelectedButton.Hovered),

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
                .Modulate(_palette.SelectedButton.Hovered),
        });
    }

    private KsTguiStyleBox InputBox(Color border)
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
        var focusedBox = InputBox(_palette.InputBorder);

        // --input-border-disabled, --color-gray.
        var disabledBox = InputBox(Color.FromHex("#808080"));

        // --input-color-placeholder is white at .5 over the input's own background, which is darker than the window.
        var placeholderColor = KsTguiPalettes.Translucent(Color.White, 0.5f, InputColor);
        var italic = IoCManager.Resolve<KsTguiFonts>().GetFont(12, FontKind.Italic);

        rules.AddRange(new StyleRule[]
        {
            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, inputBox),
            E<LineEdit>().Class(StyleClassInputFocused).Prop(LineEdit.StylePropertyStyleBox, focusedBox),
            E<LineEdit>().Class(ActionsWindow.StyleClassActionSearchBox).Prop(LineEdit.StylePropertyStyleBox, inputBox),

            // tg's disabled input: a grey border, and its text as dim as a placeholder. An input that can't be edited is that.
            E<LineEdit>().Class(LineEdit.StyleClassLineEditNotEditable)
                .Prop(LineEdit.StylePropertyStyleBox, disabledBox)
                .Prop("font-color", placeholderColor),
            E<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", placeholderColor)
                .Prop("font", italic),
            E<TextEdit>().Pseudo(TextEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", placeholderColor)
                .Prop("font", italic),

            // A TextEdit has no font rule of its own, so a text area's is given here; tg's Input is the sheet's 12px.
            E<TextEdit>().Class(StyleClassTextArea).Prop("font", IoCManager.Resolve<KsTguiFonts>().GetFont(12)),
            E<TextEdit>().Class(StyleClassTextArea).Pseudo(TextEdit.StylePseudoClassPlaceholder).Prop("font", italic),
            E<TextEdit>().Class(StyleClassTextArea).Pseudo(TextEdit.StylePseudoClassNotEditable).Prop("font-color", placeholderColor),

            // .Input--monospace: the mono family, and its own italic for the placeholder.
            E<LineEdit>().Class(StyleClassMonospace).Prop("font", IoCManager.Resolve<KsTguiFonts>().GetMonoFont(12)),
            E<TextEdit>().Class(StyleClassMonospace).Prop("font", IoCManager.Resolve<KsTguiFonts>().GetMonoFont(12)),
            E<LineEdit>().Class(StyleClassMonospace).Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop("font", IoCManager.Resolve<KsTguiFonts>().GetMonoFont(12, FontKind.Italic)),
            E<TextEdit>().Class(StyleClassMonospace).Pseudo(TextEdit.StylePseudoClassPlaceholder)
                .Prop("font", IoCManager.Resolve<KsTguiFonts>().GetMonoFont(12, FontKind.Italic)),

            E<TextureRect>().Class(OptionButton.StyleClassOptionTriangle)
                .Prop(TextureRect.StylePropertyTexture, Icon("glyph_slot.png"))
                .Prop(TextureRect.StylePropertyShader, GlyphShader("KsTguiGlyphChevron"))
                .Modulate(Color.White),

            // tg's dropdown: a 22 tall button, its text in a cell with 3px above and below and 6 either side, then a 22 wide
            //      cell with a 1px divider down its left edge and the chevron in it. The cell is one texture drawn by a
            //      shader, as it is what holds the button's height up. The button's own box has no padding for it.
            E<OptionButton>().Class(ContainerButton.StyleClassButton).Box(DropdownBox()).Margin(ButtonGap),
            E<OptionButton>().ParentOf(E<BoxContainer>()).ParentOf(E<TextureRect>().Class(OptionButton.StyleClassOptionTriangle))
                .Prop(TextureRect.StylePropertyTexture, Icon("glyph_dropdown_cell.png"))
                .Prop(TextureRect.StylePropertyShader, GlyphShader("KsTguiGlyphDropdownCell"))
                .Modulate(Color.White),
            E<OptionButton>().ParentOf(E<BoxContainer>()).ParentOf(E<Label>().Class(OptionButton.StyleClassOptionButton))
                .Margin(new Thickness(DropdownTextPaddingX, DropdownTextPaddingY)),
            E<Label>().Class(OptionButton.StyleClassOptionButton).AlignMode(Label.AlignMode.Left),

            // A heading's own chevron can't follow its state, so its style box draws one and the stock is hidden.
            E<CollapsibleHeading>().Box(new Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox
                { Shader = GlyphShader("KsTguiGlyphChevron") }),
            E<CollapsibleHeading>().PseudoPressed().Box(new Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox
                { Open = true, Shader = GlyphShader("KsTguiGlyphChevron") }),
            // The heading holds a box holding the chevron, so the chain is written ParentOf(box).ParentOf(chevron): the nested
            //      form never matches a grandchild, and the stock chevron stayed drawn beside the style box's.
            E<CollapsibleHeading>().ParentOf(E<BoxContainer>()).ParentOf(E<TextureRect>().Class(OptionButton.StyleClassOptionTriangle))
                .Modulate(Color.Transparent),
            E<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground).Panel(MenuBox()).Modulate(Color.White),
        });

        // 2px above and below a 16px line. KS's line is a pixel shorter than the 16 tg sets this one to.
        var entryBox = ButtonBox(radius: 3f);
        entryBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 2.5f);

        rules.AddRange(new StyleRule[]
        {
            CButton().Class(StyleClassDropdownEntry).Box(entryBox).Prop(StylePropertyNormalColor, Clear),

            // They are Buttons, so the margin every Button has would otherwise push tg's rows 2px apart.
            CButton().Class(StyleClassDropdownEntry).Margin(0f),
            CButton().Class(StyleClassDropdownEntry).PseudoNormal().Modulate(Clear),
            CButton().Class(StyleClassDropdownEntry).PseudoHovered().Modulate(Hovered),
            CButton().Class(StyleClassDropdownEntry).PseudoPressed().Modulate(Selected),
            CButton().Class(StyleClassDropdownEntry).PseudoDisabled().Modulate(Clear),
        });
    }

    /// <summary>
    ///     The text cell's padding either side of the divider, and above and below, which with 15px of text make 22.
    /// </summary>
    private const float DropdownTextPaddingX = 6f;
    private const float DropdownTextPaddingY = 3.5f;

    private static KsTguiStyleBox DropdownBox()
    {
        return new KsTguiStyleBox(Color.White, 2f);
    }

    private KsTguiStyleBox MenuBox()
    {
        return new KsTguiStyleBox(MenuColor, 6f)
        {
            Tint = _palette.MenuTint,
            BorderColor = Border,
            BorderThickness = new Thickness(1f),
        };
    }

    private void AddWindowRules(List<StyleRule> rules, PalettedStylesheet sheet)
    {
        var windowBackground = new KsTguiWindowBackgroundStyleBox(
            Icon(_palette.LogoTexture),
            _palette.BaseStart,
            _palette.BaseEnd,
            KsTguiPalettes.Translucent(Color.Black, 0.33f, _palette.Base));
        var titleBar = new KsTguiTitleBarStyleBox(_palette.TitleBar);
        var alertTitleBar = new KsTguiTitleBarStyleBox(KsTguiPalettes.NoticeBad);

        var borderedBox = new KsTguiStyleBox(_palette.Base)
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

            E<Label>().Class(DefaultWindow.StyleClassWindowTitle).Font(titleFont).FontColor(_palette.TextTitle),
            E<Label>().Class("FancyWindowTitle").Font(titleFont).FontColor(_palette.TextTitle),
            E<Label>().Class("windowTitleAlert").Font(titleFont).FontColor(Color.White),
        });
    }

    private KsTguiStyleBox SectionBox()
    {
        return new KsTguiStyleBox(_palette.Section);
    }

    private void AddPanelRules(List<StyleRule> rules)
    {
        var solidBox = new KsTguiStyleBox(_palette.Base);
        var lightBox = new KsTguiStyleBox(_palette.TitleBar);

        var sectionTitleBox = new KsTguiStyleBox(Clear)
        {
            BorderColor = _palette.PrimaryBase,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        sectionTitleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        sectionTitleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        sectionTitleBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 8f);

        var stripeBox = new KsTguiNoticeStyleBox(_palette.Notice);
        stripeBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 4f);
        stripeBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);

        rules.AddRange(new StyleRule[]
        {
            // Solid: these fill whole surfaces, such as the vote window and the main menu, with nothing behind them.
            E<PanelContainer>().Class(StyleClass.BackgroundPanel).Panel(solidBox).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.BackgroundPanelOpenLeft).Panel(solidBox).Modulate(Color.White),
            E<PanelContainer>().Class(StyleClass.BackgroundPanelOpenRight).Panel(solidBox).Modulate(Color.White),
            E<PanelContainer>().Class("BackgroundDark").Panel(solidBox).Modulate(Color.White),

            // A panel that sits on a Section body, such as a bounty, is translucent like one rather than solid.
            E<PanelContainer>().Class(StyleClassSectionBackground).Panel(SectionBox()).Modulate(Color.White),

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
                .Panel(new StyleBoxFlat(_palette.Base.WithAlpha(221f / 255f))),

            E<Button>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(ButtonBox()),
            E<ContainerButton>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(ButtonBox()),

            // As Baron has them: emotes and the fancy bubble's wrapper are see-through.
            E<PanelContainer>().Class("speechBox", "emoteBox").Panel(new StyleBoxEmpty()),
            E<PanelContainer>().Class(SpeechBubble.StyleClassOuterPanel).Panel(new StyleBoxEmpty()),
        });
    }

    private void AddTextRules(List<StyleRule> rules, PalettedStylesheet sheet)
    {
        rules.AddRange(new StyleRule[]
        {
            E<Label>().Class(StyleClass.LabelHeading).Font(sheet.BaseFont.GetFont(14, FontKind.Bold)),
            E<Label>().Class(StyleClass.LabelHeadingBigger).Font(sheet.BaseFont.GetFont(16, FontKind.Bold)),
            E<Label>().Class(StyleClass.LabelKeyText).Font(sheet.BaseFont.GetFont(12)).FontColor(_palette.Label),
            E<Label>().Class(StyleClass.LabelSubText).FontColor(_palette.Label),
            E<Label>().Class(StyleClass.LabelWeak).FontColor(_palette.Label),

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

    private KsTguiStyleBox RangeBackgroundBox()
    {
        var box = new KsTguiStyleBox(Clear, 2f)
        {
            BorderColor = _palette.Primary.Element,
            BorderThickness = new Thickness(1f),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 10f);
        return box;
    }

    private void AddRangeRules(List<StyleRule> rules)
    {
        var fillBox = new StyleBoxFlat(_palette.Primary.Element);
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
                .Prop(TabContainer.StylePropertyPanelStyleBox, new KsTguiTabPanelStyleBox { BarHeight = TabHeight, BarColor = _palette.Section })
                .Prop(TabContainer.StylePropertyTabStyleBox, tabActiveBox)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabInactiveBox)
                .Prop(TabContainer.stylePropertyTabFontColor, TabActiveText)
                .Prop(TabContainer.StylePropertyTabFontColorInactive, Dim),
        });
    }

    private void AddListRules(List<StyleRule> rules, PalettedStylesheet sheet)
    {
        AddItemListRules(rules, sheet);
        AddListContainerRules(rules);

        var treeHovered = new StyleBoxFlat(Hovered);
        var treeSelected = new StyleBoxFlat(Selected);

        rules.AddRange(new StyleRule[]
        {
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
        });
    }

    private void AddTooltipRules(List<StyleRule> rules)
    {
        var tooltipBox = new KsTguiStyleBox(TooltipColor, 4f) { Tint = _palette.TooltipTint };
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
    private void AddMenuRules(List<StyleRule> rules)
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

    private void AddScrollBarRules(List<StyleRule> rules)
    {
        // One colour for every state: Chromium's shades for a hovered or held thumb under a custom scrollbar-color
        //      aren't written down anywhere tgui's sources reach.
        var verticalThumb = new KsTguiScrollThumbStyleBox(ScrollThumb, horizontal: false);
        var horizontalThumb = new KsTguiScrollThumbStyleBox(ScrollThumb, horizontal: true);
        var verticalTrack = new KsTguiScrollTrackStyleBox(ScrollThumb, horizontal: false);
        var horizontalTrack = new KsTguiScrollTrackStyleBox(ScrollThumb, horizontal: true);

        // With nothing to scroll a browser draws no thumb, and its arrows pale.
        var verticalDisabledThumb = new KsTguiScrollThumbStyleBox(Color.Transparent, horizontal: false);
        var thinThumb = new KsTguiScrollThumbStyleBox(ScrollThumb, horizontal: false, thin: true);
        var horizontalDisabledThumb = new KsTguiScrollThumbStyleBox(Color.Transparent, horizontal: true);
        var verticalDisabledTrack = new KsTguiScrollTrackStyleBox(ScrollThumb.WithAlpha(0.5f), horizontal: false);
        var horizontalDisabledTrack = new KsTguiScrollTrackStyleBox(ScrollThumb.WithAlpha(0.5f), horizontal: true);

        // The arrows live in the room this leaves at each end of the bar, which is also where the thumb stops.
        var verticalInset = new Thickness(0f, KsTguiScrollBarGeometry.EndInset, 0f, KsTguiScrollBarGeometry.EndInset);
        var horizontalInset = new Thickness(KsTguiScrollBarGeometry.EndInset, 0f, KsTguiScrollBarGeometry.EndInset, 0f);

        rules.AddRange(new StyleRule[]
        {
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, verticalThumb).Prop(ScrollBar.StylePropertyTrack, verticalTrack).Margin(verticalInset),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, verticalThumb),
            E<VScrollBar>().Pseudo(ScrollBar.StylePseudoClassGrabbed).Prop(ScrollBar.StylePropertyGrabber, verticalThumb),
            E<HScrollBar>().Prop(ScrollBar.StylePropertyGrabber, horizontalThumb).Prop(ScrollBar.StylePropertyTrack, horizontalTrack).Margin(horizontalInset),
            E<HScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, horizontalThumb),
            E<HScrollBar>().Pseudo(ScrollBar.StylePseudoClassGrabbed).Prop(ScrollBar.StylePropertyGrabber, horizontalThumb),

            // tgui's text area is scrollbar-width thin: a narrower bar with no arrows.
            E<VScrollBar>().Class(StyleClassThinScrollBar)
                .Prop(ScrollBar.StylePropertyGrabber, thinThumb)
                .Prop(ScrollBar.StylePropertyTrack, new StyleBoxEmpty())
                .Margin(new Thickness(0f)),
            E<VScrollBar>().Class(StyleClassThinScrollBar).PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, thinThumb),
            E<VScrollBar>().Class(StyleClassThinScrollBar).Pseudo(ScrollBar.StylePseudoClassGrabbed).Prop(ScrollBar.StylePropertyGrabber, thinThumb),

            // A bar shown with nothing to scroll (KsTguiScrollContainer.ReserveVertical): no thumb, dimmed arrows.
            E<VScrollBar>().Class(Controls.KsTguiScrollContainer.StyleClassDisabled).Prop(ScrollBar.StylePropertyGrabber, verticalDisabledThumb).Prop(ScrollBar.StylePropertyTrack, verticalDisabledTrack),
            E<HScrollBar>().Class(Controls.KsTguiScrollContainer.StyleClassDisabled).Prop(ScrollBar.StylePropertyGrabber, horizontalDisabledThumb).Prop(ScrollBar.StylePropertyTrack, horizontalDisabledTrack),
        });
    }
}
