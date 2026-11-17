using System.Numerics;
using Content.Server.Weapons.Ranged.Systems;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Martial arts from the great novels of the jianghu: learning them from manuals, and what each one does.
///         Qi arts are paid for with qi through <see cref="CultivationSystem.TryUseTechnique"/>; body arts with stamina.
/// </summary>
public sealed partial class MartialArtsSystem : EntitySystem
{
    private static readonly SoundSpecifier PalmSound = new SoundPathSpecifier("/Audio/Effects/hit_kick.ogg");
    private static readonly SoundSpecifier DragonRoarSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly SoundSpecifier FingerSwordSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/Magic/staff_animation.ogg");
    private static readonly SoundSpecifier DrainSound = new SoundPathSpecifier("/Audio/Magic/ethereal_enter.ogg");
    private static readonly SoundSpecifier BellSound = new SoundPathSpecifier("/Audio/Effects/church_bell3.ogg");
    private static readonly SoundSpecifier LeapSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");
    private static readonly SoundSpecifier CounterSound = new SoundPathSpecifier("/Audio/Effects/metal_thud1.ogg");
    private static readonly SoundSpecifier ShiftSound = new SoundPathSpecifier("/Audio/Magic/forcewall.ogg");

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private CultivationStanceSystem _cultivationStanceSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedMartialArtsSystem _martialArtsSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedProjectileSystem _projectileSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    /// <summary>
    ///     Set while thorns or a counter are dealing damage back, so two people wearing hedgehog armor, or both
    ///         countering, can't bounce damage between them forever.
    /// </summary>
    private bool _retaliating;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;

        var shiftEnumerator = EntityQueryEnumerator<GreatShiftComponent>();
        while (shiftEnumerator.MoveNext(out var uid, out var shiftComponent))
        {
            if (currentTime >= shiftComponent.EndTime)
                RemCompDeferred(uid, shiftComponent);
        }

        var counterEnumerator = EntityQueryEnumerator<DuguCounterComponent>();
        while (counterEnumerator.MoveNext(out var uid, out var counterComponent))
        {
            if (currentTime >= counterComponent.EndTime)
                RemCompDeferred(uid, counterComponent);
        }

        var comboEnumerator = EntityQueryEnumerator<DragonPalmComboComponent>();
        while (comboEnumerator.MoveNext(out var uid, out var comboComponent))
        {
            if (currentTime >= comboComponent.Expires)
                RemCompDeferred(uid, comboComponent);
        }

        var nineYangEnumerator = EntityQueryEnumerator<NineYangComponent, CultivatorComponent>();
        while (nineYangEnumerator.MoveNext(out var uid, out var nineYangComponent, out var cultivatorComponent))
        {
            if (currentTime < nineYangComponent.NextHeal)
                continue;

            nineYangComponent.NextHeal = currentTime + nineYangComponent.HealInterval;
            if (_sharedCultivationSystem.HealInternalInjury((uid, cultivatorComponent)))
                _noticeSystem.Notify(uid, Loc.GetString("martial-art-nine-yang-heal"));
        }
    }

    #region Learning

    [SubscribeLocalEvent]
    private void OnNineYangStartup(Entity<NineYangComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.NextHeal = _gameTiming.CurTime + entity.Comp.HealInterval;
    }

    [SubscribeLocalEvent]
    private void OnManualUsed(Entity<MartialArtManualComponent> manualEntity, ref UseInHandEvent args)
    {
        if (args.Handled || !ProtoMan.TryIndex(manualEntity.Comp.Art, out var art))
            return;

        args.Handled = true;
        var userUid = args.User;

        var result = _martialArtsSystem.CanLearn(userUid, art);
        if (result != MartialArtLearnResult.Learned)
        {
            _noticeSystem.Notify(userUid, Loc.GetString($"martial-art-learn-{result}"));
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, userUid, TimeSpan.FromSeconds(5), new MartialArtReadManualDoAfterEvent(), manualEntity, used: manualEntity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _noticeSystem.Announce(userUid, Loc.GetString("cultivation-manual-start", ("user", userUid), ("manual", manualEntity.Owner)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnManualRead(Entity<MartialArtManualComponent> manualEntity, ref MartialArtReadManualDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ProtoMan.TryIndex(manualEntity.Comp.Art, out var art))
            return;

        args.Handled = true;
        var result = _martialArtsSystem.TryLearn(args.User, manualEntity.Comp.Art);
        if (result != MartialArtLearnResult.Learned)
        {
            _noticeSystem.Notify(args.User, Loc.GetString($"martial-art-learn-{result}"));
            return;
        }

        _noticeSystem.Notify(args.User, Loc.GetString(art.Forbidden ? "martial-art-learned-forbidden" : "martial-art-learned",
            ("art", Loc.GetString(art.Name))), type: art.Forbidden ? PopupType.MediumCaution : PopupType.Medium);
    }

    #endregion

    #region Helpers

    /// <summary>
    ///     Turn the next few projectiles back on whoever loosed them, for a while.
    /// </summary>
    public void StartGreatShift(EntityUid uid, TimeSpan duration, int charges)
    {
        var shiftComponent = EnsureComp<GreatShiftComponent>(uid);
        shiftComponent.EndTime = _gameTiming.CurTime + duration;
        shiftComponent.Charges = charges;
    }

    /// <summary>
    ///     Wait for the next close blow and break it.
    /// </summary>
    public void StartDuguCounter(EntityUid uid, TimeSpan duration)
    {
        var counterComponent = EnsureComp<DuguCounterComponent>(uid);
        counterComponent.EndTime = _gameTiming.CurTime + duration;
    }

    /// <summary>
    ///     Pay for a body art with stamina. Fails if it would leave you collapsed.
    /// </summary>
    public bool TryPayStamina(EntityUid userUid, float cost)
    {
        if (_cultivationSystem.IsSealed(userUid))
            return false;

        if (_staminaSystem.TryTakeStamina(userUid, cost))
        {
            _visualsSystem.Cast(userUid, FxColour.Qi);
            return true;
        }

        _noticeSystem.Notify(userUid, Loc.GetString("martial-art-too-tired"), type: PopupType.SmallCaution);
        return false;
    }

    private void Retaliate(EntityUid targetUid, DamageSpecifier damage, EntityUid sourceUid)
    {
        _retaliating = true;
        try
        {
            _damageableSystem.TryChangeDamage(targetUid, damage, origin: sourceUid);
        }
        finally
        {
            _retaliating = false;
        }
    }

    private bool InRange(EntityUid firstUid, EntityUid secondUid, float range)
    {
        return _transformSystem.InRange((firstUid, null), (secondUid, null), range);
    }

    /// <summary>
    ///     Pull qi out of a target into the user. A cultivator loses the qi itself; a mortal, who has none to give,
    ///         is drained of stamina instead and the user gains a little.
    /// </summary>
    /// <returns>Qi gained.</returns>
    private float Drain(EntityUid userUid, EntityUid targetUid, float qiDrain, float mortalStaminaDamage, float mortalQiGain)
    {
        float gained;
        if (_cultivatorQuery.TryComp(targetUid, out var targetCultivator))
        {
            gained = MathF.Min(qiDrain, targetCultivator.Qi);
            _sharedCultivationSystem.TrySpendQi((targetUid, targetCultivator), gained);
        }
        else
        {
            _staminaSystem.TakeStaminaDamage(targetUid, mortalStaminaDamage, source: userUid);
            gained = mortalQiGain;
        }

        _sharedCultivationSystem.AddQi(userUid, gained);
        return gained;
    }

    #endregion

    #region Qi arts

    [SubscribeLocalEvent]
    private void OnDragonPalm(Entity<MartialArtistComponent> entity, ref DragonPalmActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        var targetUid = args.Target;
        var currentTime = _gameTiming.CurTime;

        var comboComponent = EnsureComp<DragonPalmComboComponent>(targetUid);
        comboComponent.Stacks = comboComponent.Striker == entity.Owner && currentTime < comboComponent.Expires
            ? Math.Min(comboComponent.Stacks + 1, args.MaxStacks)
            : 1;
        comboComponent.Striker = entity.Owner;
        comboComponent.Expires = currentTime + args.ComboWindow;

        var stacks = comboComponent.Stacks;
        var damage = CultivationSystem.Damage("Blunt", args.BluntDamage + args.DamagePerStack * (float)(stacks - 1));
        _damageableSystem.TryChangeDamage(targetUid, damage, origin: entity);

        var direction = _transformSystem.GetMapCoordinates(targetUid).Position - _transformSystem.GetMapCoordinates(entity).Position;
        if (direction.LengthSquared() > 0.0001f)
        {
            var distance = args.ThrowDistance + 0.5f * (float)(stacks - 1);
            _throwingSystem.TryThrow(targetUid, Vector2.Normalize(direction) * distance, baseThrowSpeed: 8f, user: entity, recoil: false);
        }

        if (stacks >= args.MaxStacks)
        {
            // The last palm, "the haughty dragon repents": it floors them and ends the chain.
            _stunSystem.TryKnockdown(targetUid, TimeSpan.FromSeconds(2));
            RemCompDeferred(targetUid, comboComponent);
            _audioSystem.PlayPvs(DragonRoarSound, entity);
            _noticeSystem.Announce(targetUid, Loc.GetString("martial-art-dragon-palm-final", ("user", entity.Owner), ("target", targetUid)), type: PopupType.LargeCaution);
            return;
        }

        _audioSystem.PlayPvs(PalmSound, targetUid);
        _noticeSystem.Announce(targetUid, Loc.GetString("martial-art-dragon-palm", ("user", entity.Owner), ("target", targetUid), ("stacks", stacks)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnSixMeridians(Entity<MartialArtistComponent> entity, ref SixMeridiansActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var fromMap = _transformSystem.GetMapCoordinates(entity);
        var direction = _transformSystem.ToMapCoordinates(args.Target).Position - fromMap.Position;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        var beamUid = Spawn(args.Projectile, fromMap);
        _gunSystem.ShootProjectile(beamUid, direction, _physicsSystem.GetMapLinearVelocity(entity), entity, user: entity, speed: args.Speed);
        _audioSystem.PlayPvs(FingerSwordSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnNorthernDarkness(Entity<MartialArtistComponent> entity, ref NorthernDarknessActionEvent args)
    {
        if (args.Handled || !_cultivatorQuery.HasComp(entity))
            return;

        args.Handled = true;
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        var gained = Drain(entity, args.Target, args.QiDrain, args.MortalStaminaDamage, args.MortalQiGain);

        _audioSystem.PlayPvs(DrainSound, entity);
        _noticeSystem.Announce(args.Target, Loc.GetString("martial-art-northern-darkness", ("user", entity.Owner), ("target", args.Target), ("qi", (int)gained)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnStarAbsorbing(Entity<MartialArtistComponent> entity, ref StarAbsorbingActionEvent args)
    {
        if (args.Handled || !_cultivatorQuery.HasComp(entity))
            return;

        args.Handled = true;
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        var gained = Drain(entity, args.Target, args.QiDrain, args.MortalStaminaDamage, args.MortalQiGain);
        _sharedCultivationSystem.AddInsight(args.Target, -args.InsightDrain);
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-args.Heal), origin: entity);

        _audioSystem.PlayPvs(DrainSound, entity);
        _noticeSystem.Announce(args.Target, Loc.GetString("martial-art-star-absorbing", ("user", entity.Owner), ("target", args.Target), ("qi", (int)gained)), type: PopupType.LargeCaution);

        // Stolen qi is never truly yours. Sometimes it fights back.
        if (_robustRandom.Prob(args.ClashChance) && _sharedCultivationSystem.AddInternalInjury(entity.Owner))
            _noticeSystem.Notify(entity, Loc.GetString("martial-art-star-absorbing-clash"), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnGreatShift(Entity<MartialArtistComponent> entity, ref GreatShiftActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        StartGreatShift(entity, args.Duration, args.Charges);

        _audioSystem.PlayPvs(ShiftSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("martial-art-great-shift", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnGreatShiftProjectile(Entity<GreatShiftComponent> entity, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || entity.Comp.LifeStage > ComponentLifeStage.Running || entity.Comp.Charges <= 0
            || !TryComp<PhysicsComponent>(args.ProjUid, out var physicsComponent))
            return;

        // Turn it right round and make it ours, so it flies back at whoever loosed it.
        _physicsSystem.SetLinearVelocity(args.ProjUid, -physicsComponent.LinearVelocity, body: physicsComponent);
        var projectileTransform = Transform(args.ProjUid);
        _transformSystem.SetLocalRotation(args.ProjUid, projectileTransform.LocalRotation + Angle.FromDegrees(180));
        _projectileSystem.SetShooter(args.ProjUid, args.Component, entity);
        args.Cancelled = true;

        entity.Comp.Charges--;
        if (entity.Comp.Charges <= 0)
            RemCompDeferred(entity, entity.Comp);

        _audioSystem.PlayPvs(ShiftSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("martial-art-great-shift-turn", ("user", entity.Owner)), type: PopupType.Small);
    }

    #endregion

    #region Body arts

    [SubscribeLocalEvent]
    private void OnGoldenBell(Entity<MartialArtistComponent> entity, ref GoldenBellActionEvent args)
    {
        if (args.Handled || !TryPayStamina(entity, args.StaminaCost))
            return;

        args.Handled = true;
        _cultivationStanceSystem.ApplyGoldenBell(entity, args.Duration, args.DamageMultiplier);
        _audioSystem.PlayPvs(BellSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("martial-art-golden-bell", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnGracefulSteps(Entity<MartialArtistComponent> entity, ref GracefulStepsActionEvent args)
    {
        if (args.Handled || !TryPayStamina(entity, args.StaminaCost))
            return;

        args.Handled = true;
        _cultivationStanceSystem.ApplyGracefulSteps(entity, args.Duration, args.SpeedMultiplier);
        _noticeSystem.Announce(entity, Loc.GetString("martial-art-graceful-steps", ("user", entity.Owner)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnLadderCloudLeap(Entity<MartialArtistComponent> entity, ref LadderCloudLeapActionEvent args)
    {
        if (args.Handled)
            return;

        var offset = _transformSystem.ToMapCoordinates(args.Target).Position - _transformSystem.GetMapCoordinates(entity).Position;
        if (offset.LengthSquared() < 0.01f || !TryPayStamina(entity, args.StaminaCost))
            return;

        args.Handled = true;
        if (offset.Length() > args.MaxDistance)
            offset = Vector2.Normalize(offset) * args.MaxDistance;

        _throwingSystem.TryThrow(entity, offset, baseThrowSpeed: 12f, recoil: false, doSpin: false);
        _audioSystem.PlayPvs(LeapSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("martial-art-ladder-cloud", ("user", entity.Owner)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnDuguCounter(Entity<MartialArtistComponent> entity, ref DuguCounterActionEvent args)
    {
        if (args.Handled || !TryPayStamina(entity, args.StaminaCost))
            return;

        args.Handled = true;
        StartDuguCounter(entity, args.Duration);
        _noticeSystem.Announce(entity, Loc.GetString("martial-art-dugu-ready", ("user", entity.Owner)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnDuguDamageModify(Entity<DuguCounterComponent> entity, ref DamageModifyEvent args)
    {
        if (_retaliating || entity.Comp.LifeStage > ComponentLifeStage.Running
            || args.Origin is not { } attackerUid || attackerUid == entity.Owner
            || !_mobStateQuery.HasComp(attackerUid) || !InRange(entity, attackerUid, entity.Comp.Range)
            || args.Damage.GetTotal() <= 0)
            return;

        // Every technique has a flaw. The blow is broken before it lands, and the one who threw it pays.
        args.Damage *= 0f;
        RemCompDeferred(entity, entity.Comp);

        Retaliate(attackerUid, CultivationSystem.Damage("Slash", entity.Comp.CounterDamage), entity);
        _stunSystem.TryKnockdown(attackerUid, entity.Comp.Knockdown);

        _audioSystem.PlayPvs(CounterSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("martial-art-dugu-counter", ("user", entity.Owner), ("target", attackerUid)), type: PopupType.LargeCaution);
    }

    #endregion

    #region Legendary armor

    [SubscribeLocalEvent]
    private void OnHedgehogDamageModify(Entity<SoftHedgehogArmorComponent> entity, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        var wearerUid = args.Owner;
        if (_retaliating || args.Args.Origin is not { } attackerUid || attackerUid == wearerUid
            || !_mobStateQuery.HasComp(attackerUid) || !InRange(wearerUid, attackerUid, entity.Comp.Range)
            || args.Args.Damage.GetTotal() <= 0)
            return;

        Retaliate(attackerUid, CultivationSystem.Damage("Piercing", entity.Comp.ThornDamage), wearerUid);
        _noticeSystem.Notify(attackerUid, Loc.GetString("martial-art-hedgehog-thorns", ("target", attackerUid)), type: PopupType.SmallCaution);
    }

    /// <summary>
    ///     The slots the Great Sage's regalia is worn in: chain-mail, crown and boots.
    /// </summary>
    private static readonly string[] RegaliaSlots = { "outerClothing", "head", "shoes" };

    [SubscribeLocalEvent]
    private void OnRegaliaEquipped(Entity<GreatSageRegaliaComponent> entity, ref GotEquippedEvent args)
    {
        UpdateGreatSage(args.EquipTarget);
    }

    [SubscribeLocalEvent]
    private void OnRegaliaUnequipped(Entity<GreatSageRegaliaComponent> entity, ref GotUnequippedEvent args)
    {
        UpdateGreatSage(args.EquipTarget);
    }

    /// <summary>
    ///     The set wakes only when every piece is worn in its own slot.
    /// </summary>
    public void UpdateGreatSage(EntityUid wearerUid)
    {
        var complete = true;
        foreach (var slot in RegaliaSlots)
        {
            if (!_inventorySystem.TryGetSlotEntity(wearerUid, slot, out var pieceUid) || !HasComp<GreatSageRegaliaComponent>(pieceUid))
            {
                complete = false;
                break;
            }
        }

        if (complete == HasComp<GreatSageComponent>(wearerUid))
            return;

        if (complete)
        {
            AddComp<GreatSageComponent>(wearerUid);
            _audioSystem.PlayPvs(BellSound, wearerUid);
            _noticeSystem.Announce(wearerUid, Loc.GetString("martial-art-great-sage-awakens", ("user", wearerUid)), type: PopupType.Large);
        }
        else
        {
            RemComp<GreatSageComponent>(wearerUid);
            _noticeSystem.Notify(wearerUid, Loc.GetString("martial-art-great-sage-sleeps"));
        }
    }

    [SubscribeLocalEvent]
    private void OnGreatSageDamageModify(Entity<GreatSageComponent> entity, ref DamageModifyEvent args)
    {
        if (args.Damage.GetTotal() > 0)
            args.Damage *= entity.Comp.DamageMultiplier;
    }

    #endregion
}
