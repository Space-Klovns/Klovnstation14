#nullable enable
using System;
using System.Linq;
using System.Threading;
using Content.Server._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    [TestCase(75)] [TestCase(100)]
    public async Task LaterSelectedRoomPreparationFailureLeavesEveryRoomAndTileUnwritten(int coverage)
    {
        var yaml = CopySource().Replace("- proto: Table", "- proto: ''") +
            "- proto: ''\n  entities:\n  - uid: 20\n    components:\n    - type: Transform\n      parent: 1\n      pos: 0.25,1.25\n" +
            "    - type: KsProcgenMapReferenceProbe\n      unknownReferenceField: 99999\n";
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, root) =>
        {
            var spec = LayoutProjectSpec(project, root.Bytes);
            spec.Layouts!.Families["Choice"].AllowedQuarterTurns = new() { 0 };
            spec.Layouts.Rooms["Bad"] = new() { SourceKey = "test-room", ContentFingerprint = Fingerprint(root.Bytes),
                SourceOrigin = new(0, 1), Cells = new() { new(0, 0) } };
            foreach (var option in spec.Layouts.Families["Choice"].Options) option.Members[1].Room = "Bad";
            project = Project(spec); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            // A prepares successfully; B has an opaque registered override. Neither A nor any floor may be written.
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var startup = Pair.Server.System<KsProcgenRoomCopyTestSystem>(); var calls = 0;
                startup.StartupAction = _ => calls++;
                try
                {
                    Assert.That(Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!,
                        gridUid, allocations, 32, out var result, out issue,
                        budgets: new() { Packing = new() { PreferredPrefabCoveragePercent = coverage } }), Is.False);
                    Assert.That(issue?.Code, Is.EqualTo("RoomInstantiationUninspectedComponent"));
                    Assert.That(result, Is.Null); Assert.That(calls, Is.Zero);
                    Assert.That(allocations.Count, Is.EqualTo(2));
                    Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
                }
                finally { startup.StartupAction = null; }
            }, layFloor: false);
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task NativeRoomPreparationAllocatesNothingAndItsLoadingInputsAreSingleUse(bool container)
    {
        await AssertOnServer(() => WithSource(CopySource(container), (capture, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var copier = Pair.Server.System<KsProcgenRoomInstantiationSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(copier.TryPrepare(extraction!, "Room", default, 0, 2, out var prepared, out issue), Is.True, message: issue?.Code);
            Assert.That(prepared!.ImageCharacters, Is.GreaterThan(0));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            root.Available = false;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(copier.TryInstantiatePrepared(prepared, gridUid, allocations, 32, out var members, out issue), Is.True, message: issue?.Code);
                Assert.That(SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(members![10]).Target, Is.EqualTo(members[2]));
                var allocated = allocations.Count;
                Assert.That(copier.TryInstantiatePrepared(prepared, gridUid, allocations, 32, out var duplicate, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("RoomInstantiationPreparedConsumed"));
                Assert.That(duplicate, Is.Null); Assert.That(allocations.Count, Is.EqualTo(allocated));
            });
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(copier.TryInstantiatePrepared(prepared, gridUid, allocations, 32, out var duplicate, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("RoomInstantiationPreparedConsumed"));
                Assert.That(duplicate, Is.Null); Assert.That(allocations.Count, Is.EqualTo(2));
            });
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task PreparedRoomRechecksCancellationAndDefinitionsBeforeAllocating(bool cancel)
    {
        var yaml = CopySource().Replace("- proto: Table", "- proto: KsProcgenRoomCopyCapturedFixture");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var copier = Pair.Server.System<KsProcgenRoomInstantiationSystem>();
            Assert.That(copier.TryPrepare(extraction!, "Room", default, 0, 2, out var prepared, out issue), Is.True, message: issue?.Code);
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("KsProcgenRoomCopyCapturedFixture"));
            Assert.That(prototype.TryComp<KsProcgenCapturedDefaultsProbeComponent>(out var defaults, SEntMan.ComponentFactory), Is.True);
            var saved = defaults!.Value;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                using var cancellation = new CancellationTokenSource();
                if (cancel) cancellation.Cancel(); else defaults.Value++;
                try
                {
                    Assert.That(copier.TryInstantiatePrepared(prepared!, gridUid, allocations, 32, out var members, out issue,
                        cancellationToken: cancellation.Token), Is.False);
                    Assert.That(issue?.Code, Is.EqualTo(cancel ? "RoomInstantiationCancelled" : "PrototypeManifestChanged"));
                    Assert.That(members, Is.Null); Assert.That(allocations.Count, Is.EqualTo(2));
                }
                finally { defaults.Value = saved; }
            });
        }));
    }

    [Test]
    public async Task CompleteRoomPreparationFailureDoesNotWriteTerrainBeforeRejectingAnUnsupportedAnchor()
    {
        var yaml = CopySource().Replace("anchored: false", "anchored: true");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiateRoom(source, extraction!, "Room", gridUid,
                    default, 0, allocations, 32, out var instance, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("RoomInstantiationAnchoredPoseUnsupported"));
                Assert.That(instance, Is.Null); Assert.That(allocations.Count, Is.EqualTo(2));
                Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
        }));
    }
}
