using Content.Client._KS14.Tgui.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Tgui;

public sealed partial class KsTguiSheetlet
{
    // What the controls that draw themselves read, so that they follow the window theme this sheet was built for. Each has
    //     a fallback in the control (the default theme's), which is what Baron gets.
    public const string StylePropertyLabelColor = "ks-tgui-label";
    public const string StylePropertyInputBackground = "ks-tgui-input-background";
    public const string StylePropertyInputBorder = "ks-tgui-input-border";
    public const string StylePropertyInputBorderIdle = "ks-tgui-input-border-idle";
    public const string StylePropertyProgressColor = "ks-tgui-progress-color";
    public const string StylePropertyProgressBackdrop = "ks-tgui-progress-backdrop";
    public const string StylePropertyTabSelectedText = "ks-tgui-tab-selected-text";
    public const string StylePropertyTabSelectedWash = "ks-tgui-tab-selected-wash";
    public const string StylePropertyTabHoverWash = "ks-tgui-tab-hover-wash";
    public const string StylePropertyWindowBase = "ks-tgui-window-base";
    public const string StylePropertyDimmer = "ks-tgui-dimmer";
    public const string StylePropertyWindowDimmer = "ks-tgui-window-dimmer";
    public const string StylePropertyGaugeRing = "ks-tgui-gauge-ring";
    public const string StylePropertySectionLineColor = "ks-tgui-section-line-color";
    public const string StylePropertyTooltip = "ks-tgui-tooltip";

    /// <summary>
    ///     What differs between window themes and is not already read from the palette the stylesheet was given: the colours
    ///         that tgui's colour names resolve to (<c>primary</c>, <c>label</c>, a button's <c>caution</c> and
    ///         <c>danger</c>, and the selected state), and what the self-drawing controls take from style properties.
    ///     Comes after the control library's rules, so it overrides the default theme's values for the same classes.
    /// </summary>
    private void AddThemeRules(List<StyleRule> rules)
    {
        AddThemedButtonColorRules(rules);

        rules.AddRange(new StyleRule[]
        {
            E<KsTguiProgressBar>()
                .Prop(StylePropertyProgressColor, _palette.Primary.Element)
                .Prop(StylePropertyProgressBackdrop, _palette.ProgressBarBackdrop),

            E<KsTguiTabItem>()
                .Prop(StylePropertyTabSelectedText, _palette.TabSelectedText)
                .Prop(StylePropertyTabSelectedWash, _palette.TabSelectedWash)
                .Prop(StylePropertyTabHoverWash, _palette.TabHoverWash),

            E<KsTguiDimmer>().Prop(StylePropertyDimmer, _palette.Dimmer).Prop(StylePropertyWindowBase, _palette.Base),
            E<KsTguiWindowDimmer>().Prop(StylePropertyWindowDimmer, _palette.WindowDimmer),
            E<KsTguiModal>().Prop(StylePropertyDimmer, _palette.Dimmer).Prop(StylePropertyWindowBase, _palette.Base),
            E<KsTguiRoundGauge>().Prop(StylePropertyGaugeRing, _palette.PrimaryBase).Prop(StylePropertyWindowBase, _palette.Base),

            E<KsTguiKnob>()
                .Prop(StylePropertyGaugeRing, _palette.InputBorder)
                .Prop(StylePropertyWindowBase, _palette.Base)
                .Prop(StylePropertyTooltip, _palette.Tooltip)
                .Prop(StylePropertyInputBackground, _palette.InputBackground),

            E<KsTguiSlider>()
                .Prop(StylePropertyProgressColor, _palette.Primary.Element)
                .Prop(StylePropertyProgressBackdrop, _palette.ProgressBarBackdrop)
                .Prop(StylePropertyWindowBase, _palette.Base)
                .Prop(StylePropertyTooltip, _palette.Tooltip)
                .Prop(StylePropertyInputBackground, _palette.InputBackground),

            E<KsTguiBlockQuote>().Prop(StylePropertyLabelColor, _palette.Label),

            E<KsTguiNumberInput>()
                .Prop(StylePropertyInputBackground, _palette.InputBackground)
                .Prop(StylePropertyInputBorder, _palette.InputBorder)
                .Prop(StylePropertyInputBorderIdle, _palette.InputBorderIdle),

            E<KsTguiTextArea>()
                .Prop(StylePropertyInputBackground, _palette.InputBackground)
                .Prop(StylePropertyInputBorder, _palette.InputBorder)
                .Prop(StylePropertyInputBorderIdle, _palette.InputBorderIdle),
        });
    }

    /// <summary>
    ///     The colours of <see cref="KsTguiColor"/> that a theme sets: <c>primary</c> and <c>label</c> follow its primary,
    ///         <c>caution</c> and <c>danger</c> are its own, and a selected button is its selected colour.
    /// </summary>
    private void AddThemedButtonColorRules(List<StyleRule> rules)
    {
        AddButtonColorRules(rules, StyleClassButtonSelected, _palette.SelectedButton, Color.White);

        // tgui's low-contrast black text is for yellow and white alone, so caution is white text whatever its colour.
        AddButtonColorRules(rules, KsTguiColors.ButtonClass(KsTguiColor.Primary), _palette.Primary, Color.White);
        AddButtonColorRules(rules, KsTguiColors.ButtonClass(KsTguiColor.Label), _palette.LabelButton, Color.White);
        AddButtonColorRules(rules, KsTguiColors.ButtonClass(KsTguiColor.Caution), _palette.Caution,
            Color.White /* KS14: CautionIsYellow ? Black : White -> White, tgui's caution is not low-contrast */);
        AddButtonColorRules(rules, KsTguiColors.ButtonClass(KsTguiColor.Danger), _palette.Danger, Color.White);
    }

    private void AddButtonColorRules(List<StyleRule> rules, string styleClass, KsTguiThemePalette.ButtonShades shades, Color text)
    {
        rules.AddRange(new StyleRule[]
        {
            CButton().Class(styleClass).Prop(StylePropertyNormalColor, shades.Element),
            CButton().Class(styleClass).PseudoNormal().Modulate(shades.Element),
            CButton().Class(styleClass).PseudoHovered().Modulate(shades.Hovered),
            CButton().Class(styleClass).PseudoPressed().Modulate(shades.Pressed),
            CButton().Class(styleClass).PseudoDisabled().Modulate(shades.Disabled),
            CButton().Class(styleClass).ParentOf(E<Label>()).FontColor(text),
        });
    }
}
