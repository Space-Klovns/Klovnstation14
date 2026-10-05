using System;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPlanningReportTests
{
    [Test]
    public void SoftMissesProduceDegradedUnpublishedReportWithExactTrace()
    {
        var request = Request();
        var result = new KsProcgenGeometryPipelineResult
        {
            Status = KsProcgenGeometryPipelineStatus.GeometryPlanned,
            Packing = new KsProcgenPackingResult
            {
                Status = KsProcgenPackingStatus.GeometryReady,
                SearchComplete = true,
                CellClaims = [(new Vector2i(0, 0), new KsProcgenCellClaim("procedural", KsProcgenCellDisposition.ProceduralFloor))],
            },
            PortNetwork = new KsProcgenPortNetworkResult { Status = KsProcgenPortNetworkStatus.Connected },
            Partition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.MergedFallback,
                Issue = new KsProcgenIssue("PartitionMergedFallback", "fixture"),
            },
            Windows = new KsProcgenWindowPlanResult
            {
                Status = KsProcgenWindowPlanStatus.Planned,
                EligibleCells = 4,
                RequestedWindowCells = 1,
                AchievedWindowCells = 0,
            },
            FinalSizeMixOutcomes = [new KsProcgenSizeMixOutcome("small", 2, 1)],
            SemanticHash = 123UL,
        };

        var report = result.Summarize(request);
        var bounded = result.Summarize(request, maxEntries: 1);

        Assert.Multiple(() =>
        {
            Assert.That(report.Disposition, Is.EqualTo(KsProcgenPlanningDisposition.Degraded));
            Assert.That(report.Published, Is.False);
            Assert.That(report.SearchComplete, Is.True);
            Assert.That(report.SemanticHash, Is.EqualTo(123UL));
            Assert.That(report.Fallbacks.Select(item => item.Code),
                Is.EqualTo(new[] { "RoomSizeShortfall", "SoftWindowMiss", "PartitionMerge" }));
            Assert.That(report.Constraints.Single(item => item.Id == "room-size:small").State,
                Is.EqualTo(KsProcgenConstraintState.Missed));
            Assert.That(report.Constraints.Single(item => item.Id == "operational-access").State,
                Is.EqualTo(KsProcgenConstraintState.Unverified));
            Assert.That(report.Constraints.Single(item => item.Id == "gas-closure").State,
                Is.EqualTo(KsProcgenConstraintState.Unverified));
            Assert.That(bounded.Disposition, Is.EqualTo(KsProcgenPlanningDisposition.Degraded));
            Assert.That(bounded.EntriesTruncated, Is.True);
            Assert.That(bounded.Fallbacks.Single().Code, Is.EqualTo("RoomSizeShortfall"));
        });
    }

    [Test]
    public void HardMissStillRejectsWhenReportEntriesAreTruncated()
    {
        var request = Request();
        request.FallbackPolicy.AllowRoomSizeShortfall = false;
        var result = new KsProcgenGeometryPipelineResult
        {
            Status = KsProcgenGeometryPipelineStatus.FallbackDisallowed,
            Issue = new KsProcgenIssue("RoomSizeShortfallDisallowed", "fixture"),
            FinalSizeMixOutcomes = [new KsProcgenSizeMixOutcome("small", 2, 1)],
        };

        var report = result.Summarize(request, maxEntries: 1);

        Assert.That(report.Disposition, Is.EqualTo(KsProcgenPlanningDisposition.Rejected));
        Assert.That(report.EntriesTruncated, Is.True);
        Assert.That(report.SemanticHash, Is.Zero);
        Assert.That(report.Issue?.Code, Is.EqualTo("RoomSizeShortfallDisallowed"));
        Assert.That(report.Fallbacks, Is.Empty);
    }

    [Test]
    public void HardWindowCountCanPassWhileSoftFractionMisses()
    {
        var request = Request();
        request.SizeMix.Clear();
        request.WindowGoal.ExteriorWindowFraction = 1f;
        request.WindowGoal.MaximumCount = 1;
        var result = new KsProcgenGeometryPipelineResult
        {
            Status = KsProcgenGeometryPipelineStatus.GeometryPlanned,
            Packing = new KsProcgenPackingResult
            {
                Status = KsProcgenPackingStatus.GeometryReady,
                CellClaims = [(new Vector2i(0, 0), new KsProcgenCellClaim("procedural", KsProcgenCellDisposition.ProceduralFloor))],
            },
            PortNetwork = new KsProcgenPortNetworkResult { Status = KsProcgenPortNetworkStatus.Connected },
            Windows = new KsProcgenWindowPlanResult
            {
                Status = KsProcgenWindowPlanStatus.Planned,
                EligibleCells = 2,
                RequestedWindowCells = 2,
                AchievedWindowCells = 1,
            },
        };

        var report = result.Summarize(request);

        Assert.That(report.Disposition, Is.EqualTo(KsProcgenPlanningDisposition.Degraded));
        Assert.That(report.Constraints.Single(item => item.Id == "window-fraction").State,
            Is.EqualTo(KsProcgenConstraintState.Missed));
        Assert.That(report.Constraints.Single(item => item.Id == "window-fraction").Hard, Is.False);
        Assert.That(report.Constraints.Single(item => item.Id == "window-count").State,
            Is.EqualTo(KsProcgenConstraintState.Satisfied));
        Assert.That(report.Constraints.Single(item => item.Id == "window-count").Hard, Is.True);
        Assert.That(report.Fallbacks.Single().Code, Is.EqualTo("SoftWindowMiss"));
    }

    [Test]
    public void InvalidRequestStillGetsRejectedReportAndPolicyValidation()
    {
        var request = Request();
        request.FallbackPolicy = null!;
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidRequest"));

        var report = KsProcgenPlanningReportBuilder.Build(request,
            new KsProcgenGeometryPipelineResult
            {
                Status = KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = issue,
            });
        Assert.That(report.Disposition, Is.EqualTo(KsProcgenPlanningDisposition.Rejected));
        Assert.That(report.Issue?.Code, Is.EqualTo("InvalidRequest"));
        Assert.Throws<ArgumentException>(() => KsProcgenPlanningReportBuilder.Build(request,
            new KsProcgenGeometryPipelineResult(), maxEntries: 0));
    }

    [Test]
    public void AssemblyWitnessesRetainSeverityAndBudgetTruncationInPreview()
    {
        var result = new KsProcgenGeometryPipelineResult
        {
            Status = KsProcgenGeometryPipelineStatus.GeometryPlanned,
            Furnishings =
            [
                new KsProcgenFurnishedRegion("Office", new KsProcgenFurnishingResult
                {
                    Status = KsProcgenFurnishingStatus.Sparse,
                    PreferenceSearchTruncated = true,
                    RelationWitnesses =
                    [
                        new("Pack", "Core", "Assembly", "Base", 0, "Facing", "Device", "Seat",
                            KsProcgenRelationKind.UsesSeat, KsProcgenRelationSeverity.Required,
                            KsProcgenConstraintState.Satisfied, new(1, 0), new(0, 0), null),
                        new("Pack", "Core", "Assembly", "Base", 0, "Corner", "Table", null,
                            KsProcgenRelationKind.AtCorner, KsProcgenRelationSeverity.Preferred,
                            KsProcgenConstraintState.Missed, new(2, 2), null, "RelationGeometryUnmet"),
                    ],
                }),
            ],
        };
        var report = result.Summarize(Request());
        var facing = report.Constraints.Single(item => item.Id.EndsWith(":Facing"));
        var corner = report.Constraints.Single(item => item.Id.EndsWith(":Corner"));
        Assert.That(facing.Hard, Is.True);
        Assert.That(facing.State, Is.EqualTo(KsProcgenConstraintState.Satisfied));
        Assert.That(corner.Hard, Is.False);
        Assert.That(corner.State, Is.EqualTo(KsProcgenConstraintState.Missed));
        Assert.That(corner.ReasonCode, Is.EqualTo("RelationGeometryUnmet"));
        Assert.That(report.Constraints.Single(item => item.Id == "assembly-preference-search:Office").State,
            Is.EqualTo(KsProcgenConstraintState.Unverified));
        Assert.That(report.Fallbacks.Single().ReasonCode, Is.EqualTo("FurnishingPreferenceProbeBudget"));
        Assert.That(report.Constraints.Single(item => item.Id == "operational-access").State,
            Is.EqualTo(KsProcgenConstraintState.Unverified), "Geometric witnesses cannot verify engine interaction.");
        Assert.That(report.Disposition, Is.EqualTo(KsProcgenPlanningDisposition.Degraded));
        Assert.That(report.Published, Is.False);
    }

    [Test]
    public void NearPreviewReportsRequestedRangeAndExactCleanDistance()
    {
        var result = new KsProcgenGeometryPipelineResult
        {
            Status = KsProcgenGeometryPipelineStatus.GeometryPlanned,
            Furnishings = [new KsProcgenFurnishedRegion("Room", new KsProcgenFurnishingResult
            {
                Status = KsProcgenFurnishingStatus.Proposed,
                RelationWitnesses = [new("Pack", "Core", "Assembly", "Base", 0, "Near", "A", "B",
                    KsProcgenRelationKind.Near, KsProcgenRelationSeverity.Required, KsProcgenConstraintState.Satisfied,
                    new(0, 0), new(2, 0), null, MinimumDistance: 1, MaximumDistance: 4, PathDistance: 2,
                    CleanPath: new KsProcgenRelationPath([new(0, 0), new(1, 0), new(2, 0)]))],
            })],
        };
        var report = result.Summarize(Request());
        var near = report.Constraints.Single(item => item.Id.EndsWith(":Near"));
        Assert.That(near.Requested, Is.EqualTo("1..4 clean steps"));
        Assert.That(near.Achieved, Is.EqualTo("2"));
        Assert.That(near.Hard, Is.True);
        Assert.That(near.State, Is.EqualTo(KsProcgenConstraintState.Satisfied));
        Assert.That(report.Published, Is.False);
    }

    private static KsProcgenRequest Request() => new()
    {
        RequestId = "PlanningReport",
        Shape = new KsProcgenShapeSpec { Cells = [new Vector2i(0, 0)] },
        SizeMix =
        [
            new KsProcgenRoomSizeGoal { Id = "small", MinCells = 1, MaxCells = 4, TargetCount = 2 },
        ],
        WindowGoal = new KsProcgenWindowGoal { ExteriorWindowFraction = 0.25f, ToleranceCells = 0 },
    };
}
