using Content.Server._KS14.NPC.Hands;
using Content.Shared._KS14.Execution;
using Content.Shared._KS14.Mobs.Systems;
using Content.Shared.Execution;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     Has an NPC execute someone, through the same do-afters as the verb a player uses. One job from start to finish, as
///         a breach is: the weapon comes out, the execution runs, and the weapon goes back, however it ends.
/// </summary>
public sealed partial class NpcExecutionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsSoftCritSystem _softCritSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private SharedExecutionSystem _sharedExecutionSystem = default!;
    [Dependency] private SharedGunExecutionSystem _sharedGunExecutionSystem = default!;

    [Dependency] private EntityQuery<ExecutionComponent> _executionQuery = default!;
    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;
    [Dependency] private EntityQuery<KsActiveExecutionComponent> _activeExecutionQuery = default!;
    [Dependency] private EntityQuery<NpcExecutingComponent> _executingQuery = default!;
    [Dependency] private EntityQuery<NpcExecutionEscapedComponent> _escapedQuery = default!;
    [Dependency] private EntityQuery<NpcExecutionerComponent> _executionerQuery = default!;

    /// <summary>
    ///     Whether an execution <paramref name="uid"/> started is still running.
    /// </summary>
    public bool IsExecuting(EntityUid uid)
    {
        return _activeExecutionQuery.HasComp(uid);
    }

    /// <summary>
    ///     Whether executioners have given up on <paramref name="targetUid"/>: it is in soft crit, and has crawled out of
    ///         enough executions that they shoot it instead. Hard crit is never given up on, as nothing crawls away from
    ///         that.
    /// </summary>
    public bool IsGivenUp(EntityUid targetUid)
    {
        return _escapedQuery.TryComp(targetUid, out var escapedComponent) &&
            escapedComponent.GivenUp &&
            _softCritSystem.IsSoftCrit(targetUid);
    }

    /// <summary>
    ///     Whether <paramref name="targetUid"/> is critical and still one to walk up to, rather than shoot.
    /// </summary>
    public bool AwaitsExecution(EntityUid targetUid)
    {
        return _mobStateSystem.IsCritical(targetUid) && !IsGivenUp(targetUid);
    }

    /// <summary>
    ///     Notes that an execution of <paramref name="targetUid"/> is starting, and when the first one did. Only a victim in
    ///         soft crit is kept track of, as nothing else can get away.
    /// </summary>
    public void NoteAttempt(EntityUid targetUid)
    {
        if (!_softCritSystem.IsSoftCrit(targetUid))
            return;

        var escapedComponent = EnsureComp<NpcExecutionEscapedComponent>(targetUid);

        if (escapedComponent.FirstAttempt == TimeSpan.Zero)
            escapedComponent.FirstAttempt = _gameTiming.CurTime;
    }

    /// <summary>
    ///     Notes that an execution of <paramref name="targetUid"/> by <paramref name="uid"/> came to nothing, and gives up
    ///         on walking over to it if it keeps doing that: see <see cref="NpcExecutionerComponent"/>.
    /// </summary>
    public void NoteFailure(EntityUid uid, EntityUid targetUid)
    {
        if (!_escapedQuery.TryComp(targetUid, out var escapedComponent) ||
            !_executionerQuery.TryComp(uid, out var executionerComponent))
            return;

        escapedComponent.Failures++;

        if (escapedComponent.Failures >= executionerComponent.MaxAttempts ||
            _gameTiming.CurTime - escapedComponent.FirstAttempt >= executionerComponent.GiveUpAfter)
            escapedComponent.GivenUp = true;
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<NpcExecutionEscapedComponent> entity, ref MobStateChangedEvent args)
    {
        // Back on its feet, it starts afresh the next time it goes down.
        if (args.NewMobState == MobState.Alive)
            RemCompDeferred<NpcExecutionEscapedComponent>(entity);
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
