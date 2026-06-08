using Content.Shared._KS14.Power.PTL;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Power.PTL;

public sealed class PTLBoundUserInterface : BoundUserInterface
{
    private PTLWindow? _window;

    public PTLBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PTLWindow>();
        _window.OnTogglePressed += () => SendMessage(new PTLToggleMessage());
        _window.OnWithdrawPressed += () => SendMessage(new PTLWithdrawMessage());
        _window.OnDelayChanged += (delay) => SendMessage(new PTLSetDelayMessage(delay));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not PTLBoundUserInterfaceState ptlState)
            return;

        _window?.UpdateState(ptlState);
    }
}
