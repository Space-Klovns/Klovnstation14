using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Server.NPC.Systems;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     A consideration made of others, for a decision that is really several: see <see cref="KsLogicAndCon"/> and
///         <see cref="KsLogicOrCon"/>. They are read in order, each through its own curve, and only as far as it takes
///         to know the answer, so put the cheapest first, and the one most likely to settle it.
/// </summary>
public abstract partial class KsLogicCon : UtilityConsideration
{
    [Dependency] private NPCUtilitySystem _npcUtilitySystem = default!;

    [DataField(required: true)]
    public List<UtilityConsideration> Considerations = new();

    public override void Initialise(IDependencyCollection dependencyCollection)
    {
        base.Initialise(dependencyCollection);

        foreach (var consideration in Considerations)
        {
            consideration.Initialise(dependencyCollection);
        }
    }

    /// <summary>
    ///     What <paramref name="consideration"/> scores, through its own curve.
    /// </summary>
    protected float GetCurvedScore(NPCBlackboard blackboard, EntityUid targetUid, UtilityConsideration consideration)
    {
        return _npcUtilitySystem.GetScore(consideration.Curve, _npcUtilitySystem.GetScore(blackboard, targetUid, consideration));
    }
}
