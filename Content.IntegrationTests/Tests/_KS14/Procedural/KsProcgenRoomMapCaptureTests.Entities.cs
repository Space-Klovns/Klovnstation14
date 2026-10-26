#nullable enable
using System;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Threading;
using Content.Server._KS14.Procedural;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    private const string EntityBranch = "- proto: Table\n  entities:\n  - uid: 10\n    components:\n    - type: Transform\n      parent: 1\n      pos: 1.25,0.25\n      rot: 90\n      anchored: true\n      noRot: false\n- proto: Paper\n  entities:\n  - uid: 2\n    components:\n    - type: Transform\n      parent: 10\n      pos: 0.25,0\n      rot: 45\n";

    [Test]
    public async Task RealRepositoryRoomEntitiesExtractWithoutAllocatingAMapOrRunningInitialization()
    {
        await AssertOnServer(() =>
        {
            var path = "/Maps/Test/empty.yml";
            using var input = Pair.Server.ResolveDependency<IResourceManager>().ContentFileRead(path);
            using var output = new MemoryStream(); input.CopyTo(output); var bytes = output.ToArray();
            var system = Pair.Server.System<KsProcgenRoomMapCaptureSystem>(); var project = Project(Spec(bytes));
            var bindings = new System.Collections.Generic.Dictionary<string, string> { ["test-room"] = path };
            var first = Capture(system, project, bindings);
            var nonempty = first.Definitions.Bodies.Where(body => body.Kind == KsProcgenPrototypeBodyKind.Tile && body.RuntimeTileId != 0)
                .Select(body => body.PrototypeId).ToHashSet(StringComparer.Ordinal);
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = first.Maps.Single().Grids.Single().Tiles
                .Where(tile => nonempty.Contains(tile.PrototypeId)).Select(tile => tile.Cell).ToList();
            project = Project(spec); var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.True, message: issue?.Code);
            Assert.That(result!.Rooms.Single().Entities.Count, Is.EqualTo(2));
            Assert.That(result.Rooms.Single().Entities.All(entity => entity.GridPosition == new Vector2(-0.5f, -0.5f)), Is.True);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
    public async Task EntityExtractionComposesGridLocalPosesAndOrdersParentsBeforeChildren(bool sourceWorldPose, bool childFirst)
    {
        var yaml = GridYaml();
        if (sourceWorldPose)
        {
            yaml = yaml.Replace("    - type: MapGrid", "    - type: Transform\n      parent: 50\n      pos: 100,200\n      rot: 35\n    - type: MapGrid");
            yaml += "- proto: ''\n  entities:\n  - uid: 50\n    components:\n    - type: Transform\n      pos: 9000,10000\n      rot: 25\n";
        }
        var split = EntityBranch.IndexOf("- proto: Paper", StringComparison.Ordinal);
        yaml += childFirst ? EntityBranch[split..] + EntityBranch[..split] : EntityBranch;
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); root.Available = false;
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.True, message: issue?.Code);
            var room = result!.Rooms.Single(); Assert.That(room.Entities.Select(entity => entity.YamlUid), Is.EqualTo(new[] { 10, 2 }));
            Assert.That(room.Room.ResourcePath, Is.EqualTo(source.Bindings.Single().ResourcePath));
            var table = room.Entities[0]; var child = room.Entities[1];
            Assert.That(table.LocalPosition, Is.EqualTo(new Vector2(1.25f, 0.25f))); Assert.That(table.Anchored, Is.True);
            Assert.That(child.ParentYamlUid, Is.EqualTo(10)); Assert.That(child.PrototypeId, Is.EqualTo("Paper"));
            Assert.That(child.GridPosition.X, Is.EqualTo(1.25f).Within(0.00001f)); Assert.That(child.GridPosition.Y, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(child.GridRotationRadians, Is.EqualTo(Angle.FromDegrees(135.0).Theta).Within(0.000001));
            Assert.That(child.LocalRotationRadians, Is.EqualTo(Angle.FromDegrees(45.0).Theta).Within(0.000001));
            Assert.That(result.SourceContentHash, Is.EqualTo(source.ContentHash)); Assert.That(result.GraphWork, Is.GreaterThan(0));
            Assert.That(result.ComponentReferencesVerified, Is.False); Assert.That(result.CopyReady, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task EntityExtractionMergesPrototypeDefaultsAndHonorsExplicitOverrides()
    {
        var branch = "- proto: Table\n  entities:\n  - uid: 10\n    components:\n    - type: Transform\n      parent: 1\n";
        await AssertOnServer(() => WithSource(GridYaml().Replace("0: Space", "0: FloorSteel") + branch, (system, project, bindings, root) =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(manager.Index(new EntProtoId("Table")).TryComp<TransformComponent>(out var defaults, SEntMan.ComponentFactory), Is.True);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.True, message: issue?.Code);
            var entity = result!.Rooms.Single().Entities.Single();
            Assert.That(entity.LocalPosition, Is.EqualTo(defaults!.LocalPosition)); Assert.That(entity.LocalRotationRadians, Is.EqualTo(defaults.LocalRotation.Theta));
            Assert.That(entity.Anchored, Is.EqualTo(defaults.Anchored)); Assert.That(entity.NoLocalRotation, Is.EqualTo(defaults.NoLocalRotation));
            Assert.That(entity.OverrideComponentTypes, Is.EqualTo(new[] { "Transform" }));
        }));
    }

    [TestCase("missing", "MissingRoomEntityParent")]
    [TestCase("cycle", "CyclicRoomEntityParents")]
    [TestCase("self", "CyclicRoomEntityParents")]
    [TestCase("parent", "InvalidRoomEntityParent")]
    [TestCase("nan", "InvalidRoomEntityTransform")]
    [TestCase("norot", "InvalidRoomEntityTransform")]
    [TestCase("remove-core", "UnsupportedRoomEntityCoreRemoval")]
    [TestCase("remove-conflict", "InvalidRoomEntityComponentRemoval")]
    [TestCase("parent-inside", "RoomMapExtractionCutsHierarchy")]
    [TestCase("child-inside", "RoomMapExtractionCutsHierarchy")]
    public async Task InvalidEntityHierarchiesAndPartialBranchesNeverYieldAnExtraction(string kind, string code)
    {
        var branch = kind switch
        {
            "missing" => EntityBranch.Replace("parent: 10", "parent: 99"),
            "cycle" => EntityBranch.Replace("parent: 1\n", "parent: 2\n"),
            "self" => EntityBranch.Replace("parent: 1\n", "parent: 10\n"),
            "parent" => EntityBranch.Replace("parent: 10", "parent: nonsense"),
            "nan" => EntityBranch.Replace("pos: 0.25,0", "pos: NaN,0"),
            "norot" => EntityBranch.Replace("noRot: false", "noRot: true"),
            "remove-core" => EntityBranch.Replace("  - uid: 10\n", "  - uid: 10\n    missingComponents: [MetaData]\n"),
            "remove-conflict" => EntityBranch.Replace("  - uid: 10\n", "  - uid: 10\n    missingComponents: [Transform]\n"),
            "parent-inside" => EntityBranch.Replace("pos: 0.25,0", "pos: 2,0"),
            _ => EntityBranch.Replace("pos: 1.25,0.25", "pos: 0.25,0.25").Replace("rot: 90", "rot: 0").Replace("pos: 0.25,0\n", "pos: 1,0\n"),
        };
        await AssertOnServer(() => WithSource(GridYaml() + branch, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code)); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase("work", "RoomEntityExtractionGraphBudget")]
    [TestCase("depth", "RoomEntityExtractionDepthBudget")]
    [TestCase("output", "RoomEntityExtractionOutputBudget")]
    [TestCase("masks", "RoomMapMaskProbeBudget")]
    public async Task EntityExtractionUsesAtomicWorkDepthAndOutputAllowances(string kind, string code)
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var limits = new KsProcgenRoomEntityExtractionLimits();
            limits = kind switch { "work" => limits with { MaximumGraphWork = 0 }, "depth" => limits with { MaximumParentDepth = 2 },
                "output" => limits with { MaximumExtractedEntities = 1 }, _ => limits with { MaximumMaskCellProbes = 0 } };
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue, limits: limits), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
            Assert.That(system.TryExtractRoomEntities(project, source, out result, out issue), Is.True, message: issue?.Code);
        }));
    }

    [Test]
    public async Task ExtractionFloorsNegativeCoordinatesAndParsesRadians()
    {
        var branch = "- proto: Paper\n  entities:\n  - uid: 2\n    components:\n    - type: Transform\n      parent: 1\n      pos: -0.1,0.5\n      rot: 1.25 rad\n";
        await AssertOnServer(() => WithSource(GridYaml(chunkIndex: "-1,0") + branch, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(-1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.True, message: issue?.Code);
            Assert.That(result!.Rooms.Single().Entities.Count, Is.EqualTo(1));
            var entity = result.Rooms.Single().Entities.Single(); Assert.That(entity.GridPosition, Is.EqualTo(new Vector2(-0.1f, 0.5f)));
            Assert.That(entity.GridRotationRadians, Is.EqualTo(1.25).Within(0.000001));
        }));
    }

    [Test]
    public async Task ExtractionRejectsStaleDefinitionsAndCancellationAndSharesOutputAcrossRooms()
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) };
            spec.Layouts.Rooms["Other"] = new() { SourceKey = "test-room", ContentFingerprint = Fingerprint(root.Bytes), Cells = new() { new(1, 0) } };
            project = Project(spec); var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue,
                limits: new() { MaximumExtractedEntities = 3 }), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionOutputBudget"));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.That(system.TryExtractRoomEntities(project, source, out result, out issue, cancellationToken: cancellation.Token), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionCancelled"));
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("Table")); var save = prototype.MapSavable;
            try
            {
                prototype.MapSavable = !save;
                Assert.That(system.TryExtractRoomEntities(project, source, out result, out issue), Is.False);
                Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { prototype.MapSavable = save; }
            Assert.That(system.TryExtractRoomEntities(project, source, out result, out issue), Is.True, message: issue?.Code);
            Assert.That(result!.Rooms.Select(room => room.Entities.Count), Is.EqualTo(new[] { 2, 2 }));
        }));
    }

    [Test]
    public async Task EntityExtractionRejectsNestedSourceGrids()
    {
        var yaml = GridYaml(grids: 2).Replace("  - uid: 2\n    components:\n    - type: MapGrid\n",
            "  - uid: 2\n    components:\n    - type: Transform\n      parent: 1\n    - type: MapGrid\n      tileSize: 2\n");
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("UnsupportedNestedRoomEntityGrid"));
        }));
    }

    [Test]
    public async Task WholeOutsideBranchesMayBeExcludedAndEmptyOutputIsValid()
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch.Replace("pos: 1.25,0.25", "pos: 1.25,1.25"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue,
                limits: new() { MaximumExtractedEntities = 0 }), Is.True, message: issue?.Code);
            Assert.That(result!.Rooms.Single().Entities, Is.Empty);
        }));
    }

    [Test]
    public async Task SourceEntityCompositionRejectsOutOfRangeCoordinates()
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch.Replace("pos: 1.25,0.25", "pos: 2000000,0.25"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionCoordinateRange"));
        }));
    }

    [Test]
    public async Task KnownRemovedComponentsRemainLocatedAndStaleProjectMasksAreRejected()
    {
        var yaml = GridYaml() + EntityBranch.Replace("  - uid: 10\n", "  - uid: 10\n    missingComponents: [Sprite]\n");
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.True, message: issue?.Code);
            Assert.That(result!.Rooms.Single().Entities[0].RemovedComponentTypes, Is.EqualTo(new[] { "Sprite" }));
            spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells[0] = new(1, 1);
            Assert.That(system.TryExtractRoomEntities(Project(spec), source, out result, out issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomMapMaskProjectMismatch"));
        }));
    }

    [Test]
    public async Task IntentionallyIgnoredClientComponentsRemainInFrozenSourceLocators()
    {
        var branch = EntityBranch.Replace("    - type: Transform\n", "    - type: Sprite\n      sprite: Objects/Misc/paper.rsi\n    - type: Transform\n");
        await AssertOnServer(() => WithSource(GridYaml() + branch, (system, project, bindings, root) =>
        {
            Assert.That(SEntMan.ComponentFactory.IsIgnored("Sprite"), Is.True);
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(source.Maps.Single().OverrideComponentTypes, Does.Contain("Sprite"));
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.True, message: issue?.Code);
            Assert.That(result!.Rooms.Single().Entities.All(entity => entity.OverrideComponentTypes.Contains("Sprite")), Is.True);
            Assert.That(source.Maps.Single().CopyBytes(), Is.EqualTo(root.Bytes));
            Assert.That(result.ComponentReferencesVerified, Is.False);
        }));
    }
}
