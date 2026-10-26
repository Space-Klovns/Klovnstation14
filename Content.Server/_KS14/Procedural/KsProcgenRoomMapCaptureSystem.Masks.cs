using System.Collections.Frozen;
using System.Linq;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

/// <summary>Occupied native extraction cells, never proof of clean floor, enclosure or physical ports.</summary>
public sealed class KsProcgenRoomMapNativeMask
{
    internal KsProcgenRoomMapNativeMask(string roomAssetId, string path, int gridYamlUid, Vector2i sourceOrigin,
        IEnumerable<Vector2i> cells)
    { RoomAssetId = roomAssetId; ResourcePath = path; GridYamlUid = gridYamlUid; SourceOrigin = sourceOrigin; Cells = cells.ToFrozenSet(); }
    public string RoomAssetId { get; }
    public string ResourcePath { get; }
    public int GridYamlUid { get; }
    public Vector2i SourceOrigin { get; }
    /// <summary>Native grid cells, after adding SourceOrigin to the authored room-local cells.</summary>
    public IReadOnlySet<Vector2i> Cells { get; }
}

public sealed class KsProcgenRoomMapMaskResult
{
    internal KsProcgenRoomMapMaskResult(string sourceHash, IEnumerable<KsProcgenRoomMapNativeMask> rooms, int probes)
    { SourceContentHash = sourceHash; Rooms = Array.AsReadOnly(rooms.ToArray()); CellProbes = probes; }
    public string SourceContentHash { get; }
    public IReadOnlyList<KsProcgenRoomMapNativeMask> Rooms { get; }
    public int CellProbes { get; }
}

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    /// <summary>Match room-local masks at their source origin to one selected or unambiguous unit grid.</summary>
    public bool TryReconcileRoomMasks(KsProcgenProject project, KsProcgenRoomMapManifest source,
        out KsProcgenRoomMapMaskResult? result, out KsProcgenIssue? issue,
        int maximumCellProbes = 524_288, CancellationToken cancellationToken = default)
    {
        result = null; issue = null;
        if (project == null || source == null || maximumCellProbes is < 0 or > 2_097_152)
            return Reject("InvalidRoomMapMaskInput", "Native mask matching requires captured input and a bounded cell allowance.", out issue);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var limits = source.Limits;
            var projectLimits = new KsProcgenPrototypeManifestLimits
            { MaximumNodes = limits.MaximumProjectNodes, MaximumCharacters = limits.MaximumProjectCharacters, MaximumDepth = limits.MaximumProjectDepth };
            var spec = project.CopySpec();
            var node = _serialization.WriteValue(spec, alwaysWrite: true, notNullableOverride: true);
            if (!KsProcgenPrototypeManifestSystem.TryCanonicalBody(node, projectLimits, out var canonical, out var code, out _, out _))
                return Reject(code!, "Project declarations exceed their captured structural allowance.", out issue);
            if (canonical != source.CanonicalProject)
                return Reject("RoomMapMaskProjectMismatch", "Native mask matching must use the exact captured project declaration.", out issue);
            var tiles = source.Definitions.Bodies.Where(body => body.Kind == KsProcgenPrototypeBodyKind.Tile)
                .ToDictionary(body => body.PrototypeId, body => body.RuntimeTileId, StringComparer.Ordinal);
            var maps = source.Maps.ToDictionary(map => map.ResourcePath, StringComparer.Ordinal);
            var indexes = new Dictionary<KsProcgenCapturedMapGrid, Dictionary<Vector2i, string>>();
            var masks = new List<KsProcgenRoomMapNativeMask>(); var probes = 0;
            foreach (var binding in source.Bindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var room = spec.Layouts!.Rooms[binding.RoomAssetId];
                var nativeCells = room.Cells.Select(cell => cell + room.SourceOrigin).ToArray();
                var grids = maps[binding.ResourcePath].Grids;
                if (room.SourceGridUid is { } requestedGrid && !grids.Any(grid => grid.YamlUid == requestedGrid))
                    return Reject("MissingRoomMapSelectedGrid", "The explicit source grid UID is absent from the captured map.", out issue);
                KsProcgenCapturedMapGrid? selected = null;
                foreach (var grid in grids)
                {
                    if (room.SourceGridUid is { } selectedUid && grid.YamlUid != selectedUid) continue;
                    if (grid.TileSize != 1) continue;
                    if (!indexes.TryGetValue(grid, out var index))
                    {
                        index = new();
                        foreach (var tile in grid.Tiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (probes++ >= maximumCellProbes)
                                return Reject("RoomMapMaskProbeBudget", "Native source indexing exhausted the shared cell allowance.", out issue);
                            index.Add(tile.Cell, tile.PrototypeId);
                        }
                        indexes.Add(grid, index);
                    }
                    var matches = true;
                    foreach (var cell in nativeCells)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (probes++ >= maximumCellProbes)
                            return Reject("RoomMapMaskProbeBudget", "Native mask queries exhausted the shared cell allowance.", out issue);
                        if (index.TryGetValue(cell, out var tileId) && tiles[tileId] != 0) continue;
                        matches = false; break;
                    }
                    if (!matches) continue;
                    if (selected != null)
                        return Reject("AmbiguousRoomMapSourceGrid", "Multiple native unit grids match the room's extraction mask.", out issue);
                    selected = grid;
                }
                if (selected == null)
                    return Reject("RoomMapSourceMaskMismatch", "No native unit grid contains every declared occupied extraction cell.", out issue);
                masks.Add(new(binding.RoomAssetId, binding.ResourcePath, selected.YamlUid, room.SourceOrigin, nativeCells));
            }
            result = new(source.ContentHash, masks, probes); return true;
        }
        catch (OperationCanceledException)
        { return Reject("RoomMapMaskCancelled", "Native mask matching was cancelled.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Native room-map matching failed: {exception}");
            return Reject("UnsupportedRoomMapMask", "Captured source geometry could not be matched.", out issue);
        }
    }
}
