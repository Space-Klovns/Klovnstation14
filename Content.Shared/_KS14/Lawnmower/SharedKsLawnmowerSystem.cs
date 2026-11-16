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

    public static readonly EntProtoId EmaggedPrototype = "KsLawnmowerEmagged";
    private const string JumpsuitSlot = "jumpsuit";
    private const float GrillingShortsSpeedMultiplier = 1.1f;

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
        if (TryComp<MovementRelayTargetComponent>(entity.Owner, out var relay) &&
            _inventorySystem.TryGetSlotEntity(relay.Source, JumpsuitSlot, out var jumpsuitUid) &&
            HasComp<KsGrillingShortsComponent>(jumpsuitUid))
        {
            args.ModifySpeed(GrillingShortsSpeedMultiplier);
        }
    }

    [SubscribeLocalEvent]
    private void OnShortsEquipped(Entity<KsGrillingShortsComponent> entity, ref GotEquippedEvent args)
    {
        RefreshRiddenMower(args.EquipTarget, args.Slot);
    }

    [SubscribeLocalEvent]
    private void OnShortsUnequipped(Entity<KsGrillingShortsComponent> entity, ref GotUnequippedEvent args)
    {
        RefreshRiddenMower(args.EquipTarget, args.Slot);
    }

    private void RefreshRiddenMower(EntityUid riderUid, string slot)
    {
        if (slot == JumpsuitSlot &&
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
        ApplyEmaggedName(entity.Owner);
    }

    public void ApplyEmaggedName(EntityUid uid)
    {
        var prototype = ProtoMan.Index(EmaggedPrototype);
        _metaDataSystem.SetEntityName(uid, _contentLocalization.GetLocalizedPrototypeName(prototype));
        _metaDataSystem.SetEntityDescription(uid, _contentLocalization.GetLocalizedPrototypeDescription(prototype));
    }
}
