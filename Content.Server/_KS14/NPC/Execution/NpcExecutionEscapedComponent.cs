namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     On a hostile in soft crit that executioners have tried to execute: how it has gone, so that they stop once it keeps
///         crawling away. Kept on the victim so that the whole squad gives up together. See <see cref="NpcExecutionSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcExecutionSystem))]
public sealed partial class NpcExecutionEscapedComponent : Component
{
    /// <summary>
    ///     Executions of this entity that have come to nothing.
    /// </summary>
    [ViewVariables]
    public int Failures;

    /// <summary>
    ///     When the first execution of it started.
    /// </summary>
    [ViewVariables]
    public TimeSpan FirstAttempt;

    /// <summary>
    ///     Whether executioners have given up on it while it is in soft crit: they shoot it instead.
    /// </summary>
    [ViewVariables]
    public bool GivenUp;
}
