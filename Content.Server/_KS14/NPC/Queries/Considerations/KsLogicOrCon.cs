using Content.Server.NPC;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     The score of the first of its <see cref="KsLogicCon.Considerations"/> to score above 0, without reading the rest, or
///         0 if none does.
/// </summary>
public sealed partial class KsLogicOrCon : KsLogicCon
{
    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        foreach (var consideration in Considerations)
        {
            var score = GetCurvedScore(blackboard, targetUid, consideration);

            if (score > 0f)
                return score;
        }

        return 0f;
    }
}
