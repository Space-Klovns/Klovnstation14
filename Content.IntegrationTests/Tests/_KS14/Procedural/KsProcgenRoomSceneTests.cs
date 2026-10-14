#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenRoomFixtureSystem))]
public sealed class KsProcgenRoomSceneTests : GameTest
{
    [Test]
    public async Task OwnedTerrainInitializesVerifiesAndDiscardsEveryEntity()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var map = Pair.Server.System<SharedMapSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var room = Room(3, 1) with { Walls = new HashSet<Vector2i> { new(0, 1), new(1, 1), new(2, 1) } };
                var staged = Stage(room, entityId: "KsProcgenRoomObstacleFixture");
                Assert.That(staged.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), staged.Issue?.Code);
                var stage = staged.Stage!;
                Assert.That(stage.GridUid, Is.Not.Null);
                var gridUid = stage.GridUid!.Value;
                var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
                Assert.That(map.GetFilledTileCount((gridUid, grid)), Is.EqualTo(6));
                Assert.That(stage.Members.Values.All(uid => SEntMan.GetComponent<TransformComponent>(uid).ParentUid == gridUid), Is.True);
                ((HashSet<Vector2i>) room.Floor).Clear();
                ((HashSet<Vector2i>) room.Walls).Clear();
                Assert.That(stage.Terrain!.Floor.Count, Is.EqualTo(3));
                Assert.That(stage.Terrain.Walls.Count, Is.EqualTo(3));
                Assert.That(system.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                Assert.That(system.Verify(stage), Is.True);
                var observation = Pair.Server.System<KsProcgenRoomFixtureSystem>().Check(stage);
                Assert.That(observation.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), observation.Reason);
                var memberUid = stage.Members.Values.Single();
                var memberFixture = SEntMan.GetComponent<FixturesComponent>(memberUid).Fixtures["body"];
                var physics = Pair.Server.System<SharedPhysicsSystem>();
                physics.SetPosition(memberUid, "body", memberFixture, (PhysShapeCircle) memberFixture.Shape,
                    new Vector2(0f, 1f));
                Assert.That(Pair.Server.System<KsProcgenRoomFixtureSystem>().Check(stage).Reason,
                    Is.EqualTo("RoomHardFixtureOverlap"), "Members must not overlap the structural wall union.");
                physics.SetPosition(memberUid, "body", memberFixture, (PhysShapeCircle) memberFixture.Shape, Vector2.Zero);
                Assert.That(stage.PlacementVerified, Is.False);
                map.SetTile(gridUid, grid, new Vector2i(1, 0), Tile.Empty);
                Assert.That(system.Verify(stage), Is.False);
                Assert.That(Pair.Server.System<KsProcgenRoomFixtureSystem>().Check(stage).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.InvalidInput));
                Assert.That(system.Discard(stage), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { system.DiscardAll(); }
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task PreallocationTerrainFailuresLeaveNoEntities(int mode)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var room = Room(3, 1);
            if (mode == 0)
                room = room with { CellCenterOrigin = Vector2.Zero };
            var result = Stage(room, maxTerrainCells: mode == 1 ? 2 : 4096, maxSpawnedEntities: mode == 2 ? 2 : 256);
            Assert.That(result.Status, Is.EqualTo(mode == 0 ? KsProcgenAssemblyStageStatus.InvalidInput :
                KsProcgenAssemblyStageStatus.BudgetExceeded), result.Issue?.Code);
            Assert.That(result.Stage, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("ExtraTile")]
    [TestCase("MovedGrid")]
    [TestCase("MovedWall")]
    [TestCase("DeletedWall")]
    public async Task TerrainAndFrameTamperingInvalidateOwnedScene(string fault)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var map = Pair.Server.System<SharedMapSystem>();
            var transform = Pair.Server.System<SharedTransformSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var room = Room(3, 1) with { Walls = new HashSet<Vector2i> { new(1, 1) } };
                var stage = Stage(room).Stage!;
                Assert.That(stage, Is.Not.Null);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                var gridUid = stage.GridUid!.Value;
                if (fault == "ExtraTile")
                {
                    var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
                    map.SetTile(gridUid, grid, new Vector2i(30, 30), new Tile(stage.Terrain!.TileId));
                }
                else if (fault == "MovedGrid")
                    transform.SetLocalPosition(gridUid, new Vector2(1f, 0f));
                else
                {
                    var wall = SEntMan.GetEntities().Single(uid =>
                        SEntMan.TryGetComponent<MetaDataComponent>(uid, out var metadata) &&
                        metadata.EntityPrototype?.ID == "WallSolid" &&
                        SEntMan.GetComponent<TransformComponent>(uid).MapUid == stage.MapUid);
                    if (fault == "DeletedWall") SEntMan.DeleteEntity(wall);
                    else
                    {
                        transform.Unanchor(wall);
                        transform.SetLocalPosition(wall, new Vector2(20f, 20f));
                    }
                }
                Assert.That(stages.Verify(stage), Is.False);
                Assert.That(Pair.Server.System<KsProcgenRoomFixtureSystem>().Check(stage).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.InvalidInput));
                Assert.That(stages.Discard(stage), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { stages.DiscardAll(); }
        });
    }

    [Test]
    public async Task GridInitializationSpawnExhaustionRollsBackWholeScene()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var result = Stage(Room(3, 1), entityId: "KsProcgenInitializingDiskFixture", maxSpawnedEntities: 3);
                Assert.That(result.Stage, Is.Not.Null, result.Issue?.Code);
                var stage = result.Stage!;
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.BudgetExceeded), initialized.Issue?.Code);
                Assert.That(initialized.Issue!.Code, Is.EqualTo("AssemblyStageInitializationSpawnBudget"));
                Assert.That(initialized.Stage, Is.Null);
                Assert.That(stage.Active, Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { stages.DiscardAll(); }
        });
    }

    [Test]
    public async Task FixedActorPassesTinyCorridorWithoutMutationAndHonorsEveryBudget()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var map = Pair.Server.System<SharedMapSystem>();
            var actorMap = map.CreateMap(out var actorMapId);
            var actorUid = SEntMan.SpawnEntity("KsProcgenRoomActorFixture", new EntityCoordinates(actorMap, Vector2.Zero));
            try
            {
                var result = Stage(Room(3, 1));
                Assert.That(result.Stage, Is.Not.Null, result.Issue?.Code);
                var stage = result.Stage!;
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                var before = SEntMan.GetEntities().ToHashSet();
                var actorPosition = SEntMan.GetComponent<TransformComponent>(actorUid).Coordinates;
                var targets = new[] { new Vector2i(2, 0) };
                var observed = fixtures.CheckCardinalAccess(stage, actorUid, new Vector2i(0, 0), targets);
                Assert.That(observed.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), observed.Reason);
                Assert.That(observed.ReachedTerminals, Is.EquivalentTo(targets));
                Assert.That(observed.ExpandedCells, Is.EqualTo(3));
                Assert.That(observed.EngineAccessVerified || observed.Published, Is.False);
                Assert.That(SEntMan.GetComponent<TransformComponent>(actorUid).Coordinates, Is.EqualTo(actorPosition));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                var exact = fixtures.CheckCardinalAccess(stage, actorUid, new Vector2i(0, 0), targets,
                    maximumFixturePairs: observed.FixturePairs, maximumScannedEntities: observed.ScannedEntities,
                    maximumExpandedCells: observed.ExpandedCells, maximumFloorProbes: observed.FloorProbes);
                Assert.That(exact.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), exact.Reason);
                var exhausted = fixtures.CheckCardinalAccess(stage, actorUid, new Vector2i(0, 0), targets,
                    maximumExpandedCells: 2);
                Assert.That(exhausted.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.BudgetExceeded));
                Assert.That(exhausted.ExpandedCells, Is.EqualTo(2));
                Assert.That(exhausted.ReachedTerminals, Is.Empty);
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new Vector2i(0, 0), targets,
                    maximumFloorProbes: 0).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.BudgetExceeded));
                Assert.That(fixtures.Check(stage, maximumScannedEntities: 1).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.BudgetExceeded));
                var actorFixture = SEntMan.GetComponent<FixturesComponent>(actorUid).Fixtures["body"];
                Pair.Server.System<SharedPhysicsSystem>().SetRadius(actorUid, "body", actorFixture, actorFixture.Shape, 0.6f);
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new Vector2i(0, 0), targets).Reason,
                    Is.EqualTo("RoomActorRootBlocked"), "An actor's bounds cannot overhang a one-tile corridor.");
            }
            finally { stages.DiscardAll(); map.DeleteMap(actorMapId); }
        });
    }

    [Test]
    public async Task ContinuousActorSweepFindsThinObstacleBetweenFreeCenters()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var map = Pair.Server.System<SharedMapSystem>();
            var actorMap = map.CreateMap(out var actorMapId);
            var actorUid = SEntMan.SpawnEntity("KsProcgenRoomActorFixture", new EntityCoordinates(actorMap, Vector2.Zero));
            try
            {
                var stage = Stage(Room(3, 1), entityId: "KsProcgenRoomObstacleFixture").Stage!;
                Assert.That(stage, Is.Not.Null);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                var obstacleUid = stage.Members.Values.Single();
                var obstaclePosition = SEntMan.GetComponent<TransformComponent>(obstacleUid).LocalPosition;
                var obstacleFixture = SEntMan.GetComponent<FixturesComponent>(obstacleUid).Fixtures["body"];
                var shape = (PhysShapeCircle) obstacleFixture.Shape;
                // Keep the staged body pose intact while its authored fixture sits between tile centers.
                Pair.Server.System<SharedPhysicsSystem>().SetPosition(obstacleUid, "body", obstacleFixture, shape,
                    new Vector2(1f, 0.5f) - obstaclePosition);
                var root = new Vector2i(0, 0);
                var target = new Vector2i(1, 0);
                var result = fixtures.CheckCardinalAccess(stage, actorUid, root, new[] { target });
                Assert.That(result.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Rejected), result.Reason);
                Assert.That(result.Reason, Is.EqualTo("RoomActorTerminalsDisconnected"));
                Assert.That(result.ReachedTerminals, Is.Empty);
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, root, new[] { target },
                    maximumFixturePairs: 0).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.BudgetExceeded));
                var actorFixture = SEntMan.GetComponent<FixturesComponent>(actorUid).Fixtures["body"];
                Pair.Server.System<SharedPhysicsSystem>().SetCollisionMask(actorUid, "body", actorFixture, 0);
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, root, new[] { target }).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.Rejected), "The obstacle's reverse filter match still blocks.");
                Pair.Server.System<SharedPhysicsSystem>().SetPosition(obstacleUid, "body", obstacleFixture, shape,
                    new Vector2(20f, 0.5f) - obstaclePosition);
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, root, new[] { target }).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
            }
            finally { stages.DiscardAll(); map.DeleteMap(actorMapId); }
        });
    }

    [Test]
    public async Task FreshSceneRejectsHardOverlapForeignEntitiesAndUnsupportedShapes()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            EntityUid? foreignUid = null;
            try
            {
                var result = Stage(Room(3, 1), entityId: "KsProcgenRoomObstacleFixture", copies: 2);
                var stage = result.Stage!;
                Assert.That(stage, Is.Not.Null, result.Issue?.Code);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                Assert.That(fixtures.Check(stage).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
                var members = stage.Members.Values.ToArray();
                var second = SEntMan.GetComponent<FixturesComponent>(members[1]).Fixtures["body"];
                Pair.Server.System<SharedPhysicsSystem>().SetPosition(members[1], "body", second, (PhysShapeCircle) second.Shape,
                    SEntMan.GetComponent<TransformComponent>(members[0]).LocalPosition -
                    SEntMan.GetComponent<TransformComponent>(members[1]).LocalPosition);
                Assert.That(fixtures.Check(stage).Reason, Is.EqualTo("RoomHardFixtureOverlap"));
                Assert.That(fixtures.Check(stage, maximumFixturePairs: 0).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.BudgetExceeded));
                var fixtureSystem = Pair.Server.System<FixtureSystem>();
                Assert.That(fixtureSystem.TryCreateFixture(members[1], new EdgeShape(), "Unsupported"), Is.True);
                Assert.That(fixtures.Check(stage).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.UnsupportedContent));
                fixtureSystem.DestroyFixture(members[1], "Unsupported");
                Pair.Server.System<SharedPhysicsSystem>().SetPosition(members[1], "body", second,
                    (PhysShapeCircle) second.Shape, Vector2.Zero);
                foreignUid = SEntMan.SpawnEntity("KsProcgenRoomEmptyFixture", new EntityCoordinates(stage.MapUid, Vector2.Zero));
                Assert.That(fixtures.Check(stage).Reason, Is.EqualTo("RoomSceneForeignEntity"));
            }
            finally
            {
                if (foreignUid is { } uid) SEntMan.DeleteEntity(uid);
                stages.DiscardAll();
            }
        });
    }

    [Test]
    public async Task VaultDeclaredContentBlocksCleanPassageEvenWithoutHardFixtures()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var map = Pair.Server.System<SharedMapSystem>();
            var actorMap = map.CreateMap(out var actorMapId);
            var actorUid = SEntMan.SpawnEntity("KsProcgenRoomActorFixture", new EntityCoordinates(actorMap, Vector2.Zero));
            try
            {
                var stage = Stage(Room(3, 1), movement: KsProcgenMovementClass.VaultRequired).Stage!;
                Assert.That(stage, Is.Not.Null);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                var blocked = stage.DeclaredBlockingCells.Single();
                var result = Pair.Server.System<KsProcgenRoomFixtureSystem>().CheckCardinalAccess(stage, actorUid, blocked,
                    new[] { new Vector2i(2, 0) });
                Assert.That(result.Reason, Is.EqualTo("RoomActorRootBlocked"));
            }
            finally { stages.DiscardAll(); map.DeleteMap(actorMapId); }
        });
    }

    private KsProcgenRoomAssemblyStageResult Stage(KsProcgenRoomAccessMask room, string entityId = "KsProcgenRoomEmptyFixture",
        int copies = 1, KsProcgenMovementClass movement = KsProcgenMovementClass.Clear,
        int maxTerrainCells = 4096, int maxSpawnedEntities = 256)
    {
        var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
        var definition = new KsProcgenAssemblyVariant
        {
            Id = "Base", AnchorMember = "Root", Members = new List<KsProcgenAssemblyMember>
            {
                new() { Id = "Root", Entity = entityId, Movement = movement,
                    RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new List<int> { 0 } },
            },
        };
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Core", definition, new Dictionary<string, string>(),
            id => manager.TryIndex<EntityPrototype>(id, out _), out var assembly, out var issue), Is.True, issue?.Code);
        var core = new KsProcgenResolvedEntityCore("Core", 1f, copies, new[] { assembly! });
        var selections = Enumerable.Range(0, copies).Select(index => new KsProcgenRoomCoreSelection("Pack", core, index)).ToArray();
        return Pair.Server.System<KsProcgenAssemblyStageSystem>().TrySearchRoomStage("Room", selections, room, 17,
            maxSpawnedEntities: maxSpawnedEntities, floorTileId: "FloorSteel", maxTerrainCells: maxTerrainCells);
    }

    private static KsProcgenRoomAccessMask Room(int width, int height) => new(
        Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToHashSet(),
        new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(),
        new HashSet<Vector2i> { new(0, 0) }, new Vector2(0.5f, 0.5f));
}
