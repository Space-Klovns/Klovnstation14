using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Orders;

/// <summary>
///     Does nothing, for ever: an order carried out, waiting on the next. Ends through its task's
///         <c>recheckPreconditions</c> - give it an <c>OrderCurrentPrecondition</c> - or when a better plan comes along.
/// </summary>
public sealed partial class WaitForOrderChangeOperator : HTNOperator
{
    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Continuing;
    }
}
