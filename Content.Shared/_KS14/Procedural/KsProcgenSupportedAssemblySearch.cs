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
    public KsProcgenPreferenceScore PreferenceScore { get; init; }
    public bool PreferenceSearchTruncated { get; init; }
    public IReadOnlyList<KsProcgenRootDistanceOutcome> RootPreferenceOutcomes { get; init; } = [];
    public bool EnginePlacementVerified => false;
}

/// <summary>
/// Searches one selected complete variant, with tile-aligned floor roots and supported member turns.
/// Floor footprints stay disjoint and leave network tiles untouched. Support, operating-access,
/// adjacency, facing, corner and clean-path distance relations are handled. Shared XY never authorizes mounting.
/// </summary>
public static class KsProcgenSupportedAssemblySearch
{
    private static readonly Vector2i[] Cardinal = [new(0, -1), new(-1, 0), new(1, 0), new(0, 1)];

    public static KsProcgenSupportedAssemblySearchResult Search(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, KsProcgenAssemblyCapabilityReport capabilities,
        KsProcgenRoomAccessMask room, int seed, IReadOnlyList<KsProcgenFloorRootPose>? fixedFloorRoots = null,
        int maximumPlacementProbes = 4096, int maximumExpandedCells = 4096, int maximumLandingProbes = 4096,
        IReadOnlyDictionary<string, IReadOnlySet<Vector2i>>? allowedFloorCells = null, bool requireConnectedCleanFloor = false,
        IReadOnlyList<KsProcgenRootDistancePreference>? rootPreferences = null, KsProcgenPreferenceBudget? preferenceBudget = null)
    {
        var probes = 0;
        var expandedCells = 0;
        var landingProbes = 0;
        preferenceBudget ??= new();
        KsProcgenSupportedAssemblySearchResult Fail(KsProcgenSupportedAccessStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Selected supported assembly has no accepted placement."),
            PlacementProbes = probes, ExpandedCells = expandedCells,
            LandingProbes = landingProbes,
        };
        if (maximumPlacementProbes is < 0 or > 65_536 || maximumExpandedCells is < 0 or > 65_536 ||
            maximumLandingProbes is < 0 or > 65_536 ||
            !KsProcgenSupportedAccessPlanner.ValidMask(room) || !preferenceBudget.Valid)
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
        var floorIds = support.Members.Where(member => member.Layer is KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall)
            .Select(member => member.MemberId).ToHashSet(StringComparer.Ordinal);
        if (rootPreferences?.Count > 256 || rootPreferences?.Any(preference => preference == null ||
                !floorIds.Contains(preference.Subject) || !floorIds.Contains(preference.Target) || preference.Subject == preference.Target ||
                preference.MinimumDistance < 0 || preference.MaximumDistance < preference.MinimumDistance || preference.MaximumDistance > 64) == true)
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedRootPreferences");
        var preferences = KsProcgenReadOnly.Copy(rootPreferences ?? []);
        if (allowedFloorCells != null && (allowedFloorCells.Count > floorIds.Count ||
                allowedFloorCells.Any(pair => !floorIds.Contains(pair.Key) || pair.Value == null || pair.Value.Count > 4096)))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedRootPermissions");
        fixedFloorRoots ??= [];
        if (fixedFloorRoots.Count > floorIds.Count ||
            fixedFloorRoots.Select(root => root.MemberId).Distinct(StringComparer.Ordinal).Count() != fixedFloorRoots.Count ||
            fixedFloorRoots.Any(root => !floorIds.Contains(root.MemberId) ||
                !AlignedCell(root.Position - room.CellCenterOrigin, out _)))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedSearchFixedRoots");
        var fixedRoots = fixedFloorRoots.ToDictionary(root => root.MemberId,
            root => root.Position - room.CellCenterOrigin, StringComparer.Ordinal);
        if (room.Walls.Count == 0 && support.Members.Any(member => member.Layer == KsProcgenPlacementLayer.Wall))
            return Fail(KsProcgenSupportedAccessStatus.Rejected, "SupportedSearchNoPlacement");
        var members = KsProcgenSupportedMemberOrder.Order(support, assembly);
        var roots = new List<KsProcgenFloorRootPose>();
        var orientations = new List<KsProcgenMemberOrientation>();
        var selectedTurns = new Dictionary<string, int>(StringComparer.Ordinal);
        var occupiedFloor = new HashSet<Vector2i>();
        var rootFootprints = new Dictionary<string, IReadOnlyList<Vector2i>>(StringComparer.Ordinal);
        var rootRelations = new Dictionary<string, List<KsProcgenResolvedAssemblyRelation>>(StringComparer.Ordinal);
        foreach (var relation in assembly.Relations.Where(relation => relation.Severity == KsProcgenRelationSeverity.Required &&
                     relation.Target != null && relation.Kind is KsProcgenRelationKind.AdjacentTo or KsProcgenRelationKind.FacingTarget))
        foreach (var id in new[] { relation.Subject, relation.Target! }.Distinct(StringComparer.Ordinal))
        {
            if (!rootRelations.TryGetValue(id, out var related)) rootRelations.Add(id, related = new List<KsProcgenResolvedAssemblyRelation>());
            related.Add(relation);
        }
        bool RequiredRootsCompatible(string newRoot)
        {
            if (!rootRelations.TryGetValue(newRoot, out var related)) return true;
            foreach (var relation in related)
            {
                if (!rootFootprints.TryGetValue(relation.Subject, out var subject) || !rootFootprints.TryGetValue(relation.Target!, out var target)) continue;
                if (!subject.Any(cell => target.Any(other => relation.Kind == KsProcgenRelationKind.FacingTarget
                        ? KsProcgenAssemblyRelationEvaluator.FacesTarget(cell, selectedTurns[relation.Subject], other)
                        : Math.Abs((long) cell.X - other.X) + Math.Abs((long) cell.Y - other.Y) == 1))) return false;
            }
            return true;
        }
        var cells = room.Floor.Concat(room.Walls).Distinct().OrderBy(cell => KsProcgenFurnishingPlanner.CellRank(seed,
                "supported-assembly-search", assembly.AssemblyId + "/" + assembly.VariantId, cell))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        KsProcgenSupportedAccessPlan? accepted = null;
        KsProcgenSupportedAccessStatus? terminalStatus = null;
        string? terminalCode = null;
        var acceptedAssemblyTurn = 0;
        IReadOnlyList<KsProcgenFloorRootPose> acceptedRoots = [];
        IReadOnlyList<KsProcgenMemberOrientation> acceptedOrientations = [];
        var acceptedScore = new KsProcgenPreferenceScore(int.MaxValue, long.MaxValue);
        var landingTruncated = false;
        IReadOnlyList<KsProcgenRootDistanceOutcome> acceptedOutcomes = [];
        KsProcgenSupportedAssemblySearchResult Candidate() => new()
        {
            Status = KsProcgenSupportedAccessStatus.Candidate, Access = accepted,
            FloorRoots = acceptedRoots, Orientations = acceptedOrientations, AssemblyQuarterTurns = acceptedAssemblyTurn,
            PlacementProbes = probes, ExpandedCells = expandedCells, LandingProbes = landingProbes,
            PreferenceScore = acceptedScore, PreferenceSearchTruncated = landingTruncated || terminalStatus == KsProcgenSupportedAccessStatus.BudgetExceeded,
            RootPreferenceOutcomes = acceptedOutcomes,
        };
        long PartialPenalty(string id, Vector2 position)
        {
            var penalty = 0L;
            foreach (var preference in preferences)
            {
                var other = preference.Subject == id ? preference.Target : preference.Target == id ? preference.Subject : null;
                if (other == null) continue;
                var pose = roots.FirstOrDefault(root => root.MemberId == other);
                if (pose != null) penalty += KsProcgenPreferenceScoring.DistancePenalty(position, pose.Position, preference);
            }
            return penalty;
        }
        bool Visit(int index, int assemblyTurn)
        {
            if (index == members.Count)
            {
                var optimizing = preferenceBudget.Active;
                var access = KsProcgenSupportedAccessPlanner.Plan(assembly, selectedMemberIds, capabilities,
                    roots, orientations, assemblyTurn, room, maximumExpandedCells: preferenceBudget.RemainingExpanded(maximumExpandedCells - expandedCells),
                    maximumLandingProbes: preferenceBudget.RemainingLandings(maximumLandingProbes - landingProbes),
                    optimizePreferences: optimizing);
                preferenceBudget.ChargeGraph(access.ExpandedCells, access.LandingProbes);
                expandedCells += access.ExpandedCells;
                landingProbes += access.LandingProbes;
                if (access.Status == KsProcgenSupportedAccessStatus.Candidate)
                {
                    // A surface offset can leave its floor support's reserved cells. External
                    // occupancy cannot become legal merely because an operating path exists.
                    if (access.Footprints.Any(footprint => footprint.Cells.Any(cell =>
                            room.Blocking.Contains(cell) || room.Occupied.Contains(cell))))
                        return false;
                    // Projected surface members may extend beyond their floor root. Only members
                    // of the same support tree share XY; unrelated roots cannot claim those tiles.
                    var floorRootsByMember = access.Poses!.Members.ToDictionary(pose => pose.Support.MemberId,
                        pose => pose.Support.FloorRootMemberId, StringComparer.Ordinal);
                    var supportTrees = access.Footprints.GroupBy(footprint => floorRootsByMember[footprint.MemberId])
                        .Select(group => group.SelectMany(footprint => footprint.Cells).ToHashSet()).ToArray();
                    for (var treeIndex = 0; treeIndex < supportTrees.Length; treeIndex++)
                    for (var otherIndex = treeIndex + 1; otherIndex < supportTrees.Length; otherIndex++)
                        if (supportTrees[treeIndex].Overlaps(supportTrees[otherIndex])) return false;
                    if (requireConnectedCleanFloor)
                    {
                        var clean = room.Floor.Where(cell => !room.Blocking.Contains(cell)).ToHashSet();
                        foreach (var footprint in access.Footprints)
                            if (definitions[footprint.MemberId].Entry.Movement != KsProcgenMovementClass.Clear)
                                clean.ExceptWith(footprint.Cells);
                        if (clean.Count == 0 || room.Network.Any(cell => !clean.Contains(cell))) return false;
                        var start = KsProcgenGeometry.SortCells(clean)[0];
                        var reached = new HashSet<Vector2i> { start }; var queue = new Queue<Vector2i>(); queue.Enqueue(start);
                        while (queue.TryDequeue(out var cell))
                        {
                            if (preferenceBudget.RemainingExpanded(maximumExpandedCells - expandedCells) <= 0)
                            {
                                terminalStatus = KsProcgenSupportedAccessStatus.BudgetExceeded;
                                terminalCode = "SupportedRoomConnectivityBudget"; return false;
                            }
                            expandedCells++;
                            preferenceBudget.ChargeGraph(1, 0);
                            foreach (var step in Cardinal)
                                if (clean.Contains(cell + step) && reached.Add(cell + step)) queue.Enqueue(cell + step);
                        }
                        if (reached.Count != clean.Count) return false;
                    }
                    var rootPositions = roots.ToDictionary(root => root.MemberId, root => root.Position, StringComparer.Ordinal);
                    var score = new KsProcgenPreferenceScore(access.PreferredRelationsApplicable - access.PreferredRelationsSatisfied,
                        preferences.Sum(preference => KsProcgenPreferenceScoring.DistancePenalty(rootPositions[preference.Subject], rootPositions[preference.Target], preference)));
                    if (accepted == null || score.BetterThan(acceptedScore))
                    {
                        accepted = access;
                        acceptedScore = score;
                        acceptedAssemblyTurn = assemblyTurn;
                        acceptedRoots = KsProcgenReadOnly.Copy(roots);
                        acceptedOrientations = KsProcgenReadOnly.Copy(orientations);
                        acceptedOutcomes = preferences.Select(preference => new KsProcgenRootDistanceOutcome(preference.Subject, preference.Target,
                            preference.MinimumDistance, preference.MaximumDistance,
                            (long) Math.Abs(rootPositions[preference.Subject].X - rootPositions[preference.Target].X) +
                            (long) Math.Abs(rootPositions[preference.Subject].Y - rootPositions[preference.Target].Y),
                            KsProcgenPreferenceScoring.DistancePenalty(rootPositions[preference.Subject], rootPositions[preference.Target], preference))).ToReadOnlyList();
                    }
                    preferenceBudget.Activate();
                    landingTruncated |= access.PreferenceSearchTruncated;
                    if (!optimizing && !acceptedScore.Satisfied && acceptedScore.MissedRelations > 0 && access.Access.Count > 0)
                        return Visit(index, assemblyTurn);
                    return acceptedScore.Satisfied;
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
            if (member.Layer is KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall)
                positions = fixedRoots.TryGetValue(member.MemberId, out var position) ?
                    [new((int) position.X, (int) position.Y)] : cells.Where(cell =>
                        member.Layer == KsProcgenPlacementLayer.Wall ? room.Walls.Contains(cell) : room.Floor.Contains(cell));
            if (preferences.Count > 0 && roots.Count > 0 && !fixedRoots.ContainsKey(member.MemberId) &&
                member.Layer is KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall)
            {
                // A bounded ordering hint; every actual pose attempt still pays the normal probe.
                var prefix = positions.Take(32).ToArray();
                positions = prefix.OrderBy(cell => PartialPenalty(member.MemberId, room.CellCenterOrigin + new Vector2((float) cell.X, (float) cell.Y)))
                    .Concat(positions.Skip(32));
            }
            foreach (var cell in positions)
            foreach (var turn in turns)
            {
                if (probes >= maximumPlacementProbes || !preferenceBudget.ChargePlacement())
                {
                    terminalStatus = KsProcgenSupportedAccessStatus.BudgetExceeded;
                    terminalCode = "SupportedSearchPlacementBudget";
                    return false;
                }
                probes++;
                Vector2i[] footprint = [];
                if (member.Layer is KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall)
                {
                    if (allowedFloorCells != null && allowedFloorCells.TryGetValue(member.MemberId, out var allowed) && !allowed.Contains(cell)) continue;
                    footprint = definition.Entry.Footprint.Select(offset => cell +
                        KsProcgenInteractionFacingPlanner.RotateFootprintOffset(offset, turn)).ToArray();
                    if (allowedFloorCells != null && allowedFloorCells.TryGetValue(member.MemberId, out var permission) &&
                        footprint.Any(tile => !permission.Contains(tile))) continue;
                    if (footprint.Any(tile => (member.Layer == KsProcgenPlacementLayer.Wall
                            ? !room.Walls.Contains(tile) : !room.Floor.Contains(tile) || room.Walls.Contains(tile)) ||
                            room.Blocking.Contains(tile) || room.Occupied.Contains(tile) ||
                            room.Network.Contains(tile) || occupiedFloor.Contains(tile)))
                        continue;
                    // A required floor machine with no possible static front landing cannot be
                    // repaired by adding more members. Prune before enumerating child placements.
                    // Seats have four access sides; supported children retain the full final audit.
                    if (definition.Entry.RequiresInteractionApproach && definition.Entry.Role != KsProcgenEntityRole.Seat)
                    {
                        var face = KsProcgenInteractionFacingPlanner.RotateFootprintOffset(new(0, -1), turn);
                        if (!footprint.Any(tile => !footprint.Contains(tile + face) && room.Floor.Contains(tile + face) &&
                                !room.Walls.Contains(tile + face) && !room.Blocking.Contains(tile + face))) continue;
                    }
                    occupiedFloor.UnionWith(footprint);
                    rootFootprints.Add(member.MemberId, footprint);
                    roots.Add(new(member.MemberId, room.CellCenterOrigin + new Vector2((float) cell.X, (float) cell.Y)));
                }
                orientations.Add(new(member.MemberId, turn));
                selectedTurns.Add(member.MemberId, turn);
                if ((member.Layer is not (KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall) || RequiredRootsCompatible(member.MemberId)) && Visit(index + 1, assemblyTurn))
                    return true;
                selectedTurns.Remove(member.MemberId);
                orientations.RemoveAt(orientations.Count - 1);
                if (member.Layer is KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall)
                {
                    roots.RemoveAt(roots.Count - 1);
                    rootFootprints.Remove(member.MemberId);
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
                return Candidate();
            if (terminalStatus != null)
                return accepted != null && terminalStatus == KsProcgenSupportedAccessStatus.BudgetExceeded
                    ? Candidate() : Fail(terminalStatus.Value, terminalCode!);
        }
        return accepted != null ? Candidate() : Fail(KsProcgenSupportedAccessStatus.Rejected, "SupportedSearchNoPlacement");
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
