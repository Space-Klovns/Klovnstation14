using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Emag.Systems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Localizations;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Lawnmower;

public sealed partial class SharedKsLawnmowerSystem : EntitySystem
{
    [Dependency] private ContentLocalizationManager _contentLocalization = default!;
    [Dependency] private EmagSystem _emagSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private VehicleSystem _vehicleSystem = default!;

    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    /// <summary>
    ///     Driving itself is the vehicle system's (<see cref="StrapVehicleComponent"/>); a change of rider only changes
    ///         whose shorts count.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnOperatorSet(Entity<KsLawnmowerComponent> entity, ref VehicleOperatorSetEvent args)
    {
        if (!TerminatingOrDeleted(entity.Owner))
            _movementSpeedSystem.RefreshMovementSpeedModifiers(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<KsLawnmowerComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!_vehicleQuery.TryComp(entity.Owner, out var vehicleComponent) ||
            vehicleComponent.Operator is not { } riderUid ||
            !_inventorySystem.TryGetContainerSlotEnumerator(riderUid, out var slots))
        {
            return;
        }

        while (slots.NextItem(out var itemUid, out var slot))
        {
            if (!TryComp<KsGrillingShortsComponent>(itemUid, out var shortsComponent) ||
                (slot.SlotFlags & shortsComponent.Slots) == 0)
            {
                continue;
            }

            args.ModifySpeed(shortsComponent.MowerSpeedMultiplier);
            return;
        }
    }

    [SubscribeLocalEvent]
    private void OnShortsEquipped(Entity<KsGrillingShortsComponent> entity, ref GotEquippedEvent args)
    {
        RefreshRiddenMower(entity, args.EquipTarget, args.SlotFlags);
    }

    [SubscribeLocalEvent]
    private void OnShortsUnequipped(Entity<KsGrillingShortsComponent> entity, ref GotUnequippedEvent args)
    {
        RefreshRiddenMower(entity, args.EquipTarget, args.SlotFlags);
    }

    private void RefreshRiddenMower(Entity<KsGrillingShortsComponent> shorts, EntityUid riderUid, SlotFlags slot)
    {
        if ((slot & shorts.Comp.Slots) != 0 &&
            _vehicleSystem.GetOperatedVehicleOrNull(riderUid) is { } mowerUid &&
            HasComp<KsLawnmowerComponent>(mowerUid))
        {
            _movementSpeedSystem.RefreshMovementSpeedModifiers(mowerUid);
        }
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsLawnmowerComponent> entity, ref MapInitEvent args)
    {
        if (_emagSystem.CheckFlag(entity.Owner, EmagType.Interaction))
            _appearanceSystem.SetData(entity.Owner, KsLawnmowerVisuals.SafetyDisabled, true);
    }

    [SubscribeLocalEvent]
    private void OnEmagged(Entity<KsLawnmowerComponent> entity, ref GotEmaggedEvent args)
    {
        if (args.Handled || !_emagSystem.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (_emagSystem.CheckFlag(entity.Owner, EmagType.Interaction))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-lawn-mower-already-emagged"), args.UserUid, args.UserUid);
            return;
        }

        args.Handled = true;
        _appearanceSystem.SetData(entity.Owner, KsLawnmowerVisuals.SafetyDisabled, true);
        ApplyEmaggedName(entity);
    }

    public void ApplyEmaggedName(Entity<KsLawnmowerComponent> entity)
    {
        var prototype = ProtoMan.Index(entity.Comp.EmaggedPrototype);
        _metaDataSystem.SetEntityName(entity.Owner, _contentLocalization.GetLocalizedPrototypeName(prototype));
        _metaDataSystem.SetEntityDescription(entity.Owner, _contentLocalization.GetLocalizedPrototypeDescription(prototype));
    }
}
