using Content.Server._KS14.NPC.Hiding;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Hiding;

/// <summary>
///     Stays in the closet it got into until <see cref="NpcHidingSystem"/> lets it out. Ending the task does not get it out:
///         coming out is the hiding system's call alone, made on its own update whatever the NPC is planning. A plan cutting
///         this one short used to pull it out of the closet - a retreat did - so the operative root keeps it here in a
///         branch of its own, right below being stunned (healing.yml's KsOperativeInClosetCompound).
/// </summary>
public sealed partial class HideOperator : HTNOperator
{
    [Dependency] private NpcHidingSystem _npcHidingSystem = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _npcHidingSystem.IsHiding(blackboard.GetOwner()) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }
}
