#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenRoomFixtureSystem))]
public sealed class KsProcgenScopedSceneTests : KsProcgenGameTest
{
    [Test]
    public async Task UnrelatedMapsDoNotConsumeRoomInspectionWork()
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>();
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var outsideUid = maps.CreateMap(out var outsideId);
            try
            {
                var stage = Stage();
                var baseline = fixtures.Check(stage);
                Assert.That(baseline.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), message: baseline.Reason);
                Assert.That(baseline.ScannedEntities, Is.EqualTo(stage.SpawnedEntityCount));
                for (var index = 0; index < 32; index++)
                    SEntMan.SpawnEntity("Paper", new EntityCoordinates(outsideUid, Vector2.Zero));
                var scoped = fixtures.Check(stage, maximumScannedEntities: baseline.ScannedEntities);
                Assert.That(scoped.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), message: scoped.Reason);
                Assert.That(scoped.ScannedEntities, Is.EqualTo(baseline.ScannedEntities));
                var insufficient = fixtures.Check(stage, maximumScannedEntities: baseline.ScannedEntities - 1);
                Assert.That(insufficient.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.BudgetExceeded));
                Assert.That(insufficient.Reason, Is.EqualTo("RoomEntityScanBudget"));
                Assert.That(insufficient.ScannedEntities, Is.LessThanOrEqualTo(baseline.ScannedEntities - 1));
            }
            finally { stages.DiscardAll(); maps.DeleteMap(outsideId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public async Task ForeignInsertionsAreFoundAtEveryParentLayerIncludingContainers(int layer)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>();
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var fixtures = Pair.Server.System<KsProcgenRoomFixtureSystem>();
            var transforms = Pair.Server.System<SharedTransformSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var outsideUid = maps.CreateMap(out var outsideId);
            try
            {
                var stage = Stage();
                Assert.That(fixtures.Check(stage).Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed));
                var foreignUid = SEntMan.SpawnEntity("Paper", new EntityCoordinates(outsideUid, Vector2.Zero));
                var rootUid = stage.Members.Values.Single();
                var parentUid = layer switch { 0 => stage.MapUid, 1 => stage.GridUid!.Value, _ => rootUid };
                if (layer == 3)
                {
                    var containers = Pair.Server.System<SharedContainerSystem>();
                    var container = containers.EnsureContainer<Container>(rootUid, "ForeignTest", out _);
                    Assert.That(containers.Insert(foreignUid, container), Is.True);
                }
                else
                    transforms.SetCoordinates(foreignUid, new EntityCoordinates(parentUid, Vector2.Zero));
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(foreignUid).EntityPaused, Is.True);
                var rejected = fixtures.Check(stage);
                Assert.That(rejected.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Rejected));
                Assert.That(rejected.Reason, Is.EqualTo("RoomSceneForeignEntity"));
                if (layer == 3)
                {
                    var containers = Pair.Server.System<SharedContainerSystem>();
                    Assert.That(containers.TryGetContainingContainer(foreignUid, out var container), Is.True);
                    Assert.That(containers.Remove(foreignUid, container!), Is.True);
                }
                transforms.SetCoordinates(foreignUid, new EntityCoordinates(outsideUid, Vector2.Zero));
                var recovered = fixtures.Check(stage, maximumScannedEntities: stage.SpawnedEntityCount);
                Assert.That(recovered.Status, Is.EqualTo(KsProcgenRoomFixtureStatus.Observed), message: recovered.Reason);
            }
            finally { stages.DiscardAll(); maps.DeleteMap(outsideId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenAssemblyStage Stage()
    {
        var prototypes = Pair.Server.ResolveDependency<IPrototypeManager>();
        var definition = new KsProcgenAssemblyVariant
        {
            Id = "Base", AnchorMember = "Root", Members = new()
            {
                new() { Id = "Root", Entity = "KsProcgenRoomEmptyFixture", Movement = KsProcgenMovementClass.Clear,
                    RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } },
            },
        };
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("ScopedScene", definition, new Dictionary<string, string>(),
            id => prototypes.HasIndex<EntityPrototype>(id), out var assembly, out var issue), Is.True, message: issue?.Code);
        var core = new KsProcgenResolvedEntityCore("Core", 1f, 1, new[] { assembly! });
        var room = new KsProcgenRoomAccessMask(
            new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0) },
            new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(0, 0) }, new Vector2(0.5f, 0.5f));
        var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>();
        var result = stages.TrySearchRoomStage("Room", new[] { new KsProcgenRoomCoreSelection("Pack", core, 0) },
            room, 17, floorTileId: "FloorSteel");
        Assert.That(result.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), message: result.Issue?.Code);
        Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
        return result.Stage!;
    }
}
