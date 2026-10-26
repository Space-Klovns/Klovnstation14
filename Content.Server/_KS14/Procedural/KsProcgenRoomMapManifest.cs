using System.IO;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

public sealed record KsProcgenRoomMapCaptureLimits
{
    public int MaximumFiles { get; init; } = 64;
    public int MaximumFileBytes { get; init; } = 4_194_304;
    public int MaximumTotalBytes { get; init; } = 16_777_216;
    public int MaximumYamlNodes { get; init; } = 262_144;
    public int MaximumYamlDepth { get; init; } = 64;
    public int MaximumScalarCharacters { get; init; } = 1_048_576;
    public int MaximumEntities { get; init; } = 65_536;
    public int MaximumProjectNodes { get; init; } = 262_144;
    public int MaximumProjectCharacters { get; init; } = 4_194_304;
    public int MaximumProjectDepth { get; init; } = 64;
    public int MaximumChunks { get; init; } = 1024;
    public int MaximumDecodedTiles { get; init; } = 262_144;
    public int MaximumDecodedBytes { get; init; } = 2_097_152;
    public KsProcgenPrototypeManifestLimits Definitions { get; init; } = new();
}

/// <summary>Detached bytes read from a server-bound content resource. Not a loaded or verified room.</summary>
public sealed class KsProcgenCapturedRoomMap
{
    private readonly byte[] _bytes;

    internal KsProcgenCapturedRoomMap(string resourcePath, byte[] bytes, string sha256, int format,
        int entityCount, IEnumerable<string> entities, IEnumerable<string> tiles, IEnumerable<string> components,
        IEnumerable<KsProcgenCapturedMapGrid> grids)
    {
        ResourcePath = resourcePath; _bytes = bytes; Sha256 = sha256; Format = format; EntityCount = entityCount;
        EntityReferences = Array.AsReadOnly(entities.ToArray()); TileReferences = Array.AsReadOnly(tiles.ToArray());
        EntityPrototypes = Array.AsReadOnly(EntityReferences.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        TilePrototypes = Array.AsReadOnly(TileReferences.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        OverrideComponentTypes = Array.AsReadOnly(components.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Grids = Array.AsReadOnly(grids.OrderBy(grid => grid.YamlUid).ToArray());
    }

    public string ResourcePath { get; }
    public string Sha256 { get; }
    public int ByteCount => _bytes.Length;
    public int Format { get; }
    public int EntityCount { get; }
    public IReadOnlyList<string> EntityPrototypes { get; }
    public IReadOnlyList<string> TilePrototypes { get; }
    public IReadOnlyList<string> OverrideComponentTypes { get; }
    public IReadOnlyList<KsProcgenCapturedMapGrid> Grids { get; }
    internal IReadOnlyList<string> EntityReferences { get; }
    internal IReadOnlyList<string> TileReferences { get; }
    public byte[] CopyBytes() => (byte[]) _bytes.Clone();
    public Stream OpenRead() => new MemoryStream(_bytes, writable: false);
    internal bool SameBytes(KsProcgenCapturedRoomMap other) => _bytes.AsSpan().SequenceEqual(other._bytes);
}

public sealed record KsProcgenCapturedMapTile(Vector2i Cell, string PrototypeId, byte Flags, byte Variant, byte RotationMirroring);

/// <summary>Map-local decoded cells, including empty tiles. No live grid or entity is allocated.</summary>
public sealed class KsProcgenCapturedMapGrid
{
    internal KsProcgenCapturedMapGrid(int yamlUid, int chunkSize, int tileSize, int chunks, int decodedBytes,
        IEnumerable<KsProcgenCapturedMapTile> tiles)
    {
        YamlUid = yamlUid; ChunkSize = chunkSize; TileSize = tileSize; ChunkCount = chunks; DecodedBytes = decodedBytes;
        Tiles = Array.AsReadOnly(tiles.OrderBy(tile => tile.Cell.Y).ThenBy(tile => tile.Cell.X).ToArray());
    }
    public int YamlUid { get; }
    public int ChunkSize { get; }
    public int TileSize { get; }
    public int ChunkCount { get; }
    public int DecodedBytes { get; }
    public IReadOnlyList<KsProcgenCapturedMapTile> Tiles { get; }
}

public sealed record KsProcgenCapturedRoomMapBinding(string RoomAssetId, string SourceKey, string ResourcePath, string Sha256);

/// <summary>
/// Project declarations, bound static map bytes and typed candidate/map prototype dependencies.
/// Complete assets, source-map instantiation, geometry reconciliation and operation remain separate.
/// </summary>
public sealed class KsProcgenRoomMapManifest
{
    internal KsProcgenRoomMapManifest(KsProcgenProject project, string canonicalProject,
        IEnumerable<KsProcgenCapturedRoomMap> maps, IEnumerable<KsProcgenCapturedRoomMapBinding> bindings,
        KsProcgenPrototypeManifest definitions, KsProcgenRoomMapCaptureLimits limits, int nodes,
        int projectNodes, int projectCharacters, string contentHash)
    {
        DocumentId = project.DocumentId; Revision = project.Revision; ProjectDeclarationHash = project.DeclarationHash;
        CanonicalProject = canonicalProject; Maps = Array.AsReadOnly(maps.ToArray());
        Bindings = Array.AsReadOnly(bindings.ToArray()); Definitions = definitions; Limits = limits;
        YamlNodes = nodes; ProjectNodes = projectNodes; ProjectCharacters = projectCharacters; ContentHash = contentHash;
    }

    public int SchemaVersion => 8;
    public string DocumentId { get; }
    public int Revision { get; }
    public ulong ProjectDeclarationHash { get; }
    public string CanonicalProject { get; }
    public IReadOnlyList<KsProcgenCapturedRoomMap> Maps { get; }
    public IReadOnlyList<KsProcgenCapturedRoomMapBinding> Bindings { get; }
    public KsProcgenPrototypeManifest Definitions { get; }
    public KsProcgenRoomMapCaptureLimits Limits { get; }
    public int YamlNodes { get; }
    public int ProjectNodes { get; }
    public int ProjectCharacters { get; }
    public string ContentHash { get; }
    public int CapturedBytes => Maps.Sum(map => map.ByteCount);
    public int DecodedChunks => Maps.Sum(map => map.Grids.Sum(grid => grid.ChunkCount));
    public int DecodedTiles => Maps.Sum(map => map.Grids.Sum(grid => grid.Tiles.Count));
    public int DecodedBytes => Maps.Sum(map => map.Grids.Sum(grid => grid.DecodedBytes));
}
