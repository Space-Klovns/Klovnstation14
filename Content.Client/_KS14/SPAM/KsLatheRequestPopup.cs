using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._KS14.SPAM;

/// <summary>
///     The menu a right click on a lathe recipe opens: one entry, which asks the salvagers for what the recipe is
///         short of.
/// </summary>
public sealed class KsLatheRequestPopup : Popup
{
    public readonly Button RequestButton;

    public KsLatheRequestPopup(bool canRequest, Action onRequest)
    {
        RequestButton = new Button
        {
            Text = Loc.GetString("ks-spam-request-button"),
            ToolTip = Loc.GetString(canRequest ? "ks-spam-request-tooltip" : "ks-spam-request-tooltip-not-short"),
            Disabled = !canRequest,
            StyleClasses = { StyleClass.ButtonSquare },
        };
        RequestButton.OnPressed += _ =>
        {
            onRequest();
            Close();
        };

        var panel = new PanelContainer { StyleClasses = { StyleClass.BackgroundPanelDark } };
        panel.AddChild(RequestButton);
        AddChild(panel);
    }

    /// <summary>
    ///     Makes a right click on the control open the menu at the mouse. A disabled button still gets the click.
    /// </summary>
    /// <param name="canRequest">Whether the recipe is short of anything right now.</param>
    public static void Attach(Control control, Func<bool> canRequest, Action onRequest)
    {
        control.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIRightClick)
                return;

            new KsLatheRequestPopup(canRequest(), onRequest).OpenAtMouse();
            args.Handle();
        };
    }
}
