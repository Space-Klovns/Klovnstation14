using Content.Shared.Actions;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared._Mono.Overlays;
using Robust.Shared.Timing;
using Content.Shared._KS14.PhosphorNightVision;

namespace Content.Shared._Mono.PhosphorNightVision;

/// <summary>
/// Shows/hides the <see cref="PhosphorNightVisionOverlay"/> based on whether the observed
/// entity has a <see cref="PhosphorNightVisionComponent"/> equipped.
/// </summary>
public abstract partial class SharedPhosphorNightVisionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PhosphorNightVisionGlowComponent, ComponentStartup>(OnGlowStartup);

        SubscribeLocalEvent<PhosphorNightVisionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PhosphorNightVisionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PhosphorNightVisionComponent, GotEquippedEvent>(OnCompEquip);
        SubscribeLocalEvent<PhosphorNightVisionComponent, GotUnequippedEvent>(OnCompUnequip);
        SubscribeLocalEvent<PhosphorNightVisionComponent, InventoryRelayedEvent<RefreshPhosphorNightVisionEvent>>(OnRefreshEquipmentHud);
        SubscribeLocalEvent<PhosphorNightVisionComponent, RefreshPhosphorNightVisionEvent>(OnRefreshComponentHud);
        SubscribeLocalEvent<TogglePhosphorNightVisionEvent>(OnTogglePhosphorNightVisionEvent);
    }

    private void OnGlowStartup(Entity<PhosphorNightVisionGlowComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.StartTime = _gameTiming.CurTime;
        Dirty(entity);
    }

    private void OnStartup(Entity<PhosphorNightVisionComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.RelayOverlay)
            return;

        RefreshOverlay(ent, activeNvEntity: ent);
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<PhosphorNightVisionComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.RelayOverlay)
            return;

        RefreshOverlay(ent, activeNvEntity: ent);
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnCompEquip(Entity<PhosphorNightVisionComponent> ent, ref GotEquippedEvent args)
    {
        if (!ent.Comp.RelayOverlay ||
            !_gameTiming.IsFirstTimePredicted)
            return;

        RefreshOverlay(args.EquipTarget, activeNvEntity: ent);
        _actions.AddAction(args.EquipTarget, ref ent.Comp.ActionEntity, ent.Comp.Action, ent);
    }
    private void OnCompUnequip(Entity<PhosphorNightVisionComponent> ent, ref GotUnequippedEvent args)
    {
        if (!ent.Comp.RelayOverlay ||
            !_gameTiming.IsFirstTimePredicted)
            return;

        ent.Comp.Enabled = false;
        RefreshOverlay(args.EquipTarget, activeNvEntity: ent);
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
        if (!_gameTiming.IsFirstTimePredicted)
            return;

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

        RefreshOverlay(viewer ?? ent, activeNvEntity: ent!);
    }

    public virtual void RefreshOverlay(EntityUid entity, Entity<PhosphorNightVisionComponent>? activeNvEntity = null) { }
}

[ByRefEvent]
public record struct RefreshPhosphorNightVisionEvent() : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
    public HashSet<Entity<PhosphorNightVisionComponent>> Entities = new();
}
