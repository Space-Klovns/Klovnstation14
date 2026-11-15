using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Cultivation.Systems;

/// <summary>
///     Timed defensive techniques that change how a body takes damage or moves: Still Water Ward and Rooted
///         Mountain Stance. Shared, so the slowdown is predicted.
/// </summary>
public sealed partial class CultivationStanceSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;

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
}

/// <summary>
///     Raised when a Water Root's ward ends, carrying how much force it soaked up.
/// </summary>
[ByRefEvent]
public readonly record struct StillWaterWardReleasedEvent(float Stored);
