// KS14: added in this fork
namespace Content.Server.NPC.Components;

public sealed partial class NPCJukeComponent
{
    /// <summary>
    ///     How long a ranged NPC's sidestep lasts, in seconds, when it is far enough from its target not to back off.
    ///         Null: it stands still to shoot. See <c>JukeOperator.StrafeDuration</c>.
    /// </summary>
    [DataField]
    public float? KsStrafeDuration;

    /// <summary>
    ///     How long it stands still between sidesteps, in seconds.
    /// </summary>
    [DataField]
    public float KsStrafePause = 0.4f;

    /// <summary>
    ///     Which way the current sidestep goes across the line to the target: 1 or -1.
    /// </summary>
    [ViewVariables]
    public int KsStrafeSide = 1;

    /// <summary>
    ///     When the current pause ends and the sidestep after it starts.
    /// </summary>
    [ViewVariables]
    public TimeSpan KsStrafeStartsAt;

    /// <summary>
    ///     When the current sidestep ends.
    /// </summary>
    [ViewVariables]
    public TimeSpan KsStrafeEndsAt;
}
