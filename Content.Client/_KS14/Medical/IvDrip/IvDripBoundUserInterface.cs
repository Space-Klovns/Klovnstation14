using Content.Shared._KS14.Medical.IvDrip;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Medical.IvDrip;

public sealed partial class IvDripBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private IvDripWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<IvDripWindow>();
        _window.OnEnabledChanged += enabled => SendMessage(new IvDripSetEnabledMessage(enabled));
        _window.OnAmountChanged += amount => SendMessage(new IvDripSetAmountMessage(amount));
        _window.OnIntervalChanged += interval => SendMessage(new IvDripSetIntervalMessage(interval));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is IvDripBoundUserInterfaceState ivDripState)
            _window?.UpdateState(ivDripState);
    }
}
