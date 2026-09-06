using Content.Shared.Containers.ItemSlots;
using Content.Shared.Payload.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Weapons.Ranged.Chemsplosive;

/// <summary>
///     Lazy shit system for transferring ChemicalPayloadComponent from an ammo entity to the
///         projectile(s) fired by it.
/// </summary>
public sealed partial class ChemicalPayloadAmmoSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChemicalPayloadAmmoComponent, KsAmmoUsedEvent>(OnAmmoUsed);
    }

    private void OnAmmoUsed(Entity<ChemicalPayloadAmmoComponent> entity, ref KsAmmoUsedEvent args)
    {
        var projectileUid = args.ProjectileUids[0];
        if (args.ProjectileUids.Count > 1)
            Log.Error($"Chem-payload ammo entity {ToPrettyString(entity)} shot more than 1 projectile - only one projectile will actually be able to get a chem payload (otherwise unsupported behaviour)");

        // The projectile is a client-side predicted ghost on the client (PredictedSpawnAtPosition), while the
        // beakers are real, server-authoritative entities (they hold networked solutions). Reparenting a real
        // entity into a predicted ghost's container desyncs container/PVS state and throws a
        // MissingMetadataException once the server's real state catches up. Only let the server perform the
        // transfer; the client will receive the resulting container state over the network like normal.
        if (_netManager.IsClient)
            return;

        if (!HasComp<ItemSlotsComponent>(projectileUid))
            return;

        // should throw if there's no comp
        var ammoChemicalPayloadComponent = Comp<ChemicalPayloadComponent>(entity);
        var projectileChemicalPayloadComponent = Comp<ChemicalPayloadComponent>(projectileUid);

        if (ammoChemicalPayloadComponent.BeakerSlotA.ContainerSlot?.ContainedEntity is { } firstSlotUid)
            _containerSystem.Insert(firstSlotUid, projectileChemicalPayloadComponent.BeakerSlotA.ContainerSlot!);

        if (ammoChemicalPayloadComponent.BeakerSlotB.ContainerSlot?.ContainedEntity is { } secondSlotUid)
            _containerSystem.Insert(secondSlotUid, projectileChemicalPayloadComponent.BeakerSlotB.ContainerSlot!);

        projectileChemicalPayloadComponent.Spill = ammoChemicalPayloadComponent.Spill;
        RemComp(entity, ammoChemicalPayloadComponent);
    }
}
