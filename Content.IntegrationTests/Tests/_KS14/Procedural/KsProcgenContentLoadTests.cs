using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Content.Shared.Placeable;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenGeometry))]
public sealed class KsProcgenContentLoadTests : GameTest
{
    [Test]
    public async Task SupportedPoseProposalsMatchRealSurfaceDropsAndContainerInsertion()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = Pair.Server.ResolveDependency<IComponentFactory>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var handsSystem = Pair.Server.System<SharedHandsSystem>();
            var containerStageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            KsProcgenContainerStage? containerStage = null;
            try
            {
                var userUid = SEntMan.SpawnEntity(null, new EntityCoordinates(mapUid, 2f, 2f));
                var handsComponent = SEntMan.AddComponent<HandsComponent>(userUid);
                handsSystem.AddHand((userUid, handsComponent), "TestHand", HandLocation.Middle);
                foreach (var surfacePrototype in new[] { "KsProcgenTrackedSurfaceFixture", "Table" })
                foreach (var turn in Enumerable.Range(0, 4))
                {
                    var definition = new KsProcgenAssemblyVariant
                    {
                        Id = "Base", AnchorMember = "Table",
                        Members = [new() { Id = "Table", Entity = surfacePrototype,
                            RotationMode = KsProcgenMemberRotation.Independent },
                            new() { Id = "Item", Entity = "CoordinatesDisk",
                                RotationMode = KsProcgenMemberRotation.Independent }],
                        Relations = [new() { Id = "Drop", Subject = "Item", Target = "Table", Kind = KsProcgenRelationKind.OnSurface }],
                    };
                    Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("PoseFixture", definition,
                        new Dictionary<string, string>(), id => manager.TryIndex<EntityPrototype>(id, out _),
                        out var assembly, out var issue), Is.True, issue?.Message);
                    Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(manager, factory,
                        assembly!, out var report, out issue), Is.True, issue?.Message);
                    var plan = KsProcgenAssemblyPosePlanner.Plan(assembly!, ["Table", "Item"], report!,
                        [new("Table", new(2f, 3f))], [new("Table", turn), new("Item", 0)], 0);
                    Assert.That(plan.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation), plan.Issue?.Code);
                    var surfaceUid = SEntMan.SpawnEntity(surfacePrototype, new EntityCoordinates(mapUid, 2f, 3f));
                    var subjectUid = SEntMan.SpawnEntity("CoordinatesDisk", new EntityCoordinates(mapUid, 2f, 2f));
                    transformSystem.SetLocalRotation(surfaceUid, Angle.FromDegrees((double) turn * 90.0));
                    Assert.That(SEntMan.GetComponent<TransformComponent>(surfaceUid).LocalRotation,
                        Is.EqualTo(Angle.FromDegrees((double) turn * 90.0)));
                    Assert.That(handsSystem.TryPickupAnyHand(userUid, subjectUid), Is.True);
                    var dropEvent = new AfterInteractUsingEvent(userUid, subjectUid, surfaceUid,
                        new EntityCoordinates(mapUid, 2f, 3f), true);
                    SEntMan.EventBus.RaiseLocalEvent(surfaceUid, dropEvent);
                    Assert.That(dropEvent.Handled, Is.True);
                    var proposed = plan.Members.Single(pose => pose.Support.MemberId == "Item");
                    var actual = SEntMan.GetComponent<TransformComponent>(subjectUid);
                    Assert.That(actual.ParentUid, Is.EqualTo(mapUid));
                    Assert.That(actual.LocalPosition, Is.EqualTo(proposed.Position));
                    Assert.That(proposed.CoordinateParentMemberId, Is.Null);
                    Assert.That(plan.EnginePlacementVerified, Is.False);
                    SEntMan.DeleteEntity(subjectUid);
                    SEntMan.DeleteEntity(surfaceUid);
                }

                var storedAssembly = CompileContainerFixture(manager, "ComputerShuttle");
                var staged = containerStageSystem.TryStage(storedAssembly, ["Host", "Disk"]);
                Assert.That(staged.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), staged.Issue?.Code);
                containerStage = staged.Stage!;
                Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(manager, factory,
                    storedAssembly, out var storedReport, out var storedIssue), Is.True, storedIssue?.Message);
                var host = SEntMan.GetComponent<TransformComponent>(containerStage.Members["Host"]);
                var storedPlan = KsProcgenAssemblyPosePlanner.Plan(storedAssembly, ["Host", "Disk"], storedReport!,
                    [new("Host", host.LocalPosition)], [new("Host", 0), new("Disk", 0)], 0);
                Assert.That(storedPlan.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
                var diskPose = storedPlan.Members.Single(pose => pose.Support.MemberId == "Disk");
                var disk = SEntMan.GetComponent<TransformComponent>(containerStage.Members["Disk"]);
                Assert.That(disk.ParentUid, Is.EqualTo(containerStage.Members[diskPose.CoordinateParentMemberId!]));
                Assert.That(disk.LocalPosition, Is.EqualTo(diskPose.LocalPosition));
                Assert.That(disk.LocalRotation, Is.EqualTo(Angle.FromDegrees((double) diskPose.LocalQuarterTurns * 90.0)));
            }
            finally
            {
                if (containerStage != null)
                    containerStageSystem.Discard(containerStage);
                mapSystem.DeleteMap(mapId);
            }
        });
    }

    private static KsProcgenResolvedAssembly CompileContainerFixture(IPrototypeManager manager, string hostPrototype)
    {
        var definition = new KsProcgenAssemblyVariant
        {
            Id = "Base", AnchorMember = "Host",
            Members = [new() { Id = "Host", Entity = hostPrototype },
                new() { Id = "Disk", Entity = "CoordinatesDisk" }],
            Relations = [new() { Id = "Stored", Subject = "Disk", Target = "Host",
                Kind = KsProcgenRelationKind.InContainer, ContainerId = "disk_slot" }],
        };
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("RuntimeContainerFixture", definition,
            new Dictionary<string, string>(), id => manager.TryIndex<EntityPrototype>(id, out _),
            out var assembly, out var issue), Is.True, issue?.Message);
        return assembly!;
    }

    [Test]
    public async Task SurfaceContactInspectionRefutesStaleContactsInsideOverlappingBounds()
    {
        var mapId = MapId.Nullspace;
        var mapUid = EntityUid.Invalid;
        var surfaceUid = EntityUid.Invalid;
        var subjectUid = EntityUid.Invalid;
        try
        {
            await Pair.Server.WaitAssertion(() =>
            {
                mapUid = Pair.Server.System<SharedMapSystem>().CreateMap(out mapId, runMapInit: true);
                surfaceUid = SEntMan.SpawnEntity("KsProcgenCircleSensorFixture", new EntityCoordinates(mapUid, 0f, 0f));
                subjectUid = SEntMan.SpawnEntity("KsProcgenCircleBeakerFixture", new EntityCoordinates(mapUid, 0.9f, 0f));
            });
            await RunTicksSync(5);
            await Pair.Server.WaitAssertion(() =>
            {
                var contactSystem = Pair.Server.System<KsProcgenSurfaceContactSystem>();
                Assert.That(contactSystem.Check(subjectUid, surfaceUid).Status, Is.EqualTo(KsProcgenSurfaceContactStatus.Observed));
                Pair.Server.System<SharedTransformSystem>().SetCoordinates(subjectUid, new EntityCoordinates(mapUid, 0.9f, 0.9f));
                var beforeCheck = SEntMan.GetEntities().ToHashSet();
                var stale = contactSystem.Check(subjectUid, surfaceUid);
                Assert.That(stale.Status, Is.EqualTo(KsProcgenSurfaceContactStatus.NoContact));
                Assert.That(stale.SubjectTracked, Is.True, "The engine tracker still contains its old membership before reevaluation.");
                Assert.That(stale.ExaminedRecords, Is.GreaterThan(0));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeCheck));
            });
        }
        finally
        {
            await Pair.Server.WaitPost(() =>
            {
                var mapSystem = Pair.Server.System<SharedMapSystem>();
                if (mapSystem.TryGetMap(mapId, out var ownedMapUid) && ownedMapUid == mapUid)
                    mapSystem.DeleteMap(mapId);
            });
        }
    }

    [Test]
    public async Task SurfaceContactInspectionRejectsHardContactsAndExhaustedRecordBudgets()
    {
        EntityUid mapUid = EntityUid.Invalid;
        EntityUid subjectUid = EntityUid.Invalid;
        EntityUid surfaceUid = EntityUid.Invalid;
        KsProcgenSurfaceContact? hardObservation = null;
        var mapId = MapId.Nullspace;
        try
        {
            await Pair.Server.WaitAssertion(() =>
            {
                var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
                var contactSystem = Pair.Server.System<KsProcgenSurfaceContactSystem>();
                mapUid = Pair.Server.System<SharedMapSystem>().CreateMap(out mapId, runMapInit: true);
                surfaceUid = SEntMan.SpawnEntity("ChemistryHotplate", new EntityCoordinates(mapUid, 0f, 0f));
                SEntMan.SpawnEntity("ChemistryHotplate", new EntityCoordinates(mapUid, 0.02f, 0f));
                subjectUid = SEntMan.SpawnEntity("Beaker", new EntityCoordinates(mapUid, 0f, 0.1f));
                listenerSystem.TargetPrototypeId = "KsProcgenHardContactHotplateFixture";
                listenerSystem.ItemPlacedAction = (itemUid, targetUid) => hardObservation = contactSystem.Check(itemUid, targetUid);
                SEntMan.SpawnEntity("KsProcgenHardContactHotplateFixture", new EntityCoordinates(mapUid, 10f, 0f));
                SEntMan.SpawnEntity("Beaker", new EntityCoordinates(mapUid, 10f, 0.1f));
            });
            await RunTicksSync(5);
            await Pair.Server.WaitAssertion(() =>
            {
                var contactSystem = Pair.Server.System<KsProcgenSurfaceContactSystem>();
                Assert.That(hardObservation, Is.Not.Null, "Capture read-only evidence from the real engine contact callback before separation.");
                Assert.That(hardObservation!.Status, Is.EqualTo(KsProcgenSurfaceContactStatus.Rejected));
                Assert.That(hardObservation.Reason, Is.EqualTo("SurfaceHardContactObserved"));
                Assert.That(hardObservation.HardContactRecords, Is.GreaterThan(0));
                Assert.That(hardObservation.PlacementVerified, Is.False);
                var complete = contactSystem.Check(subjectUid, surfaceUid);
                Assert.That(complete.Status, Is.EqualTo(KsProcgenSurfaceContactStatus.Observed), complete.Reason);
                Assert.That(complete.ExaminedRecords, Is.GreaterThan(1));
                var exhausted = contactSystem.Check(subjectUid, surfaceUid, maxContactRecords: 1);
                Assert.That(exhausted.Status, Is.EqualTo(KsProcgenSurfaceContactStatus.BudgetExceeded));
                Assert.That(exhausted.Reason, Is.EqualTo("SurfaceContactRecordBudget"));
                Assert.That(exhausted.PlacementVerified, Is.False);
            });
        }
        finally
        {
            await Pair.Server.WaitPost(() =>
            {
                Pair.Server.System<KsProcgenInsertionTestSystem>().Reset();
                var mapSystem = Pair.Server.System<SharedMapSystem>();
                if (mapSystem.TryGetMap(mapId, out var ownedMapUid) && ownedMapUid == mapUid)
                    mapSystem.DeleteMap(mapId);
            });
        }
    }

    [Test]
    [TestCase("ChemistryHotplate", 1u)]
    [TestCase("KsProcgenUnlimitedHotplateFixture", 0u)]
    public async Task SurfaceContactsUseRealPhysicsTrackingAndReleaseCapacity(string surfacePrototype, uint capacity)
    {
        EntityUid mapUid = EntityUid.Invalid;
        EntityUid surfaceUid = EntityUid.Invalid;
        EntityUid firstUid = EntityUid.Invalid;
        EntityUid secondUid = EntityUid.Invalid;
        var mapId = MapId.Nullspace;
        try
        {
            await Pair.Server.WaitAssertion(() =>
            {
                var mapSystem = Pair.Server.System<SharedMapSystem>();
                var contactSystem = Pair.Server.System<KsProcgenSurfaceContactSystem>();
                mapUid = mapSystem.CreateMap(out mapId, runMapInit: true);
                surfaceUid = SEntMan.SpawnEntity(surfacePrototype, new EntityCoordinates(mapUid, 0f, 0f));
                firstUid = SEntMan.SpawnEntity("Beaker", new EntityCoordinates(mapUid, 0f, 0.1f));
                secondUid = SEntMan.SpawnEntity("Beaker", new EntityCoordinates(mapUid, 5f, 0f));
                Assert.That(contactSystem.Check(firstUid, surfaceUid).Status, Is.EqualTo(KsProcgenSurfaceContactStatus.NoContact));
                Assert.That(contactSystem.Check(firstUid, surfaceUid, maxContactRecords: 0).Status,
                    Is.EqualTo(KsProcgenSurfaceContactStatus.InvalidInput));
                Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(surfaceUid).PlacedEntities, Is.Empty);
            });
            await RunTicksSync(5);
            await Pair.Server.WaitAssertion(() =>
            {
                var contactSystem = Pair.Server.System<KsProcgenSurfaceContactSystem>();
                var observed = contactSystem.Check(firstUid, surfaceUid);
                Assert.That(observed.Status, Is.EqualTo(KsProcgenSurfaceContactStatus.Observed), observed.Reason);
                Assert.That(observed.SoftContactRecords, Is.GreaterThan(0));
                Assert.That(observed.HardContactRecords, Is.Zero);
                Assert.That(observed.SubjectTracked, Is.True);
                Assert.That(observed.TrackedEntities, Is.EqualTo(1));
                Assert.That(observed.MaximumTrackedEntities, Is.EqualTo(capacity));
                Assert.That(observed.SurfaceEnabled, Is.EqualTo(capacity == 0));
                Assert.That(observed.PlacementVerified, Is.False);
                Assert.That(Pair.Server.System<KsProcgenSurfacePreflightSystem>().Check(secondUid, surfaceUid).Status,
                    Is.EqualTo(capacity == 0 ? KsProcgenSurfacePreflightStatus.Candidate : KsProcgenSurfacePreflightStatus.Rejected));
                var beforeCheck = SEntMan.GetEntities().ToHashSet();
                Assert.That(contactSystem.Check(firstUid, surfaceUid), Is.EqualTo(observed));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeCheck));
                Pair.Server.System<SharedTransformSystem>().SetCoordinates(secondUid, new EntityCoordinates(mapUid, 0.05f, 0.1f));
            });
            await RunTicksSync(5);
            await Pair.Server.WaitAssertion(() =>
            {
                var contactSystem = Pair.Server.System<KsProcgenSurfaceContactSystem>();
                var second = contactSystem.Check(secondUid, surfaceUid);
                Assert.That(second.SoftContactRecords, Is.GreaterThan(0), second.Reason);
                Assert.That(second.Status, Is.EqualTo(capacity == 0 ? KsProcgenSurfaceContactStatus.Observed :
                    KsProcgenSurfaceContactStatus.Rejected));
                if (capacity != 0)
                    Assert.That(second.Reason, Is.EqualTo("SurfaceTrackerMissingSubject"));
                Assert.That(second.TrackedEntities, Is.EqualTo(capacity == 0 ? 2 : 1));
                var transformSystem = Pair.Server.System<SharedTransformSystem>();
                transformSystem.SetCoordinates(firstUid, new EntityCoordinates(mapUid, 5f, 0f));
                transformSystem.SetCoordinates(secondUid, new EntityCoordinates(mapUid, 7f, 0f));
                Assert.That(contactSystem.Check(firstUid, surfaceUid).Status, Is.EqualTo(KsProcgenSurfaceContactStatus.NoContact),
                    "Disjoint current bounds refute cached sleeping contact before another physics step.");
                var physicsSystem = Pair.Server.System<SharedPhysicsSystem>();
                physicsSystem.SetAwake(firstUid, SEntMan.GetComponent<PhysicsComponent>(firstUid), true);
                physicsSystem.SetAwake(secondUid, SEntMan.GetComponent<PhysicsComponent>(secondUid), true);
            });
            await RunTicksSync(5);
            await Pair.Server.WaitAssertion(() =>
            {
                var contactSystem = Pair.Server.System<KsProcgenSurfaceContactSystem>();
                Assert.That(contactSystem.Check(firstUid, surfaceUid).Status, Is.EqualTo(KsProcgenSurfaceContactStatus.NoContact));
                Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(surfaceUid).PlacedEntities, Is.Empty);
                Assert.That(SEntMan.GetComponent<PlaceableSurfaceComponent>(surfaceUid).IsPlaceable, Is.True);
                Assert.That(Pair.Server.System<KsProcgenSurfacePreflightSystem>().Check(firstUid, surfaceUid).Status,
                    Is.EqualTo(KsProcgenSurfacePreflightStatus.Candidate));
            });
        }
        finally
        {
            await Pair.Server.WaitPost(() =>
            {
                var mapSystem = Pair.Server.System<SharedMapSystem>();
                if (mapSystem.TryGetMap(mapId, out var ownedMapUid) && ownedMapUid == mapUid)
                    mapSystem.DeleteMap(mapId);
            });
        }
    }

    [Test]
    public async Task SurfacePreviewInitializationRetainsPausedPosesAndInitializesOnlyOnce()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stageSystem = Pair.Server.System<KsProcgenSurfaceStageSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                foreach (var subjectPrototype in new[] { "KitchenMicrowave", "Paper", "CoordinatesDisk" })
                {
                    listenerSystem.Reset();
                    listenerSystem.TargetPrototypeId = subjectPrototype;
                    var surfacePrototype = subjectPrototype == "CoordinatesDisk" ? "KsProcgenTrackedSurfaceFixture" : "Table";
                    var staged = stageSystem.TryStage(surfacePrototype, subjectPrototype);
                    Assert.That(staged.Status, Is.EqualTo(KsProcgenSurfaceStageStatus.PositionedPreview), staged.Issue?.Code);
                    var stage = staged.Stage!;
                    var beforeInitialization = stage.SpawnedEntityCount;
                    var initialized = stageSystem.TryInitialize(stage);
                    Assert.That(initialized.Status, Is.EqualTo(KsProcgenSurfaceStageStatus.InitializedPreview), initialized.Issue?.Code);
                    Assert.That(initialized.Stage, Is.SameAs(stage));
                    Assert.That(stage.Phase, Is.EqualTo(KsProcgenSurfaceStagePhase.Initialized));
                    Assert.That(mapSystem.IsInitialized(stage.MapId), Is.True);
                    Assert.That(mapSystem.IsPaused(stage.MapId), Is.True);
                    Assert.That(stageSystem.Verify(stage), Is.True);
                    Assert.That(stage.PlacementVerified, Is.False);
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(stage.SurfaceUid).EntityLifeStage,
                        Is.EqualTo(EntityLifeStage.MapInitialized));
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(stage.SubjectUid).EntityLifeStage,
                        Is.EqualTo(EntityLifeStage.MapInitialized));
                    if (subjectPrototype == "KitchenMicrowave")
                        Assert.That(stage.SpawnedEntityCount, Is.GreaterThan(beforeInitialization), "Real machine initialization creates owned content.");
                    if (subjectPrototype == "CoordinatesDisk")
                    {
                        Assert.That(listenerSystem.MapInitAttempts, Is.EqualTo(1));
                        Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(stage.SurfaceUid).PlacedEntities, Is.Empty);
                    }
                    var initializedAllocations = stage.SpawnedEntityCount;
                    Assert.That(stageSystem.TryInitialize(stage).Status, Is.EqualTo(KsProcgenSurfaceStageStatus.InitializedPreview));
                    Assert.That(stage.SpawnedEntityCount, Is.EqualTo(initializedAllocations));
                    if (subjectPrototype == "CoordinatesDisk")
                        Assert.That(listenerSystem.MapInitAttempts, Is.EqualTo(1));
                    Assert.That(stageSystem.Discard(stage), Is.True);
                    Assert.That(stageSystem.TryInitialize(stage).Issue?.Code, Is.EqualTo("SurfaceStageNotOwned"));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                }
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task SurfacePreviewInitializationRejectsLifecyclePoseAndFilterChanges()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stageSystem = Pair.Server.System<KsProcgenSurfaceStageSystem>();
            var surfaceSystem = Pair.Server.System<PlaceableSurfaceSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var tagSystem = Pair.Server.System<TagSystem>();
            ProtoId<TagPrototype> filterTag = "KsProcgenBlockedStorageFixture";
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                foreach (var fault in new[] { "Precondition", "Offset", "Disabled", "Filter", "Position", "Replacement" })
                {
                    listenerSystem.Reset();
                    var diskPrototype = fault switch
                    {
                        "Replacement" => "KsProcgenInitializingDiskFixture",
                        "Filter" => "KsProcgenSurfaceTaggedDiskFixture",
                        _ => "CoordinatesDisk",
                    };
                    var stage = stageSystem.TryStage(fault == "Filter" ? "KsProcgenTaggedSurfaceFixture" :
                        "KsProcgenTrackedSurfaceFixture", diskPrototype).Stage!;
                    listenerSystem.TargetPrototypeId = diskPrototype;
                    listenerSystem.MapInitAction = () =>
                    {
                        switch (fault)
                        {
                            case "Offset":
                                surfaceSystem.SetPositionOffset(stage.SurfaceUid, new System.Numerics.Vector2(0.5f, 0f));
                                break;
                            case "Disabled":
                                surfaceSystem.SetPlaceable(stage.SurfaceUid, false);
                                break;
                            case "Filter":
                                Assert.That(tagSystem.RemoveTag(stage.SubjectUid, filterTag), Is.True);
                                break;
                            case "Position":
                                transformSystem.SetCoordinates(stage.SubjectUid, new EntityCoordinates(stage.MapUid, 8f, 0f));
                                break;
                        }
                    };
                    if (fault == "Precondition")
                        surfaceSystem.SetPlaceable(stage.SurfaceUid, false);
                    var failed = stageSystem.TryInitialize(stage);
                    Assert.That(failed.Status, Is.EqualTo(KsProcgenSurfaceStageStatus.Rejected), fault);
                    Assert.That(failed.Issue?.Code, Is.EqualTo(fault switch
                    {
                        "Offset" => "SurfaceStageDropOffsetDrift",
                        "Disabled" or "Precondition" => "LiveSurfaceDisabled",
                        "Filter" => "SurfaceTrackingWhitelistDenied",
                        _ => "SurfaceStageSubjectDrift",
                    }), fault);
                    Assert.That(failed.Stage, Is.Null);
                    Assert.That(stage.Active, Is.False);
                    Assert.That(stageSystem.Verify(stage), Is.False);
                    Assert.That(listenerSystem.MapInitAttempts, Is.EqualTo(fault == "Precondition" ? 0 : 1));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                }
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task SurfacePreviewInitializationCleansFaultsAndPreservesUnrelatedPreviews()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stageSystem = Pair.Server.System<KsProcgenSurfaceStageSystem>();
            var containerSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var operationSystem = Pair.Server.System<KsProcgenPreviewOperationSystem>();
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                var survivor = stageSystem.TryStage("Table", "KitchenMicrowave").Stage!;
                Assert.That(stageSystem.TryInitialize(survivor).Status, Is.EqualTo(KsProcgenSurfaceStageStatus.InitializedPreview));
                var assembly = CompileContainerFixture(Pair.Server.ResolveDependency<IPrototypeManager>(), "ComputerShuttle");
                var containerStage = containerSystem.TryStage(assembly, ["Host", "Disk"]).Stage!;
                var beforeFaults = SEntMan.GetEntities().ToHashSet();
                foreach (var fault in new[] { "Precancel", "Cancel", "Exception", "Budget", "OffMap" })
                {
                    listenerSystem.Reset();
                    var stage = stageSystem.TryStage("KsProcgenTrackedSurfaceFixture", "CoordinatesDisk",
                        maxSpawnedEntities: fault == "Budget" ? 3 : 256).Stage!;
                    using var cancellationSource = new CancellationTokenSource();
                    listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                    listenerSystem.CancellationSource = cancellationSource;
                    listenerSystem.CancelTokenOnMapInit = fault == "Cancel";
                    listenerSystem.ThrowOnMapInit = fault == "Exception";
                    listenerSystem.SpawnExtraOnMapInit = fault is "Budget" or "OffMap" ? 1 : 0;
                    listenerSystem.SpawnExtraOutsideMap = true;
                    listenerSystem.MapInitAction = () =>
                    {
                        Assert.That(stageSystem.TryInitialize(stage).Issue?.Code, Is.EqualTo("SurfaceStageOperationActive"));
                        Assert.That(stageSystem.TryStage("Table", "Paper").Issue?.Code, Is.EqualTo("SurfaceStageOperationActive"));
                        Assert.That(containerSystem.TryInitialize(containerStage).Issue?.Code, Is.EqualTo("ContainerStageOperationActive"));
                        Assert.That(stageSystem.Discard(survivor), Is.False);
                        Assert.That(stageSystem.DiscardAll(), Is.Zero);
                    };
                    if (fault == "Precancel")
                        cancellationSource.Cancel();
                    var failed = stageSystem.TryInitialize(stage, cancellationToken: cancellationSource.Token);
                    Assert.That(failed.Status, Is.EqualTo(fault switch
                    {
                        "Precancel" or "Cancel" => KsProcgenSurfaceStageStatus.Cancelled,
                        "Exception" => KsProcgenSurfaceStageStatus.EngineFailure,
                        "Budget" => KsProcgenSurfaceStageStatus.BudgetExceeded,
                        _ => KsProcgenSurfaceStageStatus.Rejected,
                    }), fault);
                    Assert.That(failed.Issue?.Code, Is.EqualTo(fault switch
                    {
                        "Precancel" or "Cancel" => "SurfaceStageInitializationCancelled",
                        "Exception" => "SurfaceStageInitializationEngineFailure",
                        "Budget" => "SurfaceStageInitializationSpawnBudget",
                        _ => "SurfaceStageGeneratedEntityOffMap",
                    }), fault);
                    Assert.That(failed.Stage, Is.Null);
                    Assert.That(stage.Active, Is.False);
                    Assert.That(operationSystem.Active, Is.False);
                    Assert.That(stageSystem.Verify(survivor), Is.True);
                    Assert.That(containerSystem.Verify(containerStage), Is.True);
                    Assert.That(listenerSystem.ExtraSpawnedEntities.All(uid => !SEntMan.EntityExists(uid)), Is.True);
                    Assert.That(listenerSystem.ExtraSpawnedEntities.Count, Is.EqualTo(fault is "Budget" or "OffMap" ? 1 : 0));
                    if (fault is "Budget" or "OffMap")
                        Assert.That(stage.SpawnedEntityCount, Is.EqualTo(4), "Map, surface, subject and off-map initializer item are charged.");
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFaults));
                }
                listenerSystem.Reset();
                using (var cancelledSource = new CancellationTokenSource())
                {
                    cancelledSource.Cancel();
                    Assert.That(stageSystem.TryInitialize(survivor, cancellationToken: cancelledSource.Token).Status,
                        Is.EqualTo(KsProcgenSurfaceStageStatus.Cancelled));
                    Assert.That(survivor.Active, Is.False, "Cancellation also discards an initialized owned preview.");
                }
                Assert.That(containerSystem.Discard(containerStage), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
                containerSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task OwnedSurfaceDropPreviewVerifiesCoordinatesOrientationAndRejectsDrift()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stageSystem = Pair.Server.System<KsProcgenSurfaceStageSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var surfaceSystem = Pair.Server.System<PlaceableSurfaceSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                foreach (var quarterTurns in new[] { 0, 1, 2, 3 })
                {
                    var result = stageSystem.TryStage("Table", "KsProcgenRotatableMicrowaveFixture", subjectQuarterTurns: quarterTurns);
                    Assert.That(result.Status, Is.EqualTo(KsProcgenSurfaceStageStatus.PositionedPreview),
                        $"Quarter-turn {quarterTurns}: {result.Issue?.Code}");
                    var stage = result.Stage!;
                    Assert.That(stageSystem.Verify(stage), Is.True);
                    Assert.That(stage.PlacementVerified, Is.False);
                    Assert.That(mapSystem.IsPaused(stage.MapId), Is.True);
                    Assert.That(mapSystem.IsInitialized(stage.MapId), Is.False);
                    Assert.That(SEntMan.GetComponent<TransformComponent>(stage.SubjectUid).ParentUid, Is.EqualTo(stage.MapUid));
                    Assert.That(SEntMan.GetComponent<TransformComponent>(stage.SubjectUid).LocalPosition, Is.EqualTo(stage.DropPosition));
                    Assert.That(SEntMan.GetComponent<TransformComponent>(stage.SubjectUid).LocalRotation,
                        Is.EqualTo(Angle.FromDegrees((double) quarterTurns * 90.0)));
                    transformSystem.SetLocalRotation(stage.SubjectUid, Angle.FromDegrees((double) ((quarterTurns + 1) % 4) * 90.0));
                    Assert.That(stageSystem.Verify(stage), Is.False);
                    transformSystem.SetLocalRotation(stage.SubjectUid, Angle.FromDegrees((double) quarterTurns * 90.0));
                    Assert.That(stageSystem.Verify(stage), Is.True);
                    transformSystem.SetCoordinates(stage.SubjectUid, new EntityCoordinates(stage.MapUid, 7f, 0f));
                    Assert.That(stageSystem.Verify(stage), Is.False);
                    Assert.That(stageSystem.Discard(stage), Is.True);
                    Assert.That(stageSystem.Discard(stage), Is.False);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                }
                Assert.That(stageSystem.TryStage("Table", "KitchenMicrowave", subjectQuarterTurns: 1).Issue?.Code,
                    Is.EqualTo("SurfaceStageRotationUnsupported"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                var tracked = stageSystem.TryStage("KsProcgenTrackedSurfaceFixture", "CoordinatesDisk").Stage!;
                Assert.That(tracked.DropPosition, Is.EqualTo(new System.Numerics.Vector2(0.25f, 0f)));
                Assert.That(stageSystem.Verify(tracked), Is.True);
                surfaceSystem.SetPlaceable(tracked.SurfaceUid, false);
                Assert.That(stageSystem.Verify(tracked), Is.False);
                surfaceSystem.SetPlaceable(tracked.SurfaceUid, true);
                Assert.That(stageSystem.Verify(tracked), Is.True);
                Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(tracked.SurfaceUid).PlacedEntities, Is.Empty,
                    "A paused coordinate preview must not fabricate contact or tracker membership.");
                surfaceSystem.SetPositionOffset(tracked.SurfaceUid, new System.Numerics.Vector2(0.5f, 0f));
                Assert.That(stageSystem.Verify(tracked), Is.False);
                Assert.That(stageSystem.Discard(tracked), Is.True);
                var offMap = stageSystem.TryStage("Table", "KitchenMicrowave").Stage!;
                transformSystem.SetCoordinates(offMap.SubjectUid, EntityCoordinates.Invalid);
                Assert.That(stageSystem.Verify(offMap), Is.False);
                Assert.That(stageSystem.Discard(offMap), Is.True);
                Assert.That(SEntMan.EntityExists(offMap.SubjectUid), Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
            }
            finally
            {
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task SurfaceDropPreviewBoundsAllocationRetentionAndCleansRejectedPairs()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var stageSystem = Pair.Server.System<KsProcgenSurfaceStageSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                Assert.That(stageSystem.TryStage("Table", "Paper", subjectQuarterTurns: 4).Status,
                    Is.EqualTo(KsProcgenSurfaceStageStatus.InvalidInput));
                Assert.That(stageSystem.TryStage("Table", "Paper", maxSpawnedEntities: 257).Status,
                    Is.EqualTo(KsProcgenSurfaceStageStatus.InvalidInput));
                Assert.That(stageSystem.TryStage("Table", "Paper", maxSpawnedEntities: 2).Issue?.Code,
                    Is.EqualTo("SurfaceStageSpawnBudget"));
                Assert.That(stageSystem.TryStage("KsProcgenTrackedSurfaceFixture", "Paper").Issue?.Code,
                    Is.EqualTo("SurfaceTrackingWhitelistDenied"));
                Assert.That(stageSystem.TryStage("Paper", "KitchenMicrowave").Issue?.Code, Is.EqualTo("MissingLiveSurface"));
                using (var cancelledSource = new CancellationTokenSource())
                {
                    cancelledSource.Cancel();
                    Assert.That(stageSystem.TryStage("Table", "Paper", cancellationToken: cancelledSource.Token).Status,
                        Is.EqualTo(KsProcgenSurfaceStageStatus.Cancelled));
                }
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                listenerSystem.SpawnExtraOnStartup = 2;
                Assert.That(stageSystem.TryStage("KsProcgenTrackedSurfaceFixture", "CoordinatesDisk", maxSpawnedEntities: 3)
                    .Issue?.Code, Is.EqualTo("SurfaceStageSpawnBudget"));
                Assert.That(listenerSystem.ExtraSpawnedEntities.Count, Is.EqualTo(2));
                Assert.That(listenerSystem.ExtraSpawnedEntities.All(uid => !SEntMan.EntityExists(uid)), Is.True);
                listenerSystem.Reset();
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                listenerSystem.SpawnExtraOnStartup = 1;
                Assert.That(stageSystem.TryStage("KsProcgenTrackedSurfaceFixture", "CoordinatesDisk", maxSpawnedEntities: 4)
                    .Issue?.Code, Is.EqualTo("SurfaceStageGeneratedEntityOffMap"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                listenerSystem.Reset();
                var retained = new List<KsProcgenSurfaceStage>();
                for (var index = 0; index < KsProcgenSurfaceStageSystem.MaximumRetainedStages; index++)
                    retained.Add(stageSystem.TryStage("Table", "KitchenMicrowave").Stage!);
                var beforeOverflow = SEntMan.GetEntities().ToHashSet();
                Assert.That(stageSystem.TryStage("Table", "Paper").Issue?.Code, Is.EqualTo("SurfaceStageRetentionBudget"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeOverflow));
                Assert.That(stageSystem.Discard(retained[0]), Is.True);
                Assert.That(stageSystem.TryStage("Table", "Paper").Status, Is.EqualTo(KsProcgenSurfaceStageStatus.PositionedPreview));
                Assert.That(stageSystem.DiscardAll(), Is.EqualTo(KsProcgenSurfaceStageSystem.MaximumRetainedStages));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task SurfaceAndContainerPreviewTransactionsRejectCrossAdapterReentrancy()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var surfaceStageSystem = Pair.Server.System<KsProcgenSurfaceStageSystem>();
            var containerStageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var operationSystem = Pair.Server.System<KsProcgenPreviewOperationSystem>();
            var assembly = CompileContainerFixture(Pair.Server.ResolveDependency<IPrototypeManager>(), "ComputerShuttle");
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                var surfaceStage = surfaceStageSystem.TryStage("Table", "KitchenMicrowave").Stage!;
                var containerStage = containerStageSystem.TryStage(assembly, ["Host", "Disk"]).Stage!;
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                listenerSystem.StartupAction = () =>
                {
                    Assert.That(operationSystem.Active, Is.True);
                    Assert.That(containerStageSystem.TryStage(assembly, ["Host", "Disk"]).Issue?.Code,
                        Is.EqualTo("ContainerStageOperationActive"));
                    Assert.That(containerStageSystem.TryInitialize(containerStage).Issue?.Code,
                        Is.EqualTo("ContainerStageOperationActive"));
                    Assert.That(containerStageSystem.Discard(containerStage), Is.False);
                    Assert.That(surfaceStageSystem.Discard(surfaceStage), Is.False);
                    Assert.That(surfaceStageSystem.TryStage("Table", "Paper").Issue?.Code,
                        Is.EqualTo("SurfaceStageOperationActive"));
                };
                using (var cancellationSource = new CancellationTokenSource())
                {
                    var checks = listenerSystem.StartupAction;
                    listenerSystem.StartupAction = () => { checks!(); cancellationSource.Cancel(); };
                    Assert.That(surfaceStageSystem.TryStage("KsProcgenTrackedSurfaceFixture", "CoordinatesDisk",
                        cancellationToken: cancellationSource.Token).Status, Is.EqualTo(KsProcgenSurfaceStageStatus.Cancelled));
                }
                Assert.That(operationSystem.Active, Is.False);
                Assert.That(surfaceStageSystem.Verify(surfaceStage), Is.True);
                Assert.That(containerStageSystem.Verify(containerStage), Is.True);
                listenerSystem.Reset();
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                listenerSystem.ThrowOnStartup = true;
                Assert.That(surfaceStageSystem.TryStage("KsProcgenTrackedSurfaceFixture", "CoordinatesDisk").Status,
                    Is.EqualTo(KsProcgenSurfaceStageStatus.EngineFailure));
                Assert.That(operationSystem.Active, Is.False);
                listenerSystem.Reset();
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                listenerSystem.MapInitAction = () =>
                {
                    Assert.That(surfaceStageSystem.TryStage("Table", "Paper").Issue?.Code,
                        Is.EqualTo("SurfaceStageOperationActive"));
                    Assert.That(surfaceStageSystem.Discard(surfaceStage), Is.False);
                    Assert.That(surfaceStageSystem.DiscardAll(), Is.Zero);
                };
                Assert.That(containerStageSystem.TryInitialize(containerStage).Status,
                    Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview));
                Assert.That(surfaceStageSystem.Verify(surfaceStage), Is.True);
                Assert.That(operationSystem.Active, Is.False);
                Assert.That(surfaceStageSystem.Discard(surfaceStage), Is.True);
                Assert.That(containerStageSystem.Discard(containerStage), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
            }
            finally
            {
                listenerSystem.Reset();
                surfaceStageSystem.DiscardAll();
                containerStageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task LiveSurfacePreflightRetainsTrackingLimitsWithoutMovingOrMountingEntities()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var createdUids = new List<EntityUid>();
            EntityUid Spawn(string? prototypeId)
            {
                var uid = SEntMan.SpawnEntity(prototypeId, MapCoordinates.Nullspace);
                createdUids.Add(uid);
                return uid;
            }
            try
            {
                var preflightSystem = Pair.Server.System<KsProcgenSurfacePreflightSystem>();
                var surfaceSystem = Pair.Server.System<PlaceableSurfaceSystem>();
                var containerSystem = Pair.Server.System<SharedContainerSystem>();
                var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
                var componentFactory = Pair.Server.ResolveDependency<IComponentFactory>();
                var tableUid = Spawn("Table");
                var microwaveUid = Spawn("KitchenMicrowave");
                var trackerUid = Spawn("KsProcgenTrackedSurfaceFixture");
                var diskUid = Spawn("CoordinatesDisk");
                var paperUid = Spawn("Paper");
                var hostUid = Spawn(null);
                var beforeCheck = SEntMan.GetEntities().ToHashSet();
                var beforeCoordinates = SEntMan.GetComponent<TransformComponent>(microwaveUid).Coordinates;
                var plain = preflightSystem.Check(microwaveUid, tableUid);
                Assert.That(plain.Status, Is.EqualTo(KsProcgenSurfacePreflightStatus.Candidate));
                Assert.That(plain.HasTracker, Is.False);
                Assert.That(plain.MaximumTrackedEntities, Is.Null);
                Assert.That(plain.PlacementVerified, Is.False);
                Assert.That(SEntMan.GetComponent<TransformComponent>(microwaveUid).Coordinates, Is.EqualTo(beforeCoordinates));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeCheck));
                var tracked = preflightSystem.Check(diskUid, trackerUid);
                Assert.That(tracked.Status, Is.EqualTo(KsProcgenSurfacePreflightStatus.Candidate));
                Assert.That(tracked.HasTracker, Is.True);
                Assert.That(tracked.MaximumTrackedEntities, Is.EqualTo(1));
                Assert.That(tracked.TrackedEntities, Is.Zero);
                Assert.That(tracked.Centered, Is.True);
                Assert.That(tracked.PositionOffset, Is.EqualTo(new System.Numerics.Vector2(0.25f, 0f)));
                Assert.That(preflightSystem.Check(paperUid, trackerUid).Reason, Is.EqualTo("SurfaceTrackingWhitelistDenied"));
                SEntMan.RemoveComponent<ShuttleDestinationCoordinatesComponent>(diskUid);
                Assert.That(preflightSystem.Check(diskUid, trackerUid).Reason, Is.EqualTo("SurfaceTrackingWhitelistDenied"));
                SEntMan.AddComponent<ShuttleDestinationCoordinatesComponent>(diskUid);
                Assert.That(preflightSystem.Check(diskUid, trackerUid).Status, Is.EqualTo(KsProcgenSurfacePreflightStatus.Candidate));
                surfaceSystem.SetPlaceable(trackerUid, false);
                Assert.That(preflightSystem.Check(diskUid, trackerUid).Reason, Is.EqualTo("LiveSurfaceDisabled"));
                surfaceSystem.SetPlaceable(trackerUid, true);
                surfaceSystem.SetPositionOffset(trackerUid, new System.Numerics.Vector2(float.NaN, 0f));
                Assert.That(preflightSystem.Check(diskUid, trackerUid).Reason, Is.EqualTo("InvalidSurfaceOffset"));
                surfaceSystem.SetPositionOffset(trackerUid, new System.Numerics.Vector2(0.25f, 0f));
                Assert.That(preflightSystem.Check(diskUid, trackerUid, slotId: "Top").Status,
                    Is.EqualTo(KsProcgenSurfacePreflightStatus.UnsupportedContent));
                Assert.That(preflightSystem.Check(diskUid, trackerUid, slotId: " ").Status,
                    Is.EqualTo(KsProcgenSurfacePreflightStatus.InvalidInput));
                Assert.That(preflightSystem.Check(tableUid, tableUid).Reason, Is.EqualTo("SurfaceSelfPlacement"));
                Assert.That(preflightSystem.Check(tableUid, trackerUid).Reason, Is.EqualTo("SurfaceSubjectNotDroppable"));
                Assert.That(preflightSystem.Check(diskUid, hostUid).Reason, Is.EqualTo("MissingLiveSurface"));
                var contained = containerSystem.EnsureContainer<Container>(hostUid, "Test", out _);
                Assert.That(containerSystem.Insert(diskUid, contained, force: false), Is.True);
                Assert.That(preflightSystem.Check(diskUid, trackerUid).Reason, Is.EqualTo("SurfaceEntityContained"));
                Assert.That(containerSystem.Remove(diskUid, contained, force: false), Is.True);
                SEntMan.QueueDeleteEntity(paperUid);
                Assert.That(preflightSystem.Check(paperUid, tableUid).Reason, Is.EqualTo("SurfaceEntityNotReady"));

                Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspect(manager, componentFactory,
                    "KsProcgenTrackedSurfaceFixture", out var declaration, out var issue), Is.True, issue?.Code);
                Assert.That(declaration!.SurfaceTracker!.DeclaredCapacity, Is.EqualTo(1));
                Assert.That(declaration.SurfaceTracker.Whitelist!.Components, Is.EqualTo(new[] { "ShuttleDestinationCoordinates" }));
                Assert.That(declaration.SurfaceTracker.InitiallyTrackedEntities, Is.Zero);
                Assert.That(declaration.EnginePlacementVerified, Is.False);
                Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(trackerUid).PlacedEntities, Is.Empty);
            }
            finally
            {
                foreach (var uid in createdUids.AsEnumerable().Reverse())
                {
                    if (SEntMan.EntityExists(uid))
                        SEntMan.DeleteEntity(uid);
                }
            }
        });
    }

    [Test]
    public async Task LoadedRelationalDeclarationsRejectIgnoredFieldsWithoutChangingPrototypeData()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var prototype = manager.Index<KsProcgenAssemblyPrototype>("KsProcgenRelationalFixture");
            var bindings = new Dictionary<string, string> { ["DeviceEntity"] = "ComputerAlert" };
            var oversizedReference = new KsProcgenAssemblyReference
            {
                Id = new string('x', KsProcgenAssemblyCompiler.MaximumNameLength + 1),
                Assembly = prototype.ID, Bindings = bindings,
            };
            Assert.That(KsProcgenAssemblyCompiler.TryResolve(manager, oversizedReference, out var invalidVariants,
                out var referenceIssue), Is.False);
            Assert.That(invalidVariants, Is.Empty);
            Assert.That(referenceIssue?.Code, Is.EqualTo("InvalidAssemblyReference"));
            oversizedReference.Id = "ValidCore";
            oversizedReference.Bindings = Enumerable.Range(0, KsProcgenAssemblyCompiler.MaximumBindings + 1)
                .ToDictionary(index => $"Binding{index}", _ => "ComputerAlert");
            Assert.That(KsProcgenAssemblyCompiler.TryResolve(manager, oversizedReference, out invalidVariants,
                out referenceIssue), Is.False);
            Assert.That(invalidVariants, Is.Empty);
            Assert.That(referenceIssue?.Code, Is.EqualTo("InvalidAssemblyReference"));
            var definition = new KsProcgenAssemblyVariant
            {
                Id = "Base", AnchorMember = prototype.AnchorMember,
                Members = prototype.Members.ToList(), Relations = prototype.Relations.ToList(),
            };
            Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant(prototype.ID, definition, bindings,
                id => manager.TryIndex<EntityPrototype>(id, out _), out var before, out var issue), Is.True, issue?.Code);
            var brokenAlternative = new KsProcgenAssemblyVariant
            {
                Id = "BrokenStanding", AnchorMember = "Device",
                Members = [new() { Id = "Device", Entity = "KsProcgenMissingVariantEntity" }],
            };
            var beforeFailure = SEntMan.GetEntities().ToHashSet();
            Assert.That(KsProcgenAssemblyCompiler.TryCompileVariants(prototype.ID, [definition, brokenAlternative], bindings,
                id => manager.TryIndex<EntityPrototype>(id, out _), out var rejectedFamily, out issue), Is.False);
            Assert.That(rejectedFamily, Is.Empty, "A valid loaded base cannot conceal a malformed alternative.");
            Assert.That(issue?.Code, Is.EqualTo("InvalidAssemblyEntity"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFailure));
            definition.Relations.Add(new KsProcgenAssemblyRelation
            {
                Id = "UnsupportedFields", Subject = "Device", Target = "Table",
                Kind = KsProcgenRelationKind.FacingTarget, ContainerId = "Storage",
            });
            Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant(prototype.ID, definition, bindings,
                id => manager.TryIndex<EntityPrototype>(id, out _), out var rejected, out issue), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo("InapplicableAssemblyRelationField"));
            var pack = manager.Index<KsProcgenEntityPackPrototype>("KsProcgenRelationalPackFixture");
            Assert.That(KsProcgenAssemblyCompiler.TryResolvePack(manager, pack, out var cores, out issue), Is.True, issue?.Code);
            var replay = cores.Single().Variants.Single(variant => variant.VariantId == "Base");
            Assert.That(replay.Relations, Is.EqualTo(before!.Relations));
            Assert.That(cores.Single().Variants.Count, Is.EqualTo(2), "The complete standing alternative remains intact.");
            Assert.That(prototype.Relations.Any(relation => relation.Id == "UnsupportedFields"), Is.False);
        });
    }

    [Test]
    public async Task ContainerStageSpawnBudgetsIncludeInitializerEntitiesAndBoundRetention()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var assembly = CompileContainerFixture(manager, "ComputerShuttle");
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                Assert.That(stageSystem.TryStage(assembly, ["Host", "Disk"], maxSpawnedEntities: 0).Issue?.Code,
                    Is.EqualTo("InvalidContainerStageSpawnBudget"));
                Assert.That(stageSystem.TryStage(assembly, ["Host", "Disk"], maxSpawnedEntities: 257).Issue?.Code,
                    Is.EqualTo("InvalidContainerStageSpawnBudget"));
                var tooSmall = stageSystem.TryStage(assembly, ["Host", "Disk"], maxSpawnedEntities: 2);
                Assert.That(tooSmall.Status, Is.EqualTo(KsProcgenContainerStageStatus.BudgetExceeded));
                Assert.That(tooSmall.Issue?.Code, Is.EqualTo("ContainerStageSpawnBudget"));
                Assert.That(tooSmall.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));

                var probe = stageSystem.TryStage(assembly, ["Host", "Disk"]).Stage!;
                var initialAllocations = probe.SpawnedEntityCount;
                Assert.That(initialAllocations, Is.GreaterThanOrEqualTo(3), "Map, selected members and engine side effects count.");
                Assert.That(stageSystem.Discard(probe), Is.True);
                var beforeInitializationResult = stageSystem.TryStage(assembly, ["Host", "Disk"], maxSpawnedEntities: initialAllocations);
                Assert.That(beforeInitializationResult.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview),
                    beforeInitializationResult.Issue?.Code);
                var beforeInitialization = beforeInitializationResult.Stage!;
                Assert.That(beforeInitialization.SpawnedEntityCount, Is.EqualTo(initialAllocations));
                var exceeded = stageSystem.TryInitialize(beforeInitialization);
                Assert.That(exceeded.Status, Is.EqualTo(KsProcgenContainerStageStatus.BudgetExceeded));
                Assert.That(exceeded.Issue?.Code, Is.EqualTo("ContainerStageInitializationSpawnBudget"));
                Assert.That(exceeded.Stage, Is.Null);
                Assert.That(beforeInitialization.SpawnedEntityCount, Is.EqualTo(initialAllocations + 1), "The engine-created computer board counts.");
                Assert.That(beforeInitialization.Active, Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));

                var exact = stageSystem.TryStage(assembly, ["Host", "Disk"], maxSpawnedEntities: initialAllocations + 1).Stage!;
                Assert.That(stageSystem.TryInitialize(exact).Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview));
                Assert.That(exact.SpawnedEntityCount, Is.EqualTo(initialAllocations + 1));
                Assert.That(stageSystem.TryInitialize(exact).Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview));
                Assert.That(exact.SpawnedEntityCount, Is.EqualTo(initialAllocations + 1), "Idempotent initialization does not charge allocations twice.");
                Assert.That(stageSystem.Discard(exact), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));

                var previews = new List<KsProcgenContainerStage>();
                for (var index = 0; index < KsProcgenContainerStageSystem.MaximumRetainedStages; index++)
                {
                    var accepted = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                    Assert.That(accepted.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview));
                    previews.Add(accepted.Stage!);
                }
                var beforeExcess = SEntMan.GetEntities().ToHashSet();
                var excess = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                Assert.That(excess.Status, Is.EqualTo(KsProcgenContainerStageStatus.BudgetExceeded));
                Assert.That(excess.Issue?.Code, Is.EqualTo("ContainerStageRetentionBudget"));
                Assert.That(excess.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeExcess));
                Assert.That(stageSystem.RetainedStageCount, Is.EqualTo(16));
                Assert.That(previews.All(stageSystem.Verify), Is.True);
                Assert.That(stageSystem.Discard(previews[0]), Is.True);
                Assert.That(stageSystem.TryStage(assembly, ["Host", "Disk"]).Status,
                    Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), "Discard releases a retained-stage slot.");
                Assert.That(stageSystem.DiscardAll(), Is.EqualTo(16));
                Assert.That(stageSystem.RetainedStageCount, Is.Zero);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
            }
            finally
            {
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task ContainerStageAccountsForOffMapInitializerSpawnsAndCleansThemUp()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var assembly = CompileContainerFixture(manager, "ComputerShuttle");
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                var probe = stageSystem.TryStage(assembly, ["Host", "Disk"]).Stage!;
                var initialAllocations = probe.SpawnedEntityCount;
                Assert.That(stageSystem.Discard(probe), Is.True);
                foreach (var maxSpawnedEntities in new[] { initialAllocations + 1, initialAllocations + 3 })
                {
                    listenerSystem.Reset();
                    var stage = stageSystem.TryStage(assembly, ["Host", "Disk"], maxSpawnedEntities: maxSpawnedEntities).Stage!;
                    listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                    listenerSystem.SpawnExtraOnMapInit = 2;
                    listenerSystem.SpawnExtraOutsideMap = true;
                    listenerSystem.MapInitAction = () =>
                    {
                        Assert.That(stageSystem.TryStage(assembly, ["Host", "Disk"]).Issue?.Code,
                            Is.EqualTo("ContainerStageOperationActive"));
                        Assert.That(stageSystem.TryInitialize(stage).Issue?.Code,
                            Is.EqualTo("ContainerStageOperationActive"));
                        Assert.That(stageSystem.Discard(stage), Is.False);
                        Assert.That(stageSystem.DiscardAll(), Is.Zero);
                    };
                    var failed = stageSystem.TryInitialize(stage);
                    Assert.That(failed.Status, Is.EqualTo(maxSpawnedEntities == initialAllocations + 1 ?
                        KsProcgenContainerStageStatus.BudgetExceeded : KsProcgenContainerStageStatus.Rejected));
                    Assert.That(failed.Issue?.Code, Is.EqualTo(maxSpawnedEntities == initialAllocations + 1 ?
                        "ContainerStageInitializationSpawnBudget" : "ContainerStageInitializationFailed"));
                    Assert.That(failed.Stage, Is.Null);
                    Assert.That(stage.SpawnedEntityCount, Is.EqualTo(initialAllocations + 3), "Off-map allocations are still owned and charged.");
                    Assert.That(listenerSystem.ExtraSpawnedEntities.Count, Is.EqualTo(2));
                    Assert.That(listenerSystem.ExtraSpawnedEntities.All(uid => !SEntMan.EntityExists(uid)), Is.True);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                }
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task ContainerStageExceptionsDiscardOwnedEntitiesAndPreserveOtherPreviews()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var assembly = CompileContainerFixture(manager, "ComputerShuttle");
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                var control = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                Assert.That(control.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview));
                var beforeFailure = SEntMan.GetEntities().ToHashSet();
                foreach (var throwOnAttempt in new[] { 1, 2, 3 })
                {
                    listenerSystem.Reset();
                    listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                    listenerSystem.ThrowContainerOnAttempt = throwOnAttempt;
                    var failure = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                    Assert.That(failure.Status, Is.EqualTo(KsProcgenContainerStageStatus.EngineFailure));
                    Assert.That(failure.Issue?.Code, Is.EqualTo("ContainerStageEngineFailure"));
                    Assert.That(failure.Stage, Is.Null);
                    Assert.That(listenerSystem.ContainerAttempts, Is.EqualTo(throwOnAttempt));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFailure));
                    Assert.That(stageSystem.Verify(control.Stage!), Is.True);
                }
                listenerSystem.Reset();
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                listenerSystem.ThrowItemSlotOnAttempt = 2;
                var slotFailure = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                Assert.That(slotFailure.Status, Is.EqualTo(KsProcgenContainerStageStatus.EngineFailure));
                Assert.That(listenerSystem.ItemSlotAttempts, Is.EqualTo(2));
                Assert.That(slotFailure.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFailure));

                listenerSystem.Reset();
                var initializing = stageSystem.TryStage(assembly, ["Host", "Disk"]).Stage!;
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                listenerSystem.ThrowOnMapInit = true;
                var initializationFailure = stageSystem.TryInitialize(initializing);
                Assert.That(initializationFailure.Status, Is.EqualTo(KsProcgenContainerStageStatus.EngineFailure));
                Assert.That(initializationFailure.Issue?.Code, Is.EqualTo("ContainerStageInitializationEngineFailure"));
                Assert.That(initializationFailure.Stage, Is.Null);
                Assert.That(listenerSystem.MapInitAttempts, Is.EqualTo(1));
                Assert.That(initializing.Active, Is.False);
                Assert.That(mapSystem.MapExists(initializing.MapId), Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFailure));
                Assert.That(stageSystem.Verify(control.Stage!), Is.True);
                Assert.That(stageSystem.Discard(control.Stage!), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task ContainerStageCancellationDiscardsOnlyTheCancelledPreview()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var assembly = CompileContainerFixture(manager, "ComputerShuttle");
            var beforeTest = SEntMan.GetEntities().ToHashSet();
            try
            {
                var control = stageSystem.TryStage(assembly, ["Host", "Disk"]).Stage!;
                var beforeFailure = SEntMan.GetEntities().ToHashSet();
                using (var cancelledSource = new CancellationTokenSource())
                {
                    cancelledSource.Cancel();
                    var cancelled = stageSystem.TryStage(assembly, ["Host", "Disk"], cancellationToken: cancelledSource.Token);
                    Assert.That(cancelled.Status, Is.EqualTo(KsProcgenContainerStageStatus.Cancelled));
                    Assert.That(cancelled.Issue?.Code, Is.EqualTo("ContainerStageCancelled"));
                    Assert.That(cancelled.Stage, Is.Null);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFailure));
                }
                foreach (var cancelOnAttempt in new[] { 1, 3 })
                {
                    using var cancellationSource = new CancellationTokenSource();
                    listenerSystem.Reset();
                    listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                    listenerSystem.CancellationSource = cancellationSource;
                    listenerSystem.CancelTokenOnContainerAttempt = cancelOnAttempt;
                    var cancelled = stageSystem.TryStage(assembly, ["Host", "Disk"], cancellationToken: cancellationSource.Token);
                    Assert.That(cancelled.Status, Is.EqualTo(KsProcgenContainerStageStatus.Cancelled));
                    Assert.That(cancelled.Issue?.Code, Is.EqualTo("ContainerStageCancelled"));
                    Assert.That(cancelled.Stage, Is.Null);
                    Assert.That(listenerSystem.ContainerAttempts, Is.EqualTo(cancelOnAttempt));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFailure));
                }
                foreach (var duringInitialization in new[] { false, true })
                {
                    listenerSystem.Reset();
                    var initializing = stageSystem.TryStage(assembly, ["Host", "Disk"]).Stage!;
                    using var cancellationSource = new CancellationTokenSource();
                    listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                    listenerSystem.CancellationSource = cancellationSource;
                    listenerSystem.CancelTokenOnMapInit = duringInitialization;
                    if (!duringInitialization)
                        cancellationSource.Cancel();
                    var cancelled = stageSystem.TryInitialize(initializing, cancellationToken: cancellationSource.Token);
                    Assert.That(cancelled.Status, Is.EqualTo(KsProcgenContainerStageStatus.Cancelled));
                    Assert.That(cancelled.Issue?.Code, Is.EqualTo("ContainerStageInitializationCancelled"));
                    Assert.That(cancelled.Stage, Is.Null);
                    Assert.That(initializing.Active, Is.False);
                    Assert.That(mapSystem.MapExists(initializing.MapId), Is.False);
                    Assert.That(listenerSystem.MapInitAttempts, Is.EqualTo(duringInitialization ? 1 : 0));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeFailure));
                    Assert.That(stageSystem.Verify(control), Is.True);
                }
                listenerSystem.Reset();
                using (var cancelledSource = new CancellationTokenSource())
                {
                    Assert.That(stageSystem.TryInitialize(control).Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview));
                    cancelledSource.Cancel();
                    // Cancellation also discards an already initialized owned preview instead of retaining it.
                    Assert.That(stageSystem.TryInitialize(control, cancellationToken: cancelledSource.Token).Status,
                        Is.EqualTo(KsProcgenContainerStageStatus.Cancelled));
                    Assert.That(stageSystem.TryInitialize(control, cancellationToken: cancelledSource.Token).Issue?.Code,
                        Is.EqualTo("ContainerStageNotOwned"));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeTest));
                }
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task RetainedContainerContentsMustMatchLiveFilters()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var itemSlotsSystem = Pair.Server.System<ItemSlotsSystem>();
            var tagSystem = Pair.Server.System<TagSystem>();
            var containerSystem = Pair.Server.System<SharedContainerSystem>();
            var assembly = CompileContainerFixture(manager, "KsProcgenFilteredComputerFixture");
            var beforeStage = SEntMan.GetEntities().ToHashSet();
            try
            {
                // Cover both phases and both filter directions using real engine tag/component matching.
                foreach (var initialize in new[] { false, true })
                foreach (var blacklist in new[] { false, true })
                {
                    var result = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                    Assert.That(result.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), result.Issue?.Code);
                    var stage = result.Stage!;
                    if (initialize)
                        Assert.That(stageSystem.TryInitialize(stage).Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview));
                    var hostUid = stage.Members["Host"];
                    var diskUid = stage.Members["Disk"];
                    itemSlotsSystem.SetLock(hostUid, "disk_slot", true);
                    Assert.That(stageSystem.Verify(stage), Is.True,
                        "Locking an occupied slot does not invalidate compatible stored contents.");
                    Assert.That(itemSlotsSystem.TryGetSlot(hostUid, "disk_slot", out var slot), Is.True);
                    if (blacklist)
                    {
                        ProtoId<TagPrototype> blockedTag = "KsProcgenBlockedStorageFixture";
                        Assert.That(tagSystem.AddTag(diskUid, blockedTag), Is.True);
                    }
                    else
                    {
                        slot!.Whitelist = new EntityWhitelist { Components = ["Buckle"] };
                    }
                    Assert.That(containerSystem.TryGetContainer(hostUid, "disk_slot", out var container), Is.True);
                    Assert.That(container!.Contains(diskUid), Is.True,
                        "The incompatible child must remain contained so membership alone cannot pass this test.");
                    Assert.That(stageSystem.Verify(stage), Is.False);
                    var rejected = stageSystem.TryInitialize(stage);
                    Assert.That(rejected.Status, Is.EqualTo(KsProcgenContainerStageStatus.Rejected));
                    Assert.That(rejected.Issue?.Code, Is.EqualTo("ContainerStageInitializationPrecondition"));
                    Assert.That(rejected.Stage, Is.Null);
                    Assert.That(stage.Active, Is.False);
                    Assert.That(SEntMan.GetEntities().Except(beforeStage), Is.Empty);
                }
            }
            finally
            {
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task ContainerStageHonorsInsertionVetoesAndDoesNotReplayAttempts()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var listenerSystem = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var assembly = CompileContainerFixture(manager, "ComputerShuttle");
            var beforeStage = SEntMan.GetEntities().ToHashSet();
            try
            {
                foreach (var cancelOnAttempt in new[] { 1, 2 })
                {
                    listenerSystem.Reset();
                    listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                    listenerSystem.CancelItemSlotOnAttempt = cancelOnAttempt;
                    var rejected = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                    Assert.That(rejected.Status, Is.EqualTo(KsProcgenContainerStageStatus.Rejected));
                    Assert.That(rejected.Issue?.Code, Is.EqualTo(cancelOnAttempt == 1 ?
                        "ItemSlotInsertionDenied" : "ContainerStageInsertionFailed"));
                    Assert.That(listenerSystem.ItemSlotAttempts, Is.EqualTo(cancelOnAttempt));
                    Assert.That(rejected.Stage, Is.Null);
                    Assert.That(SEntMan.GetEntities().Except(beforeStage), Is.Empty);
                }
                listenerSystem.Reset();
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                // Preflight and TryInsert permission checks succeed, then actual container insertion fails.
                // ItemSlots.TryInsert returns true in this case; the stage must detect absent membership.
                listenerSystem.CancelContainerOnAttempt = 3;
                var failedInsert = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                Assert.That(failedInsert.Status, Is.EqualTo(KsProcgenContainerStageStatus.Rejected));
                Assert.That(failedInsert.Issue?.Code, Is.EqualTo("ContainerStageInsertionFailed"));
                Assert.That(listenerSystem.ContainerAttempts, Is.EqualTo(3));
                Assert.That(failedInsert.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities().Except(beforeStage), Is.Empty);

                listenerSystem.Reset();
                listenerSystem.TargetPrototypeId = "CoordinatesDisk";
                var accepted = stageSystem.TryStage(assembly, ["Host", "Disk"]);
                Assert.That(accepted.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), accepted.Issue?.Code);
                var itemSlotAttempts = listenerSystem.ItemSlotAttempts;
                var containerAttempts = listenerSystem.ContainerAttempts;
                Assert.That(itemSlotAttempts, Is.EqualTo(2));
                Assert.That(containerAttempts, Is.EqualTo(3));
                listenerSystem.CancelItemSlotOnAttempt = itemSlotAttempts + 1;
                listenerSystem.CancelContainerOnAttempt = containerAttempts + 1;
                Assert.That(stageSystem.Verify(accepted.Stage!), Is.True);
                Assert.That(stageSystem.TryInitialize(accepted.Stage!).Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview));
                Assert.That(stageSystem.Verify(accepted.Stage!), Is.True);
                Assert.That(listenerSystem.ItemSlotAttempts, Is.EqualTo(itemSlotAttempts));
                Assert.That(listenerSystem.ContainerAttempts, Is.EqualTo(containerAttempts));
                Assert.That(stageSystem.Discard(accepted.Stage!), Is.True);
                Assert.That(SEntMan.GetEntities().Except(beforeStage), Is.Empty);
            }
            finally
            {
                listenerSystem.Reset();
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task ContainerStageInitializationPreservesMembersOrDiscardsPreview()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var containerSystem = Pair.Server.System<SharedContainerSystem>();
            KsProcgenContainerStage MakeStage(string diskPrototype)
            {
                var definition = new KsProcgenAssemblyVariant
                {
                    Id = "Base", AnchorMember = "Host",
                    Members = [new() { Id = "Host", Entity = "ComputerShuttle" },
                        new() { Id = "Disk", Entity = diskPrototype }],
                    Relations = [new() { Id = "Stored", Subject = "Disk", Target = "Host",
                        Kind = KsProcgenRelationKind.InContainer, ContainerId = "disk_slot" }],
                };
                Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("InitializationFixture", definition,
                    new Dictionary<string, string>(), id => manager.TryIndex<EntityPrototype>(id, out _),
                    out var assembly, out var issue), Is.True, issue?.Message);
                var result = stageSystem.TryStage(assembly!, ["Host", "Disk"]);
                Assert.That(result.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), result.Issue?.Code);
                return result.Stage!;
            }
            try
            {
                var beforeStage = SEntMan.GetEntities().ToHashSet();
                var stage = MakeStage("CoordinatesDisk");
                Assert.That(stage.Phase, Is.EqualTo(KsProcgenContainerStagePhase.Uninitialized));
                mapSystem.SetPaused(stage.MapId, false);
                var initialized = stageSystem.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview), initialized.Issue?.Code);
                Assert.That(initialized.Stage, Is.SameAs(stage));
                Assert.That(stage.Phase, Is.EqualTo(KsProcgenContainerStagePhase.Initialized));
                Assert.That(mapSystem.IsInitialized(stage.MapId) && mapSystem.IsPaused(stage.MapId), Is.True);
                Assert.That(stageSystem.Verify(stage), Is.True);
                Assert.That(stage.Members.Values.All(memberUid => SEntMan.GetComponent<MetaDataComponent>(memberUid)
                    .EntityLifeStage == EntityLifeStage.MapInitialized), Is.True);
                Assert.That(containerSystem.TryGetContainer(stage.Members["Host"], "disk_slot", out var slot), Is.True);
                Assert.That(slot!.Contains(stage.Members["Disk"]), Is.True);
                Assert.That(containerSystem.TryGetContainer(stage.Members["Host"], "board", out var board), Is.True);
                Assert.That(board!.ContainedEntities.Count, Is.EqualTo(1));
                mapSystem.SetPaused(stage.MapId, false);
                Assert.That(stageSystem.Verify(stage), Is.False);
                mapSystem.SetPaused(stage.MapId, true);
                Assert.That(stageSystem.Verify(stage), Is.True);
                var afterInitialization = SEntMan.GetEntities().ToHashSet();
                Assert.That(stageSystem.TryInitialize(stage).Status, Is.EqualTo(KsProcgenContainerStageStatus.InitializedPreview));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(afterInitialization));
                Assert.That(stageSystem.Discard(stage), Is.True);
                Assert.That(SEntMan.GetEntities().Except(beforeStage), Is.Empty);
                Assert.That(stageSystem.TryInitialize(stage).Issue?.Code, Is.EqualTo("ContainerStageNotOwned"));

                var beforeFailure = SEntMan.GetEntities().ToHashSet();
                var replaced = MakeStage("KsProcgenInitializingDiskFixture");
                var failed = stageSystem.TryInitialize(replaced);
                Assert.That(failed.Status, Is.EqualTo(KsProcgenContainerStageStatus.Rejected));
                Assert.That(failed.Issue?.Code, Is.EqualTo("ContainerStageInitializationFailed"));
                Assert.That(failed.Stage, Is.Null);
                Assert.That(replaced.Active, Is.False);
                Assert.That(mapSystem.MapExists(replaced.MapId), Is.False);
                Assert.That(SEntMan.GetEntities().Except(beforeFailure), Is.Empty);

                var tampered = MakeStage("CoordinatesDisk");
                Assert.That(containerSystem.TryGetContainer(tampered.Members["Host"], "disk_slot", out slot), Is.True);
                Assert.That(containerSystem.Remove(tampered.Members["Disk"], slot!, force: false), Is.True);
                Assert.That(stageSystem.TryInitialize(tampered).Issue?.Code,
                    Is.EqualTo("ContainerStageInitializationPrecondition"));
                Assert.That(tampered.Active, Is.False);
                Assert.That(SEntMan.GetEntities().Except(beforeFailure), Is.Empty);
            }
            finally
            {
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task OwnedContainerStageInsertsVerifiesAndRollsBack()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stageSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var containerSystem = Pair.Server.System<SharedContainerSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            KsProcgenResolvedAssembly Compile(string childEntity, string containerId, bool lateFailure = false)
            {
                var definition = new KsProcgenAssemblyVariant
                {
                    Id = "Base", AnchorMember = "Host",
                    Members =
                    [
                        new() { Id = "Host", Entity = "ComputerShuttle" },
                        new() { Id = "AChild", Entity = childEntity },
                    ],
                    Relations = [new() { Id = "Stored", Subject = "AChild", Target = "Host",
                        Kind = KsProcgenRelationKind.InContainer, ContainerId = containerId }],
                };
                if (lateFailure)
                {
                    definition.Members.Add(new() { Id = "ZHost", Entity = "ComputerShuttle" });
                    definition.Members.Add(new() { Id = "ZZBad", Entity = "Table" });
                    definition.Relations.Add(new() { Id = "BadStored", Subject = "ZZBad", Target = "ZHost",
                        Kind = KsProcgenRelationKind.InContainer, ContainerId = "disk_slot" });
                }
                Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("StageFixture", definition,
                    new Dictionary<string, string>(), id => manager.TryIndex<EntityPrototype>(id, out _),
                    out var compiled, out var issue), Is.True, issue?.Message);
                return compiled!;
            }
            KsProcgenContainerStageResult Stage(KsProcgenResolvedAssembly assembly) =>
                stageSystem.TryStage(assembly, assembly.Members.Select(member => member.Id).ToArray());
            try
            {
                var assembly = Compile("CoordinatesDisk", "disk_slot");
                var result = Stage(assembly);
                Assert.That(result.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), result.Issue?.Code);
                var stage = result.Stage!;
                Assert.That(stageSystem.Verify(stage), Is.True);
                Assert.That(mapSystem.IsPaused(stage.MapId), Is.True);
                Assert.That(mapSystem.IsInitialized(stage.MapId), Is.False);
                var hostUid = stage.Members["Host"];
                var childUid = stage.Members["AChild"];
                Assert.That(containerSystem.TryGetContainer(hostUid, "disk_slot", out var container), Is.True);
                Assert.That(container!.Contains(childUid), Is.True);
                Assert.That(SEntMan.GetComponent<TransformComponent>(childUid).ParentUid, Is.EqualTo(hostUid));
                Assert.That(containerSystem.Remove(childUid, container, force: false), Is.True);
                Assert.That(stageSystem.Verify(stage), Is.False);
                Assert.That(stageSystem.Discard(stage), Is.True);
                Assert.That(stageSystem.Discard(stage), Is.False);
                Assert.That(stageSystem.Verify(stage), Is.False);
                Assert.That(SEntMan.EntityExists(hostUid) || SEntMan.EntityExists(childUid), Is.False);
                Assert.That(mapSystem.MapExists(stage.MapId), Is.False);

                var beforeFailure = SEntMan.GetEntities().ToHashSet();
                var failed = Stage(Compile("CoordinatesDisk", "disk_slot", lateFailure: true));
                Assert.That(failed.Status, Is.EqualTo(KsProcgenContainerStageStatus.Rejected));
                Assert.That(failed.Issue?.Code, Is.EqualTo("ItemSlotInsertionDenied"));
                Assert.That(failed.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities().Except(beforeFailure), Is.Empty);
                var budget = stageSystem.TryStage(assembly, ["Host", "AChild"], maxMembers: 1);
                Assert.That(budget.Status, Is.EqualTo(KsProcgenContainerStageStatus.BudgetExceeded));
                Assert.That(budget.Issue?.Code, Is.EqualTo("ContainerStageMemberBudget"));
                Assert.That(budget.Stage, Is.Null);
                Assert.That(stageSystem.TryStage(assembly, ["Host", "AChild"], maxMembers: 0).Status,
                    Is.EqualTo(KsProcgenContainerStageStatus.InvalidInput));

                var generic = Stage(Compile("CoordinatesDisk", "board"));
                Assert.That(generic.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), generic.Issue?.Code);
                Assert.That(stageSystem.Verify(generic.Stage!), Is.True);
                var second = Stage(assembly);
                Assert.That(second.Status, Is.EqualTo(KsProcgenContainerStageStatus.InsertedPreview), second.Issue?.Code);
                mapSystem.SetPaused(second.Stage!.MapId, false);
                // Uninitialized maps remain effectively paused even with their explicit pause flag cleared.
                Assert.That(stageSystem.Verify(second.Stage), Is.True);
                mapSystem.SetPaused(second.Stage.MapId, true);
                mapSystem.InitializeMap(second.Stage.MapId, unpause: false);
                Assert.That(stageSystem.Verify(second.Stage), Is.False);
                mapSystem.DeleteMap(second.Stage.MapId);
                Assert.That(stageSystem.Verify(second.Stage), Is.False);
                Assert.That(stageSystem.DiscardAll(), Is.EqualTo(2));
                Assert.That(generic.Stage!.Active || second.Stage.Active, Is.False);
                Assert.That(stageSystem.DiscardAll(), Is.Zero);

                var surfacePack = manager.Index<KsProcgenEntityPackPrototype>("KsProcgenRelationalPackFixture");
                Assert.That(KsProcgenAssemblyCompiler.TryResolvePack(manager, surfacePack, out var cores, out _), Is.True);
                var unsupported = Stage(cores.Single().Variants[0]);
                Assert.That(unsupported.Status, Is.EqualTo(KsProcgenContainerStageStatus.UnsupportedContent));
                Assert.That(unsupported.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities().Except(beforeFailure), Is.Empty);
            }
            finally
            {
                stageSystem.DiscardAll();
            }
        });
    }

    [Test]
    public async Task LiveContainerPreflightUsesEnginePermissionWithoutInsertion()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var createdUids = new List<EntityUid>();
            EntityUid Spawn(string? prototypeId)
            {
                var entityUid = SEntMan.SpawnEntity(prototypeId, MapCoordinates.Nullspace);
                createdUids.Add(entityUid);
                return entityUid;
            }
            try
            {
                var preflightSystem = Pair.Server.System<KsProcgenContainerPreflightSystem>();
                var itemSlotsSystem = Pair.Server.System<ItemSlotsSystem>();
                var containerSystem = Pair.Server.System<SharedContainerSystem>();
                var shuttleUid = Spawn("ComputerShuttle");
                var diskUid = Spawn("CoordinatesDisk");
                var otherDiskUid = Spawn("CoordinatesDisk");
                var wrongItemUid = Spawn("Table");
                var beforeCoordinates = SEntMan.GetComponent<TransformComponent>(diskUid).Coordinates;

                var eligible = preflightSystem.Check(diskUid, shuttleUid, "disk_slot");
                Assert.That(eligible.Status, Is.EqualTo(KsProcgenContainerPreflightStatus.Eligible));
                Assert.That(eligible.UsesItemSlot, Is.True);
                Assert.That(eligible.DeclaredCapacity, Is.EqualTo(1));
                Assert.That(eligible.InsertionVerified, Is.False);
                Assert.That(preflightSystem.Check(diskUid, shuttleUid, "disk_slot"), Is.EqualTo(eligible));
                Assert.That(SEntMan.GetComponent<TransformComponent>(diskUid).Coordinates, Is.EqualTo(beforeCoordinates));
                Assert.That(itemSlotsSystem.TryGetSlot(shuttleUid, "disk_slot", out var slot), Is.True);
                Assert.That(slot!.HasItem, Is.False);
                Assert.That(preflightSystem.Check(wrongItemUid, shuttleUid, "disk_slot").Reason,
                    Is.EqualTo("ItemSlotInsertionDenied"));

                itemSlotsSystem.SetLock(shuttleUid, "disk_slot", true);
                Assert.That(preflightSystem.Check(diskUid, shuttleUid, "disk_slot").Reason,
                    Is.EqualTo("ItemSlotLocked"));
                itemSlotsSystem.SetLock(shuttleUid, "disk_slot", false);
                Assert.That(itemSlotsSystem.TryInsert(shuttleUid, "disk_slot", diskUid, user: null), Is.True);
                Assert.That(preflightSystem.Check(otherDiskUid, shuttleUid, "disk_slot").Reason,
                    Is.EqualTo("ContainerSlotOccupied"));
                Assert.That(slot.Item, Is.EqualTo(diskUid));
                Assert.That(preflightSystem.Check(diskUid, shuttleUid, "disk_slot").Reason,
                    Is.EqualTo("ContainerSubjectAlreadyContained"));

                var hostUid = Spawn(null);
                var subjectUid = Spawn(null);
                var generic = containerSystem.EnsureContainer<Container>(hostUid, "General", out _);
                var genericResult = preflightSystem.Check(subjectUid, hostUid, "General");
                Assert.That(genericResult.Status, Is.EqualTo(KsProcgenContainerPreflightStatus.Eligible));
                Assert.That(genericResult.UsesItemSlot, Is.False);
                Assert.That(genericResult.DeclaredCapacity, Is.Null);
                Assert.That(generic.ContainedEntities, Is.Empty);
                Assert.That(preflightSystem.Check(hostUid, hostUid, "General").Reason,
                    Is.EqualTo("ContainerSelfInsertion"));
                Assert.That(preflightSystem.Check(subjectUid, hostUid, "Missing").Reason,
                    Is.EqualTo("MissingLiveContainer"));
                Assert.That(preflightSystem.Check(subjectUid, hostUid, " ").Status,
                    Is.EqualTo(KsProcgenContainerPreflightStatus.InvalidInput));

                var childUid = SEntMan.SpawnEntity(null, new EntityCoordinates(subjectUid, 0f, 0f));
                createdUids.Add(childUid);
                containerSystem.EnsureContainer<Container>(childUid, "Nested", out _);
                Assert.That(preflightSystem.Check(subjectUid, childUid, "Nested").Reason,
                    Is.EqualTo("ContainerInsertionDenied"));
                SEntMan.QueueDeleteEntity(subjectUid);
                Assert.That(preflightSystem.Check(subjectUid, hostUid, "General").Reason,
                    Is.EqualTo("ContainerEntityNotReady"));
            }
            finally
            {
                foreach (var entityUid in createdUids)
                {
                    if (SEntMan.EntityExists(entityUid))
                        SEntMan.DeleteEntity(entityUid);
                }
            }
        });
    }

    [Test]
    public async Task PrototypeCapabilityInspectionRetainsEngineLimits()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = Pair.Server.ResolveDependency<IComponentFactory>();
            KsProcgenPrototypeCapabilities Inspect(string id)
            {
                Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspect(manager, factory, id,
                    out var result, out var issue), Is.True, issue?.Message);
                return result!;
            }

            var table = Inspect("Table");
            var microwave = Inspect("KitchenMicrowave");
            Assert.That(table.HasSurface, Is.True);
            Assert.That(table.EnginePlacementVerified, Is.False);
            Assert.That(Inspect("Table").DeclarationHash, Is.EqualTo(table.DeclarationHash));
            var surface = KsProcgenSupportCapabilityInspector.Inspect(microwave, table,
                KsProcgenRelationKind.OnSurface);
            Assert.That(surface.State, Is.EqualTo(KsProcgenSupportInspectionState.Unverified));
            Assert.That(surface.HardCollisionCandidate, Is.False);
            Assert.That(surface.DeclaredCapacity, Is.Null);
            Assert.That(KsProcgenSupportCapabilityInspector.Inspect(table, table,
                KsProcgenRelationKind.OnSurface).HardCollisionCandidate, Is.True);

            var shuttle = Inspect("ComputerShuttle");
            var diskSlot = shuttle.Containers.Single(container => container.Id == "disk_slot");
            Assert.That(diskSlot.Kind, Is.EqualTo(KsProcgenDeclaredContainerKind.Slot));
            Assert.That(diskSlot.DeclaredCapacity, Is.EqualTo(1));
            Assert.That(diskSlot.Whitelist!.Components, Does.Contain("ShuttleDestinationCoordinates"));
            Assert.That(diskSlot.Whitelist.Tags, Is.Empty);
            Assert.That(KsProcgenSupportCapabilityInspector.Inspect(microwave, shuttle,
                KsProcgenRelationKind.InContainer, slotId: "disk_slot").State,
                Is.EqualTo(KsProcgenSupportInspectionState.Unverified));
            Assert.That(microwave.Containers.Single(container => container.Id == "microwave_entity_container")
                .DeclaredCapacity, Is.Null);

            var pack = manager.Index<KsProcgenEntityPackPrototype>("KsProcgenRelationalPackFixture");
            Assert.That(KsProcgenAssemblyCompiler.TryResolvePack(manager, pack, out var cores, out var issue),
                Is.True, issue?.Message);
            Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(manager, factory,
                cores.Single().Variants[0], out var report, out issue), Is.True, issue?.Message);
            Assert.That(report!.Members.Count, Is.EqualTo(4));
            Assert.That(report.SupportRelations.Count, Is.EqualTo(1));
            Assert.That(report.SupportRelations[0].Inspection.State,
                Is.EqualTo(KsProcgenSupportInspectionState.Unverified));
            var baseAssembly = cores.Single().Variants[0];
            var supportPlan = KsProcgenAssemblySupportPlanner.Plan(baseAssembly,
                baseAssembly.Members.Select(member => member.Id).ToArray(), report);
            Assert.That(supportPlan.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
            Assert.That(supportPlan.EnginePlacementVerified, Is.False);
            Assert.That(supportPlan.Members.Count, Is.EqualTo(4));
            var deviceSupport = supportPlan.Members.Single(member => member.MemberId == "Device");
            Assert.That(deviceSupport.Layer, Is.EqualTo(KsProcgenPlacementLayer.Surface));
            Assert.That(deviceSupport.ParentMemberId, Is.EqualTo("Table"));
            Assert.That(deviceSupport.FloorRootMemberId, Is.EqualTo("Table"));
            Assert.That(deviceSupport.Exposed, Is.True);
            Assert.That(supportPlan.Members.TakeWhile(member => member.MemberId != "Device")
                .Any(member => member.MemberId == "Table"), Is.True);
            Assert.That(supportPlan.Reservations.Single().DeclaredCapacity, Is.Null);
            Assert.That(KsProcgenAssemblySupportPlanner.Plan(baseAssembly,
                baseAssembly.Members.Select(member => member.Id).Reverse().ToArray(), report).StructureHash,
                Is.EqualTo(supportPlan.StructureHash));
            Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(manager, factory,
                cores.Single().Variants[1], out report, out issue), Is.True, issue?.Message);
            Assert.That(report!.Members.Count, Is.EqualTo(1));
            Assert.That(report.SupportRelations, Is.Empty);
            var standingAssembly = cores.Single().Variants[1];
            var standingSupport = KsProcgenAssemblySupportPlanner.Plan(standingAssembly, ["Device"], report);
            Assert.That(standingSupport.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
            Assert.That(standingSupport.Members.Single().Layer, Is.EqualTo(KsProcgenPlacementLayer.Floor));
            Assert.That(standingSupport.Reservations, Is.Empty);
            Assert.That(standingSupport.StructureHash, Is.Not.EqualTo(supportPlan.StructureHash));
            Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspect(manager, factory,
                "KsProcgenMissingCapabilityPrototype", out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("UnknownCapabilityPrototype"));
        });
    }

    [Test]
    public async Task TinyShapeCanBeNormalizedInLoadedServerContent()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var request = new KsProcgenRequest
            {
                RequestId = "LoadedTiny",
                Shape = new KsProcgenShapeSpec
                {
                    Cells =
                    [
                        new Vector2i(3, 4),
                        new Vector2i(3, 5),
                        new Vector2i(3, 6),
                    ],
                },
            };

            Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
            Assert.That(shape.TargetComponents().Count, Is.EqualTo(1));
            Assert.That(shape.TargetCells.Count, Is.EqualTo(3));

            var plan = KsProcgenPackingPlanner.Plan(request, []);
            Assert.That(plan.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
            Assert.That(plan.CellClaims.Count, Is.EqualTo(3));
            var residual = KsProcgenResidualConnector.Connect(shape!, plan,
                [new Vector2i(3, 4), new Vector2i(3, 6)], new HashSet<Vector2i>());
            Assert.That(residual.Status, Is.EqualTo(KsProcgenResidualStatus.PreliminaryReady));
            Assert.That(residual.ReservedPassageCells.Count, Is.EqualTo(3));
            var fill = KsProcgenPureFillPlanner.Plan(shape, plan, request.Seed);
            Assert.That(fill.Status, Is.EqualTo(KsProcgenPureFillStatus.Proposed));
            Assert.That(fill.Zones.Count, Is.EqualTo(1));
            Assert.That(fill.Zones[0].Kind, Is.EqualTo(KsProcgenZoneKind.Passage));
            var partition = KsProcgenPartitionPlanner.Plan(fill, residual.ReservedPassageCells);
            Assert.That(partition.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
            Assert.That(partition.FloorCells.Count, Is.EqualTo(3));
            Assert.That(partition.WallCells, Is.Empty);

            var room = new KsProcgenRoomShape("Entry", [new Vector2i(0, 0), new Vector2i(0, 1)],
                ports: [new KsProcgenRoomPort("North", new Vector2i(0, 1), new Vector2i(0, 1))]);
            var family = new KsProcgenLayoutFamily("TinyPair",
                [new KsProcgenLayoutOption("Entry", room.Cells, [room])]);
            var candidates = KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape,
                new Vector2i(3, 4), 10, out var complete);
            Assert.That(complete, Is.True);
            Assert.That(candidates.Count, Is.EqualTo(1));
            Assert.That(candidates[0].Ports[0].OutsideApproach, Is.EqualTo(new Vector2i(3, 6)));

            var traversal = new KsProcgenTraversalSnapshot();
            foreach (var (cell, _) in plan.CellClaims)
                traversal.WalkableCells.Add(cell);
            traversal.Roots.Add(new Vector2i(3, 4));
            Assert.That(KsProcgenTraversal.Validate(traversal).Valid, Is.True);

            var prototypeManager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themedTiny = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenSimpleOffice", request.Seed, fill, partition);
            Assert.That(themedTiny.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected),
                themedTiny.Issue?.Message);
            Assert.That(themedTiny.Regions.Count, Is.EqualTo(1));
            Assert.That(themedTiny.Regions[0].Kind, Is.EqualTo(KsProcgenZoneKind.Passage));
            Assert.That(themedTiny.Regions[0].Theme.DominantEntityPackId, Is.Null);
            Assert.That(themedTiny.Regions[0].FloorCells.Count, Is.EqualTo(3));
            var mandatoryTiny = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenMandatoryOfficeFixture", request.Seed, fill, partition);
            Assert.That(mandatoryTiny.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.ThemeRejected));
            Assert.That(mandatoryTiny.Issue?.Code, Is.EqualTo("MandatoryPackInPassage"));
            Assert.That(mandatoryTiny.Regions, Is.Empty);
            var tinyMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager, themedTiny, partition, request.Seed);
            Assert.That(tinyMaterials.Status, Is.EqualTo(KsProcgenMaterialStatus.Planned));
            Assert.That(tinyMaterials.Tiles.Count, Is.EqualTo(3));
            Assert.That(tinyMaterials.Tiles.All(tile => !tile.Accent), Is.True);
            Assert.That(tinyMaterials.InteriorWalls, Is.Empty);
            Assert.That(tinyMaterials.InteriorDoors, Is.Empty);

            var passageCells = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0) };
            var roomCells = Enumerable.Range(1, 3).SelectMany(y => Enumerable.Range(0, 3)
                .Select(x => new Vector2i(x, y))).ToArray();
            var officeFill = new KsProcgenPureFillResult
            {
                Status = KsProcgenPureFillStatus.Proposed,
                Zones =
                [
                    new KsProcgenZone("passage", KsProcgenZoneKind.Passage, passageCells),
                    new KsProcgenZone("office", KsProcgenZoneKind.RoomProposal, roomCells),
                ],
                ProceduralCells = 12,
            };
            var officePartition = KsProcgenPartitionPlanner.Plan(officeFill, passageCells.ToHashSet());
            Assert.That(officePartition.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
            var themedOffice = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenSimpleOffice", 17, officeFill, officePartition);
            Assert.That(themedOffice.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected),
                themedOffice.Issue?.Message);
            Assert.That(themedOffice.Regions.Count, Is.EqualTo(2));
            var officeRegion = themedOffice.Regions.Single(region => region.Kind == KsProcgenZoneKind.RoomProposal);
            Assert.That(officeRegion.Theme.DominantEntityPackId, Is.EqualTo("KsProcgenOfficeWorkstations"));
            Assert.That(officeRegion.Theme.SupportingEntityPackIds,
                Is.EquivalentTo(new[] { "KsProcgenOfficeStorage" }));
            Assert.That(officeRegion.WallCells.Count, Is.EqualTo(2));
            Assert.That(themedOffice.Regions.Single(region => region.Kind == KsProcgenZoneKind.Passage)
                .Theme.DominantEntityPackId, Is.Null);
            var officeMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager,
                themedOffice, officePartition, 17);
            var replayMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager,
                themedOffice, officePartition, 17);
            Assert.That(officeMaterials.Status, Is.EqualTo(KsProcgenMaterialStatus.Planned));
            Assert.That(officeMaterials.Tiles.Count, Is.EqualTo(10));
            Assert.That(officeMaterials.Tiles.Count(tile => tile.Accent), Is.EqualTo(1));
            Assert.That(officeMaterials.Tiles.Single(tile => tile.Cell == officePartition.DoorOpenings[0].Threshold)
                .Accent, Is.False);
            Assert.That(officeMaterials.InteriorWalls.Count, Is.EqualTo(2));
            Assert.That(officeMaterials.InteriorWalls.All(wall => wall.EntityId == "WallSolid"), Is.True);
            Assert.That(officeMaterials.InteriorDoors.Single().EntityId, Is.EqualTo("Airlock"));
            Assert.That(officeMaterials.Tiles, Is.EqualTo(replayMaterials.Tiles));
            var noDoorTheme = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenNoDoorThemeFixture", 17, officeFill, officePartition);
            Assert.That(noDoorTheme.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected));
            var noDoorMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager,
                noDoorTheme, officePartition, 17);
            Assert.That(noDoorMaterials.Status, Is.EqualTo(KsProcgenMaterialStatus.NoCompatibleDoor));
            Assert.That(noDoorMaterials.Issue?.Code, Is.EqualTo("MissingInteriorDoorMaterial"));
            Assert.That(noDoorMaterials.Tiles, Is.Empty);

            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                17, "FurnishedOffice", out var furnishingTheme, out var furnishingIssue),
                Is.True, furnishingIssue?.Message);
            var largeFloor = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5)
                .Select(y => new Vector2i(x, y))).ToArray();
            var largeRegion = new KsProcgenThemedRegion("FurnishedOffice", KsProcgenZoneKind.RoomProposal,
                ["FurnishedOffice"], largeFloor, [], furnishingTheme!, []);
            var openPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = largeFloor,
            };
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenRelationalThemeFixture", 17, "RelationalRoom",
                out var relationalTheme, out var relationalIssue), Is.True, relationalIssue?.Message);
            var relationalPack = prototypeManager.Index<KsProcgenEntityPackPrototype>(
                "KsProcgenRelationalPackFixture");
            Assert.That(KsProcgenAssemblyCompiler.TryResolvePack(prototypeManager, relationalPack,
                out var relationalCores, out relationalIssue), Is.True, relationalIssue?.Message);
            var relationalCore = relationalCores.Single();
            Assert.That(relationalCore.MinimumCount, Is.EqualTo(1));
            Assert.That(relationalCore.Variants.Select(variant => variant.VariantId),
                Is.EqualTo(new[] { "Base", "Standing" }));
            Assert.That(relationalCore.Variants[0].Members.Count, Is.EqualTo(4));
            Assert.That(relationalCore.Variants[1].Members.Count, Is.EqualTo(1));
            Assert.That(relationalCore.Variants[0].Relations.Any(relation =>
                relation.Kind == KsProcgenRelationKind.OnSurface && relation.Target == "Table"), Is.True);
            var missingRelationalBinding = new KsProcgenAssemblyReference
            {
                Id = "MissingBinding", Assembly = "KsProcgenRelationalFixture",
            };
            Assert.That(KsProcgenAssemblyCompiler.TryResolve(prototypeManager, missingRelationalBinding,
                out _, out relationalIssue), Is.False);
            Assert.That(relationalIssue?.Code, Is.EqualTo("MissingAssemblyBinding"));
            var relationalRegion = new KsProcgenThemedRegion("RelationalRoom",
                KsProcgenZoneKind.RoomProposal, ["RelationalRoom"], largeFloor, [], relationalTheme!, []);
            var relationalPlacement = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                relationalRegion, openPartition, 17);
            Assert.That(relationalPlacement.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed));
            Assert.That(relationalPlacement.Entities, Is.Not.Empty);
            Assert.That(relationalPlacement.Entities.All(entity =>
                entity.VariantId == "Standing" && entity.EntryId == "Device"), Is.True,
                "The supported standing variant must replace the unsupported surface variant whole.");
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenUnsupportedSurfaceThemeFixture", 17, "UnsupportedSurface",
                out var surfaceTheme, out _), Is.True);
            var surfacePlacement = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                relationalRegion with { Theme = surfaceTheme! }, openPartition, 17);
            Assert.That(surfacePlacement.Issue?.Code, Is.EqualTo("AssemblyPlacementUnsupported"));
            Assert.That(surfacePlacement.Entities, Is.Empty);
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenPreferredCornerThemeFixture", 17, "Corner",
                out var cornerTheme, out _), Is.True);
            var cornerRegion = relationalRegion with { Theme = cornerTheme! };
            var noCorner = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                cornerRegion, openPartition, 17);
            Assert.That(noCorner.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(noCorner.Entities.Count, Is.EqualTo(1));
            Assert.That(noCorner.RelationWitnesses.Single().State, Is.EqualTo(KsProcgenConstraintState.Missed));
            Assert.That(noCorner.PreferenceSearchTruncated, Is.False);
            var cornerWalls = new HashSet<Vector2i> { new(5, 4), new(4, 5) };
            var preferredCorner = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                cornerRegion, openPartition, 17, inspectedWalls: cornerWalls);
            Assert.That(preferredCorner.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed));
            Assert.That(preferredCorner.Entities.Single().Cell, Is.EqualTo(new Vector2i(4, 4)),
                "An inspected wall corner should outrank a legal center placement.");
            Assert.That(preferredCorner.RelationWitnesses.Single().State, Is.EqualTo(KsProcgenConstraintState.Satisfied));
            Assert.That(new[] { preferredCorner.RelationWitnesses.Single().FirstBackingWall,
                preferredCorner.RelationWitnesses.Single().SecondBackingWall },
                Is.EquivalentTo(cornerWalls.Select(cell => (Vector2i?) cell)));
            Assert.That(KsProcgenFurnishingPlanner.Plan(prototypeManager,
                cornerRegion, openPartition, 17, inspectedWalls: cornerWalls).RelationWitnesses,
                Is.EqualTo(preferredCorner.RelationWitnesses));
            var boundedCorner = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                cornerRegion, openPartition, 17, maxCandidateProbes: 1);
            Assert.That(boundedCorner.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(boundedCorner.Entities.Count, Is.EqualTo(1),
                "Exhausting preference search must retain an already valid mandatory core.");
            Assert.That(boundedCorner.PreferenceSearchTruncated, Is.True);
            Assert.That(boundedCorner.RelationWitnesses.Single().State, Is.EqualTo(KsProcgenConstraintState.Missed));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenRequiredCornerThemeFixture", 17, "RequiredCorner",
                out var requiredCornerTheme, out _), Is.True);
            var missingRequiredCorner = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                cornerRegion with { Theme = requiredCornerTheme! }, openPartition, 17);
            Assert.That(missingRequiredCorner.Status, Is.EqualTo(KsProcgenFurnishingStatus.MandatoryUnmet));
            Assert.That(missingRequiredCorner.Entities, Is.Empty);
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenNearThemeFixture", 17, "NearRoom", out var nearTheme, out _), Is.True);
            var nearRegion = relationalRegion with { Theme = nearTheme! };
            var nearPlacement = KsProcgenFurnishingPlanner.Plan(prototypeManager, nearRegion, openPartition, 17);
            Assert.That(nearPlacement.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed), nearPlacement.Issue?.Code);
            Assert.That(nearPlacement.Entities.Select(entity => entity.EntryId),
                Is.EquivalentTo(new[] { "Table", "Console", "WallLocker" }));
            var nearTable = nearPlacement.Entities.Single(entity => entity.EntryId == "Table");
            Assert.That(nearTable.DeclaredApproachLanding, Is.EqualTo(nearTable.Cell + new Vector2i(0, -1)));
            var nearWitness = nearPlacement.RelationWitnesses.Single();
            Assert.That(nearWitness.Kind, Is.EqualTo(KsProcgenRelationKind.Near));
            Assert.That(nearWitness.PathDistance, Is.InRange(0, 3));
            Assert.That(nearWitness.State, Is.EqualTo(KsProcgenConstraintState.Satisfied));
            Assert.That(nearWitness.CleanPath.Cells.Last(), Is.EqualTo(nearTable.DeclaredApproachLanding));
            Assert.That(nearWitness.CleanPath.Cells.All(cell => nearPlacement.ProtectedPassageCells.Contains(cell)), Is.True,
                "Later supporting furniture must leave the accepted Near witness route clear.");
            var nearBlocked = nearPlacement.Entities.Where(entity => entity.Movement != KsProcgenMovementClass.Clear)
                .SelectMany(entity => entity.OccupiedCells).ToHashSet();
            Assert.That(nearWitness.CleanPath.Cells.All(cell => !nearBlocked.Contains(cell)), Is.True);
            Assert.That(nearPlacement.RelationPathExpandedCells, Is.InRange(0, 4096));
            Assert.That(nearPlacement.RelationPathSearchTruncated, Is.False);
            Assert.That(KsProcgenFurnishingPlanner.Plan(prototypeManager, nearRegion, openPartition, 17).RelationWitnesses,
                Is.EqualTo(nearPlacement.RelationWitnesses));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenLongTableThemeFixture", 17, "LongTableRoom",
                out var longTableTheme, out var longTableIssue), Is.True, longTableIssue?.Message);
            var longTableRegion = new KsProcgenThemedRegion("LongTableRoom",
                KsProcgenZoneKind.RoomProposal, ["LongTableRoom"], largeFloor, [],
                longTableTheme!, []);
            var longTablePlan = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                longTableRegion, openPartition, 17);
            Assert.That(longTablePlan.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed),
                longTablePlan.Issue?.Message);
            Assert.That(longTablePlan.Entities, Is.Not.Empty);
            Assert.That(longTablePlan.Entities.All(entity => entity.OccupiedCells.Count == 2 &&
                entity.OccupiedCells.All(cell => largeFloor.Contains(cell) &&
                    !longTablePlan.ProtectedPassageCells.Contains(cell))), Is.True);
            Assert.That(longTablePlan.Entities.SelectMany(entity => entity.OccupiedCells)
                .Distinct().Count(), Is.EqualTo(longTablePlan.Entities.Count * 2));
            var longTableLighting = KsProcgenLightingPlanner.Plan(prototypeManager,
                longTableRegion, longTablePlan, 17);
            var longTableCells = longTablePlan.Entities.SelectMany(entity => entity.OccupiedCells)
                .ToHashSet();
            Assert.That(longTableLighting.Lights.All(light => !longTableCells.Contains(light.Cell)),
                Is.True);
            Assert.That(KsProcgenFurnishingPlanner.Plan(prototypeManager,
                longTableRegion, openPartition, 17).Entities, Is.EqualTo(longTablePlan.Entities));
            var narrowFloor = Enumerable.Range(0, 3).Select(y => new Vector2i(0, y)).ToArray();
            var narrowRegion = longTableRegion with
            {
                Id = "NarrowLongTable",
                FloorCells = narrowFloor,
            };
            var narrowPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = narrowFloor,
            };
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenIndependentCoresThemeFixture", 17, "IndependentCores",
                out var independentTheme, out _), Is.True);
            var independentCores = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                narrowRegion with { Theme = independentTheme! }, narrowPartition, 17);
            Assert.That(independentCores.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(independentCores.Entities.Select(entity => entity.CoreId),
                Is.EqualTo(new[] { "SmallTable" }),
                "An impossible optional singleton must not discard a different mandatory core.");
            Assert.That(independentCores.Omissions.Single().Reason, Is.EqualTo("CoreCannotFit/LargeTable"));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenWeightedCoresThemeFixture", 17, "WeightedCores",
                out var weightedTheme, out _), Is.True);
            var weightedFloor = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
            var weightedRegion = new KsProcgenThemedRegion("WeightedCores", KsProcgenZoneKind.RoomProposal,
                ["WeightedCores"], weightedFloor, [], weightedTheme!, []);
            var weightedPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = weightedFloor,
            };
            var weightedCores = KsProcgenFurnishingPlanner.Plan(prototypeManager, weightedRegion, weightedPartition, 17);
            Assert.That(weightedCores.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(weightedCores.Entities.Single().CoreId, Is.EqualTo("ZHeavy"));
            Assert.That(weightedCores.Omissions.Single().Reason, Is.EqualTo("CoreCannotFit/AlphaLight"));
            Assert.That(KsProcgenFurnishingPlanner.Plan(prototypeManager, weightedRegion, weightedPartition, 17).Entities,
                Is.EqualTo(weightedCores.Entities));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenWeightedRetryThemeFixture", 17, "WeightedRetry",
                out var weightedRetryTheme, out _), Is.True);
            var weightedRetryFloor = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7)
                .Select(y => new Vector2i(x, y))).ToArray();
            var weightedRetryRegion = new KsProcgenThemedRegion("WeightedRetry", KsProcgenZoneKind.RoomProposal,
                ["WeightedRetry"], weightedRetryFloor, [], weightedRetryTheme!, []);
            var weightedRetryPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = weightedRetryFloor,
            };
            var weightedRetry = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                weightedRetryRegion, weightedRetryPartition, 17);
            Assert.That(weightedRetry.TargetDominantClusters, Is.EqualTo(3));
            Assert.That(weightedRetry.PlacedDominantClusters, Is.EqualTo(3),
                "Each density draw must retry a fitting core after the heavier footprint fails.");
            Assert.That(weightedRetry.Entities.All(entity => entity.CoreId == "Small"), Is.True);
            Assert.That(weightedRetry.Omissions.Single().Reason, Is.EqualTo("CoreCannotFit/TooWide"));
            Assert.That(KsProcgenFurnishingPlanner.Plan(prototypeManager,
                weightedRetryRegion, weightedRetryPartition, 17).Entities, Is.EqualTo(weightedRetry.Entities));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenCoreMinimumThemeFixture", 17, "CoreMinimum",
                out var minimumTheme, out _), Is.True);
            var unmetCoreMinimum = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                narrowRegion with { Theme = minimumTheme! }, narrowPartition, 17);
            Assert.That(unmetCoreMinimum.Status, Is.EqualTo(KsProcgenFurnishingStatus.MandatoryUnmet));
            Assert.That(unmetCoreMinimum.Entities, Is.Empty,
                "An unmet mandatory core count must discard the entire speculative room proposal.");
            var coreMinimumBudget = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                narrowRegion with { Theme = minimumTheme! }, narrowPartition, 17, maxRoomCells: 2);
            Assert.That(coreMinimumBudget.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(coreMinimumBudget.Entities, Is.Empty);
            var coreMinimumPassage = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                narrowRegion with { Theme = minimumTheme!, Kind = KsProcgenZoneKind.Passage },
                narrowPartition, 17);
            Assert.That(coreMinimumPassage.Status, Is.EqualTo(KsProcgenFurnishingStatus.MandatoryUnmet));
            var rotatedTable = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                narrowRegion, narrowPartition, 17);
            Assert.That(rotatedTable.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed),
                rotatedTable.Issue?.Message);
            Assert.That(rotatedTable.Entities.Single().OccupiedCells,
                Is.EquivalentTo(new[] { new Vector2i(0, 1), new Vector2i(0, 2) }));
            Assert.That(rotatedTable.Entities.Single().QuarterTurns, Is.EqualTo(3));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenWideConsoleThemeFixture", 17, "WideConsoleRoom",
                out var wideConsoleTheme, out var wideConsoleIssue), Is.True,
                wideConsoleIssue?.Message);
            var wideConsoleRegion = new KsProcgenThemedRegion("WideConsoleRoom",
                KsProcgenZoneKind.RoomProposal, ["WideConsoleRoom"], largeFloor, [],
                wideConsoleTheme!, []);
            var wideConsolePlan = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                wideConsoleRegion, openPartition, 17);
            Assert.That(wideConsolePlan.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed),
                wideConsolePlan.Issue?.Message);
            var wideConsole = wideConsolePlan.Entities.First();
            var consoleFaces = new[]
            {
                new Vector2i(0, -1), new Vector2i(-1, 0),
                new Vector2i(0, 1), new Vector2i(1, 0),
            };
            Assert.That(wideConsole.OccupiedCells.Count, Is.EqualTo(2));
            Assert.That(wideConsole.InteractionApproach.HasValue, Is.True);
            Assert.That(wideConsole.OccupiedCells.Any(cell =>
                cell + consoleFaces[wideConsole.QuarterTurns] == wideConsole.InteractionApproach),
                Is.True);
            var wideBlocked = wideConsolePlan.Entities.SelectMany(entity => entity.OccupiedCells)
                .ToHashSet();
            Assert.That(KsProcgenTraversal.FindCleanPath(new Vector2i(0, 0),
                wideConsole.InteractionApproach!.Value, largeFloor.ToHashSet(), wideBlocked),
                Is.Not.Empty);
            var furnished = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17);
            Assert.That(furnished.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed),
                furnished.Issue?.Message);
            Assert.That(furnished.Entities.Select(entity => entity.EntryId),
                Is.EquivalentTo(new[] { "Desk", "Chair", "Console", "WallLocker" }));
            Assert.That(furnished.Entities.Where(entity => entity.CoreId == "Workstation")
                .All(entity => entity.AssemblyId == "KsProcgenOfficeWorkstationAssembly" &&
                               entity.VariantId == "Base"), Is.True);
            Assert.That(furnished.RelationWitnesses.Count, Is.EqualTo(3));
            Assert.That(furnished.RelationWitnesses.All(witness => witness.State == KsProcgenConstraintState.Satisfied &&
                witness.CoreId == "Workstation" && witness.ClusterIndex == 0), Is.True);
            Assert.That(furnished.Entities.Single(entity => entity.EntryId == "Desk").Movement,
                Is.EqualTo(KsProcgenMovementClass.VaultRequired));
            Assert.That(furnished.Entities.Single(entity => entity.EntryId == "Console").InteractionApproach,
                Is.EqualTo(furnished.Entities.Single(entity => entity.EntryId == "Chair").Cell));
            var furnishedChair = furnished.Entities.Single(entity => entity.EntryId == "Chair");
            var furnishedConsole = furnished.Entities.Single(entity => entity.EntryId == "Console");
            var chairFacing = new[]
            {
                new Vector2i(0, -1), new Vector2i(-1, 0),
                new Vector2i(0, 1), new Vector2i(1, 0),
            };
            Assert.That(chairFacing[furnishedChair.QuarterTurns],
                Is.EqualTo(furnishedConsole.Cell - furnishedChair.Cell));
            var furnishedDesk = furnished.Entities.Single(entity => entity.EntryId == "Desk");
            var storage = furnished.Entities.Single(entity => entity.EntryId == "WallLocker");
            Assert.That(System.Math.Abs(storage.Cell.X - furnishedDesk.Cell.X) +
                        System.Math.Abs(storage.Cell.Y - furnishedDesk.Cell.Y), Is.LessThanOrEqualTo(4));
            Assert.That(furnished.Entities.All(entity => !furnished.ProtectedPassageCells.Contains(entity.Cell)),
                Is.True);
            var furnishedReplay = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17);
            Assert.That(furnished.Entities, Is.EqualTo(furnishedReplay.Entities));
            var spaciousFloor = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7)
                .Select(y => new Vector2i(x, y))).ToArray();
            var spaciousRegion = new KsProcgenThemedRegion("SpaciousOffice",
                KsProcgenZoneKind.RoomProposal, ["SpaciousOffice"], spaciousFloor, [],
                furnishingTheme!, []);
            var spaciousPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = spaciousFloor,
            };
            var spaciousFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                spaciousRegion, spaciousPartition, 17);
            Assert.That(spaciousFurnishing.TargetDominantClusters, Is.EqualTo(3));
            Assert.That(spaciousFurnishing.PlacedDominantClusters, Is.GreaterThanOrEqualTo(2));
            Assert.That(spaciousFurnishing.Entities.Where(entity =>
                    entity.PackId == "KsProcgenOfficeWorkstations")
                .GroupBy(entity => entity.ClusterIndex)
                .All(cluster => cluster.Select(entity => entity.EntryId)
                    .OrderBy(id => id).SequenceEqual(new[] { "Chair", "Console", "Desk" })), Is.True);
            Assert.That(spaciousFurnishing.Entities.Select(entity => entity.Cell).Distinct().Count(),
                Is.EqualTo(spaciousFurnishing.Entities.Count));
            var spaciousBlocked = spaciousFurnishing.Entities
                .Where(entity => entity.Movement != KsProcgenMovementClass.Clear)
                .Select(entity => entity.Cell).ToHashSet();
            var spaciousRoot = spaciousFloor.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).First();
            Assert.That(spaciousFurnishing.Entities.Where(entity => entity.InteractionApproach.HasValue)
                .All(entity => KsProcgenTraversal.FindCleanPath(spaciousRoot,
                    entity.InteractionApproach!.Value, spaciousFloor.ToHashSet(),
                    spaciousBlocked).Count > 0), Is.True);
            Assert.That(KsProcgenFurnishingPlanner.Plan(prototypeManager,
                spaciousRegion, spaciousPartition, 17).Entities,
                Is.EqualTo(spaciousFurnishing.Entities));
            var lowDensityRegion = spaciousRegion with
            {
                Theme = furnishingTheme! with { FurnishingDensity = 0f },
            };
            var lowDensityFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                lowDensityRegion, spaciousPartition, 17);
            Assert.That(lowDensityFurnishing.TargetDominantClusters, Is.EqualTo(1));
            Assert.That(lowDensityFurnishing.PlacedDominantClusters, Is.EqualTo(1));
            var densityBudgetMiss = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                spaciousRegion, spaciousPartition, 17,
                maxCandidateProbes: lowDensityFurnishing.CandidateProbes);
            Assert.That(densityBudgetMiss.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(densityBudgetMiss.PlacedDominantClusters, Is.EqualTo(1));
            Assert.That(densityBudgetMiss.CandidateProbes,
                Is.LessThanOrEqualTo(lowDensityFurnishing.CandidateProbes));
            Assert.That(densityBudgetMiss.Omissions.Select(omission => omission.Reason),
                Does.Contain("FurnishingDensityProbeBudget"));
            var litOffice = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17);
            var litOfficeReplay = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17);
            Assert.That(litOffice.Status, Is.EqualTo(KsProcgenLightingPlanStatus.Proposed),
                litOffice.Issue?.Message);
            Assert.That(litOffice.Lights, Is.Not.Empty);
            Assert.That(litOffice.Lights, Is.EqualTo(litOfficeReplay.Lights));
            Assert.That(litOffice.Lights.All(light =>
                !furnished.ProtectedPassageCells.Contains(light.Cell) &&
                furnished.Entities.All(entity => entity.Cell != light.Cell)), Is.True);
            Assert.That(litOffice.EstimatedCoveredCells,
                Is.GreaterThanOrEqualTo((int) System.Math.Ceiling(
                    litOffice.TotalFloorCells * litOffice.RequestedCoverage)));
            Assert.That(litOffice.WorkingCoverageVerified, Is.False);
            var sparseLighting = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17, maxRoomCells: 4);
            Assert.That(sparseLighting.Status, Is.EqualTo(KsProcgenLightingPlanStatus.Sparse));
            Assert.That(sparseLighting.Lights, Is.Empty);
            var cappedLighting = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17, maxFixtures: 1);
            Assert.That(cappedLighting.Status, Is.EqualTo(KsProcgenLightingPlanStatus.BudgetExceeded));
            var doorPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = largeFloor,
                DoorOpenings =
                [
                    new KsProcgenPartitionDoor("FurnishedOffice", "passage",
                        new Vector2i(2, 0), new Vector2i(2, 1), new Vector2i(2, -1)),
                ],
            };
            var furnishedWithDoor = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, doorPartition, 17);
            Assert.That(furnishedWithDoor.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed));
            Assert.That(furnishedWithDoor.ProtectedPassageCells,
                Does.Contain(new Vector2i(2, 0)));
            Assert.That(furnishedWithDoor.ProtectedPassageCells,
                Does.Contain(new Vector2i(2, 1)));
            Assert.That(furnishedWithDoor.Entities.All(entity =>
                entity.Cell != new Vector2i(2, 0) && entity.Cell != new Vector2i(2, 1)), Is.True);

            var tinyRoom = new KsProcgenThemedRegion("TinyRoom", KsProcgenZoneKind.RoomProposal,
                ["TinyRoom"], request.Shape.Cells, [], furnishingTheme!,
                [new KsProcgenPackMinimum("KsProcgenOfficeWorkstations", 1)]);
            var tinyOpenPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = request.Shape.Cells,
            };
            var mandatoryFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                tinyRoom, tinyOpenPartition, 17);
            Assert.That(mandatoryFurnishing.Status, Is.EqualTo(KsProcgenFurnishingStatus.MandatoryUnmet));
            Assert.That(mandatoryFurnishing.Entities, Is.Empty);
            var optionalTiny = tinyRoom with { UnplacedRequiredPacks = [] };
            var sparseFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                optionalTiny, tinyOpenPartition, 17);
            Assert.That(sparseFurnishing.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(sparseFurnishing.Entities, Is.Empty);
            Assert.That(sparseFurnishing.Omissions.Select(omission => omission.PackId),
                Is.EquivalentTo(new[] { "KsProcgenOfficeWorkstations", "KsProcgenOfficeStorage" }));
            var budgetedFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17, maxCandidateProbes: 1);
            Assert.That(budgetedFurnishing.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(budgetedFurnishing.Entities, Is.Empty);
            var tooLargeOptional = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17, maxRoomCells: 4);
            Assert.That(tooLargeOptional.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(tooLargeOptional.Entities, Is.Empty);
            Assert.That(tooLargeOptional.Omissions.Count, Is.EqualTo(2));
            var tooLargeRequired = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion with
                {
                    UnplacedRequiredPacks = [new KsProcgenPackMinimum("KsProcgenOfficeWorkstations", 1)],
                }, openPartition, 17, maxRoomCells: 4);
            Assert.That(tooLargeRequired.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));

            var purePipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                request, [], "KsProcgenSimpleOffice");
            Assert.That(purePipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                purePipeline.Issue?.Message);
            Assert.That(purePipeline.Materials?.Tiles.Count, Is.EqualTo(3));
            Assert.That(purePipeline.Materials?.InteriorWalls, Is.Empty);
            Assert.That(purePipeline.Furnishings.Count, Is.EqualTo(1));
            Assert.That(purePipeline.Furnishings[0].Proposal.Entities, Is.Empty);
            Assert.That(purePipeline.HullBoundary?.Status,
                Is.EqualTo(KsProcgenHullBoundaryStatus.UnresolvedBoundary));
            Assert.That(purePipeline.HullBoundary?.Edges.Count, Is.EqualTo(8));
            Assert.That(purePipeline.HullBoundary?.GasClosureVerified, Is.False);
            Assert.That(purePipeline.Lighting.Count, Is.EqualTo(1));
            Assert.That(purePipeline.Lighting[0].Proposal.Status,
                Is.EqualTo(KsProcgenLightingPlanStatus.Sparse));
            Assert.That(purePipeline.Lighting[0].Proposal.WorkingCoverageVerified, Is.False);
            var tileStageSystem = Pair.Server.System<KsProcgenTileStageSystem>();
            var tileBudgetMiss = tileStageSystem.TryStage(purePipeline, maxTiles: 2);
            Assert.That(tileBudgetMiss.Status, Is.EqualTo(KsProcgenTileStageStatus.BudgetExceeded));
            Assert.That(tileBudgetMiss.Stage, Is.Null);
            var tileStageResult = tileStageSystem.TryStage(purePipeline);
            Assert.That(tileStageResult.Status, Is.EqualTo(KsProcgenTileStageStatus.Staged),
                tileStageResult.Issue?.Message);
            var tileStage = tileStageResult.Stage!;
            try
            {
                Assert.That(tileStage.StagedCells, Is.EqualTo(3));
                Assert.That(tileStage.SemanticHash, Is.EqualTo(purePipeline.SemanticHash));
                Assert.That(tileStageSystem.Verify(tileStage), Is.True);
                var stagedGrid = Pair.Server.ResolveDependency<IEntityManager>()
                    .GetComponent<MapGridComponent>(tileStage.GridUid);
                var stagedMapSystem = Pair.Server.System<SharedMapSystem>();
                Assert.That(stagedMapSystem
                    .GetTileRef(tileStage.GridUid, stagedGrid, new Vector2i(3, 4)).Tile.IsEmpty, Is.False);
                stagedMapSystem.SetTile(tileStage.GridUid, stagedGrid, new Vector2i(3, 4), Tile.Empty);
                Assert.That(tileStageSystem.Verify(tileStage), Is.False);
            }
            finally
            {
                Assert.That(tileStageSystem.Discard(tileStage), Is.True);
            }
            Assert.That(tileStage.Active, Is.False);
            Assert.That(tileStageSystem.Verify(tileStage), Is.False);
            Assert.That(tileStageSystem.Discard(tileStage), Is.False);
            Assert.That(Pair.Server.System<SharedMapSystem>().MapExists(tileStage.MapId), Is.False);
            var firstCancelledResult = tileStageSystem.TryStage(purePipeline);
            var secondCancelledResult = tileStageSystem.TryStage(purePipeline);
            Assert.That(firstCancelledResult.Status, Is.EqualTo(KsProcgenTileStageStatus.Staged));
            Assert.That(secondCancelledResult.Status, Is.EqualTo(KsProcgenTileStageStatus.Staged));
            var firstCancelledStage = firstCancelledResult.Stage!;
            var secondCancelledStage = secondCancelledResult.Stage!;
            Assert.That(firstCancelledStage.MapId, Is.Not.EqualTo(secondCancelledStage.MapId));
            Assert.That(tileStageSystem.DiscardAll(), Is.EqualTo(2));
            Assert.That(firstCancelledStage.Active, Is.False);
            Assert.That(secondCancelledStage.Active, Is.False);
            Assert.That(Pair.Server.System<SharedMapSystem>().MapExists(firstCancelledStage.MapId), Is.False);
            Assert.That(Pair.Server.System<SharedMapSystem>().MapExists(secondCancelledStage.MapId), Is.False);
            Assert.That(tileStageSystem.DiscardAll(), Is.Zero);
            var externallyRemovedResult = tileStageSystem.TryStage(purePipeline);
            Assert.That(externallyRemovedResult.Status, Is.EqualTo(KsProcgenTileStageStatus.Staged));
            var externallyRemovedStage = externallyRemovedResult.Stage!;
            Pair.Server.System<SharedMapSystem>().DeleteMap(externallyRemovedStage.MapId);
            Assert.That(tileStageSystem.Verify(externallyRemovedStage), Is.False);
            Assert.That(tileStageSystem.Discard(externallyRemovedStage), Is.True);
            Assert.That(externallyRemovedStage.Active, Is.False);
            var pureReport = purePipeline.Summarize(request);
            Assert.That(pureReport.Disposition, Is.EqualTo(KsProcgenPlanningDisposition.Degraded));
            Assert.That(pureReport.Fallbacks.Select(item => item.Code), Does.Contain("SparseLighting"));
            Assert.That(pureReport.Constraints.Single(item => item.Id == "gas-closure").State,
                Is.EqualTo(KsProcgenConstraintState.Unverified));
            Assert.That(pureReport.Published, Is.False);
            var noSparseLightingRequest = new KsProcgenRequest
            {
                RequestId = "NoSparseLighting",
                Shape = request.Shape,
                FallbackPolicy = new KsProcgenFallbackPolicy { AllowSparseLighting = false },
            };
            var noSparseLighting = KsProcgenGeometryPipeline.Plan(prototypeManager,
                noSparseLightingRequest, [], "KsProcgenSimpleOffice");
            Assert.That(noSparseLighting.Status,
                Is.EqualTo(KsProcgenGeometryPipelineStatus.FallbackDisallowed));
            Assert.That(noSparseLighting.Issue?.Code, Is.EqualTo("SparseLightingDisallowed"));
            Assert.That(noSparseLighting.SemanticHash, Is.Zero);
            var noSizeShortfallRequest = new KsProcgenRequest
            {
                RequestId = "NoSizeShortfall",
                Shape = request.Shape,
                SizeMix =
                [
                    new KsProcgenRoomSizeGoal { Id = "tiny-room", MinCells = 1, MaxCells = 3 },
                ],
                FallbackPolicy = new KsProcgenFallbackPolicy { AllowRoomSizeShortfall = false },
            };
            var noSizeShortfall = KsProcgenGeometryPipeline.Plan(prototypeManager,
                noSizeShortfallRequest, [], "KsProcgenSimpleOffice");
            Assert.That(noSizeShortfall.Status,
                Is.EqualTo(KsProcgenGeometryPipelineStatus.FallbackDisallowed));
            Assert.That(noSizeShortfall.Issue?.Code, Is.EqualTo("RoomSizeShortfallDisallowed"));
            var noPartitionMergeRequest = new KsProcgenRequest
            {
                RequestId = "NoPartitionMerge",
                Shape = new KsProcgenShapeSpec
                {
                    AddRectangles =
                    [
                        new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(3, 2) },
                    ],
                },
                RootCells = [new Vector2i(0, 0), new Vector2i(2, 0)],
                FallbackPolicy = new KsProcgenFallbackPolicy { AllowMergedPartition = false },
            };
            var noPartitionMerge = KsProcgenGeometryPipeline.Plan(prototypeManager,
                noPartitionMergeRequest, [], "KsProcgenSimpleOffice");
            Assert.That(noPartitionMerge.Status,
                Is.EqualTo(KsProcgenGeometryPipelineStatus.FallbackDisallowed));
            Assert.That(noPartitionMerge.Issue?.Code, Is.EqualTo("PartitionFallbackDisallowed"));
            var pureReplay = KsProcgenGeometryPipeline.Plan(prototypeManager,
                request, [], "KsProcgenSimpleOffice");
            Assert.That(purePipeline.SemanticHash, Is.Not.EqualTo(0UL));
            Assert.That(pureReplay.SemanticHash, Is.EqualTo(purePipeline.SemanticHash));
            var windowRequest = new KsProcgenRequest
            {
                RequestId = "LoadedTinyWindows",
                Shape = request.Shape,
                WindowGoal = new KsProcgenWindowGoal
                {
                    ExteriorWindowFraction = 1f,
                    MinimumCount = 1,
                },
            };
            var inspectedWindowBoundary = new[]
            {
                new KsProcgenWindowBoundaryCell(new Vector2i(3, 4), true, true, true),
            };
            var windowedPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                windowRequest, [], "KsProcgenSimpleOffice",
                inspectedWindowBoundary: inspectedWindowBoundary);
            Assert.That(windowedPipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                windowedPipeline.Issue?.Message);
            Assert.That(windowedPipeline.Windows?.ChosenWindowCells,
                Is.EqualTo(new[] { new Vector2i(3, 4) }));
            Assert.That(windowedPipeline.Windows?.AirtightnessVerified, Is.False);
            windowRequest.WindowGoal.MinimumCount = 2;
            var hardWindowMiss = KsProcgenGeometryPipeline.Plan(prototypeManager,
                windowRequest, [], "KsProcgenSimpleOffice",
                inspectedWindowBoundary: inspectedWindowBoundary);
            Assert.That(hardWindowMiss.Status,
                Is.EqualTo(KsProcgenGeometryPipelineStatus.WindowTargetUnmet));
            Assert.That(hardWindowMiss.Issue?.Code, Is.EqualTo("WindowCountInfeasible"));
            var hybridRequest = new KsProcgenRequest
            {
                RequestId = "LoadedHybridTiny",
                Mode = KsProcgenMode.Hybrid,
                Shape = request.Shape,
                RootCells = [new Vector2i(3, 6)],
            };
            var hybridPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                hybridRequest, [family], "KsProcgenSimpleOffice",
                new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 67 });
            Assert.That(hybridPipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                hybridPipeline.Issue?.Message);
            Assert.That(hybridPipeline.Packing?.Placements.Count, Is.EqualTo(1));
            Assert.That(hybridPipeline.HullBoundary, Is.Null);
            Assert.That(hybridPipeline.Materials?.Tiles.Count, Is.EqualTo(1));
            Assert.That(hybridPipeline.SemanticHash, Is.Not.EqualTo(purePipeline.SemanticHash));
            Assert.That(tileStageSystem.TryStage(hybridPipeline).Status,
                Is.EqualTo(KsProcgenTileStageStatus.UnsupportedContent));
            var constantRequest = new KsProcgenRequest
            {
                RequestId = "LoadedConstantTiny",
                Mode = KsProcgenMode.Hybrid,
                Shape = request.Shape,
                RootCells = [new Vector2i(3, 6)],
                ConstantRegions =
                [
                    new KsProcgenConstantRegionSpec
                    {
                        Id = "Arrival",
                        SourceId = "DeclaredArrivalSource",
                        ContentFingerprint = "loaded-fixture",
                        Origin = new Vector2i(3, 4),
                        LocalCells = [new Vector2i(0, 0), new Vector2i(0, 1)],
                        Ports =
                        [
                            new KsProcgenConstantPortSpec
                            {
                                Id = "SouthDoor",
                                Threshold = new Vector2i(0, 1),
                                OutwardNormal = new Vector2i(0, 1),
                            },
                        ],
                    },
                ],
            };
            var constantPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                constantRequest, [], "KsProcgenSimpleOffice");
            Assert.That(constantPipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                constantPipeline.Issue?.Message);
            Assert.That(constantPipeline.PortNetwork?.Status,
                Is.EqualTo(KsProcgenPortNetworkStatus.Connected));
            Assert.That(constantPipeline.PortNetwork?.Groups.Single().RoomIds,
                Is.EqualTo(new[] { "constant:Arrival" }));
            Assert.That(constantPipeline.PortNetwork?.Groups.Single().RootCells, Is.EqualTo(1));
            Assert.That(constantPipeline.HasUnverifiedConstantRegions, Is.True);
            Assert.That(constantPipeline.ConstantContractHash, Is.Not.Null);
            var unknownThemePipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                request, [], "KsProcgenMissingTheme");
            Assert.That(unknownThemePipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.ThemeRejected));
            Assert.That(unknownThemePipeline.Materials, Is.Null);
            Assert.That(unknownThemePipeline.SemanticHash, Is.EqualTo(0UL));
            var roomOnlyRequest = new KsProcgenRequest
            {
                RequestId = "MandatoryRoomPlan",
                Shape = new KsProcgenShapeSpec
                {
                    AddRectangles =
                    [
                        new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(2, 2) },
                    ],
                },
            };
            var requiredContentPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                roomOnlyRequest, [], "KsProcgenMandatoryOfficeFixture");
            Assert.That(requiredContentPipeline.Status,
                Is.EqualTo(KsProcgenGeometryPipelineStatus.ContentUnmet),
                requiredContentPipeline.Issue?.Message);
            Assert.That(requiredContentPipeline.HasUnplacedRequiredEntityPacks, Is.True);
            Assert.That(requiredContentPipeline.SemanticHash, Is.EqualTo(0UL));
            Assert.That(requiredContentPipeline.Themes?.Regions.Single().UnplacedRequiredPacks.Single()
                .MinimumCount, Is.EqualTo(1));
            var noSparseFurnishingRequest = new KsProcgenRequest
            {
                RequestId = "NoSparseFurnishing",
                Shape = roomOnlyRequest.Shape,
                FallbackPolicy = new KsProcgenFallbackPolicy { AllowSparseFurnishing = false },
            };
            var noSparseFurnishing = KsProcgenGeometryPipeline.Plan(prototypeManager,
                noSparseFurnishingRequest, [], "KsProcgenSimpleOffice");
            Assert.That(noSparseFurnishing.Status,
                Is.EqualTo(KsProcgenGeometryPipelineStatus.FallbackDisallowed));
            Assert.That(noSparseFurnishing.Issue?.Code, Is.EqualTo("SparseFurnishingDisallowed"));
            var optionalRoomRequest = new KsProcgenRequest
            {
                RequestId = "OptionalOfficePlan",
                Shape = new KsProcgenShapeSpec
                {
                    AddRectangles =
                    [
                        new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(4, 4) },
                    ],
                },
            };
            var furnishedPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                optionalRoomRequest, [], "KsProcgenSimpleOffice");
            Assert.That(furnishedPipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                furnishedPipeline.Issue?.Message);
            Assert.That(furnishedPipeline.Furnishings.Single().Proposal.Entities
                .Select(entity => entity.EntryId), Does.Contain("Console"));
            Assert.That(furnishedPipeline.Lighting.Single().Proposal.Lights, Is.Not.Empty,
                $"Furnishings: {string.Join(';', furnishedPipeline.Furnishings.Single().Proposal.Entities.Select(entity => $"{entity.EntryId}@{entity.Cell}/q{entity.QuarterTurns}/approach{entity.InteractionApproach}"))}; " +
                $"protected: {string.Join(';', furnishedPipeline.Furnishings.Single().Proposal.ProtectedPassageCells)}");
            Assert.That(furnishedPipeline.Lighting.Single().Proposal.WorkingCoverageVerified, Is.False);
            Assert.That(furnishedPipeline.SemanticHash, Is.Not.EqualTo(0UL));
            var crowdedOfficeRequest = new KsProcgenRequest
            {
                RequestId = "CrowdedOfficePlan",
                Shape = new KsProcgenShapeSpec
                {
                    AddRectangles =
                    [
                        new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(3, 3) },
                    ],
                },
            };
            var crowdedOffice = KsProcgenGeometryPipeline.Plan(prototypeManager,
                crowdedOfficeRequest, [], "KsProcgenSimpleOffice");
            Assert.That(crowdedOffice.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned));
            Assert.That(crowdedOffice.Furnishings.Single().Proposal.Entities.Select(entity => entity.EntryId),
                Does.Contain("Console"));
            Assert.That(crowdedOffice.Lighting.Single().Proposal.Status, Is.EqualTo(KsProcgenLightingPlanStatus.Sparse));
            Assert.That(crowdedOffice.Lighting.Single().Proposal.Issue?.Code, Is.EqualTo("LightingEstimateShortfall"));
            Assert.That(crowdedOffice.Lighting.Single().Proposal.WorkingCoverageVerified, Is.False);
            optionalRoomRequest.FallbackPolicy.AllowMergedPartition = false;
            var stricterSamePlan = KsProcgenGeometryPipeline.Plan(prototypeManager,
                optionalRoomRequest, [], "KsProcgenSimpleOffice");
            Assert.That(stricterSamePlan.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned));
            Assert.That(stricterSamePlan.SemanticHash, Is.Not.EqualTo(furnishedPipeline.SemanticHash));

            Assert.That(KsProcgenThemeValidator.TryResolve(prototypeManager, "KsProcgenSimpleOffice",
                out var theme, out issue), Is.True, issue?.Message);
            Assert.That(theme?.TilePacks.Count, Is.EqualTo(1));
            Assert.That(theme?.EntityPacks.Count, Is.EqualTo(2));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                seed: 17, roomId: "OfficeA", out var firstChoice, out issue), Is.True, issue?.Message);
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                seed: 17, roomId: "OfficeA", out var replayChoice, out issue), Is.True, issue?.Message);
            Assert.That(replayChoice?.TilePaletteId, Is.EqualTo(firstChoice?.TilePaletteId));
            Assert.That(replayChoice?.WallFamilyId, Is.EqualTo(firstChoice?.WallFamilyId));
            Assert.That(replayChoice?.DominantEntityPackId, Is.EqualTo(firstChoice?.DominantEntityPackId));
            Assert.That(firstChoice?.DominantEntityPackId, Is.EqualTo("KsProcgenOfficeWorkstations"));
            Assert.That(firstChoice?.SupportingEntityPackIds, Is.EquivalentTo(new[] { "KsProcgenOfficeStorage" }));
            Assert.That(KsProcgenThemeValidator.TryResolve(prototypeManager, "KsProcgenMissingTheme",
                out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("UnknownTheme"));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                seed: 17, roomId: "", out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("InvalidRoomId"));
        });
    }

    [Test]
    public async Task MandatoryContentPrecedesOptionalFurnishing()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var prototypeManager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenWeightedCoresThemeFixture", 17, "MandatoryScheduling",
                out var weightedTheme, out _), Is.True);
            var weightedFloor = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
            var weightedRegion = new KsProcgenThemedRegion("MandatoryScheduling", KsProcgenZoneKind.RoomProposal,
                ["MandatoryScheduling"], weightedFloor, [], weightedTheme!, []);
            var weightedPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = weightedFloor,
            };
            var mandatorySupportRegion = weightedRegion with
            {
                Theme = weightedTheme! with
                {
                    SupportingEntityPackIds = ["KsProcgenMandatorySupportCoreFixture"],
                },
            };
            var mandatorySupport = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                mandatorySupportRegion, weightedPartition, 17);
            Assert.That(mandatorySupport.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(mandatorySupport.Entities.Single().PackId, Is.EqualTo("KsProcgenMandatorySupportCoreFixture"),
                "Required support must claim the only free cell before optional dominant furniture.");
            Assert.That(mandatorySupport.Omissions.All(omission => omission.PackId == weightedTheme.DominantEntityPackId),
                Is.True, "Retained mandatory support must not be reported as unavailable.");
            Assert.That(mandatorySupport.PlacedDominantClusters, Is.Zero);
            Assert.That(KsProcgenFurnishingPlanner.Plan(prototypeManager,
                mandatorySupportRegion, weightedPartition, 17).Entities, Is.EqualTo(mandatorySupport.Entities));
            var mandatoryChoiceRegion = weightedRegion with
            {
                Theme = weightedTheme! with
                {
                    SupportingEntityPackIds = ["KsProcgenMandatorySupportChoiceFixture"],
                },
                UnplacedRequiredPacks = [new("KsProcgenMandatorySupportChoiceFixture", 1)],
            };
            var mandatoryChoice = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                mandatoryChoiceRegion, weightedPartition, 17);
            Assert.That(mandatoryChoice.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(mandatoryChoice.Entities.Single().CoreId, Is.EqualTo("Small"));
            Assert.That(mandatoryChoice.Entities.Single().PackId, Is.EqualTo("KsProcgenMandatorySupportChoiceFixture"));
            Assert.That(mandatoryChoice.CandidateProbes, Is.GreaterThan(1),
                "The required pack must try the heavier nonfitting core before its fitting alternative.");
            var mandatoryChoiceWithoutDominant = mandatoryChoiceRegion with
            {
                Theme = mandatoryChoiceRegion.Theme with { DominantEntityPackId = null },
            };
            var supportOnlyChoice = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                mandatoryChoiceWithoutDominant, weightedPartition, 17);
            Assert.That(supportOnlyChoice.Entities.Count, Is.EqualTo(1),
                "A core selected to meet a pack minimum must not receive another initial optional copy.");
            Assert.That(supportOnlyChoice.Entities.Single().ClusterIndex, Is.Zero);
            Assert.That(supportOnlyChoice.Omissions.Single().Reason, Is.EqualTo("CoreCannotFit/TooWide"),
                "The already selected Small core must not be attempted again as optional content.");
            var impossibleSupport = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                mandatorySupportRegion with
                {
                    Theme = mandatorySupportRegion.Theme with
                    {
                        SupportingEntityPackIds = ["KsProcgenImpossibleSupportCoreFixture"],
                    },
                }, weightedPartition, 17);
            Assert.That(impossibleSupport.Status, Is.EqualTo(KsProcgenFurnishingStatus.MandatoryUnmet));
            Assert.That(impossibleSupport.Entities, Is.Empty);
            Assert.That(impossibleSupport.RelationWitnesses, Is.Empty);
            var mandatoryBudgetFloor = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0) };
            var mandatorySupportBudget = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                mandatorySupportRegion with
                {
                    FloorCells = mandatoryBudgetFloor,
                    Theme = mandatorySupportRegion.Theme with
                    {
                        SupportingEntityPackIds = ["KsProcgenImpossibleSupportCoreFixture"],
                    },
                }, new KsProcgenPartitionResult
                {
                    Status = KsProcgenPartitionStatus.Proposed,
                    FloorCells = mandatoryBudgetFloor,
                }, 17, maxCandidateProbes: 1);
            Assert.That(mandatorySupportBudget.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(mandatorySupportBudget.Entities, Is.Empty);
        });
    }

    [Test]
    public async Task MandatoryCoresBacktrackAcrossPosesChoicesAndVariants()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var prototypeManager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager,
                "KsProcgenWeightedCoresThemeFixture", 17, "MandatoryBacktracking", out var theme, out _), Is.True);
            var floor = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
                .Select(y => new Vector2i(x, y))).Where(cell => cell != new Vector2i(2, 0)).ToArray();
            var walls = new[] { new Vector2i(2, 0), new Vector2i(3, 1) };
            var region = new KsProcgenThemedRegion("MandatoryBacktracking", KsProcgenZoneKind.RoomProposal,
                ["MandatoryBacktracking"], floor, walls, theme! with
                {
                    DominantEntityPackId = "KsProcgenMandatoryFlexiblePackFixture",
                    SupportingEntityPackIds = ["KsProcgenRequiredCornerPackFixture"],
                }, []);
            var partition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = floor,
                WallCells = walls,
            };
            var joint = KsProcgenFurnishingPlanner.Plan(prototypeManager, region, partition, 17);
            Assert.That(joint.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed), joint.Issue?.Code);
            var flexible = joint.Entities.Single(entity => entity.PackId == "KsProcgenMandatoryFlexiblePackFixture");
            var corner = joint.Entities.Single(entity => entity.PackId == "KsProcgenRequiredCornerPackFixture");
            Assert.That(flexible.Cell, Is.Not.EqualTo(new Vector2i(1, 1)),
                "The first valid wide-table pose consumes the only required corner and must be revisited.");
            Assert.That(corner.Cell, Is.EqualTo(new Vector2i(2, 1)));
            Assert.That(joint.MandatoryBacktracks, Is.GreaterThan(0));
            Assert.That(joint.Entities.SelectMany(entity => entity.OccupiedCells).Distinct().Count(), Is.EqualTo(3));
            Assert.That(joint.RelationWitnesses.Count, Is.EqualTo(1), "Rejected branches must not leave witnesses.");
            Assert.That(joint.RelationWitnesses.Single().SubjectCell, Is.EqualTo(corner.Cell));
            var blocked = joint.Entities.SelectMany(entity => entity.OccupiedCells).Concat(walls).ToHashSet();
            var expectedPath = KsProcgenTraversal.FindCleanPath(new Vector2i(0, 0),
                flexible.DeclaredApproachLanding!.Value, floor.ToHashSet(), blocked);
            Assert.That(expectedPath, Is.Not.Empty);
            Assert.That(joint.ProtectedPassageCells, Is.EquivalentTo(expectedPath),
                "Only the accepted branch's root/landing path should survive rollback.");
            var replay = KsProcgenFurnishingPlanner.Plan(prototypeManager, region, partition, 17);
            Assert.That(replay.Entities, Is.EqualTo(joint.Entities));
            Assert.That(replay.RelationWitnesses, Is.EqualTo(joint.RelationWitnesses));
            Assert.That(replay.MandatoryBacktracks, Is.EqualTo(joint.MandatoryBacktracks));

            var narrowFloor = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0) };
            var narrowPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = narrowFloor,
            };
            var choiceRegion = region with
            {
                FloorCells = narrowFloor,
                WallCells = [],
                Theme = region.Theme with
                {
                    DominantEntityPackId = "KsProcgenMandatoryCoreChoiceFixture",
                    SupportingEntityPackIds = ["KsProcgenMandatorySupportChoiceFixture"],
                },
                UnplacedRequiredPacks =
                [
                    new("KsProcgenMandatoryCoreChoiceFixture", 1),
                    new("KsProcgenMandatorySupportChoiceFixture", 1),
                ],
            };
            var choices = KsProcgenFurnishingPlanner.Plan(prototypeManager, choiceRegion, narrowPartition, 17);
            Assert.That(choices.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse), choices.Issue?.Code);
            Assert.That(choices.Entities.Count, Is.EqualTo(2));
            Assert.That(choices.Entities.All(entity => entity.CoreId == "Small" && entity.OccupiedCells.Count == 1), Is.True);
            Assert.That(choices.Entities.All(entity => entity.ClusterIndex == 0), Is.True);
            Assert.That(choices.MandatoryBacktracks, Is.GreaterThan(0),
                "A wide core that fits locally must be replaced when it leaves no room for another required pack.");
            var variantRegion = choiceRegion with
            {
                Theme = choiceRegion.Theme with { DominantEntityPackId = "KsProcgenMandatoryVariantPackFixture" },
                UnplacedRequiredPacks =
                [
                    new("KsProcgenMandatoryVariantPackFixture", 1),
                    new("KsProcgenMandatorySupportChoiceFixture", 1),
                ],
            };
            var variants = KsProcgenFurnishingPlanner.Plan(prototypeManager, variantRegion, narrowPartition, 17);
            Assert.That(variants.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse), variants.Issue?.Code);
            Assert.That(variants.Entities.Single(entity => entity.PackId == "KsProcgenMandatoryVariantPackFixture").VariantId,
                Is.EqualTo("Compact"));
            Assert.That(variants.Entities.Count, Is.EqualTo(2));
            Assert.That(variants.MandatoryBacktracks, Is.GreaterThan(0));
            var budget = KsProcgenFurnishingPlanner.Plan(prototypeManager, choiceRegion,
                narrowPartition, 17, maxCandidateProbes: 1);
            Assert.That(budget.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(budget.Entities, Is.Empty);
            Assert.That(budget.RelationWitnesses, Is.Empty);
            var depth = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                choiceRegion with
                {
                    Theme = choiceRegion.Theme with
                    {
                        DominantEntityPackId = "KsProcgenMandatoryDepthFixture",
                        SupportingEntityPackIds = [],
                    },
                    UnplacedRequiredPacks = [],
                }, narrowPartition, 17);
            Assert.That(depth.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(depth.Issue?.Code, Is.EqualTo("FurnishingMandatoryDepthBudget"));
            Assert.That(depth.Entities, Is.Empty);
        });
    }
}
