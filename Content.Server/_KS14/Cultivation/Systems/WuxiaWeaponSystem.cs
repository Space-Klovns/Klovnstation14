using System.Numerics;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Timing;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Random;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Ordinary wuxia weapons anyone can make: no qi needed, one trick each. And the hidden weapons a cultivator throws
///         harder than anyone.
/// </summary>
public sealed partial class WuxiaWeaponSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private CultivationStanceSystem _stanceSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private JianghuSystem _jianghuSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;

    /// <summary>
    ///     A good hit from range can wrap the rope around them and yank them a step closer.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMeteorHammerHit(Entity<MeteorHammerComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0 || !_robustRandom.Prob(entity.Comp.PullChance))
            return;

        var victimUid = args.HitEntities[0];
        var userPosition = _transformSystem.GetWorldPosition(args.User);
        var offset = userPosition - _transformSystem.GetWorldPosition(victimUid);
        if (!HasComp<MobStateComponent>(victimUid) || offset.Length() < 1.5f)
            return;

        _transformSystem.SetWorldPosition(victimUid, _transformSystem.GetWorldPosition(victimUid) + Vector2.Normalize(offset));
        _noticeSystem.Announce(victimUid, Loc.GetString("wuxia-meteor-hammer-yank", ("user", args.User), ("target", victimUid)), type: PopupType.Medium);
    }

    /// <summary>
    ///     Shoot the dart out at someone up to a few tiles away and snap it back, maybe with what they were holding.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRopeDartUsed(Entity<RopeDartComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || args.CanReach || args.Target is not { } victimUid || !HasComp<MobStateComponent>(victimUid)
            || !_interactionSystem.InRangeUnobstructed(args.User, victimUid, range: entity.Comp.Range))
            return;

        args.Handled = true;
        if (TryComp<UseDelayComponent>(entity, out var useDelayComponent) && !_useDelaySystem.TryResetDelay((entity, useDelayComponent), checkDelayed: true))
        {
            _noticeSystem.Notify(args.User, Loc.GetString("wuxia-rope-dart-coiling"));
            return;
        }

        _damageableSystem.TryChangeDamage(victimUid, CultivationSystem.Damage("Piercing", entity.Comp.Damage), origin: args.User);
        _noticeSystem.Announce(victimUid, Loc.GetString("wuxia-rope-dart-stings", ("user", args.User), ("target", victimUid)), type: PopupType.MediumCaution);

        if (_handsSystem.GetActiveItem(victimUid) is { } heldUid && _robustRandom.Prob(entity.Comp.DisarmChance)
            && _handsSystem.TryDrop(victimUid, heldUid, checkActionBlocker: false))
            _noticeSystem.Announce(victimUid, Loc.GetString("wuxia-rope-dart-tears", ("target", victimUid), ("item", heldUid)), type: PopupType.Medium);
    }

    /// <summary>
    ///     Wielded in both hands, a heavy swing can sweep someone off their feet; bamboo splits after enough of them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBambooStaffHit(Entity<BambooStaffComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0 || !TryComp<WieldableComponent>(entity, out var wieldableComponent)
            || !wieldableComponent.Wielded || !_robustRandom.Prob(entity.Comp.SweepChance))
            return;

        var victimUid = args.HitEntities[0];
        if (!HasComp<MobStateComponent>(victimUid))
            return;

        _stunSystem.TryKnockdown(victimUid, entity.Comp.SweepDuration, refresh: true);
        _noticeSystem.Announce(victimUid, Loc.GetString("wuxia-bamboo-sweep", ("user", args.User), ("target", victimUid)), type: PopupType.MediumCaution);

        entity.Comp.SweepsLeft--;
        if (entity.Comp.SweepsLeft > 0)
            return;

        _noticeSystem.Announce(args.User, Loc.GetString("wuxia-bamboo-splits"), type: PopupType.MediumCaution);
        QueueDel(entity);
    }

    /// <summary>
    ///     In a member's hand a sect's sword strikes truer.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSectJianHit(Entity<SectJianComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0 || entity.Comp.Sect is not { } sectUid
            || _jianghuSystem.GetSect(args.User)?.Owner != sectUid)
            return;

        args.BonusDamage += CultivationSystem.Damage("Slash", entity.Comp.MemberBonusDamage);
        foreach (var victimUid in args.HitEntities)
            _staminaSystem.TakeStaminaDamage(victimUid, entity.Comp.MemberStaminaDamage, source: args.User);
    }

    [SubscribeLocalEvent]
    private void OnSectJianExamined(Entity<SectJianComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wuxia-sect-jian-examine"));
    }

    /// <summary>
    ///     A cultivator's needle finds an acupoint from across the room, and their limbs grow heavy.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnNeedleHit(Entity<AcupointNeedleComponent> entity, ref ThrowDoHitEvent args)
    {
        if (args.Component.Thrower is not { } throwerUid || !_cultivatorQuery.HasComp(throwerUid) || !HasComp<MobStateComponent>(args.Target))
            return;

        _stanceSystem.ApplySnare(args.Target, entity.Comp.SlowDuration, entity.Comp.SlowMultiplier);
        _noticeSystem.Notify(args.Target, Loc.GetString("wuxia-needle-acupoint"), type: PopupType.MediumCaution);
    }

    /// <summary>
    ///     Cultivators throw flying daggers hard enough to bury them to the hilt.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFlyingDaggerHit(Entity<FlyingDaggerComponent> entity, ref ThrowDoHitEvent args)
    {
        if (args.Component.Thrower is not { } throwerUid || !_cultivatorQuery.TryComp(throwerUid, out var cultivatorComponent)
            || !HasComp<MobStateComponent>(args.Target))
            return;

        var bonus = entity.Comp.BonusPerRealm * (float)((int)cultivatorComponent.Realm + 1);
        _damageableSystem.TryChangeDamage(args.Target, CultivationSystem.Damage("Slash", bonus), origin: throwerUid);
    }

    [SubscribeLocalEvent]
    private void OnSmokePelletUsed(Entity<SmokePelletComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _noticeSystem.Announce(args.User, Loc.GetString("wuxia-smoke-pellet-crushed", ("user", args.User)), type: PopupType.Medium);
        Pop(entity, Transform(args.User).Coordinates);
    }

    [SubscribeLocalEvent]
    private void OnSmokePelletLand(Entity<SmokePelletComponent> entity, ref LandEvent args)
    {
        Pop(entity, Transform(entity).Coordinates);
    }

    private void Pop(Entity<SmokePelletComponent> entity, Robust.Shared.Map.EntityCoordinates centreCoordinates)
    {
        if (EntityManager.IsQueuedForDeletion(entity))
            return;

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
                Spawn(entity.Comp.Smoke, centreCoordinates.Offset(new Vector2((float)x, (float)y)));
        }

        QueueDel(entity);
    }
}
