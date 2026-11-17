using Content.Shared._KS14.Atmos;
using Robust.Client.UserInterface;

namespace Content.Client.Atmos.UI;

public sealed partial class GasThermomachineBoundUserInterface
{
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is KsGasThermomachineStatusState status)
            _window?.SetGasStatus(status.Temperature, status.Pressure);
    }
}
