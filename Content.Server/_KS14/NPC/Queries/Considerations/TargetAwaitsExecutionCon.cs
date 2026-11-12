using Content.Server._KS14.NPC.Execution;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the target is critical and still to be executed, 0 if it is not, or if executioners have given up on it
///         because it keeps crawling away (see <see cref="NpcExecutionSystem.IsGivenUp"/>): then it is just another
///         hostile to shoot.
/// </summary>
public sealed partial class TargetAwaitsExecutionCon : UtilityConsideration
{
    [Dependency] private NpcExecutionSystem _npcExecutionSystem = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        return _npcExecutionSystem.AwaitsExecution(targetUid) ? 1f : 0f;
    }
}
