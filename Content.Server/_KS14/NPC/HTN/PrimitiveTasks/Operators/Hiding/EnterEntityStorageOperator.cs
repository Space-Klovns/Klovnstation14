using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Hiding;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Interaction;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Hiding;

/// <summary>
///     Gets into the closet at <see cref="TargetKey"/>, shutting it behind itself, to hide (see <see cref="NpcHidingSystem"/>).
///         Fails if it is out of reach, or turns out not to be one it could get out of again.
/// </summary>
public sealed partial class EnterEntityStorageOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcHidingSystem _npcHidingSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        return (blackboard.TryGetEntity(TargetKey, _entityManager, out var storageUid) && _npcHidingSystem.IsHidingPlace(storageUid), null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetEntity(TargetKey, _entityManager, out var storageUid))
            return HTNOperatorStatus.Failed;

        var ownerUid = blackboard.GetOwner();

        if (!_interactionSystem.InRangeUnobstructed(ownerUid, storageUid))
            return HTNOperatorStatus.Failed;

        return _npcHidingSystem.TryHideIn(ownerUid, storageUid) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
