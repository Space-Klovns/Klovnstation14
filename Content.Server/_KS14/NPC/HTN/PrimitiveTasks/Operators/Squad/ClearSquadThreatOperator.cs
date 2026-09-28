using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;

/// <summary>
///     Forgets the owner's squad's threat, for when the fight is over. A later threat at the same spot then counts
///         as new rather than as a repeat of the old one.
/// </summary>
public sealed partial class ClearSquadThreatOperator : HTNOperator
{
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        _npcSquadSystem.ClearThreat(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        return HTNOperatorStatus.Finished;
    }
}
