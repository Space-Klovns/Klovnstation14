using System.Numerics;
using Robust.Shared.Physics.Components;
using Content.Server._KS14.NPC.Components;
using Content.Server.NPC.Components;
using Content.Shared._KS14.LaserSight;
using Content.Shared._KS14.LockOn;

namespace Content.Server.NPC.Systems;

// Turret tracking, lock-on and laser sight hooks for ranged NPCs. Partial so the upstream file stays minimal.
public sealed partial class NPCCombatSystem
{
    [Dependency] private SharedKsTargetLockOnSystem _lockOnSystem = default!;
    [Dependency] private KsLaserSightSystem _laserSightSystem = default!;

    [Dependency] private EntityQuery<KsNpcAimTrackingComponent> _aimTrackingQuery = default!;
    [Dependency] private EntityQuery<KsTargetLockOnComponent> _targetLockOnQuery = default!;
    [Dependency] private EntityQuery<KsLaserSightComponent> _laserSightQuery = default!;

    /// <summary>
    ///     Angular velocities above this (rad/s) are treated as teleports and clamped.
    /// </summary>
    private const double MaxTrackedAngularVelocity = 4d * Math.PI;

    /// <summary>
    ///     Updates the lock-on and the laser sight from the current line-of-sight state.
    /// </summary>
    private void KsUpdateSight(EntityUid shooterUid, EntityUid targetUid, bool targetVisible)
    {
        if (_targetLockOnQuery.TryComp(shooterUid, out var lockOnComponent))
            _lockOnSystem.UpdateLock((shooterUid, lockOnComponent), targetUid, targetVisible);

        if (_laserSightQuery.TryComp(shooterUid, out var laserSightComponent) && laserSightComponent.OnlyWhileTargeting)
            _laserSightSystem.SetEnabled((shooterUid, laserSightComponent), targetVisible);

        // A reacquired target would otherwise produce a one-tick angular velocity spike from the stale goal angle.
        if (!targetVisible && _aimTrackingQuery.TryComp(shooterUid, out var trackingComponent))
            trackingComponent.LastGoalAngle = null;
    }

    /// <summary>
    ///     Stops targeting entirely: clears the lock and turns off a laser that is only on while targeting.
    ///         For the early-outs of <see cref="UpdateRanged"/> where the sight is never updated.
    /// </summary>
    private void KsStopTargeting(EntityUid shooterUid)
    {
        _lockOnSystem.ClearLock(shooterUid);

        if (_laserSightQuery.TryComp(shooterUid, out var laserSightComponent) && laserSightComponent.OnlyWhileTargeting)
            _laserSightSystem.SetEnabled((shooterUid, laserSightComponent), false);

        if (_aimTrackingQuery.TryComp(shooterUid, out var trackingComponent))
            trackingComponent.LastGoalAngle = null;
    }

    private void KsOnRangedShutdown(EntityUid shooterUid)
    {
        if (TerminatingOrDeleted(shooterUid))
            return;

        _lockOnSystem.ClearLock(shooterUid);

        if (_laserSightQuery.TryComp(shooterUid, out var laserSightComponent) && laserSightComponent.OnlyWhileTargeting)
            _laserSightSystem.SetEnabled((shooterUid, laserSightComponent), false);

        if (_aimTrackingQuery.TryComp(shooterUid, out var trackingComponent))
        {
            trackingComponent.LastGoalAngle = null;
            trackingComponent.LastTarget = null;
        }
    }

    /// <summary>
    ///     Leads the target and rotates towards it, using the tracking-adjusted speed when the shooter has
    ///         <see cref="KsNpcAimTrackingComponent"/>. Returns whether the shooter is aimed accurately enough to fire.
    /// </summary>
    private bool KsTryAim(
        EntityUid shooterUid,
        NPCRangedCombatComponent rangedComponent,
        TransformComponent shooterTransform,
        PhysicsComponent targetBody,
        Vector2 targetPos,
        Vector2 worldPos,
        float distance,
        float frameTime,
        out Vector2 targetSpot)
    {
        targetSpot = targetPos + targetBody.LinearVelocity * distance / ShootSpeed;

        var goalRotation = (targetSpot - worldPos).ToWorldAngle();
        double rotationSpeed = rangedComponent.RotationSpeed?.Theta ?? double.MaxValue;
        var tolerance = rangedComponent.AccuracyThreshold;

        if (_aimTrackingQuery.TryComp(shooterUid, out var trackingComponent))
        {
            if (trackingComponent.LastTarget != rangedComponent.Target)
            {
                trackingComponent.LastTarget = rangedComponent.Target;
                trackingComponent.LastGoalAngle = null;
            }

            // Instant rotation (no speed) needs no compensation.
            if (rotationSpeed < float.MaxValue)
            {
                if (trackingComponent.LastGoalAngle is { } lastGoalAngle && frameTime > 0f)
                {
                    var angularVelocity = Math.Abs(Angle.ShortestDistance(lastGoalAngle, goalRotation).Theta) / frameTime;
                    angularVelocity = Math.Min(angularVelocity, MaxTrackedAngularVelocity);
                    rotationSpeed += angularVelocity * trackingComponent.AngularVelocityCompensation;
                }

                if (trackingComponent.MaxRotationSpeed is { } maxRotationSpeed)
                    rotationSpeed = Math.Min(rotationSpeed, maxRotationSpeed.Theta);
            }

            trackingComponent.LastGoalAngle = goalRotation;

            if (trackingComponent.AccuracyThreshold is { } thresholdOverride)
                tolerance = thresholdOverride;
        }

        return _rotate.TryRotateTo(shooterUid, goalRotation, frameTime, tolerance, rotationSpeed, shooterTransform);
    }
}
