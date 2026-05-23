using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     Returns 1 if the random prob is true.
///         Otherwise, returns 0.
/// </summary>
public sealed partial class RandomValueCon : UtilityConsideration
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;

    /// <summary>
    ///     Probability (in percent; 0 - 1) for score to be true.
    /// </summary>
    [DataField(required: true)] public float Probability;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
        => _robustRandom.Prob(Probability) ? 1f : 0f;
}
