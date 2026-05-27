using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Handles queued sensor events on the owner.
/// </summary>
public sealed partial class HandleSensorsOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly EntityQuery<NpcSensorsComponent> _sensorsQuery = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager) ||
            !_sensorsQuery.TryGetComponent(ownerUid, out var sensorsComponent))
            return (true, null);

        return (true, sensorsComponent.AggregatedEffects)!;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager) ||
            !_sensorsQuery.TryGetComponent(ownerUid, out var sensorsComponent))
            return HTNOperatorStatus.Finished;

        sensorsComponent.AggregatedEffects.Clear();
        return HTNOperatorStatus.Finished;
    }
}
