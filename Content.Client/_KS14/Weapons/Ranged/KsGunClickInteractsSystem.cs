using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Storage;

namespace Content.Client._KS14.Weapons.Ranged;

/// <summary>
///     Raised on what a click out of combat mode lands on, to ask whether the click interacts with it rather than firing
///         a <see cref="Content.Shared._KS14.Weapons.Ranged.KsGunFiresOutOfCombatComponent"/> gun.
/// </summary>
[ByRefEvent]
public record struct KsGunClickInteractsEvent(bool Interacts = false);

/// <summary>
///     Answers <see cref="KsGunClickInteractsEvent"/> for the things a gun that fires out of combat mode is used on
///         instead: tanks it refills from, and storage or slots it's put into.
/// </summary>
public sealed partial class KsGunClickInteractsSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnRefillable(Entity<RefillableSolutionComponent> entity, ref KsGunClickInteractsEvent args)
    {
        args.Interacts = true;
    }

    [SubscribeLocalEvent]
    private void OnDrainable(Entity<DrainableSolutionComponent> entity, ref KsGunClickInteractsEvent args)
    {
        args.Interacts = true;
    }

    [SubscribeLocalEvent]
    private void OnStorage(Entity<StorageComponent> entity, ref KsGunClickInteractsEvent args)
    {
        args.Interacts = true;
    }

    [SubscribeLocalEvent]
    private void OnItemSlots(Entity<ItemSlotsComponent> entity, ref KsGunClickInteractsEvent args)
    {
        args.Interacts = true;
    }
}
