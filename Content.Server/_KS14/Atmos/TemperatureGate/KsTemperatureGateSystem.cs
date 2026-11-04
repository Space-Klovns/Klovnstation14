using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.Atmos.TemperatureGate;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;

namespace Content.Server._KS14.Atmos.TemperatureGate;

/// <summary>
///     Runs <see cref="KsTemperatureGateComponent"/>s: while the inlet's temperature passes, the gate equalizes one way
///         like a passive gate.
/// </summary>
public sealed partial class KsTemperatureGateSystem : KsSharedTemperatureGateSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private Content.Server._KS14.Atmos.KsTransferThermoSystem _transferThermoSystem = default!;
    [Dependency] private KsLiquidFlowSystem _liquidFlowSystem = default!;
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private PowerReceiverSystem _powerReceiverSystem = default!;

    [SubscribeLocalEvent]
    private void OnDeviceUpdate(Entity<KsTemperatureGateComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        SetFlowing(entity, Flow(entity, args.dt));
    }

    private bool Flow(Entity<KsTemperatureGateComponent> entity, float deltaTime)
    {
        if (!entity.Comp.Enabled ||
            !_powerReceiverSystem.IsPowered(entity.Owner) ||
            !_nodeContainerSystem.TryGetNodes(entity.Owner, entity.Comp.InletName, entity.Comp.OutletName, out PipeNode? inlet, out PipeNode? outlet))
            return false;

        var temperature = inlet.Air.Temperature;
        if (entity.Comp.Inverted ? temperature <= entity.Comp.TargetTemperature : temperature >= entity.Comp.TargetTemperature)
            return false;

        var inletPressure = inlet.Air.Pressure;
        var outletPressure = outlet.Air.Pressure;
        var gasMoved = false;

        if (inletPressure > outletPressure)
        {
            var transferFraction = _atmosphereSystem.FractionToEqualizePressure(inlet.Air, outlet.Air);
            var removed = inlet.Air.RemoveRatio(transferFraction, includeLiquid: false);
            gasMoved = removed.TransferMoles > 0f;
            _transferThermoSystem.Apply(removed, inletPressure, outletPressure, doesWork: false);
            _atmosphereSystem.Merge(outlet.Air, removed);
        }

        var liquidMoved = _liquidFlowSystem.Transfer(inlet.Air, outlet.Air, entity.Comp.LiquidConductance, deltaTime);
        return gasMoved || liquidMoved > 0f;
    }
}
