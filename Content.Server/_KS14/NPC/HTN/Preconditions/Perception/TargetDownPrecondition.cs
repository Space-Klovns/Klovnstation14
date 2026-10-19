using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Perception;

/// <summary>
///     Met when the owner has just confirmed a hostile its squad believed alive is dead - seen the body, seen it die,
///         or killed it - and has not said so yet (see <see cref="NpcPerceptionSystem"/>). Pair with
///         <c>AcknowledgeTargetDownOperator</c>, which clears it.
/// </summary>
public sealed partial class TargetDownPrecondition : HTNPrecondition
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    /// <summary>
    ///     A death older than this goes unsaid: the moment has passed.
    /// </summary>
    [DataField]
    public TimeSpan Within = TimeSpan.FromSeconds(5);

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _npcPerceptionSystem.HasPendingKillCallout(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), Within);
    }
}
