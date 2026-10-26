using System.Linq;

namespace Content.Shared._KS14.Procedural;

public static partial class KsProcgenGroupWindowPlanner
{
    /// <summary>Classify actual candidate faces against a declared fresh-map exterior, after partitioning.</summary>
    private static string? EmptyExteriorFacts(KsProcgenRequest request, KsProcgenGroupPartitionResult partition,
        KsProcgenGroupHullBoundaryResult hull, int maximumWork,
        out IReadOnlyList<KsProcgenExteriorWindowFact> facts, out int work)
    {
        facts = []; work = 0;
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out _) ||
            shape!.ConstantRegions.Count != 0 || shape.PreservedCells.Count != 0 || shape.Entrances.Count != 0 ||
            partition.Scene!.CleanHostCells.Count != 0 || partition.Scene.CleanDomainCells.Any(cell => !shape.ContainsTarget(cell)))
            return "ConflictingEmptyExteriorContext";
        var resolved = new List<KsProcgenExteriorWindowFact>();
        foreach (var group in hull.Edges.GroupBy(edge => edge.BoundaryCell).OrderBy(group => group.Key.Y).ThenBy(group => group.Key.X))
        {
            if (work >= maximumWork) return "EmptyExteriorClassificationBudget";
            work++;
            if (group.Any(edge => edge.Kind is not (KsProcgenOwnedBoundaryKind.ProposedTargetWall or KsProcgenOwnedBoundaryKind.WritableEnvelope)))
                continue;
            var edges = group.OrderBy(edge => edge.FloorCell.Y).ThenBy(edge => edge.FloorCell.X).ToArray();
            var exterior = edges.FirstOrDefault(edge =>
            {
                var outside = edge.BoundaryCell + (edge.BoundaryCell - edge.FloorCell);
                return !shape.ContainsTarget(outside) && !shape.ContainsEnvelope(outside) && !shape.ContainsPreserved(outside);
            });
            var selected = exterior ?? edges[0];
            // Eligibility remains a geometric/prototype proposal. Only live validation can prove a seal.
            resolved.Add(new(group.Key, selected.BoundaryCell - selected.FloorCell, exterior != null, exterior != null));
        }
        facts = KsProcgenReadOnly.Copy(resolved);
        return null;
    }
}
