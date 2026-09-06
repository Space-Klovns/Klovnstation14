using Content.Shared._KS14.Sensors;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Sensors.UI;

public sealed partial class KsDatalinkReceiverBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private KsDatalinkReceiverWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsDatalinkReceiverWindow>();
        _window.OnTogglePressed += () => SendMessage(new KsDatalinkToggleMessage());
        _window.OnFrequencyChanged += frequency => SendMessage(new KsDatalinkSetFrequencyMessage(frequency));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not KsDatalinkReceiverBuiState receiverState)
            return;

        _window?.UpdateState(receiverState);
    }
}
