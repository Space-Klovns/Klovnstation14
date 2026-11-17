using Content.Server.NPC;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     The lowest score of its <see cref="KsLogicCon.Considerations"/>, or 0 the moment one scores 0, without reading the
///         rest. Give it an inverse curve for "not both".
/// </summary>
public sealed partial class KsLogicAndCon : KsLogicCon
{
    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        var lowest = 1f;

        foreach (var consideration in Considerations)
        {
            lowest = MathF.Min(lowest, GetCurvedScore(blackboard, targetUid, consideration));

            if (lowest <= 0f)
                return 0f;
        }

        return lowest;
    }
}
