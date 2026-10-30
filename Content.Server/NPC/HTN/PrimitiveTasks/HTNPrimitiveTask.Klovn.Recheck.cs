// KS14: added in this fork
namespace Content.Server.NPC.HTN.PrimitiveTasks;

public sealed partial class HTNPrimitiveTask
{
    /// <summary>
    ///     If true, <see cref="Preconditions"/> are checked again on every update while this task runs, not only
    ///         when it is planned, and the task fails the moment one stops holding - the NPC replans straight away.
    ///         For a task that should not carry on once the world has moved on under it: shooting at a target that
    ///         has gone out of sight, say.
    /// </summary>
    /// <remarks>
    ///     Checked against the live blackboard, not the planner's copy: a key an earlier task only wrote as a plan
    ///         effect, with <see cref="ApplyEffectsOnStartup"/> off, is not there, and the task fails at once.
    /// </remarks>
    [DataField]
    public bool RecheckPreconditions;

    /// <summary>
    ///     Whether every precondition still holds. Only meaningful with <see cref="RecheckPreconditions"/>.
    /// </summary>
    public bool PreconditionsStillMet(NPCBlackboard blackboard)
    {
        foreach (var precondition in Preconditions)
        {
            if (!precondition.IsMet(blackboard))
                return false;
        }

        return true;
    }
}
