using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Windows.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.Cargo.BUI;

// The tgui layout of the console, picked when the window opens.
public sealed partial class CargoOrderConsoleBoundUserInterface
{
    [ViewVariables]
    private KsTguiCargoConsoleWindow? _ksTguiMenu;

    /// <summary>
    ///     Opens the tgui window instead of the stock one when that theme is on.
    /// </summary>
    private bool KsOpenTguiWindow()
    {
        if (!KsTguiTheme.ReadEnabled())
            return false;

        var dependencies = IoCManager.Instance!;
        var localPlayer = dependencies.Resolve<IPlayerManager>().LocalEntity;

        var orderRequester = Loc.GetString("cargo-console-paper-approver-default");
        if (EntMan.EntityExists(localPlayer))
            orderRequester = _identity.GetIdentityShortInfo(localPlayer.Value, Owner) ?? orderRequester;

        _ksTguiMenu = new KsTguiCargoConsoleWindow(
            Owner,
            EntMan,
            dependencies.Resolve<IPrototypeManager>(),
            EntMan.System<SpriteSystem>(),
            orderRequester);

        _ksTguiMenu.OnClose += Close;
        this.WithGuide(_ksTguiMenu);
        _ksTguiMenu.OnOrderApproved += ApproveOrder;
        _ksTguiMenu.OnOrderCanceled += RemoveOrder;
        _ksTguiMenu.OnOrderSubmitted += (requester, reason, product, amount) =>
        {
            if (amount < 1 || amount > OrderCapacity)
                return;

            SendMessage(new CargoConsoleAddOrderMessage(requester, reason, product, amount));
        };
        _ksTguiMenu.OnAccountAction += (account, amount) => SendMessage(new CargoConsoleWithdrawFundsMessage(account, amount));
        _ksTguiMenu.OnToggleUnboundedLimit += () => SendMessage(new CargoConsoleToggleLimitMessage());

        _ksTguiMenu.OpenCentered();
        return true;
    }

    private void KsUpdateTguiWindow(CargoConsoleInterfaceState state, EntityUid station)
    {
        _ksTguiMenu?.UpdateState(state, station);
    }
}
