using System.Linq;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

internal sealed record KsProcgenRoomTilePlacement(Vector2i Cell, Tile Tile);

internal sealed class KsProcgenRoomTerrainPlan(string sourceHash, string extractionHash, string roomId,
    KsProcgenPrototypeManifest definitions, IEnumerable<KsProcgenRoomTilePlacement> tiles, int sourceTileProbes)
{
    public string SourceHash { get; } = sourceHash;
    public string ExtractionHash { get; } = extractionHash;
    public string RoomId { get; } = roomId;
    public KsProcgenPrototypeManifest Definitions { get; } = definitions;
    public IReadOnlyList<KsProcgenRoomTilePlacement> Tiles { get; } = Array.AsReadOnly(tiles.ToArray());
    public int SourceTileProbes { get; } = sourceTileProbes;
}

/// <summary>Captured room terrain only. Its caller owns the complete paused-map transaction and cleanup.</summary>
public sealed partial class KsProcgenRoomTerrainSystem : EntitySystem
{
    [Dependency] private KsProcgenPreviewOperationSystem _operations = default!;
    [Dependency] private KsProcgenPrototypeManifestSystem _definitions = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    internal bool TryPrepare(KsProcgenRoomMapManifest source, KsProcgenRoomEntityExtraction extraction,
        string roomId, Vector2i origin, int quarterTurns, out KsProcgenRoomTerrainPlan? plan, out KsProcgenIssue? issue,
        int maximumTiles = 65_536, int maximumSourceTileProbes = 262_144, CancellationToken cancellationToken = default)
    {
        plan = null; issue = null;
        if (source == null || extraction == null || maximumTiles is < 0 or > 65_536 ||
            maximumSourceTileProbes is < 0 or > 1_048_576 || quarterTurns is < 0 or > 3 ||
            origin.X is < -1_000_000 or > 1_000_000 || origin.Y is < -1_000_000 or > 1_000_000)
            return Reject("InvalidRoomTerrainInput", "Terrain capture needs bounded source, placement and work inputs.", out issue);
        if (source.ContentHash != extraction.SourceContentHash)
            return Reject("RoomTerrainSourceMismatch", "Terrain and entity extraction must refer to the same captured source.", out issue);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var room = extraction.Rooms.SingleOrDefault(room => room.Room.RoomAssetId == roomId);
            var binding = source.Bindings.SingleOrDefault(binding => binding.RoomAssetId == roomId);
            if (room == null || binding == null || binding.ResourcePath != room.Room.ResourcePath)
                return Reject("RoomTerrainMissingRoom", "The room needs a matching captured source binding.", out issue);
            var mask = room.Room;
            if (mask.Cells.Count > maximumTiles)
                return Reject("RoomTerrainTileBudget", "The selected footprint exceeds the tile allowance.", out issue);
            var grid = source.Maps.Single(map => map.ResourcePath == mask.ResourcePath).Grids.Single(grid => grid.YamlUid == mask.GridYamlUid);
            if (grid.TileSize != 1)
                return Reject("RoomTerrainUnsupportedGrid", "Room terrain currently requires a native unit grid.", out issue);
            if (grid.Tiles.Count > maximumSourceTileProbes)
                return Reject("RoomTerrainProbeBudget", "The selected native unit grid exceeds its source scan allowance.", out issue);
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            var definitions = extraction.Definitions.Bodies.Where(body => body.Kind == KsProcgenPrototypeBodyKind.Tile)
                .ToDictionary(body => body.PrototypeId, StringComparer.Ordinal);
            var tiles = new List<KsProcgenRoomTilePlacement>();
            foreach (var sourceTile in grid.Tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!mask.Cells.Contains(sourceTile.Cell)) continue;
                if (!definitions.TryGetValue(sourceTile.PrototypeId, out var definition) || definition.RuntimeTileId == 0 || sourceTile.RotationMirroring > 7)
                    return Reject("RoomTerrainUnsupportedTile", "Selected cells need captured occupied tiles and native rotation/mirror codes 0-7.", out issue);
                var target = KsProcgenGeometry.TransformCell(sourceTile.Cell, mask.SourceOrigin, origin, quarterTurns);
                if (target.X is < -1_000_000 or > 1_000_000 || target.Y is < -1_000_000 or > 1_000_000)
                    return Reject("RoomTerrainTransformRange", "A transformed room tile exceeds supported coordinates.", out issue);
                var mirrored = sourceTile.RotationMirroring >= 4;
                // Native Clyde.GridRendering mirrors after rotating. Composition therefore turns
                // mirrored tiles in the opposite code direction from unmirrored tiles.
                var rotation = ((sourceTile.RotationMirroring & 3) + (mirrored ? quarterTurns : -quarterTurns) + 4) & 3;
                var rotationMirroring = (byte) (rotation + (mirrored ? 4 : 0));
                tiles.Add(new(target, new Tile((int) definition.RuntimeTileId, flags: sourceTile.Flags,
                    variant: sourceTile.Variant, rotationMirroring: rotationMirroring)));
            }
            if (tiles.Count != mask.Cells.Count)
                return Reject("RoomTerrainMaskMismatch", "Every selected source cell must have exactly one captured tile.", out issue);
            plan = new(source.ContentHash, extraction.ContentHash, roomId, extraction.Definitions,
                tiles.OrderBy(tile => tile.Cell.Y).ThenBy(tile => tile.Cell.X), grid.Tiles.Count); return true;
        }
        catch (OperationCanceledException)
        { return Reject("RoomTerrainCancelled", "Room terrain preparation was cancelled.", out issue); }
        catch (ArgumentException)
        { return Reject("RoomTerrainTransformRange", "Room terrain placement exceeds supported coordinates.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Room terrain preparation failed: {exception}");
            return Reject("RoomTerrainSourceMismatch", "The captured room grid could not be reconciled.", out issue);
        }
    }

    internal bool TryWrite(KsProcgenRoomTerrainPlan plan, EntityUid frameUid, List<EntityUid> allocations,
        int maximumAllocations, out KsProcgenIssue? issue, CancellationToken cancellationToken = default)
    {
        issue = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryFrame(frameUid, allocations, maximumAllocations, out var frame, out issue)) return false;
            var originalMapUid = Transform(frameUid).MapUid;
            var originalMapId = Transform(frameUid).MapID;
            if (!_definitions.Matches(plan.Definitions, out issue)) return false;
            foreach (var tile in plan.Tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryFrame(frameUid, allocations, maximumAllocations, out frame, out issue)) return false;
                if (Transform(frameUid).MapUid != originalMapUid || Transform(frameUid).MapID != originalMapId)
                    return Reject("RoomTerrainContextDrift", "A terrain callback moved the owned grid to another map.", out issue);
                _maps.SetTile(frame.Owner, frame.Comp, tile.Cell, tile.Tile);
            }
            if (!_definitions.Matches(plan.Definitions, out issue)) return false;
            if (!TryFrame(frameUid, allocations, maximumAllocations, out frame, out issue)) return false;
            if (Transform(frameUid).MapUid != originalMapUid || Transform(frameUid).MapID != originalMapId)
                return Reject("RoomTerrainContextDrift", "A terrain callback moved the owned grid to another map.", out issue);
            return Verify(plan, frameUid, allocations, maximumAllocations, out issue, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        { return Reject("RoomTerrainCancelled", "Terrain writing was cancelled; retire the owning transaction.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Room terrain writing failed: {exception}");
            return Reject("RoomTerrainEngineFailure", "Native terrain writing failed; retire the owning transaction.", out issue);
        }
    }

    internal bool Verify(KsProcgenRoomTerrainPlan plan, EntityUid frameUid, List<EntityUid> allocations,
        int maximumAllocations, out KsProcgenIssue? issue, CancellationToken cancellationToken = default)
    {
        issue = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryFrame(frameUid, allocations, maximumAllocations, out var frame, out issue)) return false;
        foreach (var tile in plan.Tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_maps.GetTileRef(frame.Owner, frame.Comp, tile.Cell).Tile != tile.Tile)
                return Reject("RoomTerrainDrift", "Native callbacks changed a copied room tile.", out issue);
        }
        return true;
    }

    internal bool CheckContext(EntityUid frameUid, List<EntityUid> allocations, int maximumAllocations, out KsProcgenIssue? issue) =>
        TryFrame(frameUid, allocations, maximumAllocations, out _, out issue);

    private bool TryFrame(EntityUid frameUid, List<EntityUid> allocations, int maximumAllocations,
        out Entity<MapGridComponent> frame, out KsProcgenIssue? issue)
    {
        frame = default; issue = null;
        if (maximumAllocations is < 1 or > KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage ||
            !_operations.IsCapturing(allocations) || !allocations.Contains(frameUid) || Terminating(frameUid) ||
            !TryComp(frameUid, out MapGridComponent? grid) || grid.CanSplit || grid.TileSize != 1 ||
            !TryComp(frameUid, out TransformComponent? transform) || transform.MapUid is not { } mapUid ||
            !allocations.Contains(mapUid) || Terminating(mapUid) || !_maps.TryGetMap(transform.MapID, out var actualMapUid) ||
            actualMapUid != mapUid || !_maps.IsPaused(transform.MapID) || _maps.IsInitialized(transform.MapID))
            return Reject("RoomTerrainContext", "Terrain writes require an owned paused uninitialized unsplittable grid.", out issue);
        if (allocations.Count > maximumAllocations)
            return Reject("RoomTerrainAllocationBudget", "Terrain callbacks exceeded the owning allocation allowance.", out issue);
        frame = (frameUid, grid); return true;
    }

    private static bool Reject(string code, string message, out KsProcgenIssue? issue)
    { issue = new(code, message); return false; }
}
