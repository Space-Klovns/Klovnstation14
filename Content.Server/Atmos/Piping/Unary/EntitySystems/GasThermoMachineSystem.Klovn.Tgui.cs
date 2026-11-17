using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Atmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Unary.Components;

namespace Content.Server.Atmos.Piping.Unary.EntitySystems;

public sealed partial class GasThermoMachineSystem
{
    [Dependency] private SharedUserInterfaceSystem _ksUserInterfaceSystem = default!;

    /// <summary>
    ///     Tells whoever has the window open about the gas the machine is working on, powered or not, as tg's Status section.
    /// </summary>
    private void KsUpdateStatus(EntityUid uid, GasThermoMachineComponent thermoMachine)
    {
        if (!_ksUserInterfaceSystem.IsUiOpen(uid, ThermomachineUiKey.Key))
            return;

        // Not GetHeatExchangeGasMixture: that wakes the tile it reads, and looking at a gas shouldn't.
        GasMixture? gasMixture = null;
        if (thermoMachine.Atmospheric)
            gasMixture = _atmosphereSystem.GetContainingMixture(uid);
        else if (_nodeContainer.TryGetNode(uid, thermoMachine.InletName, out PipeNode? inlet))
            gasMixture = inlet.Air;

        _ksUserInterfaceSystem.SetUiState(
            uid,
            ThermomachineUiKey.Key,
            new KsGasThermomachineStatusState(gasMixture?.Temperature ?? 0f, gasMixture?.Pressure ?? 0f));
    }
}
