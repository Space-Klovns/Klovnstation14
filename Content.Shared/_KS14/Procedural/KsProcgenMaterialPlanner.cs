using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenMaterialStatus : byte
{
    Planned,
    InvalidInput,
    NoCompatibleDoor,
}

public sealed record KsProcgenTileChoice(Vector2i Cell, string RegionId, string TileId, bool Accent);
public sealed record KsProcgenWallChoice(Vector2i Cell, string RegionId, string EntityId);
public sealed record KsProcgenDoorChoice(Vector2i Cell, string RegionId, string EntityId);

/// <summary>
/// Exact prototype choices only. Placement, exterior hull, gas closure, and fixture checks follow.
/// </summary>
public sealed class KsProcgenMaterialResult
{
    public KsProcgenMaterialStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenTileChoice> Tiles { get; init; } = [];
    public IReadOnlyList<KsProcgenWallChoice> InteriorWalls { get; init; } = [];
    public IReadOnlyList<KsProcgenDoorChoice> InteriorDoors { get; init; } = [];
    public int AccentShortfall { get; init; }
}

public static class KsProcgenMaterialPlanner
{
    public static KsProcgenMaterialResult Plan(
        IPrototypeManager prototypeManager,
        KsProcgenThemeAssignmentResult assignment,
        KsProcgenPartitionResult partition,
        int seed)
    {
        if (prototypeManager == null || assignment == null || partition == null ||
            assignment.Status != KsProcgenThemeAssignmentStatus.Selected ||
            partition.Status == KsProcgenPartitionStatus.InvalidInput)
            return Invalid("InvalidMaterialInput");

        var expectedFloor = new HashSet<Vector2i>(partition.FloorCells);
        var expectedWalls = new HashSet<Vector2i>(partition.WallCells);
        if (expectedFloor.Count != partition.FloorCells.Count ||
            expectedWalls.Count != partition.WallCells.Count || expectedFloor.Overlaps(expectedWalls))
            return Invalid("InvalidMaterialCoverage");

        var doorwayCells = new HashSet<Vector2i>(partition.DoorOpenings.Select(door => door.Threshold));
        if (doorwayCells.Any(cell => !expectedFloor.Contains(cell)))
            return Invalid("InvalidMaterialDoorway");

        var plannedFloor = new HashSet<Vector2i>();
        var plannedWalls = new HashSet<Vector2i>();
        var tiles = new List<KsProcgenTileChoice>();
        var walls = new List<KsProcgenWallChoice>();
        var doorPrototypes = new Dictionary<string, string?>(StringComparer.Ordinal);
        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        var accentShortfall = 0;
        foreach (var region in assignment.Regions.OrderBy(region => region.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(region.Id) || !regionIds.Add(region.Id) ||
                !prototypeManager.TryIndex<KsProcgenTilePackPrototype>(region.Theme.TilePackId, out var tilePack) ||
                !prototypeManager.TryIndex<KsProcgenWallPackPrototype>(region.Theme.WallPackId, out var wallPack))
                return Invalid("InvalidMaterialSelection");

            var palette = tilePack.Palettes.SingleOrDefault(candidate => candidate.Id == region.Theme.TilePaletteId);
            var family = wallPack.Families.SingleOrDefault(candidate => candidate.Id == region.Theme.WallFamilyId);
            if (palette == null || family == null || string.IsNullOrWhiteSpace(palette.PrimaryTile) ||
                string.IsNullOrWhiteSpace(family.InteriorWall) || !float.IsFinite(palette.AccentFraction) ||
                palette.AccentFraction is < 0f or > 1f)
                return Invalid("InvalidMaterialSelection");
            doorPrototypes.Add(region.Id, family.Door);

            var regionFloor = KsProcgenGeometry.SortCells(region.FloorCells);
            var regionWalls = KsProcgenGeometry.SortCells(region.WallCells);
            foreach (var cell in regionFloor)
            {
                if (!expectedFloor.Contains(cell) || !plannedFloor.Add(cell))
                    return Invalid("InvalidMaterialCoverage");
            }
            foreach (var cell in regionWalls)
            {
                if (!expectedWalls.Contains(cell) || !plannedWalls.Add(cell))
                    return Invalid("InvalidMaterialCoverage");
            }

            var targetAccents = palette.AccentTile == null || region.Kind == KsProcgenZoneKind.Passage
                ? 0 : (int) Math.Round(regionFloor.Count * palette.AccentFraction,
                    MidpointRounding.AwayFromZero);
            var candidates = regionFloor.Where(cell => !doorwayCells.Contains(cell))
                .Select(cell => (Cell: cell, Score: AccentScore(seed, region.Id, cell)))
                .OrderBy(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Cell.Y)
                .ThenBy(candidate => candidate.Cell.X).ToArray();
            var accents = new HashSet<Vector2i>(candidates.Take(targetAccents).Select(candidate => candidate.Cell));
            accentShortfall += Math.Max(0, targetAccents - accents.Count);
            foreach (var cell in regionFloor)
            {
                var accent = accents.Contains(cell);
                tiles.Add(new KsProcgenTileChoice(cell, region.Id,
                    accent ? palette.AccentTile! : palette.PrimaryTile, accent));
            }
            foreach (var cell in regionWalls)
                walls.Add(new KsProcgenWallChoice(cell, region.Id, family.InteriorWall));
        }

        if (plannedFloor.Count != expectedFloor.Count || plannedWalls.Count != expectedWalls.Count)
            return Invalid("InvalidMaterialCoverage");

        var floorOwners = tiles.ToDictionary(tile => tile.Cell, tile => tile.RegionId);
        var doorChoices = new List<KsProcgenDoorChoice>();
        foreach (var doorway in partition.DoorOpenings.OrderBy(door => door.Threshold.Y)
                     .ThenBy(door => door.Threshold.X))
        {
            if (!floorOwners.TryGetValue(doorway.Threshold, out var owner) ||
                !doorPrototypes.TryGetValue(owner, out var prototypeId) ||
                string.IsNullOrWhiteSpace(prototypeId))
                return new KsProcgenMaterialResult
                {
                    Status = KsProcgenMaterialStatus.NoCompatibleDoor,
                    Issue = new KsProcgenIssue("MissingInteriorDoorMaterial",
                        "The selected wall family has no interior door for a required opening."),
                };
            doorChoices.Add(new KsProcgenDoorChoice(doorway.Threshold, owner, prototypeId));
        }

        return new KsProcgenMaterialResult
        {
            Status = KsProcgenMaterialStatus.Planned,
            Tiles = tiles.OrderBy(tile => tile.Cell.Y).ThenBy(tile => tile.Cell.X).ToArray(),
            InteriorWalls = walls.OrderBy(wall => wall.Cell.Y).ThenBy(wall => wall.Cell.X).ToArray(),
            InteriorDoors = doorChoices,
            AccentShortfall = accentShortfall,
        };
    }

    private static ulong AccentScore(int seed, string regionId, Vector2i cell)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(seed);
        hash.AddString("theme/accent-cells");
        hash.AddString(regionId);
        hash.AddInt(cell.X);
        hash.AddInt(cell.Y);
        return hash.Value;
    }

    private static KsProcgenMaterialResult Invalid(string code) => new()
    {
        Status = KsProcgenMaterialStatus.InvalidInput,
        Issue = new KsProcgenIssue(code, "Material choices do not match the accepted region geometry."),
    };
}
