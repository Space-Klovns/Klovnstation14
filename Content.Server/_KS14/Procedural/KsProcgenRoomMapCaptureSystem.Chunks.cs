using System.Buffers.Binary;
using System.Globalization;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    private sealed class DecodeWork(KsProcgenRoomMapCaptureLimits limits)
    {
        public readonly KsProcgenRoomMapCaptureLimits Limits = limits;
        public int Chunks;
        public int Tiles;
        public int Bytes;
    }

    private static bool TryDecodeGrid(int uid, MappingDataNode component, IReadOnlyDictionary<int, string> aliases,
        DecodeWork work, CancellationToken cancellationToken, out KsProcgenCapturedMapGrid? captured, out KsProcgenIssue? issue)
    {
        captured = null; issue = null;
        var chunkSize = 16; var tileSize = 1;
        if (component.Has("chunkSize") && (!Integer(component, "chunkSize", out chunkSize) || chunkSize is < 1 or > 256) ||
            component.Has("tileSize") && (!Integer(component, "tileSize", out tileSize) || tileSize is < 1 or > ushort.MaxValue))
            return Reject("UnsupportedRoomMapGridScale", "Grid dimensions exceed the supported static decoding bounds.", out issue);
        if (!component.TryGet<MappingDataNode>("chunks", out var chunks))
        {
            if (component.Has("chunks")) return Reject("InvalidRoomMapChunks", "Grid chunks require a mapping.", out issue);
            captured = new(uid, chunkSize, tileSize, 0, 0, Array.Empty<KsProcgenCapturedMapTile>()); return true;
        }
        var cells = new List<KsProcgenCapturedMapTile>();
        var indices = new HashSet<Vector2i>(); var bytes = 0;
        foreach (var (key, value) in chunks.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++work.Chunks > work.Limits.MaximumChunks)
                return Reject("RoomMapChunkBudget", "Cumulative source chunks exceed the decode allowance.", out issue);
            if (value is not MappingDataNode chunk || !Coordinate(key, out var index) ||
                !chunk.TryGet<ValueDataNode>("ind", out var ind) || !Coordinate(ind.Value, out var storedIndex) ||
                index != storedIndex || !indices.Add(index))
                return Reject("InvalidRoomMapChunkIndex", "Chunk keys and unique stored indices must agree.", out issue);
            var size = 16; var version = 1;
            if (chunk.Has("size") && (!Integer(chunk, "size", out size) || size is < 1 or > 256) || size != chunkSize ||
                chunk.Has("version") && !Integer(chunk, "version", out version) || version is < 1 or > 7)
                return Reject("UnsupportedRoomMapChunkFormat", "Chunk sizes must match their grid and versions must be in 1-7.", out issue);
            var tileCount = size * size; var stride = version < 6 ? 4 : version == 6 ? 6 : 7;
            var byteCount = tileCount * stride;
            if (tileCount > work.Limits.MaximumDecodedTiles - work.Tiles)
                return Reject("RoomMapDecodedTileBudget", "Cumulative decoded cells exceed the allowance.", out issue);
            if (byteCount > work.Limits.MaximumDecodedBytes - work.Bytes)
                return Reject("RoomMapDecodedByteBudget", "Cumulative decoded payload bytes exceed the allowance.", out issue);
            var originX = (long) index.X * size; var originY = (long) index.Y * size;
            if (originX < -1_000_000 || originY < -1_000_000 || originX + size - 1 > 1_000_000 || originY + size - 1 > 1_000_000)
                return Reject("RoomMapDecodedCoordinateRange", "Decoded cells exceed supported source coordinates.", out issue);
            work.Tiles += tileCount; work.Bytes += byteCount; bytes += byteCount;
            if (!chunk.TryGet<ValueDataNode>("tiles", out var payload))
                return Reject("InvalidRoomMapChunkPayload", "A chunk requires a base64 tile payload.", out issue);
            // Allocate only the already-budgeted exact destination, never the untrusted decoded length.
            var data = new byte[byteCount];
            if (!Convert.TryFromBase64String(payload.Value, data, out var written) || written != byteCount)
                return Reject("InvalidRoomMapChunkPayload", "Chunk payload length must exactly match size and version.", out issue);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var offset = (y * size + x) * stride;
                var alias = version < 6 ? (int) BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2)) :
                    BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
                if (alias < 0 || !aliases.TryGetValue(alias, out var prototypeId))
                    return Reject("UnknownRoomMapTileAlias", "Every decoded tile must resolve through the map's tile table.", out issue);
                var flagsOffset = offset + (version < 6 ? 2 : 4);
                cells.Add(new(new((int) originX + x, (int) originY + y), prototypeId, data[flagsOffset], data[flagsOffset + 1],
                    version >= 7 ? data[flagsOffset + 2] : (byte) 0));
            }
        }
        captured = new(uid, chunkSize, tileSize, indices.Count, bytes, cells); return true;
    }

    private static bool Coordinate(string value, out Vector2i cell)
    {
        cell = default; var parts = value.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)) return false;
        cell = new(x, y); return true;
    }
    private static bool Reject(string code, string message, out KsProcgenIssue? issue)
    { issue = new(code, message); return false; }
}
