using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Binary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos.Components;
using Content.Shared.Examine;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Piping.Binary.EntitySystems;

[UsedImplicitly]
public sealed partial class GasPassiveGateSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private Content.Server._KS14.Atmos.KsTransferThermoSystem _ksTransferThermoSystem = default!; // KS14
    [Dependency] private Content.Server._KS14.Fluidmos.KsLiquidFlowSystem _ksLiquidFlowSystem = default!; // KS14
    [Dependency] private NodeContainerSystem _nodeContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasPassiveGateComponent, AtmosDeviceUpdateEvent>(OnPassiveGateUpdated);
        SubscribeLocalEvent<GasPassiveGateComponent, ExaminedEvent>(OnExamined);
    }

    private void OnPassiveGateUpdated(EntityUid uid, GasPassiveGateComponent gate, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNodes(uid, gate.InletName, gate.OutletName, out PipeNode? inlet, out PipeNode? outlet))
            return;

        // ReSharper disable thrice InconsistentNaming
        var P1 = inlet.Air.Pressure;
        var P2 = outlet.Air.Pressure;
        var V1 = inlet.Air.Volume;
        var pressureDelta = P1 - P2;

        var dt = args.dt;
        float dV = 0;
        if (pressureDelta > 0 && P1 > 0)
        {
            var transferFrac = _atmosphereSystem.FractionToEqualizePressure(inlet.Air, outlet.Air);
            dV = transferFrac * V1;

            // Actually transfer the gas.
            // KS14 start: the gas throttles, and liquid crosses by the orifice law instead of riding with it
            var ksRemoved = inlet.Air.RemoveRatio(transferFrac, includeLiquid: false);
            _ksTransferThermoSystem.Apply(ksRemoved, P1, P2, doesWork: false);
            _atmosphereSystem.Merge(outlet.Air, ksRemoved);
            // KS14 end
        }

        _ksLiquidFlowSystem.Transfer(inlet.Air, outlet.Air, gate.KsLiquidConductance, dt); // KS14

        gate.FlowRate = AtmosphereSystem.ExponentialMovingAverage(dV, gate.FlowRate, dt);
    }

    private void OnExamined(Entity<GasPassiveGateComponent> gate, ref ExaminedEvent args)
    {
        if (!Transform(gate).Anchored || !args.IsInDetailsRange) // Not anchored? Out of range? No status.
            return;

        var str = Loc.GetString("gas-passive-gate-examined", ("flowRate", $"{gate.Comp.FlowRate:0.#}"));
        args.PushMarkup(str);
    }
}
