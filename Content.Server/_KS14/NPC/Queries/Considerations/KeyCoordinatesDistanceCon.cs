using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     Scores entities based on their distance from
///         the given coordinates key.
/// </summary>
public sealed partial class KeyCoordinatesDistanceCon : UtilityConsideration
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    /// <summary>
    ///     Key of the coordinates to get distance from.
    /// </summary>
    [DataField(required: true)] public string Key = "TargetCoordinates";

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(Key, out var coordinates, EntityManager) ||
            !EntityManager.TransformQuery.TryGetComponent(targetUid, out var targetTransform))
            return 0f;

        if (!coordinates.TryDistance(EntityManager, _transformSystem, targetTransform.Coordinates, out var distance))
            return 0f;

        var radius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
        return Math.Clamp(distance / radius, 0f, 1f);
    }
}
