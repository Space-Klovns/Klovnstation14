using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Server._KS14.NPC.Perception;

public sealed partial class NpcPerceptionSystem
{
    [Dependency] private InventorySystem _inventorySystem = default!;

    [Dependency] private EntityQuery<InventoryComponent> _inventoryQuery = default!;

    /// <summary>
    ///     How lit something looks to <paramref name="observer"/>, given <paramref name="lightLevel"/>: what it sees in the
    ///         dark with, if anything, brings it up towards fully lit.
    /// </summary>
    public float GetSeenLightLevel(Entity<NpcPerceptionComponent?> observer, float lightLevel)
    {
        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false))
            return lightLevel;

        return lightLevel + (1f - lightLevel) * observer.Comp.DarkVision;
    }

    /// <summary>
    ///     Asks what <paramref name="uid"/> sees in the dark with. Called when that can have changed, rather than on every
    ///         look round, so an NPC walks through its worn items only when they change.
    /// </summary>
    public void RefreshDarkVision(EntityUid uid)
    {
        if (!_perceptionQuery.TryComp(uid, out var perceptionComponent))
            return;

        var ev = new NpcGetDarkVisionEvent();
        RaiseLocalEvent(uid, ref ev);

        if (_inventoryQuery.TryComp(uid, out var inventoryComponent))
            _inventorySystem.RelayEvent((uid, inventoryComponent), ref ev);

        perceptionComponent.DarkVision = ev.DarkVision;
    }

    [SubscribeLocalEvent]
    private void OnGetDarkVision(Entity<NpcDarkVisionComponent> entity, ref NpcGetDarkVisionEvent args)
    {
        args.DarkVision = MathF.Max(args.DarkVision, entity.Comp.Strength);
    }

    [SubscribeLocalEvent]
    private void OnGetDarkVisionRelayed(Entity<NpcDarkVisionComponent> entity, ref InventoryRelayedEvent<NpcGetDarkVisionEvent> args)
    {
        OnGetDarkVision(entity, ref args.Args);
    }

    [SubscribeLocalEvent]
    private void OnDarkVisionEquipped(Entity<NpcDarkVisionComponent> entity, ref GotEquippedEvent args)
    {
        RefreshDarkVision(args.EquipTarget);
    }

    [SubscribeLocalEvent]
    private void OnDarkVisionUnequipped(Entity<NpcDarkVisionComponent> entity, ref GotUnequippedEvent args)
    {
        RefreshDarkVision(args.EquipTarget);
    }
}
