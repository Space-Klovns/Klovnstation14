using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Standing;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.PlayDead;

/// <summary>
///     Lies the owner down, keeping hold of its weapon, and keeps it there; gets it up again when the task ends. Never
///         finishes on its own: give the task <c>recheckPreconditions: true</c> with a <c>PlayingDeadPrecondition</c>,
///         so it ends - and the owner gets up - the moment it should stop.
/// </summary>
public sealed partial class PlayDeadOperator : HTNOperator
{
    [Dependency] private StandingStateSystem _standingStateSystem = default!;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        // The weapon stays in hand: the whole point is getting up shooting.
        _standingStateSystem.Down(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), playSound: true, dropHeldItems: false);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);

        // One that has died for real meanwhile stays down: standing refuses the dead.
        _standingStateSystem.Stand(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
