using Content.Client.Stylesheets;
using Content.Shared._KS14.SalvageTablet;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._KS14.SalvageTablet;

/// <summary>
///     The menu a right click on a lathe recipe opens: one entry, which asks the salvagers for what the recipe is
///         short of. While the lathe waits out its last request, the entry says how long is left, and counts it down.
/// </summary>
public sealed class KsLatheRequestPopup : Popup
{
    public readonly Button RequestButton;

    private readonly bool _canRequest;
    private readonly Func<TimeSpan>? _cooldownLeft;
    private int _shownSeconds = -1;

    /// <param name="cooldownLeft">How long until the lathe may ask again; nothing while it may.</param>
    public KsLatheRequestPopup(bool canRequest, Action onRequest, Func<TimeSpan>? cooldownLeft = null)
    {
        _canRequest = canRequest;
        _cooldownLeft = cooldownLeft;
        RequestButton = new Button
        {
            StyleClasses = { StyleClass.ButtonSquare },
        };
        RefreshButton();
        RequestButton.OnPressed += _ =>
        {
            onRequest();
            Close();
        };

        var panel = new PanelContainer { StyleClasses = { StyleClass.BackgroundPanelDark } };
        panel.AddChild(RequestButton);
        AddChild(panel);
    }

    private void RefreshButton()
    {
        var left = _cooldownLeft?.Invoke() ?? TimeSpan.Zero;
        var seconds = Math.Max((int)Math.Ceiling(left.TotalSeconds), 0);
        if (seconds == _shownSeconds)
            return;

        _shownSeconds = seconds;
        if (seconds > 0)
        {
            var time = KsSalvageTabletTaskWindow.FormatTime(left);
            RequestButton.Text = Loc.GetString("ks-salvage-tablet-request-button-wait", ("time", time));
            RequestButton.ToolTip = Loc.GetString("ks-salvage-tablet-request-tooltip-wait", ("time", time));
            RequestButton.Disabled = true;
            return;
        }

        RequestButton.Text = Loc.GetString("ks-salvage-tablet-request-button");
        RequestButton.ToolTip = Loc.GetString(_canRequest ? "ks-salvage-tablet-request-tooltip" : "ks-salvage-tablet-request-tooltip-not-short");
        RequestButton.Disabled = !_canRequest;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        RefreshButton();
    }

    /// <summary>
    ///     How long until the lathe may ask the salvagers again, from its networked cooldown.
    /// </summary>
    public static TimeSpan CooldownLeft(EntityUid? latheUid)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        if (latheUid is not { } uid || !entityManager.TryGetComponent<KsMaterialRequestCooldownComponent>(uid, out var cooldownComponent))
            return TimeSpan.Zero;

        var left = cooldownComponent.NextRequestTime - IoCManager.Resolve<IGameTiming>().CurTime;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    ///     Makes a right click on the control open the menu at the mouse. A disabled button still gets the click.
    /// </summary>
    /// <param name="canRequest">Whether the recipe is short of anything right now.</param>
    /// <param name="latheUid">The lathe the recipe belongs to, whose cooldown the menu shows.</param>
    public static void Attach(Control control, Func<bool> canRequest, Func<EntityUid?> latheUid, Action onRequest)
    {
        control.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIRightClick)
                return;

            new KsLatheRequestPopup(canRequest(), onRequest, cooldownLeft: () => CooldownLeft(latheUid())).OpenAtMouse();
            args.Handle();
        };
    }
}
