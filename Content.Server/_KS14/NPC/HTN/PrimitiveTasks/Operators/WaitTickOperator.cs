using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Waits one whole tick. Useful for waiting for things to be queuedel'd after an operator.
/// </summary>
public sealed partial class WaitTickOperator : HTNOperator
{
    [DataField] public string TickPassedKey = "WaitedTickPassed";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (blackboard.ContainsKey(TickPassedKey))
        {
            blackboard.Remove<bool>(TickPassedKey);
            return HTNOperatorStatus.Finished;
        }

        // a bool is the smallest value i could think of so whatever
        blackboard.SetValue(TickPassedKey, true);
        return HTNOperatorStatus.Continuing;
    }
}
