using Content.Server._KS14.Explosion;
using Content.Shared._KS14.Throwing;
using Content.Shared.Buckle;
using Content.Shared.Database;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Explosion.EntitySystems;

// Explosions send mobs with KsExplosionThrownComponent flying, marked to slam into what they hit.
public sealed partial class ExplosionSystem
{
    [Dependency] private KsThrownSlamSystem _ksThrownSlamSystem = default!;
    [Dependency] private PullingSystem _ksPullingSystem = default!;
    [Dependency] private SharedBuckleSystem _ksBuckleSystem = default!;
    [Dependency] private SharedPhysicsSystem _ksPhysicsSystem = default!;
    [Dependency] private SharedStunSystem _ksStunSystem = default!;

    [Dependency] private EntityQuery<KsExplosionThrownComponent> _explosionThrownQuery = default!;

    /// <summary>
    ///     Throws a mob away from <paramref name="epicenter"/>, as fast and as far as <paramref name="throwForce"/>
    ///         and its <see cref="KsExplosionThrownComponent"/> say.
    /// </summary>
    /// <param name="mob">Its transform is null for something anchored or in a container, as
    ///     <see cref="ProcessEntity"/> has it: that is never thrown.</param>
    private void KsThrowMob(Entity<TransformComponent?> mob, MapCoordinates epicenter, float throwForce)
    {
        // Cheapest first: nearly everything an explosion touches is no mob.
        if (mob.Comp is not { Anchored: false } transformComponent ||
            !_explosionThrownQuery.TryGetComponent(mob, out var thrownComponent) ||
            throwForce < thrownComponent.MinThrowForce ||
            EntityManager.IsQueuedForDeletion(mob) ||
            !_physicsQuery.TryGetComponent(mob, out var physicsComponent) ||
            physicsComponent.BodyType != BodyType.KinematicController ||
            _ksBuckleSystem.IsBuckled(mob.Owner))
        {
            return;
        }

        // Whoever holds them loses them: a pull's joint would yank them back mid-flight.
        if (TryComp<PullableComponent>(mob, out var pullableComponent) && pullableComponent.BeingPulled)
            _ksPullingSystem.TryStopPull(mob, pullableComponent);

        var direction = _transformSystem.GetWorldPosition(transformComponent) - epicenter.Position;
        direction = direction.IsLengthZero() ? _robustRandom.NextVector2().Normalized() : direction.Normalized();

        if (thrownComponent.KnockdownTime is { } knockdownTime)
            _ksStunSystem.TryKnockdown(mob.Owner, knockdownTime, drop: false);

        var distance = MathF.Min(throwForce * thrownComponent.DistancePerForce, thrownComponent.MaxDistance);
        var speed = MathF.Min(throwForce * thrownComponent.SpeedPerForce, thrownComponent.MaxSpeed);
        _throwingSystem.TryThrow(mob, direction * distance, physicsComponent, transformComponent, baseThrowSpeed: speed,
            predicted: false);
        _ksThrownSlamSystem.MarkThrown(mob, thrownComponent.Slam);

        // A throw adds to the speed they already had, from running or from another blast a moment ago. The cap is on
        // how fast they end up, not on what each blast adds.
        var velocity = physicsComponent.LinearVelocity;
        if (velocity.LengthSquared() > thrownComponent.MaxSpeed * thrownComponent.MaxSpeed)
            _ksPhysicsSystem.SetLinearVelocity(mob, velocity.Normalized() * thrownComponent.MaxSpeed, body: physicsComponent);

        _adminLogger.Add(LogType.ExplosionHit, LogImpact.Medium,
            $"Explosion at {epicenter:epicenter} threw {ToPrettyString(mob):subject} {distance:distance} tiles at {speed:speed} m/s (throw force {throwForce:force})");
    }
}
