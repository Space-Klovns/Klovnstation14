using Content.Server._KS14.NPC.Grenades;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Grenades;

/// <summary>
///     The owner has got to where it was going in on (see <see cref="NpcGrenadeSystem"/>).
/// </summary>
public sealed partial class EndGrenadePushOperator : HTNOperator
{
    [Dependency] private NpcGrenadeSystem _npcGrenadeSystem = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        _npcGrenadeSystem.StopPush(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        return HTNOperatorStatus.Finished;
    }
}
