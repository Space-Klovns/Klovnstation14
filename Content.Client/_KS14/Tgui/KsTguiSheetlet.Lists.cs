using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The standard for lists: tgui's <c>VirtualList</c> of <c>&lt;Button className="candystripe" color="transparent" fluid&gt;</c>
///         (<c>SpawnSearch.tsx</c>). Rows are a transparent Button, 20 tall with 6px of padding and a 2px margin below (a
///         fluid Button zeroes only its side margins), darkened by <c>--candystripe-odd</c> on the first, third and so on, text
///         at .5 and solid on hover, and a selected row clear with <c>--color-green</c> text at +5 lightness. SpawnSearch's
///         rows set <c>transition: none</c>, so none of this fades.
/// </summary>
public sealed partial class KsTguiSheetlet
{
    /// <summary>
    ///     The 2px between rows, a fluid Button's <c>margin-bottom</c> (<c>--space-xs</c>).
    /// </summary>
    private const int ListRowGap = 2;

    private KsTguiListColors ListColors()
    {
        var bad = KsTguiColors.Shade(KsTguiColor.Bad, 0f);
        var selectedText = _palette.SelectedButton.Hovered;

        return new KsTguiListColors(
            Stripe: _palette.Candystripe,
            Hover: Border,
            SelectedHover: KsTguiPalettes.Translucent(KsTguiFeelUIController.ShiftLightness(selectedText, 0.1f), 0.1f, _palette.Base),
            DisabledWash: KsTguiPalettes.Translucent(bad, 0.2f, _palette.Base),
            Text: White50,
            TextHover: Color.White,
            TextSelected: selectedText,
            TextDisabled: bad);
    }

    /// <summary>
    ///     The engine's <see cref="ItemList"/>. It reads one stylebox and one font colour for all its rows, so the rows are
    ///         painted by <see cref="KsTguiItemListRowStyleBox"/> (stripe, hover, selected text, and the text itself), and the
    ///         list's own text is made clear. The selector is what tells the box which lists exist.
    /// </summary>
    private void AddItemListRules(List<StyleRule> rules, PalettedStylesheet sheet)
    {
        var font = sheet.BaseFont.GetFont(12);
        var rowBox = new KsTguiItemListRowStyleBox(ListColors(), font.GetHeight(1f));

        rules.Add(new StyleRule(new KsTguiItemListSelector(),
        new StyleProperty[]
        {
            new StyleProperty(ItemList.StylePropertyBackground, SectionBox()),
            new StyleProperty(ItemList.StylePropertyItemBackground, rowBox),
            new StyleProperty(ItemList.StylePropertyDisabledItemBackground, rowBox),
            new StyleProperty(ItemList.StylePropertySelectedItemBackground, rowBox),
            new StyleProperty("font", font),
            new StyleProperty("font-color", Clear),
        }));
    }

    /// <summary>
    ///     <see cref="ListContainer"/>: every list gets the 2px between rows, and a row that carries
    ///         <see cref="ListContainer.StyleClassListContainerButton"/> is a candystripe transparent Button. The row's box is
    ///         a stylebox override, which no rule can replace, so the rules tint it and
    ///         <see cref="ListContainerButton"/> reads the rest (stripe, padding, selected and hovered classes) as properties.
    /// </summary>
    private void AddListContainerRules(List<StyleRule> rules)
    {
        var colors = ListColors();

        // tgui: hover turns the text solid and washes the row white .1; :active is the same at opacity .75.
        var pressedWash = KsTguiPalettes.Translucent(Color.White, 0.075f, _palette.Base);
        var pressedText = KsTguiPalettes.Translucent(Color.White, 0.75f, _palette.Base);

        static MutableSelectorElement Row() => E<ContainerButton>().Class(ListContainer.StyleClassListContainerButton);

        rules.AddRange(new StyleRule[]
        {
            E<ListContainer>().Prop(ListContainer.StylePropertySeparation, ListRowGap),

            // The row's box draws white and is tinted by the rules below. White leaves the tint as it's written.
            Row().Prop(ListContainerButton.KsStylePropertyBacking, Color.White)
                .Prop(ListContainerButton.KsStylePropertyStripe, colors.Stripe)
                .Prop(ListContainerButton.KsStylePropertyPadding, new Thickness(6f, 2.5f, 6f, 2.5f))
                .Prop(StylePropertyNormalColor, Clear),

            Row().PseudoNormal().Modulate(Clear),
            Row().PseudoHovered().Modulate(colors.Hover),
            Row().PseudoPressed().Modulate(pressedWash),
            Row().PseudoDisabled().Modulate(colors.DisabledWash),

            // A selected row stays clear, and lights on hover. The engine calls it pressed either way.
            Row().Class(ListContainerButton.KsStyleClassSelected).PseudoPressed().Modulate(Clear),
            Row().Class(ListContainerButton.KsStyleClassSelected).Class(ListContainerButton.KsStyleClassHovered).PseudoPressed()
                .Modulate(colors.SelectedHover),

            Row().ParentOf(E<Label>()).FontColor(colors.Text),
            Row().PseudoHovered().ParentOf(E<Label>()).FontColor(colors.TextHover),
            Row().PseudoPressed().ParentOf(E<Label>()).FontColor(pressedText),
            Row().Class(ListContainerButton.KsStyleClassSelected).ParentOf(E<Label>()).FontColor(colors.TextSelected),
            Row().PseudoDisabled().ParentOf(E<Label>()).FontColor(colors.TextDisabled),
        });
    }
}
