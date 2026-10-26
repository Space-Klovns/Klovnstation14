#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenRoomFixtureSystem))]
public sealed class KsProcgenOwnedScenePhysicsTests : KsProcgenGameTest
{
    [Test]
    public async Task CompleteOwnedSceneBoundsItsScanAndNeverMovesTheActor()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var maps = Pair.Server.System<SharedMapSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var outsideUid = maps.CreateMap(out var outsideId);
            var actorUid = SEntMan.SpawnEntity("KsProcgenRoomActorFixture", new EntityCoordinates(outsideUid, new Vector2(20f, 20f)));
            try
            {
                var stage = Generate(3, 1);
                Assert.That(fixtures.Check(stage).Reason, Is.EqualTo("InvalidInitializedRoomScene"));
                AssertInitialized(stage);
                // Unrelated live entities must not consume map-scoped inspection work.
                for (var index = 0; index < 32; index++) SEntMan.SpawnEntity("Paper", new EntityCoordinates(outsideUid, Vector2.Zero));
                var initialPosition = SEntMan.GetComponent<TransformComponent>(actorUid).Coordinates;
                var report = fixtures.Check(stage, maximumScannedEntities: 2);
                Assert.That(report.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), message: report.Reason);
                Assert.That(report.ScannedEntities, Is.EqualTo(2));
                var access = fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { new Vector2i(2, 0) }, maximumScannedEntities: 2);
                Assert.That(access.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), message: access.Reason);
                Assert.That(access.ReachedTerminals, Is.EqualTo(new[] { new Vector2i(2, 0) }));
                Assert.That(access.ScannedEntities, Is.EqualTo(2));
                Assert.That(SEntMan.GetComponent<TransformComponent>(actorUid).Coordinates, Is.EqualTo(initialPosition));
                Assert.That(access.EngineAccessVerified || access.Published || stage.EngineAccessVerified, Is.False);
                Assert.That(fixtures.Check(stage, maximumScannedEntities: 1).Reason, Is.EqualTo("RoomEntityScanBudget"));
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { new Vector2i(2, 0) },
                    maximumExpandedCells: 0).Reason, Is.EqualTo("RoomAccessExpandedBudget"));
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { new Vector2i(2, 0) },
                    maximumFloorProbes: 0).Reason, Is.EqualTo("RoomFloorProbeBudget"));
            }
            finally { stages.DiscardAll(); maps.DeleteMap(outsideId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task DisconnectedIslandsAndStructuralUnderlayAreNotCleanFloor(bool structuralUnderlay)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var maps = Pair.Server.System<SharedMapSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var outsideUid = maps.CreateMap(out var outsideId);
            var actorUid = SEntMan.SpawnEntity("KsProcgenRoomActorFixture", new EntityCoordinates(outsideUid, Vector2.Zero));
            try
            {
                var request = Request(1, 1);
                if (structuralUnderlay) request.Shape.EnvelopeCells = new() { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
                else request.Shape.Cells.Add(new(1, 1));
                var result = stages.TryGenerate(request, "KsProcgenSimpleOffice", planningBudgets: Budgets());
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                var stage = result.Stage!; AssertInitialized(stage);
                var report = fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { structuralUnderlay ? new Vector2i(1, 0) : new Vector2i(1, 1) });
                Assert.That(report.Reason, Is.EqualTo(structuralUnderlay ? "InvalidRoomActorInput" : "RoomActorTerminalsDisconnected"));
                Assert.That(report.ReachedTerminals, Is.Empty);
            }
            finally { stages.DiscardAll(); maps.DeleteMap(outsideId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ActualFixtureOverlapIsDetectedAcrossTheCompleteOwnedScene()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var physics = Pair.Server.System<SharedPhysicsSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var stage = Generate(7, 7, catalog: Catalog("KsProcgenRoomObstacleFixture", KsProcgenMovementClass.Clear, copies: 2));
                AssertInitialized(stage);
                Assert.That(fixtures.Check(stage).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
                var members = stage.Assemblies.SelectMany(assembly => assembly.Values).ToArray(); Assert.That(members.Length, Is.EqualTo(2));
                var second = SEntMan.GetComponent<FixturesComponent>(members[1]).Fixtures["body"];
                var offset = SEntMan.GetComponent<TransformComponent>(members[0]).LocalPosition - SEntMan.GetComponent<TransformComponent>(members[1]).LocalPosition;
                physics.SetPosition(members[1], "body", second, (PhysShapeCircle) second.Shape, offset);
                Assert.That(stages.Verify(stage), Is.True, message: "Declaration and transform verification cannot prove fixture separation.");
                Assert.That(fixtures.Check(stage).Reason, Is.EqualTo("RoomHardFixtureOverlap"));
                Assert.That(fixtures.Check(stage, maximumFixturePairs: 0).Reason, Is.EqualTo("RoomFixturePairBudget"));
                var fixtureSystem = Pair.Server.System<FixtureSystem>();
                Assert.That(fixtureSystem.TryCreateFixture(members[1], new EdgeShape(), "Unsupported"), Is.True);
                Assert.That(fixtures.Check(stage).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.UnsupportedContent));
                fixtureSystem.DestroyFixture(members[1], "Unsupported");
                physics.SetPosition(members[1], "body", second, (PhysShapeCircle) second.Shape, Vector2.Zero);
                Assert.That(fixtures.Check(stage).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ContinuousSweepsFindAnObstacleBetweenOtherwiseFreeTileCenters()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var maps = Pair.Server.System<SharedMapSystem>(); var physics = Pair.Server.System<SharedPhysicsSystem>();
            var before = SEntMan.GetEntities().ToHashSet(); var outsideUid = maps.CreateMap(out var outsideId);
            var actorUid = SEntMan.SpawnEntity("KsProcgenRoomActorFixture", new EntityCoordinates(outsideUid, Vector2.Zero));
            try
            {
                var stage = Generate(7, 7, catalog: Catalog("KsProcgenRoomObstacleFixture", KsProcgenMovementClass.Clear));
                AssertInitialized(stage); var obstacleUid = stage.Assemblies.Single().Values.Single();
                var fixture = SEntMan.GetComponent<FixturesComponent>(obstacleUid).Fixtures["body"];
                var bodyPosition = SEntMan.GetComponent<TransformComponent>(obstacleUid).LocalPosition;
                var fixtureSystem = Pair.Server.System<FixtureSystem>();
                // A thin curtain crosses every row between columns zero and one. Tile centers
                // remain clear, and the actor cannot bypass it outside the declared floor.
                var curtain = new PolygonShape();
                curtain.SetAsBox(0.01f, 4f, new Vector2(1f, 3.5f) - bodyPosition, 0f);
                Assert.That(fixtureSystem.TryCreateFixture(obstacleUid, curtain, "curtain",
                    collisionLayer: fixture.CollisionLayer, collisionMask: fixture.CollisionMask), Is.True);
                // Preserve collision enablement: removing the last fixture disables the body.
                fixtureSystem.DestroyFixture(obstacleUid, "body");
                Assert.That(SEntMan.GetComponent<PhysicsComponent>(obstacleUid).CanCollide, Is.True);
                Assert.That(stages.Verify(stage), Is.True);
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { new Vector2i(0, 0) }).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new(1, 0), new[] { new Vector2i(1, 0) }).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
                var report = fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { new Vector2i(1, 0) });
                Assert.That(report.Reason, Is.EqualTo("RoomActorTerminalsDisconnected"));
                Assert.That(report.ReachedTerminals, Is.Empty);
                var actorFixture = SEntMan.GetComponent<FixturesComponent>(actorUid).Fixtures["body"];
                physics.SetCollisionMask(actorUid, "body", actorFixture, 0);
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { new Vector2i(1, 0) }).Reason,
                    Is.EqualTo("RoomActorTerminalsDisconnected"), message: "The reverse collision filter must still block.");
                fixtureSystem.DestroyFixture(obstacleUid, "curtain");
                Assert.That(fixtures.CheckCardinalAccess(stage, actorUid, new(0, 0), new[] { new Vector2i(1, 0) }).Status,
                    Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
            }
            finally { stages.DiscardAll(); maps.DeleteMap(outsideId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task DeclaredVaultObstaclesRemainExcludedFromCleanPassages(bool layered)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var maps = Pair.Server.System<SharedMapSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var outsideUid = maps.CreateMap(out var outsideId);
            var actorUid = SEntMan.SpawnEntity("KsProcgenRoomActorFixture", new EntityCoordinates(outsideUid, Vector2.Zero));
            try
            {
                var catalog = layered ? KsProcgenOwnedLayeredTests.Catalog(Pair.Server.ResolveDependency<IPrototypeManager>(), contained: true, lighting: false)
                    : Catalog("KsProcgenRoomEmptyFixture", KsProcgenMovementClass.VaultRequired, footprintWidth: 2);
                var result = stages.TryGenerate(Request(7, 7), layered ? "Layered" : "Physics", planningBudgets: Budgets(), content: catalog);
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                var stage = result.Stage!; AssertInitialized(stage);
                if (layered)
                {
                    foreach (var uid in stage.Assemblies.Single().Values)
                        if (SEntMan.TryGetComponent<PhysicsComponent>(uid, out _))
                            Pair.Server.System<SharedPhysicsSystem>().SetCanCollide(uid, false);
                    Assert.That(stages.Verify(stage), Is.True);
                }
                var proposal = result.Proposal!.Furnishings.Single(region => region.Proposal.Entities.Count > 0).Proposal;
                var blocked = layered ? proposal.Layered!.BlockedCells[0] : proposal.Entities.Single().OccupiedCells.Last();
                if (!layered) Assert.That(blocked, Is.Not.EqualTo(proposal.Entities.Single().Cell));
                var retainedRequest = stage.Request.CopyRequest(); retainedRequest.Shape.Cells.Clear();
                var report = fixtures.CheckCardinalAccess(stage, actorUid, blocked, new[] { new Vector2i(0, 0) });
                Assert.That(report.Reason, Is.EqualTo("RoomActorRootBlocked"));
                Assert.That(report.ReachedTerminals, Is.Empty);
            }
            finally { stages.DiscardAll(); maps.DeleteMap(outsideId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task HullUnionDoesNotExemptFurnitureFromCollisionChecks()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var before = SEntMan.GetEntities().ToHashSet(); var request = Request(7, 7);
            var target = request.Shape.Cells.ToHashSet();
            request.Shape.EnvelopeCells = target.SelectMany(cell => new[] { cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
                cell + new Vector2i(0, 1), cell + new Vector2i(0, -1) }).Where(cell => !target.Contains(cell)).Distinct().ToList();
            try
            {
                var result = stages.TryGenerate(request, "Physics", planningBudgets: Budgets(), content: Catalog("KsProcgenRoomObstacleFixture", KsProcgenMovementClass.Clear));
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                var stage = result.Stage!; AssertInitialized(stage);
                Assert.That(result.Proposal!.Structure!.Windows.Count, Is.GreaterThan(0));
                var observed = fixtures.Check(stage);
                Assert.That(observed.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), message: observed.Reason);
                var obstacleUid = stage.Assemblies.SelectMany(assembly => assembly.Values)
                    .Single(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "KsProcgenRoomObstacleFixture");
                var fixture = SEntMan.GetComponent<FixturesComponent>(obstacleUid).Fixtures["body"];
                Pair.Server.System<SharedPhysicsSystem>().SetPosition(obstacleUid, "body", fixture, (PhysShapeCircle) fixture.Shape,
                    new Vector2(-0.5f, 0.5f) - SEntMan.GetComponent<TransformComponent>(obstacleUid).LocalPosition);
                Assert.That(fixtures.Check(stage).Reason, Is.EqualTo("RoomHardFixtureOverlap"));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ForeignDescendantsAreFoundUnderNoncollidableAndContainedMembers(bool contained)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var before = SEntMan.GetEntities().ToHashSet(); var foreignUid = EntityUid.Invalid;
            try
            {
                var catalog = contained ? KsProcgenOwnedLayeredTests.Catalog(Pair.Server.ResolveDependency<IPrototypeManager>(), contained: true, lighting: false)
                    : Catalog("KsProcgenRoomEmptyFixture", KsProcgenMovementClass.Clear);
                var result = stages.TryGenerate(Request(7, 7), contained ? "Layered" : "Physics", planningBudgets: Budgets(), content: catalog);
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                var stage = result.Stage!; AssertInitialized(stage);
                var baseline = fixtures.Check(stage);
                var parentUid = stage.Assemblies.Single().Values.First();
                if (contained)
                {
                    var pose = result.Proposal!.Furnishings.Single(region => region.Proposal.Layered != null).Proposal.Layered!.Access.Poses!.Members
                        .Single(member => member.Support.Layer == KsProcgenPlacementLayer.Container);
                    parentUid = stage.Assemblies.Single()[pose.Support.MemberId];
                }
                foreignUid = SEntMan.SpawnEntity("Paper", new EntityCoordinates(parentUid, Vector2.Zero));
                Assert.That(fixtures.Check(stage).Reason, Is.EqualTo("RoomSceneForeignEntity"));
                Assert.That(stages.Verify(stage), Is.False);
                SEntMan.DeleteEntity(foreignUid);
                Assert.That(stages.Verify(stage), Is.True);
                Assert.That(fixtures.Check(stage).Reason, Is.EqualTo(baseline.Reason));
            }
            finally
            {
                if (SEntMan.EntityExists(foreignUid)) SEntMan.DeleteEntity(foreignUid);
                stages.DiscardAll();
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenOwnedStage Generate(int width, int height, KsProcgenThemeCatalog? catalog = null)
    {
        var result = Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(Request(width, height),
            catalog == null ? "KsProcgenNoDoorThemeFixture" : "Physics", planningBudgets: Budgets(), content: catalog);
        Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
        return result.Stage!;
    }
    private void AssertInitialized(KsProcgenOwnedStage stage)
    {
        var result = Pair.Server.System<KsProcgenOwnedStageSystem>().TryInitialize(stage);
        Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: result.Issue?.Code);
    }
    private KsProcgenThemeCatalog Catalog(string entityId, KsProcgenMovementClass movement, int copies = 1, int footprintWidth = 1)
    {
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "Physics",
            Themes = new() { ["Physics"] = new() { Parent = "KsProcgenSimpleOffice", LightingPacks = new(), Goals = new() { FurnishingDensity = 0f },
                EntityPacks = new() { new() { Pack = "Fixtures" } } } },
            Entities = new() { ["Fixtures"] = new() { Assemblies = new() { new() { Id = "Fixture", Assembly = "Fixture", MinimumCount = copies } } } },
            Assemblies = new() { ["Fixture"] = new() { AnchorMember = "Root", Members = new()
                { new() { Id = "Root", Entity = entityId, Movement = movement, AllowedQuarterTurns = new() { 0 },
                    Footprint = Enumerable.Range(0, footprintWidth).Select(x => new Vector2i(x, 0)).ToList() } } } },
        };
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, message: issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(Pair.Server.ResolveDependency<IPrototypeManager>(), "Physics", out var catalog, out issue, assets: draft),
            Is.True, message: issue?.Code);
        return catalog!;
    }
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 };
    private static KsProcgenRequest Request(int width, int height) => new()
    {
        RequestId = "OwnedPhysics", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToList() },
    };
}
