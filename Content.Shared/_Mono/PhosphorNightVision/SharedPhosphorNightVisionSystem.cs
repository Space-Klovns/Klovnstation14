using Content.Shared.Actions;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared._Mono.Overlays;

namespace Content.Shared._Mono.PhosphorNightVision;

/// <summary>
/// Shows/hides the <see cref="PhosphorNightVisionOverlay"/> based on whether the observed
/// entity has a <see cref="PhosphorNightVisionComponent"/> equipped.
/// </summary>
public abstract partial class SharedPhosphorNightVisionSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<PhosphorNightVisionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PhosphorNightVisionComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<PhosphorNightVisionComponent, GotEquippedEvent>(OnCompEquip);
        SubscribeLocalEvent<PhosphorNightVisionComponent, GotUnequippedEvent>(OnCompUnequip);
        SubscribeLocalEvent<PhosphorNightVisionComponent, InventoryRelayedEvent<RefreshPhosphorNightVisionEvent>>(OnRefreshEquipmentHud);
        SubscribeLocalEvent<PhosphorNightVisionComponent, RefreshPhosphorNightVisionEvent>(OnRefreshComponentHud);
        SubscribeLocalEvent<TogglePhosphorNightVisionEvent>(OnTogglePhosphorNightVisionEvent);
    }

    private void OnStartup(Entity<PhosphorNightVisionComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.RelayOverlay)
            return;

        RefreshOverlay(ent);
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnRemove(Entity<PhosphorNightVisionComponent> ent, ref ComponentRemove args)
    {
        if (ent.Comp.RelayOverlay)
            return;

        RefreshOverlay(ent);
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnCompEquip(Entity<PhosphorNightVisionComponent> ent, ref GotEquippedEvent args)
    {
        if (!ent.Comp.RelayOverlay)
            return;

        RefreshOverlay(args.EquipTarget);
        _actions.AddAction(args.EquipTarget, ref ent.Comp.ActionEntity, ent.Comp.Action, ent);
    }
    private void OnCompUnequip(Entity<PhosphorNightVisionComponent> ent, ref GotUnequippedEvent args)
    {
        if (!ent.Comp.RelayOverlay)
            return;

        ent.Comp.Enabled = false; // mono
        RefreshOverlay(ent);
    }
    protected virtual void OnRefreshEquipmentHud(Entity<PhosphorNightVisionComponent> ent, ref InventoryRelayedEvent<RefreshPhosphorNightVisionEvent> args)
    {
        OnRefreshComponentHud(ent, ref args.Args);
    }
    protected virtual void OnRefreshComponentHud(Entity<PhosphorNightVisionComponent> ent, ref RefreshPhosphorNightVisionEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        args.Entities.Add(ent);
    }

    private void OnTogglePhosphorNightVisionEvent(TogglePhosphorNightVisionEvent args)
    {
        var ent = args.Action.Comp.Container;

        if (!TryComp<PhosphorNightVisionComponent>(ent, out var nightVisionComp))
            return;

        SetEnabled(ent.Value, !nightVisionComp.Enabled, args.Performer);
        args.Handled = true;
    }

    /// <param name="ent">The night vision to toggle.</param>
    /// <param name="enabled">Whether to enable or disable.</param>
    /// <param name="viewer">Viewer of the night vision, used to refresh their overlay. If null, assumes the night vision entity is the viewer.</param>
    public void SetEnabled(Entity<PhosphorNightVisionComponent?> ent, bool enabled, EntityUid? viewer = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.Enabled == enabled)
            return;

        ent.Comp.Enabled = enabled;
        Dirty(ent);

        RefreshOverlay(viewer ?? ent);
    }

    protected virtual void RefreshOverlay(EntityUid entity) { }
}

[ByRefEvent]
public record struct RefreshPhosphorNightVisionEvent() : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
    public List<Entity<PhosphorNightVisionComponent>> Entities = new();
}
