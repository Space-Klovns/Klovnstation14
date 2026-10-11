using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Portal;

/// <summary>
///     The geometry of a view portal: how a point near one portal maps onto the portal it is linked to, and
///         the eye that has to be used to render that other side so it lands pixel for pixel in the window.
/// </summary>
/// <remarks>
///     Everything here is the engine's convention: <see cref="Angle"/> is counter-clockwise, and an eye with
///         rotation theta maps a world point P to the view-space vector Rot(+theta) * (P - eyeCentre), which
///         is what <c>Eye.GetViewMatrix</c> computes. The tests pin this against the engine's own matrices.
/// </remarks>
public static class KsPortalTransform
{
    /// <summary>
    ///     How far the destination portal is turned relative to the source one.
    /// </summary>
    public static Angle GetRelativeRotation(Angle sourceRotation, Angle destinationRotation)
    {
        return destinationRotation - sourceRotation;
    }

    /// <summary>
    ///     Where a point at the source portal's side appears if the destination's surroundings are placed at
    ///         the source: the destination plus the offset from the source, turned by the relative rotation.
    /// </summary>
    public static Vector2 MapPoint(
        Vector2 point,
        Vector2 sourcePosition,
        Angle sourceRotation,
        Vector2 destinationPosition,
        Angle destinationRotation)
    {
        var relativeRotation = GetRelativeRotation(sourceRotation, destinationRotation);
        return destinationPosition + relativeRotation.RotateVec(point - sourcePosition);
    }

    /// <summary>
    ///     The centre and rotation of an eye on the destination's map which, with the same scale as
    ///         the main eye, puts <see cref="MapPoint"/> of every world point on the pixel the main eye
    ///         puts that point on.
    /// </summary>
    /// <param name="mainEyeCentre">The main eye's position plus its offset.</param>
    /// <param name="mainEyeRotation">The main eye's rotation.</param>
    public static void GetPortalEye(
        Vector2 mainEyeCentre,
        Angle mainEyeRotation,
        Vector2 sourcePosition,
        Angle sourceRotation,
        Vector2 destinationPosition,
        Angle destinationRotation,
        out Vector2 eyeCentre,
        out Angle eyeRotation)
    {
        // The eye centre is simply the image of the main eye centre. The rotation has to cancel the relative
        //      rotation the mapping applies to the world, hence minus (see the class remarks for the sign).
        eyeCentre = MapPoint(mainEyeCentre, sourcePosition, sourceRotation, destinationPosition, destinationRotation);
        eyeRotation = mainEyeRotation - GetRelativeRotation(sourceRotation, destinationRotation);
    }

    /// <summary>
    ///     The doorway segment of a portal in world space: <paramref name="width"/> long, centred on the portal
    ///         and running along its local X axis.
    /// </summary>
    public static void GetSegment(
        Vector2 portalPosition,
        Angle portalRotation,
        float width,
        out Vector2 start,
        out Vector2 end)
    {
        var halfAxis = portalRotation.RotateVec(new Vector2(width * 0.5f, 0f));
        start = portalPosition - halfAxis;
        end = portalPosition + halfAxis;
    }

    /// <summary>
    ///     A point in the portal's own frame: X along the doorway, Y across it (its normal).
    /// </summary>
    public static Vector2 ToPortalFrame(Vector2 worldPoint, Vector2 portalPosition, Angle portalRotation)
    {
        return (-portalRotation).RotateVec(worldPoint - portalPosition);
    }

    /// <summary>
    ///     The world area an eye covers. The box is turned by minus the eye's rotation, because the eye turns
    ///         the world by plus its rotation to get view space (the engine's own bounds do the same).
    /// </summary>
    public static Box2Rotated GetEyeWorldBounds(Vector2 eyeCentre, Angle eyeRotation, Vector2 worldSize)
    {
        return new Box2Rotated(Box2.CenteredAround(eyeCentre, worldSize), -eyeRotation, eyeCentre);
    }

    /// <summary>
    ///     The smallest and largest resolution scale a portal view is rendered at, relative to the main view.
    /// </summary>
    public const float MinimumResolutionScale = 0.1f;

    /// <summary>
    ///     Size and render scale of a portal view that covers exactly the world area the main view covers, with
    ///         <paramref name="resolutionScale"/> times as many pixels along each axis.
    /// </summary>
    /// <remarks>
    ///     The world area of a viewport is its size divided by its render scale, so scaling only the size would
    ///         drift by the rounding of the size. The render scale is derived from the rounded size instead.
    /// </remarks>
    public static void GetReducedViewport(
        Vector2i mainSize,
        Vector2 mainRenderScale,
        float resolutionScale,
        out Vector2i size,
        out Vector2 renderScale)
    {
        var clamped = Math.Clamp(resolutionScale, MinimumResolutionScale, 1f);

        size = new Vector2i(
            Math.Max(1, (int)MathF.Round(mainSize.X * clamped)),
            Math.Max(1, (int)MathF.Round(mainSize.Y * clamped)));

        renderScale = mainRenderScale * new Vector2((float)size.X / mainSize.X, (float)size.Y / mainSize.Y);
    }

    /// <summary>
    ///     The part of an eye's view that a window's triangles touch, in the view's own (unrotated) frame, so a
    ///         capture only has to be drawn over that much.
    /// </summary>
    /// <param name="fullBox">The whole view in its own frame, centred on the eye.</param>
    /// <param name="region"><paramref name="fullBox"/> clipped to the triangles' bounding box.</param>
    /// <returns>False when the triangles do not touch the view at all.</returns>
    public static bool TryGetWindowRegion(
        Vector2 eyeCentre,
        Angle eyeRotation,
        Vector2 worldSize,
        List<Vector2> triangles,
        out Box2 fullBox,
        out Box2 region)
    {
        fullBox = Box2.CenteredAround(eyeCentre, worldSize);
        region = default;

        if (triangles.Count == 0)
            return false;

        // The view is the box turned by minus the eye's rotation about its centre, so a world point is
        //      taken into the box's frame by turning it by plus the eye's rotation.
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);

        foreach (var vertex in triangles)
        {
            var local = eyeCentre + eyeRotation.RotateVec(vertex - eyeCentre);
            minimum = Vector2.Min(minimum, local);
            maximum = Vector2.Max(maximum, local);
        }

        var left = MathF.Max(minimum.X, fullBox.Left);
        var bottom = MathF.Max(minimum.Y, fullBox.Bottom);
        var right = MathF.Min(maximum.X, fullBox.Right);
        var top = MathF.Min(maximum.Y, fullBox.Top);

        if (left >= right || bottom >= top)
            return false;

        region = new Box2(left, bottom, right, top);
        return true;
    }

    /// <summary>
    ///     The texture pixels, from the texture's top left, that <paramref name="region"/> of
    ///         <paramref name="fullBox"/> corresponds to when the texture is stretched over the whole box.
    /// </summary>
    public static UIBox2 GetTextureSubRegion(Box2 fullBox, Box2 region, Vector2 textureSize)
    {
        return new UIBox2(
            (region.Left - fullBox.Left) / fullBox.Width * textureSize.X,
            (fullBox.Top - region.Top) / fullBox.Height * textureSize.Y,
            (region.Right - fullBox.Left) / fullBox.Width * textureSize.X,
            (fullBox.Top - region.Bottom) / fullBox.Height * textureSize.Y);
    }

    /// <summary>
    ///     A cheap estimate of how much of the view, from 0 to 1, a doorway's window takes up: the sector the
    ///         doorway subtends from the viewer, out to the far edge of the view.
    /// </summary>
    public static float EstimateWindowFraction(Vector2 viewer, Vector2 segmentStart, Vector2 segmentEnd, Vector2 eyeCentre, Vector2 worldSize)
    {
        var first = segmentStart - viewer;
        var second = segmentEnd - viewer;
        var firstLength = first.Length();
        var secondLength = second.Length();
        if (firstLength <= float.Epsilon || secondLength <= float.Epsilon)
            return 1f;

        var cosine = Math.Clamp(Vector2.Dot(first, second) / (firstLength * secondLength), -1f, 1f);
        var angle = MathF.Acos(cosine);

        var nearDistance = ((segmentStart + segmentEnd) * 0.5f - viewer).Length();
        var farDistance = worldSize.Length() * 0.5f + (viewer - eyeCentre).Length();
        var area = 0.5f * angle * MathF.Max(0f, farDistance * farDistance - nearDistance * nearDistance);

        return Math.Clamp(area / MathF.Max(worldSize.X * worldSize.Y, float.Epsilon), 0f, 1f);
    }

    /// <summary>
    ///     Vertices written by <see cref="TryGetVisibleWindow"/>: the shadow quad of the doorway, as two triangles.
    /// </summary>
    public const int WindowVertexCount = 6;

    /// <summary>
    ///     A viewer closer than this to the doorway's line has no usable shadow: the wedge has no width.
    /// </summary>
    public const float MinimumViewerDistance = 0.001f;

    /// <summary>
    ///     The region through which the destination is visible: the doorway segment plus its shadow as an
    ///         occluder would cast it from the viewer, extruded away from the viewer. A viewer on either side
    ///         of the doorway sees through it, onto the other side.
    /// </summary>
    /// <remarks>
    ///     The segment and its two extruded ends form one quad, which covers exactly the segment and the shadow
    ///         volume behind it, while everything between the viewer and the segment stays outside.
    /// </remarks>
    /// <param name="extrusion">How far past the segment the shadow runs; must exceed the view distance.</param>
    /// <param name="triangles">Cleared and filled with <see cref="WindowVertexCount"/> triangle-list vertices.</param>
    /// <returns>False, with nothing written, when the viewer is (nearly) on the line of the segment.</returns>
    public static bool TryGetVisibleWindow(Vector2 viewer, Vector2 segmentStart, Vector2 segmentEnd, float extrusion, List<Vector2> triangles)
    {
        triangles.Clear();

        var axis = segmentEnd - segmentStart;
        var length = axis.Length();
        if (length <= float.Epsilon)
            return false;

        var distanceToLine = MathF.Abs(axis.X * (viewer.Y - segmentStart.Y) - axis.Y * (viewer.X - segmentStart.X)) / length;
        if (distanceToLine < MinimumViewerDistance)
            return false;

        var extrudedStart = Extrude(segmentStart, viewer, extrusion);
        var extrudedEnd = Extrude(segmentEnd, viewer, extrusion);

        AddQuad(triangles, segmentStart, segmentEnd, extrudedEnd, extrudedStart);

        return true;
    }

    private static Vector2 Extrude(Vector2 corner, Vector2 viewer, float extrusion)
    {
        return corner + Vector2.Normalize(corner - viewer) * extrusion;
    }

    private static void AddQuad(List<Vector2> triangles, Vector2 first, Vector2 second, Vector2 third, Vector2 fourth)
    {
        triangles.Add(first);
        triangles.Add(second);
        triangles.Add(third);
        triangles.Add(first);
        triangles.Add(third);
        triangles.Add(fourth);
    }
}
