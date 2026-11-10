using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Windows.Disposal;
using Content.Client.Power.EntitySystems;
using Content.Shared.Disposal.Components;
using Robust.Client.UserInterface;

namespace Content.Client.Disposal.Unit;

public sealed partial class DisposalUnitBoundUserInterface
{
    [ViewVariables] private KsTguiDisposalWindow? _tguiWindow;

    /// <summary>
    ///     Opens tgui's layout when the theme is on. False leaves the stock window to the caller.
    /// </summary>
    private bool OpenTgui()
    {
        if (!KsTguiTheme.ReadEnabled())
            return false;

        _tguiWindow = this.CreateWindow<KsTguiDisposalWindow>();
        _tguiWindow.OpenCenteredRight();

        _tguiWindow.OnEject += () => ButtonPressed(DisposalUnitComponent.UiButton.Eject);
        _tguiWindow.OnEngage += () => ButtonPressed(DisposalUnitComponent.UiButton.Engage);
        _tguiWindow.OnPower += () => ButtonPressed(DisposalUnitComponent.UiButton.Power);

        if (EntMan.TryGetComponent(Owner, out DisposalUnitComponent? component))
            Refresh((Owner, component));

        return true;
    }

    private bool RefreshTgui(Entity<DisposalUnitComponent> entity)
    {
        if (_tguiWindow == null)
            return false;

        var disposalSystem = EntMan.System<DisposalUnitSystem>();

        _tguiWindow.Title = EntMan.GetComponent<MetaDataComponent>(entity.Owner).EntityName;
        _tguiWindow.SetState(
            disposalSystem.GetState(entity.Owner, entity.Comp),
            EntMan.System<PowerReceiverSystem>().IsPowered(Owner),
            entity.Comp.Engaged,
            disposalSystem.EstimatedFullPressure(entity.Owner, entity.Comp));

        return true;
    }
}
