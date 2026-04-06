using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     Scores entities based on their distance from
///         the given entity key.
/// </summary>
public sealed partial class KeyEntityDistanceCon : UtilityConsideration
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    /// <summary>
    ///     Key of the entity to get distance from.
    /// </summary>
    [DataField(required: true)] public string Key = "Target";

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!blackboard.TryGetValue<EntityUid>(Key, out var keyUid, EntityManager))
            return 0f;

        if (!EntityManager.TransformQuery.TryGetComponent(keyUid, out var keyTransform) ||
            !EntityManager.TransformQuery.TryGetComponent(targetUid, out var targetTransform))
        {
            return 0f;
        }

        if (!keyTransform.Coordinates.TryDistance(EntityManager, _transformSystem, targetTransform.Coordinates,
            out var distance))
        {
            return 0f;
        }

        var radius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
        return Math.Clamp(distance / radius, 0f, 1f);
    }
}
