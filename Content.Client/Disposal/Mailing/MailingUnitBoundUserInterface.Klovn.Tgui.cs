using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Windows.Disposal;
using Content.Client.Disposal.Unit;
using Content.Client.Power.EntitySystems;
using Content.Shared.Disposal;
using Content.Shared.Disposal.Components;
using Robust.Client.UserInterface;

namespace Content.Client.Disposal.Mailing;

public sealed partial class MailingUnitBoundUserInterface
{
    [ViewVariables] private KsTguiMailingWindow? _tguiWindow;

    /// <summary>
    ///     Opens tgui's layout when the theme is on. False leaves the stock window to the caller.
    /// </summary>
    private bool OpenTgui()
    {
        if (!KsTguiTheme.ReadEnabled())
            return false;

        _tguiWindow = this.CreateWindow<KsTguiMailingWindow>();
        _tguiWindow.OpenCenteredRight();

        _tguiWindow.OnEject += () => ButtonPressed(DisposalUnitComponent.UiButton.Eject);
        _tguiWindow.OnEngage += () => ButtonPressed(DisposalUnitComponent.UiButton.Engage);
        _tguiWindow.OnPower += () => ButtonPressed(DisposalUnitComponent.UiButton.Power);
        _tguiWindow.OnTargetSelected += target => SendMessage(new TargetSelectedMessage(target));

        if (EntMan.TryGetComponent(Owner, out MailingUnitComponent? component))
            Refresh((Owner, component));

        return true;
    }

    private bool RefreshTgui(Entity<MailingUnitComponent> entity)
    {
        if (_tguiWindow == null)
            return false;

        if (EntMan.TryGetComponent(entity.Owner, out DisposalUnitComponent? disposals))
        {
            var disposalSystem = EntMan.System<DisposalUnitSystem>();

            _tguiWindow.SetState(
                disposalSystem.GetState(Owner, disposals),
                EntMan.System<PowerReceiverSystem>().IsPowered(Owner),
                disposals.Engaged,
                disposalSystem.EstimatedFullPressure(Owner, disposals));
        }

        _tguiWindow.SetTargets(entity.Comp.Tag ?? " ", entity.Comp.Target, entity.Comp.TargetList);

        return true;
    }
}
