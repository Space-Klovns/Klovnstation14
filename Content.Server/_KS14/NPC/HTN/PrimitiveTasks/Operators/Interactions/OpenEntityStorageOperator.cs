using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Interaction;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;

/// <summary>
///     Opens the storage at <see cref="TargetKey"/> - a locker, crate, body bag - straight through the storage
///         system: no free hand needed, so an NPC holding a gun can do it, and an open one is left open rather than
///         toggled shut. Succeeds if it is open afterwards; fails if it is out of reach, or welded or locked.
/// </summary>
/// <remarks>
///     Not <c>InteractWithOperator</c>: with something in the active hand that interacts using the held item, which
///         does not open a locker, and with an empty hand it toggles - shutting one that is already open.
/// </remarks>
public sealed partial class OpenEntityStorageOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var storageUid, _entityManager) ||
            !_entityManager.TryGetComponent<EntityStorageComponent>(storageUid, out var storageComponent))
            return HTNOperatorStatus.Failed;

        if (storageComponent.Open)
            return HTNOperatorStatus.Finished;

        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_interactionSystem.InRangeUnobstructed(ownerUid, storageUid))
            return HTNOperatorStatus.Failed;

        return _entityStorageSystem.TryOpenStorage(ownerUid, storageUid, silent: true)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }
}
