using System;
using System.Numerics;
using Content.Client._KS14.Storage.ClosetDoor;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._KS14.Storage;

/// <summary>
///     <see cref="KsClosetDoorSystem.GetDoorTransform(float, float, float)"/> against tgstation's
///         <c>get_door_transform</c>, worked out point by point the way BYOND applies its matrix.
/// </summary>
[TestOf(typeof(KsClosetDoorSystem))]
public sealed class KsClosetDoorTransformTest
{
    // tgstation's /obj/structure/closet, in metres.
    private const float HingeX = -6.5f / 32f;
    private const float Squish = 0.2f;

    /// <summary>
    ///     The corners of a 32x48 door sprite centred on the origin, and its middle.
    /// </summary>
    private static readonly Vector2[] Points =
    [
        new(-0.5f, -0.75f), new(0.5f, -0.75f), new(-0.5f, 0.75f), new(0.5f, 0.75f), Vector2.Zero,
    ];

    /// <summary>
    ///     Where tgstation puts <paramref name="point"/>: <c>Translate(-hinge, 0)</c>, then
    ///         <c>matrix(cos, 0, 0, -sin * squish, 1, 0)</c>, which maps <c>x' = ax + by + c</c> and
    ///         <c>y' = dx + ey + f</c>, then <c>Translate(hinge, 0)</c>.
    /// </summary>
    private static Vector2 TgTransform(Vector2 point, float angleDegrees)
    {
        var angle = angleDegrees * MathF.PI / 180f;
        var x = point.X - HingeX;
        var y = point.Y;

        var swungX = MathF.Cos(angle) * x;
        var swungY = -MathF.Sin(angle) * Squish * x + y;

        return new Vector2(swungX + HingeX, swungY);
    }

    [Test]
    [TestCase(0f)]
    [TestCase(30f)]
    [TestCase(60f)]
    [TestCase(90f)]
    [TestCase(123f)]
    [TestCase(140f)]
    public void TestMatchesTgstation(float angleDegrees)
    {
        var transform = KsClosetDoorSystem.GetDoorTransform(angleDegrees, HingeX, Squish);

        foreach (var point in Points)
        {
            var expected = TgTransform(point, angleDegrees);
            var actual = Vector2.Transform(point, transform);
            Assert.That(Vector2.Distance(actual, expected), Is.LessThan(1e-5f),
                $"{point} at {angleDegrees} degrees went to {actual}, not {expected}");
        }
    }

    [Test]
    public void TestShutDoorIsUntouched()
    {
        Assert.That(KsClosetDoorSystem.GetDoorTransform(0f, HingeX, Squish), Is.EqualTo(Matrix3x2.Identity));
    }

    /// <summary>
    ///     Every point on the hinge stays where it is, at any angle: the door turns about it.
    /// </summary>
    [Test]
    public void TestHingeStaysPut()
    {
        for (var angleDegrees = 0f; angleDegrees <= 180f; angleDegrees += 15f)
        {
            var transform = KsClosetDoorSystem.GetDoorTransform(angleDegrees, HingeX, Squish);
            foreach (var y in new[] { -0.75f, 0f, 0.75f })
            {
                var hinge = new Vector2(HingeX, y);
                Assert.That(Vector2.Distance(Vector2.Transform(hinge, transform), hinge), Is.LessThan(1e-5f),
                    $"the hinge moved at {angleDegrees} degrees");
            }
        }
    }

    /// <summary>
    ///     Swung past 90 degrees, the door is mirrored onto the other side of its hinge, and its far edge has dropped,
    ///         as tgstation draws an open door hanging out to the side.
    /// </summary>
    [Test]
    public void TestOpenDoorHangsOutPastTheHinge()
    {
        var transform = KsClosetDoorSystem.GetDoorTransform(140f, HingeX, Squish);
        var farEdge = Vector2.Transform(new Vector2(0.5f, 0f), transform);

        Assert.That(farEdge.X, Is.LessThan(HingeX));
        Assert.That(farEdge.Y, Is.LessThan(0f));
    }
}
