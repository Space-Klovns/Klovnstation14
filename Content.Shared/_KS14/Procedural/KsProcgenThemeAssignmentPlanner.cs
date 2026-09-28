using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenThemeAssignmentStatus : byte
{
    Selected,
    InvalidInput,
    ThemeRejected,
}

public sealed record KsProcgenPackMinimum(string PackId, int MinimumCount);

/// <summary>
/// One final logical region after rejected seams and open interfaces have been joined.
/// These content choices have not been placed or tested against real wall/light behavior.
/// </summary>
public sealed record KsProcgenThemedRegion(
    string Id,
    KsProcgenZoneKind Kind,
    IReadOnlyList<string> SourceZoneIds,
    IReadOnlyList<Vector2i> FloorCells,
    IReadOnlyList<Vector2i> WallCells,
    KsProcgenThemeSelection Theme,
    IReadOnlyList<KsProcgenPackMinimum> UnplacedRequiredPacks);

public sealed class KsProcgenThemeAssignmentResult
{
    public KsProcgenThemeAssignmentStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenThemedRegion> Regions { get; init; } = [];
}

/// <summary>
/// Resolves region ownership after partition fallback, then selects one coherent theme palette
/// and activity pack per actual room. Passages receive materials/light choices without furniture.
/// </summary>
public static class KsProcgenThemeAssignmentPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenThemeAssignmentResult Plan(
        IPrototypeManager prototypeManager,
        string themeId,
        int seed,
        KsProcgenPureFillResult fill,
        KsProcgenPartitionResult partition)
    {
        if (prototypeManager == null || fill == null || partition == null ||
            fill.Status != KsProcgenPureFillStatus.Proposed ||
            partition.Status == KsProcgenPartitionStatus.InvalidInput)
            return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeAssignmentInput");

        var zoneByCell = new Dictionary<Vector2i, string>();
        var zoneById = new Dictionary<string, KsProcgenZone>(StringComparer.Ordinal);
        foreach (var zone in fill.Zones)
        {
            if (string.IsNullOrWhiteSpace(zone.Id) || !zoneById.TryAdd(zone.Id, zone))
                return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeZones");
            foreach (var cell in zone.Cells)
            {
                if (!zoneByCell.TryAdd(cell, zone.Id))
                    return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeZones");
            }
        }

        var floor = new HashSet<Vector2i>(partition.FloorCells);
        var walls = new HashSet<Vector2i>(partition.WallCells);
        if (zoneByCell.Count != fill.ProceduralCells || floor.Count != partition.FloorCells.Count ||
            walls.Count != partition.WallCells.Count || floor.Overlaps(walls) ||
            floor.Count + walls.Count != zoneByCell.Count ||
            floor.Any(cell => !zoneByCell.ContainsKey(cell)) ||
            walls.Any(cell => !zoneByCell.ContainsKey(cell)))
            return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeCoverage");

        var parent = zoneById.Keys.ToDictionary(id => id, id => id, StringComparer.Ordinal);
        string Root(string id)
        {
            while (parent[id] != id)
                id = parent[id];
            return id;
        }

        void Union(string first, string second)
        {
            first = Root(first);
            second = Root(second);
            if (first == second)
                return;
            if (string.CompareOrdinal(first, second) > 0)
                (first, second) = (second, first);
            parent[second] = first;
        }

        var mergedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var merged in partition.MergedZoneGroups)
        {
            if (merged.Count < 2 || merged.Any(id => !zoneById.ContainsKey(id) || !mergedIds.Add(id)))
                return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeMergeGroups");
            foreach (var id in merged.Skip(1))
                Union(merged[0], id);
        }

        var initialRoots = zoneById.Keys.ToDictionary(id => id, Root, StringComparer.Ordinal);
        var doors = new HashSet<(string First, string Second)>();
        foreach (var door in partition.DoorOpenings)
        {
            if (!zoneById.ContainsKey(door.FirstZoneId) || !zoneById.ContainsKey(door.SecondZoneId) ||
                !floor.Contains(door.Threshold) || !floor.Contains(door.InsideApproach) ||
                !floor.Contains(door.OutsideApproach))
                return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeDoor");
            var first = initialRoots[door.FirstZoneId];
            var second = initialRoots[door.SecondZoneId];
            if (first == second || !doors.Add(Pair(first, second)))
                return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeDoor");
        }

        // Interfaces without a partition door are one open space for theme purposes.
        var openPairs = new HashSet<(string First, string Second)>();
        foreach (var (cell, zoneId) in zoneByCell)
        foreach (var offset in Cardinal)
        {
            if (!zoneByCell.TryGetValue(cell + offset, out var otherId))
                continue;
            var first = initialRoots[zoneId];
            var second = initialRoots[otherId];
            if (first != second && !doors.Contains(Pair(first, second)))
                openPairs.Add(Pair(first, second));
        }

        foreach (var (first, second) in openPairs.OrderBy(pair => pair.First, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Second, StringComparer.Ordinal))
            Union(first, second);
        if (doors.Any(pair => Root(pair.First) == Root(pair.Second)))
            return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "InvalidThemeTopology");

        if (!KsProcgenThemeValidator.TryResolve(prototypeManager, themeId, out var theme, out var issue))
            return new KsProcgenThemeAssignmentResult
            {
                Status = KsProcgenThemeAssignmentStatus.ThemeRejected,
                Issue = issue,
            };

        var regions = new List<KsProcgenThemedRegion>();
        foreach (var group in fill.Zones.GroupBy(zone => Root(zone.Id))
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ids = group.Select(zone => zone.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var cells = new HashSet<Vector2i>(group.SelectMany(zone => zone.Cells));
            var regionFloor = KsProcgenGeometry.SortCells(cells.Where(floor.Contains));
            if (regionFloor.Count == 0)
                return Failure(KsProcgenThemeAssignmentStatus.InvalidInput, "EmptyThemedRegion");
            var kind = group.Any(zone => zone.Kind == KsProcgenZoneKind.Passage)
                ? KsProcgenZoneKind.Passage : KsProcgenZoneKind.RoomProposal;
            if (kind == KsProcgenZoneKind.Passage && theme!.EntityPacks.Any(pack => pack.MinimumCount > 0))
                return Failure(KsProcgenThemeAssignmentStatus.ThemeRejected, "MandatoryPackInPassage");

            // Shared passages use the same material stream so their open seams remain coherent.
            var selectionId = kind == KsProcgenZoneKind.Passage ? "<passage>" : string.Join("+", ids);
            if (!KsProcgenThemeSelector.TrySelect(prototypeManager, themeId, seed, selectionId,
                    out var selection, out issue))
                return new KsProcgenThemeAssignmentResult
                {
                    Status = KsProcgenThemeAssignmentStatus.ThemeRejected,
                    Issue = issue,
                };
            if (kind == KsProcgenZoneKind.Passage)
                selection = selection! with { DominantEntityPackId = null, SupportingEntityPackIds = [] };

            var requiredPacks = theme!.EntityPacks.Where(pack => pack.MinimumCount > 0)
                .OrderBy(pack => pack.Pack, StringComparer.Ordinal)
                .Select(pack => new KsProcgenPackMinimum(pack.Pack, pack.MinimumCount)).ToArray();
            regions.Add(new KsProcgenThemedRegion(group.Key, kind, ids, regionFloor,
                KsProcgenGeometry.SortCells(cells.Where(walls.Contains)), selection!, requiredPacks));
        }

        return new KsProcgenThemeAssignmentResult
        {
            Status = KsProcgenThemeAssignmentStatus.Selected,
            Regions = regions,
        };
    }

    private static (string First, string Second) Pair(string first, string second) =>
        string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);

    private static KsProcgenThemeAssignmentResult Failure(KsProcgenThemeAssignmentStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Theme assignment could not satisfy the partition or theme contract."),
    };
}
