using System.Threading;
using System.Threading.Tasks;
using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets value of specified key to the item in the active hand.
/// </summary>
public sealed partial class GetActiveHeldItemOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;

    [DataField(required: true)] public string Key;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager))
            return (false, null);

        return (true, new Dictionary<string, object>()
        {
            {Key, _handsSystem.GetActiveItem(ownerUid) ?? EntityUid.Invalid}
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_handsSystem.TryGetActiveItem(ownerUid, out var itemUid))
            return HTNOperatorStatus.Failed;

        blackboard.SetValue(Key, itemUid);
        return HTNOperatorStatus.Finished;
    }
}
