using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Trinary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Audio;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Piping.Trinary.EntitySystems;

[UsedImplicitly]
public sealed partial class PressureControlledValveSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private Content.Server._KS14.Atmos.KsTransferThermoSystem _ksTransferThermoSystem = default!; // KS14
    [Dependency] private Content.Server._KS14.Fluidmos.KsLiquidFlowSystem _ksLiquidFlowSystem = default!; // KS14
    [Dependency] private SharedAmbientSoundSystem _ambientSoundSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PressureControlledValveComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PressureControlledValveComponent, AtmosDeviceUpdateEvent>(OnUpdate);
        SubscribeLocalEvent<PressureControlledValveComponent, AtmosDeviceDisabledEvent>(OnFilterLeaveAtmosphere);
    }

    private void OnInit(EntityUid uid, PressureControlledValveComponent comp, ComponentInit args)
    {
        UpdateAppearance(uid, comp);
    }

    private void OnUpdate(EntityUid uid, PressureControlledValveComponent comp, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNodes(uid, comp.InletName, comp.ControlName, comp.OutletName, out PipeNode? inletNode, out PipeNode? controlNode, out PipeNode? outletNode))
        {
            _ambientSoundSystem.SetAmbience(uid, false);
            comp.Enabled = false;
            return;
        }

        // If output is higher than input, flip input/output to enable bidirectional flow.
        if (outletNode.Air.Pressure > inletNode.Air.Pressure)
        {
            PipeNode temp = outletNode;
            outletNode = inletNode;
            inletNode = temp;
        }

        float control = (controlNode.Air.Pressure - outletNode.Air.Pressure) - comp.Threshold;
        float transferRate;
        if (control < 0)
        {
            comp.Enabled = false;
            transferRate = 0;
        }
        else
        {
            comp.Enabled = true;
            transferRate = Math.Min(control * comp.Gain, comp.MaxTransferRate * _atmosphereSystem.PumpSpeedup());
        }
        UpdateAppearance(uid, comp);

        // We multiply the transfer rate in L/s by the seconds passed since the last process to get the liters.
        var transferVolume = transferRate * args.dt;
        if (transferVolume <= 0)
        {
            _ambientSoundSystem.SetAmbience(uid, false);
            return;
        }

        // clamp to equalization so we don't overshoot (happens with silly euler)
        var maxFrac = _atmosphereSystem.FractionToEqualizePressure(inletNode.Air, outletNode.Air);
        var maxVol = inletNode.Air.Volume * maxFrac;
        var clampedVolume = Math.Min(transferVolume, maxVol);

        _ambientSoundSystem.SetAmbience(uid, true);
        // KS14 start: the gas throttles, and liquid crosses by the orifice law, as far as the valve is open
        var ksInletPressure = inletNode.Air.Pressure;
        var ksOutletPressure = outletNode.Air.Pressure;
        var removed = inletNode.Air.RemoveVolume(clampedVolume, includeLiquid: false);
        _ksTransferThermoSystem.Apply(removed, ksInletPressure, ksOutletPressure, doesWork: false);
        _atmosphereSystem.Merge(outletNode.Air, removed);
        var ksMaxVolume = comp.MaxTransferRate * _atmosphereSystem.PumpSpeedup() * args.dt;
        var ksOpening = ksMaxVolume > 0f ? Math.Clamp(transferVolume / ksMaxVolume, 0f, 1f) : 0f;
        _ksLiquidFlowSystem.Transfer(inletNode.Air, outletNode.Air, comp.KsLiquidConductance * ksOpening, args.dt);
        // KS14 end
    }

    private void OnFilterLeaveAtmosphere(EntityUid uid, PressureControlledValveComponent comp, ref AtmosDeviceDisabledEvent args)
    {
        comp.Enabled = false;
        UpdateAppearance(uid, comp);
        _ambientSoundSystem.SetAmbience(uid, false);
    }

    private void UpdateAppearance(EntityUid uid, PressureControlledValveComponent? comp = null, AppearanceComponent? appearance = null)
    {
        if (!Resolve(uid, ref comp, ref appearance, false))
            return;

        _appearance.SetData(uid, FilterVisuals.Enabled, comp.Enabled, appearance);
    }
}
