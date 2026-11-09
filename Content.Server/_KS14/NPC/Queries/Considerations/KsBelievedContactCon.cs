using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     A consideration of the spot the owner believes a hostile to be at (see
///         <see cref="NpcPerceptionSystem.TryGetBelievedCoordinates"/>), whether or not the hostile is really there.
///         A hostile it knows nothing of scores <see cref="UnknownScore"/>.
/// </summary>
public abstract partial class KsBelievedContactCon : UtilityConsideration
{
    [Dependency] protected NpcPerceptionSystem NpcPerceptionSystem = default!;

    /// <summary>
    ///     What a hostile the owner has no belief about scores.
    /// </summary>
    protected virtual float UnknownScore => 0f;

    public sealed override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!NpcPerceptionSystem.TryGetBelievedCoordinates(ownerUid, targetUid, out var believedCoordinates, out _, out _))
            return UnknownScore;

        return GetBelievedScore(ownerUid, believedCoordinates);
    }

    /// <summary>
    ///     The score for a hostile believed to be at <paramref name="believedCoordinates"/>.
    /// </summary>
    protected abstract float GetBelievedScore(EntityUid ownerUid, EntityCoordinates believedCoordinates);
}
