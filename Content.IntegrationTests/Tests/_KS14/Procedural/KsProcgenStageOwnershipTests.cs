using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenStageOwnershipSystem))]
public sealed class KsProcgenStageOwnershipTests : KsProcgenGameTest
{
    [TestCase(KsProcgenStageKind.Tiles)] [TestCase(KsProcgenStageKind.Surface)]
    [TestCase(KsProcgenStageKind.Container)] [TestCase(KsProcgenStageKind.Assembly)]
    [TestCase(KsProcgenStageKind.Owned)]
    public async Task ExternalMapDeletionRetiresTheHandleAndCleansEscapedOwnedMembers(KsProcgenStageKind kind)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var owner = Pair.Server.System<KsProcgenStageOwnershipSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var stage = Create(kind); var child = Member(stage);
            Assert.That(SEntMan.GetComponent<KsProcgenStageSessionComponent>(stage.MapUid).Stage, Is.SameAs(stage));
            Assert.That(owner.Count(kind), Is.EqualTo(1));
            Assert.That(maps.IsPaused(stage.MapId), Is.True);
            var outside = maps.CreateMap(out var outsideId, runMapInit: false);
            try
            {
                if (kind != KsProcgenStageKind.Tiles)
                    Pair.Server.System<SharedTransformSystem>().SetCoordinates(child, new EntityCoordinates(outside, Vector2.Zero));
                maps.DeleteMap(stage.MapId);
                Assert.That(stage.Active, Is.False);
                Assert.That(owner.Count(kind), Is.Zero);
                Assert.That(SEntMan.EntityExists(child), Is.False);
                Assert.That(maps.MapExists(outsideId), Is.True);
                Assert.That(Discard(stage), Is.False);
                // Reusing a numeric map ID must never let an old handle delete its replacement.
                var replacement = maps.CreateMap(stage.MapId, runMapInit: false);
                Assert.That(replacement, Is.Not.EqualTo(stage.MapUid));
                Assert.That(Discard(stage), Is.False);
                Assert.That(maps.MapExists(stage.MapId), Is.True);
                maps.DeleteMap(stage.MapId);
            }
            finally { Discard(stage); if (maps.MapExists(outsideId)) maps.DeleteMap(outsideId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task MixedPausedStagesShareTheComponentRegistryAndDiscardOnlyTheirOwnKind()
    {
        await AssertOnServer(() =>
        {
            var owner = Pair.Server.System<KsProcgenStageOwnershipSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var stages = new List<KsProcgenStageHandle>();
            try
            {
                foreach (var kind in new[] { KsProcgenStageKind.Tiles, KsProcgenStageKind.Surface, KsProcgenStageKind.Container, KsProcgenStageKind.Assembly }) stages.Add(Create(kind));
                Assert.That(owner.RetainedStageCount, Is.EqualTo(4));
                Assert.That(owner.Snapshot(), Is.EquivalentTo(stages));
                Assert.That(Pair.Server.System<KsProcgenSurfaceStageSystem>().DiscardAll(), Is.EqualTo(1));
                Assert.That(stages.Single(stage => stage.Kind == KsProcgenStageKind.Surface).Active, Is.False);
                Assert.That(stages.Where(stage => stage.Kind != KsProcgenStageKind.Surface).All(stage => stage.Active), Is.True);
                Assert.That(owner.RetainedStageCount, Is.EqualTo(3));
            }
            finally { foreach (var stage in stages) Discard(stage); }
            Assert.That(owner.RetainedStageCount, Is.Zero);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
    public async Task OwnershipLossDuringInitializationRejectsAndCleansLaterCapturedAllocations(bool lateSpawn, bool queueDeletion)
    {
        await AssertOnServer(() =>
        {
            var listener = Pair.Server.System<KsProcgenInsertionTestSystem>(); listener.Reset();
            var before = SEntMan.GetEntities().ToHashSet();
            var stage = (KsProcgenContainerStage) Create(KsProcgenStageKind.Container);
            try
            {
                listener.TargetPrototypeId = "CoordinatesDisk";
                listener.MapInitAction = () =>
                {
                    if (queueDeletion) SEntMan.QueueDeleteEntity(stage.MapUid);
                    else SEntMan.RemoveComponent<KsProcgenStageSessionComponent>(stage.MapUid);
                };
                listener.SpawnExtraOnMapInit = lateSpawn ? 1 : 0; listener.SpawnExtraOutsideMap = true;
                var result = Pair.Server.System<KsProcgenContainerStageSystem>().TryInitialize(stage);
                Assert.That(result.Status, Is.EqualTo(KsProcgenContainerStageStatus.Rejected), result.Issue?.Code);
                Assert.That(result.Stage, Is.Null);
                Assert.That(stage.Active, Is.False);
                Assert.That(Pair.Server.System<KsProcgenStageOwnershipSystem>().RetainedStageCount, Is.Zero);
                Assert.That(listener.ExtraSpawnedEntities.Count, Is.EqualTo(lateSpawn ? 1 : 0));
                Assert.That(listener.ExtraSpawnedEntities.Any(SEntMan.EntityExists), Is.False);
            }
            finally { listener.Reset(); Discard(stage); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task TileStagesCannotAllocateOrDiscardDuringAnotherPreviewInitialization()
    {
        await AssertOnServer(() =>
        {
            var tiles = Pair.Server.System<KsProcgenTileStageSystem>(); var listener = Pair.Server.System<KsProcgenInsertionTestSystem>();
            listener.Reset(); var before = SEntMan.GetEntities().ToHashSet();
            var tile = (KsProcgenTileStage) Create(KsProcgenStageKind.Tiles);
            var stage = (KsProcgenContainerStage) Create(KsProcgenStageKind.Container);
            try
            {
                var plan = TilePlan(); listener.TargetPrototypeId = "CoordinatesDisk";
                listener.MapInitAction = () =>
                {
                    Assert.That(tiles.TryStage(plan).Issue?.Code, Is.EqualTo("TileStageOperationActive"));
                    Assert.That(tiles.Discard(tile), Is.False);
                    Assert.That(tiles.DiscardAll(), Is.Zero);
                };
                var result = Pair.Server.System<KsProcgenContainerStageSystem>().TryInitialize(stage);
                Assert.That(result.Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview), result.Issue?.Code);
                Assert.That(tiles.Verify(tile), Is.True);
            }
            finally { listener.Reset(); Discard(stage); Discard(tile); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task TileRetentionBudgetCountsPausedMapsAndRejectsBeforeAllocation()
    {
        await AssertOnServer(() =>
        {
            var tiles = Pair.Server.System<KsProcgenTileStageSystem>(); var plan = TilePlan();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                for (var index = 0; index < KsProcgenTileStageSystem.MaximumRetainedStages; index++)
                    Assert.That(tiles.TryStage(plan).Status, Is.EqualTo(KsProcgenTileStageStatus.Staged));
                Assert.That(tiles.RetainedStageCount, Is.EqualTo(16));
                var retained = SEntMan.GetEntities().ToHashSet();
                Assert.That(tiles.TryStage(plan).Issue?.Code, Is.EqualTo("TileStageRetentionBudget"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(retained));
                Assert.That(tiles.DiscardAll(), Is.EqualTo(16));
            }
            finally { tiles.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task GlobalRetentionRefusesEveryKindBeforeAllocationEvenBelowItsOwnLimit()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = new List<KsProcgenStageHandle>();
            var owner = Pair.Server.System<KsProcgenStageOwnershipSystem>();
            try
            {
                foreach (var kind in new[] { KsProcgenStageKind.Tiles, KsProcgenStageKind.Surface,
                             KsProcgenStageKind.Container, KsProcgenStageKind.Assembly, KsProcgenStageKind.Owned })
                for (var count = 0; count < (kind == KsProcgenStageKind.Tiles ? 12 : 13); count++) stages.Add(Create(kind));
                Assert.That(owner.RetainedStageCount, Is.EqualTo(64));
                var retained = SEntMan.GetEntities().ToHashSet();
                Assert.That(Pair.Server.System<KsProcgenTileStageSystem>().TryStage(TilePlan()).Issue?.Code, Is.EqualTo("TileStageRetentionBudget"));
                Assert.That(Pair.Server.System<KsProcgenSurfaceStageSystem>().TryStage("Table", "KitchenMicrowave").Issue?.Code, Is.EqualTo("SurfaceStageRetentionBudget"));
                Assert.That(Pair.Server.System<KsProcgenContainerStageSystem>().TryStage(Assembly(container: true), new[] { "Host", "Disk" }).Issue?.Code, Is.EqualTo("ContainerStageRetentionBudget"));
                Assert.That(Pair.Server.System<KsProcgenAssemblyStageSystem>().TryStage(Assembly(container: false), new[] { "Root", "Device" },
                    new[] { new KsProcgenFloorRootPose("Root", Vector2.Zero) },
                    new[] { new KsProcgenMemberOrientation("Root", 0), new KsProcgenMemberOrientation("Device", 0) }, 0).Issue?.Code, Is.EqualTo("AssemblyStageRetentionBudget"));
                Assert.That(GenerateOwned().Issue?.Code, Is.EqualTo("OwnedStageRetentionBudget"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(retained));
            }
            finally { foreach (var stage in stages) Discard(stage); }
            Assert.That(owner.RetainedStageCount, Is.Zero);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenOwnedStageResult GenerateOwned() => Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(
        new() { RequestId = "OwnedRetention", Shape = new() { Cells = new() { new(0, 0) } } }, "KsProcgenNoDoorThemeFixture",
        planningBudgets: new() { MinimumRoomCells = 1 });

    private KsProcgenStageHandle Create(KsProcgenStageKind kind)
    {
        switch (kind)
        {
            case KsProcgenStageKind.Owned:
            {
                var result = GenerateOwned();
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code); return result.Stage!;
            }
            case KsProcgenStageKind.Tiles:
            {
                var result = Pair.Server.System<KsProcgenTileStageSystem>().TryStage(TilePlan());
                Assert.That(result.Status, Is.EqualTo(KsProcgenTileStageStatus.Staged), result.Issue?.Code); return result.Stage!;
            }
            case KsProcgenStageKind.Surface:
            {
                var result = Pair.Server.System<KsProcgenSurfaceStageSystem>().TryStage("Table", "KitchenMicrowave");
                Assert.That(result.Status, Is.EqualTo(KsProcgenSurfaceStageStatus.PositionedPreview), result.Issue?.Code); return result.Stage!;
            }
            case KsProcgenStageKind.Container:
            {
                var result = Pair.Server.System<KsProcgenContainerStageSystem>().TryStage(Assembly(container: true), new[] { "Host", "Disk" });
                Assert.That(result.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), result.Issue?.Code); return result.Stage!;
            }
            default:
            {
                var result = Pair.Server.System<KsProcgenAssemblyStageSystem>().TryStage(Assembly(container: false), new[] { "Root", "Device" },
                    new[] { new KsProcgenFloorRootPose("Root", Vector2.Zero) },
                    new[] { new KsProcgenMemberOrientation("Root", 0), new KsProcgenMemberOrientation("Device", 0) }, 0);
                Assert.That(result.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), result.Issue?.Code); return result.Stage!;
            }
        }
    }

    private KsProcgenResolvedAssembly Assembly(bool container)
    {
        var definition = container
            ? new KsProcgenAssemblyVariant { Id = "Base", AnchorMember = "Host", Members = new() { new() { Id = "Host", Entity = "ComputerShuttle" }, new() { Id = "Disk", Entity = "CoordinatesDisk" } },
                Relations = new() { new() { Id = "Stored", Subject = "Disk", Target = "Host", Kind = KsProcgenRelationKind.InContainer, ContainerId = "disk_slot" } } }
            : new KsProcgenAssemblyVariant { Id = "Base", AnchorMember = "Root", Members = new() { new() { Id = "Root", Entity = "Table" }, new() { Id = "Device", Entity = "KitchenMicrowave" } },
                Relations = new() { new() { Id = "Top", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.OnSurface } } };
        var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Ownership", definition, new Dictionary<string, string>(), id => manager.HasIndex<EntityPrototype>(id), out var assembly, out var issue), Is.True, issue?.Code);
        return assembly!;
    }
    private KsProcgenGeometryPipelineResult TilePlan()
    {
        var result = KsProcgenGeometryPipeline.Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), new KsProcgenRequest
            { RequestId = "OwnershipTiles", Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } } }, [], "KsProcgenNoDoorThemeFixture");
        Assert.That(result.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned), result.Issue?.Code); return result;
    }
    private static EntityUid Member(KsProcgenStageHandle stage) => stage switch
    {
        KsProcgenOwnedStage owned => owned.GridUid,
        KsProcgenTileStage tiles => tiles.GridUid, KsProcgenSurfaceStage surface => surface.SubjectUid,
        KsProcgenContainerStage container => container.Members["Host"], KsProcgenAssemblyStage assembly => assembly.Members["Root"], _ => EntityUid.Invalid,
    };
    private bool Discard(KsProcgenStageHandle stage) => stage switch
    {
        KsProcgenOwnedStage owned => Pair.Server.System<KsProcgenOwnedStageSystem>().Discard(owned),
        KsProcgenTileStage tiles => Pair.Server.System<KsProcgenTileStageSystem>().Discard(tiles),
        KsProcgenSurfaceStage surface => Pair.Server.System<KsProcgenSurfaceStageSystem>().Discard(surface),
        KsProcgenContainerStage container => Pair.Server.System<KsProcgenContainerStageSystem>().Discard(container),
        KsProcgenAssemblyStage assembly => Pair.Server.System<KsProcgenAssemblyStageSystem>().Discard(assembly), _ => false,
    };
}
