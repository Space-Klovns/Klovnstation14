using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenConvexSweepTests
{
    [TestCase(1f, 0f, true)]
    [TestCase(1f, 0.3f, false)]
    [TestCase(3f, 0f, false)]
    [TestCase(-1f, 0f, false)]
    [TestCase(2.19f, 0f, true)]
    public void CircleSweepIsAFiniteCapsule(float x, float y, bool expected)
    {
        Assert.That(KsProcgenConvexShapeOverlap.TrySweep(new PhysShapeCircle(0.1f), Transform.Empty,
            new Vector2(2f, 0f), new PhysShapeCircle(0.1f), new Transform(new Vector2(x, y), 0f),
            out var overlaps, out var issue), Is.True, issue);
        Assert.That(overlaps, Is.EqualTo(expected));
    }

    [Test]
    public void ContinuousSweepDetectsObstacleMissedByBothEndpoints()
    {
        var moving = new PhysShapeCircle(0.1f);
        var obstacle = new PhysShapeCircle(0.1f);
        var middle = new Transform(new Vector2(1f, 0f), 0f);
        KsProcgenConvexShapeOverlap.TryCheck(moving, Transform.Empty, obstacle, middle, out var startHit, out _);
        KsProcgenConvexShapeOverlap.TryCheck(moving, new Transform(new Vector2(2f, 0f), 0f), obstacle, middle,
            out var endHit, out _);
        Assert.That(startHit || endHit, Is.False);
        Assert.That(KsProcgenConvexShapeOverlap.TrySweep(moving, Transform.Empty, new Vector2(2f, 0f),
            obstacle, middle, out var sweepHit, out var issue), Is.True, issue);
        Assert.That(sweepHit, Is.True);
    }

    [Test]
    public void PolygonHullAndRotatedCircleOffsetArePreserved()
    {
        var box = new PolygonShape();
        Assert.That(box.Set([new(-0.1f, -0.1f), new(0.1f, -0.1f), new(0.1f, 0.1f), new(-0.1f, 0.1f)], 4), Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TrySweep(box, Transform.Empty, new Vector2(2f, 2f),
            new PhysShapeCircle(0.1f), new Transform(new Vector2(1f, 1f), 0f), out var hit, out var issue), Is.True, issue);
        Assert.That(hit, Is.True);
        KsProcgenConvexShapeOverlap.TrySweep(box, Transform.Empty, new Vector2(2f, 2f), new PhysShapeCircle(0.1f),
            new Transform(new Vector2(0f, 2f), 0f), out hit, out _);
        Assert.That(hit, Is.False, "The swept AABB must not replace the convex hull.");
        var offset = new PhysShapeCircle(0.1f, new Vector2(1f, 0f));
        Assert.That(KsProcgenConvexShapeOverlap.TrySweep(offset, new Transform(Vector2.Zero, Angle.FromDegrees(90.0)),
            new Vector2(2f, 0f), new PhysShapeCircle(0.1f), new Transform(new Vector2(1f, 1f), 0f),
            out hit, out issue), Is.True, issue);
        Assert.That(hit, Is.True);
    }

    [Test]
    public void ZeroLengthSweepMatchesClosedOverlapAndRejectsInvalidShapes()
    {
        var shape = new PhysShapeCircle(0.25f);
        Assert.That(KsProcgenConvexShapeOverlap.TrySweep(shape, Transform.Empty, Vector2.Zero, shape,
            Transform.Empty, out var hit, out var issue), Is.True, issue);
        Assert.That(hit, Is.True);
        Assert.That(KsProcgenConvexShapeOverlap.TrySweep(shape, Transform.Empty, new Vector2(float.NaN, 0f),
            shape, Transform.Empty, out hit, out _), Is.False);
        Assert.That(hit, Is.False);
        Assert.That(KsProcgenConvexShapeOverlap.TrySweep(new EdgeShape(), Transform.Empty, Vector2.One,
            shape, Transform.Empty, out hit, out _), Is.False);
        Assert.That(hit, Is.False);
    }
}
