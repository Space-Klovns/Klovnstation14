// KS14: added in this fork
namespace Content.Server.NPC.HTN;

public abstract partial class HTNTask
{
    /// <summary>
    ///     If true, this task failing to plan (a primitive's preconditions or <c>Plan</c> failing, or a compound
    ///         having no satisfied branch) skips the task instead of failing the branch that contains it.
    ///         A skipped task adds nothing to the plan and no entry to the branch traversal record.
    /// </summary>
    /// <remarks>
    ///     Replaces the old "wrapper compound with a fallback <c>NoOperator</c> branch" idiom for optional steps.
    /// </remarks>
    [DataField]
    public bool Optional;
}
