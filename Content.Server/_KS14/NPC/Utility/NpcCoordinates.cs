using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Utility;

/// <summary>
///     The "is it within this range" checks the NPC systems made by hand.
/// </summary>
public static class NpcCoordinates
{
    /// <summary>
    ///     Whether <paramref name="a"/> and <paramref name="b"/> are on the same map and no further apart than
    ///         <paramref name="range"/>, itself included.
    /// </summary>
    public static bool IsWithin(MapCoordinates a, MapCoordinates b, float range)
    {
        return a.MapId == b.MapId && (a.Position - b.Position).LengthSquared() <= range * range;
    }

    /// <summary>
    ///     Whether <paramref name="a"/> and <paramref name="b"/> have a distance between them (see
    ///         <see cref="EntityCoordinates.TryDistance(IEntityManager, SharedTransformSystem, EntityCoordinates, out float)"/>)
    ///         and it is no more than <paramref name="range"/>.
    /// </summary>
    public static bool IsWithin(EntityCoordinates a,
        EntityCoordinates b,
        float range,
        IEntityManager entityManager,
        SharedTransformSystem transformSystem)
    {
        return a.TryDistance(entityManager, transformSystem, b, out var distance) && distance <= range;
    }

    /// <summary>
    ///     Whether <paramref name="targetPosition"/> lies within a cone of <paramref name="angleDegrees"/> (the whole
    ///         width, not the half) from <paramref name="originPosition"/>, pointed at
    ///         <paramref name="referencePosition"/>. The edge of the cone is in.
    /// </summary>
    public static bool IsInCone(Vector2 originPosition, Vector2 targetPosition, Vector2 referencePosition, float angleDegrees)
    {
        var forward = targetPosition - originPosition;
        Vector2Helpers.Normalize(ref forward);

        var toReference = referencePosition - originPosition;
        Vector2Helpers.Normalize(ref toReference);

        var dot = Vector2.Dot(forward, toReference);

        var halfFovRad = MathF.PI * (angleDegrees / 2f) / 180f;
        var threshold = MathF.Cos(halfFovRad);

        return dot >= threshold;
    }
}
