// KS14: added in this fork
using Content.Server.NPC.Components;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

public sealed partial class JukeOperator
{
    /// <summary>
    ///     If set, a ranged NPC that is far enough from its target not to back off sidesteps across its line of fire
    ///         for this many seconds at a time, pausing <see cref="StrafePause"/> between steps, rather than standing
    ///         still to trade shots. It never steps where it would lose sight of the target. Only for
    ///         <see cref="JukeType.Away"/>.
    /// </summary>
    [DataField]
    public float? StrafeDuration;

    /// <summary>
    ///     How long to stand between sidesteps, in seconds. See <see cref="StrafeDuration"/>.
    /// </summary>
    [DataField]
    public float StrafePause = 0.4f;

    private void KsApplyStrafe(NPCJukeComponent jukeComponent)
    {
        jukeComponent.KsStrafeDuration = StrafeDuration;
        jukeComponent.KsStrafePause = StrafePause;
    }
}
