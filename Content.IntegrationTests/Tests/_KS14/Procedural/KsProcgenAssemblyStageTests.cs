using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Placeable;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenAssemblyStageSystem))]
public sealed class KsProcgenAssemblyStageTests : GameTest
{
    private KsProcgenResolvedAssembly Compile(KsProcgenAssemblyVariant definition)
    {
        var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("StageFixture", definition,
            new Dictionary<string, string>(), id => manager.TryIndex<EntityPrototype>(id, out _),
            out var assembly, out var issue), Is.True, issue?.Message);
        return assembly!;
    }

    private KsProcgenResolvedAssembly SurfaceAssembly(string subjectPrototype = "CoordinatesDisk",
        string surfacePrototype = "KsProcgenTrackedSurfaceFixture") => Compile(new()
    {
        Id = "Base", AnchorMember = "Root",
        Members = [new() { Id = "Root", Entity = surfacePrototype, RotationMode = KsProcgenMemberRotation.Independent },
            new() { Id = "Subject", Entity = subjectPrototype, RotationMode = KsProcgenMemberRotation.Independent }],
        Relations = [new() { Id = "Top", Subject = "Subject", Target = "Root", Kind = KsProcgenRelationKind.OnSurface }],
    });

    private KsProcgenAssemblyStageResult Stage(KsProcgenResolvedAssembly assembly,
        int rootQuarterTurns = 0, int subjectQuarterTurns = 0, int maxSpawnedEntities = 256,
        CancellationToken cancellationToken = default) => Pair.Server.System<KsProcgenAssemblyStageSystem>().TryStage(
        assembly, assembly.Members.Select(member => member.Id).ToArray(), [new("Root", new(2f, 3f))],
        assembly.Members.Select(member => new KsProcgenMemberOrientation(member.Id,
            member.Id == "Root" ? rootQuarterTurns : subjectQuarterTurns)).ToArray(), 0,
        cancellationToken: cancellationToken, maxSpawnedEntities: maxSpawnedEntities);

    [Test]
    public async Task CompleteAssemblyStagesNestedSupportAndIndependentRoots()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var containerSystem = Pair.Server.System<SharedContainerSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var assembly = Compile(new()
                {
                    Id = "Base", AnchorMember = "Root",
                    Members = [new() { Id = "Root", Entity = "Table", RotationMode = KsProcgenMemberRotation.Independent },
                        new() { Id = "Device", Entity = "KsProcgenRotatableMicrowaveFixture", RotationMode = KsProcgenMemberRotation.Independent },
                        new() { Id = "Stored", Entity = "Paper", RotationMode = KsProcgenMemberRotation.Independent },
                        new() { Id = "Host", Entity = "ComputerShuttle", RotationMode = KsProcgenMemberRotation.Independent },
                        new() { Id = "Disk", Entity = "CoordinatesDisk", RotationMode = KsProcgenMemberRotation.Independent }],
                    Relations = [new() { Id = "Top", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.OnSurface },
                        new() { Id = "Inside", Subject = "Stored", Target = "Device", Kind = KsProcgenRelationKind.InContainer,
                            ContainerId = "microwave_entity_container" },
                        new() { Id = "Slot", Subject = "Disk", Target = "Host", Kind = KsProcgenRelationKind.InContainer, ContainerId = "disk_slot" }],
                });
                var staged = system.TryStage(assembly, ["Root", "Device", "Stored", "Host", "Disk"],
                    [new("Root", new(2f, 3f)), new("Host", new(12f, 3f))],
                    [new("Root", 1), new("Device", 2), new("Stored", 2), new("Host", 0), new("Disk", 0)], 0);
                Assert.That(staged.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), staged.Issue?.Code);
                var stage = staged.Stage!;
                Assert.That(stage.Members.Count, Is.EqualTo(5));
                Assert.That(system.Verify(stage), Is.True);
                Assert.That(stage.PlacementVerified, Is.False);
                foreach (var pose in stage.Poses.Members)
                {
                    var actual = SEntMan.GetComponent<TransformComponent>(stage.Members[pose.Support.MemberId]);
                    Assert.That(actual.LocalPosition, Is.EqualTo(pose.LocalPosition));
                    Assert.That(actual.LocalRotation, Is.EqualTo(Angle.FromDegrees((double) pose.LocalQuarterTurns * 90.0)));
                }
                Assert.That(containerSystem.TryGetContainer(stage.Members["Device"], "microwave_entity_container", out var contents), Is.True);
                Assert.That(contents.Contains(stage.Members["Stored"]), Is.True);
                Assert.That(containerSystem.TryGetContainer(stage.Members["Host"], "disk_slot", out var slot), Is.True);
                Assert.That(slot.Contains(stage.Members["Disk"]), Is.True);
                var allocations = stage.SpawnedEntityCount;
                Assert.That(system.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                Assert.That(stage.SpawnedEntityCount, Is.GreaterThan(allocations), "Real microwave initialization creates its board.");
                Assert.That(mapSystem.IsPaused(stage.MapId), Is.True);
                Assert.That(system.Verify(stage), Is.True);
                var initializedAllocations = stage.SpawnedEntityCount;
                Assert.That(system.TryInitialize(stage).Stage, Is.SameAs(stage));
                Assert.That(stage.SpawnedEntityCount, Is.EqualTo(initializedAllocations));
                Assert.That(system.Discard(stage), Is.True);
                Assert.That(system.TryInitialize(stage).Issue?.Code, Is.EqualTo("AssemblyStageNotOwned"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                system.DiscardAll();
            }
        });
    }

    [Test]
    public async Task SupportedMachineAccessIsCheckedBeforeAllocationAndRetainedAsGeometryOnly()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var assembly = Compile(new()
            {
                Id = "Base", AnchorMember = "Root",
                Members = [new() { Id = "Root", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired },
                    new() { Id = "Device", Entity = "KsProcgenRotatableMicrowaveFixture", RequiresInteractionApproach = true,
                        RotationMode = KsProcgenMemberRotation.Independent },
                    new() { Id = "Stored", Entity = "Paper", RotationMode = KsProcgenMemberRotation.Independent }],
                Relations = [new() { Id = "Top", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.OnSurface },
                    new() { Id = "Inside", Subject = "Stored", Target = "Device", Kind = KsProcgenRelationKind.InContainer,
                        ContainerId = "microwave_entity_container" }],
            });
            var room = new KsProcgenRoomAccessMask(
                Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3).Select(y => new Vector2i(x, y))).ToHashSet(),
                new HashSet<Vector2i> { new(3, 2) }, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
                new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);
            KsProcgenAssemblyStageResult Try(int turn, KsProcgenRoomAccessMask mask, int budget) => system.TryStage(
                assembly, ["Root", "Device", "Stored"], [new("Root", new(2f, 2f))],
                [new("Root", 0), new("Device", turn), new("Stored", turn)], 0,
                accessMask: mask, maximumAccessExpandedCells: budget);
            try
            {
                var wrongFacing = Try(3, room, 4096);
                Assert.That(wrongFacing.Issue?.Code, Is.EqualTo("SupportedCleanApproachUnavailable"));
                Assert.That(wrongFacing.Stage, Is.Null);
                var blocked = Try(1, room with { Blocking = new HashSet<Vector2i> { new(1, 2) } }, 4096);
                Assert.That(blocked.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.Rejected));
                Assert.That(Try(1, room, 0).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.BudgetExceeded));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before), "No preview is allocated for rejected access geometry.");
                var staged = Try(1, room, 4096);
                Assert.That(staged.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), staged.Issue?.Code);
                var stage = staged.Stage!;
                var access = stage.Access!;
                Assert.That(access.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
                Assert.That(access.Poses, Is.SameAs(stage.Poses));
                Assert.That(access.Access.Single().Approach, Is.EqualTo(new Vector2i(1, 2)));
                Assert.That(access.Access.Single().CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 2)));
                Assert.That(access.Footprints.Any(footprint => footprint.MemberId == "Stored"), Is.False);
                Assert.That(system.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                Assert.That(system.Verify(stage), Is.True);
                Assert.That(stage.Access, Is.SameAs(access));
                Assert.That(access.EnginePlacementVerified, Is.False);
                Assert.That(system.Discard(stage), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                system.DiscardAll();
            }
        });
    }

    [Test]
    public async Task LandingProbeBudgetIsEnforcedByBothPreviewEntryPointsBeforeAllocation()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var assembly = SurfaceAssembly(subjectPrototype: "KsProcgenRotatableMicrowaveFixture", surfacePrototype: "Table");
            assembly.Members.Single(member => member.Id == "Subject").Entry.RequiresInteractionApproach = true;
            var room = new KsProcgenRoomAccessMask(
                Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3).Select(y => new Vector2i(x, y))).ToHashSet(),
                new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryStage(assembly, ["Root", "Subject"], [new("Root", new(1f, 1f))],
                [new("Root", 0), new("Subject", 0)], 0, accessMask: room, maximumLandingProbes: 0).Issue?.Code,
                Is.EqualTo("SupportedLandingProbeBudget"));
            Assert.That(system.TrySearchStage(assembly, ["Root", "Subject"], room, 2,
                maximumLandingProbes: 0).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.BudgetExceeded));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            var accepted = system.TryStage(assembly, ["Root", "Subject"], [new("Root", new(1f, 1f))],
                [new("Root", 0), new("Subject", 0)], 0, accessMask: room, maximumLandingProbes: 1);
            try
            {
                Assert.That(accepted.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), accepted.Issue?.Code);
                Assert.That(accepted.Stage!.Access!.LandingProbes, Is.EqualTo(1));
            }
            finally
            {
                if (accepted.Stage != null)
                    system.Discard(accepted.Stage);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SupportedNearUsesLoadedGeometryAndSharedBudgetWithoutPublishing()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var assembly = Compile(new()
            {
                Id = "NearDesk", AnchorMember = "Root",
                Members = [new() { Id = "Root", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired,
                        RotationMode = KsProcgenMemberRotation.Independent },
                    new() { Id = "Device", Entity = "KsProcgenRotatableMicrowaveFixture", RequiresInteractionApproach = true,
                        RotationMode = KsProcgenMemberRotation.Independent },
                    new() { Id = "Target", Entity = "Paper", Movement = KsProcgenMovementClass.Clear,
                        RotationMode = KsProcgenMemberRotation.Independent }],
                Relations = [new() { Id = "Top", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.OnSurface },
                    new() { Id = "Near", Subject = "Device", Target = "Target", Kind = KsProcgenRelationKind.Near,
                        MinimumDistance = 4, MaximumDistance = 4 }],
            });
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = Pair.Server.ResolveDependency<IComponentFactory>();
            Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(manager, factory, assembly,
                out var capabilities, out var issue), Is.True, issue?.Code);
            var room = new KsProcgenRoomAccessMask(
                Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
                new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(),
                new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);
            var roots = new KsProcgenFloorRootPose[] { new("Root", new(2f, 2f)), new("Target", new(3f, 2f)) };
            var turns = new KsProcgenMemberOrientation[] { new("Root", 0), new("Device", 1), new("Target", 0) };
            var before = SEntMan.GetEntities().ToHashSet();
            var accessOnly = KsProcgenSupportedAccessPlanner.Plan(assembly with
                { Relations = assembly.Relations.Where(relation => relation.Id != "Near").ToArray() },
                ["Root", "Device", "Target"], capabilities!, roots, turns, 0, room);
            Assert.That(accessOnly.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
            var exhausted = KsProcgenSupportedAccessPlanner.Plan(assembly, ["Root", "Device", "Target"], capabilities!, roots, turns,
                0, room, maximumExpandedCells: accessOnly.ExpandedCells);
            Assert.That(exhausted.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.BudgetExceeded));
            Assert.That(exhausted.Relations, Is.Empty);
            var accepted = KsProcgenSupportedAccessPlanner.Plan(assembly, ["Root", "Device", "Target"], capabilities!, roots, turns, 0, room);
            Assert.That(accepted.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), accepted.Issue?.Code);
            Assert.That(accepted.Relations.Single().PathDistance, Is.EqualTo(4));
            Assert.That(accepted.Relations.Single().CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 2)));
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var gate = system.TryStage(assembly, ["Root", "Device", "Target"], roots, turns, 0, accessMask: room);
            try
            {
                Assert.That(gate.Issue?.Code, Is.EqualTo("UnsupportedAssemblyStageSpatialRelations"));
                Assert.That(gate.Stage, Is.Null);
            }
            finally
            {
                if (gate.Stage != null)
                    system.Discard(gate.Stage);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SupportedSpatialSearchUsesLoadedDeclarationsWithoutBypassingEngineGate()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var assembly = Compile(new()
            {
                Id = "CornerDesk", AnchorMember = "Root",
                Members = [new() { Id = "Root", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired,
                        RotationMode = KsProcgenMemberRotation.Independent },
                    new() { Id = "Device", Entity = "KsProcgenRotatableMicrowaveFixture", RequiresInteractionApproach = true,
                        RotationMode = KsProcgenMemberRotation.Independent },
                    new() { Id = "Seat", Entity = "ChairOfficeLight", Role = KsProcgenEntityRole.Seat,
                        Movement = KsProcgenMovementClass.Clear, RotationMode = KsProcgenMemberRotation.Independent }],
                Relations = [new() { Id = "Top", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.OnSurface },
                    new() { Id = "Corner", Subject = "Root", Kind = KsProcgenRelationKind.AtCorner },
                    new() { Id = "Adjacent", Subject = "Seat", Target = "Root", Kind = KsProcgenRelationKind.AdjacentTo },
                    new() { Id = "Facing", Subject = "Seat", Target = "Root", Kind = KsProcgenRelationKind.FacingTarget },
                    new() { Id = "Use", Subject = "Device", Target = "Seat", Kind = KsProcgenRelationKind.UsesSeat }],
            });
            var room = new KsProcgenRoomAccessMask(
                Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
                new HashSet<Vector2i> { new(3, 2), new(2, 3) }, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
                new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = Pair.Server.ResolveDependency<IComponentFactory>();
            Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(manager, factory, assembly,
                out var capabilities, out var issue), Is.True, issue?.Code);
            var result = KsProcgenSupportedAssemblySearch.Search(assembly, ["Root", "Device", "Seat"], capabilities!, room, 21,
                fixedFloorRoots: [new("Root", new(2f, 2f)), new("Seat", new(2f, 1f))]);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            Assert.That(result.Access!.Relations.Count, Is.EqualTo(4));
            Assert.That(result.Access.Relations.All(witness => witness.State == KsProcgenConstraintState.Satisfied), Is.True);
            Assert.That(result.Orientations.Single(turn => turn.MemberId == "Seat").QuarterTurns, Is.EqualTo(2));
            Assert.That(result.Access.EnginePlacementVerified, Is.False);
            var gate = system.TrySearchStage(assembly, ["Root", "Device", "Seat"], room, 21,
                fixedFloorRoots: [new("Root", new(2f, 2f)), new("Seat", new(2f, 1f))]);
            try
            {
                Assert.That(gate.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.UnsupportedContent));
                Assert.That(gate.Issue?.Code, Is.EqualTo("UnsupportedAssemblyStageSpatialRelations"));
                Assert.That(gate.Stage, Is.Null);
            }
            finally
            {
                if (gate.Stage != null)
                    system.Discard(gate.Stage);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            var wrongFacing = KsProcgenSupportedAccessPlanner.Plan(assembly, ["Root", "Device", "Seat"], capabilities!,
                result.FloorRoots, result.Orientations.Select(turn => turn.MemberId == "Seat" ? turn with { QuarterTurns = 0 } : turn).ToArray(),
                result.AssemblyQuarterTurns, room);
            Assert.That(wrongFacing.Issue?.Code, Is.EqualTo("SupportedSpatialRelationUnmet"));
            Assert.That(wrongFacing.Relations, Is.Empty);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task AutomaticSupportedSearchStagesChosenPoseAndCleansUpEngineRejection()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var assembly = SurfaceAssembly(subjectPrototype: "KsProcgenRotatableMicrowaveFixture", surfacePrototype: "Table");
            assembly.Members.Single(member => member.Id == "Root").Entry.Movement = KsProcgenMovementClass.VaultRequired;
            assembly.Members.Single(member => member.Id == "Subject").Entry.RequiresInteractionApproach = true;
            var room = new KsProcgenRoomAccessMask(
                new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0) }, new HashSet<Vector2i> { new(3, 0) },
                new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);
            try
            {
                Assert.That(system.TrySearchStage(assembly, ["Root", "Subject"], room, 15,
                    maximumPlacementProbes: 0).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.BudgetExceeded));
                Assert.That(system.TrySearchStage(assembly, ["Root", "Subject"], room, 15,
                    maximumAccessExpandedCells: 0).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.BudgetExceeded));
                Assert.That(system.TrySearchStage(assembly, ["Root", "Subject"], room, 15,
                    fixedFloorRoots: [new("Root", new(0.5f, 0f))]).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InvalidInput));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                var result = system.TrySearchStage(assembly, ["Root", "Subject"], room, 15,
                    fixedFloorRoots: [new("Root", new(2f, 0f))]);
                Assert.That(result.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), result.Issue?.Code);
                var stage = result.Stage!;
                Assert.That(stage.Poses.Members.Single(pose => pose.Support.MemberId == "Subject").QuarterTurns, Is.EqualTo(1));
                Assert.That(stage.Access!.Access.Single().Approach, Is.EqualTo(new Vector2i(1, 0)));
                Assert.That(stage.Access.Poses, Is.SameAs(stage.Poses));
                Assert.That(system.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                Assert.That(system.Verify(stage), Is.True);
                var retainedEntities = SEntMan.GetEntities().ToHashSet();
                var locked = SurfaceAssembly(subjectPrototype: "KitchenMicrowave", surfacePrototype: "Table");
                locked.Members.Single(member => member.Id == "Subject").Entry.RequiresInteractionApproach = true;
                var rejected = system.TrySearchStage(locked, ["Root", "Subject"], room, 15,
                    fixedFloorRoots: [new("Root", new(2f, 0f))]);
                Assert.That(rejected.Issue?.Code, Is.EqualTo("AssemblyStageRotationUnsupported"));
                Assert.That(rejected.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(retainedEntities));
                Assert.That(system.Verify(stage), Is.True);
                Assert.That(stage.PlacementVerified, Is.False);
                Assert.That(system.Discard(stage), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                system.DiscardAll();
            }
        });
    }

    [Test]
    public async Task UnsupportedRelationsAndRotationLocksCannotRetainAPreview()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var locked = SurfaceAssembly(subjectPrototype: "KitchenMicrowave", surfacePrototype: "Table");
                var rejected = Stage(locked, subjectQuarterTurns: 1);
                Assert.That(rejected.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.Rejected));
                Assert.That(rejected.Issue?.Code, Is.EqualTo("AssemblyStageRotationUnsupported"));
                Assert.That(rejected.Stage, Is.Null);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                var spatial = locked with { Relations = locked.Relations.Append(new("Adjacent", "Subject",
                    KsProcgenRelationKind.AdjacentTo, "Root", KsProcgenRelationSeverity.Required, 1, 1,
                    null, null, KsProcgenApproachPolicy.EmptyFloor)).ToArray() };
                Assert.That(Stage(spatial).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.UnsupportedContent));
                var invalid = system.TryStage(locked, ["Root"], [new("Root", Vector2.Zero)], [new("Root", 0)], 0);
                Assert.That(invalid.Issue?.Code, Is.EqualTo("InvalidSupportSelection"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                system.DiscardAll();
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangedPosesAndLiveSupportsRejectBeforeOrDuringInitialization(bool duringInitialization)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var listener = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var transformSystem = Pair.Server.System<SharedTransformSystem>();
            var surfaceSystem = Pair.Server.System<PlaceableSurfaceSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                foreach (var fault in new[] { "RootPosition", "RootRotation", "SubjectRotation", "Offset", "Disabled", "Filter", "Queued" })
                {
                    listener.Reset();
                    var assembly = SurfaceAssembly(subjectPrototype: "KsProcgenSurfaceTaggedDiskFixture",
                        surfacePrototype: "KsProcgenTaggedSurfaceFixture");
                    var staged = Stage(assembly, rootQuarterTurns: 1);
                    Assert.That(staged.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), staged.Issue?.Code);
                    var stage = staged.Stage!;
                    var rootUid = stage.Members["Root"];
                    var subjectUid = stage.Members["Subject"];
                    void ApplyFault()
                    {
                        switch (fault)
                        {
                            case "RootPosition": transformSystem.SetCoordinates(rootUid, new EntityCoordinates(stage.MapUid, 9f, 3f)); break;
                            case "RootRotation": transformSystem.SetLocalRotation(rootUid, Angle.Zero); break;
                            case "SubjectRotation": transformSystem.SetLocalRotation(subjectUid, Angle.FromDegrees(90.0)); break;
                            case "Offset": surfaceSystem.SetPositionOffset(rootUid, new(0.5f, 0f)); break;
                            case "Disabled": surfaceSystem.SetPlaceable(rootUid, false); break;
                            case "Filter":
                                Assert.That(Pair.Server.System<TagSystem>().RemoveTag(subjectUid, "KsProcgenBlockedStorageFixture"), Is.True);
                                break;
                            case "Queued": SEntMan.QueueDeleteEntity(subjectUid); break;
                        }
                    }
                    listener.TargetPrototypeId = "KsProcgenSurfaceTaggedDiskFixture";
                    if (duringInitialization)
                        listener.MapInitAction = ApplyFault;
                    else
                    {
                        ApplyFault();
                        Assert.That(system.Verify(stage), Is.False, fault);
                    }
                    var failed = system.TryInitialize(stage);
                    Assert.That(failed.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.Rejected), fault);
                    Assert.That(failed.Issue?.Code, Is.EqualTo(fault switch
                    {
                        "Offset" => "AssemblyStageDropOffsetDrift",
                        "Disabled" => "LiveSurfaceDisabled",
                        "Filter" => "SurfaceTrackingWhitelistDenied",
                        "Queued" => "AssemblyStageMemberDrift",
                        _ => "AssemblyStagePoseDrift",
                    }), fault);
                    Assert.That(failed.Stage, Is.Null);
                    Assert.That(stage.Active, Is.False);
                    Assert.That(listener.MapInitAttempts, Is.EqualTo(duringInitialization ? 1 : 0));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                }
            }
            finally
            {
                listener.Reset();
                system.DiscardAll();
            }
        });
    }

    [Test]
    public async Task LiveCapacityMustFitAllSelectedSurfaceChildrenTogether()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var listener = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var assembly = Compile(new()
                {
                    Id = "Base", AnchorMember = "Root",
                    Members = [new() { Id = "Root", Entity = "Table", RotationMode = KsProcgenMemberRotation.Independent },
                        new() { Id = "First", Entity = "CoordinatesDisk", RotationMode = KsProcgenMemberRotation.Independent },
                        new() { Id = "Second", Entity = "CoordinatesDisk", RotationMode = KsProcgenMemberRotation.Independent }],
                    Relations = [new() { Id = "Top1", Subject = "First", Target = "Root", Kind = KsProcgenRelationKind.OnSurface },
                        new() { Id = "Top2", Subject = "Second", Target = "Root", Kind = KsProcgenRelationKind.OnSurface }],
                });
                var stage = Stage(assembly).Stage!;
                Assert.That(stage.Poses.SupportPlan!.Reservations.Single().DeclaredCapacity, Is.Null);
                listener.TargetPrototypeId = "CoordinatesDisk";
                listener.MapInitAction = () => SEntMan.EnsureComponent<ItemPlacerComponent>(stage.Members["Root"]);
                var rejected = system.TryInitialize(stage);
                Assert.That(rejected.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.Rejected));
                Assert.That(rejected.Issue?.Code, Is.EqualTo("AssemblyStageSurfaceCapacityConflict"));
                Assert.That(rejected.Stage, Is.Null);
                Assert.That(stage.Active, Is.False);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                listener.Reset();
                system.DiscardAll();
            }
        });
    }

    [Test]
    public async Task InitializationFaultsCleanAllOwnedEntitiesAndPreserveOtherPreviews()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var listener = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var operationSystem = Pair.Server.System<KsProcgenPreviewOperationSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var survivor = Stage(SurfaceAssembly()).Stage!;
                var survivorEntities = SEntMan.GetEntities().ToHashSet();
                foreach (var fault in new[] { "Precancel", "Cancel", "Exception", "Budget", "OffMap", "Replacement" })
                {
                    listener.Reset();
                    using var cancellationSource = new CancellationTokenSource();
                    var prototype = fault == "Replacement" ? "KsProcgenInitializingDiskFixture" : "CoordinatesDisk";
                    var stage = Stage(SurfaceAssembly(subjectPrototype: prototype), maxSpawnedEntities: fault == "Budget" ? 3 : 256).Stage!;
                    listener.TargetPrototypeId = prototype;
                    listener.CancellationSource = cancellationSource;
                    listener.CancelTokenOnMapInit = fault == "Cancel";
                    listener.ThrowOnMapInit = fault == "Exception";
                    listener.SpawnExtraOnMapInit = fault is "Budget" or "OffMap" ? 2 : 0;
                    listener.SpawnExtraOutsideMap = fault == "OffMap";
                    if (fault == "Precancel")
                        cancellationSource.Cancel();
                    var failed = system.TryInitialize(stage, cancellationToken: cancellationSource.Token);
                    Assert.That(failed.Status, Is.EqualTo(fault switch
                    {
                        "Cancel" or "Precancel" => KsProcgenAssemblyStageStatus.Cancelled,
                        "Exception" => KsProcgenAssemblyStageStatus.EngineFailure,
                        "Budget" => KsProcgenAssemblyStageStatus.BudgetExceeded,
                        _ => KsProcgenAssemblyStageStatus.Rejected,
                    }), fault);
                    Assert.That(failed.Stage, Is.Null);
                    Assert.That(stage.Active, Is.False);
                    Assert.That(listener.ExtraSpawnedEntities.All(uid => !SEntMan.EntityExists(uid)), Is.True);
                    Assert.That(system.Verify(survivor), Is.True);
                    Assert.That(operationSystem.Active, Is.False);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(survivorEntities));
                }
                listener.Reset();
                listener.TargetPrototypeId = "CoordinatesDisk";
                listener.MapInitAction = () =>
                {
                    var during = SEntMan.GetEntities().ToHashSet();
                    Assert.That(Stage(SurfaceAssembly()).Issue?.Code, Is.EqualTo("AssemblyStageOperationActive"));
                    Assert.That(system.Discard(survivor), Is.False);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(during));
                };
                Assert.That(system.TryInitialize(survivor).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                Assert.That(operationSystem.Active, Is.False);
            }
            finally
            {
                listener.Reset();
                system.DiscardAll();
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RealInsertionFaultsRollBackAllMembersOfTheAssembly()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var listener = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var assembly = Compile(new()
            {
                Id = "Base", AnchorMember = "Root",
                Members = [new() { Id = "Root", Entity = "Table" }, new() { Id = "Subject", Entity = "CoordinatesDisk" },
                    new() { Id = "Host", Entity = "ComputerShuttle" }, new() { Id = "Stored", Entity = "CoordinatesDisk" }],
                Relations = [new() { Id = "Top", Subject = "Subject", Target = "Root", Kind = KsProcgenRelationKind.OnSurface },
                    new() { Id = "Slot", Subject = "Stored", Target = "Host", Kind = KsProcgenRelationKind.InContainer,
                        ContainerId = "disk_slot" }],
            });
            try
            {
                foreach (var fault in new[] { "ItemSlotVeto", "ContainerVeto", "Exception", "CancelToken" })
                {
                    listener.Reset();
                    using var cancellationSource = new CancellationTokenSource();
                    listener.TargetPrototypeId = "CoordinatesDisk";
                    listener.CancelItemSlotOnAttempt = fault == "ItemSlotVeto" ? 1 : 0;
                    listener.CancelContainerOnAttempt = fault == "ContainerVeto" ? 1 : 0;
                    listener.ThrowContainerOnAttempt = fault == "Exception" ? 1 : 0;
                    listener.CancelTokenOnContainerAttempt = fault == "CancelToken" ? 1 : 0;
                    listener.CancellationSource = cancellationSource;
                    var rejected = system.TryStage(assembly, ["Root", "Subject", "Host", "Stored"],
                        [new("Root", new(2f, 3f)), new("Host", new(8f, 3f))],
                        [new("Root", 0), new("Subject", 0), new("Host", 0), new("Stored", 0)], 0,
                        cancellationToken: cancellationSource.Token);
                    Assert.That(rejected.Status, Is.EqualTo(fault switch
                    {
                        "Exception" => KsProcgenAssemblyStageStatus.EngineFailure,
                        "CancelToken" => KsProcgenAssemblyStageStatus.Cancelled,
                        _ => KsProcgenAssemblyStageStatus.Rejected,
                    }), fault);
                    Assert.That(rejected.Stage, Is.Null);
                    Assert.That(system.RetainedStageCount, Is.Zero);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                }
            }
            finally
            {
                listener.Reset();
                system.DiscardAll();
            }
        });
    }

    [Test]
    public async Task BudgetsStartupFaultsAndCrossAdapterReentrancyCannotLeakStages()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var surfaceSystem = Pair.Server.System<KsProcgenSurfaceStageSystem>();
            var containerSystem = Pair.Server.System<KsProcgenContainerStageSystem>();
            var listener = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var assembly = SurfaceAssembly();
            try
            {
                Assert.That(Stage(assembly, maxSpawnedEntities: 2).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.BudgetExceeded));
                Assert.That(Stage(assembly, maxSpawnedEntities: 257).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InvalidInput));
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                Assert.That(Stage(assembly, cancellationToken: canceled.Token).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.Cancelled));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                foreach (var fault in new[] { "Exception", "OffMap" })
                {
                    listener.Reset();
                    listener.TargetPrototypeId = "CoordinatesDisk";
                    listener.ThrowOnStartup = fault == "Exception";
                    listener.SpawnExtraOnStartup = fault == "OffMap" ? 2 : 0;
                    var failed = Stage(assembly);
                    Assert.That(failed.Status, Is.EqualTo(fault == "Exception" ?
                        KsProcgenAssemblyStageStatus.EngineFailure : KsProcgenAssemblyStageStatus.Rejected));
                    Assert.That(failed.Stage, Is.Null);
                    Assert.That(listener.ExtraSpawnedEntities.All(uid => !SEntMan.EntityExists(uid)), Is.True);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                }
                listener.Reset();
                var surfaceSurvivor = surfaceSystem.TryStage("Table", "Paper").Stage!;
                listener.TargetPrototypeId = "CoordinatesDisk";
                listener.StartupAction = () =>
                {
                    var during = SEntMan.GetEntities().ToHashSet();
                    Assert.That(Stage(assembly).Issue?.Code, Is.EqualTo("AssemblyStageOperationActive"));
                    Assert.That(surfaceSystem.TryStage("Table", "Paper").Issue?.Code, Is.EqualTo("SurfaceStageOperationActive"));
                    Assert.That(containerSystem.TryStage(assembly, ["Root", "Subject"]).Issue?.Code, Is.EqualTo("ContainerStageOperationActive"));
                    Assert.That(system.DiscardAll(), Is.Zero);
                    Assert.That(surfaceSystem.Discard(surfaceSurvivor), Is.False);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(during));
                };
                Assert.That(Stage(assembly).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview));
                var primitive = surfaceSystem.TryStage("KsProcgenTrackedSurfaceFixture", "CoordinatesDisk");
                Assert.That(primitive.Status, Is.EqualTo(KsProcgenSurfaceStageStatus.PositionedPreview));
                Assert.That(surfaceSystem.Discard(primitive.Stage!), Is.True);
                Assert.That(surfaceSystem.Verify(surfaceSurvivor), Is.True);
                listener.Reset();
                system.DiscardAll();
                surfaceSystem.Discard(surfaceSurvivor);
                for (var index = 0; index < KsProcgenAssemblyStageSystem.MaximumRetainedStages; index++)
                    Assert.That(Stage(assembly).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview));
                var beforeOverflow = SEntMan.GetEntities().ToHashSet();
                Assert.That(Stage(assembly).Issue?.Code, Is.EqualTo("AssemblyStageRetentionBudget"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(beforeOverflow));
            }
            finally
            {
                listener.Reset();
                system.DiscardAll();
                surfaceSystem.DiscardAll();
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }
}
