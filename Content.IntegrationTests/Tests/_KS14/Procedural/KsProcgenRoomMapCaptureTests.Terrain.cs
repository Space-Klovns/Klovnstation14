#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    private static string TerrainSource(byte rotationMirroring)
    {
        const string original = "    - type: MapGrid";
        const string replacement = "    - type: Transform\n      parent: 50\n    - type: MapGrid";
        return CopySource().Replace(GridYaml().Replace(original, replacement),
            GridYaml(changePayload: bytes => { bytes[13] = rotationMirroring; return bytes; }).Replace(original, replacement));
    }

    private static IEnumerable<TestCaseData> TerrainTransforms()
    {
        // Expected native texture orientations for clockwise room turns, including mirrored tiles.
        var expected = new[,] { { 0, 3, 2, 1 }, { 1, 0, 3, 2 }, { 2, 1, 0, 3 }, { 3, 2, 1, 0 },
            { 4, 5, 6, 7 }, { 5, 6, 7, 4 }, { 6, 7, 4, 5 }, { 7, 4, 5, 6 } };
        for (var code = 0; code < 8; code++)
            for (var turn = 0; turn < 4; turn++) yield return new(code, turn, expected[code, turn]);
    }

    [TestCaseSource(nameof(TerrainTransforms))]
    public async Task CapturedRoomTerrainPreservesMetadataAndComposesNativeMirrorRotation(int code, int turns, int expectedCode)
    {
        await AssertOnServer(() => WithSource(TerrainSource((byte) code), (capture, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            root.Available = false;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var terrain = Pair.Server.System<KsProcgenRoomTerrainSystem>();
                var maps = Pair.Server.System<SharedMapSystem>(); var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
                var tileId = Pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId;
                var sentinel = new Tile(tileId, flags: 11, variant: 2, rotationMirroring: 1);
                maps.SetTile(gridUid, grid, new Vector2i(-2, -2), sentinel);
                Assert.That(terrain.TryPrepare(source, extraction!, "Room", new(2, 4), turns, out var plan, out issue,
                    maximumTiles: 1, maximumSourceTileProbes: 4), Is.True, message: issue?.Code);
                Assert.That(plan!.SourceTileProbes, Is.EqualTo(4)); Assert.That(plan.Tiles, Has.Count.EqualTo(1));
                Assert.That(terrain.TryWrite(plan, gridUid, allocations, 32, out issue), Is.True, message: issue?.Code);
                var expectedCell = turns switch { 0 => new Vector2i(3, 4), 1 => new(2, 3), 2 => new(1, 4), _ => new(2, 5) };
                var actual = maps.GetTileRef(gridUid, grid, expectedCell).Tile;
                Assert.That(actual, Is.EqualTo(new Tile(tileId, flags: 100, variant: 10, rotationMirroring: (byte) expectedCode)));
                Assert.That(maps.GetTileRef(gridUid, grid, new Vector2i(-2, -2)).Tile, Is.EqualTo(sentinel));
                Assert.That(maps.GetTileRef(gridUid, grid, expectedCell + new Vector2i(1, 1)).Tile, Is.EqualTo(Tile.Empty));
                Assert.That(allocations.Count, Is.EqualTo(2), message: "Terrain copying must not instantiate source-map entities.");
            }, layFloor: false);
        }));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public async Task CompleteRoomCopyCombinesExactLShapedTerrainAndNativeEntityGraph(int turns)
    {
        var yaml = CopySource().Replace("anchored: false", "anchored: true").Replace("pos: 1.25,0.25", "pos: 1.5,0.5");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0), new(0, 1), new(1, 1) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            root.Available = false;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiateRoom(source, extraction!, "Room", gridUid,
                    new(2, 3), turns, allocations, 32, out var instance, out issue), Is.True, message: issue?.ToString());
                Assert.That(instance!.Terrain.SourceHash, Is.EqualTo(source.ContentHash));
                Assert.That(instance.Terrain.ExtractionHash, Is.EqualTo(extraction!.ContentHash));
                Assert.That(instance.Terrain.Tiles, Has.Count.EqualTo(3)); Assert.That(instance.Members, Has.Count.EqualTo(2));
                var grid = SEntMan.GetComponent<MapGridComponent>(gridUid); var maps = Pair.Server.System<SharedMapSystem>();
                foreach (var tile in instance.Terrain.Tiles) Assert.That(maps.GetTileRef(gridUid, grid, tile.Cell).Tile, Is.EqualTo(tile.Tile));
                var hole = KsProcgenGeometry.TransformCell(default, default, new(2, 3), turns);
                Assert.That(maps.GetTileRef(gridUid, grid, hole).Tile, Is.EqualTo(Tile.Empty));
                var tableUid = instance.Members[10];
                Assert.That(SEntMan.GetComponent<TransformComponent>(tableUid).Anchored, Is.True,
                    message: "Captured floors must be present before the anchored structure initializes.");
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(tableUid).EntityName, Is.EqualTo("Authored table"));
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(tableUid).EntityLifeStage, Is.EqualTo(EntityLifeStage.Initialized));
                Assert.That(SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(tableUid).Target, Is.EqualTo(instance.Members[2]));
            }, layFloor: false);
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task CompleteRoomCopyRebasesPositiveAndNegativeAtlasOriginsBeforeRotation(bool negative)
    {
        var yaml = CopySource();
        if (negative) yaml = yaml.Replace("        0,0:\n          ind: 0,0", "        -1,-1:\n          ind: -1,-1")
            .Replace("pos: 1.25,0.25", "pos: -0.75,-1.75");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(0, 0) };
            spec.Layouts.Rooms["Room"].SourceOrigin = negative ? new(-1, -2) : new(1, 0); project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiateRoom(source, extraction!, "Room", gridUid,
                    new(-3, -4), 1, allocations, 32, out var instance, out issue), Is.True, message: issue?.ToString());
                var tile = instance!.Terrain.Tiles.Single(); Assert.That(tile.Cell, Is.EqualTo(new Vector2i(-3, -4)));
                Assert.That(tile.Tile.Flags, Is.EqualTo(100)); Assert.That(tile.Tile.RotationMirroring, Is.EqualTo(7));
                Assert.That(SEntMan.GetComponent<TransformComponent>(instance.Members[10]).LocalPosition,
                    Is.EqualTo(new System.Numerics.Vector2(-2.75f, -3.25f)));
            }, layFloor: false);
        }));
    }

    [Test]
    public async Task TerrainAndEntitySnapshotsFromDifferentCapturedSourcesCannotBeCombined()
    {
        await AssertOnServer(() => WithSource(CopySource(), (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithSource(CopySource(), (otherCapture, otherProject, otherBindings, otherRoot) =>
            {
                var otherSource = Capture(otherCapture, otherProject, otherBindings);
                Assert.That(otherSource.ContentHash, Is.Not.EqualTo(source.ContentHash));
                WithRoomCopyTransaction((gridUid, allocations) =>
                {
                    Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiateRoom(otherSource, extraction!, "Room", gridUid,
                        default, 0, allocations, 32, out var instance, out issue), Is.False);
                    Assert.That(instance, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomTerrainSourceMismatch"));
                    Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
                }, layFloor: false);
            });
        }));
    }

    [TestCase("tiles", "RoomTerrainTileBudget")]
    [TestCase("probes", "RoomTerrainProbeBudget")]
    [TestCase("rotation", "InvalidRoomTerrainInput")]
    [TestCase("invalid-code", "RoomTerrainUnsupportedTile")]
    [TestCase("cancel", "RoomTerrainCancelled")]
    public async Task TerrainPreflightRejectsAtomicallyBeforeWriting(string mode, string expectedIssue)
    {
        await AssertOnServer(() => WithSource(TerrainSource(mode == "invalid-code" ? (byte) 255 : (byte) 0),
            (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                using var cancellation = new CancellationTokenSource(); if (mode == "cancel") cancellation.Cancel();
                Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiateRoom(source, extraction!, "Room", gridUid,
                    default, mode == "rotation" ? 4 : 0, allocations, 32, out var instance, out issue,
                    maximumTiles: mode == "tiles" ? 0 : 1, maximumSourceTileProbes: mode == "probes" ? 3 : 4,
                    cancellationToken: cancellation.Token), Is.False);
                Assert.That(issue?.Code, Is.EqualTo(expectedIssue)); Assert.That(instance, Is.Null);
                Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
                Assert.That(allocations.Count, Is.EqualTo(2));
            }, layFloor: false);
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task CombinedRoomFailuresRetireTerrainAndEntitiesWithoutReturningAPartialReceipt(bool changeTerrain)
    {
        await AssertOnServer(() => WithSource(CopySource(), (capture, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var startup = Pair.Server.System<KsProcgenRoomCopyTestSystem>();
                if (changeTerrain) startup.StartupAction = _ =>
                {
                    var maps = Pair.Server.System<SharedMapSystem>(); var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
                    var tile = maps.GetTileRef(gridUid, grid, new Vector2i(1, 0)).Tile;
                    maps.SetTile(gridUid, grid, new Vector2i(1, 0), new Tile(tile.TypeId, flags: (byte) (tile.Flags ^ 1),
                        variant: tile.Variant, rotationMirroring: tile.RotationMirroring));
                };
                try
                {
                    Assert.That(Pair.Server.System<KsProcgenRoomInstantiationSystem>().TryInstantiateRoom(source, extraction!, "Room", gridUid,
                        default, 0, allocations, 32, out var instance, out issue,
                        maximumImageCharacters: changeTerrain ? 4_194_304 : 0), Is.False);
                    Assert.That(instance, Is.Null);
                    Assert.That(issue?.Code, Is.EqualTo(changeTerrain ? "RoomTerrainDrift" : "RoomInstantiationImageBudget"));
                }
                finally { startup.StartupAction = null; }
            }, layFloor: false);
        }));
    }
}
