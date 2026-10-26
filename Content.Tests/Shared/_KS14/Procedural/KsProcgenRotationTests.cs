#nullable enable
using System;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenRotationTests
{
    [TestCase(0, 2f, -1f)] [TestCase(1, -1f, -2f)]
    [TestCase(2, -2f, 1f)] [TestCase(3, 1f, 2f)]
    public void NativeVectorRotationMatchesClockwiseAuthoringConvention(int turn, float x, float y)
    {
        var actual = KsProcgenRotation.ToEngineAngle(turn).RotateVec(new Vector2(2f, -1f));
        Assert.That(actual.X, Is.EqualTo(x).Within(0.00001f)); Assert.That(actual.Y, Is.EqualTo(y).Within(0.00001f));
    }

    [TestCase(-1)] [TestCase(4)]
    public void InvalidAuthorTurnsCannotSilentlyBecomeAnEnginePose(int turn) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => KsProcgenRotation.ToEngineAngle(turn));
}
