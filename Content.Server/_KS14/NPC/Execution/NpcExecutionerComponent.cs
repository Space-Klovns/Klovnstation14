namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     Marks an NPC that, with nothing left to fight, walks up to any hostile left down and executes it, rather than
///         shooting it from where it stands. See <see cref="NpcExecutionSystem"/>.
/// </summary>
/// <remarks>
///     A hostile in soft crit can crawl, and moving breaks an execution off. So an execution that comes to nothing is
///         counted against the victim (<see cref="NpcExecutionEscapedComponent"/>), and once it has slipped away
///         <see cref="MaxAttempts"/> times, or <see cref="GiveUpAfter"/> has gone by since the first try, the executioner
///         gives up on walking over and shoots it like any other hostile.
/// </remarks>
[RegisterComponent]
public sealed partial class NpcExecutionerComponent : Component
{
    /// <summary>
    ///     How many executions of a hostile in soft crit may come to nothing before it is shot instead.
    /// </summary>
    [DataField]
    public int MaxAttempts = 2;

    /// <summary>
    ///     How long after the first execution of a hostile in soft crit was tried to keep trying, however few attempts
    ///         that was.
    /// </summary>
    [DataField]
    public TimeSpan GiveUpAfter = TimeSpan.FromSeconds(10);
}
