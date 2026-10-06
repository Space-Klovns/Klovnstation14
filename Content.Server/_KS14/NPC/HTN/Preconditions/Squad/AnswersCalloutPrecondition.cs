using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Squad;

/// <summary>
///     Met when a squadmate is calling out a hostile the owner cannot see - a <see cref="NpcContactState.Reported"/>
///         contact no more than <see cref="MaxAge"/> old - and the owner is one to go and help: it does that at all
///         (<see cref="NpcSquadMemberComponent.RespondsToCallouts"/>), and the hostile is within its
///         <see cref="NpcSquadMemberComponent.CalloutResponseRange"/>. An NPC outside any squad goes by the defaults.
/// </summary>
public sealed partial class AnswersCalloutPrecondition : HTNPrecondition
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private EntityQuery<NpcSquadMemberComponent> _squadMemberQuery = default!;

    private static readonly List<NpcContactState> ReportedStates = [NpcContactState.Reported];

    /// <summary>
    ///     How fresh the callout must be.
    /// </summary>
    [DataField]
    public TimeSpan? MaxAge;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var responds = true;
        var range = 0f;
        if (_squadMemberQuery.TryComp(ownerUid, out var squadMemberComponent))
        {
            responds = squadMemberComponent.RespondsToCallouts;
            range = squadMemberComponent.CalloutResponseRange;
        }

        return responds && _npcPerceptionSystem.HasContact(ownerUid, ReportedStates, MaxAge, maxDistance: range);
    }
}
