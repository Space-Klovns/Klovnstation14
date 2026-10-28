#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedDiagnosticReportBuilder))]
public sealed class KsProcgenSupportedReviewTests : KsProcgenGameTest
{
    [TestCase(KsProcgenRelationSeverity.Required)] [TestCase(KsProcgenRelationSeverity.Preferred)]
    public async Task KnownRequiredRootContradictionsNeedNoPathWorkAndPreferencesRemainSoft(KsProcgenRelationSeverity severity)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var variant = new KsProcgenAssemblyVariant { Id = "RootFacing", AnchorMember = "Machine", Members = new() {
                new() { Id = "Machine", Entity = "KsProcgenFilteredComputerFixture", RequiresInteractionApproach = true,
                    RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } },
                new() { Id = "Target", Entity = "KsProcgenRoomEmptyFixture", Movement = KsProcgenMovementClass.Clear,
                    RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } } },
                Relations = new() { new() { Id = "Face", Subject = "Machine", Target = "Target", Kind = KsProcgenRelationKind.FacingTarget, Severity = severity } } };
            Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("RootFacing", variant, new Dictionary<string, string>(), _ => true, out var assembly, out var issue), Is.True, issue?.Code);
            Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(SProtoMan, SEntMan.ComponentFactory, assembly!, out var capabilities, out issue), Is.True, issue?.Code);
            var room = new KsProcgenRoomAccessMask(Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3).Select(y => new Vector2i(x, y))).ToHashSet(),
                new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { new(0, 0) }, new(0.5f, 0.5f));
            var result = KsProcgenSupportedAssemblySearch.Search(assembly!, new[] { "Machine", "Target" }, capabilities!, room, 17,
                fixedFloorRoots: new[] { new KsProcgenFloorRootPose("Machine", new(1.5f, 1.5f)), new("Target", new(2.5f, 1.5f)) },
                maximumExpandedCells: severity == KsProcgenRelationSeverity.Required ? 0 : 65_536, requireConnectedCleanFloor: true,
                preferenceBudget: new(maximumPlacementProbes: 0));
            Assert.That(result.Status, Is.EqualTo(severity == KsProcgenRelationSeverity.Required ? KsProcgenSupportedAccessStatus.Rejected : KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            if (severity == KsProcgenRelationSeverity.Required) Assert.That(result.ExpandedCells, Is.Zero);
            else Assert.That(result.PreferenceScore.MissedRelations, Is.EqualTo(1));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task CompleteSupportedRoomReviewPreservesLayersParentsFacingAndCardinalAccess()
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result);
            Assert.That(report.SupportedEntities.Count, Is.EqualTo(5));
            Assert.That(report.SupportedEntities.Select(member => member.Layer).Distinct(), Is.EquivalentTo(new[] { "Floor", "Wall", "Surface", "Container" }));
            var table = report.SupportedEntities.Single(member => member.Member.SourceMemberId == "Table");
            var device = report.SupportedEntities.Single(member => member.Member.SourceMemberId == "Device");
            var paper = report.SupportedEntities.Single(member => member.Member.SourceMemberId == "Paper");
            var apc = report.SupportedEntities.Single(member => member.Member.SourceMemberId == "Apc");
            Assert.That(device.SupportParentMemberId, Is.EqualTo(table.Member.MemberId));
            Assert.That(device.Position, Is.Not.Null);
            Assert.That(paper.SupportParentMemberId, Is.EqualTo(device.Member.MemberId));
            Assert.That(paper.CoordinateParentMemberId, Is.EqualTo(device.Member.MemberId));
            Assert.That(paper.SlotId, Is.EqualTo("microwave_entity_container"));
            Assert.That(paper.Exposed, Is.False);
            Assert.That(paper.Position, Is.Null);
            Assert.That(paper.Footprint.Cells, Is.Empty);
            Assert.That(apc.Footprint.Cells.Single(), Is.EqualTo(new KsProcgenDiagnosticCell((int) System.MathF.Floor(apc.Position!.X), (int) System.MathF.Floor(apc.Position.Y))));
            Assert.That(report.OperatingAccess.Any(access => access.Member.MemberId == device.Member.MemberId && access.AssociatedSeatMemberId != null), Is.True);
            var layered = result.Furnishings.Single().Proposal.Layered!;
            foreach (var access in report.OperatingAccess)
            {
                var source = layered.Access.Access.Single(item => item.MemberId == access.Member.MemberId);
                Assert.That(access.CleanPath.Truncated, Is.False);
                Assert.That(access.CleanPath.Cells.Select(cell => (cell.X, cell.Y)), Is.EqualTo(source.CleanPath.Cells.Select(cell => (cell.X, cell.Y))));
                Assert.That(access.QuarterTurns, Is.EqualTo(source.QuarterTurns));
            }
            Assert.That(report.Relations.Select(relation => relation.Kind), Does.Contain("UsesSeat"));
            Assert.That(report.EntriesWritten, Is.EqualTo(Entries(report)));
            Assert.That(report.GeometryCellsWritten, Is.EqualTo(Geometry(report)));
            Assert.That(report.GeometryTruncated || report.EntriesTruncated || report.InputWorkTruncated, Is.False);
            var json = KsProcgenDiagnosticExport.Serialize(report);
            Assert.That(json.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete));
            using var document = JsonDocument.Parse(json.Data);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(3));
            Assert.That(document.RootElement.GetProperty("supportedEntities").GetArrayLength(), Is.EqualTo(5));
            Assert.That(report.Published || report.EngineAccessVerified || report.WorkingLightingVerified, Is.False);
            var directory = System.Environment.GetEnvironmentVariable("KS_PROCGEN_ARTIFACT_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "supported-room.json"), json.Data);
            }
        }));
    }

    [TestCase(1)] [TestCase(160)] [TestCase(1000)]
    public async Task GeometryAllowanceIsSharedAcrossEveryCollectionAndMarksPartialWitnesses(int maximum)
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var full = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result);
            var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result, maximumGeometryCells: maximum);
            Assert.That(Geometry(report), Is.LessThanOrEqualTo(maximum));
            Assert.That(report.GeometryCellsWritten, Is.EqualTo(Geometry(report)));
            Assert.That(report.GeometryTruncated, Is.EqualTo(Geometry(full) > maximum));
            Assert.That(report.EntriesTruncated, Is.False);
            foreach (var access in report.OperatingAccess)
            {
                var original = full.OperatingAccess.Single(item => item.Member.MemberId == access.Member.MemberId);
                Assert.That(access.CleanPath.Truncated, Is.EqualTo(access.CleanPath.Cells.Count != original.CleanPath.Cells.Count));
                Assert.That(access.CleanPath.Cells, Is.EqualTo(original.CleanPath.Cells.Take(access.CleanPath.Cells.Count)));
            }
        }));
    }

    [Test]
    public async Task OmittedMetadataDoesNotConsumeNestedGeometryAndCannotExceedItsAllowance()
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var full = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result);
            var prefix = full.Constraints.Count + full.Stages.Count + full.Attempts.Count + full.Attempts.Sum(attempt => attempt.Fallbacks.Count) +
                full.SizeGoalAttempts.Count + full.Fallbacks.Count + full.SizeMix.Count + full.Doors.Count;
            var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result, maximumEntries: prefix);
            Assert.That(report.EntriesTruncated, Is.True);
            Assert.That(report.EntriesWritten, Is.EqualTo(prefix));
            Assert.That(Entries(report), Is.EqualTo(prefix));
            Assert.That(report.Rooms, Is.Empty);
            Assert.That(report.SupportedEntities, Is.Empty);
            Assert.That(report.GeometryCellsWritten, Is.EqualTo(BaseGeometry(full)));
            Assert.That(report.GeometryTruncated, Is.False);
        }));
    }

    [TestCase(1)] [TestCase(10)] [TestCase(50)]
    public async Task ProjectionInputExhaustionReturnsNoPartialCandidateButRetainsGenerationWork(int maximum)
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result, maximumInputElements: maximum);
            Assert.That(report.Status, Is.EqualTo("BudgetExceeded"));
            Assert.That(report.ReasonCode, Is.EqualTo("DiagnosticProjectionWorkBudget"));
            Assert.That(report.InputWorkTruncated, Is.True);
            Assert.That(report.ProjectionInputElements, Is.EqualTo(maximum));
            Assert.That(report.SemanticHash, Is.EqualTo("0"));
            Assert.That(Entries(report) + Geometry(report), Is.Zero);
            Assert.That(report.Work.ExpandedCells, Is.EqualTo(result.ExpandedCells));
            Assert.That(report.Work.FurnishingProbes, Is.EqualTo(result.FurnishingProbes));
        }));
    }

    [Test]
    public async Task SupportedLabelsRemainJsonTextAndBoundedExportsRemainCanonical()
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result);
            var member = report.SupportedEntities.First(item => item.Exposed) with { EntityId = "<script>alert('x')</script>", SupportParentMemberId = "<&parent>" };
            var text = new KsProcgenOwnedDiagnosticReport { RequestId = request.RequestId, FloorCells = report.FloorCells, SupportedEntities = new[] { member } };
            var rendered = KsProcgenDiagnosticExport.Serialize(text);
            Assert.That(rendered.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete));
            using var document = JsonDocument.Parse(rendered.Data);
            Assert.That(document.RootElement.GetProperty("supportedEntities")[0].GetProperty("entityId").GetString(), Is.EqualTo(member.EntityId));
            Assert.That(KsProcgenDiagnosticExport.Serialize(report, maximumBytes: 32).Data, Is.Empty);
            Assert.That(KsProcgenDiagnosticExport.Serialize(KsProcgenOwnedDiagnosticReportBuilder.Build(request, result)).Data,
                Is.EqualTo(KsProcgenDiagnosticExport.Serialize(report).Data));
        }));
    }

    private static int BaseGeometry(KsProcgenOwnedDiagnosticReport report) => report.RequestedCells.Count + report.FloorCells.Count + report.WallCells.Count +
        report.ProtectedCells.Count + report.PreservedCells.Count + report.VoidCells.Count + report.EntranceCells.Count + report.WindowCells.Count;
    private static int Geometry(KsProcgenOwnedDiagnosticReport report) => BaseGeometry(report) + report.Rooms.Sum(room => room.Cells.Count) +
        report.Entities.Sum(entity => entity.Footprint.Count) + report.SupportedEntities.Sum(member => member.Footprint.Cells.Count) +
        report.OperatingAccess.Sum(access => access.CleanPath.Cells.Count) + report.Relations.Sum(relation => relation.CleanPath.Cells.Count);
    private static int Entries(KsProcgenOwnedDiagnosticReport report) => report.Constraints.Count + report.Stages.Count + report.Attempts.Count +
        report.Attempts.Sum(attempt => attempt.Fallbacks.Count) + report.SizeGoalAttempts.Count + report.Fallbacks.Count + report.SizeMix.Count +
        report.Doors.Count + report.Rooms.Count + report.Entities.Count + report.Preferences.Count + report.Distances.Count +
        report.SupportedEntities.Count + report.OperatingAccess.Count + report.Relations.Count;

    private void WithRoom(System.Action<KsProcgenRequest, KsProcgenOwnedInteriorResult> assertions)
    {
        var before = SEntMan.GetEntities().ToHashSet();
        var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "Review", Themes = new() { ["Review"] = new() { Parent = "KsProcgenSimpleOffice", LightingPacks = new(),
                Goals = new() { FurnishingDensity = 0f, LightingCoverage = 0f }, EntityPacks = new() { new() { Pack = "Contents" } } } },
            Entities = new() { ["Contents"] = new() { Assemblies = new() { new() { Id = "Workstation", Assembly = "Workstation", MinimumCount = 1 } } } },
            Assemblies = new() { ["Workstation"] = new() { AnchorMember = "Table", Members = new() {
                new() { Id = "Table", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired, RotationMode = KsProcgenMemberRotation.Independent },
                new() { Id = "Device", Entity = "KsProcgenRotatableMicrowaveFixture", Role = KsProcgenEntityRole.Equipment, RequiresInteractionApproach = true },
                new() { Id = "Paper", Entity = "Paper", Movement = KsProcgenMovementClass.Clear },
                new() { Id = "Apc", Entity = "APCBasic", Role = KsProcgenEntityRole.Equipment, RequiresInteractionApproach = true, RotationMode = KsProcgenMemberRotation.Independent },
                new() { Id = "Seat", Entity = "ChairOfficeLight", Role = KsProcgenEntityRole.Seat, Movement = KsProcgenMovementClass.Clear, LocalQuarterTurns = 2 } },
                Relations = new() {
                    new() { Id = "Top", Subject = "Device", Target = "Table", Kind = KsProcgenRelationKind.OnSurface },
                    new() { Id = "Inside", Subject = "Paper", Target = "Device", Kind = KsProcgenRelationKind.InContainer, ContainerId = "microwave_entity_container" },
                    new() { Id = "Use", Subject = "Device", Target = "Seat", Kind = KsProcgenRelationKind.UsesSeat },
                    new() { Id = "Adjacent", Subject = "Seat", Target = "Table", Kind = KsProcgenRelationKind.AdjacentTo },
                    new() { Id = "Face", Subject = "Seat", Target = "Table", Kind = KsProcgenRelationKind.FacingTarget } } } },
        };
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Review", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
        var request = new KsProcgenRequest { RequestId = "SupportedReview", Seed = 17, EntranceDomain = new(), ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(8, 8) } },
                EnvelopeCells = Enumerable.Range(-1, 10).SelectMany(x => Enumerable.Range(-1, 10).Where(y => x is -1 or 8 || y is -1 or 8).Select(y => new Vector2i(x, y))).ToList() } };
        try
        {
            var result = stages.TryGenerate(request, "Review", content: catalog, planningBudgets: new() { PreferredMaximumRoomCells = 64, MaximumContentRoomCells = 64 });
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), result.Issue?.Code);
            assertions(request, result.Proposal!);
            Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
            Assert.That(stages.Verify(result.Stage!), Is.True);
        }
        finally { stages.DiscardAll(); }
        Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
    }
}
