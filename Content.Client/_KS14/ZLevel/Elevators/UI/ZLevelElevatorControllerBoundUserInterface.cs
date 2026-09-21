using Content.Shared._KS14.ZLevel.Elevators;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.ZLevel.Elevators.UI;

[UsedImplicitly]
public sealed partial class ZLevelElevatorControllerBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ZLevelElevatorControllerMenu? _menu;

    public ZLevelElevatorControllerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<ZLevelElevatorControllerMenu>();
        _menu.OnFloorSelected += zLevel => SendMessage(new ZLevelElevatorSelectFloorMessage(zLevel));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ZLevelElevatorControllerState elevatorState)
            _menu?.UpdateState(elevatorState);
    }
}
