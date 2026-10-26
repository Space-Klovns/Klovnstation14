#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenRoomMapCaptureSystem))]
public sealed partial class KsProcgenRoomMapCaptureTests : KsProcgenGameTest
{
    private const string Yaml = "meta: {format: 7}\ntilemap: {0: Space, 1: FloorSteel}\nentities:\n- proto: Table\n  entities:\n  - uid: 1\n    components:\n    - type: Transform\n      parent: invalid\n";

    [Test]
    public async Task CapturesRealContentResourceWithoutLoadingOrInitializingItsEntities()
    {
        await AssertOnServer(() =>
        {
            var resource = Pair.Server.ResolveDependency<IResourceManager>();
            var path = "/Maps/Test/empty.yml";
            using var input = resource.ContentFileRead(path); using var output = new MemoryStream(); input.CopyTo(output);
            var bytes = output.ToArray(); var before = SEntMan.GetEntities().ToHashSet();
            var project = Project(Spec(bytes)); var system = Pair.Server.System<KsProcgenRoomMapCaptureSystem>();
            Assert.That(system.TryCapture(project, new Dictionary<string, string> { ["test-room"] = path }, out var result, out var issue), Is.True, message: issue?.Code);
            Assert.That(result!.Maps.Single().CopyBytes(), Is.EqualTo(bytes));
            Assert.That(result.Maps.Single().Format, Is.EqualTo(7));
            Assert.That(result.Maps.Single().EntityCount, Is.GreaterThan(0));
            Assert.That(result.Definitions.Bodies.Any(body => body.PrototypeId == "FloorSteel"), Is.True);
            Assert.That(result.ContentHash.Length, Is.EqualTo(64));
            Assert.That(result.ProjectNodes, Is.GreaterThan(0));
            Assert.That(result.ProjectCharacters, Is.EqualTo(result.CanonicalProject.Length));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(6)] [TestCase(7)]
    public async Task DetachedSourcesRetainExactBytesAndDoNotExposeWritableBuffers(int format)
    {
        await AssertOnServer(() => WithSource(Yaml.Replace("format: 7", $"format: {format}"), (system, project, bindings, root) =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var result = Capture(system, project, bindings);
            var map = result.Maps.Single(); var expected = Encoding.UTF8.GetBytes(Yaml.Replace("format: 7", $"format: {format}"));
            Assert.That(map.CopyBytes(), Is.EqualTo(expected)); Assert.That(map.Format, Is.EqualTo(format));
            var copy = map.CopyBytes(); copy[0] ^= 1;
            using (var stream = map.OpenRead())
            {
                Assert.That(stream.CanWrite, Is.False);
                Assert.Throws<NotSupportedException>(() => stream.WriteByte(0));
                Assert.Throws<UnauthorizedAccessException>(() => ((MemoryStream) stream).GetBuffer());
            }
            Assert.That(map.CopyBytes(), Is.EqualTo(expected));
            Assert.That(map.EntityPrototypes, Is.EqualTo(new[] { "Table" }));
            Assert.That(map.OverrideComponentTypes, Is.EqualTo(new[] { "Transform" }));
            Assert.That(map.TilePrototypes, Is.EqualTo(new[] { "FloorSteel", "Space" }));
            Assert.That(result.Definitions.Bodies.Any(body => body.PrototypeId == "Table"), Is.True);
            Assert.That(root.LastStream!.WasDisposed, Is.True);
            root.Bytes[0] ^= 1;
            Assert.That(map.CopyBytes(), Is.EqualTo(expected), message: "The mounted resource's backing array must not alias captured bytes.");
            Assert.That(system.Matches(result, project, bindings, out var issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("RoomMapFingerprintMismatch"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task IdentityBindsFullProjectDeclarationsAndDirectMapPrototypeBodies()
    {
        await AssertOnServer(() => WithSource(Yaml, (system, project, bindings, unusedRoot) =>
        {
            var result = Capture(system, project, bindings); var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.That(Capture(system, project, bindings).ContentHash, Is.EqualTo(result.ContentHash));
            }
            finally { CultureInfo.CurrentCulture = culture; }
            var copy = project.CopySpec(); copy.Layouts!.Rooms["Room"].Cells[0] = new(2, 0);
            Assert.That(system.Matches(result, Project(copy), bindings, out var issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("RoomMapManifestChanged"));
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("Table")); var save = prototype.MapSavable;
            try
            {
                prototype.MapSavable = !save;
                Assert.That(system.Matches(result, project, bindings, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("RoomMapManifestChanged"));
            }
            finally { prototype.MapSavable = save; }
            Assert.That(system.Matches(result, project, bindings, out issue), Is.True, message: issue?.Code);
        }));
    }

    [Test]
    public async Task DeduplicatesSharedResourcesAndFreezesBindingsBeforeReadCallbacks()
    {
        await AssertOnServer(() => WithSource(Yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec();
            spec.Layouts!.Rooms["Other"] = new() { SourceKey = "test-room", ContentFingerprint = Fingerprint(root.Bytes), Cells = new() { new(0, 0) } };
            project = Project(spec); var path = bindings["test-room"];
            root.OnOpen = () => bindings["test-room"] = "/Maps/Test/does-not-exist.yml";
            var result = Capture(system, project, bindings);
            Assert.That(result.Maps.Count, Is.EqualTo(1)); Assert.That(result.Bindings.Count, Is.EqualTo(2));
            Assert.That(result.Bindings.All(binding => binding.ResourcePath == path), Is.True);
            Assert.That(root.Opened, Is.EqualTo(1));
            Assert.That(system.Matches(result, project, bindings, out var issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("MissingRoomMapSource"));
        }));
    }

    [TestCase("unbound", "UnboundRoomMapSource")]
    [TestCase("opaque", "UnsupportedRoomMapFingerprint")]
    [TestCase("mismatch", "RoomMapFingerprintMismatch")]
    [TestCase("missing", "MissingRoomMapSource")]
    [TestCase("relative", "InvalidRoomMapCaptureInput")]
    [TestCase("traversal", "InvalidRoomMapCaptureInput")]
    [TestCase("outside", "InvalidRoomMapCaptureInput")]
    [TestCase("limits", "InvalidRoomMapCaptureInput")]
    public async Task InvalidInputsNeverReturnPartialCaptures(string kind, string code)
    {
        await AssertOnServer(() => WithSource(Yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); var limits = new KsProcgenRoomMapCaptureLimits();
            switch (kind)
            {
                case "unbound": bindings.Clear(); break;
                case "opaque": spec.Layouts!.Rooms["Room"].ContentFingerprint = "declared-v1"; break;
                case "mismatch": spec.Layouts!.Rooms["Room"].ContentFingerprint = "sha256:" + new string('0', 64); break;
                case "missing": bindings["test-room"] = "/Maps/Test/does-not-exist.yml"; break;
                case "relative": bindings["test-room"] = "Maps/Test/room.yml"; break;
                case "traversal": bindings["test-room"] = "/Maps/Test/../room.yml"; break;
                case "outside": bindings["test-room"] = "/Audio/room.yml"; break;
                case "limits": limits = limits with { Definitions = new() { MaximumReferences = 0 } }; break;
            }
            Assert.That(system.TryCapture(Project(spec), bindings, out var result, out var issue, limits: limits), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
            if (kind is not ("mismatch" or "missing")) Assert.That(root.Opened, Is.Zero);
            Assert.That(root.LastStream == null || root.LastStream.WasDisposed, Is.True);
        }));
    }

    [TestCase("bytes", "RoomMapFileByteBudget")]
    [TestCase("total", "RoomMapTotalByteBudget")]
    [TestCase("nodes", "RoomMapYamlNodeBudget")]
    [TestCase("depth", "RoomMapYamlDepthBudget")]
    [TestCase("scalar", "RoomMapScalarBudget")]
    [TestCase("entities", "RoomMapEntityBudget")]
    [TestCase("project", "PrototypeManifestCharacterBudget")]
    public async Task WorkBudgetsBoundNonseekableShortReadSources(string kind, string code)
    {
        var text = Yaml + "  - uid: 2\n";
        await AssertOnServer(() => WithSource(text, (system, project, bindings, root) =>
        {
            var limits = new KsProcgenRoomMapCaptureLimits();
            limits = kind switch
            {
                "bytes" => limits with { MaximumFileBytes = root.Bytes.Length - 1 },
                "total" => limits with { MaximumTotalBytes = root.Bytes.Length - 1 },
                "nodes" => limits with { MaximumYamlNodes = 1 },
                "depth" => limits with { MaximumYamlDepth = 1 },
                "scalar" => limits with { MaximumScalarCharacters = 3 },
                "project" => limits with { MaximumProjectCharacters = 1 },
                _ => limits with { MaximumEntities = 1 },
            };
            root.ChunkSize = 3;
            Assert.That(system.TryCapture(project, bindings, out var result, out var issue, limits: limits), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
            if (kind == "project") Assert.That(root.Opened, Is.Zero);
            else
            {
                Assert.That(root.LastStream!.WasDisposed, Is.True);
                Assert.That(root.LastStream.BytesRead, Is.LessThanOrEqualTo(root.Bytes.Length));
            }
        }));
    }

    [TestCase("files", "RoomMapFileBudget")]
    [TestCase("bytes", "RoomMapTotalByteBudget")]
    [TestCase("nodes", "RoomMapYamlNodeBudget")]
    [TestCase("entities", "RoomMapEntityBudget")]
    [TestCase("definitions", "PrototypeManifestDefinitionBudget")]
    public async Task IndependentFileCapturesShareCumulativeBudgets(string kind, string code)
    {
        await AssertOnServer(() => WithSource(Yaml, (system, project, bindings, root) =>
        {
            var first = Capture(system, project, bindings); root.Opened = 0;
            root.Paths.Add("other.yml"); bindings["other-room"] = bindings["test-room"].Replace("room.yml", "other.yml");
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Other"] = new()
            { SourceKey = "other-room", ContentFingerprint = Fingerprint(root.Bytes), Cells = new() { new(1, 0) } };
            project = Project(spec);
            var limits = new KsProcgenRoomMapCaptureLimits();
            limits = kind switch
            {
                "files" => limits with { MaximumFiles = 1 },
                "bytes" => limits with { MaximumTotalBytes = root.Bytes.Length * 2 - 1 },
                "nodes" => limits with { MaximumYamlNodes = first.YamlNodes * 2 - 1 },
                "entities" => limits with { MaximumEntities = 1 },
                _ => limits with { Definitions = new() { MaximumDefinitions = 2 } },
            };
            Assert.That(system.TryCapture(project, bindings, out var rejected, out var issue, limits: limits), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
            if (kind == "files") Assert.That(root.Opened, Is.Zero);
            else Assert.That(root.LastStream!.WasDisposed, Is.True);
            var result = Capture(system, project, bindings);
            Assert.That(result.Maps.Count, Is.EqualTo(2)); Assert.That(result.YamlNodes, Is.EqualTo(first.YamlNodes * 2));
            Assert.That(result.CapturedBytes, Is.EqualTo(root.Bytes.Length * 2));
            Assert.That(result.Maps.Sum(map => map.EntityCount), Is.EqualTo(2), message: "Map-local UIDs may repeat in independent resources.");
        }));
    }

    [Test]
    public async Task DuplicateMapReferencesStillConsumeTheDirectReferenceAllowance()
    {
        await AssertOnServer(() => WithSource(Yaml.Replace("1: FloorSteel", "1: FloorSteel, 2: FloorSteel"), (system, project, bindings, root) =>
        {
            Assert.That(system.TryCapture(project, bindings, out var result, out var issue,
                limits: new() { Definitions = new() { MaximumDefinitions = 3, MaximumReferences = 3 } }), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
            Assert.That(root.LastStream!.WasDisposed, Is.True);
        }));
    }

    [Test]
    public async Task PreCancelledRequestsAndInvalidUtf8NeverProduceCaptures()
    {
        await AssertOnServer(() => WithSource(Yaml, (system, project, bindings, root) =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.That(system.TryCapture(project, bindings, out var result, out var issue, cancellationToken: cancellation.Token), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomMapCaptureCancelled")); Assert.That(root.Opened, Is.Zero);
            root.Bytes[0] = 0xff;
            Assert.That(system.TryCapture(Project(Spec(root.Bytes)), bindings, out result, out issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("UnsupportedRoomMapSource")); Assert.That(root.LastStream!.WasDisposed, Is.True);
        }));
    }

    [Test]
    public async Task GeometryOnlyProjectsStillCaptureTheirThemeCandidateBodies()
    {
        await AssertOnServer(() =>
        {
            var spec = Spec(Encoding.UTF8.GetBytes(Yaml)); var room = spec.Layouts!.Rooms["Room"]; room.SourceKey = null; room.ContentFingerprint = null;
            spec.Areas["Area"] = new() { Theme = "KsProcgenNoDoorThemeFixture", Request = new()
            { RequestId = "Area", Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } } } };
            var project = Project(spec); var system = Pair.Server.System<KsProcgenRoomMapCaptureSystem>();
            var result = Capture(system, project, new Dictionary<string, string>());
            Assert.That(result.Maps, Is.Empty); Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.Definitions.Bodies.Any(body => body.PrototypeId == "WallSolid"), Is.True);
            Assert.That(system.Matches(result, project, new Dictionary<string, string>(), out var issue), Is.True, message: issue?.Code);
        });
    }

    [TestCase("alias", "UnsupportedRoomMapAlias")]
    [TestCase("documents", "InvalidRoomMapDocument")]
    [TestCase("format", "UnsupportedRoomMapFormat")]
    [TestCase("uid", "InvalidRoomMapEntityUid")]
    [TestCase("component", "UnsupportedRoomMapComponent")]
    [TestCase("prototype", "MissingPrototypeManifestEntity")]
    [TestCase("tile", "MissingPrototypeManifestTile")]
    [TestCase("duplicate", "UnsupportedRoomMapSource")]
    public async Task UnsupportedOrAmbiguousMapDataIsRejectedWithoutAllocatingEntities(string kind, string code)
    {
        var text = kind switch
        {
            "alias" => Yaml + "extra: &anchor [*anchor]\n",
            "documents" => Yaml + "---\n{}\n",
            "format" => Yaml.Replace("format: 7", "format: 999"),
            "uid" => Yaml + "  - uid: 1\n",
            "component" => Yaml.Replace("type: Transform", "type: KsMissingMapComponent"),
            "prototype" => Yaml.Replace("proto: Table", "proto: KsMissingMapEntity"),
            "tile" => Yaml.Replace("1: FloorSteel", "1: KsMissingMapTile"),
            _ => Yaml + "meta: {format: 7}\n",
        };
        await AssertOnServer(() => WithSource(text, (system, project, bindings, root) =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(project, bindings, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
            Assert.That(root.LastStream!.WasDisposed, Is.True);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task CancellationAndReadFailureDisposeEveryOpenedResource(bool ioFailure)
    {
        await AssertOnServer(() => WithSource(Yaml, (system, project, bindings, root) =>
        {
            using var cancellation = new CancellationTokenSource();
            root.ThrowOnRead = ioFailure;
            if (!ioFailure) root.OnChunk = cancellation.Cancel;
            Assert.That(system.TryCapture(project, bindings, out var result, out var issue, cancellationToken: cancellation.Token), Is.False);
            Assert.That(result, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo(ioFailure ? "UnsupportedRoomMapSource" : "RoomMapCaptureCancelled"));
            Assert.That(root.LastStream!.WasDisposed, Is.True);
        }));
    }

    [TestCase(1)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
    public async Task ChunkVersionsPreserveEveryNativeCellAndTileAttribute(int version)
    {
        await AssertOnServer(() => WithSource(GridYaml(version: version, alias: version < 6 ? 89 : int.MaxValue, chunkIndex: "-1,2"), (system, project, bindings, unusedRoot) =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var result = Capture(system, project, bindings); var grid = result.Maps.Single().Grids.Single();
            Assert.That(result.SchemaVersion, Is.EqualTo(8)); Assert.That(grid.YamlUid, Is.EqualTo(1));
            Assert.That(result.DecodedChunks, Is.EqualTo(1)); Assert.That(result.DecodedTiles, Is.EqualTo(4));
            Assert.That(result.DecodedBytes, Is.EqualTo(4 * (version < 6 ? 4 : version == 6 ? 6 : 7)));
            Assert.That(grid.Tiles[0].Cell, Is.EqualTo(new Robust.Shared.Maths.Vector2i(-2, 4)));
            Assert.That(grid.Tiles[0].PrototypeId, Is.EqualTo("Space"));
            var tile = grid.Tiles[3]; Assert.That(tile.Cell, Is.EqualTo(new Robust.Shared.Maths.Vector2i(-1, 5)));
            Assert.That(tile.PrototypeId, Is.EqualTo("FloorSteel")); Assert.That(tile.Flags, Is.EqualTo(102));
            Assert.That(tile.Variant, Is.EqualTo(12)); Assert.That(tile.RotationMirroring, Is.EqualTo(version >= 7 ? 4 : 0));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase("truncated", "InvalidRoomMapChunkPayload")]
    [TestCase("trailing", "InvalidRoomMapChunkPayload")]
    [TestCase("base64", "InvalidRoomMapChunkPayload")]
    [TestCase("alias", "UnknownRoomMapTileAlias")]
    [TestCase("negative", "UnknownRoomMapTileAlias")]
    [TestCase("index", "InvalidRoomMapChunkIndex")]
    [TestCase("duplicate", "InvalidRoomMapChunkIndex")]
    [TestCase("numeric-alias", "InvalidRoomMapTile")]
    [TestCase("version", "UnsupportedRoomMapChunkFormat")]
    [TestCase("size", "UnsupportedRoomMapChunkFormat")]
    [TestCase("scale", "UnsupportedRoomMapGridScale")]
    [TestCase("overflow", "RoomMapDecodedCoordinateRange")]
    public async Task MalformedChunksFailBeforeAnyWorldMutation(string kind, string code)
    {
        var yaml = kind switch
        {
            "truncated" => GridYaml(changePayload: bytes => bytes[..^1]),
            "trailing" => GridYaml(changePayload: bytes => bytes.Concat(new byte[] { 0 }).ToArray()),
            "base64" => GridYaml().Replace("tiles: ", "tiles: '!!!!' # "),
            "alias" => GridYaml().Replace("70000: FloorSteel", "70001: FloorSteel"),
            "negative" => GridYaml(changePayload: bytes => { Array.Fill(bytes, (byte) 255, 7, 4); return bytes; }),
            "index" => GridYaml().Replace("ind: 0,0", "ind: 1,0"),
            "duplicate" => GridYaml(chunks: 2).Replace("1,0:", "00,0:").Replace("ind: 1,0", "ind: 0,0"),
            "numeric-alias" => GridYaml().Replace("tilemap: {0:", "tilemap: {00: Space, 0:"),
            "version" => GridYaml(version: 8),
            "size" => GridYaml(changePayload: bytes => bytes[..7]).Replace("size: 2", "size: 1"),
            "scale" => GridYaml().Replace("chunkSize: 2", "chunkSize: 257"),
            _ => GridYaml(chunkIndex: "2147483647,0"),
        };
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, root) =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(project, bindings, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code)); Assert.That(root.LastStream!.WasDisposed, Is.True);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase("chunks", "RoomMapChunkBudget")]
    [TestCase("tiles", "RoomMapDecodedTileBudget")]
    [TestCase("bytes", "RoomMapDecodedByteBudget")]
    public async Task DecodeBudgetsAccumulateAcrossDistinctFiles(string kind, string code)
    {
        await AssertOnServer(() => WithSource(GridYaml(), (system, project, bindings, root) =>
        {
            root.Paths.Add("other.yml"); bindings["other-room"] = bindings["test-room"].Replace("room.yml", "other.yml");
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Other"] = new()
            { SourceKey = "other-room", ContentFingerprint = Fingerprint(root.Bytes), Cells = new() { new(1, 0) } }; project = Project(spec);
            var limits = new KsProcgenRoomMapCaptureLimits();
            limits = kind switch { "chunks" => limits with { MaximumChunks = 1 }, "tiles" => limits with { MaximumDecodedTiles = 7 },
                _ => limits with { MaximumDecodedBytes = 55 } };
            Assert.That(system.TryCapture(project, bindings, out var rejected, out var issue, limits: limits), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
            var result = Capture(system, project, bindings);
            Assert.That(result.DecodedChunks, Is.EqualTo(2)); Assert.That(result.DecodedTiles, Is.EqualTo(8)); Assert.That(result.DecodedBytes, Is.EqualTo(56));
        }));
    }

    [Test]
    public async Task InheritedGridsAreRejectedInsteadOfSilentlyMissingTheirChunks()
    {
        await AssertOnServer(() => WithSource(Yaml, (system, project, bindings, unusedRoot) =>
        {
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("Table"));
            try
            {
                prototype.Components.Add("MapGrid", new EntityPrototype.ComponentRegistryEntry(new MapGridComponent()));
                Assert.That(system.TryCapture(project, bindings, out var result, out var issue), Is.False);
                Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("UnsupportedInheritedRoomMapGrid"));
            }
            finally { prototype.Components.Remove("MapGrid"); }
        }));
    }

    [TestCase("success")] [TestCase("empty")] [TestCase("missing")] [TestCase("ambiguous")] [TestCase("scale")] [TestCase("stale")] [TestCase("budget")]
    public async Task NativeMasksMatchOnlyUnambiguousOccupiedUnitGridCells(string kind)
    {
        var yaml = GridYaml(grids: kind == "ambiguous" ? 2 : 1);
        if (kind == "scale") yaml = yaml.Replace("chunkSize: 2", "chunkSize: 2\n      tileSize: 2");
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new()
            { kind == "empty" ? new(0, 0) : kind == "missing" ? new(5, 5) : new(1, 0) };
            project = Project(spec); var source = Capture(system, project, bindings);
            if (kind == "stale") { spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells[0] = new(1, 1); project = Project(spec); }
            var before = SEntMan.GetEntities().ToHashSet();
            var accepted = system.TryReconcileRoomMasks(project, source, out var result, out var issue, maximumCellProbes: kind == "budget" ? 4 : 20);
            if (kind == "success")
            {
                Assert.That(accepted, Is.True, message: issue?.Code); Assert.That(result!.CellProbes, Is.EqualTo(5));
                Assert.That(result.SourceContentHash, Is.EqualTo(source.ContentHash)); Assert.That(result.Rooms.Single().GridYamlUid, Is.EqualTo(1));
                Assert.That(result.Rooms.Single().Cells, Is.EquivalentTo(spec.Layouts!.Rooms["Room"].Cells));
            }
            else
            {
                Assert.That(accepted, Is.False); Assert.That(result, Is.Null);
                var code = kind switch { "ambiguous" => "AmbiguousRoomMapSourceGrid", "stale" => "RoomMapMaskProjectMismatch",
                    "budget" => "RoomMapMaskProbeBudget", _ => "RoomMapSourceMaskMismatch" };
                Assert.That(issue?.Code, Is.EqualTo(code));
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task NativeMaskIndexesAndWorkAreSharedAcrossRoomExtractions()
    {
        await AssertOnServer(() => WithSource(GridYaml(), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) };
            spec.Layouts.Rooms["Other"] = new() { SourceKey = "test-room", ContentFingerprint = Fingerprint(root.Bytes), Cells = new() { new(1, 1) } };
            project = Project(spec); var source = Capture(system, project, bindings); root.Available = false;
            Assert.That(system.TryReconcileRoomMasks(project, source, out var rejected, out var issue, maximumCellProbes: 5), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomMapMaskProbeBudget"));
            Assert.That(system.TryReconcileRoomMasks(project, source, out var result, out issue, maximumCellProbes: 6), Is.True, message: issue?.Code);
            Assert.That(result!.CellProbes, Is.EqualTo(6)); Assert.That(result.Rooms.Count, Is.EqualTo(2));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.That(system.TryReconcileRoomMasks(project, source, out rejected, out issue, cancellationToken: cancellation.Token), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomMapMaskCancelled"));
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task DecodingAndRejectedChunksLeaveLiveTileChangeEventsEnabled(bool reject)
    {
        var yaml = reject ? GridYaml().Replace("70000: FloorSteel", "70001: FloorSteel") : GridYaml();
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, unusedRoot) =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
            var grid = maps.CreateGridEntity(mapId); grid.Comp.CanSplit = false;
            var tileId = Pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId;
            var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>(); var events = 0;
            try
            {
                maps.SetTile(grid.Owner, grid.Comp, new Robust.Shared.Maths.Vector2i(0, 0), new Tile(tileId));
                hook.TileAction = () => events++;
                var before = SEntMan.GetEntities().ToHashSet();
                Assert.That(system.TryCapture(project, bindings, out _, out _), Is.EqualTo(!reject));
                Assert.That(events, Is.Zero); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                maps.SetTile(grid.Owner, grid.Comp, new Robust.Shared.Maths.Vector2i(0, 0), Tile.Empty);
                Assert.That(events, Is.EqualTo(1), message: "Source decoding must leave actual live tile change notification enabled.");
            }
            finally { hook.TileAction = null; SEntMan.DeleteEntity(mapUid); }
        }));
    }

    private static string GridYaml(int version = 7, int alias = 70000, string chunkIndex = "0,0", int grids = 1, int chunks = 1,
        Func<byte[], byte[]>? changePayload = null)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            for (var tile = 0; tile < 4; tile++)
            {
                var id = tile == 0 ? 0 : alias;
                if (version < 6) writer.Write((ushort) id); else writer.Write(id);
                writer.Write((byte) (99 + tile)); writer.Write((byte) (9 + tile));
                if (version >= 7) writer.Write((byte) (7 - tile));
            }
        }
        var data = bytes.ToArray(); if (changePayload != null) data = changePayload(data);
        var text = new StringBuilder($"meta: {{format: 7}}\ntilemap: {{0: Space, {alias}: FloorSteel}}\nentities:\n- proto: ''\n  entities:\n");
        for (var grid = 1; grid <= grids; grid++)
        {
            text.Append($"  - uid: {grid}\n    components:\n    - type: MapGrid\n      chunkSize: 2\n      chunks:\n");
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                var index = chunk == 0 ? chunkIndex : $"{chunk},0";
                text.Append($"        {index}:\n          ind: {index}\n          size: 2\n          version: {version}\n          tiles: {Convert.ToBase64String(data)}\n");
            }
        }
        return text.ToString();
    }

    private KsProcgenProject Project(KsProcgenProjectSpec spec)
    {
        Assert.That(KsProcgenProject.TryCapture(Pair.Server.ResolveDependency<IPrototypeManager>(), spec, out var project, out var issue), Is.True, message: issue?.Code);
        return project!;
    }
    private static KsProcgenProjectSpec Spec(byte[] bytes) => new()
    {
        DocumentId = "MapCapture", Layouts = new() { DocumentId = "MapCapture", Rooms = new()
        { ["Room"] = new() { SourceKey = "test-room", ContentFingerprint = Fingerprint(bytes), Cells = new() { new(0, 0) } } } },
    };
    private static string Fingerprint(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes));
    private static KsProcgenRoomMapManifest Capture(KsProcgenRoomMapCaptureSystem system, KsProcgenProject project, IReadOnlyDictionary<string, string> bindings)
    {
        Assert.That(system.TryCapture(project, bindings, out var result, out var issue), Is.True, message: issue?.Code); return result!;
    }
    private void WithSource(string yaml, Action<KsProcgenRoomMapCaptureSystem, KsProcgenProject, Dictionary<string, string>, SourceRoot> action)
    {
        var root = new SourceRoot(Encoding.UTF8.GetBytes(yaml));
        var prefix = "/Maps/Test/_KS14/Capture/" + Guid.NewGuid().ToString("N");
        Pair.Server.ResolveDependency<IResourceManager>().AddRoot(new ResPath(prefix), root);
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal) { ["test-room"] = prefix + "/room.yml" };
        try { action(Pair.Server.System<KsProcgenRoomMapCaptureSystem>(), Project(Spec(root.Bytes)), bindings, root); }
        finally { root.OnOpen = null; root.OnChunk = null; root.Available = false; }
    }

    private sealed class SourceRoot(byte[] bytes) : IContentRoot
    {
        public byte[] Bytes = bytes;
        public bool Available = true;
        public HashSet<string> Paths = new(StringComparer.Ordinal) { "room.yml" };
        public int Opened;
        public int ChunkSize = int.MaxValue;
        public bool ThrowOnRead;
        public Action? OnOpen;
        public Action? OnChunk;
        public ReadStream? LastStream;
        public void Mount() { }
        public bool FileExists(ResPath path) => Available && Paths.Contains(path.CanonPath);
        public IEnumerable<ResPath> FindFiles(ResPath path) => [];
        public IEnumerable<string> GetRelativeFilePaths() => [];
        public bool TryGetFile(ResPath path, [NotNullWhen(true)] out Stream? stream)
        {
            stream = null; if (!FileExists(path)) return false;
            Opened++; OnOpen?.Invoke(); LastStream = new ReadStream(this); stream = LastStream; return true;
        }
    }
    private sealed class ReadStream(SourceRoot owner) : Stream
    {
        private readonly MemoryStream _inner = new(owner.Bytes, writable: false);
        public bool WasDisposed;
        public int BytesRead;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (owner.ThrowOnRead) throw new IOException("Fixture read failure");
            var read = _inner.Read(buffer, offset, Math.Min(count, owner.ChunkSize)); BytesRead += read; owner.OnChunk?.Invoke(); return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { WasDisposed = true; if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
