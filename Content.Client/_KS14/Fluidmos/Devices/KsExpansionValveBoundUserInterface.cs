using Content.Shared._KS14.Fluidmos.Devices;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Fluidmos.Devices;

[UsedImplicitly]
public sealed class KsExpansionValveBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private KsExpansionValveWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsExpansionValveWindow>();
        _window.OpeningSet += opening => SendMessage(new KsExpansionValveSetOpeningMessage(opening));
        _window.AutomaticSet += automatic => SendMessage(new KsExpansionValveSetAutomaticMessage(automatic));
        _window.PressureSet += pressure => SendMessage(new KsExpansionValveSetPressureMessage(pressure));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is KsExpansionValveBoundUserInterfaceState valveState)
            _window?.UpdateState(valveState);
    }
}
