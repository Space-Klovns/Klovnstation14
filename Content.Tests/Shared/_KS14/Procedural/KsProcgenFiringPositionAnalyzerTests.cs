using System.Collections.Generic;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenFiringPositionAnalyzerTests
{
    [Test]
    public void ReachableProtectedPositionCanShootDifferentTarget()
    {
        var walkable = new HashSet<Vector2i>
        {
            new(0, 0), new(0, 1), new(0, 2),
        };
        var candidates = new HashSet<Vector2i> { new(0, 0) };
        var result = KsProcgenFiringPositionAnalyzer.Analyze(walkable,
            new HashSet<Vector2i>(), new HashSet<Vector2i> { new(2, 0) },
            candidates, new Vector2i(0, 1), new Vector2i(4, 0), new Vector2i(0, 2),
            radius: 4);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenFiringPositionStatus.Complete));
            Assert.That(result.Samples[0].Reachable, Is.True);
            Assert.That(result.Samples[0].ProtectedFromThreat, Is.True);
            Assert.That(result.Samples[0].ClearShotToTarget, Is.True);
            Assert.That(result.AcceptedCells, Is.EqualTo(new[] { new Vector2i(0, 0) }));
            Assert.That(result.EvaluatedRays, Is.EqualTo(2));
            Assert.That(result.EngineProjectileVerified, Is.False);
            Assert.That(result.EngineTraversalVerified, Is.False);
        });
    }

    [Test]
    public void DirectExposureAndDisconnectedCandidateAreRejected()
    {
        var walkable = new HashSet<Vector2i>
        {
            new(0, 0), new(0, 1), new(0, 2), new(5, 0),
        };
        var candidates = new HashSet<Vector2i> { new(0, 0), new(5, 0) };
        var result = KsProcgenFiringPositionAnalyzer.Analyze(walkable,
            new HashSet<Vector2i>(), new HashSet<Vector2i>(), candidates,
            new Vector2i(0, 1), new Vector2i(4, 0), new Vector2i(0, 2),
            radius: 6);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenFiringPositionStatus.Complete));
            Assert.That(result.Samples[0].Reachable, Is.True);
            Assert.That(result.Samples[0].ProtectedFromThreat, Is.False);
            Assert.That(result.Samples[1].Reachable, Is.False);
            Assert.That(result.AcceptedCells, Is.Empty);
        });
    }

    [Test]
    public void ExpansionAndRayBudgetsReturnNoPartialCandidates()
    {
        var walkable = new HashSet<Vector2i> { new(0, 0), new(0, 1) };
        var candidates = new HashSet<Vector2i> { new(0, 0) };
        var blocked = new HashSet<Vector2i> { new(2, 0) };
        var expansion = KsProcgenFiringPositionAnalyzer.Analyze(walkable,
            new HashSet<Vector2i>(), blocked, candidates,
            new Vector2i(0, 1), new Vector2i(4, 0), new Vector2i(0, 2),
            radius: 4, maxExpandedCells: 1);
        var ray = KsProcgenFiringPositionAnalyzer.Analyze(walkable,
            new HashSet<Vector2i>(), blocked, candidates,
            new Vector2i(0, 1), new Vector2i(4, 0), new Vector2i(0, 2),
            radius: 4, maxRays: 1);

        Assert.Multiple(() =>
        {
            Assert.That(expansion.Status, Is.EqualTo(KsProcgenFiringPositionStatus.BudgetExceeded));
            Assert.That(expansion.Issue?.Code, Is.EqualTo("FiringPositionExpansionBudget"));
            Assert.That(expansion.Samples, Is.Empty);
            Assert.That(ray.Status, Is.EqualTo(KsProcgenFiringPositionStatus.BudgetExceeded));
            Assert.That(ray.Issue?.Code, Is.EqualTo("FiringPositionRayBudget"));
            Assert.That(ray.Samples, Is.Empty);
        });
    }
}
