using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     How far the owner believes a hostile to be, as a fraction of <see cref="MaxRange"/>, or 1 for one it knows nothing
///         of. For hostiles out of sight: unlike <c>TargetDistanceCon</c>, this goes by where it thinks they are, not
///         where they really are.
/// </summary>
public sealed partial class ContactDistanceCon : UtilityConsideration
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    [DataField(required: true)]
    public float MaxRange;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_npcPerceptionSystem.TryGetBelievedCoordinates(ownerUid, targetUid, out var believedCoordinates, out _, out _) ||
            !EntityManager.TransformQuery.GetComponent(ownerUid).Coordinates.TryDistance(EntityManager, _transformSystem, believedCoordinates, out var distance))
            return 1f;

        return Math.Clamp(distance / MaxRange, 0f, 1f);
    }
}
