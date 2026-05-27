using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     Assumes a field of view starting from the owner
///         and pointed in the direction of the reference coordinates.
///
///     For targets that are in this FOV (given <see cref="Angle"/>),
///         returns 1f. Otherwise, returns 0f.
/// </summary>
public sealed partial class CoordinatesInFOVCon : UtilityConsideration
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    /// <summary>
    ///     Second set of coordinates, compared with owner coordinates, to determine
    ///         the direction of the FOV.
    /// </summary>
    [DataField("toKey", required: true)] public string ReferenceCoordinatesKey = default!;

    /// <summary>
    ///     Permitting angle, in degrees.
    /// </summary>
    [DataField(required: true)] public float Angle;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(ReferenceCoordinatesKey, out var referenceCoordinates, EntityManager))
            return 0f;

        var ownerPosition = _transformSystem.GetWorldPosition(ownerUid);
        var targetPosition = _transformSystem.GetWorldPosition(targetUid);
        var referencePosition = _transformSystem.ToWorldPosition(referenceCoordinates);

        var forward = targetPosition - ownerPosition;
        Vector2Helpers.Normalize(ref forward);

        var toTarget = referencePosition - ownerPosition;
        Vector2Helpers.Normalize(ref toTarget);

        var dot = Vector2.Dot(forward, toTarget);

        var halfFovRad = MathF.PI * (Angle / 2f) / 180f;
        var threshold = MathF.Cos(halfFovRad);

        return dot >= threshold ? 1f : 0f;
    }
}
