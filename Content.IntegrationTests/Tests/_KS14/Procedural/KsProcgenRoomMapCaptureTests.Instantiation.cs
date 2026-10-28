#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomCopyTestSystem : EntitySystem
{
    public Action<EntityUid>? StartupAction;
    public Action<EntityUid>? MapInitAction;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KsProcgenMapReferenceProbeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsProcgenMapReferenceProbeComponent, MapInitEvent>(OnMapInit);
    }
    private void OnStartup(Entity<KsProcgenMapReferenceProbeComponent> entity, ref ComponentStartup args) => StartupAction?.Invoke(entity.Owner);
    private void OnMapInit(Entity<KsProcgenMapReferenceProbeComponent> entity, ref MapInitEvent args) => MapInitAction?.Invoke(entity.Owner);
}

public sealed partial class KsProcgenRoomMapCaptureTests
{
    [TestPrototypes]
    private const string CopyPrototypes = """
- type: entity
  id: KsProcgenRoomCopyCapturedFixture
  parent: Table
  components:
  - type: KsProcgenCapturedDefaultsProbe
    value: 23
    numbers: [5, 6]
""";

    private static string CopySource(bool container = false) =>
        GridYaml().Replace("    - type: MapGrid", "    - type: Transform\n      parent: 50\n    - type: MapGrid") +
        EntityBranch.Replace("anchored: true", "anchored: false").Replace("- proto: Paper", AuthoredOverrides +
            "    - type: KsProcgenMapReferenceProbe\n      target: 2\n      networkTarget: 2\n      mapTarget: 50\n      nested: {2: [10, 2]}\n" +
            (container ? "    - type: ContainerContainer\n      containers:\n        contents: !type:Container\n          ents: [2]\n" : "") + "- proto: Paper") +
        "    - type: MetaData\n      name: Authored child\n" +
        "- proto: ''\n  entities:\n  - uid: 50\n    components:\n    - type: Map\n";

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public async Task NativeRoomCopyRestoresOverridesAndRemapsGraphAtEveryQuarterTurn(int turns)
    {
        await AssertOnServer(() => WithSource(CopySource(), (capture, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(0, 0) };
            spec.Layouts.Rooms["Room"].SourceOrigin = new(1, 0); project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            root.Available = false;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var startup = Pair.Server.System<KsProcgenRoomCopyTestSystem>(); var calls = 0; var mapInitCalls = 0;
                startup.MapInitAction = _ => mapInitCalls++;
                startup.StartupAction = uid =>
                {
                    calls++;
                    var probe = SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(uid);
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(probe.Target).EntityName, Is.EqualTo("Authored child"),
                        message: "All target components must be loaded before the first startup callback.");
                };
                try
                {
                    Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiate(extraction!, "Room", gridUid,
                        new(2, 3), turns, allocations, 32, out var members, out issue), Is.True, message: issue?.ToString());
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(mapInitCalls, Is.Zero);
                    var tableUid = members![10]; var paperUid = members[2];
                    var tableTransform = SEntMan.GetComponent<TransformComponent>(tableUid);
                    var paperTransform = SEntMan.GetComponent<TransformComponent>(paperUid);
                    var expected = turns switch { 0 => new Vector2(2.25f, 3.25f), 1 => new(2.25f, 3.75f),
                        2 => new(2.75f, 3.75f), _ => new(2.75f, 3.25f) };
                    Assert.That(tableTransform.ParentUid, Is.EqualTo(gridUid));
                    Assert.That(Vector2.Distance(tableTransform.LocalPosition, expected), Is.LessThan(0.00001f));
                    var expectedRotation = Angle.FromDegrees(90.0).Theta + KsProcgenRotation.ToEngineAngle(turns).Theta;
                    Assert.That(Angle.ShortestDistance(tableTransform.LocalRotation, new Angle(expectedRotation)).Theta, Is.EqualTo(0.0).Within(0.000001));
                    Assert.That(tableTransform.Anchored, Is.False);
                    Assert.That(paperTransform.ParentUid, Is.EqualTo(tableUid));
                    Assert.That(paperTransform.LocalPosition, Is.EqualTo(new Vector2(0.25f, 0)));
                    Assert.That(paperTransform.LocalRotation.Theta, Is.EqualTo(Angle.FromDegrees(45.0).Theta).Within(0.000001));
                    var metadata = SEntMan.GetComponent<MetaDataComponent>(tableUid);
                    Assert.That(metadata.EntityName, Is.EqualTo("Authored table"));
                    Assert.That(metadata.EntityDescription, Is.EqualTo("Not a prototype default"));
                    Assert.That(metadata.EntityLifeStage, Is.EqualTo(EntityLifeStage.Initialized));
                    var light = SEntMan.GetComponent<Robust.Server.GameObjects.PointLightComponent>(tableUid);
                    Assert.That(light.Radius, Is.EqualTo(3.25f)); Assert.That(light.Energy, Is.EqualTo(0.75f));
                    var probe = SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(tableUid);
                    Assert.That(probe.Target, Is.EqualTo(paperUid));
                    Assert.That(probe.NetworkTarget, Is.EqualTo(SEntMan.GetNetEntity(paperUid)));
                    Assert.That(probe.MapTarget, Is.EqualTo(tableTransform.MapID));
                    Assert.That(probe.Nested[paperUid], Is.EqualTo(new[] { SEntMan.GetNetEntity(tableUid), SEntMan.GetNetEntity(paperUid) }));
                }
                finally { startup.StartupAction = null; startup.MapInitAction = null; }
            });
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task RepeatedRoomCopiesKeepReferencesAndContainerContentsWithinTheirOwnInstance(bool container)
    {
        await AssertOnServer(() => WithSource(CopySource(container), (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var copier = Pair.Server.System<KsProcgenRoomInstantiationSystem>();
                Assert.That(copier.TryInstantiate(extraction!, "Room", gridUid, default, 0, allocations, 32,
                    out var first, out issue), Is.True, message: issue?.ToString());
                Assert.That(copier.TryInstantiate(extraction!, "Room", gridUid, new(8, 0), 1, allocations, 32,
                    out var second, out issue), Is.True, message: issue?.ToString());
                Assert.That(first!.Values.Intersect(second!.Values), Is.Empty);
                foreach (var members in new[] { first, second })
                {
                    var probe = SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(members[10]);
                    Assert.That(probe.Target, Is.EqualTo(members[2]));
                    Assert.That(probe.Nested.Keys.Single(), Is.EqualTo(members[2]));
                    if (!container) continue;
                    var containers = Pair.Server.System<SharedContainerSystem>();
                    Assert.That(containers.TryGetContainer(members[10], "contents", out var contents), Is.True);
                    Assert.That(contents!.ContainedEntities, Is.EqualTo(new[] { members[2] }));
                    Assert.That(contents.Owner, Is.EqualTo(members[10]));
                }
                SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(first[10]).Nested.Clear();
                Assert.That(SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(second[10]).Nested, Has.Count.EqualTo(1));
            });
        }));
    }

    [TestCase("images", "RoomInstantiationImageBudget")]
    [TestCase("allocations", "RoomInstantiationAllocationBudget")]
    [TestCase("cancelled", "RoomInstantiationCancelled")]
    [TestCase("anchored-offset", "RoomInstantiationAnchoredPoseUnsupported")]
    [TestCase("no-rotation", "RoomInstantiationRotationUnsupported")]
    public async Task RoomCopyPreflightRejectsWithoutAllocatingPartialMembers(string mode, string expectedIssue)
    {
        await AssertOnServer(() => WithSource(mode == "anchored-offset" ? CopySource().Replace("anchored: false", "anchored: true") :
            mode == "no-rotation" ? CopySource().Replace("rot: 90", "rot: 0").Replace("noRot: false", "noRot: true") : CopySource(),
            (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                using var cancellation = new CancellationTokenSource(); if (mode == "cancelled") cancellation.Cancel();
                var before = allocations.Count;
                Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiate(extraction!, "Room", gridUid,
                    default, mode == "no-rotation" ? 1 : 0, allocations, mode == "allocations" ? allocations.Count : 32, out var members, out issue,
                    maximumImageCharacters: mode == "images" ? 0 : 4_194_304, cancellationToken: cancellation.Token), Is.False);
                Assert.That(issue?.Code, Is.EqualTo(expectedIssue)); Assert.That(members, Is.Null);
                Assert.That(allocations.Count, Is.EqualTo(before));
            });
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task RoomCopyStartupCancellationAndExtraAllocationsAreRetiredByTheOwningTransaction(bool cancel)
    {
        await AssertOnServer(() => WithSource(CopySource(), (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                using var cancellation = new CancellationTokenSource();
                var startup = Pair.Server.System<KsProcgenRoomCopyTestSystem>();
                startup.StartupAction = _ =>
                {
                    if (cancel) cancellation.Cancel();
                    else SEntMan.SpawnEntity("Paper", new EntityCoordinates(gridUid, new Vector2(4.5f, 4.5f)));
                };
                try
                {
                    Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiate(extraction!, "Room", gridUid,
                        default, 0, allocations, allocations.Count + 2, out var members, out issue,
                        cancellationToken: cancellation.Token), Is.False);
                    Assert.That(issue?.Code, Is.EqualTo(cancel ? "RoomInstantiationCancelled" : "RoomInstantiationAllocationBudget"));
                    Assert.That(members, Is.Null);
                }
                finally { startup.StartupAction = null; }
            });
        }));
    }

    [TestCase(0)] [TestCase(1)]
    public async Task CenteredAnchoredRoomsPreserveTheirMountAndExplicitComponentRemovals(int turns)
    {
        var yaml = CopySource().Replace("anchored: false", "anchored: true").Replace("pos: 1.25,0.25", "pos: 1.5,0.5")
            .Replace("  - uid: 10\n", "  - uid: 10\n    missingComponents: [Climbable]\n");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiate(extraction!, "Room", gridUid,
                    new(2, 3), turns, allocations, 32, out var members, out issue), Is.True, message: issue?.ToString());
                var tableUid = members![10];
                var transform = SEntMan.GetComponent<TransformComponent>(tableUid);
                Assert.That(transform.Anchored, Is.True); Assert.That(transform.ParentUid, Is.EqualTo(gridUid));
                Assert.That(transform.LocalPosition.X - MathF.Floor(transform.LocalPosition.X), Is.EqualTo(0.5f));
                Assert.That(transform.LocalPosition.Y - MathF.Floor(transform.LocalPosition.Y), Is.EqualTo(0.5f));
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(tableUid).EntityPrototype!.Components.ContainsKey("Climbable"), Is.True);
                Assert.That(SEntMan.HasComponent(tableUid, SEntMan.ComponentFactory.GetRegistration("Climbable").Type), Is.False);
            });
        }));
    }

    [Test]
    public async Task UnprototypedRoomMembersRestoreTheirExplicitComponentsAndReferences()
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"),
            (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiate(extraction!, "Room", gridUid,
                    default, 0, allocations, 32, out var members, out issue), Is.True, message: issue?.ToString());
                var rootUid = members![10];
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(rootUid).EntityPrototype, Is.Null);
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(rootUid).EntityName, Is.EqualTo("Authored table"));
                Assert.That(SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(rootUid).Target, Is.EqualTo(members[2]));
                Assert.That(SEntMan.GetComponent<Robust.Server.GameObjects.PointLightComponent>(rootUid).Radius, Is.EqualTo(3.25f));
            });
        }));
    }

    [Test]
    public async Task AllocationTimePrototypeDriftCannotReplaceCapturedDefaultsDuringOverrideMerging()
    {
        var yaml = CopySource().Replace("- proto: Table", "- proto: KsProcgenRoomCopyCapturedFixture")
            .Replace("- proto: Paper", "    - type: KsProcgenCapturedDefaultsProbe\n      numbers: [91]\n- proto: Paper");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("KsProcgenRoomCopyCapturedFixture"));
            Assert.That(prototype.TryComp<KsProcgenCapturedDefaultsProbeComponent>(out var liveDefaults, SEntMan.ComponentFactory), Is.True);
            var savedValue = liveDefaults!.Value;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var rootUid = EntityUid.Invalid;
                void OnAdded(Entity<MetaDataComponent> entity)
                {
                    if (rootUid != EntityUid.Invalid) return;
                    rootUid = entity.Owner;
                    liveDefaults.Value = 99;
                }
                SEntMan.EntityAdded += OnAdded;
                try
                {
                    Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiate(extraction!, "Room", gridUid,
                        default, 0, allocations, 32, out var members, out issue), Is.False);
                    Assert.That(members, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                    var copiedDefaults = SEntMan.GetComponent<KsProcgenCapturedDefaultsProbeComponent>(rootUid);
                    Assert.That(copiedDefaults.Value, Is.EqualTo(23), message: "The copy must merge its partial override with captured defaults, even when live defaults drift after preflight.");
                    Assert.That(copiedDefaults.Numbers, Is.EqualTo(new[] { 91 }));
                }
                finally { SEntMan.EntityAdded -= OnAdded; liveDefaults.Value = savedValue; }
            });
        }));
    }

    [TestCase("move")] [TestCase("child-move")] [TestCase("queue-delete")] [TestCase("metadata")]
    public async Task LaterRoomMemberStartupCannotInvalidateAnEarlierMembersVerification(string mode)
    {
        var yaml = CopySource().Replace("      name: Authored child\n", "      name: Authored child\n    - type: KsProcgenMapReferenceProbe\n");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var firstUid = EntityUid.Invalid; var calls = 0;
                var startup = Pair.Server.System<KsProcgenRoomCopyTestSystem>();
                startup.StartupAction = uid =>
                {
                    calls++;
                    if (firstUid == EntityUid.Invalid) { firstUid = uid; return; }
                    switch (mode)
                    {
                        case "move":
                            Pair.Server.System<SharedTransformSystem>().SetLocalPosition(firstUid,
                                SEntMan.GetComponent<TransformComponent>(firstUid).LocalPosition + Vector2.UnitX);
                            break;
                        case "child-move":
                            Pair.Server.System<SharedTransformSystem>().SetLocalPosition(uid,
                                SEntMan.GetComponent<TransformComponent>(uid).LocalPosition + Vector2.UnitX);
                            break;
                        case "queue-delete": SEntMan.QueueDeleteEntity(firstUid); break;
                        default:
                            var prototype = SEntMan.GetComponent<MetaDataComponent>(firstUid).EntityPrototype!;
                            prototype.MapSavable = !prototype.MapSavable;
                            break;
                    }
                };
                try
                {
                    Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiate(extraction!, "Room", gridUid,
                        default, 0, allocations, 32, out var members, out issue), Is.False);
                    Assert.That(calls, Is.EqualTo(2)); Assert.That(members, Is.Null);
                    Assert.That(issue?.Code, Is.EqualTo(mode == "metadata" ? "CapturedPrototypeRoundtripDrift" : "RoomInstantiationMemberDrift"));
                }
                finally { startup.StartupAction = null; }
            });
        }));
    }

    [TestCase("entity")] [TestCase("network")] [TestCase("map")]
    public async Task CapturedReferenceReadersRejectUnboundAliasesInsteadOfDroppingLinks(string kind)
    {
        await AssertOnServer(() =>
        {
            var context = Pair.Server.System<KsProcgenRoomInstantiationSystem>().CreateInstanceContext();
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var node = new ValueDataNode("99999");
            Action read = kind switch
            {
                "entity" => () => serialization.Read<EntityUid>(node, context: context),
                "network" => () => serialization.Read<NetEntity>(node, context: context),
                _ => () => serialization.Read<MapId>(node, context: context),
            };
            var exception = Assert.Catch(read.Invoke);
            Assert.That(exception!.GetBaseException().Message, Does.Contain("Unbound captured"));
            Assert.That(serialization.Read<EntityUid>(new ValueDataNode("invalid"), context: context), Is.EqualTo(EntityUid.Invalid));
            Assert.That(serialization.Read<NetEntity>(new ValueDataNode("invalid"), context: context), Is.EqualTo(NetEntity.Invalid));
            Assert.That(serialization.Read<MapId>(new ValueDataNode("nullspace"), context: context), Is.EqualTo(MapId.Nullspace));
        });
    }

    private void WithRoomCopyTransaction(Action<EntityUid, List<EntityUid>> action, bool layFloor = true)
    {
        var before = SEntMan.GetEntities().ToHashSet();
        var operations = Pair.Server.System<KsProcgenPreviewOperationSystem>(); var owner = new object();
        Assert.That(operations.TryEnter(owner), Is.True);
        var allocations = new List<EntityUid>(); var maps = Pair.Server.System<SharedMapSystem>();
        var mapId = MapId.Nullspace; var mapUid = EntityUid.Invalid;
        try
        {
            operations.SetCapture(owner, allocations);
            mapUid = maps.CreateMap(out mapId, runMapInit: false); maps.SetPaused(mapId, true);
            var grid = maps.CreateGridEntity(mapId); grid.Comp.CanSplit = false;
            var tileId = Pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId;
            if (layFloor)
                for (var x = -3; x <= 11; x++)
                    for (var y = -3; y <= 7; y++) maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(tileId));
            action(grid.Owner, allocations);
        }
        finally
        {
            operations.SetCapture(owner, null);
            try { Pair.Server.System<KsProcgenStageOwnershipSystem>().Cleanup(mapId, mapUid, allocations); }
            finally { operations.Exit(owner); }
        }
        Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before), message: "All graph members and startup allocations must retire with their owner.");
    }
}
