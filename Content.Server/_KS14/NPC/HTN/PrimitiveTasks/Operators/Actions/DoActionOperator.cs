using System.Threading;
using System.Threading.Tasks;
using Content.Server.Actions;
using Content.Server.NPC;
using Content.Server.NPC.HTN;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Actions;

/// <inheritdoc/>
public sealed partial class DoActionOperator : KsBaseActionOperator
{
    /// <summary>
    ///     If null, there will be no target. Otherwise, this will fail if no valid target is found. Defaults to null.
    /// </summary>
    [DataField] public string? TargetKey = null;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _)
    {
        if (TargetKey == null)
            return (true, null);

        return (blackboard.ContainsKey(TargetKey), null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!TryGetValidAction(blackboard, out var ownerUid, out var actionEntity))
            return HTNOperatorStatus.Failed;

        if (TargetKey != null)
        {
            if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, EntityManager))
                return HTNOperatorStatus.Failed;

            ActionsSystem.SetEventTarget(actionEntity, targetUid);
        }

        ActionsSystem.PerformAction(ownerUid, actionEntity, predicted: false);
        return HTNOperatorStatus.Finished;
    }
}
