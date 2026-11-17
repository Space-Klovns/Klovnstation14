using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.SalvageTablet;

/// <summary>
///     The style classes of the S.P.A.M. windows. Both the tgui sheet (<see cref="Tgui.KsTguiSheetlet"/>) and the Baron sheet
///         (<see cref="BaronUI.KsBaronUiSheetlet"/>) give them rules, each in its own colours, so a live theme toggle restyles
///         them with nothing read in code.
/// </summary>
public static class KsSalvageTabletStyleClasses
{
    /// <summary>
    ///     A dark panel behind part of a task, so it reads as a label set into the card.
    /// </summary>
    public const string Plaque = "KsSalvageTabletPlaque";

    /// <summary>
    ///     The panel around a task card's contents, outlined while <see cref="Tracked"/>.
    /// </summary>
    public const string CardOutline = "KsSalvageTabletCardOutline";

    /// <summary>
    ///     With <see cref="CardOutline"/>: the task is tracked.
    /// </summary>
    public const string Tracked = "KsSalvageTabletTracked";

    /// <summary>
    ///     One material of the silo, outlined while <see cref="Requested"/> or <see cref="Low"/>.
    /// </summary>
    public const string MaterialRow = "KsSalvageTabletMaterialRow";

    /// <summary>
    ///     With <see cref="MaterialRow"/>, or on its text: the lathes have asked for the material.
    /// </summary>
    public const string Requested = "KsSalvageTabletRequested";

    /// <summary>
    ///     With <see cref="MaterialRow"/>: the silo is low on the material.
    /// </summary>
    public const string Low = "KsSalvageTabletLow";

    /// <summary>
    ///     The mark beside a material the silo is low on.
    /// </summary>
    public const string LowIcon = "KsSalvageTabletLowIcon";

    /// <summary>
    ///     The cart's bare - and + buttons. A transparent tgui button under the tgui theme already; Baron gives it no plate.
    /// </summary>
    public const string AmountButton = "KsSalvageTabletAmountButton";

    /// <summary>
    ///     Black at .35 under either theme: the plaque sits on the card's button face, not on the window, so a colour flattened
    ///         over the window base would be wrong there.
    /// </summary>
    private static readonly Color PlaqueColor = Color.Black.WithAlpha(0.35f);

    // Not Color.Transparent, which is white.
    private static readonly Color Clear = Color.Black.WithAlpha(0f);

    /// <summary>
    ///     Gives a control a class, or takes it away.
    /// </summary>
    public static void Toggle(Control control, string styleClass, bool on)
    {
        if (on == control.HasStyleClass(styleClass))
            return;

        if (on)
            control.AddStyleClass(styleClass);
        else
            control.RemoveStyleClass(styleClass);
    }

    /// <summary>
    ///     The rules both themes share the shape of, in one theme's state colours.
    /// </summary>
    public static StyleRule[] Rules(Color tracked, Color requested, Color low)
    {
        return
        [
            E<PanelContainer>().Class(Plaque).Panel(Box(PlaqueColor, Clear, 0f, 6f, 6f, 4f, 4f)).Modulate(Color.White),

            E<PanelContainer>().Class(CardOutline).Panel(Box(Clear, Clear, 2f)).Modulate(Color.White),
            E<PanelContainer>().Class(CardOutline).Class(Tracked).Panel(Box(Clear, tracked, 2f)),

            E<PanelContainer>().Class(MaterialRow).Panel(RowBox(Clear)).Modulate(Color.White),
            E<PanelContainer>().Class(MaterialRow).Class(Requested).Panel(RowBox(requested)),
            E<PanelContainer>().Class(MaterialRow).Class(Low).Panel(RowBox(low)),
            E<Label>().Class(Requested).FontColor(requested),
            E<Label>().Class(LowIcon).FontColor(low),
        ];
    }

    private static StyleBoxFlat RowBox(Color border)
    {
        return Box(Clear, border, 1f, 3f, 5f, 2f, 2f);
    }

    /// <remarks>
    ///     A margin left null is the border's thickness, as a flat box's own.
    /// </remarks>
    private static StyleBoxFlat Box(Color background, Color border, float borderThickness,
        float? left = null, float? right = null, float? top = null, float? bottom = null)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(borderThickness),
            ContentMarginLeftOverride = left,
            ContentMarginRightOverride = right,
            ContentMarginTopOverride = top,
            ContentMarginBottomOverride = bottom,
        };
    }
}
