#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenPreferenceAudit))]
public sealed class KsProcgenPreferenceAuditTests : KsProcgenGameTest
{
    [TestCase("distance")] [TestCase("penalty")] [TestCase("band")] [TestCase("missing")]
    [TestCase("duplicate")] [TestCase("score")] [TestCase("authored_score")] [TestCase("source")]
    public async Task IndependentAuditRejectsForgedPolicyOrMeasurements(string mutation)
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var room = result.Themes!.Regions.Single();
            var layered = result.Furnishings.Single().Proposal.Layered!;
            var original = layered.Placement;
            Assert.That(KsProcgenPreferenceAudit.TryVerify(room, layered.Composition, layered.Capabilities, original, layered.Access, out var issue), Is.True, issue?.Code);
            Assert.That(original.RootPreferenceOutcomes, Is.Not.Empty);
            var outcomes = original.RootPreferenceOutcomes.ToArray();
            var score = original.PreferenceScore;
            var composition = layered.Composition;
            switch (mutation)
            {
                case "distance": outcomes[0] = outcomes[0] with { Distance = outcomes[0].Distance + 1 }; break;
                case "penalty": outcomes[0] = outcomes[0] with { Penalty = outcomes[0].Penalty + 1 }; break;
                case "band": outcomes[0] = outcomes[0] with { MaximumDistance = outcomes[0].MaximumDistance - 1 }; break;
                case "missing": outcomes = outcomes.Skip(1).ToArray(); break;
                case "duplicate": outcomes = outcomes.Append(outcomes[0]).ToArray(); break;
                case "score": score = score with { DistancePenalty = score.DistancePenalty + 1 }; break;
                case "authored_score": score = score with { MissedRelations = score.MissedRelations + 1 }; break;
                case "source":
                    var instances = composition.Instances.ToArray();
                    var source = instances[0].Assembly;
                    instances[0] = instances[0] with { Assembly = source with { Members = source.Members.Select((member, index) =>
                        index == 0 ? member with { Entry = member.Entry with { Weight = member.Entry.Weight + 1f } } : member).ToArray() } };
                    composition = KsProcgenRoomAssemblyComposer.Compose(room.Id, instances);
                    Assert.That(composition.Assembly, Is.Not.Null);
                    break;
            }
            var forged = Copy(original, outcomes, score);
            Assert.That(KsProcgenPreferenceAudit.TryVerify(room, composition, layered.Capabilities, forged, layered.Access, out issue), Is.False);
            Assert.That(issue!.Code, Is.EqualTo("StaleLayeredPreferenceEvidence"));
            Assert.That(KsProcgenPreferenceAudit.TryVerify(room, layered.Composition, layered.Capabilities, original, layered.Access, out issue), Is.True);
        }));
    }

    [Test]
    public async Task SelectedProvenanceIsDetachedAndStructurallyReconstructed()
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var room = result.Themes!.Regions.Single();
            var layered = result.Furnishings.Single().Proposal.Layered!;
            var source = layered.Composition.Instances.ToArray();
            var members = source[0].Assembly.Members.Reverse().ToList();
            var footprint = members[0].Entry.Footprint.ToList();
            var turns = members[0].Entry.AllowedQuarterTurns.ToList();
            members[0] = members[0] with { Entry = members[0].Entry with { Footprint = footprint, AllowedQuarterTurns = turns } };
            source[0] = source[0] with { Assembly = source[0].Assembly with { Members = members } };
            var detached = KsProcgenRoomAssemblyComposer.Compose(room.Id, source);
            members.Clear();
            footprint.Clear(); turns.Clear();
            source[0] = source[0] with { PackId = "ChangedAfterComposition" };
            Assert.That(detached.Instances[0].Assembly.Members, Is.Not.Empty);
            Assert.That(detached.Instances[0].Assembly.Members[0].Entry.Footprint, Is.Not.Empty);
            Assert.That(detached.Instances[0].Assembly.Members[0].Entry.AllowedQuarterTurns, Is.Not.Empty);
            Assert.That(detached.Instances[0].PackId, Is.Not.EqualTo("ChangedAfterComposition"));
            Assert.That(KsProcgenPreferenceAudit.TryVerify(room, detached, layered.Capabilities, layered.Placement, layered.Access, out var issue), Is.True, issue?.Code);
            var collection = layered.Placement.RootPreferenceOutcomes as ICollection<KsProcgenRootDistanceOutcome>;
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection!.IsReadOnly, Is.True);
        }));
    }

    [Test]
    public async Task PreferenceReviewIsCanonicalBoundedAndDoesNotClaimEngineSuccess()
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result, maximumEntries: 65_536);
            var layered = result.Furnishings.Single().Proposal.Layered!;
            Assert.That(report.Preferences.Count, Is.EqualTo(1));
            Assert.That(report.Preferences.Single().DistancePenalty, Is.EqualTo(layered.Placement.PreferenceScore.DistancePenalty));
            Assert.That(report.Distances.Count, Is.EqualTo(layered.Placement.RootPreferenceOutcomes.Count));
            foreach (var distance in report.Distances)
            {
                Assert.That(distance.Metric, Is.EqualTo("ManhattanAnchors"));
                Assert.That(distance.Subject.PackId, Is.EqualTo("Contents"));
                Assert.That(distance.Target.PackId, Is.EqualTo("Contents"));
                Assert.That(distance.Subject.SourceMemberId, Is.EqualTo("Root"));
                Assert.That(distance.Penalty, Is.Zero);
            }
            var json = KsProcgenDiagnosticExport.Serialize(report);
            Assert.That(json.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete));
            using var parsed = JsonDocument.Parse(json.Data);
            Assert.That(parsed.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(3));
            Assert.That(parsed.RootElement.GetProperty("distances").GetArrayLength(), Is.EqualTo(report.Distances.Count));
            Assert.That(parsed.RootElement.GetProperty("engineAccessVerified").GetBoolean(), Is.False);
            Assert.That(parsed.RootElement.GetProperty("published").GetBoolean(), Is.False);
            var legacyEntries = report.Constraints.Count + report.Stages.Count + report.Attempts.Count + report.Attempts.Sum(attempt => attempt.Fallbacks.Count) +
                report.SizeGoalAttempts.Count + report.Fallbacks.Count + report.SizeMix.Count + report.Doors.Count + report.Rooms.Count + report.Entities.Count;
            var limited = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result, maximumEntries: legacyEntries + report.Preferences.Count + 1);
            Assert.That(limited.EntriesTruncated, Is.True);
            Assert.That(limited.Preferences.Count, Is.EqualTo(1));
            Assert.That(limited.Distances.Count, Is.EqualTo(1));
            Assert.That(KsProcgenDiagnosticExport.Serialize(report, maximumBytes: 32).Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.BudgetExceeded));
            var repeated = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result, maximumEntries: 65_536);
            Assert.That(KsProcgenDiagnosticExport.Serialize(repeated).Data, Is.EqualTo(json.Data));
        }));
    }

    [Test]
    public async Task LongRelationalPathRejectsAnObstructingLightAmongManyOtherLights()
    {
        await AssertOnServer(() => WithRoom((request, result) =>
        {
            var region = result.Themes!.Regions.Single();
            var furnishing = result.Furnishings.Single().Proposal;
            var layered = furnishing.Layered!;
            var path = layered.Access.Relations.Single().CleanPath.Cells.ToHashSet();
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(6));
            var columns = region.FloorCells.Select(cell => cell.X).ToArray();
            var cells = region.FloorCells.Where(cell => (cell.X == columns.Min() || cell.X == columns.Max()) &&
                !path.Contains(cell) && !furnishing.ProtectedPassageCells.Contains(cell) && !layered.OccupiedCells.Contains(cell)).Take(12).ToArray();
            Assert.That(cells.Length, Is.EqualTo(12));
            var lights = cells.Select(cell => new KsProcgenLightProposal(cell, "Poweredlight", "Audit", KsProcgenLightingSupply.CallerSupplied)).ToArray();
            KsProcgenGroupInteriorResult Analyze(IReadOnlyList<KsProcgenLightProposal> proposals) => KsProcgenGroupInteriorAnalyzer.Analyze(
                request, result.Partition!, result.Themes!, result.Furnishings,
                new[] { new KsProcgenLitRegion(region.Id, new() { Status = KsProcgenLightingPlanStatus.Proposed,
                    Lights = proposals, TotalFloorCells = region.FloorCells.Count }) }, structure: result.Structure);
            var benign = Analyze(lights);
            Assert.That(benign.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), benign.Issue?.Code);
            var obstruction = path.First(cell => !layered.OccupiedCells.Contains(cell) && !furnishing.ProtectedPassageCells.Contains(cell));
            var blocked = Analyze(lights.Append(new(obstruction, "Poweredlight", "Audit", KsProcgenLightingSupply.CallerSupplied)).ToArray());
            Assert.That(blocked.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Rejected));
            Assert.That(blocked.Issue!.Code, Is.EqualTo("BlockedLayeredRelationPath"));
        }, longRelation: true));
    }

    private void WithRoom(System.Action<KsProcgenRequest, KsProcgenOwnedInteriorResult> assertions, bool longRelation = false)
    {
        var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
        var before = SEntMan.GetEntities().ToHashSet();
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "PreferenceAudit", Themes = new() { ["PreferenceAudit"] = new() { Parent = "KsProcgenSimpleOffice", LightingPacks = new(),
                Goals = new() { CoherentContents = true, FurnishingDensity = 0.04f, LightingCoverage = 0f },
                EntityPacks = new() { new() { Pack = "Contents" } } } },
            Entities = new() { ["Contents"] = new() { Assemblies = new() {
                new() { Id = "Alpha", Assembly = "Table", MinimumCount = 1 }, new() { Id = "Beta", Assembly = "Table", MinimumCount = 1 } } } },
            Assemblies = new() { ["Table"] = new() { AnchorMember = "Root", Members = new() {
                new() { Id = "Root", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired } } } },
        };
        if (longRelation)
        {
            spec.Themes["PreferenceAudit"].Goals!.FurnishingDensity = 0f;
            spec.Entities["Contents"].Assemblies = new() { new() { Id = "Pair", Assembly = "Pair", MinimumCount = 1 } };
            spec.Assemblies["Pair"] = new() { AnchorMember = "Root", Members = new() {
                new() { Id = "Root", Entity = "KsProcgenRoomEmptyFixture", Movement = KsProcgenMovementClass.Clear },
                new() { Id = "Target", Entity = "KsProcgenRoomEmptyFixture", Movement = KsProcgenMovementClass.Clear } },
                Relations = new() { new() { Id = "Near", Subject = "Root", Target = "Target", Kind = KsProcgenRelationKind.Near,
                    MinimumDistance = 6, MaximumDistance = 6 } } };
        }
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "PreferenceAudit", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
        var request = new KsProcgenRequest { RequestId = "PreferenceAudit", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, EntranceDomain = new(),
            Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(10, 10) } } } };
        try
        {
            var result = stages.TryGenerate(request, "PreferenceAudit", content: catalog,
                planningBudgets: new() { PreferredMaximumRoomCells = 128, MaximumContentRoomCells = 128 });
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), result.Issue?.Code);
            assertions(request, result.Proposal!);
            Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
            Assert.That(stages.Verify(result.Stage!), Is.True);
        }
        finally { stages.DiscardAll(); }
        Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
    }

    private static KsProcgenSupportedAssemblySearchResult Copy(KsProcgenSupportedAssemblySearchResult original,
        IReadOnlyList<KsProcgenRootDistanceOutcome> outcomes, KsProcgenPreferenceScore score) => new()
    {
        Status = original.Status, Access = original.Access, FloorRoots = original.FloorRoots, Orientations = original.Orientations,
        AssemblyQuarterTurns = original.AssemblyQuarterTurns, PlacementProbes = original.PlacementProbes,
        ExpandedCells = original.ExpandedCells, LandingProbes = original.LandingProbes,
        PreferenceSearchTruncated = original.PreferenceSearchTruncated, PreferenceScore = score, RootPreferenceOutcomes = outcomes,
    };
}
