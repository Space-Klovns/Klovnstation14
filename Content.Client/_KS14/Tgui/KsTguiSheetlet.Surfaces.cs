using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Tgui;

public sealed partial class KsTguiSheetlet
{
    /// <summary>
    ///     Rules for surfaces outside windows: radial menus and HUD widgets.
    /// </summary>
    private void AddSurfaceRules(List<StyleRule> rules)
    {
        AddRadialRules(rules);
        AddRetrofitRules(rules);
    }

    /// <summary>
    ///     tgui has no radial menu, so its tokens stand in: a dark translucent ring, the primary blue under the cursor, and
    ///         the section border between sectors. The sectors are drawn in code, so the colours go through style properties
    ///         <see cref="RadialMenuButtonWithSector"/> reads. A colour a caller sets itself still wins.
    /// </summary>
    private void AddRadialRules(List<StyleRule> rules)
    {
        rules.Add(E<RadialMenuButtonWithSector>()
            .Prop(RadialMenuButtonWithSector.KsStylePropertyBackground, KsTguiPalettes.Translucent(_palette.Base, 0.85f, Color.White))
            .Prop(RadialMenuButtonWithSector.KsStylePropertyHoverBackground, _palette.Primary.Element.WithAlpha(0.9f))
            .Prop(RadialMenuButtonWithSector.KsStylePropertyBorder, Border)
            .Prop(RadialMenuButtonWithSector.KsStylePropertyHoverBorder, _palette.PrimaryBase)
            .Prop(RadialMenuButtonWithSector.KsStylePropertySeparator, Border));
    }

    /// <summary>
    ///     Class for the label column of a label/value row in an existing window. A class of its own rather than
    ///         <c>LabelKeyText</c>, which Baron styles too.
    /// </summary>
    public const string StyleClassLabelKey = "KsTguiLabelKey";

    // Retrofit
    public const string StyleClassSectionLine = "KsTguiSectionLine";
    public const string StyleClassSectionHeading = "KsTguiSectionHeading";
    public const string StylePropertySectionLine = "ks-tgui-section-line";

    /// <summary>
    ///     On the labels of the header row of an existing list: <c>Table.Row header</c>'s bold.
    /// </summary>
    public const string StyleClassTableHeaderLabel = "KsTguiTableHeaderLabel";

    private void AddRetrofitRules(List<StyleRule> rules)
    {
        rules.Add(E<Label>().Class(StyleClassTableHeaderLabel).Font(IoCManager.Resolve<KsTguiFonts>().GetFont(12, FontKind.Bold)));

        rules.Add(E<Label>().Class(StyleClassLabelKey).FontColor(_palette.Label));

        // A heading in an existing window, as a Section's title: white.
        rules.Add(E<Label>().Class(StyleClassSectionHeading).FontColor(Color.White));

        // The separator under such a heading becomes the Section's blue underline.
        rules.Add(E<HSeparator>().Class(StyleClassSectionLine).Prop(StylePropertySectionLine, true).Prop(StylePropertySectionLineColor, _palette.PrimaryBase));
    }
}
