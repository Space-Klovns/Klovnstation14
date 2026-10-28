#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    [TestCase(75, "L", 3)] [TestCase(100, "Four", 4)]
    public async Task CompleteNativeLayoutPlanningNeedsNoMapAndFreezesSelectionBeforeAllocation(int coverage, string option, int count)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var before = SEntMan.GetEntities().ToHashSet(); var system = Pair.Server.System<KsProcgenLayoutInstantiationSystem>();
            Assert.That(Pair.Server.System<KsProcgenPreviewOperationSystem>().Active, Is.False);
            var budgets = new KsProcgenLayoutInstantiationBudgets { Packing = new() { PreferredPrefabCoveragePercent = coverage } };
            Assert.That(system.TryPrepare(project, "Area", source, extraction!, 32, out var plan, out issue, budgets: budgets),
                Is.True, message: issue?.ToString());
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            Assert.That(plan!.DocumentId, Is.EqualTo(project.DocumentId)); Assert.That(plan.Revision, Is.EqualTo(project.Revision));
            Assert.That(plan.ProjectDeclarationHash, Is.EqualTo(project.DeclarationHash)); Assert.That(plan.AreaId, Is.EqualTo("Area"));
            Assert.That(plan.SourceContentHash, Is.EqualTo(source.ContentHash)); Assert.That(plan.ExtractionContentHash, Is.EqualTo(extraction!.ContentHash));
            Assert.That(plan.RoomCount, Is.EqualTo(count)); Assert.That(plan.EntityCount, Is.EqualTo(2 * count));
            Assert.That(plan.TerrainCellCount, Is.EqualTo(count)); Assert.That(plan.SourceTileProbes, Is.EqualTo(4 * count));
            Assert.That(plan.ImageCharacters, Is.GreaterThan(0)); Assert.That(plan.GeneratedCells, Has.Count.EqualTo(4 - count));
            Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) plan.GeneratedCells).Clear());
            // Materialization must use the frozen plan, not reread source files or rerun mutable packing preferences.
            budgets.Packing.PreferredPrefabCoveragePercent = coverage == 75 ? 100 : 75; budgets.Packing.MaxSearchNodes = 0;
            root.Available = false; Array.Fill(root.Bytes, (byte) 0);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(system.TryInstantiatePrepared(plan, gridUid, allocations, 32, out var result, out issue), Is.True, message: issue?.ToString());
                Assert.That(result!.Packing.Placements.Single().OptionId, Is.EqualTo(option));
                Assert.That(result.Rooms, Has.Count.EqualTo(count));
                Assert.That(result.GeneratedCells, Is.EqualTo(plan.GeneratedCells));
            }, layFloor: false);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(system.TryInstantiatePrepared(plan, gridUid, allocations, 32, out var result, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("PreparedLayoutConsumed")); Assert.That(result, Is.Null);
                Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
        }));
    }

    [TestCase("context")] [TestCase("cancelled")] [TestCase("members")]
    public async Task PreparedLayoutEntryPreconditionsRejectBeforeWritesAndDoNotConsumeTheLoadingInputs(string mode)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var system = Pair.Server.System<KsProcgenLayoutInstantiationSystem>();
            Assert.That(system.TryPrepare(project, "Area", source, extraction!, 32, out var plan, out issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                using var cancellation = new CancellationTokenSource();
                var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
                if (mode == "context") grid.CanSplit = true;
                if (mode == "cancelled") cancellation.Cancel();
                Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations,
                    mode == "members" ? allocations.Count + plan!.EntityCount - 1 : 32, out var result, out issue,
                    cancellationToken: cancellation.Token), Is.False);
                var expected = mode switch { "context" => "RoomTerrainContext", "cancelled" => "LayoutInstantiationCancelled", _ => "LayoutInstantiationAllocationBudget" };
                Assert.That(issue?.Code, Is.EqualTo(expected)); Assert.That(result, Is.Null);
                Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(grid.ChunkCount, Is.Zero);
                grid.CanSplit = false;
                Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out result, out issue), Is.True, message: issue?.ToString());
            }, layFloor: false);
        }));
    }

    [Test]
    public async Task PreparedLayoutDefinitionDriftConsumesTheAttemptWithoutWritingAnyTerrainOrMembers()
    {
        var yaml = CopySource().Replace("- proto: Table", "- proto: KsProcgenRoomCopyCapturedFixture");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var system = Pair.Server.System<KsProcgenLayoutInstantiationSystem>();
            Assert.That(system.TryPrepare(project, "Area", source, extraction!, 32, out var plan, out issue), Is.True, message: issue?.Code);
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("KsProcgenRoomCopyCapturedFixture"));
            Assert.That(prototype.TryComp<KsProcgenCapturedDefaultsProbeComponent>(out var defaults, SEntMan.ComponentFactory), Is.True);
            var saved = defaults!.Value;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                defaults.Value++;
                try
                {
                    Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out var result, out issue), Is.False);
                    Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged")); Assert.That(result, Is.Null);
                    Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
                }
                finally { defaults.Value = saved; }
            }, layFloor: false);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out var result, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("PreparedLayoutConsumed")); Assert.That(result, Is.Null);
                Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
        }));
    }

    [Test]
    public async Task FailedNativeLayoutStartupCannotReuseAnyPartOfItsPreparedPlanInAnotherTransaction()
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var system = Pair.Server.System<KsProcgenLayoutInstantiationSystem>();
            Assert.That(system.TryPrepare(project, "Area", source, extraction!, 32, out var plan, out issue,
                budgets: new() { Packing = new() { PreferredPrefabCoveragePercent = 100 } }), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var startup = Pair.Server.System<KsProcgenRoomCopyTestSystem>(); var calls = 0; var first = EntityUid.Invalid;
                startup.StartupAction = uid =>
                {
                    calls++; if (calls == 1) first = uid;
                    if (calls == 4) Pair.Server.System<SharedTransformSystem>().SetLocalPosition(first,
                        SEntMan.GetComponent<TransformComponent>(first).LocalPosition + Vector2.UnitX);
                };
                try
                {
                    Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out var result, out issue), Is.False);
                    Assert.That(issue?.Code, Is.EqualTo("RoomInstantiationMemberDrift")); Assert.That(result, Is.Null); Assert.That(calls, Is.EqualTo(4));
                }
                finally { startup.StartupAction = null; }
            }, layFloor: false);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out var result, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("PreparedLayoutConsumed")); Assert.That(result, Is.Null);
                Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
        }));
    }

    [Test]
    public async Task PreparedLayoutsRejectAnotherSystemBeforeItCanInspectOrWriteAWorld()
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var system = Pair.Server.System<KsProcgenLayoutInstantiationSystem>();
            Assert.That(system.TryPrepare(project, "Area", source, extraction!, 32, out var plan, out issue), Is.True, message: issue?.Code);
            var other = new KsProcgenLayoutInstantiationSystem(); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(other.TryInstantiatePrepared(plan!, EntityUid.Invalid, new(), 32, out var result, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("PreparedLayoutOwnerMismatch")); Assert.That(result, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            WithRoomCopyTransaction((gridUid, allocations) =>
                Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out _, out issue), Is.True, message: issue?.Code), layFloor: false);
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task PreparedLayoutsRejectQueuedGridAndMapDeletionBeforeWritingOrConsuming(bool queueMap)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var system = Pair.Server.System<KsProcgenLayoutInstantiationSystem>();
            Assert.That(system.TryPrepare(project, "Area", source, extraction!, 32, out var plan, out issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                SEntMan.QueueDeleteEntity(queueMap ? SEntMan.GetComponent<TransformComponent>(gridUid).MapUid!.Value : gridUid);
                Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out var result, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("RoomTerrainContext")); Assert.That(result, Is.Null);
                Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
            WithRoomCopyTransaction((gridUid, allocations) =>
                Assert.That(system.TryInstantiatePrepared(plan!, gridUid, allocations, 32, out _, out issue), Is.True, message: issue?.Code), layFloor: false);
        }));
    }

    [TestCase("members", "LayoutInstantiationAllocationBudget")]
    [TestCase("images", "LayoutInstantiationImageBudget")]
    [TestCase("cancelled", "LayoutInstantiationCancelled")]
    public async Task SourceLayoutPreparationFailuresAllocateNoMapGridOrMembers(string mode, string expected)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var before = SEntMan.GetEntities().ToHashSet(); using var cancellation = new CancellationTokenSource();
            if (mode == "cancelled") cancellation.Cancel();
            Assert.That(Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryPrepare(project, "Area", source, extraction!,
                mode == "members" ? 0 : 32, out var plan, out issue,
                budgets: new() { MaximumImageCharacters = mode == "images" ? 0 : 4_194_304 }, cancellationToken: cancellation.Token), Is.False);
            Assert.That(plan, Is.Null); Assert.That(issue?.Code, Is.EqualTo(expected));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            Assert.That(Pair.Server.System<KsProcgenPreviewOperationSystem>().Active, Is.False);
        }));
    }
}
