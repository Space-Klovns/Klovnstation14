using Content.Shared._KS14.Sensors;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Sensors.UI;

public sealed partial class KsDatalinkTransmitterBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private KsDatalinkTransmitterWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsDatalinkTransmitterWindow>();
        _window.OnTogglePressed += () => SendMessage(new KsDatalinkToggleMessage());
        _window.OnFrequencyChanged += frequency => SendMessage(new KsDatalinkSetFrequencyMessage(frequency));
        _window.OnPowerChanged += powerFraction => SendMessage(new KsDatalinkSetPowerMessage(powerFraction));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not KsDatalinkTransmitterBuiState transmitterState)
            return;

        _window?.UpdateState(transmitterState);
    }
}
