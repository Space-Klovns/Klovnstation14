using System.Collections.Generic;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenCoverAnalyzerTests
{
    [Test]
    public void ProjectileCoverIsIndependentOfVisionAndCountsNearbyPositions()
    {
        var legal = new HashSet<Vector2i>
        {
            new(0, 0), new(1, 0), new(3, 0), new(4, 0),
        };
        var sample = new HashSet<Vector2i> { new(0, 0) };
        var threat = new HashSet<Vector2i> { new(4, 0) };
        var projectileBlocker = new HashSet<Vector2i> { new(2, 0) };
        var cover = KsProcgenCoverAnalyzer.Analyze(legal, sample, projectileBlocker,
            threat, radius: 4);
        var exposure = KsProcgenExposureAnalyzer.Analyze(
            new HashSet<Vector2i> { new(0, 0), new(4, 0) },
            new HashSet<Vector2i>(), radius: 4);

        Assert.Multiple(() =>
        {
            Assert.That(cover.Status, Is.EqualTo(KsProcgenCoverStatus.Complete));
            Assert.That(cover.Samples[0].Protected, Is.True);
            Assert.That(cover.Samples[0].BlockedThreatRays, Is.EqualTo(1));
            Assert.That(cover.Samples[0].NearbyLegalPositions, Is.EqualTo(1));
            Assert.That(cover.Samples[0].NearbyProtectedPositions, Is.EqualTo(1));
            Assert.That(cover.ProtectedSamples, Is.EqualTo(1));
            Assert.That(cover.EngineProjectileVerified, Is.False);
            Assert.That(exposure.VisiblePairs, Is.EqualTo(1));
        });
    }

    [Test]
    public void ClearAndCornerTouchingShotsHaveDifferentProtection()
    {
        var legal = new HashSet<Vector2i> { new(0, 0) };
        var sample = new HashSet<Vector2i> { new(0, 0) };
        var threat = new HashSet<Vector2i> { new(2, 2) };
        var clear = KsProcgenCoverAnalyzer.Analyze(legal, sample,
            new HashSet<Vector2i>(), threat, radius: 3);
        var blocked = KsProcgenCoverAnalyzer.Analyze(legal, sample,
            new HashSet<Vector2i> { new(1, 0) }, threat, radius: 3);

        Assert.Multiple(() =>
        {
            Assert.That(clear.Samples[0].Protected, Is.False);
            Assert.That(clear.Samples[0].ClearThreatRays, Is.EqualTo(1));
            Assert.That(blocked.Samples[0].Protected, Is.True);
            Assert.That(blocked.Samples[0].BlockedThreatRays, Is.EqualTo(1));
        });
    }

    [Test]
    public void OutOfRangeThreatDoesNotCountAsCover()
    {
        var legal = new HashSet<Vector2i> { new(0, 0) };
        var result = KsProcgenCoverAnalyzer.Analyze(legal, legal,
            new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(10, 0) }, radius: 3);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenCoverStatus.Complete));
            Assert.That(result.EvaluatedRays, Is.Zero);
            Assert.That(result.Samples[0].ThreatsInRange, Is.Zero);
            Assert.That(result.Samples[0].Protected, Is.False);
        });
    }

    [Test]
    public void RayBudgetAndInvalidSampleReturnNoPartialMetrics()
    {
        var legal = new HashSet<Vector2i> { new(0, 0), new(1, 0) };
        var samples = new HashSet<Vector2i> { new(0, 0), new(1, 0) };
        var threat = new HashSet<Vector2i> { new(3, 0) };
        var budget = KsProcgenCoverAnalyzer.Analyze(legal, samples,
            new HashSet<Vector2i>(), threat, radius: 3, maxRays: 1);
        var invalid = KsProcgenCoverAnalyzer.Analyze(legal, samples,
            new HashSet<Vector2i> { new(0, 0) }, threat);

        Assert.Multiple(() =>
        {
            Assert.That(budget.Status, Is.EqualTo(KsProcgenCoverStatus.BudgetExceeded));
            Assert.That(budget.Issue?.Code, Is.EqualTo("CoverRayBudget"));
            Assert.That(budget.Samples, Is.Empty);
            Assert.That(invalid.Status, Is.EqualTo(KsProcgenCoverStatus.InvalidInput));
            Assert.That(invalid.Samples, Is.Empty);
        });
    }
}
