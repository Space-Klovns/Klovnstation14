using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     Scores a hostile by what the owner believes about it (see <see cref="NpcPerceptionSystem"/>): the score listed
///         for its state in <see cref="Scores"/>, or 0 for a state not listed or a hostile it knows nothing of.
/// </summary>
public sealed partial class ContactStateCon : UtilityConsideration
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [DataField(required: true)]
    public Dictionary<NpcContactState, float> Scores = new();

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_npcPerceptionSystem.TryGetContact(ownerUid, targetUid, out var contact))
            return 0f;

        return Scores.GetValueOrDefault(contact.State);
    }
}
