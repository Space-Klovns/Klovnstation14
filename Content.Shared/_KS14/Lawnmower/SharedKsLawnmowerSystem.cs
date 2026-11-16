using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Buckle.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Localizations;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
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
    [Dependency] private SharedMoverController _moverController = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    [SubscribeLocalEvent]
    private void OnStrapped(Entity<KsLawnmowerComponent> entity, ref StrappedEvent args)
    {
        _moverController.SetRelay(args.Buckle.Owner, entity.Owner);
        _movementSpeedSystem.RefreshMovementSpeedModifiers(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnUnstrapped(Entity<KsLawnmowerComponent> entity, ref UnstrappedEvent args)
    {
        if (TryComp<RelayInputMoverComponent>(args.Buckle.Owner, out var relay) && relay.RelayEntity == entity.Owner)
            RemComp(args.Buckle.Owner, relay);

        if (!TerminatingOrDeleted(entity.Owner))
            _movementSpeedSystem.RefreshMovementSpeedModifiers(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<KsLawnmowerComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<MovementRelayTargetComponent>(entity.Owner, out var relayTargetComponent) ||
            !_inventorySystem.TryGetContainerSlotEnumerator(relayTargetComponent.Source, out var slots))
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
            TryComp<RelayInputMoverComponent>(riderUid, out var relay) &&
            HasComp<KsLawnmowerComponent>(relay.RelayEntity))
        {
            _movementSpeedSystem.RefreshMovementSpeedModifiers(relay.RelayEntity);
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
