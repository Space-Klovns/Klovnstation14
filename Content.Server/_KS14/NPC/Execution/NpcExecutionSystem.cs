using Content.Server._KS14.NPC.Hands;
using Content.Shared._KS14.Execution;
using Content.Shared.Execution;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     Has an NPC execute someone, through the same do-afters as the verb a player uses. One job from start to finish, as
///         a breach is: the weapon comes out, the execution runs, and the weapon goes back, however it ends.
/// </summary>
public sealed partial class NpcExecutionSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private SharedExecutionSystem _sharedExecutionSystem = default!;
    [Dependency] private SharedGunExecutionSystem _sharedGunExecutionSystem = default!;

    [Dependency] private EntityQuery<ExecutionComponent> _executionQuery = default!;
    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;
    [Dependency] private EntityQuery<KsActiveExecutionComponent> _activeExecutionQuery = default!;
    [Dependency] private EntityQuery<NpcExecutingComponent> _executingQuery = default!;
    [Dependency] private EntityQuery<NpcExecutionRefusedComponent> _refusedQuery = default!;

    /// <summary>
    ///     Whether an execution <paramref name="uid"/> started is still running.
    /// </summary>
    public bool IsExecuting(EntityUid uid)
    {
        return _activeExecutionQuery.HasComp(uid);
    }

    /// <summary>
    ///     Whether an execution of <paramref name="targetUid"/> was cancelled, so that executioners shoot it instead of trying
    ///         again. It stays that way until the target is back on its feet.
    /// </summary>
    public bool IsRefused(EntityUid targetUid)
    {
        return _refusedQuery.HasComp(targetUid);
    }

    /// <summary>
    ///     Whether <paramref name="targetUid"/> is critical and still one to walk up to, rather than shoot.
    /// </summary>
    public bool AwaitsExecution(EntityUid targetUid)
    {
        return _mobStateSystem.IsCritical(targetUid) && !IsRefused(targetUid);
    }

    /// <summary>
    ///     Called when the execution <paramref name="uid"/> started on <paramref name="targetUid"/> is over and the victim is
    ///         still alive. If the do-after was cancelled - for whatever reason, the victim being moved as much as the
    ///         executioner - the victim is not tried again, but shot. The execution systems say nothing of why a do-after
    ///         ended, but a completed one leaves its victim dead, so one that did not is one that was cancelled. Not counted
    ///         is an execution that never started (the do-after was refused), a task that was dropped before it was over
    ///         (that goes through <see cref="FinishExecution"/>, and the do-after is not this system's to judge any more),
    ///         or an executioner that died.
    /// </summary>
    public void NoteIfCancelled(EntityUid uid, EntityUid targetUid)
    {
        if (!_executingQuery.HasComp(uid) ||
            IsExecuting(uid) ||
            _mobStateSystem.IsDead(uid) ||
            _mobStateSystem.IsDead(targetUid))
            return;

        EnsureComp<NpcExecutionRefusedComponent>(targetUid);
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<NpcExecutionRefusedComponent> entity, ref MobStateChangedEvent args)
    {
        // Back on its feet, it starts afresh the next time it goes down.
        if (args.NewMobState == MobState.Alive)
            RemCompDeferred<NpcExecutionRefusedComponent>(entity);
    }

    /// <summary>
    ///     Takes <paramref name="weaponUid"/> out and starts executing <paramref name="targetUid"/> with it. Follow it with
    ///         <see cref="FinishExecution"/>, however it goes.
    /// </summary>
    public bool TryStartExecution(EntityUid uid, EntityUid targetUid, EntityUid weaponUid)
    {
        if (!_npcHandsSystem.TryTakeOut(uid, weaponUid, out var takenOut))
            return false;

        EnsureComp<NpcExecutingComponent>(uid).TakenOut = takenOut;

        if (_gunQuery.TryComp(weaponUid, out var gunComponent))
            _sharedGunExecutionSystem.TryStartGunExecutionDoafter(weaponUid, targetUid, uid, gunComponent.GunExecutionTime);
        else if (_executionQuery.TryComp(weaponUid, out var executionComponent))
            _sharedExecutionSystem.TryStartExecutionDoAfter(weaponUid, targetUid, uid, executionComponent);

        if (IsExecuting(uid))
            return true;

        FinishExecution(uid);
        return false;
    }

    /// <summary>
    ///     Puts the weapon back and everything as it was. Does nothing for an NPC that is not executing anyone.
    /// </summary>
    public void FinishExecution(EntityUid uid)
    {
        if (!_executingQuery.TryComp(uid, out var executingComponent))
            return;

        _npcHandsSystem.PutBack(uid, executingComponent.TakenOut);
        RemComp<NpcExecutingComponent>(uid);
    }
}
