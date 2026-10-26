using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenAreaSnapshotSystem))]
public sealed class KsProcgenAreaSnapshotTests : GameTest
{
    [Test]
    public async Task PaintedMixedSizeProfileProducesThreeRoomsWithoutRequestEdits()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var profileId = "KsProcgenMixedOfficeArea";
            var profile = manager.Index<KsProcgenAreaProfilePrototype>(profileId);
            Assert.That(profile.RoomSubdivision!.UseSizeGoals, Is.True);
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                for (var x = 0; x < 9; x++)
                for (var y = 0; y < 4; y++)
                {
                    var marker = Paint(grid.Owner, new((float) x + 0.5f, (float) y + 0.5f));
                    SEntMan.GetComponent<KsProcgenAreaCellComponent>(marker).Profile = profileId;
                }
                SEntMan.GetComponent<KsProcgenEntranceComponent>(Entrance(grid.Owner, new(-1, 0))).InwardNormal = new(1, 0);
                var before = SEntMan.GetEntities().ToHashSet();
                var snapshot = Pair.Server.System<KsProcgenAreaSnapshotSystem>().Snapshot(grid.Owner, "MixedSizes");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var blob = snapshot.Blobs.Single();
                var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                    new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }, new HashSet<Vector2i>(), true, true);
                var budgets = new KsProcgenOwnedPlanBudgets { MaximumPartitionMerges = 0, MaximumConfigurationProbes = 1 };
                var result = KsProcgenOwnedInteriorPlanner.Plan(manager, blob, 23, context, [], budgets: budgets);
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
                Assert.That(result.Partition!.SizeGoalSubdivisionUsed, Is.True);
                Assert.That(result.Themes!.Regions.Select(region => region.FloorCells.Count).Order(),
                    Is.EqualTo(new[] { 5, 9, 16 }));
                Assert.That(result.SizeMix.All(outcome => outcome.AchievedCount == outcome.RequestedCount), Is.True);
                Assert.That(result.LayoutProbes, Is.EqualTo(1));
                Assert.That(result.Attempts.Single().SizeGoalsRequested, Is.True);
                Assert.That(result.Stages.All(stage => stage.SizeGoalsRequested), Is.True);
                Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Not.Contain("RoomSizeDeviation"));
                Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Not.Contain("MergedPartition"));
                var replay = KsProcgenOwnedInteriorPlanner.Plan(manager, blob, 23, context, [], budgets: budgets with
                {
                    MaximumReportedGraphExpansions = result.ExpandedCells,
                    MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                    MaximumOrderProbes = result.OrderProbes,
                    MaximumFurnishingProbes = result.FurnishingProbes,
                    MaximumLightingFixtures = result.FixtureSelections,
                    MaximumLayoutProbes = 1,
                });
                Assert.That(replay.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), replay.Issue?.Code);
                Assert.That(replay.SemanticHash, Is.EqualTo(result.SemanticHash));
                Assert.That(result.Access, Is.Not.Null);
                Assert.That(result.Published || result.EngineAccessVerified || result.GasClosureVerified, Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                mapSystem.DeleteMap(mapId);
            }
        });
    }

    [Test]
    public async Task AuthoredGenerationGoalsDriveRetriesAndWindowFailureWithoutRequestEdits()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var profileId = "KsProcgenWholeOfficeArea";
            var profile = manager.Index<KsProcgenAreaProfilePrototype>(profileId);
            var authored = profile.GenerationGoals!;
            Assert.That(authored.SizeMix.Single().Id, Is.EqualTo("WholeRoom"));
            Assert.That(authored.SizeMix.Single().MinCells, Is.EqualTo(30));
            Assert.That(authored.FallbackPolicy.AllowMergedPartition || authored.FallbackPolicy.AllowRoomSizeShortfall, Is.False);
            Assert.That(authored.FallbackPolicy.AllowSparseFurnishing && authored.FallbackPolicy.AllowSparseLighting, Is.True);
            var originalMerge = authored.FallbackPolicy.AllowMergedPartition;
            var originalSize = authored.FallbackPolicy.AllowRoomSizeShortfall;
            var originalMinimumWindows = authored.WindowGoal.MinimumCount;
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                for (var x = 0; x < 6; x++)
                for (var y = 0; y < 5; y++)
                {
                    var marker = Paint(grid.Owner, new((float) x + 0.5f, (float) y + 0.5f));
                    SEntMan.GetComponent<KsProcgenAreaCellComponent>(marker).Profile = profileId;
                }
                SEntMan.GetComponent<KsProcgenEntranceComponent>(Entrance(grid.Owner, new(-1, 0))).InwardNormal = new(1, 0);
                var before = SEntMan.GetEntities().ToHashSet();
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "AuthoredGoals");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var blob = snapshot.Blobs.Single();
                var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                    new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }, new HashSet<Vector2i>(), true, true);
                var caller = new KsProcgenOwnedPlanBudgets { MaximumPartitionMerges = 0, MaximumConfigurationProbes = 1,
                    MaximumLayoutProbes = 3, PreferredMaximumRoomCells = 128 };
                var result = KsProcgenOwnedInteriorPlanner.Plan(manager, blob, 23, context, [], budgets: caller);
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
                Assert.That(result.Attempts.Select(attempt => attempt.PreferredMaximumRoomCells), Is.EqualTo(new[] { 4, 8, 64 }));
                Assert.That(result.SizeMix.Single().AchievedCount, Is.EqualTo(1));
                Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Not.Contain("MergedPartition"));
                Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Not.Contain("RoomSizeDeviation"));
                var direct = KsProcgenOwnedInteriorPlanner.Plan(manager, blob.CreateRequest(23), blob.Profile.Theme!,
                    context, [], budgets: blob.CreateOwnedBudgets(budgets: caller));
                Assert.That(direct.SemanticHash, Is.EqualTo(result.SemanticHash));
                authored.FallbackPolicy.AllowMergedPartition = true;
                authored.FallbackPolicy.AllowRoomSizeShortfall = true;
                var relaxedSnapshot = snapshotSystem.Snapshot(grid.Owner, "AuthoredGoals");
                Assert.That(relaxedSnapshot.SnapshotHash, Is.Not.EqualTo(snapshot.SnapshotHash));
                Assert.That(relaxedSnapshot.Blobs.Single().Id, Is.EqualTo(blob.Id));
                var relaxed = KsProcgenOwnedInteriorPlanner.Plan(manager, relaxedSnapshot.Blobs.Single(), 23, context, [], budgets: caller);
                Assert.That(relaxed.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), relaxed.Issue?.Code);
                Assert.That(relaxed.LayoutProbes, Is.EqualTo(1));
                Assert.That(relaxed.SemanticHash, Is.Not.EqualTo(result.SemanticHash));
                var replay = KsProcgenOwnedInteriorPlanner.Plan(manager, blob, 23, context, [], budgets: caller);
                Assert.That(replay.SemanticHash, Is.EqualTo(result.SemanticHash));
                authored.WindowGoal.MinimumCount = 1;
                var windowSnapshot = snapshotSystem.Snapshot(grid.Owner, "AuthoredGoals");
                Assert.That(windowSnapshot.SnapshotHash, Is.Not.EqualTo(relaxedSnapshot.SnapshotHash));
                var windowFailure = KsProcgenOwnedInteriorPlanner.Plan(manager, windowSnapshot.Blobs.Single(), 23, context, [], budgets: caller);
                Assert.That(windowFailure.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected), windowFailure.Issue?.Code);
                Assert.That(windowFailure.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Structure));
                Assert.That(windowFailure.SemanticHash, Is.Zero);
                Assert.That(windowFailure.Floor, Is.Null);
                Assert.That(windowFailure.Furnishings, Is.Empty);
                Assert.That(blob.CreateRequest(23).WindowGoal.MinimumCount, Is.Zero);
                Assert.That(blob.CreateRequest(23).FallbackPolicy.AllowMergedPartition, Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(result.Published || result.EngineAccessVerified || result.GasClosureVerified, Is.False);
            }
            finally
            {
                authored.FallbackPolicy.AllowMergedPartition = originalMerge;
                authored.FallbackPolicy.AllowRoomSizeShortfall = originalSize;
                authored.WindowGoal.MinimumCount = originalMinimumWindows;
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task InvalidAuthoredGoalsAndForgedBlobGoalsRejectBeforeAnyProposalOrMapWrite()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var profileId = "KsProcgenWholeOfficeArea";
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var profile = manager.Index<KsProcgenAreaProfilePrototype>(profileId);
            var original = profile.GenerationGoals;
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                SEntMan.GetComponent<KsProcgenAreaCellComponent>(Paint(grid.Owner, new(0.5f, 0.5f))).Profile = profileId;
                var before = SEntMan.GetEntities().ToHashSet();
                var validBlob = snapshotSystem.Snapshot(grid.Owner, "InvalidGoals").Blobs.Single();
                var forged = validBlob with { Profile = validBlob.Profile with
                {
                    GenerationGoals = validBlob.Profile.GenerationGoals! with { FallbackPolicy = null! },
                } };
                var rejected = KsProcgenOwnedInteriorPlanner.Plan(manager, forged, 23,
                    new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true), []);
                Assert.That(rejected.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
                Assert.That(rejected.Stages, Is.Empty);
                Assert.That(rejected.LayoutProbes + rejected.ExpandedCells, Is.Zero);
                foreach (var invalid in new KsProcgenAreaGoalsSpec[]
                {
                    new() { FallbackPolicy = null! }, new() { SizeMix = null! }, new() { WindowGoal = null! },
                    new() { WindowGoal = new() { ExteriorWindowFraction = float.NaN } },
                    new() { SizeMix = new List<KsProcgenRoomSizeGoal> { new() { Id = "Bad", MinCells = 0 } } },
                })
                {
                    profile.GenerationGoals = invalid;
                    var failed = snapshotSystem.Snapshot(grid.Owner, "InvalidGoals");
                    Assert.That(failed.Issue?.Code, Is.EqualTo("InvalidAreaGenerationGoals"));
                    Assert.That(failed.Blobs, Is.Empty);
                    Assert.That(failed.SnapshotHash, Is.Zero);
                }
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                profile.GenerationGoals = original;
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task SerializedSubdivisionPaintFeedsCompleteRetriesAndDetachesFromPrototypeEdits()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var profileId = "KsProcgenOfficeSubdivisionArea";
            var profile = manager.Index<KsProcgenAreaProfilePrototype>(profileId);
            Assert.That(profile.RoomSubdivision!.MinimumRoomCells, Is.EqualTo(4));
            Assert.That(profile.RoomSubdivision.PreferredMaximumRoomCells, Is.EqualTo(4));
            Assert.That(profile.RoomSubdivision.AlternativeMaximumRoomCells, Is.EqualTo(new[] { 8, 64 }));
            var originalSizes = profile.RoomSubdivision.AlternativeMaximumRoomCells.ToArray();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                for (var x = 0; x < 6; x++)
                for (var y = 0; y < 5; y++)
                {
                    var marker = Paint(grid.Owner, new((float) x + 0.5f, (float) y + 0.5f));
                    SEntMan.GetComponent<KsProcgenAreaCellComponent>(marker).Profile = profile.ID;
                }
                var entrance = Entrance(grid.Owner, new(-1, 0));
                SEntMan.GetComponent<KsProcgenEntranceComponent>(entrance).InwardNormal = new(1, 0);
                var before = SEntMan.GetEntities().ToHashSet();
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "SubdivisionHost");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var blob = snapshot.Blobs.Single();
                var caller = new KsProcgenOwnedPlanBudgets
                {
                    PreferredMaximumRoomCells = 128, MaximumPartitionMerges = 0,
                    MaximumConfigurationProbes = 1, MaximumLayoutProbes = 3,
                };
                var budgets = blob.CreateOwnedBudgets(budgets: caller);
                var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                    new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }, new HashSet<Vector2i>(), true, true);
                var request = blob.CreateRequest(23);
                request.FallbackPolicy.AllowMergedPartition = false;
                request.FallbackPolicy.AllowRoomSizeShortfall = false;
                request.SizeMix.Add(new() { Id = "WholeRoom", MinCells = 30, MaxCells = 30, TargetCount = 1 });
                var retried = KsProcgenOwnedInteriorPlanner.Plan(manager, request, blob.Profile.Theme!, context, [], budgets: budgets);
                Assert.That(retried.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), retried.Issue?.Code);
                Assert.That(retried.Attempts.Select(attempt => attempt.PreferredMaximumRoomCells), Is.EqualTo(new[] { 4, 8, 64 }));
                Assert.That(retried.ConfigurationProbes, Is.EqualTo(1));
                Assert.That(retried.SizeMix.Single().AchievedCount, Is.EqualTo(1));
                var explicitBlob = KsProcgenOwnedInteriorPlanner.Plan(manager, blob, 23, context, [], budgets: caller);
                var explicitRequest = KsProcgenOwnedInteriorPlanner.Plan(manager, blob.CreateRequest(23), blob.Profile.Theme!,
                    context, [], budgets: budgets);
                Assert.That(explicitBlob.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), explicitBlob.Issue?.Code);
                Assert.That(explicitBlob.SemanticHash, Is.EqualTo(explicitRequest.SemanticHash));
                Assert.That(explicitBlob.Attempts[0].PreferredMaximumRoomCells, Is.EqualTo(4));
                Assert.That(caller.PreferredMaximumRoomCells, Is.EqualTo(128));
                profile.RoomSubdivision.AlternativeMaximumRoomCells.Reverse();
                var changed = snapshotSystem.Snapshot(grid.Owner, "SubdivisionHost");
                Assert.That(changed.SnapshotHash, Is.Not.EqualTo(snapshot.SnapshotHash));
                Assert.That(changed.Blobs.Single().Id, Is.EqualTo(blob.Id));
                Assert.That(blob.Profile.RoomSubdivision!.AlternativeMaximumRoomCells, Is.EqualTo(new[] { 8, 64 }));
                Assert.That(blob.CreateOwnedBudgets().AlternativeMaximumRoomCells, Is.EqualTo(new[] { 8, 64 }));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(explicitBlob.Published || explicitBlob.EngineAccessVerified, Is.False);
            }
            finally
            {
                profile.RoomSubdivision!.AlternativeMaximumRoomCells = originalSizes.ToList();
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task InvalidLiveSubdivisionProfileFailsWithoutSnapshotArtifactsOrMapWrites()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var profileId = "KsProcgenOfficeSubdivisionArea";
            var profile = Pair.Server.ResolveDependency<IPrototypeManager>()
                .Index<KsProcgenAreaProfilePrototype>(profileId);
            var original = profile.RoomSubdivision;
            var originalMode = profile.Mode;
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                var marker = Paint(grid.Owner, new(0.5f, 0.5f));
                SEntMan.GetComponent<KsProcgenAreaCellComponent>(marker).Profile = profile.ID;
                var before = SEntMan.GetEntities().ToHashSet();
                var validBlob = snapshotSystem.Snapshot(grid.Owner, "InvalidSubdivision").Blobs.Single();
                var forgedBlob = validBlob with
                {
                    Profile = validBlob.Profile with { RoomSubdivision = new(4, 4, null!) },
                };
                var rejected = KsProcgenOwnedInteriorPlanner.Plan(Pair.Server.ResolveDependency<IPrototypeManager>(),
                    forgedBlob, 23, new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true), []);
                Assert.That(rejected.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
                Assert.That(rejected.Issue?.Code, Is.EqualTo("InvalidOwnedInteriorInput"));
                Assert.That(rejected.Stages, Is.Empty);
                Assert.That(rejected.LayoutProbes + rejected.ExpandedCells, Is.Zero);
                foreach (var invalid in new KsProcgenRoomSubdivisionSpec[]
                {
                    new() { MinimumRoomCells = 0 },
                    new() { AlternativeMaximumRoomCells = null! },
                    new() { AlternativeMaximumRoomCells = new List<int> { 8, 8 } },
                })
                {
                    profile.RoomSubdivision = invalid;
                    var failed = snapshotSystem.Snapshot(grid.Owner, "InvalidSubdivision");
                    Assert.That(failed.Issue?.Code, Is.EqualTo("InvalidAreaSubdivisionProfile"));
                    Assert.That(failed.Blobs, Is.Empty);
                    Assert.That(failed.SnapshotHash, Is.Zero);
                }
                profile.RoomSubdivision = original;
                profile.Mode = KsProcgenMode.Hybrid;
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "InvalidSubdivision").Issue?.Code,
                    Is.EqualTo("InvalidAreaSubdivisionProfile"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                profile.RoomSubdivision = original;
                profile.Mode = originalMode;
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    private EntityUid Entrance(EntityUid gridUid, Vector2i cell, string prototypeId = "KsProcgenEntrance")
    {
        var markerUid = SEntMan.SpawnEntity(prototypeId, new EntityCoordinates(gridUid, new Vector2((float) cell.X + 0.5f, (float) cell.Y + 0.5f)));
        Assert.That(Pair.Server.System<KsProcgenAreaSnapshotSystem>().BindMarkerToGrid(gridUid, markerUid, cell), Is.Null);
        return markerUid;
    }

    private EntityUid Paint(EntityUid gridUid, Vector2 position)
    {
        var coordinates = new EntityCoordinates(gridUid, position);
        var markerUid = SEntMan.SpawnEntity("KsProcgenAreaCell", coordinates);
        // Spawn resolves empty-grid coordinates onto the map. Map authoring instead retains an
        // explicit grid parent; establish that authored relationship through the public engine API.
        Pair.Server.System<SharedTransformSystem>().SetCoordinates(markerUid, coordinates);
        Assert.That(SEntMan.GetComponent<TransformComponent>(markerUid).ParentUid, Is.EqualTo(gridUid));
        return markerUid;
    }

    [Test]
    public async Task LoadedPaintNormalizesRotatedGridBlobsAndRetainsSourceMarkers()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                transformSystem.SetLocalRotation(grid.Owner, Angle.FromDegrees(73.0));
                var first = Paint(grid.Owner, new(0.5f, 0.5f));
                var second = Paint(grid.Owner, new(1.5f, 1.5f));
                Assert.That(SEntMan.GetComponent<TransformComponent>(first).ParentUid, Is.EqualTo(grid.Owner));
                var before = SEntMan.GetEntities().ToHashSet();
                var disconnected = snapshotSystem.Snapshot(grid.Owner, "FixtureGrid");
                var firstTransform = SEntMan.GetComponent<TransformComponent>(first);
                var secondTransform = SEntMan.GetComponent<TransformComponent>(second);
                Assert.That(disconnected.MarkerCount, Is.EqualTo(2), $"first={firstTransform.Coordinates}, grid={firstTransform.GridUid}; second={secondTransform.Coordinates}, grid={secondTransform.GridUid}");
                Assert.That(firstTransform.LocalPosition, Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(secondTransform.LocalPosition, Is.EqualTo(new Vector2(1.5f, 1.5f)));
                Assert.That(disconnected.Status, Is.EqualTo(KsProcgenStatus.Success), disconnected.Issue?.Code);
                Assert.That(disconnected.Blobs.Count, Is.EqualTo(2));
                Assert.That(disconnected.Blobs.All(blob => blob.Profile.Theme == "KsProcgenSimpleOffice"), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                var bridge = Paint(grid.Owner, new(1.5f, 0.5f));
                var connected = snapshotSystem.Snapshot(grid.Owner, "FixtureGrid");
                Assert.That(connected.Blobs.Single().Cells, Is.EqualTo(new Vector2i[] { new(0, 0), new(1, 0), new(1, 1) }));
                var removed = snapshotSystem.Snapshot(grid.Owner, "FixtureGrid", keepVoid: new HashSet<Vector2i> { new(1, 0) });
                Assert.That(removed.Blobs.Count, Is.EqualTo(2));
                Assert.That(disconnected.Blobs.Count, Is.EqualTo(2), "Previous detached snapshots stay unchanged.");
                Assert.That(SEntMan.EntityExists(first) && SEntMan.EntityExists(second) && SEntMan.EntityExists(bridge), Is.True);
                var request = connected.Blobs.Single().CreateRequest(91);
                Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
                Assert.That(shape!.TargetCells, Is.EqualTo(connected.Blobs.Single().Cells));
                Assert.That(shape.EnvelopeCells, Is.Empty);
                Assert.That(shape.PreservedCells, Is.Empty);
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task FractionalPaintMissingProfilesAndChannelOverlapsFailWithoutMutation()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                var markerUid = Paint(grid.Owner, new(0.75f, 0.5f));
                var markerTransform = SEntMan.GetComponent<TransformComponent>(markerUid);
                Assert.That(markerTransform.LocalPosition, Is.EqualTo(new Vector2(0.75f, 0.5f)), $"parent={markerTransform.ParentUid}; grid={markerTransform.GridUid}; anchored={markerTransform.Anchored}");
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Fixture").Issue?.Code, Is.EqualTo("AreaMarkerNotTileAligned"));
                transformSystem.SetCoordinates(markerUid, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
                var markerComponent = SEntMan.GetComponent<KsProcgenAreaCellComponent>(markerUid);
                markerComponent.Profile = "MissingAreaProfile";
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Fixture").Issue?.Code, Is.EqualTo("InvalidAreaProfileReference"));
                markerComponent.Profile = "KsProcgenOfficeArea";
                var otherUid = Paint(grid.Owner, new(0.5f, 0.5f));
                SEntMan.GetComponent<KsProcgenAreaCellComponent>(otherUid).Channel = "Other";
                var before = SEntMan.GetEntities().ToHashSet();
                var overlap = snapshotSystem.Snapshot(grid.Owner, "Fixture");
                Assert.That(overlap.Issue?.Code, Is.EqualTo("OverlappingAreaChannels"));
                Assert.That(overlap.Blobs, Is.Empty);
                Assert.That(overlap.SnapshotHash, Is.Zero);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Fixture", maximumMarkers: 1).Status, Is.EqualTo(KsProcgenStatus.BudgetExceeded));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Fixture", maximumScannedMarkers: 0).Issue?.Code, Is.EqualTo("AreaSnapshotScanBudget"));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task SnapshotIdsDoNotUseEntityUidsAndProfilesAreDetachedFromLivePrototypeChanges()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var profilePrototype = manager.Index(new ProtoId<KsProcgenAreaProfilePrototype>("KsProcgenOfficeArea"));
            var previousMaximum = profilePrototype.Limits.MaxCells;
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var firstGrid = mapSystem.CreateGridEntity(mapId);
                var secondGrid = mapSystem.CreateGridEntity(mapId);
                foreach (var gridUid in new[] { firstGrid.Owner, secondGrid.Owner })
                foreach (var x in new[] { 0, 1, 2 })
                    Paint(gridUid, new((float) x + 0.5f, 0.5f));
                var first = snapshotSystem.Snapshot(firstGrid.Owner, "StableAuthoredHost");
                var second = snapshotSystem.Snapshot(secondGrid.Owner, "StableAuthoredHost");
                Assert.That(first.Status, Is.EqualTo(KsProcgenStatus.Success));
                Assert.That(second.SnapshotHash, Is.EqualTo(first.SnapshotHash));
                Assert.That(second.Blobs.Single().Id, Is.EqualTo(first.Blobs.Single().Id));
                profilePrototype.Limits.MaxCells = 1;
                Assert.That(snapshotSystem.Snapshot(firstGrid.Owner, "StableAuthoredHost").Issue?.Code, Is.EqualTo("AreaProfileCellBudget"));
                Assert.That(first.Blobs.Single().Profile.MaxCells, Is.EqualTo(previousMaximum));
                Assert.That(first.Blobs.Single().Cells.Count, Is.EqualTo(3));
            }
            finally
            {
                profilePrototype.Limits.MaxCells = previousMaximum;
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task NumberedAndGenericEntrancesNormalizeIdenticallyOnPausedRotatedGrids()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                for (var number = 1; number <= 10; number++)
                {
                    var numberText = number.ToString(CultureInfo.InvariantCulture);
                    var aliasUid = SEntMan.SpawnEntity($"KsProcgenEntrance{numberText}", new EntityCoordinates(mapUid, Vector2.Zero));
                    Assert.That(SEntMan.GetComponent<KsProcgenEntranceComponent>(aliasUid).PortId, Is.EqualTo(numberText));
                }
                var snapshots = new List<KsProcgenAreaNormalization>();
                foreach (var prototypeId in new[] { "KsProcgenEntrance", "KsProcgenEntrance1" })
                {
                    var grid = mapSystem.CreateGridEntity(mapId);
                    transformSystem.SetLocalRotation(grid.Owner, Angle.FromDegrees(73.0));
                    Paint(grid.Owner, new(0.5f, 0.5f));
                    Paint(grid.Owner, new(0.5f, 1.5f));
                    var entranceUid = Entrance(grid.Owner, new(-1, 0), prototypeId: prototypeId);
                    var entranceComponent = SEntMan.GetComponent<KsProcgenEntranceComponent>(entranceUid);
                    entranceComponent.PortId = "1";
                    entranceComponent.InwardNormal = new(1, 0);
                    entranceComponent.ThresholdOffsets = [new(0, 1), new(0, 0)];
                    transformSystem.SetLocalRotation(entranceUid, Angle.FromDegrees(180.0));
                    var before = SEntMan.GetEntities().ToHashSet();
                    var snapshot = snapshotSystem.Snapshot(grid.Owner, "SameAuthoredHost");
                    Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                    Assert.That(snapshot.Entrances.Single().InsideApproach, Is.EqualTo(new Vector2i[] { new(0, 0), new(0, 1) }));
                    Assert.That(snapshot.Entrances.Single().OutsideApproach, Is.EqualTo(new Vector2i[] { new(-2, 0), new(-2, 1) }));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                    Assert.That(SEntMan.HasComponent<KsProcgenEntranceComponent>(entranceUid), Is.True);
                    var request = snapshot.Blobs.Single().CreateRequest(1);
                    Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
                    Assert.That(shape!.Entrances.Single().PortId, Is.EqualTo("1"));
                    Assert.That(KsProcgenPackingPlanner.Plan(request, []).Issue?.Code, Is.EqualTo("UnsupportedEntranceAwarePacking"));
                    entranceComponent.ThresholdOffsets.Clear();
                    Assert.That(snapshot.Entrances.Single().ThresholdCells.Count, Is.EqualTo(2));
                    snapshots.Add(snapshot);
                }
                Assert.That(snapshots[1].SnapshotHash, Is.EqualTo(snapshots[0].SnapshotHash));
                Assert.That(snapshots[1].Blobs.Single().Id, Is.EqualTo(snapshots[0].Blobs.Single().Id));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task ExplicitBindingAuthorsVoidGridMetadataAndRejectsInvalidActionsWithoutMutation()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                var markerUid = SEntMan.SpawnEntity("KsProcgenAreaCell", new EntityCoordinates(mapUid, Vector2.Zero));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host").Status, Is.EqualTo(KsProcgenStatus.NoOp));
                var before = SEntMan.GetEntities().ToHashSet();
                Assert.That(snapshotSystem.BindMarkerToGrid(grid.Owner, markerUid, new(-4, 2)), Is.Null);
                var transformComponent = SEntMan.GetComponent<TransformComponent>(markerUid);
                Assert.That(transformComponent.ParentUid, Is.EqualTo(grid.Owner));
                Assert.That(transformComponent.LocalPosition, Is.EqualTo(new Vector2(-3.5f, 2.5f)));
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "Host");
                Assert.That(snapshot.Blobs.Single().Cells, Is.EqualTo(new Vector2i[] { new(-4, 2) }));
                Assert.That(snapshotSystem.BindMarkerToGrid(grid.Owner, markerUid, new(int.MaxValue, 0))?.Code, Is.EqualTo("InvalidAreaMarkerBinding"));
                Assert.That(snapshotSystem.BindMarkerToGrid(mapUid, markerUid, new(0, 0))?.Code, Is.EqualTo("InvalidAreaMarkerBinding"));
                Assert.That(snapshotSystem.BindMarkerToGrid(grid.Owner, grid.Owner, new(0, 0))?.Code, Is.EqualTo("InvalidAreaMarkerBinding"));
                Assert.That(transformComponent.LocalPosition, Is.EqualTo(new Vector2(-3.5f, 2.5f)));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                SEntMan.QueueDeleteEntity(markerUid);
                Assert.That(snapshotSystem.BindMarkerToGrid(grid.Owner, markerUid, new(0, 0))?.Code, Is.EqualTo("InvalidAreaMarkerBinding"));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host").Issue?.Code, Is.EqualTo("AreaMarkerLifecycleInvalid"));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task LoadedEntranceFailuresAndSharedScanBudgetLeaveSourcesIntact()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                Paint(grid.Owner, new(0.5f, 0.5f));
                var markerUid = Entrance(grid.Owner, new(-1, 0));
                var entranceComponent = SEntMan.GetComponent<KsProcgenEntranceComponent>(markerUid);
                entranceComponent.InwardNormal = new(1, 0);
                var before = SEntMan.GetEntities().ToHashSet();
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host", maximumEntrances: 0).Issue?.Code, Is.EqualTo("EntranceMarkerBudget"));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host", maximumScannedMarkers: 1).Issue?.Code, Is.EqualTo("AreaSnapshotScanBudget"));
                entranceComponent.ThresholdOffsets = [new(0, 0), new(0, 1)];
                var orphan = snapshotSystem.Snapshot(grid.Owner, "Host");
                Assert.That(orphan.Issue?.Code, Is.EqualTo("OrphanEntranceMarker"));
                Assert.That(orphan.Blobs, Is.Empty);
                Assert.That(orphan.Entrances, Is.Empty);
                Assert.That(orphan.SnapshotHash, Is.Zero);
                entranceComponent.ThresholdOffsets = [new(int.MinValue, 0)];
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host").Issue?.Code, Is.EqualTo("InvalidEntranceSpan"));
                entranceComponent.ThresholdOffsets = [new(0, 0)];
                entranceComponent.BlobId = "Wrong";
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host").Issue?.Code, Is.EqualTo("EntranceOwnerMismatch"));
                entranceComponent.BlobId = null;
                transformSystem.SetCoordinates(markerUid, new EntityCoordinates(grid.Owner, new Vector2(-0.25f, 0.5f)));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host").Issue?.Code, Is.EqualTo("EntranceMarkerNotTileAligned"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(SEntMan.EntityExists(markerUid), Is.True);
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task NestedGridChildrenUseExactHostCoordinatesAndQueuedGridsAreRejected()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                var parentUid = SEntMan.SpawnEntity(null, new EntityCoordinates(mapUid, Vector2.Zero));
                transformSystem.SetCoordinates(parentUid, new EntityCoordinates(grid.Owner, new Vector2(2f, 1f)));
                var markerUid = SEntMan.SpawnEntity("KsProcgenAreaCell", new EntityCoordinates(mapUid, Vector2.Zero));
                transformSystem.SetCoordinates(markerUid, new EntityCoordinates(parentUid, new Vector2(0.5f, 0.5f)));
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "Host");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                Assert.That(snapshot.Blobs.Single().Cells, Is.EqualTo(new Vector2i[] { new(2, 1) }));
                SEntMan.QueueDeleteEntity(grid.Owner);
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host").Issue?.Code, Is.EqualTo("InvalidAreaSnapshotInput"));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task LoadedConnectionProfilesBindWholeAlternativesAndMissingMembershipFailsAtomically()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                var profile = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new ProtoId<KsProcgenAreaProfilePrototype>("KsProcgenServiceArea"));
                var previousReference = profile.EntranceConnections;
                try
                {
                    for (var y = 0; y < 5; y++)
                    {
                        var paintUid = Paint(grid.Owner, new(0.5f, (float) y + 0.5f));
                        SEntMan.GetComponent<KsProcgenAreaCellComponent>(paintUid).Profile = "KsProcgenServiceArea";
                    }
                    var entranceUids = new List<EntityUid>();
                    var labels = new[] { "1", "3", "4", "9", "8" };
                    for (var y = 0; y < labels.Length; y++)
                    {
                        var entranceUid = Entrance(grid.Owner, new(-1, y));
                        var entranceComponent = SEntMan.GetComponent<KsProcgenEntranceComponent>(entranceUid);
                        entranceComponent.PortId = labels[y];
                        entranceComponent.InwardNormal = new(1, 0);
                        entranceUids.Add(entranceUid);
                    }
                    var before = SEntMan.GetEntities().ToHashSet();
                    var snapshot = snapshotSystem.Snapshot(grid.Owner, "Host");
                    Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                    var contract = snapshot.Blobs.Single().ConnectionContract!;
                    Assert.That(contract.Configurations.Count, Is.EqualTo(2));
                    Assert.That(contract.Configurations.Single(configuration => configuration.Id == "ServiceAndStorage")
                        .Groups.Single(group => group.Ports.Contains("1")).Ports, Is.EqualTo(new[] { "1", "3", "4" }));
                    Assert.That(contract.Configurations.Single(configuration => configuration.Id == "CrossService")
                        .Groups.Single(group => group.Ports.Contains("1")).Ports, Is.EqualTo(new[] { "1", "8" }));
                    var request = snapshot.Blobs.Single().CreateRequest(19);
                    Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
                    Assert.That(shape!.EntranceContract!.ContractHash, Is.EqualTo(contract.ContractHash));
                    var scene = new KsProcgenEntranceScene(shape.TargetCells.ToHashSet(),
                        Enumerable.Range(-2, 2).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
                        new HashSet<Vector2i>(), true);
                    var selection = KsProcgenEntranceSceneSelector.Select(request, contract.Configurations.Select(configuration =>
                        new KsProcgenEntranceSceneCandidate(configuration.Id, scene)).ToArray());
                    Assert.That(selection.Issue?.Code, Is.EqualTo("NoFeasibleEntranceScene"), "A one-cell-wide strip cannot isolate adjacent inside landings.");
                    Assert.That(selection.Accepted, Is.Null);
                    Assert.That(selection.SelectionHash, Is.Zero);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                    profile.EntranceConnections = "MissingConnectionProfile";
                    Assert.That(snapshotSystem.Snapshot(grid.Owner, "Host").Issue?.Code, Is.EqualTo("InvalidEntranceConnectionProfile"));
                    Assert.That(snapshot.Blobs.Single().Profile.EntranceConnections, Is.EqualTo(previousReference));
                    profile.EntranceConnections = previousReference;
                    SEntMan.GetComponent<KsProcgenEntranceComponent>(entranceUids[4]).PortId = "Unknown";
                    var failed = snapshotSystem.Snapshot(grid.Owner, "Host");
                    Assert.That(failed.Issue?.Code, Is.EqualTo("InvalidEntranceMembership"));
                    Assert.That(failed.Blobs, Is.Empty);
                    Assert.That(failed.Entrances, Is.Empty);
                    Assert.That(failed.SnapshotHash, Is.Zero);
                    Assert.That(snapshot.Blobs.Single().ConnectionContract!.ContractHash, Is.EqualTo(contract.ContractHash));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                }
                finally
                {
                    profile.EntranceConnections = previousReference;
                }
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task LoadedGeometryPipelineRejectsEntranceDeclarationsBeforeAllocation()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var request = new KsProcgenRequest
            {
                RequestId = "DeclaredPreview", ConnectivityPolicy = KsProcgenConnectivityPolicy.DeclaredNetworks,
                Shape = new() { Cells = [new(0, 0), new(1, 0), new(2, 0)] }, EntranceDomain = new()
                {
                    Entrances = [new() { PortId = "Entry", ThresholdCells = [new(-1, 0)], InwardNormal = new(1, 0) }],
                    Configurations = [new() { Id = "Only", Groups = [new() { Id = "Room", Ports = ["Entry"], RootCells = [new(2, 0)] }] }],
                },
            };
            Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
            Assert.That(shape!.EntranceContract!.Configurations.Single().Groups.Single().RootCells, Is.EqualTo(new Vector2i[] { new(2, 0) }));
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var pipeline = KsProcgenGeometryPipeline.Plan(manager, request, [], "KsProcgenSimpleOffice");
            Assert.That(pipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.InvalidInput));
            Assert.That(pipeline.Issue?.Code, Is.EqualTo("UnsupportedEntranceAwarePacking"));
            Assert.That(Pair.Server.System<KsProcgenTileStageSystem>().TryStage(pipeline).Stage, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task MarkerAuthoredIrregularBlobRetriesRoutingOrderWithoutChangingTheSourceMap()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                var holes = new HashSet<Vector2i> { new(2, 3), new(2, 4), new(3, 5), new(3, 6), new(4, 4), new(5, 5) };
                for (var x = 0; x < 7; x++)
                for (var y = 0; y < 7; y++)
                    if (!holes.Contains(new(x, y)))
                        Paint(grid.Owner, new((float) x + 0.5f, (float) y + 0.5f));
                foreach (var (id, cell, normal) in new[] { ("A1", new Vector2i(-1, 1), new Vector2i(1, 0)),
                             ("A2", new Vector2i(7, 1), new Vector2i(-1, 0)), ("B1", new Vector2i(-1, 5), new Vector2i(1, 0)),
                             ("B2", new Vector2i(7, 5), new Vector2i(-1, 0)) })
                {
                    var entranceUid = Entrance(grid.Owner, cell);
                    var entranceComponent = SEntMan.GetComponent<KsProcgenEntranceComponent>(entranceUid);
                    entranceComponent.PortId = id;
                    entranceComponent.InwardNormal = normal;
                }
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "OrderSearch");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var request = snapshot.Blobs.Single().CreateRequest(3);
                request.ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland;
                request.EntranceDomain!.Configurations = new List<KsProcgenEntranceConfigurationSpec>
                    { new() { Id = "Separate", Groups = new List<KsProcgenEntranceGroupSpec>
                        { new() { Id = "A", Ports = new List<string> { "A1", "A2" } },
                            new() { Id = "B", Ports = new List<string> { "B1", "B2" } } } } };
                var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                    request.EntranceDomain.Entrances.SelectMany(port => port.ThresholdCells
                        .SelectMany(cell => new[] { cell, cell - port.InwardNormal })).ToHashSet(), new HashSet<Vector2i>(), true, true);
                var before = SEntMan.GetEntities().ToHashSet();
                Assert.That(KsProcgenGroupRoutePlanner.Plan(request, "Separate", context).Status,
                    Is.EqualTo(KsProcgenEntranceSceneStatus.Rejected));
                var selection = KsProcgenGroupRoutePlanner.Select(request, context);
                Assert.That(selection.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), selection.Issue?.Code);
                Assert.That(selection.GroupOrderProbes, Is.EqualTo(2));
                Assert.That(selection.Accepted!.GroupOrder, Is.EqualTo(new[] { "B", "A" }));
                Assert.That(selection.Accepted.Analysis!.Components.Count, Is.EqualTo(2));
                Assert.That(selection.Accepted.FloorCells.Count, Is.EqualTo(22));
                Assert.That(selection.Accepted.FloorCells.Intersect(holes), Is.Empty);
                Assert.That(selection.Accepted.EngineAccessVerified, Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "OrderSearch").SnapshotHash, Is.EqualTo(snapshot.SnapshotHash));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task MarkerAuthoredNetworksRetainTheirRoutesThroughThemesAndFurnishing()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                for (var x = 0; x < 8; x++)
                for (var y = 0; y < 7; y++)
                    Paint(grid.Owner, new((float) x + 0.5f, (float) y + 0.5f));
                foreach (var (label, cell, normal) in new[]
                {
                    ("A1", new Vector2i(-1, 0), new Vector2i(1, 0)), ("A2", new Vector2i(8, 0), new Vector2i(-1, 0)),
                    ("B1", new Vector2i(-1, 6), new Vector2i(1, 0)), ("B2", new Vector2i(8, 6), new Vector2i(-1, 0)),
                })
                {
                    var entranceUid = Entrance(grid.Owner, cell);
                    var entranceComponent = SEntMan.GetComponent<KsProcgenEntranceComponent>(entranceUid);
                    entranceComponent.PortId = label;
                    entranceComponent.InwardNormal = normal;
                }
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "OwnedInterior");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var request = snapshot.Blobs.Single().CreateRequest(23);
                request.ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland;
                request.EntranceDomain!.Configurations = new List<KsProcgenEntranceConfigurationSpec>
                {
                    new() { Id = "Separate", Groups = new List<KsProcgenEntranceGroupSpec>
                    {
                        new() { Id = "A", Ports = new List<string> { "A1", "A2" }, RootCells = new List<Vector2i> { new(4, 1) } },
                        new() { Id = "B", Ports = new List<string> { "B1", "B2" }, RootCells = new List<Vector2i> { new(4, 5) } },
                    } },
                };
                var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                    request.EntranceDomain.Entrances.SelectMany(port => port.ThresholdCells
                        .SelectMany(cell => new[] { cell, cell - port.InwardNormal })).ToHashSet(), new HashSet<Vector2i>(), true, true);
                var before = SEntMan.GetEntities().ToHashSet();
                var floor = KsProcgenGroupFloorPlanner.Select(request, context).Accepted!;
                Assert.That(floor, Is.Not.Null);
                var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64);
                Assert.That(partition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), partition.Issue?.Code);
                var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
                var themes = KsProcgenGroupThemePlanner.Plan(manager, "KsProcgenSimpleOffice", request.Seed, partition);
                Assert.That(themes.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected), themes.Issue?.Code);
                Assert.That(themes.Regions.Count, Is.EqualTo(2));
                Assert.That(themes.Regions.SelectMany(region => region.ProtectedPassageCells), Is.EquivalentTo(partition.ProtectedCells));
                var ownedMaterials = KsProcgenGroupMaterialPlanner.Plan(manager, request, partition, themes);
                Assert.That(ownedMaterials.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), ownedMaterials.Issue?.Code);
                Assert.That(ownedMaterials.Tiles.Select(tile => tile.Cell), Is.EquivalentTo(partition.FloorCells));
                Assert.That(ownedMaterials.Walls.Select(wall => wall.Cell), Is.EquivalentTo(partition.WallCells));
                Assert.That(ownedMaterials.Tiles.Where(tile => partition.ProtectedCells.Contains(tile.Cell))
                    .All(tile => !tile.Accent), Is.True);
                Assert.That(ownedMaterials.EngineMaterialsVerified, Is.False);
                Assert.That(ownedMaterials.GasClosureVerified, Is.False);
                var hull = KsProcgenGroupHullWallPlanner.Plan(manager, request, partition, themes);
                Assert.That(hull.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), hull.Issue?.Code);
                Assert.That(hull.InteriorMaterials!.MaterialHash, Is.EqualTo(ownedMaterials.MaterialHash));
                Assert.That(hull.HullWalls, Is.Empty, "Painted floor markers do not authorize an exterior envelope.");
                Assert.That(hull.Boundary!.Edges.Count(edge => edge.Kind == KsProcgenOwnedBoundaryKind.OperationalEntrance),
                    Is.EqualTo(4));
                Assert.That(hull.Boundary.Edges.Any(edge => edge.Kind == KsProcgenOwnedBoundaryKind.MissingFootprintHull), Is.True);
                Assert.That(hull.HasUnresolvedBoundary, Is.True);
                Assert.That(hull.GasClosureVerified, Is.False);
                var windows = KsProcgenGroupWindowPlanner.Plan(manager, request, partition, themes, []);
                Assert.That(windows.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned), windows.Issue?.Code);
                Assert.That(windows.SourceHull!.WallProposalHash, Is.EqualTo(hull.WallProposalHash));
                Assert.That(windows.Selection!.Status, Is.EqualTo(KsProcgenWindowPlanStatus.NotApplicable));
                Assert.That(windows.Windows, Is.Empty);
                Assert.That(windows.Boundary!.ExteriorScopeComplete, Is.False);
                Assert.That(windows.WindowGoalVerified, Is.False);
                var mixedZones = floor.Regions.Select(network => new KsProcgenZone(network.Id + "/Mixed",
                    network.GroupIds.Contains("A") ? KsProcgenZoneKind.RoomProposal : KsProcgenZoneKind.Passage,
                    network.FloorCells)).ToArray();
                var mixedPartition = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: mixedZones);
                Assert.That(mixedPartition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
                var rejectedThemes = KsProcgenGroupThemePlanner.Plan(manager, "KsProcgenMandatoryOfficeFixture", request.Seed, mixedPartition);
                Assert.That(rejectedThemes.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.ThemeRejected));
                Assert.That(rejectedThemes.Issue?.Code, Is.EqualTo("MandatoryPackInPassage"));
                Assert.That(rejectedThemes.Regions, Is.Empty, "A later network failure discards earlier theme choices.");
                var furnishedRegions = new List<KsProcgenFurnishedRegion>();
                var litRegions = new List<KsProcgenLitRegion>();
                foreach (var region in themes.Regions)
                {
                    Assert.That(region.EntranceGroupIds.Count, Is.EqualTo(1));
                    Assert.That(region.ProtectedPassageCells.Count, Is.GreaterThan(2));
                    var owned = partition.Regions.Single(network => network.GroupIds.SequenceEqual(region.EntranceGroupIds));
                    var furnished = KsProcgenFurnishingPlanner.Plan(manager, region, owned.Partition, request.Seed,
                        inspectedWalls: partition.WallCells.ToHashSet());
                    Assert.That(furnished.Status, Is.AnyOf(KsProcgenFurnishingStatus.Proposed, KsProcgenFurnishingStatus.Sparse), furnished.Issue?.Code);
                    Assert.That(furnished.Entities, Is.Not.Empty);
                    Assert.That(furnished.ProtectedPassageCells, Is.SupersetOf(region.ProtectedPassageCells));
                    var blockers = furnished.Entities.Where(entity => entity.Movement != KsProcgenMovementClass.Clear)
                        .SelectMany(entity => entity.OccupiedCells).ToHashSet();
                    Assert.That(blockers.Intersect(region.ProtectedPassageCells), Is.Empty);
                    var root = region.ProtectedPassageCells[0];
                    foreach (var terminal in region.ProtectedPassageCells)
                        Assert.That(KsProcgenTraversal.FindCleanPath(root, terminal, region.FloorCells.ToHashSet(), blockers), Is.Not.Empty);
                    var invalidRegion = region with { ProtectedPassageCells = new Vector2i[] { new(500, 500) } };
                    var invalid = KsProcgenFurnishingPlanner.Plan(manager, invalidRegion, owned.Partition, request.Seed);
                    Assert.That(invalid.Status, Is.EqualTo(KsProcgenFurnishingStatus.InvalidInput));
                    Assert.That(invalid.Entities, Is.Empty);
                    var lights = KsProcgenLightingPlanner.Plan(manager, region, furnished, request.Seed);
                    Assert.That(lights.Status, Is.AnyOf(KsProcgenLightingPlanStatus.Proposed, KsProcgenLightingPlanStatus.Sparse));
                    Assert.That(lights.WorkingCoverageVerified, Is.False);
                    Assert.That(lights.Lights.All(light => !furnished.ProtectedPassageCells.Contains(light.Cell)), Is.True);
                    Assert.That(lights.ExpandedCells, Is.GreaterThan(0));
                    var exactLights = KsProcgenLightingPlanner.Plan(manager, region, furnished, request.Seed,
                        maxExpandedCells: lights.ExpandedCells);
                    Assert.That(exactLights.Lights, Is.EqualTo(lights.Lights));
                    var limitedLights = KsProcgenLightingPlanner.Plan(manager, region, furnished, request.Seed,
                        maxExpandedCells: lights.ExpandedCells - 1);
                    Assert.That(limitedLights.Status, Is.EqualTo(KsProcgenLightingPlanStatus.BudgetExceeded));
                    Assert.That(limitedLights.Issue?.Code, Is.EqualTo("LightingExpansionBudget"));
                    Assert.That(limitedLights.Lights, Is.Empty);
                    Assert.That(limitedLights.ExpandedCells, Is.EqualTo(lights.ExpandedCells - 1));
                    var missingReservations = new KsProcgenFurnishingResult
                    {
                        Status = furnished.Status, Entities = furnished.Entities,
                    };
                    Assert.That(KsProcgenLightingPlanner.Plan(manager, region, missingReservations, request.Seed).Status,
                        Is.EqualTo(KsProcgenLightingPlanStatus.InvalidInput));
                    furnishedRegions.Add(new(region.Id, furnished));
                    litRegions.Add(new(region.Id, lights));
                }
                var aggregateLights = KsProcgenGroupLightingPlanner.Plan(manager, themes, furnishedRegions, request.Seed);
                Assert.That(aggregateLights.Status, Is.AnyOf(KsProcgenLightingPlanStatus.Proposed, KsProcgenLightingPlanStatus.Sparse));
                Assert.That(aggregateLights.FixtureCount, Is.EqualTo(litRegions.Sum(region => region.Proposal.Lights.Count)));
                Assert.That(aggregateLights.ExpandedCells, Is.EqualTo(litRegions.Sum(region => region.Proposal.ExpandedCells)));
                Assert.That(aggregateLights.FixtureCount, Is.GreaterThan(1));
                var limitedAggregate = KsProcgenGroupLightingPlanner.Plan(manager, themes, furnishedRegions, request.Seed,
                    maximumExpandedCells: aggregateLights.ExpandedCells - 1);
                Assert.That(limitedAggregate.Status, Is.EqualTo(KsProcgenLightingPlanStatus.BudgetExceeded));
                Assert.That(limitedAggregate.Regions, Is.Empty);
                Assert.That(limitedAggregate.ExpandedCells, Is.EqualTo(aggregateLights.ExpandedCells - 1));
                var fixtureLimited = KsProcgenGroupLightingPlanner.Plan(manager, themes, furnishedRegions, request.Seed, maximumFixtures: 1);
                Assert.That(fixtureLimited.Status, Is.EqualTo(KsProcgenLightingPlanStatus.BudgetExceeded));
                Assert.That(fixtureLimited.Regions, Is.Empty);
                Assert.That(fixtureLimited.FixtureCount, Is.LessThanOrEqualTo(1));
                var replayLights = KsProcgenGroupLightingPlanner.Plan(manager, themes, furnishedRegions.AsEnumerable().Reverse().ToArray(),
                    request.Seed, maximumFixtures: aggregateLights.FixtureCount, maximumExpandedCells: aggregateLights.ExpandedCells);
                Assert.That(replayLights.Regions.SelectMany(region => region.Proposal.Lights),
                    Is.EqualTo(aggregateLights.Regions.SelectMany(region => region.Proposal.Lights)));
                var access = KsProcgenGroupInteriorAnalyzer.Analyze(request, partition, themes, furnishedRegions, aggregateLights.Regions);
                Assert.That(access.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), access.Issue?.Code);
                Assert.That(access.Analysis!.Components.Count, Is.EqualTo(2));
                Assert.That(access.BlockedCells, Is.SupersetOf(litRegions.SelectMany(region => region.Proposal.Lights).Select(light => light.Cell)));
                Assert.That(access.EngineAccessVerified, Is.False);
                var tacticalPairs = new KsProcgenRoutePair[]
                {
                    new("A", new(0, 0), new(7, 0)), new("B", new(0, 6), new(7, 6)),
                };
                var nominatedChokes = new HashSet<Vector2i> { new(4, 1), new(4, 5) };
                var tactical = KsProcgenGroupTacticalAnalyzer.Analyze(request, access, tacticalPairs, nominatedChokes,
                    measureRedundancy: true);
                Assert.That(tactical.Status, Is.AnyOf(KsProcgenTacticalStatus.Complete, KsProcgenTacticalStatus.DetailsTruncated),
                    tactical.Issue?.Code);
                Assert.That(tactical.Networks.Count, Is.EqualTo(2));
                Assert.That(tactical.Networks.All(network => network.AlternateRoutes.Pairs.Count == 1 &&
                    network.Redundancy!.Pairs.Count == 1 && network.GroupIds.Count == 1), Is.True);
                Assert.That(tactical.Networks.SelectMany(network => network.RequiredTerminals),
                    Is.SupersetOf(nominatedChokes));
                Assert.That(tactical.EngineTraversalVerified, Is.False);
                Assert.That(tactical.MeasurementScope, Is.EqualTo(KsProcgenEntranceIsolationScope.DomainLocal));
                var replayTactical = KsProcgenGroupTacticalAnalyzer.Analyze(request, access, tacticalPairs.Reverse().ToArray(),
                    nominatedChokes, measureRedundancy: true, maximumExpandedCells: tactical.ExpandedCells,
                    maximumSearches: tactical.Searches, maximumExaminedFlowEdges: tactical.ExaminedFlowEdges);
                Assert.That(replayTactical.MeasurementHash, Is.EqualTo(tactical.MeasurementHash));
                var foreignTactical = KsProcgenGroupTacticalAnalyzer.Analyze(request, access,
                    new KsProcgenRoutePair[] { new("Foreign", new(0, 0), new(7, 6)) }, nominatedChokes);
                Assert.That(foreignTactical.Issue?.Code, Is.EqualTo("CrossNetworkTacticalPair"));
                Assert.That(foreignTactical.Networks, Is.Empty);
                Assert.That(foreignTactical.MeasurementHash, Is.Zero);
                Assert.That(partition.EngineAccessVerified, Is.False);
                var complete = KsProcgenOwnedInteriorPlanner.Plan(manager, request, "KsProcgenSimpleOffice", context, [],
                    budgets: new KsProcgenOwnedPlanBudgets { PreferredMaximumRoomCells = 64 });
                Assert.That(complete.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), complete.Issue?.Code);
                Assert.That(complete.Partition!.PartitionHash, Is.EqualTo(partition.PartitionHash));
                Assert.That(complete.Access!.AccessHash, Is.EqualTo(access.AccessHash));
                Assert.That(complete.Furnishings.SelectMany(room => room.Proposal.Entities),
                    Is.EqualTo(furnishedRegions.SelectMany(room => room.Proposal.Entities)));
                Assert.That(complete.Published, Is.False);
                Assert.That(KsProcgenGeometryPipeline.Plan(manager, request, [], "KsProcgenSimpleOffice").Issue?.Code,
                    Is.EqualTo("UnsupportedEntranceAwarePacking"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "OwnedInterior").SnapshotHash, Is.EqualTo(snapshot.SnapshotHash));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task MarkerAuthoredRequestGrowsOwnedFloorWithoutChangingTheSourceMap()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                for (var x = 0; x < 5; x++)
                for (var y = 0; y < 5; y++)
                    Paint(grid.Owner, new((float) x + 0.5f, (float) y + 0.5f));
                foreach (var (label, y) in new[] { ("A1", 0), ("B1", 4) })
                {
                    var entranceUid = Entrance(grid.Owner, new(-1, y));
                    var entranceComponent = SEntMan.GetComponent<KsProcgenEntranceComponent>(entranceUid);
                    entranceComponent.PortId = label;
                    entranceComponent.InwardNormal = new(1, 0);
                }
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "FloorGrowth");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var request = snapshot.Blobs.Single().CreateRequest(23);
                request.ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland;
                request.EntranceDomain!.Configurations = new List<KsProcgenEntranceConfigurationSpec>
                {
                    new() { Id = "Separate", Groups = new List<KsProcgenEntranceGroupSpec>
                    {
                        new() { Id = "A", Ports = new List<string> { "A1" } },
                        new() { Id = "B", Ports = new List<string> { "B1" } },
                    } },
                };
                var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                    new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(-1, 4), new(-2, 4) },
                    new HashSet<Vector2i>(), true, true);
                var before = SEntMan.GetEntities().ToHashSet();
                var selection = KsProcgenGroupFloorPlanner.Select(request, context);
                Assert.That(selection.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), selection.Issue?.Code);
                var floor = selection.Accepted!;
                Assert.That(floor.FloorCells.Count, Is.EqualTo(20));
                Assert.That(floor.WallCells, Is.EqualTo(Enumerable.Range(0, 5).Select(x => new Vector2i(x, 2))));
                Assert.That(floor.Regions.Count, Is.EqualTo(2));
                Assert.That(floor.Regions.All(region => region.FloorCells.Count == 10 && region.ProtectedCells.Count == 1), Is.True);
                Assert.That(floor.ProtectedCells.Count, Is.EqualTo(2));
                Assert.That(floor.Analysis!.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
                Assert.That(floor.EngineAccessVerified, Is.False);
                Assert.That(KsProcgenPackingPlanner.Plan(request, []).Issue?.Code, Is.EqualTo("UnsupportedEntranceAwarePacking"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "FloorGrowth").SnapshotHash, Is.EqualTo(snapshot.SnapshotHash));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task MarkerAuthoredRequestConstructsSeparatedRoutesWithoutChangingTheSourceMap()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                for (var y = 0; y < 5; y++)
                    Paint(grid.Owner, new(0.5f, (float) y + 0.5f));
                var labels = new[] { ("1", 0), ("3", 1), ("8", 3), ("9", 4) };
                foreach (var (label, y) in labels)
                {
                    var entranceUid = Entrance(grid.Owner, new(-1, y));
                    var entranceComponent = SEntMan.GetComponent<KsProcgenEntranceComponent>(entranceUid);
                    entranceComponent.PortId = label;
                    entranceComponent.InwardNormal = new(1, 0);
                }
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "Routes");
                var request = snapshot.Blobs.Single().CreateRequest(8);
                request.ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland;
                request.EntranceDomain!.Configurations = [new() { Id = "Separate", Groups =
                    [new() { Id = "Service", Ports = ["1", "3"] }, new() { Id = "Storage", Ports = ["8", "9"] }] }];
                var before = SEntMan.GetEntities().ToHashSet();
                var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                    Enumerable.Range(-2, 2).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
                    new HashSet<Vector2i>(), true, true);
                var proposal = KsProcgenGroupRoutePlanner.Select(request, context);
                Assert.That(proposal.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), proposal.Issue?.Code);
                Assert.That(proposal.Accepted!.FloorCells.Count, Is.EqualTo(4));
                Assert.That(proposal.Accepted.ProposedWallCells, Is.EqualTo(new Vector2i[] { new(0, 2) }));
                Assert.That(proposal.Accepted.Analysis!.Components.Count, Is.EqualTo(2));
                Assert.That(proposal.Accepted.EngineAccessVerified, Is.False);
                Assert.That(KsProcgenPackingPlanner.Plan(request, []).Issue?.Code, Is.EqualTo("UnsupportedEntranceAwarePacking"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                Assert.That(snapshotSystem.Snapshot(grid.Owner, "Routes").SnapshotHash, Is.EqualTo(snapshot.SnapshotHash));
            }
            finally
            {
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }
}
