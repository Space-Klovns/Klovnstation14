using Content.Server._KS14.NPC.Hands;
using Content.Shared._KS14.Execution;
using Content.Shared.Execution;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     Has an NPC execute someone, through the same do-afters as the verb a player uses. One job from start to finish, as
///         a breach is: the weapon comes out, the execution runs, and the weapon goes back, however it ends.
/// </summary>
public sealed partial class NpcExecutionSystem : EntitySystem
{
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private SharedExecutionSystem _sharedExecutionSystem = default!;
    [Dependency] private SharedGunExecutionSystem _sharedGunExecutionSystem = default!;

    [Dependency] private EntityQuery<ExecutionComponent> _executionQuery = default!;
    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;
    [Dependency] private EntityQuery<KsActiveExecutionComponent> _activeExecutionQuery = default!;
    [Dependency] private EntityQuery<NpcExecutingComponent> _executingQuery = default!;

    /// <summary>
    ///     Whether an execution <paramref name="uid"/> started is still running.
    /// </summary>
    public bool IsExecuting(EntityUid uid)
    {
        return _activeExecutionQuery.HasComp(uid);
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
