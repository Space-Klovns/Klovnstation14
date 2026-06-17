using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Shared.Power;
using Content.Shared._KS14.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emag.Systems;
using JetBrains.Annotations;
using Content.Shared._KS14.Atmos.EntitySystems;
using Content.Shared.Atmos.EntitySystems;

namespace Content.Server._KS14.Atmos.EntitySystems;

[UsedImplicitly]
public sealed class GasGrenadeCompressorSystem : SharedGasGrenadeCompressorSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly EmagSystem _emagSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasGrenadeCompressorComponent, AtmosDeviceUpdateEvent>(OnUpdate);
    }

    private void OnUpdate(Entity<GasGrenadeCompressorComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        var powered = _powerReceiverSystem.IsPowered(entity.Owner);
        var targetPressure = entity.Comp.TargetPressure;
        _appearanceSystem.SetData(entity.Owner, PowerDeviceVisuals.Powered, entity.Comp.Enabled && powered);

        if (!entity.Comp.Enabled || !powered)
        {
            UpdateUserInterface(entity);
            return;
        }

        if (!_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.InletName, out PipeNode? inlet))
            return;

        if (entity.Comp.InsertedUid is not { } grenadeUid)
        {
            UpdateUserInterface(entity);
            return;
        }

        if (!ReleaseGasOnTriggerQuery.TryGetComponent(grenadeUid, out var releaseComponent) || releaseComponent.Air == null)
        {
            UpdateUserInterface(entity);
            return;
        }

        var grenadeAir = releaseComponent.Air;
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

        var removed = inlet.Air.Remove(molesToTransfer);

        // Whitelist check
        if (!_emagSystem.CheckFlag(entity.Owner, EmagType.Interaction))
        {
            var filteredRemoved = new GasMixture(removed.Volume) { Temperature = removed.Temperature };
            foreach (var gas in entity.Comp.GasWhitelist)
            {
                var moles = removed.GetMoles(gas);
                if (moles > 0)
                {
                    filteredRemoved.SetMoles(gas, moles);
                    removed.SetMoles(gas, 0);
                }
            }
            // Put back non-whitelisted gases
            _atmosphereSystem.Merge(inlet.Air, removed);
            removed = filteredRemoved;
        }

        _atmosphereSystem.Merge(grenadeAir, removed);
        UpdateUserInterface(entity);
    }
}
