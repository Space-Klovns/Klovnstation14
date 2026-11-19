// BF14-BLU: ported from Monolith via Battlefield14 and reworked:
//  - all of it is shared, so a hit on a plated wearer predicts the same damage as the server deals (Monolith absorbed
//      damage on the server only);
//  - a negative absorption ratio wears the plate down, as documented (Monolith only did for a positive one);
//  - the break popup and examine text are localised for the reader;
//  - the holder's speed comes from its active plate, rather than a copy kept on the holder.
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.ArmorPlate;

/// <summary>
///     Armor plates in clothing (<see cref="ArmorPlateHolderComponent"/>): picks the active plate, lets it absorb the
///         wearer's damage and set their speed, and describes it on examine.
/// </summary>
public sealed partial class ArmorPlateSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private EntityQuery<ArmorPlateItemComponent> _plateQuery = default!;
    [Dependency] private EntityQuery<ArmorPlateHolderComponent> _holderQuery = default!;

    /// <summary>
    ///     The damage type a plate takes its durability damage as.
    /// </summary>
    private static readonly ProtoId<DamageTypePrototype> PlateDamageType = "Blunt";

    [SubscribeLocalEvent]
    private void OnPlateInserted(Entity<ArmorPlateHolderComponent> entity, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != StorageComponent.ContainerId || entity.Comp.ActivePlate != null)
            return;

        if (_plateQuery.HasComp(args.Entity))
            SetActivePlate(entity, args.Entity);
    }

    [SubscribeLocalEvent]
    private void OnPlateRemoved(Entity<ArmorPlateHolderComponent> entity, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != StorageComponent.ContainerId || entity.Comp.ActivePlate != args.Entity)
            return;

        // Taken out of storage by being destroyed: the plate broke.
        if (entity.Comp.ShowBreakPopup &&
            TerminatingOrDeleted(args.Entity) &&
            TryGetWearer(entity.Owner, out var wearerUid) &&
            MetaData(args.Entity).EntityPrototype is { } plateProto)
        {
            _popupSystem.PopupEntity(
                KsPopupMessage.Create("armor-plate-break", ("plateName", new KsPopupPrototypeName(plateProto.ID))),
                wearerUid,
                wearerUid,
                PopupType.MediumCaution);
        }

        EntityUid? nextPlateUid = null;
        foreach (var containedUid in args.Container.ContainedEntities)
        {
            if (containedUid == args.Entity || !_plateQuery.HasComp(containedUid))
                continue;

            nextPlateUid = containedUid;
            break;
        }

        SetActivePlate(entity, nextPlateUid);
    }

    // By-value class event: the engine has Entity<T> handlers only for ref events.
    [SubscribeLocalEvent]
    private void OnRefreshMoveSpeed(EntityUid uid, ArmorPlateHolderComponent component, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (TryGetActivePlate((uid, component), out var plate))
            args.Args.ModifySpeed(plate.Comp.WalkSpeedModifier, plate.Comp.SprintSpeedModifier);
    }

    /// <summary>
    ///     Each worn holder's active plate takes its share of the damage and passes the rest on. Damage with no origin
    ///         (bleeding, suffocation) goes past the plates.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBeforeDamageChanged(Entity<InventoryComponent> entity, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.Origin == null || !args.Damage.AnyPositive())
            return;

        var slotEnumerator = _inventorySystem.GetSlotEnumerator((entity.Owner, entity.Comp));
        while (slotEnumerator.NextItem(out var equippedUid))
        {
            if (!_holderQuery.TryComp(equippedUid, out var holderComponent) ||
                !TryGetActivePlate((equippedUid, holderComponent), out var plate))
            {
                continue;
            }

            args.Damage = AbsorbDamage(entity.Owner, plate, args.Damage);
            if (args.Damage.Empty)
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    /// <summary>
    ///     Wears the plate down for its share of <paramref name="damage"/>, and returns what is left for the wearer.
    ///         <paramref name="damage"/> itself is left alone: it can be the attacker's own, such as a weapon's.
    /// </summary>
    private DamageSpecifier AbsorbDamage(EntityUid wearerUid, Entity<ArmorPlateItemComponent> plate, DamageSpecifier damage)
    {
        var absorbedTotal = FixedPoint2.Zero;
        var plateDamageTotal = FixedPoint2.Zero;
        var remaining = new Dictionary<string, FixedPoint2>();

        foreach (var (damageType, amount) in damage.DamageDict)
        {
            if (amount <= FixedPoint2.Zero)
            {
                remaining[damageType] = amount;
                continue;
            }

            var ratio = plate.Comp.AbsorptionRatios.GetValueOrDefault(damageType, 0f);
            var multiplier = plate.Comp.DamageMultipliers.GetValueOrDefault(damageType, 1.0f);

            var absorbed = ratio > 0f ? amount * ratio : FixedPoint2.Zero;
            var passedOn = ratio >= 0f ? amount - absorbed : amount * (1f - ratio);

            absorbedTotal += absorbed;
            plateDamageTotal += amount * MathF.Abs(ratio) * multiplier;

            if (passedOn > FixedPoint2.Zero)
                remaining[damageType] = passedOn;
        }

        // A copy keeps the attack's armour penetration for the armour after the plate.
        var passedOnDamage = new DamageSpecifier(damage);
        passedOnDamage.DamageDict.Clear();
        foreach (var (damageType, amount) in remaining)
        {
            passedOnDamage.DamageDict.Add(damageType, amount);
        }

        if (plateDamageTotal > FixedPoint2.Zero)
        {
            var plateDamage = new DamageSpecifier();
            plateDamage.DamageDict.Add(PlateDamageType, plateDamageTotal);
            _damageableSystem.TryChangeDamage(plate.Owner, plateDamage, ignoreResistances: true);
        }

        if (absorbedTotal > FixedPoint2.Zero)
            _staminaSystem.TakeStaminaDamage(wearerUid, (float)absorbedTotal * plate.Comp.StaminaDamageMultiplier);

        return passedOnDamage;
    }

    [SubscribeLocalEvent]
    private void OnHolderExamined(Entity<ArmorPlateHolderComponent> entity, ref ExaminedEvent args)
    {
        if (!TryGetActivePlate(entity.AsNullable(), out var plate))
        {
            args.PushMarkup(Loc.GetString("armor-plate-examine-no-plate"));
            return;
        }

        var plateName = Name(plate.Owner);
        var plateDamage = TryComp<DamageableComponent>(plate.Owner, out var damageableComponent)
            ? (float)_damageableSystem.GetPositiveDamage((plate.Owner, damageableComponent)).GetTotal()
            : 0f;
        var durabilityPercent = Math.Clamp(
            (plate.Comp.MaxDurability - plateDamage) / plate.Comp.MaxDurability * 100f,
            0f,
            100f);

        var durabilityColor = durabilityPercent switch
        {
            > 66f => "green",
            >= 33f => "yellow",
            _ => "red",
        };

        args.PushMarkup(Loc.GetString("armor-plate-examine-with-plate",
            ("plateName", plateName),
            ("percent", (int)durabilityPercent),
            ("durabilityColor", durabilityColor)));
    }

    [SubscribeLocalEvent]
    private void OnPlateVerbExamine(Entity<ArmorPlateItemComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var examineMarkup = GetPlateExamine(entity.Comp);
        var ev = new ArmorExamineEvent(examineMarkup);
        RaiseLocalEvent(entity, ref ev);

        _examineSystem.AddDetailedExamineVerb(args,
            entity.Comp,
            examineMarkup,
            Loc.GetString("armor-plate-examinable-verb-text"),
            "/Textures/Interface/VerbIcons/dot.svg.192dpi.png",
            Loc.GetString("armor-plate-examinable-verb-message"));
    }

    /// <summary>
    ///     The active plate of a holder, if it has one.
    /// </summary>
    public bool TryGetActivePlate(Entity<ArmorPlateHolderComponent?> holder, out Entity<ArmorPlateItemComponent> plate)
    {
        plate = default;
        if (!_holderQuery.Resolve(holder.Owner, ref holder.Comp, false) ||
            holder.Comp.ActivePlate is not { } plateUid ||
            !_plateQuery.TryComp(plateUid, out var plateComponent))
        {
            return false;
        }

        plate = (plateUid, plateComponent);
        return true;
    }

    private void SetActivePlate(Entity<ArmorPlateHolderComponent> holder, EntityUid? plateUid)
    {
        holder.Comp.ActivePlate = plateUid;
        Dirty(holder);

        if (TryGetWearer(holder.Owner, out var wearerUid))
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(wearerUid);
    }

    /// <summary>
    ///     The entity wearing <paramref name="clothingUid"/>, if it is worn.
    /// </summary>
    private bool TryGetWearer(EntityUid clothingUid, out EntityUid wearerUid)
    {
        wearerUid = default;
        if (!_containerSystem.TryGetContainingContainer(clothingUid, out var container) ||
            !HasComp<InventoryComponent>(container.Owner))
        {
            return false;
        }

        wearerUid = container.Owner;
        return true;
    }

    private FormattedMessage GetPlateExamine(ArmorPlateItemComponent plate)
    {
        var message = new FormattedMessage();
        message.AddMarkupOrThrow(Loc.GetString("armor-plate-attributes-examine"));
        message.PushNewline();
        message.AddMarkupOrThrow(Loc.GetString("armor-plate-initial-durability", ("durability", plate.MaxDurability)));

        var walkPercent = MathF.Round((plate.WalkSpeedModifier - 1.0f) * 100f, 1);
        var sprintPercent = MathF.Round((plate.SprintSpeedModifier - 1.0f) * 100f, 1);
        if (walkPercent != 0f || sprintPercent != 0f)
        {
            if (MathHelper.CloseTo(walkPercent, sprintPercent, 0.5f))
            {
                AddSpeedLine(message, "both", walkPercent);
            }
            else
            {
                AddSpeedLine(message, "sprint", sprintPercent);
                AddSpeedLine(message, "walk", walkPercent);
            }
        }

        foreach (var (damageType, ratio) in plate.AbsorptionRatios)
        {
            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString("armor-plate-ratios-display",
                ("deltasign", GetDirection(ratio)),
                ("dmgType", Loc.GetString("armor-damage-type-" + damageType.Id.ToLowerInvariant())),
                ("ratioPercent", MathF.Abs(MathF.Round(ratio * 100, 1))),
                ("multiplier", plate.DamageMultipliers.GetValueOrDefault(damageType, 1.0f).ToString("0.##"))));
        }

        message.PushNewline();
        message.AddMarkupOrThrow(Loc.GetString("armor-plate-stamina-value",
            ("multiplier", MathF.Round(plate.StaminaDamageMultiplier * 100f, 1))));

        return message;
    }

    private void AddSpeedLine(FormattedMessage message, string gait, float percent)
    {
        message.PushNewline();
        message.AddMarkupOrThrow(Loc.GetString("armor-plate-speed-display",
            ("gait", gait),
            ("deltasign", GetDirection(percent)),
            ("speedPercent", MathF.Abs(percent))));
    }

    /// <summary>
    ///     -1 for a value that helps the wearer, 1 for one that hinders them, as the locale strings select on.
    /// </summary>
    private static int GetDirection(float value) => value < 0 ? 1 : value > 0 ? -1 : 0;
}
