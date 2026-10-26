#nullable enable
using System;
using System.Linq;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenTileAllocationTestSystem : EntitySystem
{
    public Action? TileAction;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }
    private void OnTileChanged(ref TileChangedEvent args)
    {
        TileAction?.Invoke();
    }
    public EntityUid Allocate() => Spawn("Paper", doMapInit: false);
}

[TestOf(typeof(KsProcgenPreviewOperationSystem))]
public sealed class KsProcgenTileAllocationTests : KsProcgenGameTest
{
    [Test]
    public async Task LiveCallbackAllocationsAreRejectedAndCleanedEvenOutsideTheMap()
    {
        await AssertOnServer(() =>
        {
            var tiles = Pair.Server.System<KsProcgenTileStageSystem>(); var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>();
            var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>(); var plan = Plan();
            var before = SEntMan.GetEntities().ToHashSet(); var spawned = EntityUid.Invalid; var observed = 0;
            try
            {
                hook.TileAction = () => { hook.TileAction = null; spawned = hook.Allocate(); observed = operations.CapturedEntityCount; };
                var result = tiles.TryStage(plan);
                Assert.That(spawned, Is.Not.EqualTo(EntityUid.Invalid));
                Assert.That(observed, Is.EqualTo(3));
                Assert.That(result.Status, Is.EqualTo(KsProcgenTileStageStatus.UnsupportedContent));
                Assert.That(result.Issue?.Code, Is.EqualTo("TileStageUnexpectedAllocation"));
                Assert.That(result.Stage, Is.Null);
                Assert.That(SEntMan.EntityExists(spawned), Is.False);
                Assert.That(tiles.RetainedStageCount, Is.Zero);
                Assert.That(operations.Active, Is.False); Assert.That(operations.CapturedEntityCount, Is.Zero);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                var clean = tiles.TryStage(plan);
                Assert.That(clean.Status, Is.EqualTo(KsProcgenTileStageStatus.Staged));
                Assert.That(clean.Stage!.SpawnedEntityCount, Is.EqualTo(2));
                Assert.That(tiles.Verify(clean.Stage), Is.True);
                Assert.That(tiles.Discard(clean.Stage), Is.True);
            }
            finally { hook.TileAction = null; tiles.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(2, false)] [TestCase(3, true)]
    public async Task DeletedCallbackEntitiesStillConsumeTheCumulativeBudget(int limit, bool accepted)
    {
        await AssertOnServer(() =>
        {
            var tiles = Pair.Server.System<KsProcgenTileStageSystem>(); var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>();
            var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>(); var plan = Plan();
            var before = SEntMan.GetEntities().ToHashSet(); var spawned = EntityUid.Invalid;
            try
            {
                hook.TileAction = () => { hook.TileAction = null; spawned = hook.Allocate(); SEntMan.DeleteEntity(spawned); };
                var result = tiles.TryStage(plan, maxSpawnedEntities: limit);
                Assert.That(spawned, Is.Not.EqualTo(EntityUid.Invalid));
                Assert.That(result.Status, Is.EqualTo(accepted ? KsProcgenTileStageStatus.Staged : KsProcgenTileStageStatus.BudgetExceeded));
                if (accepted)
                {
                    Assert.That(result.Stage!.SpawnedEntityCount, Is.EqualTo(3));
                    Assert.That(tiles.Verify(result.Stage), Is.True); tiles.Discard(result.Stage);
                }
                else
                {
                    Assert.That(result.Stage, Is.Null);
                    Assert.That(result.Issue?.Code, Is.EqualTo("TileStageSpawnBudget"));
                }
                Assert.That(operations.Active, Is.False); Assert.That(operations.CapturedEntityCount, Is.Zero);
            }
            finally { hook.TileAction = null; tiles.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ThrowingTileCallbackReleasesTheScopeAndEveryCapturedAllocation()
    {
        await AssertOnServer(() =>
        {
            var tiles = Pair.Server.System<KsProcgenTileStageSystem>(); var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>();
            var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>(); var plan = Plan();
            var before = SEntMan.GetEntities().ToHashSet(); var spawned = EntityUid.Invalid;
            try
            {
                hook.TileAction = () => { hook.TileAction = null; spawned = hook.Allocate(); throw new InvalidOperationException("KS tile allocation fixture."); };
                var result = tiles.TryStage(plan);
                Assert.That(result.Status, Is.EqualTo(KsProcgenTileStageStatus.EngineFailure));
                Assert.That(result.Stage, Is.Null); Assert.That(SEntMan.EntityExists(spawned), Is.False);
                Assert.That(operations.Active, Is.False); Assert.That(operations.CapturedEntityCount, Is.Zero);
            }
            finally { hook.TileAction = null; tiles.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(0, KsProcgenTileStageStatus.InvalidPlan)] [TestCase(1, KsProcgenTileStageStatus.BudgetExceeded)]
    [TestCase(257, KsProcgenTileStageStatus.InvalidPlan)]
    public async Task ImpossibleOrUnsupportedAllocationLimitsFailBeforeAnyEngineWork(int limit, KsProcgenTileStageStatus status)
    {
        await AssertOnServer(() =>
        {
            var tiles = Pair.Server.System<KsProcgenTileStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var result = tiles.TryStage(Plan(), maxSpawnedEntities: limit);
            Assert.That(result.Status, Is.EqualTo(status)); Assert.That(result.Stage, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            Assert.That(Pair.Server.System<KsProcgenPreviewOperationSystem>().Active, Is.False);
        });
    }

    private KsProcgenGeometryPipelineResult Plan()
    {
        var result = KsProcgenGeometryPipeline.Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), new KsProcgenRequest
            { RequestId = "TileCapture", Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } } }, [], "KsProcgenNoDoorThemeFixture");
        Assert.That(result.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned), message: result.Issue?.Code); return result;
    }

    [TestCase(false)] [TestCase(true)]
    public async Task CancellationBeforeOrInsideTileCallbacksCleansTheSameCaptureScope(bool duringCallback)
    {
        await AssertOnServer(() =>
        {
            var tiles = Pair.Server.System<KsProcgenTileStageSystem>(); var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>();
            var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>(); var plan = Plan();
            var before = SEntMan.GetEntities().ToHashSet(); using var cancellation = new CancellationTokenSource();
            var spawned = EntityUid.Invalid;
            try
            {
                if (duringCallback) hook.TileAction = () => { hook.TileAction = null; spawned = hook.Allocate(); cancellation.Cancel(); };
                else cancellation.Cancel();
                var result = tiles.TryStage(plan, cancellationToken: cancellation.Token);
                Assert.That(result.Status, Is.EqualTo(KsProcgenTileStageStatus.Cancelled));
                Assert.That(result.Stage, Is.Null);
                Assert.That(spawned.IsValid(), Is.EqualTo(duringCallback));
                Assert.That(operations.Active, Is.False); Assert.That(operations.CapturedEntityCount, Is.Zero);
            }
            finally { hook.TileAction = null; tiles.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }
}
