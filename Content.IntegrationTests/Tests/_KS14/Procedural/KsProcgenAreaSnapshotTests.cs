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
            var profilePrototype = manager.Index<KsProcgenAreaProfilePrototype>("KsProcgenOfficeArea");
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
                var profile = Pair.Server.ResolveDependency<IPrototypeManager>().Index<KsProcgenAreaProfilePrototype>("KsProcgenServiceArea");
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
