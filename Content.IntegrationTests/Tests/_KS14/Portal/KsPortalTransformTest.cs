#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._KS14.Portal;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Portal;

/// <summary>
///     The view portal eye maths, checked against the engine's own view matrices so no sign convention is
///         taken on trust.
/// </summary>
[TestFixture]
public sealed class KsPortalTransformTest
{
    private static readonly Vector2 RenderScale = new Vector2(32f, -32f);

    private static Vector2 ToView(Eye eye, Vector2 point)
    {
        eye.GetViewMatrix(out var viewMatrix, RenderScale);
        return Vector2.Transform(point, viewMatrix);
    }

    private static void AssertSamePixel(
        Vector2 mainCentre,
        Angle mainRotation,
        Vector2 mainOffset,
        Vector2 aPosition,
        Angle aRotation,
        Vector2 bPosition,
        Angle bRotation,
        bool differentMap)
    {
        var mainEye = new Eye
        {
            Position = new MapCoordinates(mainCentre, new MapId(1)),
            Offset = mainOffset,
            Rotation = mainRotation,
            Scale = new Vector2(0.75f, 0.75f),
        };

        KsPortalTransform.GetPortalEye(
            mainCentre + mainOffset, mainRotation, aPosition, aRotation, bPosition, bRotation,
            out var portalCentre, out var portalRotation);

        var portalEye = new Eye
        {
            Position = new MapCoordinates(portalCentre, new MapId(differentMap ? 2 : 1)),
            Rotation = portalRotation,
            Scale = mainEye.Scale,
        };

        var offsets = new[]
        {
            Vector2.Zero, new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1.25f, 0.5f), new Vector2(1.4f, -1.4f),
        };

        foreach (var offset in offsets)
        {
            var point = aPosition + offset;
            var image = KsPortalTransform.MapPoint(point, aPosition, aRotation, bPosition, bRotation);

            var main = ToView(mainEye, point);
            var portal = ToView(portalEye, image);

            Assert.That(portal.X, Is.EqualTo(main.X).Within(1e-3f), $"X for offset {offset}");
            Assert.That(portal.Y, Is.EqualTo(main.Y).Within(1e-3f), $"Y for offset {offset}");
        }
    }

    [Test]
    public void IdentityRotationSameMap()
    {
        AssertSamePixel(new Vector2(3f, 4f), Angle.Zero, Vector2.Zero,
            new Vector2(5f, 5f), Angle.Zero, new Vector2(-20f, 7f), Angle.Zero, differentMap: false);
    }

    [Test]
    public void QuarterTurnRelativeRotation()
    {
        AssertSamePixel(new Vector2(3f, 4f), Angle.Zero, Vector2.Zero,
            new Vector2(5f, 5f), Angle.Zero, new Vector2(-20f, 7f), new Angle(MathF.PI / 2f), differentMap: false);
    }

    [Test]
    public void CrossMapWithRotatedEyeAndOffset()
    {
        AssertSamePixel(new Vector2(-2f, 9f), new Angle(0.6), new Vector2(0.5f, -0.25f),
            new Vector2(1f, 2f), new Angle(1.1), new Vector2(40f, -15f), new Angle(-2.3), differentMap: true);
    }

    [Test]
    public void SegmentIsCentredAndTurnedWithPortal()
    {
        // A segment 4 long, turned a quarter turn, runs along Y instead of X around the same centre.
        KsPortalTransform.GetSegment(new Vector2(2f, 3f), new Angle(MathF.PI / 2f), 4f, out var start, out var end);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((start + end) / 2f, Is.EqualTo(new Vector2(2f, 3f)).Using(Vector2Comparer));
            Assert.That(MathF.Abs(end.X - start.X), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(MathF.Abs(end.Y - start.Y), Is.EqualTo(4f).Within(1e-3f));
        }
    }

    private static readonly IComparer<Vector2> Vector2Comparer = Comparer<Vector2>.Create(
        (first, second) => (first - second).Length() < 1e-3f ? 0 : 1);

    [Test]
    public void PortalFrameHasXAlongDoorwayAndYAcross()
    {
        var local = KsPortalTransform.ToPortalFrame(new Vector2(2f, 5f), new Vector2(2f, 3f), new Angle(MathF.PI / 2f));

        // The portal's local X points along world +Y when turned a quarter turn, so a point 2 up is 2 along it.
        Assert.That(local.X, Is.EqualTo(2f).Within(1e-3f));
        Assert.That(local.Y, Is.EqualTo(0f).Within(1e-3f));
    }

    [TestCase(1f, 0f)]
    [TestCase(0f, -1f)]
    [TestCase(-3f, 4f)]
    public void FacingDoorwayRunsAcrossTheDirectionWithItsNormalAlongIt(float directionX, float directionY)
    {
        var direction = new Vector2(directionX, directionY);
        var rotation = KsPortalTransform.GetFacingRotation(direction);

        // A point further along the direction is straight out on the +Y side, and the doorway runs across it.
        var local = KsPortalTransform.ToPortalFrame(direction * 2f, Vector2.Zero, rotation);
        Assert.That(local.X, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(local.Y, Is.EqualTo(direction.Length() * 2f).Within(1e-3f));

        KsPortalTransform.GetSegment(Vector2.Zero, rotation, 2f, out var start, out var end);
        Assert.That(Vector2.Dot(end - start, direction), Is.EqualTo(0f).Within(1e-3f));
    }

    private static bool InsideTriangles(List<Vector2> triangles, Vector2 point)
    {
        for (var index = 0; index < triangles.Count; index += 3)
        {
            var (a, b, c) = (triangles[index], triangles[index + 1], triangles[index + 2]);
            var d1 = Cross(b - a, point - a);
            var d2 = Cross(c - b, point - b);
            var d3 = Cross(a - c, point - c);
            var hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            var hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            if (!(hasNegative && hasPositive))
                return true;
        }

        return false;
    }

    private static float Cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

    [TestCase(-5f)]
    [TestCase(5f)]
    public void WindowIsSegmentAndShadowFromEitherSide(float viewerY)
    {
        var triangles = new List<Vector2>();
        var shadowSign = MathF.Sign(-viewerY);
        KsPortalTransform.GetSegment(Vector2.Zero, Angle.Zero, 2f, out var start, out var end);

        Assert.That(KsPortalTransform.TryGetVisibleWindow(new Vector2(0f, viewerY), start, end, 100f, triangles), Is.True);
        Assert.That(triangles.Count, Is.EqualTo(KsPortalTransform.WindowVertexCount));

        // Behind the segment as seen from the viewer: seen, wedge widening with distance.
        Assert.That(InsideTriangles(triangles, new Vector2(0f, 0.05f * shadowSign)), "just behind the segment");
        Assert.That(InsideTriangles(triangles, new Vector2(0f, 60f * shadowSign)), "far behind the portal");
        Assert.That(InsideTriangles(triangles, new Vector2(1.9f, 30f * shadowSign)), "wedge widens with distance");
        Assert.That(InsideTriangles(triangles, new Vector2(2.5f, 0.5f * shadowSign)), Is.False, "beside the doorway");
        Assert.That(InsideTriangles(triangles, new Vector2(0f, -2f * shadowSign)), Is.False, "between viewer and portal");
        Assert.That(InsideTriangles(triangles, new Vector2(0f, viewerY * 1.5f)), Is.False, "behind the viewer");
        Assert.That(InsideTriangles(triangles, new Vector2(20f, 40f * shadowSign)), Is.False, "outside the wedge");
    }

    [Test]
    public void WindowSeenFromNearTheDoorwayLineCoversItsWholeHalfOfTheView()
    {
        // Just off the doorway's line and off its centre: the wedge opens to nearly half a turn.
        var triangles = new List<Vector2>();
        KsPortalTransform.GetSegment(Vector2.Zero, Angle.Zero, 2f, out var start, out var end);
        var viewer = new Vector2(0.3f, -0.05f);

        Assert.That(KsPortalTransform.TryGetVisibleWindow(viewer, start, end, 50f, triangles), Is.True);

        using (Assert.EnterMultipleScope())
        {
            // A far edge drawn straight between the two extruded corners runs about 3 tiles above the doorway here.
            Assert.That(InsideTriangles(triangles, new Vector2(10f, 10f)), "well up the view, off to the side");
            Assert.That(InsideTriangles(triangles, new Vector2(0f, 40f)), "straight up, near the edge of the view");
            Assert.That(InsideTriangles(triangles, new Vector2(-30f, 20f)), "up and off to the other side");
            Assert.That(InsideTriangles(triangles, new Vector2(0f, -10f)), Is.False, "the viewer's own half");
        }
    }

    [Test]
    public void ViewerOnTheLineOfTheSegmentHasNoWindow()
    {
        KsPortalTransform.GetSegment(Vector2.Zero, Angle.Zero, 2f, out var start, out var end);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(KsPortalTransform.TryGetVisibleWindow(new Vector2(0.2f, 0f), start, end, 100f, new List<Vector2>()), Is.False, "on the segment");
            Assert.That(KsPortalTransform.TryGetVisibleWindow(new Vector2(7f, 0f), start, end, 100f, new List<Vector2>()), Is.False, "on its extension");
            Assert.That(KsPortalTransform.TryGetVisibleWindow(new Vector2(0.2f, 0.1f), start, end, 100f, new List<Vector2>()), Is.True, "just off the line, either side");
            Assert.That(KsPortalTransform.TryGetVisibleWindow(new Vector2(0.2f, -0.1f), start, end, 100f, new List<Vector2>()), Is.True);
        }
    }

    [Test]
    public void EyeWorldBoundsMatchViewMatrixForRotatedEye()
    {
        var eye = new Eye
        {
            Position = new MapCoordinates(new Vector2(4f, -3f), new MapId(1)),
            Rotation = new Angle(0.7),
            Scale = new Vector2(0.5f, 0.5f),
        };

        var worldSize = new Vector2(20f, 12f);
        var bounds = KsPortalTransform.GetEyeWorldBounds(new Vector2(4f, -3f), eye.Rotation, worldSize);
        bounds.GetCorners(out var bottomLeft, out var bottomRight, out var topRight, out var topLeft);

        // Every corner of the bounds must land on a corner of the screen, which in view space (before the
        //      pixel scale) is half the world size away from the centre on both axes.
        eye.GetViewMatrix(out var viewMatrix, new Vector2(2f, 2f));
        foreach (var corner in new[] { bottomLeft, bottomRight, topRight, topLeft })
        {
            var view = Vector2.Transform(corner, viewMatrix);
            Assert.That(MathF.Abs(view.X), Is.EqualTo(worldSize.X / 2f).Within(1e-3f));
            Assert.That(MathF.Abs(view.Y), Is.EqualTo(worldSize.Y / 2f).Within(1e-3f));
        }
    }

    [TestCase(1f)]
    [TestCase(0.5f)]
    [TestCase(0.37f)]
    [TestCase(0.1f)]
    public void ReducedViewportCoversTheSameWorldArea(float resolutionScale)
    {
        var mainSize = new Vector2i(1280, 720);
        var mainRenderScale = new Vector2(2f, 2f);

        KsPortalTransform.GetReducedViewport(mainSize, mainRenderScale, resolutionScale, out var size, out var renderScale);

        // The engine's own world size of a viewport: size / (renderScale * pixels per meter).
        var mainWorld = (Vector2)mainSize / mainRenderScale;
        var reducedWorld = (Vector2)size / renderScale;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reducedWorld.X, Is.EqualTo(mainWorld.X).Within(1e-3f));
            Assert.That(reducedWorld.Y, Is.EqualTo(mainWorld.Y).Within(1e-3f));
            Assert.That(size.X, Is.EqualTo((int)MathF.Round(1280 * resolutionScale)).Within(1));
        }
    }

    [Test]
    public void ReducedViewportClampsTheScale()
    {
        KsPortalTransform.GetReducedViewport(new Vector2i(100, 100), Vector2.One, 0f, out var tiny, out _);
        KsPortalTransform.GetReducedViewport(new Vector2i(100, 100), Vector2.One, 5f, out var huge, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tiny, Is.EqualTo(new Vector2i(10, 10)));
            Assert.That(huge, Is.EqualTo(new Vector2i(100, 100)));
        }
    }

    [Test]
    public void WindowRegionIsTheViewPartTheTrianglesTouch()
    {
        var centre = new Vector2(10f, 20f);
        var rotation = new Angle(0.6);
        var worldSize = new Vector2(16f, 9f);

        var bounds = KsPortalTransform.GetEyeWorldBounds(centre, rotation, worldSize);

        // A small triangle around a world point that is a known place inside the view.
        var inside = centre + (-rotation).RotateVec(new Vector2(2f, 2f));
        var triangles = new List<Vector2> { inside, inside + new Vector2(0.5f, 0f), inside + new Vector2(0f, 0.5f) };

        Assert.That(KsPortalTransform.TryGetWindowRegion(centre, rotation, worldSize, triangles, out var fullBox, out var region), Is.True);

        var quad = new Box2Rotated(region, -rotation, centre);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(region.Width * region.Height, Is.LessThan(fullBox.Width * fullBox.Height * 0.1f), "only a small part of the view");
            Assert.That(quad.Contains(inside), Is.True, "the quad still holds the triangle's corner");
            Assert.That(bounds.Contains(quad.Center), Is.True);
        }

        foreach (var vertex in triangles)
            Assert.That(quad.Enlarged(0.001f).Contains(vertex), Is.True, $"vertex {vertex}");

        // Wholly outside the view: nothing to draw.
        var outside = new List<Vector2> { centre + new Vector2(500f, 0f), centre + new Vector2(501f, 0f), centre + new Vector2(500f, 1f) };
        Assert.That(KsPortalTransform.TryGetWindowRegion(centre, rotation, worldSize, outside, out _, out _), Is.False);
    }

    [Test]
    public void CaptureUVMapsTheViewCornersToTheTextureCorners()
    {
        var centre = new Vector2(3f, 4f);
        var rotation = new Angle(0.6);
        var worldSize = new Vector2(10f, 6f);

        var triangles = new List<Vector2> { centre, centre + new Vector2(0.5f, 0f), centre + new Vector2(0f, 0.5f) };
        Assert.That(KsPortalTransform.TryGetWindowRegion(centre, rotation, worldSize, triangles, out var fullBox, out _), Is.True);

        // A corner of the view, in its own frame, taken back out into the world the way the view is turned.
        Vector2 WorldCorner(Vector2 corner) => centre + (-rotation).RotateVec(corner - centre);

        using (Assert.EnterMultipleScope())
        {
            // V runs up from the bottom, as the engine's world-space texture draws have it.
            AssertClose(KsPortalTransform.GetCaptureUV(WorldCorner(fullBox.TopLeft), centre, rotation, fullBox), new Vector2(0f, 1f));
            AssertClose(KsPortalTransform.GetCaptureUV(WorldCorner(fullBox.BottomRight), centre, rotation, fullBox), new Vector2(1f, 0f));
            AssertClose(KsPortalTransform.GetCaptureUV(WorldCorner(fullBox.BottomLeft), centre, rotation, fullBox), new Vector2(0f, 0f));
            AssertClose(KsPortalTransform.GetCaptureUV(centre, centre, rotation, fullBox), new Vector2(0.5f, 0.5f));
        }

        static void AssertClose(Vector2 actual, Vector2 expected)
            => Assert.That(Vector2.Distance(actual, expected), Is.LessThan(1e-4f), $"{actual} is not {expected}");
    }

    [Test]
    public void WindowFractionFallsWithDistanceAndWidth()
    {
        var viewer = Vector2.Zero;
        var eyeSize = new Vector2(20f, 12f);

        float Fraction(float distance, float width)
        {
            KsPortalTransform.GetSegment(new Vector2(distance, 0f), new Angle(MathF.PI / 2f), width, out var start, out var end);
            return KsPortalTransform.EstimateWindowFraction(viewer, start, end, viewer, eyeSize);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Fraction(3f, 1f), Is.GreaterThan(Fraction(3f, 0.2f)));
            Assert.That(Fraction(3f, 1f), Is.GreaterThan(Fraction(8f, 1f)));
            Assert.That(Fraction(100f, 1f), Is.EqualTo(0f), "past the edge of the view");
            Assert.That(Fraction(3f, 1f), Is.InRange(0f, 1f));
        }
    }
}
