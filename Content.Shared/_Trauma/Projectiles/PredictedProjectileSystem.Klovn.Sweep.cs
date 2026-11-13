// KS14: added in this fork
using System.Numerics;
using Content.Shared._KS14.Projectiles;
using Content.Shared.Damage.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Trauma.Projectiles;

/// <summary>
///     Rounds aimed at something only they can hit (<see cref="RequireProjectileTargetComponent"/>: a light, a mob lying down)
///         hit it whenever they go through it.
/// </summary>
/// <remarks>
///     <para>
///         Physics has no continuous collision (<c>SolveTOI</c> is a TODO), and a round is a sensor found by overlap, step by
///             step. At 40 tiles a second it moves more than a tile a tick: through a light's fixture, 0.15 of a tile deep,
///             it can step clean over it, and where the light is on a wall it can step onto the light and the wall at once,
///             when whichever contact is handled first spends the round. Either way an accurate round went through the light
///             and left it lit, as often as not.
///     </para>
///     <para>
///         So before each physics step, a round with a <see cref="TargetedProjectileComponent"/> whose target is one of
///             those looks along where it is about to go. If that crosses the target's fixture with nothing it would hit on
///             the way there, it hits the target now (<see cref="DoHit(EntityUid, EntityUid)"/>), and the contact the step
///             may yet make with it is ignored (<see cref="KsSweptHitComponent"/>), so a round that goes on through does not
///             hit it twice. Only those rounds, of which there are few: everything else is left to physics as it was.
///     </para>
/// </remarks>
public sealed partial class PredictedProjectileSystem
{
    private EntityQuery<RequireProjectileTargetComponent> _ksRequireTargetQuery;
    private EntityQuery<KsSweptHitComponent> _ksSweptHitQuery;

    private void KsInitializeSweep()
    {
        _ksRequireTargetQuery = GetEntityQuery<RequireProjectileTargetComponent>();
        _ksSweptHitQuery = GetEntityQuery<KsSweptHitComponent>();

        // Before the step that would carry the round through.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var enumerator = EntityQueryEnumerator<TargetedProjectileComponent, ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var targetedComponent, out var projectileComponent, out var physicsComponent, out var transformComponent))
        {
            var targetUid = targetedComponent.Target;

            if (projectileComponent.ProjectileSpent ||
                !_ksRequireTargetQuery.TryComp(targetUid, out var requireTargetComponent) ||
                !requireTargetComponent.Active ||
                _ksSweptHitQuery.HasComp(uid) ||
                TerminatingOrDeleted(targetUid))
                continue;

            var step = physicsComponent.LinearVelocity * frameTime;
            if (step.LengthSquared() < 0.0001f)
                continue;

            if (!KsTryGetSweepFraction(uid, transformComponent, targetUid, step, out var fraction) ||
                KsIsBlockedBefore(uid, transformComponent, targetUid, step * fraction))
                continue;

            AddComp(uid, new KsSweptHitComponent { HitUid = targetUid });
            DoHit(uid, targetUid);
        }
    }

    /// <summary>
    ///     Skips the contact physics makes with what a round already hit by sweeping. See the remarks on the system.
    /// </summary>
    private bool KsIsSweptHit(EntityUid uid, EntityUid otherUid)
    {
        return _ksSweptHitQuery.TryComp(uid, out var sweptHitComponent) && sweptHitComponent.HitUid == otherUid;
    }

    /// <summary>
    ///     How far along <paramref name="step"/> the round meets <paramref name="targetUid"/>'s fixture, from 0 (it already
    ///         does) to 1. Its fixture's box is grown by half the round's size, so a round's edge counts, not its middle.
    /// </summary>
    private bool KsTryGetSweepFraction(EntityUid uid, TransformComponent transformComponent, EntityUid targetUid, Vector2 step, out float fraction)
    {
        fraction = 0f;

        if (FindHardFixture(targetUid) is not { } targetFixture ||
            !_fixturesQuery.TryComp(uid, out var fixturesComponent) ||
            !fixturesComponent.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var projectileFixture))
            return false;

        var start = _transformSystem.GetWorldPosition(transformComponent);
        var roundBox = projectileFixture.Shape.ComputeAABB(_physicsSystem.GetPhysicsTransform(uid), 0);
        var targetBox = targetFixture.Shape.ComputeAABB(_physicsSystem.GetPhysicsTransform(targetUid), 0)
            .Enlarged(MathF.Max(roundBox.Width, roundBox.Height) / 2f);

        return KsIntersectSegment(targetBox, start, step, out fraction);
    }

    /// <summary>
    ///     Whether something the round would hit is on the way to <paramref name="targetUid"/>, <paramref name="toTarget"/> on
    ///         from where it is: a wall or window between, a mob in front. What it cannot hit is not in the way: anything
    ///         else that only takes a round aimed at it, and what it was fired from.
    /// </summary>
    private bool KsIsBlockedBefore(EntityUid uid, TransformComponent transformComponent, EntityUid targetUid, Vector2 toTarget)
    {
        var length = toTarget.Length();
        if (length < 0.01f ||
            !_fixturesQuery.TryComp(uid, out var fixturesComponent) ||
            !fixturesComponent.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var projectileFixture))
            return false;

        var shooterUid = _query.CompOrNull(uid)?.Shooter;
        var ray = new CollisionRay(_transformSystem.GetWorldPosition(transformComponent), toTarget / length, projectileFixture.CollisionMask);

        foreach (var _ in _physicsSystem.IntersectRayWithPredicate(transformComponent.MapID,
                     ray,
                     length,
                     otherUid => otherUid == uid ||
                         otherUid == targetUid ||
                         otherUid == shooterUid ||
                         _ksRequireTargetQuery.TryComp(otherUid, out var other) && other.Active ||
                         FindHardFixture(otherUid) == null,
                     returnOnFirstHit: true))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Where the segment from <paramref name="start"/> along <paramref name="step"/> first meets <paramref name="box"/>, as a
    ///         fraction of the step: 0 if it starts inside.
    /// </summary>
    private static bool KsIntersectSegment(Box2 box, Vector2 start, Vector2 step, out float fraction)
    {
        var enter = 0f;
        var leave = 1f;
        fraction = 0f;

        for (var axis = 0; axis < 2; axis++)
        {
            var origin = axis == 0 ? start.X : start.Y;
            var delta = axis == 0 ? step.X : step.Y;
            var min = axis == 0 ? box.Left : box.Bottom;
            var max = axis == 0 ? box.Right : box.Top;

            if (MathF.Abs(delta) < 0.00001f)
            {
                if (origin < min || origin > max)
                    return false;

                continue;
            }

            var near = (min - origin) / delta;
            var far = (max - origin) / delta;
            if (near > far)
                (near, far) = (far, near);

            enter = MathF.Max(enter, near);
            leave = MathF.Min(leave, far);

            if (enter > leave)
                return false;
        }

        fraction = enter;
        return true;
    }
}
