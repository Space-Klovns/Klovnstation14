using System.Numerics;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The water law's own arts: the Bubble Prison, a slow drifting bubble that holds and drowns whoever it catches, and
///         Riding the Current, a glide that leaves a slick of water behind.
/// </summary>
public sealed partial class WaterLawSystem : EntitySystem
{
    private static readonly Robust.Shared.Prototypes.EntProtoId BubbleEffect = "KsFxWaterBubble";
    private static readonly TimeSpan DrownTick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WardHealTick = TimeSpan.FromSeconds(1);
    private const float WardHealPerSecond = 2f;

    private TimeSpan _nextWardHeal;

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    #region Bubble Prison

    [SubscribeLocalEvent]
    private void OnBubblePrison(Entity<CultivatorComponent> entity, ref CultivationBubblePrisonActionEvent args)
    {
        if (args.Handled)
            return;

        var from = _transformSystem.GetMapCoordinates(entity);
        var direction = _transformSystem.ToMapCoordinates(args.Target).Position - from.Position;
        if (direction.LengthSquared() < 0.0001f || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var bubbleUid = Spawn(args.Projectile, from);
        var bubble = EnsureComp<BubbleProjectileComponent>(bubbleUid);
        bubble.Caster = entity;
        bubble.TrapTime = args.TrapTime;
        bubble.DrownPerSecond = args.DrownPerSecond * _cultivationSystem.Power(entity);
        _gunSystem.ShootProjectile(bubbleUid, direction, _physicsSystem.GetMapLinearVelocity(entity), entity, user: entity, speed: args.Speed);
    }

    [SubscribeLocalEvent]
    private void OnBubbleHit(Entity<BubbleProjectileComponent> entity, ref ProjectileHitEvent args)
    {
        if (args.Target == entity.Comp.Caster || !HasComp<MobStateComponent>(args.Target))
            return;

        var trapped = EnsureComp<BubbleTrappedComponent>(args.Target);
        trapped.Until = _gameTiming.CurTime + entity.Comp.TrapTime;
        trapped.NextDrown = _gameTiming.CurTime + DrownTick;
        trapped.DrownPerSecond = entity.Comp.DrownPerSecond;
        trapped.Caster = entity.Comp.Caster;
        if (trapped.Effect is not { } effect || TerminatingOrDeleted(effect))
            trapped.Effect = SpawnAttachedTo(BubbleEffect, new EntityCoordinates(args.Target, Vector2.Zero));

        _stunSystem.TryAddStunDuration(args.Target, entity.Comp.TrapTime);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-bubble-trapped", ("target", args.Target)), type: PopupType.MediumCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        if (now >= _nextWardHeal)
        {
            // Still water mends: the ward heals whoever it shelters a little each second.
            _nextWardHeal = now + WardHealTick;
            var wardQuery = EntityQueryEnumerator<StillWaterWardComponent>();
            while (wardQuery.MoveNext(out var wardedUid, out _))
                _damageableSystem.HealEvenly(wardedUid, FixedPoint2.New(-WardHealPerSecond), origin: wardedUid);
        }

        var query = EntityQueryEnumerator<BubbleTrappedComponent>();
        while (query.MoveNext(out var uid, out var trapped))
        {
            if (now >= trapped.Until)
            {
                if (trapped.Effect is { } effect)
                    QueueDel(effect);
                RemCompDeferred<BubbleTrappedComponent>(uid);
                continue;
            }

            if (now < trapped.NextDrown)
                continue;

            trapped.NextDrown = now + DrownTick;
            _damageableSystem.TryChangeDamage(uid, CultivationSystem.Damage("Asphyxiation", trapped.DrownPerSecond), origin: trapped.Caster);
        }
    }

    #endregion

    #region Riding the Current

    [SubscribeLocalEvent]
    private void OnRidingCurrent(Entity<CultivatorComponent> entity, ref CultivationRidingCurrentActionEvent args)
    {
        if (args.Handled)
            return;

        var from = Transform(entity).Coordinates;
        var offset = _transformSystem.ToMapCoordinates(args.Target).Position - _transformSystem.GetMapCoordinates(entity).Position;
        if (offset.LengthSquared() < 0.01f || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var maxDistance = args.BaseDistance + (float)entity.Comp.Realm;
        if (offset.Length() > maxDistance)
            offset = Vector2.Normalize(offset) * maxDistance;

        // A slick of water along the way, for whoever follows.
        var steps = (int)MathF.Ceiling(offset.Length());
        for (var i = 0; i <= steps; i++)
        {
            var spot = from.Offset(offset * ((float)i / Math.Max(1, steps)));
            _puddleSystem.TrySpillAt(spot, new Solution("Water", 8), out _, sound: false);
        }

        _visualsSystem.Art(CultivationVisualsSystem.ArtMistSteam, entity);
        _throwingSystem.TryThrow(entity, offset, baseThrowSpeed: 12f, recoil: false, doSpin: false);
    }

    #endregion
}

/// <summary>
///     A Bubble Prison in flight.
/// </summary>
[RegisterComponent, Access(typeof(WaterLawSystem))]
public sealed partial class BubbleProjectileComponent : Component
{
    [DataField]
    public EntityUid? Caster;

    [DataField]
    public TimeSpan TrapTime;

    [DataField]
    public float DrownPerSecond;
}

/// <summary>
///     Held inside a bubble: can't move, can't breathe.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(WaterLawSystem))]
public sealed partial class BubbleTrappedComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Until;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDrown;

    [DataField]
    public float DrownPerSecond;

    [DataField]
    public EntityUid? Caster;

    [DataField]
    public EntityUid? Effect;
}
