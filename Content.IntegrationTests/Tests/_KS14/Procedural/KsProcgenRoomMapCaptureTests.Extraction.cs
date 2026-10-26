#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    [TestCase(1)] [TestCase(2)]
    public async Task ExplicitGridSelectionResolvesAmbiguousAtlasesAndScopesEntityMembership(int gridUid)
    {
        var yaml = GridYaml(grids: 2) + EntityBranch.Replace("uid: 2\n", "uid: 12\n") + EntityBranch.Replace("uid: 10", "uid: 20")
            .Replace("uid: 2\n", "uid: 30\n").Replace("parent: 10", "parent: 20").Replace("parent: 1\n", "parent: 2\n");
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); var room = spec.Layouts!.Rooms["Room"];
            room.Cells = new() { new(0, 0) }; room.SourceOrigin = new(1, 0); room.SourceGridUid = gridUid;
            project = Project(spec); var source = Capture(system, project, bindings); root.Available = false;
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryReconcileRoomMasks(project, source, out var masks, out var issue, maximumCellProbes: 5), Is.True, message: issue?.Code);
            var mask = masks!.Rooms.Single(); Assert.That(mask.GridYamlUid, Is.EqualTo(gridUid));
            Assert.That(mask.SourceOrigin, Is.EqualTo(new Vector2i(1, 0))); Assert.That(mask.Cells, Is.EquivalentTo(new[] { new Vector2i(1, 0) }));
            Assert.That(masks.CellProbes, Is.EqualTo(5));
            Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out issue), Is.True, message: issue?.Code);
            var members = extraction!.Rooms.Single().Entities;
            Assert.That(members.Select(entity => entity.YamlUid), Is.EqualTo(gridUid == 1 ? new[] { 10, 12 } : new[] { 20, 30 }));
            Assert.That(members[0].RoomPosition, Is.EqualTo(new Vector2(0.25f, 0.25f)));
            Assert.That(members[1].RoomPosition.X, Is.EqualTo(0.25f).Within(0.00001f));
            Assert.That(members[1].RoomPosition.Y, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(members[1].LocalPosition, Is.EqualTo(new Vector2(0.25f, 0)));
            Assert.That(extraction.CopyReady, Is.False); Assert.That(extraction.ComponentReferencesVerified, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task NegativeAtlasOriginsPreserveNativeAndParentLocalPoses()
    {
        var yaml = GridYaml(chunkIndex: "-1,-1") + EntityBranch.Replace("pos: 1.25,0.25", "pos: -0.75,-1.75");
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); var room = spec.Layouts!.Rooms["Room"];
            room.Cells = new() { new(0, 0) }; room.SourceOrigin = new(-1, -2);
            project = Project(spec); var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var members = extraction!.Rooms.Single().Entities;
            Assert.That(members.Count, Is.EqualTo(2));
            Assert.That(members[0].GridPosition, Is.EqualTo(new Vector2(-0.75f, -1.75f)));
            Assert.That(members[0].LocalPosition, Is.EqualTo(members[0].GridPosition));
            Assert.That(members[0].RoomPosition, Is.EqualTo(new Vector2(0.25f, 0.25f)));
            Assert.That(members[1].RoomPosition.X, Is.EqualTo(0.25f).Within(0.00001f));
            Assert.That(members[1].RoomPosition.Y, Is.EqualTo(0.5f).Within(0.00001f));
        }));
    }

    [TestCase("missing-grid", "MissingRoomMapSelectedGrid")]
    [TestCase("wrong-grid", "RoomMapSourceMaskMismatch")]
    [TestCase("empty-tile", "RoomMapSourceMaskMismatch")]
    [TestCase("scaled-grid", "RoomMapSourceMaskMismatch")]
    public async Task ExplicitSelectionCannotFallBackToAnotherGridOrUnoccupiedTile(string kind, string code)
    {
        var yaml = GridYaml(grids: 2);
        if (kind == "scaled-grid") yaml = yaml.Replace("chunkSize: 2", "chunkSize: 2\n      tileSize: 2");
        // Give the second grid a disjoint chunk; the first still contains the requested cells.
        if (kind == "wrong-grid")
        {
            var start = yaml.IndexOf("  - uid: 2\n", StringComparison.Ordinal);
            yaml = yaml[..start] + yaml[start..].Replace("0,0", "1,0");
        }
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); var room = spec.Layouts!.Rooms["Room"];
            room.Cells = new() { new(0, 0) }; room.SourceOrigin = kind == "empty-tile" ? default : new(1, 0);
            room.SourceGridUid = kind == "missing-grid" ? 99 : 2;
            project = Project(spec); var source = Capture(system, project, bindings);
            Assert.That(system.TryReconcileRoomMasks(project, source, out var masks, out var issue), Is.False);
            Assert.That(masks, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
        }));
    }

    [TestCase("origin")] [TestCase("grid")]
    public async Task SourceSelectorsAreSerializedAndBoundToTheCapturedProject(string kind)
    {
        await AssertOnServer(() => WithSource(GridYaml(grids: 2), (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); var room = spec.Layouts!.Rooms["Room"];
            room.Cells = new() { new(0, 0) }; room.SourceOrigin = new(1, 0); room.SourceGridUid = 1;
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var node = serialization.WriteValue(spec, alwaysWrite: true, notNullableOverride: true);
            var roundtrip = serialization.Read<KsProcgenProjectSpec>(node, notNullableOverride: true);
            project = Project(roundtrip); var source = Capture(system, project, bindings);
            Assert.That(project.RoomSources.Single().SourceGridUid, Is.EqualTo(1));
            Assert.That(project.RoomSources.Single().SourceOrigin, Is.EqualTo(new Vector2i(1, 0)));
            spec = project.CopySpec();
            if (kind == "origin") spec.Layouts!.Rooms["Room"].SourceOrigin = new(1, 1);
            else spec.Layouts!.Rooms["Room"].SourceGridUid = 2;
            var changed = Project(spec);
            Assert.That(changed.DeclarationHash, Is.Not.EqualTo(project.DeclarationHash));
            Assert.That(system.TryReconcileRoomMasks(changed, source, out var masks, out var issue), Is.False);
            Assert.That(masks, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomMapMaskProjectMismatch"));
            Assert.That(system.Matches(source, changed, bindings, out issue), Is.False);
        }));
    }
}
