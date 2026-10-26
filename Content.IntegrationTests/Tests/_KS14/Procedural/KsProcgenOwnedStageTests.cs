#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedStageSystem))]
public sealed class KsProcgenOwnedStageTests : KsProcgenGameTest
{
    [TestCase(1)] [TestCase(3)]
    public async Task TinyStandaloneRequestsMaterializeAndInitializeWithoutFakeEntrances(int length)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var maps = Pair.Server.System<SharedMapSystem>(); var request = Strip(length);
            var result = Generate(request);
            try
            {
                AssertPositioned(result);
                var stage = result.Stage!;
                Assert.That(request.EntranceDomain, Is.Null);
                Assert.That(stage.Request.CopyRequest().EntranceDomain!.Configurations.Single().Groups.Single().Ports, Is.Empty);
                Assert.That(maps.GetFilledTileCount((stage.GridUid, SEntMan.GetComponent<MapGridComponent>(stage.GridUid))), Is.EqualTo(length));
                Assert.That(stage.SpawnedEntityCount, Is.EqualTo(2));
                Assert.That(stages.Verify(stage), Is.True);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(stage), Is.True);
                Assert.That(maps.IsPaused(stage.MapId), Is.True);
                Assert.That(stage.EngineAccessVerified || stage.GasClosureVerified || stage.WorkingLightingVerified || stage.Published, Is.False);
                request.Shape.Cells.Clear();
                Assert.That(stage.Request.CopyRequest().Shape.Cells.Count, Is.EqualTo(length));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(2, true)] [TestCase(24, false)] [TestCase(24, true)]
    public async Task IndependentRoomsKeepNestedSupportsAndRespectInitializationBudgets(int roomCount, bool lighting)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var request = Rooms(two: false);
            var roomCells = request.Shape.Cells.ToArray();
            request.Shape.Cells = Enumerable.Range(0, roomCount).SelectMany(index => roomCells.Select(cell => cell + new Vector2i(12 * index, 0))).ToList();
            var catalog = KsProcgenOwnedLayeredTests.Catalog(Pair.Server.ResolveDependency<IPrototypeManager>(), contained: true, lighting: lighting);
            var result = stages.TryGenerate(request, "Layered", planningBudgets: Planning() with
                { MaximumReportedGraphExpansions = 1_000_000, MaximumFurnishingProbes = 65_536, MaximumLightingFixtures = 128 }, content: catalog);
            try
            {
                AssertPositioned(result);
                var stage = result.Stage!; var proposal = result.Proposal!;
                Assert.That(stage.Assemblies.Count, Is.EqualTo(roomCount));
                Assert.That(stage.Assemblies.Sum(members => members.Count), Is.EqualTo(3 * roomCount));
                var containers = Pair.Server.System<SharedContainerSystem>();
                for (var index = 0; index < stage.Assemblies.Count; index++)
                {
                    var members = stage.Assemblies[index];
                    var room = proposal.Furnishings.Where(region => region.Proposal.Layered != null).ElementAt(index).Proposal.Layered!;
                    foreach (var pose in room.Access.Poses!.Members)
                    {
                        var uid = members[pose.Support.MemberId]; var transform = SEntMan.GetComponent<TransformComponent>(uid);
                        Assert.That(transform.LocalPosition, Is.EqualTo(pose.LocalPosition));
                        Assert.That(transform.LocalRotation, Is.EqualTo(KsProcgenRotation.ToEngineAngle(pose.LocalQuarterTurns)));
                        Assert.That(transform.ParentUid, Is.EqualTo(pose.CoordinateParentMemberId is { } parent ? members[parent] : stage.GridUid));
                        if (pose.Support.Layer == KsProcgenPlacementLayer.Container)
                        {
                            Assert.That(containers.TryGetContainingContainer(uid, out var container), Is.True);
                            Assert.That(container!.ID, Is.EqualTo("microwave_entity_container"));
                        }
                    }
                }
                var minimum = 2 + stage.Entities.Count + 3 * roomCount;
                Assert.That(stage.SpawnedEntityCount, Is.GreaterThanOrEqualTo(minimum));
                var initialized = stages.TryInitialize(stage);
                if (roomCount == 24 && lighting)
                {
                    Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.BudgetExceeded), message: initialized.Issue?.Code);
                    Assert.That(initialized.Issue?.Code, Is.EqualTo("OwnedStageInitializationSpawnBudget"));
                    Assert.That(stage.Active, Is.False);
                    Assert.That(Pair.Server.System<SharedMapSystem>().MapExists(stage.MapId), Is.False);
                    Assert.That(stage.Assemblies.SelectMany(members => members.Values).Any(SEntMan.EntityExists), Is.False);
                }
                else
                {
                    Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
                    Assert.That(stages.Verify(stage), Is.True);
                }
                Assert.That(stage.Assemblies.SelectMany(members => members.Values).Distinct().Count(), Is.EqualTo(3 * roomCount));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task HullTilesAndEntitiesAreCompleteAndSceneIgnoresLaterAuthoringEdits()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var request = Rooms(two: false, envelope: true);
            var result = stages.TryGenerate(request, "KsProcgenSimpleOffice", planningBudgets: Planning());
            try
            {
                AssertPositioned(result);
                var stage = result.Stage!; var proposal = result.Proposal!; var maps = Pair.Server.System<SharedMapSystem>();
                var walls = proposal.Structure!.RemainingWalls;
                Assert.That(walls.Count, Is.GreaterThan(0));
                var grid = SEntMan.GetComponent<MapGridComponent>(stage.GridUid);
                foreach (var wall in walls)
                {
                    Assert.That(maps.GetTileRef(stage.GridUid, grid, wall.Cell).Tile.TypeId, Is.Not.Zero);
                    Assert.That(stage.Entities.Any(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == wall.EntityId &&
                        SEntMan.GetComponent<TransformComponent>(uid).LocalPosition == new Vector2(wall.Cell.X + 0.5f, wall.Cell.Y + 0.5f)), Is.True);
                }
                Assert.That(proposal.Lighting!.FixtureCount, Is.GreaterThan(0));
                Assert.That(stage.Entities.Count + stage.Assemblies.Sum(assembly => assembly.Count), Is.EqualTo(walls.Count + proposal.Structure.Windows.Count +
                    proposal.Structure.SourceHull!.InteriorMaterials!.Doors.Count +
                    proposal.Furnishings.Sum(room => room.Proposal.Layered?.Assembly.Members.Count ?? room.Proposal.Entities.Count) + proposal.Lighting.FixtureCount));
                request.Shape.Cells.Clear(); request.Shape.EnvelopeCells.Clear();
                var copy = stage.Request.CopyRequest(); copy.Shape.Cells.Clear();
                Assert.That(stages.Verify(stage), Is.True);
                var tile = proposal.Structure.SourceHull.InteriorMaterials.Tiles.First();
                maps.SetTile(stage.GridUid, grid, tile.Cell, Tile.Empty);
                Assert.That(stages.Verify(stage), Is.False);
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(initialized.Stage, Is.Null); Assert.That(stage.Active, Is.False);
                Assert.That(maps.MapExists(stage.MapId), Is.False);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task FreshExteriorAutomaticallySelectsAndMaterializesTheConfiguredWindowFraction()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var request = Rooms(two: false, envelope: true);
            request.WindowGoal = new() { ExteriorWindowFraction = 0.5f, HardFraction = true, ToleranceCells = 0 };
            var result = stages.TryGenerate(request, "KsProcgenSimpleOffice", planningBudgets: Planning());
            try
            {
                AssertPositioned(result); var stage = result.Stage!; var structure = result.Proposal!.Structure!;
                Assert.That(structure.Windows.Count, Is.EqualTo(14));
                Assert.That(structure.RemainingWalls.Count, Is.EqualTo(14));
                Assert.That(structure.Boundary!.Facts.Count, Is.EqualTo(28));
                Assert.That(structure.Boundary.Facts.All(fact => fact.FacesExterior && !fact.IsExistingWindow), Is.True);
                foreach (var window in structure.Windows)
                    Assert.That(stage.Entities.Any(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == window.EntityId &&
                        SEntMan.GetComponent<TransformComponent>(uid).LocalPosition == new Vector2(window.Cell.X + 0.5f, window.Cell.Y + 0.5f)), Is.True);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(stage), Is.True);
                Assert.That(stage.GasClosureVerified || structure.WindowGoalVerified, Is.False);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task PartitionDoorTransformsFollowTheirCardinalApproaches(bool verticalShape)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var request = Strip(1);
            request.Shape.Cells = Enumerable.Range(0, verticalShape ? 6 : 12)
                .SelectMany(x => Enumerable.Range(0, verticalShape ? 12 : 6).Select(y => new Vector2i(x, y))).ToList();
            var result = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture",
                planningBudgets: Planning() with { PreferredMaximumRoomCells = 16, MinimumRoomCells = 4 });
            try
            {
                AssertPositioned(result); var stage = result.Stage!;
                var materials = result.Proposal!.Structure!.SourceHull!.InteriorMaterials!;
                Assert.That(materials.Doors.Count, Is.GreaterThan(0));
                foreach (var door in materials.Doors)
                {
                    var opening = materials.DoorOpenings.Single(item => item.Threshold == door.Cell);
                    var uid = stage.Entities.Single(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == door.EntityId &&
                        SEntMan.GetComponent<TransformComponent>(uid).LocalPosition == new Vector2(door.Cell.X + 0.5f, door.Cell.Y + 0.5f));
                    Assert.That(SEntMan.GetComponent<TransformComponent>(uid).LocalRotation,
                        Is.EqualTo(Angle.FromDegrees(opening.InsideApproach.X == door.Cell.X ? 0 : -90)));
                }
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("host")] [TestCase("facts")] [TestCase("preserved")] [TestCase("incomplete")] [TestCase("null")]
    public async Task EmptyExteriorDeclarationCannotReplaceExistingHostContext(string conflict)
    {
        await AssertOnServer(() =>
        {
            var request = Strip(3); request.EntranceDomain = new();
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true) { ExteriorIsEmptySpace = true };
            IReadOnlyList<KsProcgenExteriorWindowFact> facts = [];
            switch (conflict)
            {
                case "host": context = context with { CleanHostCells = new HashSet<Vector2i> { new(-1, 0) } }; break;
                case "facts": facts = new[] { new KsProcgenExteriorWindowFact(new(-1, 0), new(-1, 0), true, true) }; break;
                case "preserved": request.Shape.PreservedCells.Add(new(0, 0)); break;
                case "incomplete": context = context with { HostContextComplete = false }; break;
                case "null": context = context with { ExistingCleanDomainCells = null! }; break;
            }
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "KsProcgenNoDoorThemeFixture", context, facts, budgets: Planning());
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
            Assert.That(result.Issue?.Code, Is.EqualTo(conflict == "null" ? "InvalidOwnedInteriorInput" : "ConflictingEmptyExteriorContext"));
            Assert.That(result.Structure, Is.Null);
        });
    }

    [TestCase("terrain")] [TestCase("entities")] [TestCase("work")]
    public async Task KnownInsufficientBudgetsFailBeforeAnyEngineAllocation(string budget)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>();
            var callbacks = 0;
            try
            {
                hook.TileAction = () => callbacks++;
                var limits = new KsProcgenOwnedStageBudgets();
                limits = budget switch { "terrain" => limits with { MaximumTerrainCells = 2 },
                    "entities" => limits with { MaximumSpawnedEntities = 1 }, _ => limits with { MaximumPlacementWork = 2 } };
                var result = Generate(Strip(3), budgets: limits);
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.BudgetExceeded), message: result.Issue?.Code);
                Assert.That(result.Stage, Is.Null); Assert.That(callbacks, Is.Zero);
                Assert.That(result.Proposal!.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate));
            }
            finally { hook.TileAction = null; Pair.Server.System<KsProcgenOwnedStageSystem>().DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task TileCallbacksShareCancellationAndCumulativeAllocationRollback(bool cancel)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>();
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>();
            using var cancellation = new CancellationTokenSource(); var allocatedUid = EntityUid.Invalid;
            try
            {
                hook.TileAction = () =>
                {
                    hook.TileAction = null; allocatedUid = hook.Allocate();
                    if (cancel) cancellation.Cancel(); else SEntMan.DeleteEntity(allocatedUid);
                };
                var result = Generate(Strip(3), budgets: new() { MaximumSpawnedEntities = 2 }, cancellationToken: cancellation.Token);
                Assert.That(allocatedUid, Is.Not.EqualTo(EntityUid.Invalid));
                Assert.That(result.Status, Is.EqualTo(cancel ? KsProcgenOwnedStageStatus.Cancelled : KsProcgenOwnedStageStatus.BudgetExceeded));
                Assert.That(result.Stage, Is.Null); Assert.That(stages.RetainedStageCount, Is.Zero);
                Assert.That(operations.Active, Is.False); Assert.That(operations.CapturedEntityCount, Is.Zero);
                Assert.That(SEntMan.EntityExists(allocatedUid), Is.False);
            }
            finally { hook.TileAction = null; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ForeignDescendantsAndEscapedOwnedMembersInvalidateTheWholeScene()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var maps = Pair.Server.System<SharedMapSystem>();
            var catalog = KsProcgenOwnedLayeredTests.Catalog(Pair.Server.ResolveDependency<IPrototypeManager>(), contained: true);
            var result = stages.TryGenerate(Rooms(two: false), "Layered", planningBudgets: Planning(), content: catalog);
            var outsideUid = maps.CreateMap(out var outsideId, runMapInit: false);
            var foreignUid = EntityUid.Invalid;
            try
            {
                AssertPositioned(result); var stage = result.Stage!;
                var memberUid = stage.Assemblies.Single().Values.First();
                foreignUid = SEntMan.SpawnEntity("Paper", new EntityCoordinates(memberUid, Vector2.Zero));
                Assert.That(stages.Verify(stage), Is.False);
                SEntMan.DeleteEntity(foreignUid);
                Assert.That(stages.Verify(stage), Is.True);
                Pair.Server.System<SharedTransformSystem>().SetCoordinates(memberUid, new EntityCoordinates(outsideUid, Vector2.Zero));
                Assert.That(stages.Verify(stage), Is.False);
                maps.DeleteMap(stage.MapId);
                Assert.That(stage.Active, Is.False); Assert.That(stages.RetainedStageCount, Is.Zero);
                Assert.That(SEntMan.EntityExists(memberUid), Is.False);
                Assert.That(maps.MapExists(outsideId), Is.True);
                Assert.That(stages.Discard(stage), Is.False);
            }
            finally
            {
                if (SEntMan.EntityExists(foreignUid)) SEntMan.DeleteEntity(foreignUid);
                stages.DiscardAll(); if (maps.MapExists(outsideId)) maps.DeleteMap(outsideId);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task InitializationFailureCleansLateAllocationsAndAllowsRecovery(bool cancel)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var probe = Pair.Server.System<KsProcgenLifecycleProbeSystem>(); var result = Generate(Strip(3));
            using var cancellation = new CancellationTokenSource();
            try
            {
                AssertPositioned(result); var stage = result.Stage!;
                SEntMan.AddComponent<KsProcgenLifecycleProbeComponent>(stage.GridUid);
                probe.OnInitialize = () =>
                {
                    Assert.That(stage.LifecyclePhase, Is.EqualTo(KsProcgenStageLifecyclePhase.Initializing));
                    Pair.Server.System<KsProcgenTileAllocationTestSystem>().Allocate(); if (cancel) cancellation.Cancel();
                };
                var initialized = stages.TryInitialize(stage, cancellationToken: cancellation.Token);
                Assert.That(initialized.Status, Is.EqualTo(cancel ? KsProcgenOwnedStageStatus.Cancelled : KsProcgenOwnedStageStatus.Rejected));
                Assert.That(stage.Active, Is.False); Assert.That(stages.RetainedStageCount, Is.Zero);
                Assert.That(Pair.Server.System<KsProcgenPreviewOperationSystem>().Active, Is.False);
                probe.OnInitialize = null;
                var recovery = Generate(Strip(1)); AssertPositioned(recovery);
                Assert.That(stages.TryInitialize(recovery.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
            }
            finally { probe.OnInitialize = null; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task HostAndPreservedContentRequireAuthoritativeCapture()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var request = Strip(3);
            request.Shape.PreservedCells.Add(new(0, 0));
            var result = Generate(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.UnsupportedContent), message: result.Issue?.Code);
            request = Strip(3);
            request.EntranceDomain = new() { Entrances = new() { new() { PortId = "Host", ThresholdCells = new() { new(-1, 0) }, InwardNormal = new(1, 0) } } };
            result = Generate(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.UnsupportedContent), message: result.Issue?.Code);
            Assert.That(result.Stage, Is.Null); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenOwnedStageResult Generate(KsProcgenRequest request, KsProcgenOwnedStageBudgets? budgets = null,
        CancellationToken cancellationToken = default) => Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(request,
        "KsProcgenNoDoorThemeFixture", planningBudgets: Planning(), stageBudgets: budgets, cancellationToken: cancellationToken);
    private static KsProcgenOwnedPlanBudgets Planning() => new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 };
    private static void AssertPositioned(KsProcgenOwnedStageResult result) =>
        Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
    private static KsProcgenRequest Strip(int length) => new()
    {
        RequestId = "OwnedStage", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, length).Select(x => new Vector2i(x, 0)).ToList() },
    };
    private static KsProcgenRequest Rooms(bool two = true, bool envelope = false)
    {
        var request = Strip(1);
        request.Shape.Cells = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7).Select(y => new Vector2i(x, y))).ToList();
        if (two) request.Shape.Cells.AddRange(request.Shape.Cells.Select(cell => cell + new Vector2i(12, 0)).ToArray());
        if (envelope)
        {
            var target = request.Shape.Cells.ToHashSet();
            request.Shape.EnvelopeCells = target.SelectMany(cell => new[] { cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
                cell + new Vector2i(0, 1), cell + new Vector2i(0, -1) }).Where(cell => !target.Contains(cell)).Distinct().ToList();
        }
        return request;
    }
}
