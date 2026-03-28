using Content.Server.Examine;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     Returns 1 if the target key coordinates (if any) are in LOS of origin key coordinates (if any).
///         Otherwise, returns 0.
/// </summary>
public sealed partial class CoordinatesInLOSCon : UtilityConsideration
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly ExamineSystem _examineSystem = default!;

    /// <summary>
    ///     Coordinates that must be visible by the target for this to be valid.
    /// </summary>
    [DataField(required: true)] public string ToKey;
    [DataField(required: true)] public float Radius;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(ToKey, out var toCoordinates, EntityManager))
            return 0f;

        //var radius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);

        // this could use one small optimisation but who cares
        return _examineSystem.InRangeUnOccluded(
            _transformSystem.GetMapCoordinates(targetUid),
            _transformSystem.ToMapCoordinates(toCoordinates),
            Radius + 0.5f,
            null
        ) ? 1f : 0f;
    }
}
