using Robust.Shared.Timing;

namespace Content.Shared._KS14.LockOn;

public sealed partial class SharedKsTargetLockOnSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    ///     Advances the lock state. Call every tick with the current target and whether it is visible.
    /// </summary>
    public void UpdateLock(Entity<KsTargetLockOnComponent> entity, EntityUid? target, bool targetVisible)
    {
        var lockOn = entity.Comp;

        if (target == null)
        {
            ClearLock((entity.Owner, lockOn));
            return;
        }

        var curTime = _timing.CurTime;

        if (targetVisible)
        {
            if (lockOn.Target != target)
            {
                lockOn.Target = target;
                lockOn.LockStartTime = curTime;
                lockOn.LockEndTime = curTime + lockOn.LockOnTime;
                lockOn.LostSightTime = null;
                Dirty(entity.Owner, lockOn);
                return;
            }

            lockOn.LostSightTime = null;
            return;
        }

        if (lockOn.Target == null)
            return;

        lockOn.LostSightTime ??= curTime;

        if (curTime - lockOn.LostSightTime.Value > lockOn.LoseLockGrace)
            ClearLock((entity.Owner, lockOn));
    }

    /// <summary>
    ///     True if the entity has no lock-on requirement, or it has finished locking onto <paramref name="target"/>.
    /// </summary>
    public bool IsLocked(Entity<KsTargetLockOnComponent?> entity, EntityUid target)
    {
        if (!Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return true;

        return entity.Comp.Target == target && _timing.CurTime >= entity.Comp.LockEndTime;
    }

    /// <summary>
    ///     Lock progress from 0 to 1.
    /// </summary>
    public float GetLockProgress(Entity<KsTargetLockOnComponent> entity)
    {
        var lockOn = entity.Comp;

        if (lockOn.Target == null)
            return 0f;

        var total = (float)(lockOn.LockEndTime - lockOn.LockStartTime).TotalSeconds;
        if (total <= 0f)
            return 1f;

        var elapsed = (float)(_timing.CurTime - lockOn.LockStartTime).TotalSeconds;
        return Math.Clamp(elapsed / total, 0f, 1f);
    }

    public void ClearLock(Entity<KsTargetLockOnComponent?> entity)
    {
        if (!Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.LostSightTime = null;

        if (entity.Comp.Target == null)
            return;

        entity.Comp.Target = null;
        Dirty(entity.Owner, entity.Comp);
    }
}
