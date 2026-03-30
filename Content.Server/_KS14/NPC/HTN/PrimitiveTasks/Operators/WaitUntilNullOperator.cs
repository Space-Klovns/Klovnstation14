using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Waits until a key is not in the blackboard.
/// </summary>
public sealed partial class WaitUntilNullOperator : HTNOperator
{
    [DataField(required: true)] public string Key = "ThingToWaitFor";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (blackboard.ContainsKey(Key))
            return HTNOperatorStatus.Continuing;

        return HTNOperatorStatus.Finished;
    }
}
