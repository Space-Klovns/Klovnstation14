using Content.Server._KS14.Explosion;
using Content.Shared._KS14.Throwing;
using Content.Shared.Buckle;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Server.Explosion.EntitySystems;

// Explosions send mobs with KsExplosionThrownComponent flying, marked to slam into what they hit.
public sealed partial class ExplosionSystem
{
    [Dependency] private KsThrownSlamSystem _ksThrownSlamSystem = default!;
    [Dependency] private PullingSystem _ksPullingSystem = default!;
    [Dependency] private SharedBuckleSystem _ksBuckleSystem = default!;
    [Dependency] private SharedStunSystem _ksStunSystem = default!;

    /// <summary>
    ///     Throws a mob away from <paramref name="epicenter"/>, as fast and as far as <paramref name="throwForce"/>
    ///         and its <see cref="KsExplosionThrownComponent"/> say.
    /// </summary>
    /// <param name="xform">Null for something anchored or in a container, which is never thrown.</param>
    private void KsThrowMob(EntityUid uid, TransformComponent? xform, MapCoordinates epicenter, float throwForce)
    {
        if (xform == null || xform.Anchored || EntityManager.IsQueuedForDeletion(uid) ||
            !TryComp<KsExplosionThrownComponent>(uid, out var thrown) || throwForce < thrown.MinThrowForce ||
            !_physicsQuery.TryGetComponent(uid, out var physics) || physics.BodyType != BodyType.KinematicController ||
            _ksBuckleSystem.IsBuckled(uid))
        {
            return;
        }

        // Whoever holds them loses them: a pull's joint would yank them back mid-flight.
        if (TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled)
            _ksPullingSystem.TryStopPull(uid, pullable);

        var direction = _transformSystem.GetWorldPosition(xform) - epicenter.Position;
        direction = direction.IsLengthZero() ? _robustRandom.NextVector2().Normalized() : direction.Normalized();

        if (thrown.KnockdownTime is { } knockdownTime)
            _ksStunSystem.TryKnockdown(uid, knockdownTime, drop: false);

        var distance = MathF.Min(throwForce * thrown.DistancePerForce, thrown.MaxDistance);
        var speed = MathF.Min(throwForce * thrown.SpeedPerForce, thrown.MaxSpeed);
        _throwingSystem.TryThrow(uid, direction * distance, physics, xform, speed, predicted: false);
        _ksThrownSlamSystem.MarkThrown(uid, thrown.Slam);
    }
}
