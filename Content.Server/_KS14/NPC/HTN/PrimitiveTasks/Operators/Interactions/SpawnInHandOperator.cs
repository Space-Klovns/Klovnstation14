using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;

/// <summary>
///     Spawns an item straight into one of the owner's free hands. Fails, and deletes the item, if no hand takes it.
/// </summary>
public sealed partial class SpawnInHandOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private HandsSystem _handsSystem = default!;

    [DataField(required: true)]
    public EntProtoId Proto;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var itemUid = _entityManager.SpawnNextToOrDrop(Proto, owner);

        if (_handsSystem.TryPickupAnyHand(owner, itemUid, checkActionBlocker: false, animate: false))
            return HTNOperatorStatus.Finished;

        _entityManager.DeleteEntity(itemUid);
        return HTNOperatorStatus.Failed;
    }
}
