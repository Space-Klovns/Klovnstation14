using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Emag.Systems;
using JetBrains.Annotations;
using Content.Shared._KS14.Atmos.EntitySystems;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Trigger.Components;

namespace Content.Server._KS14.Atmos.EntitySystems;

[UsedImplicitly]
public sealed class GasGrenadeCompressorSystem : SharedGasGrenadeCompressorSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private readonly EmagSystem _emagSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasGrenadeCompressorComponent, AtmosDeviceUpdateEvent>(OnUpdate);
    }

    private void OnUpdate(Entity<GasGrenadeCompressorComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!entity.Comp.Active)
            return;

        if (!_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.InletName, out PipeNode? inlet))
            return;

        if (entity.Comp.InsertedUid is not { } grenadeUid)
        {
            UpdateUserInterface(entity);
            return;
        }

        if (HasComp<ActiveTimerTriggerComponent>(grenadeUid))
            return;

        if (!ReleaseGasOnTriggerQuery.TryGetComponent(grenadeUid, out var releaseComponent) || releaseComponent.Air == null)
        {
            UpdateUserInterface(entity);
            return;
        }

        var grenadeAir = releaseComponent.Air;
        var targetPressure = entity.Comp.TargetPressure;
        if (grenadeAir.Pressure >= targetPressure)
        {
            UpdateUserInterface(entity);
            return;
        }

        // Transfer gas
        var transferVol = Atmospherics.MaxTransferRate * _atmosphereSystem.PumpSpeedup() * args.dt;

        // We want to fill the grenade up to TargetPressure.
        var deltaMoles = -SharedAtmosphereSystem.MolesToPressureThreshold(grenadeAir, targetPressure);
        if (deltaMoles <= 0)
        {
            UpdateUserInterface(entity);
            return;
        }

        var availableMoles = inlet.Air.TotalMoles;
        var molesToTransfer = Math.Min(deltaMoles, availableMoles);

        // Ensure we don't transfer more than the transfer rate allows
        var maxMolesByRate = (entity.Comp.TargetPressure * transferVol) / (Atmospherics.R * inlet.Air.Temperature);
        molesToTransfer = Math.Min(molesToTransfer, maxMolesByRate);

        if (molesToTransfer <= 0)
        {
            UpdateUserInterface(entity);
            return;
        }

        GasMixture removed;

        // Whitelist check
        if (!_emagSystem.CheckFlag(entity.Owner, EmagType.Interaction))
        {
            removed = new GasMixture(inlet.Air.Volume) { Temperature = inlet.Air.Temperature };
            foreach (var gas in entity.Comp.GasWhitelist)
            {
                // ratio of how much this mixture is made up of this gas * max amount we can transfer
                var molesMoved = (inlet.Air.GetMoles(gas) / availableMoles) * molesToTransfer;

                removed.SetMoles(gas, molesMoved);
                inlet.Air.AdjustMoles(gas, -molesMoved);
            }
        }
        else
            removed = inlet.Air.Remove(molesToTransfer);

        _atmosphereSystem.Merge(grenadeAir, removed);
        UpdateUserInterface(entity);
    }
}
