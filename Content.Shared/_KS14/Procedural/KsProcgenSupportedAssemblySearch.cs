using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed class KsProcgenSupportedAssemblySearchResult
{
    public KsProcgenSupportedAccessStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenFloorRootPose> FloorRoots { get; init; } = [];
    public IReadOnlyList<KsProcgenMemberOrientation> Orientations { get; init; } = [];
    public int AssemblyQuarterTurns { get; init; }
    public KsProcgenSupportedAccessPlan? Access { get; init; }
    public int PlacementProbes { get; init; }
    public int ExpandedCells { get; init; }
    public int LandingProbes { get; init; }
    public bool EnginePlacementVerified => false;
}

/// <summary>
/// Searches one selected complete variant, with tile-aligned floor roots and supported member turns.
/// Floor footprints stay disjoint and leave network tiles untouched. Support, operating-access,
/// adjacency, facing, corner and clean-path distance relations are handled. Shared XY never authorizes mounting.
/// </summary>
public static class KsProcgenSupportedAssemblySearch
{
    public static KsProcgenSupportedAssemblySearchResult Search(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, KsProcgenAssemblyCapabilityReport capabilities,
        KsProcgenRoomAccessMask room, int seed, IReadOnlyList<KsProcgenFloorRootPose>? fixedFloorRoots = null,
        int maximumPlacementProbes = 4096, int maximumExpandedCells = 4096, int maximumLandingProbes = 4096)
    {
        var probes = 0;
        var expandedCells = 0;
        var landingProbes = 0;
        KsProcgenSupportedAssemblySearchResult Fail(KsProcgenSupportedAccessStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Selected supported assembly has no accepted placement."),
            PlacementProbes = probes, ExpandedCells = expandedCells,
            LandingProbes = landingProbes,
        };
        if (maximumPlacementProbes is < 0 or > 65_536 || maximumExpandedCells is < 0 or > 65_536 ||
            maximumLandingProbes is < 0 or > 65_536 ||
            !KsProcgenSupportedAccessPlanner.ValidMask(room))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedSearchInput");
        var support = KsProcgenAssemblySupportPlanner.Plan(assembly, selectedMemberIds, capabilities);
        if (support.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation)
            return Fail(support.Status == KsProcgenAssemblySupportStatus.InvalidInput ?
                KsProcgenSupportedAccessStatus.InvalidInput : KsProcgenSupportedAccessStatus.Rejected, support.Issue!.Code);
        var definitions = assembly.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);
        if (assembly.Relations.Any(relation => !Enum.IsDefined(relation.Kind) || !Enum.IsDefined(relation.Severity) ||
                !Enum.IsDefined(relation.ApproachPolicy) || !definitions.ContainsKey(relation.Subject) ||
                relation.MinimumDistance < 0 || relation.MaximumDistance < relation.MinimumDistance || relation.MaximumDistance > 64 ||
                relation.Target != null && !definitions.ContainsKey(relation.Target)))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedSearchRelations");
        if (assembly.Relations.Any(relation => selectedMemberIds.Contains(relation.Subject) &&
                !KsProcgenSupportedAccessPlanner.SupportedRelation(relation.Kind)))
            return Fail(KsProcgenSupportedAccessStatus.UnsupportedContent, "SupportedSearchSpatialRelationUnsupported");
        foreach (var member in support.Members)
        {
            var definition = definitions[member.MemberId];
            if (!Enum.IsDefined(definition.RotationMode) || definition.LocalQuarterTurns is < 0 or > 3 ||
                definition.Entry.AllowedQuarterTurns.Count is < 1 or > 4 ||
                definition.Entry.AllowedQuarterTurns.Any(turn => turn is < 0 or > 3) ||
                definition.Entry.AllowedQuarterTurns.Distinct().Count() != definition.Entry.AllowedQuarterTurns.Count ||
                !Enum.IsDefined(definition.Entry.Movement) || !Enum.IsDefined(definition.Entry.Role) ||
                definition.Entry.Footprint.Count is < 1 or > 16 || !definition.Entry.Footprint.Contains(Vector2i.Zero) ||
                definition.Entry.Footprint.Distinct().Count() != definition.Entry.Footprint.Count ||
                definition.Entry.Footprint.Any(cell => Math.Abs((long) cell.X) > 8 || Math.Abs((long) cell.Y) > 8))
                return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedSearchMember");
        }
        var floorIds = support.Members.Where(member => member.Layer == KsProcgenPlacementLayer.Floor)
            .Select(member => member.MemberId).ToHashSet(StringComparer.Ordinal);
        fixedFloorRoots ??= [];
        if (fixedFloorRoots.Count > floorIds.Count ||
            fixedFloorRoots.Select(root => root.MemberId).Distinct(StringComparer.Ordinal).Count() != fixedFloorRoots.Count ||
            fixedFloorRoots.Any(root => !floorIds.Contains(root.MemberId) ||
                !AlignedCell(root.Position - room.CellCenterOrigin, out _)))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedSearchFixedRoots");
        var fixedRoots = fixedFloorRoots.ToDictionary(root => root.MemberId,
            root => root.Position - room.CellCenterOrigin, StringComparer.Ordinal);
        // The support planner supplies a deterministic parent-before-child order.
        var members = support.Members;
        var roots = new List<KsProcgenFloorRootPose>();
        var orientations = new List<KsProcgenMemberOrientation>();
        var selectedTurns = new Dictionary<string, int>(StringComparer.Ordinal);
        var occupiedFloor = new HashSet<Vector2i>();
        var cells = room.Floor.OrderBy(cell => KsProcgenFurnishingPlanner.CellRank(seed,
                "supported-assembly-search", assembly.AssemblyId + "/" + assembly.VariantId, cell))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        KsProcgenSupportedAccessPlan? accepted = null;
        KsProcgenSupportedAccessStatus? terminalStatus = null;
        string? terminalCode = null;
        var acceptedAssemblyTurn = 0;
        bool Visit(int index, int assemblyTurn)
        {
            if (index == members.Count)
            {
                var access = KsProcgenSupportedAccessPlanner.Plan(assembly, selectedMemberIds, capabilities,
                    roots, orientations, assemblyTurn, room, maximumExpandedCells: maximumExpandedCells - expandedCells,
                    maximumLandingProbes: maximumLandingProbes - landingProbes);
                expandedCells += access.ExpandedCells;
                landingProbes += access.LandingProbes;
                if (access.Status == KsProcgenSupportedAccessStatus.Candidate)
                {
                    // A surface offset can leave its floor support's reserved cells. External
                    // occupancy cannot become legal merely because an operating path exists.
                    if (access.Footprints.Any(footprint => footprint.Cells.Any(cell =>
                            room.Blocking.Contains(cell) || room.Occupied.Contains(cell))))
                        return false;
                    accepted = access;
                    acceptedAssemblyTurn = assemblyTurn;
                    return true;
                }
                // Unsupported content and malformed declarations cannot improve with another pose.
                if (access.Status is KsProcgenSupportedAccessStatus.InvalidInput or
                    KsProcgenSupportedAccessStatus.UnsupportedContent or KsProcgenSupportedAccessStatus.BudgetExceeded)
                {
                    terminalStatus = access.Status;
                    terminalCode = access.Issue!.Code;
                }
                return false;
            }
            var member = members[index];
            var definition = definitions[member.MemberId];
            var turns = definition.Entry.AllowedQuarterTurns.OrderBy(turn => turn).Where(turn =>
                (definition.RotationMode != KsProcgenMemberRotation.AssemblyRelative ||
                 turn == (assemblyTurn + definition.LocalQuarterTurns) % 4) &&
                (member.Layer != KsProcgenPlacementLayer.Container || turn == selectedTurns[member.ParentMemberId!]));
            IEnumerable<Vector2i> positions = [Vector2i.Zero];
            if (member.Layer == KsProcgenPlacementLayer.Floor)
                positions = fixedRoots.TryGetValue(member.MemberId, out var position) ?
                    [new((int) position.X, (int) position.Y)] : cells;
            foreach (var cell in positions)
            foreach (var turn in turns)
            {
                if (probes >= maximumPlacementProbes)
                {
                    terminalStatus = KsProcgenSupportedAccessStatus.BudgetExceeded;
                    terminalCode = "SupportedSearchPlacementBudget";
                    return false;
                }
                probes++;
                Vector2i[] footprint = [];
                if (member.Layer == KsProcgenPlacementLayer.Floor)
                {
                    footprint = definition.Entry.Footprint.Select(offset => cell +
                        KsProcgenInteractionFacingPlanner.RotateFootprintOffset(offset, turn)).ToArray();
                    if (footprint.Any(tile => !room.Floor.Contains(tile) || room.Walls.Contains(tile) ||
                            room.Blocking.Contains(tile) || room.Occupied.Contains(tile) ||
                            room.Network.Contains(tile) || occupiedFloor.Contains(tile)))
                        continue;
                    occupiedFloor.UnionWith(footprint);
                    roots.Add(new(member.MemberId, room.CellCenterOrigin + new Vector2((float) cell.X, (float) cell.Y)));
                }
                orientations.Add(new(member.MemberId, turn));
                selectedTurns.Add(member.MemberId, turn);
                if (Visit(index + 1, assemblyTurn))
                    return true;
                selectedTurns.Remove(member.MemberId);
                orientations.RemoveAt(orientations.Count - 1);
                if (member.Layer == KsProcgenPlacementLayer.Floor)
                {
                    roots.RemoveAt(roots.Count - 1);
                    occupiedFloor.ExceptWith(footprint);
                }
                if (terminalStatus != null)
                    return false;
            }
            return false;
        }
        for (var assemblyTurn = 0; assemblyTurn < 4; assemblyTurn++)
        {
            if (Visit(0, assemblyTurn))
                return new()
                {
                    Status = KsProcgenSupportedAccessStatus.Candidate,
                    FloorRoots = roots.AsReadOnly(), Orientations = orientations.AsReadOnly(),
                    AssemblyQuarterTurns = acceptedAssemblyTurn, Access = accepted,
                    PlacementProbes = probes, ExpandedCells = expandedCells,
                    LandingProbes = landingProbes,
                };
            if (terminalStatus != null)
                return Fail(terminalStatus.Value, terminalCode!);
        }
        return Fail(KsProcgenSupportedAccessStatus.Rejected, "SupportedSearchNoPlacement");
    }

    private static bool AlignedCell(Vector2 position, out Vector2i cell)
    {
        cell = default;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            Math.Abs(position.X) > 1_000_000f || Math.Abs(position.Y) > 1_000_000f ||
            position.X != MathF.Truncate(position.X) || position.Y != MathF.Truncate(position.Y))
            return false;
        cell = new((int) position.X, (int) position.Y);
        return true;
    }
}
