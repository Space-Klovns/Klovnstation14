using System.Threading;
using System.Threading.Tasks;
using Content.Server.Actions;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared.Actions;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Actions;

/// <inheritdoc/>
/// <remarks>Fails if there is no target/target is invalid.</remarks>
public sealed partial class DoWorldActionOperator : KsBaseActionOperator
{
    /// <summary>
    ///     Key of coordinates to target. If no value can be found, this will fail.
    /// </summary>
    [DataField(required: true)] public string TargetCoordinatesKey;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _)
        => (blackboard.ContainsKey(TargetCoordinatesKey), null);

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out var targetCoordinates, EntityManager) ||
            !TryGetValidAction(blackboard, out var ownerUid, out var actionEntity) ||
            ActionsSystem.GetEvent(actionEntity) is not { } actionEvent)
            return HTNOperatorStatus.Failed;

        if (actionEvent is not WorldTargetActionEvent worldTargetActionEvent)
            throw new InvalidOperationException($"Expected event of type {actionEvent.GetType()} on entity {EntityManager.ToPrettyString(actionEntity)} on owner {EntityManager.ToPrettyString(ownerUid)} to be of type {nameof(WorldTargetActionEvent)}");

        worldTargetActionEvent.Target = targetCoordinates;
        ActionsSystem.PerformAction(ownerUid, actionEntity, actionEvent: actionEvent, predicted: false);

        return HTNOperatorStatus.Finished;
    }
}
