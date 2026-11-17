// KS14: added in this fork
using System.Numerics;
using Content.Shared._KS14.Occlusion;

namespace Content.Shared.Examine;

public abstract partial class ExamineSystemShared
{
    /// <summary>
    ///     How far past an occluder's bounds a hit may lie and still be believed. Far above float error.
    /// </summary>
    private const float KsOccluderHitSlack = 0.01f;

    /// <summary>
    ///     Whether the occluder tree reported this occluder as on the ray from <paramref name="start"/> to
    ///         <paramref name="end"/> only through its ray test going wrong: a ray lying exactly along a tile edge is
    ///         reported as hitting every occluder on that line, behind its start and past its end included. See
    ///         <see cref="KsSegmentBounds"/>.
    /// </summary>
    private bool KsIsUnreachableOccluderHit(OccluderComponent occluder, TransformComponent xform, Vector2 start, Vector2 end)
    {
        var (worldPosition, worldRotation) = _transform.GetWorldPositionRotation(xform);
        var worldBounds = new Box2Rotated(occluder.LocalBounds.Translated(worldPosition), worldRotation, worldPosition)
            .CalcBoundingBox()
            .Enlarged(KsOccluderHitSlack);

        return !KsSegmentBounds.Touches(start, end, worldBounds);
    }
}
