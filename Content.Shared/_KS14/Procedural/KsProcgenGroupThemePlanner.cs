using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

/// <summary>Resolve each owned partition separately and retain its contract and route reservations.</summary>
public static class KsProcgenGroupThemePlanner
{
    public static KsProcgenThemeAssignmentResult Plan(IPrototypeManager prototypeManager, string themeId,
        int seed, KsProcgenGroupPartitionResult partition)
    {
        if (partition == null || partition.Status != KsProcgenEntranceSceneStatus.Candidate || partition.PartitionHash == 0 ||
            partition.Analysis?.Status != KsProcgenEntranceSceneStatus.Candidate || partition.Scene == null || partition.Regions.Count == 0 ||
            partition.Regions.Select(network => network.Id).Distinct(StringComparer.Ordinal).Count() != partition.Regions.Count ||
            !partition.Scene.CleanDomainCells.SetEquals(partition.FloorCells) ||
            !partition.Scene.CleanDomainCells.SetEquals(partition.Regions.SelectMany(network => network.Partition.FloorCells)) ||
            partition.Regions.Any(network => network.GroupIds.Count == 0 || network.Partition.FloorCells.Count == 0 ||
                network.ProtectedCells.Any(cell => !network.Partition.FloorCells.Contains(cell)) ||
                !partition.Analysis.Components.Any(component => component.Cells.SequenceEqual(network.Partition.FloorCells) &&
                    component.GroupIds.SequenceEqual(network.GroupIds))))
            return new() { Status = KsProcgenThemeAssignmentStatus.InvalidInput,
                Issue = new("InvalidGroupThemeInput", "A validated owned partition is required.") };
        var regions = new List<KsProcgenThemedRegion>();
        foreach (var network in partition.Regions)
        {
            var themes = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager, themeId, seed, network.Fill, network.Partition);
            if (themes.Status != KsProcgenThemeAssignmentStatus.Selected)
                return new() { Status = themes.Status, Issue = themes.Issue };
            foreach (var region in themes.Regions)
                regions.Add(region with
                {
                    Theme = region.Theme with { SupportingEntityPackIds = KsProcgenReadOnly.Copy(region.Theme.SupportingEntityPackIds) },
                    EntranceGroupIds = KsProcgenReadOnly.Copy(network.GroupIds),
                    ProtectedPassageCells = KsProcgenGeometry.SortCells(region.FloorCells.Where(network.ProtectedCells.Contains)),
                    SourceZoneIds = KsProcgenReadOnly.Copy(region.SourceZoneIds),
                    UnplacedRequiredPacks = KsProcgenReadOnly.Copy(region.UnplacedRequiredPacks),
                });
        }
        return new() { Status = KsProcgenThemeAssignmentStatus.Selected, Regions = regions.ToReadOnlyList() };
    }
}
