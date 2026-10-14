using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenOwnedDiagnosticReportTests
{
    [Test]
    public void RejectionRetainsWorkAndUnverifiedEngineConstraintsWithoutArtifacts()
    {
        var result = new KsProcgenOwnedInteriorResult
        {
            Status = KsProcgenOwnedPlanStatus.BudgetExceeded, Issue = new("TestBudget", "Bound exhausted"),
            ExpandedCells = 7, FurnishingProbes = 3, PartitionSeamProbes = 2,
            Stages = new[] { new KsProcgenOwnedStageOutcome(KsProcgenOwnedPlanStage.Partition, null, false, "TestBudget", 7, 3) },
        };
        var report = KsProcgenOwnedDiagnosticReportBuilder.Build(Request(), result);
        Assert.That(report.Status, Is.EqualTo("BudgetExceeded"));
        Assert.That(report.ReasonCode, Is.EqualTo("TestBudget"));
        Assert.That(report.Work.ExpandedCells, Is.EqualTo(7));
        Assert.That(report.Work.FurnishingProbes, Is.EqualTo(3));
        Assert.That(report.FloorCells, Is.Empty);
        Assert.That(report.Rooms, Is.Empty);
        Assert.That(report.Entities, Is.Empty);
        Assert.That(report.Constraints.Single(constraint => constraint.Id == "EngineCleanAccess").State,
            Is.EqualTo(KsProcgenConstraintState.Unverified));
        Assert.That(report.Published || report.EngineAccessVerified || report.GasClosureVerified ||
            report.WorkingLightingVerified || report.GraphWorkFullyMetered || report.SearchComplete, Is.False);
    }

    [Test]
    public void MetadataAndGeometryTruncationAreIndependentAndDoNotHideTotals()
    {
        var request = Request();
        request.Shape.Cells.AddRange(new[] { new Vector2i(1, 0), new(2, 0) });
        var result = new KsProcgenOwnedInteriorResult { Status = KsProcgenOwnedPlanStatus.Rejected, ExpandedCells = 77 };
        var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result, maximumEntries: 1, maximumGeometryCells: 2);
        Assert.That(report.EntriesTruncated, Is.True);
        Assert.That(report.GeometryTruncated, Is.True);
        Assert.That(report.Constraints.Count, Is.EqualTo(1));
        Assert.That(report.RequestedCells.Count, Is.EqualTo(2));
        Assert.That(report.Work.ExpandedCells, Is.EqualTo(77));
    }

    [Test]
    public void ForgedIncompleteCandidateIsNotReportedAsAProposal()
    {
        var report = KsProcgenOwnedDiagnosticReportBuilder.Build(Request(),
            new KsProcgenOwnedInteriorResult { Status = KsProcgenOwnedPlanStatus.Candidate, SemanticHash = 21 });
        Assert.That(report.Status, Is.EqualTo("InvalidArtifact"));
        Assert.That(report.ReasonCode, Is.EqualTo("IncompleteOwnedDiagnosticArtifact"));
        Assert.That(report.SemanticHash, Is.EqualTo("0"));
        Assert.That(report.FloorCells, Is.Empty);
    }

    [Test]
    public void RequestAndNestedFallbackListsAreDetached()
    {
        var request = Request();
        var fallbacks = new List<KsProcgenOwnedFallback> { new("MergedPartition", "Room") };
        var result = new KsProcgenOwnedInteriorResult
        {
            Status = KsProcgenOwnedPlanStatus.Rejected,
            Attempts = new[] { new KsProcgenOwnedConfigurationAttempt(0, "Config", KsProcgenOwnedPlanStatus.Rejected,
                "Miss", 7, 0, 0, 0, 0, fallbacks) },
        };
        var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result);
        request.Shape.Cells.Add(new(1, 0));
        fallbacks.Clear();
        Assert.That(report.RequestedCells.Count, Is.EqualTo(1));
        Assert.That(report.Attempts.Single().Fallbacks.Count, Is.EqualTo(1));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(65537, 1)]
    public void InvalidReportLimitsReject(int entries, int cells)
    {
        Assert.Throws<System.ArgumentException>(() => KsProcgenOwnedDiagnosticReportBuilder.Build(Request(),
            new KsProcgenOwnedInteriorResult(), maximumEntries: entries, maximumGeometryCells: cells));
    }

    private static KsProcgenRequest Request() => new()
        { RequestId = "Diagnostic", Shape = new() { Cells = new List<Vector2i> { new(0, 0) } } };
}
