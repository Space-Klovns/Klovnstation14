#nullable enable
using System.Numerics;
using Content.Shared._KS14.LaserSight;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.LaserSight;

[TestFixture]
public sealed class KsLaserSightMathTest
{
    private static readonly Vector2[] Targets =
    [
        new(0f, -1f), new(0f, 1f), new(1f, 0f), new(-1f, 0f), new(3f, 4f), new(-2f, 5f), new(-7f, -1f), new(0.5f, -6f),
    ];

    /// <summary>
    ///     A gun turned with <c>ToWorldAngle</c> (as the server aims) must have a beam that points at the target, and
    ///         the drawn quad must lie along the same direction, not behind the gun.
    /// </summary>
    [Test]
    public void BeamPointsAlongAimedDirection()
    {
        foreach (var target in Targets)
        {
            var expected = Vector2.Normalize(target);
            var rotation = target.ToWorldAngle();

            var direction = KsLaserSightMath.GetDirection(rotation, Angle.Zero);
            Assert.That(Vector2.Distance(direction, expected), Is.LessThan(0.001f), $"direction for {target}");

            var origin = new Vector2(10f, -3f);
            var box = KsLaserSightMath.GetBeamBox(origin, rotation, 5f, 0.1f);
            var drawn = Vector2.Normalize(box.Center - origin);
            Assert.That(Vector2.Distance(drawn, expected), Is.LessThan(0.001f), $"drawn quad for {target}");
        }
    }

    [Test]
    public void PositiveAngleOffsetTurnsLikeEntityRotation()
    {
        var turned = KsLaserSightMath.GetDirection(Angle.Zero, Angle.FromDegrees(90));
        var rotated = Angle.FromDegrees(90).ToWorldVec();
        Assert.That(Vector2.Distance(turned, rotated), Is.LessThan(0.001f));
    }

    /// <summary>
    ///     The fade has a constant length, (1 - FadeStart) * MaxLength, and always ends at the end of the drawn beam:
    ///         at MaxLength for a free beam, at the hit point for a cut one.
    /// </summary>
    [Test]
    public void FadeHasConstantLengthAndEndsAtTheHitPoint()
    {
        // Free beam: unchanged, solid to 6 of 12 and zero at 12.
        Assert.That(KsLaserSightMath.GetFadeLength(12f, 0.5f), Is.EqualTo(6f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetFadeSplit(12f, 12f, 0.5f), Is.EqualTo(6f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(6f, 12f, 12f, 0.5f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(9f, 12f, 12f, 0.5f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(12f, 12f, 12f, 0.5f), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(12f, 12f, 12f, 1f), Is.EqualTo(1f).Within(0.0001f), "1 means no fade");

        // Cut at 9: the same 6 m fade, starting at 3, alpha 0 at the hit.
        Assert.That(KsLaserSightMath.GetFadeSplit(9f, 12f, 0.5f), Is.EqualTo(3f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(3f, 9f, 12f, 0.5f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(6f, 9f, 12f, 0.5f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(9f, 9f, 12f, 0.5f), Is.EqualTo(0f).Within(0.0001f));

        // Cut closer than the fade length: the fade starts at the origin and still ends at the hit.
        Assert.That(KsLaserSightMath.GetFadeSplit(4f, 12f, 0.5f), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(2f, 4f, 12f, 0.5f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(KsLaserSightMath.GetAlphaAt(4f, 4f, 12f, 0.5f), Is.EqualTo(0f).Within(0.0001f));

        var origin = new Vector2(2f, 3f);
        var rotation = new Vector2(3f, 4f).ToWorldAngle();
        var direction = Vector2.Normalize(new Vector2(3f, 4f));
        var positions = new Vector2[KsLaserSightMath.FadeStripVertexCount];
        var alphas = new float[KsLaserSightMath.FadeStripVertexCount];

        KsLaserSightMath.GetFadeStrip(origin, rotation, 9f, 0.2f, 12f, 0.5f, positions, alphas);

        var splitCentre = (positions[2] + positions[3]) / 2f;
        var endCentre = (positions[4] + positions[5]) / 2f;
        Assert.That(Vector2.Distance(splitCentre, origin + direction * 3f), Is.LessThan(0.001f));
        Assert.That(Vector2.Distance(endCentre, origin + direction * 9f), Is.LessThan(0.001f));
        Assert.That(Vector2.Distance(positions[0], positions[1]), Is.EqualTo(0.2f).Within(0.001f));
        Assert.That(alphas, Is.EqualTo(new[] { 1f, 1f, 1f, 1f, 0f, 0f }).Within(0.0001f));

        // Cut at 4: strip is a gradient from the origin to the hit.
        KsLaserSightMath.GetFadeStrip(origin, rotation, 4f, 0.2f, 12f, 0.5f, positions, alphas);
        Assert.That(Vector2.Distance((positions[2] + positions[3]) / 2f, origin), Is.LessThan(0.001f));
        Assert.That(alphas, Is.EqualTo(new[] { 1f, 1f, 1f, 1f, 0f, 0f }).Within(0.0001f));
    }
}
