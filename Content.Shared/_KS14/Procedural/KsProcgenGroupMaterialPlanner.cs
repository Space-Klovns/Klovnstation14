using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Content.Shared.Maps;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenGroupMaterialStatus : byte
{
    Planned,
    InvalidInput,
    IncompatibleMaterials,
    BudgetExceeded,
}

/// <summary>Exact writable-cell choices; read-only floor and host context are never emitted.</summary>
public sealed class KsProcgenGroupMaterialResult
{
    public KsProcgenGroupMaterialStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenTileChoice> Tiles { get; init; } = [];
    public IReadOnlyList<KsProcgenWallChoice> Walls { get; init; } = [];
    public IReadOnlyList<KsProcgenDoorChoice> Doors { get; init; } = [];
    public IReadOnlyList<KsProcgenPartitionDoor> DoorOpenings { get; init; } = [];
    public IReadOnlySet<Vector2i> SeparationWalls { get; init; } = FrozenSet<Vector2i>.Empty;
    public int UnattachedWallCells { get; init; }
    public int AccentShortfall { get; init; }
    public int ExpandedCells { get; init; }
    public ulong MaterialHash { get; init; }
    public bool EngineMaterialsVerified => false;
    public bool GasClosureVerified => false;
}

/// <summary>
/// Resolves owned partitions independently, then checks complete shared wall components.
/// All incident room families must agree on their shared wall prototype. Disconnected unused
/// wall islands use the first canonical region's wall as an explicit deterministic fallback.
/// Geometry and prototypes remain proposals; no collision, operation or gas properties are inferred.
/// </summary>
public static class KsProcgenGroupMaterialPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenGroupMaterialResult Plan(IPrototypeManager prototypeManager, KsProcgenRequest request,
        KsProcgenGroupPartitionResult partition, KsProcgenThemeAssignmentResult themes,
        int maximumExpandedCells = 262_144)
    {
        var expanded = 0;
        KsProcgenGroupMaterialResult Fail(KsProcgenGroupMaterialStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Owned material choices did not validate."), ExpandedCells = expanded,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenGroupMaterialStatus.InvalidInput, Issue = issue };
        if (prototypeManager == null || partition == null || partition.Status != KsProcgenEntranceSceneStatus.Candidate ||
            partition.Scene == null || partition.Analysis?.Status != KsProcgenEntranceSceneStatus.Candidate ||
            partition.PartitionHash == 0 || shape!.EntranceContract == null ||
            themes?.Status != KsProcgenThemeAssignmentStatus.Selected || themes.Regions.Count is < 1 or > 256 ||
            partition.Regions.Count is < 1 or > 64 || shape.TargetCells.Count > 4_096 ||
            partition.FloorCells.Count > 4_096 || partition.WallCells.Count > 4_096 || partition.ProtectedCells.Count > 4_096 ||
            maximumExpandedCells is < 0 or > 1_000_000)
            return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidGroupMaterialInput");
        var analysis = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            partition.Analysis.ConfigurationId!, partition.Scene, Math.Min(131_072, maximumExpandedCells));
        expanded += analysis.ExpandedCells;
        if (analysis.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(analysis.Status == KsProcgenEntranceSceneStatus.BudgetExceeded
                ? KsProcgenGroupMaterialStatus.BudgetExceeded : KsProcgenGroupMaterialStatus.InvalidInput, analysis.Issue!.Code);
        var floor = partition.FloorCells.ToHashSet();
        var walls = partition.WallCells.ToHashSet();
        if (analysis.SceneHash != partition.Analysis.SceneHash || floor.Count != partition.FloorCells.Count ||
            !floor.SetEquals(partition.Scene.CleanDomainCells) || walls.Count != partition.WallCells.Count ||
            !walls.SetEquals(shape.TargetCells.Where(cell => shape.CanWrite(cell) && !floor.Contains(cell))) ||
            !partition.ProtectedCells.IsSubsetOf(floor))
            return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "StaleGroupMaterialSource");
        var ownerByCell = analysis.Components.SelectMany(component => component.Cells.Select(cell => (cell, component)))
            .ToDictionary(entry => entry.cell, entry => entry.component);
        var regionByFloor = new Dictionary<Vector2i, KsProcgenThemedRegion>();
        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        var themedWalls = new HashSet<Vector2i>();
        foreach (var region in themes.Regions)
        {
            if (region == null || region.Theme == null || !Enum.IsDefined(region.Kind) ||
                string.IsNullOrWhiteSpace(region.Id) || !regionIds.Add(region.Id) ||
                region.FloorCells.Count is < 1 or > 4_096 || region.WallCells.Count > 4_096 ||
                region.ProtectedPassageCells.Count > 4_096 || region.EntranceGroupIds.Count > 64 ||
                !ownerByCell.TryGetValue(region.FloorCells[0], out var owner) ||
                region.EntranceGroupIds.Count != owner.GroupIds.Count ||
                !region.EntranceGroupIds.ToHashSet(StringComparer.Ordinal).SetEquals(owner.GroupIds) ||
                !region.ProtectedPassageCells.ToHashSet().SetEquals(region.FloorCells.Where(partition.ProtectedCells.Contains)))
                return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidMaterialOwnership");
            foreach (var cell in region.FloorCells)
            {
                if (!ownerByCell.TryGetValue(cell, out var actual) || actual.Component != owner.Component ||
                    !regionByFloor.TryAdd(cell, region))
                    return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidMaterialOwnership");
            }
            foreach (var cell in region.WallCells)
                if (!walls.Contains(cell) || !themedWalls.Add(cell))
                    return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidThemedWallCoverage");
        }
        if (!floor.SetEquals(regionByFloor.Keys))
            return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "IncompleteMaterialFloorCoverage");

        var tiles = new List<KsProcgenTileChoice>();
        var wallChoices = new List<KsProcgenWallChoice>();
        var doors = new List<KsProcgenDoorChoice>();
        var localFloor = new HashSet<Vector2i>();
        var localWalls = new HashSet<Vector2i>();
        var doorCells = new HashSet<Vector2i>();
        var networkIds = new HashSet<string>(StringComparer.Ordinal);
        var materialByRegion = new Dictionary<string, string>(StringComparer.Ordinal);
        var shortfall = 0;
        foreach (var network in partition.Regions.OrderBy(network => network.Id, StringComparer.Ordinal))
        {
            if (network == null || !networkIds.Add(network.Id) || network.Partition == null ||
                network.Partition.Status is KsProcgenPartitionStatus.InvalidInput or KsProcgenPartitionStatus.BudgetExceeded or KsProcgenPartitionStatus.RequiredRoomRejected ||
                network.GroupIds.Count > 64 || network.ProtectedCells.Count > 4_096 ||
                network.Partition.FloorCells.Count is < 1 or > 4_096 || network.Partition.WallCells.Count > 4_096 ||
                network.Partition.DoorOpenings.Count > 4_096 ||
                !ownerByCell.TryGetValue(network.Partition.FloorCells[0], out var owner) ||
                !network.GroupIds.ToHashSet(StringComparer.Ordinal).SetEquals(owner.GroupIds) ||
                network.GroupIds.Count != owner.GroupIds.Count ||
                !network.ProtectedCells.ToHashSet().SetEquals(network.Partition.FloorCells.Where(partition.ProtectedCells.Contains)))
                return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidMaterialNetwork");
            foreach (var cell in network.Partition.FloorCells)
                if (!ownerByCell.TryGetValue(cell, out var actual) || actual.Component != owner.Component || !localFloor.Add(cell))
                    return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidMaterialNetwork");
            foreach (var cell in network.Partition.WallCells)
                if (!themedWalls.Contains(cell) || !localWalls.Add(cell))
                    return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidMaterialNetwork");
            foreach (var door in network.Partition.DoorOpenings)
            {
                if (!shape.CanWrite(door.Threshold) || !doorCells.Add(door.Threshold) ||
                    !ownerByCell.TryGetValue(door.Threshold, out var thresholdOwner) || thresholdOwner.Component != owner.Component ||
                    !ownerByCell.TryGetValue(door.InsideApproach, out var insideOwner) || insideOwner.Component != owner.Component ||
                    !ownerByCell.TryGetValue(door.OutsideApproach, out var outsideOwner) || outsideOwner.Component != owner.Component ||
                    !Cardinal.Contains(door.InsideApproach - door.Threshold) ||
                    door.OutsideApproach - door.Threshold != door.Threshold - door.InsideApproach)
                    return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidOwnedMaterialDoor");
            }
            var networkThemes = themes.Regions.Where(region => ownerByCell[region.FloorCells[0]].Component == owner.Component).ToArray();
            var primary = network.Partition.FloorCells.Where(cell => partition.ProtectedCells.Contains(cell) || !shape.CanWrite(cell)).ToFrozenSet();
            var material = KsProcgenMaterialPlanner.Plan(prototypeManager,
                new() { Status = KsProcgenThemeAssignmentStatus.Selected, Regions = networkThemes }, network.Partition,
                request.Seed, protectedPrimaryCells: primary);
            if (material.Status != KsProcgenMaterialStatus.Planned)
                return Fail(material.Status == KsProcgenMaterialStatus.NoCompatibleDoor
                    ? KsProcgenGroupMaterialStatus.IncompatibleMaterials : KsProcgenGroupMaterialStatus.InvalidInput, material.Issue!.Code);
            if (material.Tiles.Any(tile => !prototypeManager.TryIndex<ContentTileDefinition>(tile.TileId, out _)) ||
                material.InteriorWalls.Any(wall => !prototypeManager.TryIndex<EntityPrototype>(wall.EntityId, out _)) ||
                material.InteriorDoors.Any(door => !prototypeManager.TryIndex<EntityPrototype>(door.EntityId, out _)))
                return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "MissingOwnedMaterialPrototype");
            tiles.AddRange(material.Tiles.Where(tile => shape.CanWrite(tile.Cell)));
            wallChoices.AddRange(material.InteriorWalls);
            doors.AddRange(material.InteriorDoors);
            shortfall += material.AccentShortfall;
            foreach (var region in networkThemes)
            {
                if (!KsProcgenSelectedContent.TryWall(prototypeManager, region.Theme, out var family))
                    return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "MissingOwnedMaterialPrototype");
                var wallId = family!.InteriorWall;
                if (!prototypeManager.TryIndex<EntityPrototype>(wallId, out _))
                    return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "MissingOwnedMaterialPrototype");
                materialByRegion.Add(region.Id, wallId);
            }
        }
        if (!localFloor.SetEquals(floor) || !localWalls.SetEquals(themedWalls))
            return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "IncompleteMaterialNetworkCoverage");

        var separation = walls.Except(themedWalls).ToHashSet();
        var existingWallOwners = wallChoices.ToDictionary(wall => wall.Cell, wall => wall.RegionId);
        var remaining = walls.ToHashSet();
        var unattached = 0;
        foreach (var start in KsProcgenGeometry.SortCells(walls))
        {
            if (!remaining.Remove(start))
                continue;
            var cells = new List<Vector2i>();
            var incidentRegions = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Vector2i>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var cell))
            {
                if (expanded >= maximumExpandedCells)
                    return Fail(KsProcgenGroupMaterialStatus.BudgetExceeded, "GroupMaterialExpansionBudget");
                expanded++;
                cells.Add(cell);
                if (existingWallOwners.TryGetValue(cell, out var wallOwner)) incidentRegions.Add(wallOwner);
                foreach (var offset in Cardinal)
                {
                    var neighbor = cell + offset; // Normalized coordinates are bounded far below integer limits.
                    if (regionByFloor.TryGetValue(neighbor, out var incident)) incidentRegions.Add(incident.Id);
                    if (remaining.Remove(neighbor)) queue.Enqueue(neighbor);
                }
            }
            if (incidentRegions.Count == 0)
            {
                unattached += cells.Count;
                incidentRegions.Add(regionIds.OrderBy(id => id, StringComparer.Ordinal).First());
            }
            if (incidentRegions.Select(id => materialByRegion[id]).Distinct(StringComparer.Ordinal).Count() != 1)
                return Fail(KsProcgenGroupMaterialStatus.IncompatibleMaterials,
                    cells.Any(separation.Contains) ? "ConflictingSeparationWallMaterials" : "ConflictingInteriorWallMaterials");
            var regionId = incidentRegions.OrderBy(id => id, StringComparer.Ordinal).First();
            foreach (var cell in cells.Where(separation.Contains)) wallChoices.Add(new(cell, regionId, materialByRegion[regionId]));
        }
        if (tiles.Count != floor.Count(shape.CanWrite) || wallChoices.Count != walls.Count || doors.Count != doorCells.Count)
            return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "IncompleteOwnedMaterials");
        var orderedTiles = tiles.OrderBy(tile => tile.Cell.Y).ThenBy(tile => tile.Cell.X).ToReadOnlyList();
        var orderedWalls = wallChoices.OrderBy(wall => wall.Cell.Y).ThenBy(wall => wall.Cell.X).ToReadOnlyList();
        var orderedDoors = doors.OrderBy(door => door.Cell.Y).ThenBy(door => door.Cell.X).ToReadOnlyList();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-material-v1");
        hash.AddString(partition.PartitionHash.ToString(CultureInfo.InvariantCulture));
        hash.AddString(analysis.SceneHash.ToString(CultureInfo.InvariantCulture));
        var openings = partition.Regions.SelectMany(network => network.Partition.DoorOpenings)
            .OrderBy(door => door.Threshold.Y).ThenBy(door => door.Threshold.X).ToArray();
        hash.AddInt(openings.Length);
        foreach (var opening in openings)
        {
            hash.AddString(opening.FirstZoneId); hash.AddString(opening.SecondZoneId);
            AddCell(opening.Threshold); AddCell(opening.InsideApproach); AddCell(opening.OutsideApproach);
        }
        hash.AddInt(shortfall); hash.AddInt(unattached);
        hash.AddInt(orderedTiles.Count);
        foreach (var tile in orderedTiles)
        {
            AddCell(tile.Cell); hash.AddString(tile.RegionId); hash.AddString(tile.TileId); hash.AddInt(tile.Accent ? 1 : 0);
        }
        hash.AddInt(orderedWalls.Count);
        foreach (var wall in orderedWalls)
        {
            AddCell(wall.Cell); hash.AddString(wall.RegionId); hash.AddString(wall.EntityId);
            hash.AddInt(separation.Contains(wall.Cell) ? 1 : 0);
        }
        hash.AddInt(orderedDoors.Count);
        foreach (var door in orderedDoors) { AddCell(door.Cell); hash.AddString(door.RegionId); hash.AddString(door.EntityId); }
        void AddCell(Vector2i cell) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        return new()
        {
            Status = KsProcgenGroupMaterialStatus.Planned, Tiles = orderedTiles, Walls = orderedWalls, Doors = orderedDoors,
            DoorOpenings = KsProcgenReadOnly.Copy(openings),
            SeparationWalls = separation.ToFrozenSet(), UnattachedWallCells = unattached, AccentShortfall = shortfall,
            ExpandedCells = expanded, MaterialHash = hash.Value,
        };
    }
}
