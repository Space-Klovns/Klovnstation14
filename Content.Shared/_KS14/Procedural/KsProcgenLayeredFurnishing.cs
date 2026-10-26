using System.Collections.Frozen;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Complete supported room witness. Floor projections are not surface/container entity poses.</summary>
public sealed class KsProcgenLayeredFurnishing
{
    internal KsProcgenLayeredFurnishing(KsProcgenRoomCoreSearchResult result, KsProcgenRoomAccessMask room,
        IReadOnlyList<KsProcgenEntityProposal> floorEntities)
    {
        Composition = result.Composition!;
        Assembly = Composition.Assembly!; Capabilities = result.Capabilities!;
        Bindings = Composition.Bindings;
        Placement = result.Placement!; Access = Placement.Access!;
        Room = new(room.Floor.ToFrozenSet(), room.Walls.ToFrozenSet(), room.Blocking.ToFrozenSet(), room.Occupied.ToFrozenSet(), room.Network.ToFrozenSet(), room.CellCenterOrigin);
        FloorEntities = floorEntities;
        OccupiedCells = KsProcgenGeometry.SortCells(Access.Footprints.SelectMany(footprint => footprint.Cells).Distinct());
        var definitions = Assembly.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);
        BlockedCells = KsProcgenGeometry.SortCells(Access.Footprints.Where(footprint => definitions[footprint.MemberId].Entry.Movement != KsProcgenMovementClass.Clear)
            .SelectMany(footprint => footprint.Cells).Distinct());
        ApproachCells = KsProcgenGeometry.SortCells(Access.Access.Select(access => access.Approach).Distinct());
    }
    public KsProcgenResolvedAssembly Assembly { get; }
    public KsProcgenRoomAssemblyComposition Composition { get; }
    public KsProcgenAssemblyCapabilityReport Capabilities { get; }
    public IReadOnlyList<KsProcgenRoomMemberBinding> Bindings { get; }
    public KsProcgenSupportedAssemblySearchResult Placement { get; }
    public KsProcgenSupportedAccessPlan Access { get; }
    public KsProcgenRoomAccessMask Room { get; }
    public IReadOnlyList<KsProcgenEntityProposal> FloorEntities { get; }
    public IReadOnlyList<Vector2i> OccupiedCells { get; }
    public IReadOnlyList<Vector2i> BlockedCells { get; }
    public IReadOnlyList<Vector2i> ApproachCells { get; }
    public bool EnginePlacementVerified => false;
}

/// <summary>Uses captured cores in the owned candidate; omissions roll back whole optional instances.</summary>
internal static partial class KsProcgenLayeredFurnishingPlanner
{
    internal static IReadOnlySet<Vector2i> SupportWalls(IReadOnlySet<Vector2i> floor, IEnumerable<Vector2i> walls) =>
        walls.Where(cell => floor.Contains(cell + new Vector2i(0, -1)) || floor.Contains(cell + new Vector2i(-1, 0)) ||
            floor.Contains(cell + new Vector2i(1, 0)) || floor.Contains(cell + new Vector2i(0, 1))).ToFrozenSet();

    internal static bool Needed(KsProcgenThemedRegion region) => region.Kind == KsProcgenZoneKind.RoomProposal &&
        SelectedPacks(region).Any(pack => pack.Cores.Count > 0);

    private static IEnumerable<KsProcgenSelectedEntityPack> SelectedPacks(KsProcgenThemedRegion region)
    {
        var ids = region.Theme.SupportingEntityPackIds.Concat(region.Theme.DominantEntityPackId is { } dominant ? new[] { dominant } : [])
            .Concat(region.UnplacedRequiredPacks.Select(pack => pack.PackId)).ToHashSet(StringComparer.Ordinal);
        return region.Theme.Content?.EntityPacks.Where(pack => ids.Contains(pack.Id)) ?? [];
    }

}
