using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenConvexShapeOverlapTests
{
    [TestCase(0.9f, 0.0f, true)]
    [TestCase(1.0f, 0.0f, true)]
    [TestCase(0.9f, 0.9f, false)]
    public void CircleBoundsDoNotReplaceShapeOverlap(float x, float y, bool expected)
    {
        var circle = new PhysShapeCircle(0.5f);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(circle, Transform.Empty, circle,
            new Transform(new Vector2(x, y), 0f), out var overlaps, out var issue), Is.True, issue);
        Assert.That(overlaps, Is.EqualTo(expected));
    }

    [Test]
    public void RotationAppliesToAuthoredCircleOffsets()
    {
        var offset = new PhysShapeCircle(0.1f, new Vector2(2f, 0f));
        var center = new PhysShapeCircle(0.1f);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(offset, new Transform(Vector2.Zero, Angle.FromDegrees(90.0)),
            center, new Transform(new Vector2(0f, 2f), 0f), out var overlaps, out var issue), Is.True, issue);
        Assert.That(overlaps, Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(offset, Transform.Empty, center,
            new Transform(new Vector2(0f, 2f), 0f), out overlaps, out issue), Is.True, issue);
        Assert.That(overlaps, Is.False);
    }

    [Test]
    public void ConvexShapesHandleContainmentCrossingAndSeparatedCorners()
    {
        var triangle = new PolygonShape();
        Assert.That(triangle.Set([new(-1f, -1f), new(1f, -1f), new(-1f, 1f)], 3), Is.True);
        var circle = new PhysShapeCircle(0.1f);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(triangle, Transform.Empty, circle,
            new Transform(new Vector2(0.8f, 0.8f), 0f), out var overlaps, out var issue), Is.True, issue);
        Assert.That(overlaps, Is.False, "Intersecting AABBs do not fill a triangle's absent corner.");
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(triangle, Transform.Empty, circle,
            new Transform(new Vector2(-0.5f, -0.5f), 0f), out overlaps, out issue), Is.True, issue);
        Assert.That(overlaps, Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(triangle, Transform.Empty, triangle,
            new Transform(Vector2.Zero, Angle.FromDegrees(180.0)), out overlaps, out issue), Is.True, issue);
        Assert.That(overlaps, Is.True);
    }

    [Test]
    public void LegacyBoxVerticesFollowWorldRotation()
    {
        var box = new PhysShapeAabb();
        var point = new PhysShapeCircle(0f);
        var corner = new Transform(new Vector2(0.45f, 0.45f), 0f);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(box, Transform.Empty, point, corner,
            out var overlaps, out var issue), Is.True, issue);
        Assert.That(overlaps, Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(box, new Transform(Vector2.Zero, Angle.FromDegrees(45.0)),
            point, corner, out overlaps, out issue), Is.True, issue);
        Assert.That(overlaps, Is.False, "The square's axis-aligned corner is outside its rotated diamond.");
    }

    [Test]
    public void RoundedPolygonCornersUseEuclideanSkinDistance()
    {
        var polygon = new PolygonShape();
        Assert.That(polygon.Set([new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)], 4), Is.True);
        var point = new PhysShapeCircle(0f);
        var skin = polygon.Radius;
        Assert.That(skin, Is.GreaterThan(0f));
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(polygon, Transform.Empty, point,
            new Transform(new Vector2(1f + skin * 0.5f, 1f + skin * 0.5f), 0f), out var overlaps, out var issue), Is.True, issue);
        Assert.That(overlaps, Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(polygon, Transform.Empty, point,
            new Transform(new Vector2(1f + skin * 0.9f, 1f + skin * 0.9f), 0f), out overlaps, out issue), Is.True, issue);
        Assert.That(overlaps, Is.False, "Axis inflation cannot fill a rounded corner outside its skin radius.");
    }

    [Test]
    public void RotatedThinPolygonsCanCrossWithoutContainingVertices()
    {
        var rectangle = new PolygonShape();
        Assert.That(rectangle.Set([new(-3f, -0.1f), new(3f, -0.1f), new(3f, 0.1f), new(-3f, 0.1f)], 4), Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(rectangle, Transform.Empty, rectangle,
            new Transform(Vector2.Zero, Angle.FromDegrees(90.0)), out var overlaps, out var issue), Is.True, issue);
        Assert.That(overlaps, Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(rectangle, Transform.Empty, rectangle,
            new Transform(new Vector2(5f, 0f), Angle.FromDegrees(90.0)), out overlaps, out issue), Is.True, issue);
        Assert.That(overlaps, Is.False);
    }

    [Test]
    public void UnsupportedAndNonfiniteShapesNeverReturnOverlap()
    {
        var circle = new PhysShapeCircle(0.5f);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(new EdgeShape(), Transform.Empty, circle,
            Transform.Empty, out var overlaps, out var issue), Is.False);
        Assert.That(overlaps, Is.False);
        Assert.That(issue, Is.EqualTo("UnsupportedSurfaceContactShape"));
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(circle, new Transform(new Vector2(float.NaN, 0f), 0f),
            circle, Transform.Empty, out overlaps, out issue), Is.False);
        Assert.That(overlaps, Is.False);
        Assert.That(KsProcgenConvexShapeOverlap.TryCheck(new PhysShapeCircle(float.NaN), Transform.Empty,
            circle, Transform.Empty, out overlaps, out issue), Is.False);
        Assert.That(overlaps, Is.False);
        Assert.That(issue, Is.EqualTo("InvalidSurfaceContactShape"));
    }
}
