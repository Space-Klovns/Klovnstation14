using Content.Shared.Power;
using Content.Shared._KS14.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emag.Systems;
using Content.Shared.Materials;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger.Components.Triggers;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Shared._KS14.Atmos.EntitySystems;

[UsedImplicitly]
public abstract class SharedGasGrenadeCompressorSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ISerializationManager _serializationManager = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materialStorageSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _userInterfaceSystem = default!;

    [Dependency] protected readonly EntityQuery<ReleaseGasOnTriggerComponent> ReleaseGasOnTriggerQuery = default!;
    [Dependency] private readonly EntityQuery<MaterialStorageComponent> _materialStorageQuery = default!;

    private static readonly EntProtoId AirGrenadeId = "AirGrenade";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasGrenadeCompressorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, PowerChangedEvent>(OnPowerChanged);

        SubscribeLocalEvent<GasGrenadeCompressorComponent, EntInsertedIntoContainerMessage>(OnEntInsertedIntoContainer);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, EntRemovedFromContainerMessage>(OnEntRemovedFromContainer);

        SubscribeLocalEvent<GasGrenadeCompressorComponent, GasGrenadeCompressorChangeTargetPressureMessage>(OnChangeTargetPressure);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, GasGrenadeCompressorToggleMessage>(OnToggle);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, GasGrenadeCompressorRearmMessage>(OnRearm);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, MaterialAmountChangedEvent>(OnMaterialAmountChanged);
    }

    private void OnMapInit(Entity<GasGrenadeCompressorComponent> entity, ref MapInitEvent args)
    {
        UpdateUserInterface(entity);
    }

    private void OnPowerChanged(Entity<GasGrenadeCompressorComponent> entity, ref PowerChangedEvent args)
    {
        var wasActive = entity.Comp.Active;

        entity.Comp.Active = entity.Comp.Enabled && args.Powered;
        DirtyField(entity!, nameof(entity.Comp.Active));

        if (wasActive != entity.Comp.Active)
        {
            _appearanceSystem.SetData(entity.Owner, GasGrenadeCompressorVisuals.Active, entity.Comp.Active);
            UpdateUserInterface(entity);
        }
    }

    private void OnEntInsertedIntoContainer(Entity<GasGrenadeCompressorComponent> entity, ref EntInsertedIntoContainerMessage args)
    {
        if (entity.Comp.InsertedUid is { })
            return;

        if (!_itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.SlotName, out var slot)
            || slot.ContainerSlot != args.Container)
            return;

        entity.Comp.InsertedUid = args.Entity;
        DirtyField(entity!, nameof(entity.Comp.InsertedUid));
        UpdateUserInterface(entity);
    }

    private void OnEntRemovedFromContainer(Entity<GasGrenadeCompressorComponent> entity, ref EntRemovedFromContainerMessage args)
    {
        if (entity.Comp.InsertedUid is not { })
            return;

        if (!_itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.SlotName, out var slot)
            || slot.ContainerSlot != args.Container)
            return;

        entity.Comp.InsertedUid = null;
        DirtyField(entity!, nameof(entity.Comp.InsertedUid));
        UpdateUserInterface(entity);
    }

    protected void UpdateUserInterface(Entity<GasGrenadeCompressorComponent> entity)
    {
        if (!TryComp<UserInterfaceComponent>(entity.Owner, out var userInterfaceComponent))
            return;

        var hasGrenade = false;
        var grenadePressure = 0f;
        var isSpent = false;

        if (entity.Comp.InsertedUid is { } grenadeUid)
        {
            hasGrenade = true;
            if (ReleaseGasOnTriggerQuery.TryGetComponent(grenadeUid, out var releaseComp))
                grenadePressure = releaseComp.Air?.Pressure ?? 0;
            else
                isSpent = true;
        }

        var steelAmount = 0;
        if (_materialStorageQuery.TryGetComponent(entity, out var materialStorageComponent))
            steelAmount = _materialStorageSystem.GetMaterialAmount(entity.Owner, entity.Comp.Material, component: materialStorageComponent);

        var state = new GasGrenadeCompressorBoundUserInterfaceState(entity.Comp.TargetPressure, entity.Comp.Enabled, hasGrenade, grenadePressure, isSpent, steelAmount);
        _userInterfaceSystem.SetUiState((entity.Owner, userInterfaceComponent), GasGrenadeCompressorUiKey.Key, state);
    }

    private void OnChangeTargetPressure(Entity<GasGrenadeCompressorComponent> entity, ref GasGrenadeCompressorChangeTargetPressureMessage args)
    {
        entity.Comp.TargetPressure = Math.Clamp(args.TargetPressure, 0, entity.Comp.MaxTargetPressure);
        DirtyField(entity!, nameof(entity.Comp.TargetPressure));

        UpdateUserInterface(entity);
    }

    private void OnToggle(Entity<GasGrenadeCompressorComponent> entity, ref GasGrenadeCompressorToggleMessage args)
    {
        entity.Comp.Enabled = args.Enabled;
        entity.Comp.Active = args.Enabled && _powerReceiverSystem.IsPowered(entity.Owner);

        DirtyField(entity!, nameof(entity.Comp.Enabled));
        DirtyField(entity!, nameof(entity.Comp.Active));

        _appearanceSystem.SetData(entity.Owner, GasGrenadeCompressorVisuals.Active, entity.Comp.Active);

        UpdateUserInterface(entity);
    }

    private void OnRearm(Entity<GasGrenadeCompressorComponent> entity, ref GasGrenadeCompressorRearmMessage args)
    {
        if (!_itemSlotsSystem.TryGetSlot(entity.Owner, entity.Comp.SlotName, out var slot) || slot.Item is not { } grenadeUid)
            return;

        if (ReleaseGasOnTriggerQuery.HasComponent(grenadeUid) ||
            HasComp<ActiveTimerTriggerComponent>(grenadeUid))
            return; // Not spent

        if (!_materialStorageQuery.TryGetComponent(entity, out var materialStorageComponent) ||
            _materialStorageSystem.GetMaterialAmount(entity.Owner, entity.Comp.Material, materialStorageComponent) < 1000)
            return; // Not enough steel

        // Consume steel
        _materialStorageSystem.TryChangeMaterialAmount(entity.Owner, entity.Comp.Material, -1000, materialStorageComponent);

        // Reset visuals to default
        _appearanceSystem.RemoveData(grenadeUid, ReleaseGasOnTriggerVisuals.Key);
        _appearanceSystem.SetData(grenadeUid, TriggerVisuals.VisualState, TriggerVisualState.Unprimed);

        // Re-arm grenade using prototype specs to ensure correctness
        if (_prototypeManager.TryIndex<EntityPrototype>(AirGrenadeId, out var proto))
        {
            if (proto.TryGetComponent<ReleaseGasOnTriggerComponent>(out var protoRelease, _componentFactory))
            {
                var release = _serializationManager.CreateCopy(protoRelease, notNullableOverride: true);
                release.Active = false;
                release.StartingTotalMoles = 0;
                if (release.Air != null)
                {
                    release.Air.Clear();
                    release.Air.Volume = 1000f;
                }
                if (TryComp<ReleaseGasOnTriggerComponent>(grenadeUid, out var existingRelease))
                {
                    _serializationManager.CopyTo(release, ref existingRelease, notNullableOverride: true);
                    Dirty(grenadeUid, existingRelease);
                }
                else
                {
                    AddComp(grenadeUid, release);
                    Dirty(grenadeUid, release);
                }
            }

            if (proto.TryGetComponent<TriggerOnUseComponent>(out var protoOnUse, _componentFactory))
            {
                var onUse = _serializationManager.CreateCopy(protoOnUse, notNullableOverride: true);
                if (TryComp<TriggerOnUseComponent>(grenadeUid, out var existingOnUse))
                {
                    _serializationManager.CopyTo(onUse, ref existingOnUse, notNullableOverride: true);
                    Dirty(grenadeUid, existingOnUse);
                }
                else
                {
                    AddComp(grenadeUid, onUse);
                    Dirty(grenadeUid, onUse);
                }
            }

            if (proto.TryGetComponent<TimerTriggerComponent>(out var protoTimer, _componentFactory))
            {
                var timer = _serializationManager.CreateCopy(protoTimer, notNullableOverride: true);
                if (TryComp<TimerTriggerComponent>(grenadeUid, out var existingTimer))
                {
                    _serializationManager.CopyTo(timer, ref existingTimer, notNullableOverride: true);
                    Dirty(grenadeUid, existingTimer);
                }
                else
                {
                    AddComp(grenadeUid, timer);
                    Dirty(grenadeUid, timer);
                }
            }

            if (proto.TryGetComponent<RemoveComponentsOnTriggerComponent>(out var protoRemove, _componentFactory))
            {
                var remove = _serializationManager.CreateCopy(protoRemove, notNullableOverride: true);
                remove.Triggered = false;
                if (TryComp<RemoveComponentsOnTriggerComponent>(grenadeUid, out var existingRemove))
                {
                    _serializationManager.CopyTo(remove, ref existingRemove, notNullableOverride: true);
                    Dirty(grenadeUid, existingRemove);
                }
                else
                {
                    AddComp(grenadeUid, remove);
                    Dirty(grenadeUid, remove);
                }
            }
        }
        else
        {
            // Fallback for non-standard grenades
            var release = EnsureComp<ReleaseGasOnTriggerComponent>(grenadeUid);
            release.Active = false;
            release.StartingTotalMoles = 0;
            release.KeysIn = new() { "timer" };
            release.Air ??= new GasMixture(1000f);
            Dirty(grenadeUid, release);

            EnsureComp<TriggerOnUseComponent>(grenadeUid);
            var timer = EnsureComp<TimerTriggerComponent>(grenadeUid);
            timer.Delay = TimeSpan.FromSeconds(3);
            Dirty(grenadeUid, timer);
        }

        UpdateUserInterface(entity);
    }

    private void OnEmagged(Entity<GasGrenadeCompressorComponent> entity, ref GotEmaggedEvent args)
    {
        args.Handled = true;
    }

    private void OnMaterialAmountChanged(Entity<GasGrenadeCompressorComponent> entity, ref MaterialAmountChangedEvent args)
    {
        UpdateUserInterface(entity);
    }
}
