using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using System.Numerics;
using Content.Shared.Movement.Systems;
using Content.Shared.Sprite;
using Robust.Shared.Physics.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Cultivation.Systems;

/// <summary>
///     Timed techniques that change how a body takes damage or moves: Still Water Ward, Rooted Mountain Stance,
///         Golden Bell Shield and Graceful Waves Steps. Shared, so the slowdown is predicted.
/// </summary>
public sealed partial class CultivationStanceSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private SharedCultivationSystem _cultivationSystem = default!;
    [Dependency] private SharedScaleVisualsSystem _scaleVisualsSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Removal is the server's call; the client just follows the networked state.
        if (_netManager.IsClient)
            return;

        var currentTime = _gameTiming.CurTime;

        var wardEnumerator = EntityQueryEnumerator<StillWaterWardComponent>();
        while (wardEnumerator.MoveNext(out var uid, out var wardComponent))
        {
            if (currentTime >= wardComponent.EndTime)
                RemCompDeferred(uid, wardComponent);
        }

        var stanceEnumerator = EntityQueryEnumerator<RootedStanceComponent>();
        while (stanceEnumerator.MoveNext(out var uid, out var stanceComponent))
        {
            if (currentTime >= stanceComponent.EndTime)
                RemCompDeferred(uid, stanceComponent);
        }

        var bellEnumerator = EntityQueryEnumerator<GoldenBellComponent>();
        while (bellEnumerator.MoveNext(out var uid, out var bellComponent))
        {
            if (currentTime >= bellComponent.EndTime)
                RemCompDeferred(uid, bellComponent);
        }

        var stepsEnumerator = EntityQueryEnumerator<GracefulStepsComponent>();
        while (stepsEnumerator.MoveNext(out var uid, out var stepsComponent))
        {
            if (currentTime >= stepsComponent.EndTime)
                RemCompDeferred(uid, stepsComponent);
        }

        ExpireAll<CultivationSnaredComponent>(currentTime, snared => snared.EndTime);
        ExpireAll<CultivationHasteComponent>(currentTime, haste => haste.EndTime);
        ExpireAll<GoldenBellQiComponent>(currentTime, bell => bell.EndTime);
        ExpireAll<DharmaIdolComponent>(currentTime, idol => idol.EndTime);
        ExpireAll<TurtleBreathingComponent>(currentTime, turtle => turtle.EndTime);
    }

    private void ExpireAll<T>(TimeSpan currentTime, Func<T, TimeSpan> endTime) where T : IComponent
    {
        var enumerator = EntityQueryEnumerator<T>();
        while (enumerator.MoveNext(out var uid, out var component))
        {
            if (currentTime >= endTime(component))
                RemCompDeferred(uid, component);
        }
    }

    #region Still Water Ward

    public void ApplyWard(EntityUid uid, TimeSpan duration, float damageMultiplier, bool releaseWave)
    {
        var wardComponent = EnsureComp<StillWaterWardComponent>(uid);
        wardComponent.EndTime = _gameTiming.CurTime + duration;
        wardComponent.DamageMultiplier = damageMultiplier;
        wardComponent.ReleaseWave = releaseWave;
        Dirty(uid, wardComponent);
    }

    [SubscribeLocalEvent]
    private void OnWardDamageModify(Entity<StillWaterWardComponent> entity, ref DamageModifyEvent args)
    {
        var before = args.Damage.GetTotal();
        if (before <= 0)
            return;

        args.Damage *= entity.Comp.DamageMultiplier;
        entity.Comp.Stored += (float)(before - args.Damage.GetTotal());
    }

    [SubscribeLocalEvent]
    private void OnWardShutdown(Entity<StillWaterWardComponent> entity, ref ComponentShutdown args)
    {
        if (!entity.Comp.ReleaseWave || TerminatingOrDeleted(entity))
            return;

        var ev = new StillWaterWardReleasedEvent(entity.Comp.Stored);
        RaiseLocalEvent(entity, ref ev);
    }

    #endregion

    #region Rooted Mountain Stance

    public void ApplyRootedStance(EntityUid uid, TimeSpan duration, float damageMultiplier, float speedMultiplier)
    {
        var stanceComponent = EnsureComp<RootedStanceComponent>(uid);
        stanceComponent.EndTime = _gameTiming.CurTime + duration;
        stanceComponent.DamageMultiplier = damageMultiplier;
        stanceComponent.SpeedMultiplier = speedMultiplier;
        Dirty(uid, stanceComponent);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    [SubscribeLocalEvent]
    private void OnStanceDamageModify(Entity<RootedStanceComponent> entity, ref DamageModifyEvent args)
    {
        if (args.Damage.GetTotal() > 0)
            args.Damage *= entity.Comp.DamageMultiplier;
    }

    [SubscribeLocalEvent]
    private void OnStanceRefreshSpeed(Entity<RootedStanceComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        // Shut down but not yet removed: don't keep slowing them.
        if (entity.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.ModifySpeed(entity.Comp.SpeedMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnStanceShutdown(Entity<RootedStanceComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity))
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity);
    }

    #endregion

    #region Golden Bell Shield

    /// <summary>
    ///     The kinds of damage hardened skin turns aside.
    /// </summary>
    private static readonly string[] PhysicalDamageTypes = { "Blunt", "Slash", "Piercing" };

    public void ApplyGoldenBell(EntityUid uid, TimeSpan duration, float damageMultiplier)
    {
        var bellComponent = EnsureComp<GoldenBellComponent>(uid);
        bellComponent.EndTime = _gameTiming.CurTime + duration;
        bellComponent.DamageMultiplier = damageMultiplier;
        Dirty(uid, bellComponent);
    }

    [SubscribeLocalEvent]
    private void OnBellDamageModify(Entity<GoldenBellComponent> entity, ref DamageModifyEvent args)
    {
        foreach (var type in PhysicalDamageTypes)
        {
            if (args.Damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                args.Damage.DamageDict[type] = amount * entity.Comp.DamageMultiplier;
        }
    }

    #endregion

    #region Graceful Waves Steps

    public void ApplyGracefulSteps(EntityUid uid, TimeSpan duration, float speedMultiplier)
    {
        var stepsComponent = EnsureComp<GracefulStepsComponent>(uid);
        stepsComponent.EndTime = _gameTiming.CurTime + duration;
        stepsComponent.SpeedMultiplier = speedMultiplier;
        Dirty(uid, stepsComponent);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    [SubscribeLocalEvent]
    private void OnStepsRefreshSpeed(Entity<GracefulStepsComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (entity.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.ModifySpeed(entity.Comp.SpeedMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnStepsShutdown(Entity<GracefulStepsComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity))
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity);
    }

    #endregion

    #region Snares and haste

    public void ApplySnare(EntityUid uid, TimeSpan duration, float speedMultiplier)
    {
        var snaredComponent = EnsureComp<CultivationSnaredComponent>(uid);
        snaredComponent.EndTime = _gameTiming.CurTime + duration;
        // The strongest hold wins while two overlap.
        snaredComponent.SpeedMultiplier = snaredComponent.LifeStage <= ComponentLifeStage.Running && snaredComponent.SpeedMultiplier < speedMultiplier
            ? snaredComponent.SpeedMultiplier
            : speedMultiplier;
        Dirty(uid, snaredComponent);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    public void ApplyHaste(EntityUid uid, TimeSpan duration, float speedMultiplier)
    {
        var hasteComponent = EnsureComp<CultivationHasteComponent>(uid);
        hasteComponent.EndTime = _gameTiming.CurTime + duration;
        hasteComponent.SpeedMultiplier = speedMultiplier;
        Dirty(uid, hasteComponent);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    [SubscribeLocalEvent]
    private void OnSnaredRefreshSpeed(Entity<CultivationSnaredComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (entity.Comp.LifeStage <= ComponentLifeStage.Running)
            args.ModifySpeed(entity.Comp.SpeedMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnSnaredShutdown(Entity<CultivationSnaredComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity))
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity);
    }

    [SubscribeLocalEvent]
    private void OnHasteRefreshSpeed(Entity<CultivationHasteComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (entity.Comp.LifeStage <= ComponentLifeStage.Running)
            args.ModifySpeed(entity.Comp.SpeedMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnHasteShutdown(Entity<CultivationHasteComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity))
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity);
    }

    #endregion

    #region Golden Bell Qi, Dharma Idol, Turtle Breathing

    public void ApplyGoldenBellQi(EntityUid uid, TimeSpan duration)
    {
        var bellComponent = EnsureComp<GoldenBellQiComponent>(uid);
        bellComponent.EndTime = _gameTiming.CurTime + duration;
        Dirty(uid, bellComponent);
    }

    [SubscribeLocalEvent]
    private void OnGoldenBellQiDamageModify(Entity<GoldenBellQiComponent> entity, ref DamageModifyEvent args)
    {
        if (entity.Comp.LifeStage <= ComponentLifeStage.Running && args.Damage.GetTotal() > 0)
            args.Damage *= 0f;
    }

    public void ApplyDharmaIdol(EntityUid uid, TimeSpan duration, float damageMultiplier)
    {
        var idolComponent = EnsureComp<DharmaIdolComponent>(uid);
        idolComponent.EndTime = _gameTiming.CurTime + duration;
        idolComponent.DamageMultiplier = damageMultiplier;
        Dirty(uid, idolComponent);
        _scaleVisualsSystem.SetSpriteScale(uid, new Vector2(1.4f, 1.4f));
    }

    [SubscribeLocalEvent]
    private void OnDharmaIdolDamageModify(Entity<DharmaIdolComponent> entity, ref DamageModifyEvent args)
    {
        if (entity.Comp.LifeStage <= ComponentLifeStage.Running && args.Damage.GetTotal() > 0)
            args.Damage *= entity.Comp.DamageMultiplier;
    }

    [SubscribeLocalEvent]
    private void OnDharmaIdolShutdown(Entity<DharmaIdolComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity))
            _scaleVisualsSystem.SetSpriteScale(entity, Vector2.One);
    }

    public void ApplyTurtleBreathing(EntityUid uid, TimeSpan duration)
    {
        var turtleComponent = EnsureComp<TurtleBreathingComponent>(uid);
        turtleComponent.EndTime = _gameTiming.CurTime + duration;
        Dirty(uid, turtleComponent);
    }

    [SubscribeLocalEvent]
    private void OnTurtleBreathingDamageModify(Entity<TurtleBreathingComponent> entity, ref DamageModifyEvent args)
    {
        if (entity.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        // No breath is needed, and the cold of the void can't reach a body this still.
        foreach (var type in TurtleImmuneTypes)
        {
            if (args.Damage.DamageDict.ContainsKey(type))
                args.Damage.DamageDict[type] = 0;
        }
    }

    private static readonly string[] TurtleImmuneTypes = { "Asphyxiation", "Cold" };

    #endregion

    #region Barrier arrays

    [SubscribeLocalEvent]
    private void OnBarrierPreventCollide(Entity<QiBarrierComponent> entity, ref PreventCollideEvent args)
    {
        if (entity.Comp.Maker is not { } makerUid)
            return;

        if (args.OtherEntity == makerUid || _cultivationSystem.IsAlly(makerUid, args.OtherEntity))
            args.Cancelled = true;
    }

    #endregion
}

/// <summary>
///     Raised when a Water Root's ward ends, carrying how much force it soaked up.
/// </summary>
[ByRefEvent]
public readonly record struct StillWaterWardReleasedEvent(float Stored);
