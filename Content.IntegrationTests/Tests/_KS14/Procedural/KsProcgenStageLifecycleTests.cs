#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenLifecycleProbeComponent : Component;

public sealed class KsProcgenLifecycleProbeSystem : EntitySystem
{
    public Action? OnInitialize;
    public int Calls;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KsProcgenLifecycleProbeComponent, MapInitEvent>(OnMapInit);
    }
    private void OnMapInit(Entity<KsProcgenLifecycleProbeComponent> entity, ref MapInitEvent args)
    {
        Calls++;
        OnInitialize?.Invoke();
    }
}

[TestOf(typeof(KsProcgenStageLifecycleSystem))]
public sealed class KsProcgenStageLifecycleTests : KsProcgenGameTest
{
    [TestCase(KsProcgenStageKind.Surface)]
    [TestCase(KsProcgenStageKind.Container)]
    [TestCase(KsProcgenStageKind.Assembly)]
    public async Task InitializationIsPausedNonReentrantAndIdempotentAcrossAdapters(KsProcgenStageKind kind)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var probeSystem = Pair.Server.System<KsProcgenLifecycleProbeSystem>();
            var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var stage = Create(kind);
            try
            {
                probeSystem.Calls = 0;
                Assert.That(stage.LifecyclePhase, Is.EqualTo(KsProcgenStageLifecyclePhase.Uninitialized));
                probeSystem.OnInitialize = () =>
                {
                    Assert.That(stage.LifecyclePhase, Is.EqualTo(KsProcgenStageLifecyclePhase.Initializing));
                    Assert.That(operations.Active, Is.True);
                    Assert.That(mapSystem.IsPaused(stage.MapId), Is.True);
                    Assert.That(Initialize(stage).Status, Is.EqualTo("InvalidInput"));
                    Assert.That(Discard(stage), Is.False);
                };
                Assert.That(Initialize(stage).Status, Is.EqualTo("InitializedPreview"));
                Assert.That(probeSystem.Calls, Is.EqualTo(1));
                Assert.That(stage.LifecyclePhase, Is.EqualTo(KsProcgenStageLifecyclePhase.Initialized));
                Assert.That(mapSystem.IsPaused(stage.MapId), Is.True);
                Assert.That(mapSystem.IsInitialized(stage.MapId), Is.True);
                Assert.That(operations.Active, Is.False);
                Assert.That(operations.CapturedEntityCount, Is.Zero);
                Assert.That(Initialize(stage).Status, Is.EqualTo("InitializedPreview"));
                Assert.That(probeSystem.Calls, Is.EqualTo(1), message: "Idempotent initialization must not replay callbacks.");
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.That(Initialize(stage, token: cancellation.Token).Status, Is.EqualTo("Cancelled"));
                Assert.That(stage.LifecyclePhase, Is.EqualTo(KsProcgenStageLifecyclePhase.Retired));
                Assert.That(stage.Active, Is.False);
                Assert.That(Discard(stage), Is.False);
            }
            finally { probeSystem.OnInitialize = null; Discard(stage); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(KsProcgenStageKind.Surface)]
    [TestCase(KsProcgenStageKind.Container)]
    [TestCase(KsProcgenStageKind.Assembly)]
    public async Task CallbackCancellationCleansOffMapAllocationsAndAllowsTheNextInitialization(KsProcgenStageKind kind)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var probeSystem = Pair.Server.System<KsProcgenLifecycleProbeSystem>();
            var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>();
            using var cancellation = new CancellationTokenSource();
            var stage = Create(kind);
            var escapedUid = EntityUid.Invalid;
            KsProcgenStageHandle? recovered = null;
            try
            {
                probeSystem.OnInitialize = () =>
                {
                    escapedUid = Pair.Server.System<KsProcgenTileAllocationTestSystem>().Allocate();
                    cancellation.Cancel();
                };
                Assert.That(Initialize(stage, token: cancellation.Token).Status, Is.EqualTo("Cancelled"));
                Assert.That(escapedUid.IsValid(), Is.True);
                Assert.That(SEntMan.EntityExists(escapedUid), Is.False);
                Assert.That(stage.LifecyclePhase, Is.EqualTo(KsProcgenStageLifecyclePhase.Retired));
                Assert.That(operations.Active, Is.False);
                Assert.That(operations.CapturedEntityCount, Is.Zero);
                probeSystem.OnInitialize = null;
                recovered = Create(kind);
                Assert.That(Initialize(recovered).Status, Is.EqualTo("InitializedPreview"));
            }
            finally
            {
                probeSystem.OnInitialize = null;
                Discard(stage);
                if (recovered != null) Discard(recovered);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenStageHandle Create(KsProcgenStageKind kind)
    {
        if (kind == KsProcgenStageKind.Surface)
        {
            var result = Pair.Server.System<KsProcgenSurfaceStageSystem>().TryStage("Table", "KitchenMicrowave");
            Assert.That(result.Status, Is.EqualTo(KsProcgenSurfaceStageStatus.PositionedPreview), message: result.Issue?.Code);
            SEntMan.EnsureComponent<KsProcgenLifecycleProbeComponent>(result.Stage!.SubjectUid);
            return result.Stage;
        }
        var members = kind == KsProcgenStageKind.Container
            ? new List<KsProcgenAssemblyMember> { new() { Id = "Host", Entity = "ComputerShuttle" }, new() { Id = "Disk", Entity = "CoordinatesDisk" } }
            : new List<KsProcgenAssemblyMember> { new() { Id = "Host", Entity = "Table" }, new() { Id = "Disk", Entity = "KitchenMicrowave" } };
        var relation = new KsProcgenAssemblyRelation
        {
            Id = "Supported", Subject = "Disk", Target = "Host",
            Kind = kind == KsProcgenStageKind.Container ? KsProcgenRelationKind.InContainer : KsProcgenRelationKind.OnSurface,
            ContainerId = kind == KsProcgenStageKind.Container ? "disk_slot" : null,
        };
        var definition = new KsProcgenAssemblyVariant { Id = "Base", AnchorMember = "Host", Members = members, Relations = new() { relation } };
        var prototypes = Pair.Server.ResolveDependency<IPrototypeManager>();
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Lifecycle", definition, new Dictionary<string, string>(),
            id => prototypes.HasIndex<EntityPrototype>(id), out var assembly, out var issue), Is.True, message: issue?.Code);
        KsProcgenStageHandle stage;
        EntityUid hostUid;
        if (kind == KsProcgenStageKind.Container)
        {
            var result = Pair.Server.System<KsProcgenContainerStageSystem>().TryStage(assembly!, new[] { "Host", "Disk" });
            Assert.That(result.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), message: result.Issue?.Code);
            stage = result.Stage!; hostUid = result.Stage!.Members["Host"];
        }
        else
        {
            var result = Pair.Server.System<KsProcgenAssemblyStageSystem>().TryStage(assembly!, new[] { "Host", "Disk" },
                new[] { new KsProcgenFloorRootPose("Host", Vector2.Zero) },
                new[] { new KsProcgenMemberOrientation("Host", 0), new KsProcgenMemberOrientation("Disk", 0) }, 0);
            Assert.That(result.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), message: result.Issue?.Code);
            stage = result.Stage!; hostUid = result.Stage!.Members["Host"];
        }
        SEntMan.EnsureComponent<KsProcgenLifecycleProbeComponent>(hostUid);
        return stage;
    }

    private (string Status, KsProcgenIssue? Issue) Initialize(KsProcgenStageHandle stage, CancellationToken token = default)
    {
        switch (stage)
        {
            case KsProcgenSurfaceStage surface:
                var surfaceResult = Pair.Server.System<KsProcgenSurfaceStageSystem>().TryInitialize(surface, cancellationToken: token);
                return (surfaceResult.Status.ToString(), surfaceResult.Issue);
            case KsProcgenContainerStage container:
                var containerResult = Pair.Server.System<KsProcgenContainerStageSystem>().TryInitialize(container, cancellationToken: token);
                return (containerResult.Status.ToString(), containerResult.Issue);
            default:
                var assemblyResult = Pair.Server.System<KsProcgenAssemblyStageSystem>().TryInitialize((KsProcgenAssemblyStage) stage, cancellationToken: token);
                return (assemblyResult.Status.ToString(), assemblyResult.Issue);
        }
    }

    private bool Discard(KsProcgenStageHandle stage) => stage switch
    {
        KsProcgenSurfaceStage surface => Pair.Server.System<KsProcgenSurfaceStageSystem>().Discard(surface),
        KsProcgenContainerStage container => Pair.Server.System<KsProcgenContainerStageSystem>().Discard(container),
        _ => Pair.Server.System<KsProcgenAssemblyStageSystem>().Discard((KsProcgenAssemblyStage) stage),
    };
}
