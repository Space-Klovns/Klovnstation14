using System.Collections.Generic;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenExposureAnalyzerTests
{
    [Test]
    public void OpenSamplesReportPairFractionAndLongestRay()
    {
        var samples = new HashSet<Vector2i>
        {
            new(0, 0), new(1, 0), new(2, 0), new(3, 0),
        };
        var result = KsProcgenExposureAnalyzer.Analyze(samples, new HashSet<Vector2i>(),
            radius: 3);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenExposureStatus.Complete));
            Assert.That(result.InRangePairs, Is.EqualTo(6));
            Assert.That(result.VisiblePairs, Is.EqualTo(6));
            Assert.That(result.VisiblePairFraction, Is.EqualTo(1f));
            Assert.That(result.LongestClearRaySquared, Is.EqualTo(9));
            Assert.That(result.Samples[0].VisiblePeers, Is.EqualTo(3));
            Assert.That(result.EngineVisionVerified, Is.False);
        });
    }

    [Test]
    public void OpaqueTileBlocksStraightAndCornerTouchingRays()
    {
        var samples = new HashSet<Vector2i>
        {
            new(0, 0), new(2, 0), new(0, 2),
        };
        var opaque = new HashSet<Vector2i> { new(1, 0) };
        var result = KsProcgenExposureAnalyzer.Analyze(samples, opaque, radius: 3);
        var clearCorner = KsProcgenExposureAnalyzer.Analyze(
            new HashSet<Vector2i> { new(0, 0), new(2, 2) },
            new HashSet<Vector2i>(), radius: 3);
        var blockedCorner = KsProcgenExposureAnalyzer.Analyze(
            new HashSet<Vector2i> { new(0, 0), new(2, 2) }, opaque,
            radius: 3);

        Assert.Multiple(() =>
        {
            Assert.That(result.InRangePairs, Is.EqualTo(3));
            Assert.That(result.VisiblePairs, Is.EqualTo(1));
            Assert.That(result.Samples[0].VisibleFraction, Is.EqualTo(0.5f));
            Assert.That(result.Samples[1].VisiblePeers, Is.Zero);
            Assert.That(clearCorner.VisiblePairs, Is.EqualTo(1));
            Assert.That(blockedCorner.VisiblePairs, Is.Zero);
        });
    }

    [Test]
    public void PairAndSampleBudgetsReturnNoPartialResult()
    {
        var samples = new HashSet<Vector2i>
        {
            new(0, 0), new(1, 0), new(2, 0),
        };
        var pairBudget = KsProcgenExposureAnalyzer.Analyze(samples,
            new HashSet<Vector2i>(), radius: 3, maxPairs: 2);
        var sampleBudget = KsProcgenExposureAnalyzer.Analyze(samples,
            new HashSet<Vector2i>(), maxSamples: 2);

        Assert.Multiple(() =>
        {
            Assert.That(pairBudget.Status, Is.EqualTo(KsProcgenExposureStatus.BudgetExceeded));
            Assert.That(pairBudget.Issue?.Code, Is.EqualTo("ExposurePairBudget"));
            Assert.That(pairBudget.Samples, Is.Empty);
            Assert.That(sampleBudget.Status, Is.EqualTo(KsProcgenExposureStatus.BudgetExceeded));
            Assert.That(sampleBudget.Issue?.Code, Is.EqualTo("ExposureSampleBudget"));
            Assert.That(sampleBudget.Samples, Is.Empty);
        });
    }

    [Test]
    public void OutOfRangeAndExtremeCoordinatesDoNotOverflow()
    {
        var result = KsProcgenExposureAnalyzer.Analyze(
            new HashSet<Vector2i>
            {
                new(int.MinValue, 0), new(int.MaxValue, 0),
            }, new HashSet<Vector2i>(), radius: 3);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenExposureStatus.Complete));
            Assert.That(result.InRangePairs, Is.Zero);
            Assert.That(result.VisiblePairFraction, Is.Zero);
            Assert.That(result.Samples, Has.Count.EqualTo(2));
        });
    }
}
