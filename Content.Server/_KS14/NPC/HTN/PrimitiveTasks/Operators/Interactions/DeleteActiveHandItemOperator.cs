using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;

/// <summary>
///     Deletes whatever is in the owner's active hand, e.g. a weapon spawned by <see cref="SpawnInHandOperator"/> that
///         shouldn't be left behind as loot. Finishes with an empty hand too.
/// </summary>
public sealed partial class DeleteActiveHandItemOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private HandsSystem _handsSystem = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_handsSystem.TryGetActiveItem(owner, out var itemUid))
            _entityManager.QueueDeleteEntity(itemUid.Value);

        return HTNOperatorStatus.Finished;
    }
}
