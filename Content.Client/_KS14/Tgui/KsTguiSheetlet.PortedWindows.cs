using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Tgui;

public sealed partial class KsTguiSheetlet
{
    /// <summary>
    ///     On the label of a <see cref="Controls.KsTguiButton"/> whose text is left aligned.
    /// </summary>
    public const string StyleClassButtonLabelLeft = "KsTguiButtonLabelLeft";

    /// <summary>
    ///     Rules for the windows laid out after their tgui interfaces.
    /// </summary>
    private void AddPortedWindowRules(List<StyleRule> rules)
    {
        // PDA, reagent dispenser: a button's text at the left. Two classes, so it beats the button label's centring.
        rules.Add(E<Label>().Class(Button.StyleClassButton).Class(StyleClassButtonLabelLeft).AlignMode(Label.AlignMode.Left));

        // S.P.A.M.: a tracked task, and the silo's asked-for and low materials, in tgui's state colours. Its - and + are
        //      transparent tgui buttons, which this sheet already draws.
        rules.AddRange(SalvageTablet.KsSalvageTabletStyleClasses.Rules(
            tracked: KsTguiPalettes.Good,
            requested: KsTguiPalettes.Average,
            low: KsTguiPalettes.Bad));
    }
}
