using Content.Shared._KS14.Power.PTL;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Power.PTL;

public sealed class PtlBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PtlWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PtlWindow>();
        _window.OnTogglePressed += () => SendMessage(new PtlToggleMessage());
        _window.OnWithdrawPressed += () => SendMessage(new PtlWithdrawMessage());
        _window.OnDelayChanged += (delay) => SendMessage(new PtlSetDelayMessage(delay));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not PtlBoundUserInterfaceState ptlState)
            return;

        _window?.UpdateState(ptlState);
    }
}
