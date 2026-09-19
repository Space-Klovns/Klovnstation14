using System;
using System.Numerics;
using Content.Shared._KS14.ZLevel.Audio;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.ZLevel;

[TestFixture]
public sealed class KsZLevelAudioLeakTests
{
    private const float VerticalDistance = 2f;

    private static readonly Vector2 Listener = Vector2.Zero;

    [Test]
    public void GetPathLength_IsJustTheDropWhenEverythingLinesUp()
    {
        // Sound directly over the gap, gap directly over the listener: nothing but the drop is left.
        var pathLength = KsZLevelAudioLeak.GetPathLength(Listener, Listener, Listener, VerticalDistance);

        Assert.That(pathLength, Is.EqualTo(VerticalDistance).Within(0.0001f),
            "a sound straight overhead has only the height between them to travel");
    }

    [Test]
    public void GetPathLength_CountsBothLegsOfTheBend()
    {
        var hole = new Vector2(3f, 0f);
        var source = new Vector2(7f, 0f);

        // 4 along the upper floor to the gap, then the hypotenuse of 3 across and 2 down.
        var expected = 4f + MathF.Sqrt(3f * 3f + VerticalDistance * VerticalDistance);

        Assert.That(KsZLevelAudioLeak.GetPathLength(source, hole, Listener, VerticalDistance),
            Is.EqualTo(expected).Within(0.0001f),
            "the path is the run to the gap plus the run down from it, not the straight line between the two");
    }

    /// <summary>
    ///     A sound that has to bend further to escape has to sound further away, or a gap on the far side of
    ///         the room would be as loud as one underfoot.
    /// </summary>
    [Test]
    public void GetPathLength_GrowsWithHowFarTheSoundHasToBend()
    {
        var source = new Vector2(5f, 0f);
        var previous = 0f;

        for (var holeX = 5f; holeX >= -5f; holeX -= 1f)
        {
            var pathLength = KsZLevelAudioLeak.GetPathLength(source, new Vector2(holeX, 0f), Listener, VerticalDistance);

            Assert.That(pathLength, Is.GreaterThanOrEqualTo(previous),
                $"a gap at {holeX} should not be a shorter path than the one before it");

            previous = pathLength;
        }
    }

    /// <summary>
    ///     The whole point of the aperture: what a listener hears is the gap's direction, not the sound's.
    /// </summary>
    [Test]
    public void GetApparentPosition_PointsAtTheGapRatherThanTheSound()
    {
        // Sound to the east, the only way down is to the west.
        var source = new Vector2(4f, 0f);
        var hole = new Vector2(-3f, 0f);

        var apparent = KsZLevelAudioLeak.GetApparentPosition(source, hole, Listener, VerticalDistance);

        Assert.That(apparent.X, Is.LessThan(0f),
            "the sound should arrive from the side the gap is on, not the side it was made on");
    }

    [Test]
    public void GetApparentPosition_SitsAtTheFullPathDistance()
    {
        var source = new Vector2(7f, 1f);
        var hole = new Vector2(2f, -1f);

        var apparent = KsZLevelAudioLeak.GetApparentPosition(source, hole, Listener, VerticalDistance);
        var pathLength = KsZLevelAudioLeak.GetPathLength(source, hole, Listener, VerticalDistance);

        Assert.Multiple(() =>
        {
            Assert.That((apparent - Listener).Length(), Is.EqualTo(pathLength).Within(0.0001f),
                "the distance handed to the mixer has to be the whole bent path, or the bend costs nothing");

            Assert.That((apparent - Listener).Length(), Is.GreaterThan((hole - Listener).Length()),
                "and so must be further off than the gap itself, which is only the last leg of it");
        });
    }

    /// <summary>
    ///     A gap directly overhead has no horizontal direction at all, which nothing in a flat mix can express.
    ///     The distance still has to come out right.
    /// </summary>
    [Test]
    public void GetApparentPosition_StillCarriesDistanceWithNoDirectionToGiveIt()
    {
        var source = new Vector2(5f, 0f);

        var apparent = KsZLevelAudioLeak.GetApparentPosition(source, Listener, Listener, VerticalDistance);
        var pathLength = KsZLevelAudioLeak.GetPathLength(source, Listener, Listener, VerticalDistance);

        Assert.Multiple(() =>
        {
            Assert.That((apparent - Listener).Length(), Is.EqualTo(pathLength).Within(0.0001f),
                "a gap straight overhead should still be as far off as the sound had to travel");

            Assert.That(apparent.X, Is.GreaterThan(0f),
                "and with no gap direction to use, should fall back to pointing at the sound itself");
        });
    }

    [Test]
    public void GetApparentPosition_HandlesEverythingBeingInTheSamePlace()
    {
        var apparent = KsZLevelAudioLeak.GetApparentPosition(Listener, Listener, Listener, VerticalDistance);

        Assert.That((apparent - Listener).Length(), Is.EqualTo(VerticalDistance).Within(0.0001f),
            "with no direction available anywhere, the drop still has to be worth its own distance");
    }
}
