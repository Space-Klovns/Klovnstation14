using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;

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
        => (blackboard.ContainsKey(TargetCoordinatesKey, EntityManager), null); // entity-aware, as Update's read is

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetCoordinates(TargetCoordinatesKey, EntityManager, out var targetCoordinates) ||
            !TryPerformWorldAction(blackboard, targetCoordinates, throwOnWrongEvent: true))
            return HTNOperatorStatus.Failed;

        return HTNOperatorStatus.Finished;
    }
}
