using System;
using Content.Shared._KS14.Light;
using Content.Shared._KS14.ZLevel.Light;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.ZLevel;

[TestFixture]
public sealed class KsZLevelLightLeakTests
{
    private const float Radius = 7f;
    private const float Energy = 0.8f;
    private const float Falloff = 6.8f;
    private const float CurveFactor = 0f;

    private static float Attenuate(float distance, float radius) =>
        KsLightAttenuation.Attenuate(distance, radius, Falloff, CurveFactor);

    /// <summary>
    ///     How bright the real light is on a floor that far below it, at the point directly underneath.
    /// </summary>
    private static float ArrivingBelow(float verticalDistance) =>
        Energy * Attenuate(verticalDistance + KsLightAttenuation.LightingHeight, Radius);

    [Test]
    public void Attenuate_IsFullOnlyWhereTheLightIs()
    {
        Assert.That(Attenuate(0f, Radius), Is.EqualTo(1f).Within(0.0001f),
            "nothing attenuates a light at zero distance from itself");

        Assert.That(Attenuate(Radius, Radius), Is.EqualTo(0f).Within(0.0001f),
            "a light must be fully out by the time it reaches its own radius");

        Assert.That(Attenuate(Radius * 2f, Radius), Is.EqualTo(0f).Within(0.0001f),
            "and stay out past it, rather than coming back through the curve");
    }

    [Test]
    public void Attenuate_FallsOffWithDistance()
    {
        var previous = float.MaxValue;

        for (var distance = 0f; distance <= Radius; distance += 0.25f)
        {
            var current = Attenuate(distance, Radius);

            Assert.That(current, Is.LessThanOrEqualTo(previous),
                $"light got brighter going from just under {distance} tiles away to {distance}");

            previous = current;
        }
    }

    /// <summary>
    ///     The floor a light stands on is already <see cref="KsLightAttenuation.LightingHeight"/> below it, so
    ///         a leak of no depth at all has to come back as the light itself. Anything else means the solve
    ///         and the shader disagree about where a light sits.
    /// </summary>
    [Test]
    public void TryGetLeakedLight_IsIdentityAtNoDepth()
    {
        Assert.That(
            KsZLevelLightLeak.TryGetLeakedLight(
                Radius, Energy, Falloff, CurveFactor, 0f, out var leakedRadius, out var leakedEnergy),
            Is.True,
            "a light always reaches the floor it is standing on");

        Assert.Multiple(() =>
        {
            Assert.That(leakedRadius, Is.EqualTo(Radius).Within(0.0001f),
                "a leak of no depth should cover exactly what the light already covers");
            Assert.That(leakedEnergy, Is.EqualTo(Energy).Within(0.0001f),
                "and should be exactly as bright as the light already is");
        });
    }

    /// <summary>
    ///     The two things a viewer actually reads off a leak: how far it spreads, and how bright it is under
    ///         the light. Both are matched exactly; what is between them is an approximation.
    /// </summary>
    [Test]
    public void TryGetLeakedLight_MatchesReachAndBrightnessBelow()
    {
        const float verticalDistance = 2f;
        var height = verticalDistance + KsLightAttenuation.LightingHeight;

        Assert.That(
            KsZLevelLightLeak.TryGetLeakedLight(
                Radius, Energy, Falloff, CurveFactor, verticalDistance, out var leakedRadius, out var leakedEnergy),
            Is.True,
            $"a radius of {Radius} should still reach {verticalDistance} tiles down");

        // Where the real light runs out across the lower floor, measured in three dimensions...
        var trueReach = MathF.Sqrt(Radius * Radius - height * height);

        // ...and where the stand-in runs out across the floor it is standing on.
        var standInReach = MathF.Sqrt(leakedRadius * leakedRadius - KsLightAttenuation.LightingHeightSquared);

        Assert.Multiple(() =>
        {
            Assert.That(standInReach, Is.EqualTo(trueReach).Within(0.0001f),
                "the stand-in should stop spreading exactly where the real light stops arriving");

            Assert.That(leakedEnergy * Attenuate(KsLightAttenuation.LightingHeight, leakedRadius),
                Is.EqualTo(ArrivingBelow(verticalDistance)).Within(0.0001f),
                "and be exactly as bright, underneath the light, as what arrives there from above");

            Assert.That(leakedEnergy, Is.LessThan(Energy),
                "light that has fallen two tiles cannot arrive brighter than it set out");
        });
    }

    [Test]
    public void TryGetLeakedLight_DimsWithDepth()
    {
        var previousEnergy = float.MaxValue;
        var previousRadius = float.MaxValue;

        for (var verticalDistance = 0f; verticalDistance < Radius; verticalDistance += 0.5f)
        {
            if (!KsZLevelLightLeak.TryGetLeakedLight(
                    Radius, Energy, Falloff, CurveFactor, verticalDistance, out var radius, out var energy))
                break;

            Assert.Multiple(() =>
            {
                Assert.That(energy, Is.LessThanOrEqualTo(previousEnergy),
                    $"a leak {verticalDistance} tiles down came out brighter than the one above it");
                Assert.That(radius, Is.LessThanOrEqualTo(previousRadius),
                    $"a leak {verticalDistance} tiles down came out wider than the one above it");
            });

            previousEnergy = energy;
            previousRadius = radius;
        }
    }

    /// <summary>
    ///     Spend the whole radius dropping and there is nothing left to spread with, which is the cutoff that
    ///         stops a stack lighting itself all the way down.
    /// </summary>
    [Test]
    public void TryGetLeakedLight_RefusesWhatCannotReach()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                KsZLevelLightLeak.TryGetLeakedLight(
                    Radius, Energy, Falloff, CurveFactor, Radius, out _, out _),
                Is.False,
                "a drop of the light's whole radius leaves nothing of it to arrive");

            Assert.That(
                KsZLevelLightLeak.TryGetLeakedLight(
                    Radius, Energy, Falloff, CurveFactor, Radius * 10f, out _, out _),
                Is.False,
                "and neither does a drop far past it");

            // Just inside the cutoff the reach is tiny, and the solve there is a ratio of two vanishing
            //      numbers - it has to come back refused rather than with an enormous energy.
            Assert.That(
                KsZLevelLightLeak.TryGetLeakedLight(
                    Radius,
                    Energy,
                    Falloff,
                    CurveFactor,
                    Radius - KsLightAttenuation.LightingHeight - 0.0001f,
                    out _,
                    out var marginalEnergy),
                Is.False,
                "a leak with no meaningful spread left should be refused, not solved");

            Assert.That(marginalEnergy, Is.Zero,
                "a refused leak should not hand back an energy at all");
        });
    }

    /// <summary>
    ///     A gap directly under a light passes all of it, which is what makes the plain depth solve the right
    ///         answer on its own for a light shining straight down - and keeps every other test here honest.
    /// </summary>
    [Test]
    public void GetHoleFactor_IsOneDirectlyUnderTheLight()
    {
        Assert.That(KsZLevelLightLeak.GetHoleFactor(0f, Radius, Falloff, CurveFactor),
            Is.EqualTo(1f).Within(0.0001f),
            "a gap underfoot should pass exactly what reaches the floor underfoot");
    }

    [Test]
    public void GetHoleFactor_FallsOffAcrossTheFloor()
    {
        var previous = float.MaxValue;

        for (var distance = 0f; distance <= Radius; distance += 0.5f)
        {
            var factor = KsZLevelLightLeak.GetHoleFactor(distance, Radius, Falloff, CurveFactor);

            Assert.Multiple(() =>
            {
                Assert.That(factor, Is.LessThanOrEqualTo(previous),
                    $"a gap {distance} tiles away passed more of the light than a nearer one");
                Assert.That(factor, Is.InRange(0f, 1f),
                    $"a gap {distance} tiles away cannot pass more light than the light has");
            });

            previous = factor;
        }
    }

    [Test]
    public void GetHoleFactor_IsNothingBeyondTheLightsReach()
    {
        Assert.That(KsZLevelLightLeak.GetHoleFactor(Radius * 2f, Radius, Falloff, CurveFactor),
            Is.Zero,
            "a gap outside the light's radius has no light arriving at it to pass on");
    }

    [Test]
    public void GetHoleFactor_RefusesALightWithNoRadius()
    {
        Assert.That(KsZLevelLightLeak.GetHoleFactor(0f, 0f, Falloff, CurveFactor),
            Is.Zero,
            "a light with no radius has nothing to pass through anything");
    }

    [Test]
    public void TryGetLeakedLight_RefusesALightWithNoRadius()
    {
        Assert.That(
            KsZLevelLightLeak.TryGetLeakedLight(0f, Energy, Falloff, CurveFactor, 0f, out _, out _),
            Is.False,
            "a light with no radius has nothing to leak");
    }
}
