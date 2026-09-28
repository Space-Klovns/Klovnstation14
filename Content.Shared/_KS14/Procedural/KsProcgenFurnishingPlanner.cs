using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenFurnishingStatus : byte
{
    Proposed,
    Sparse,
    MandatoryUnmet,
    BudgetExceeded,
    InvalidInput,
}

/// <summary>
/// One speculative entity placement. Real collision, anchoring, direction, and operation follow.
/// </summary>
public sealed record KsProcgenEntityProposal(
    string PackId,
    string EntryId,
    string EntityId,
    KsProcgenEntityRole Role,
    KsProcgenMovementClass Movement,
    Vector2i Cell,
    int QuarterTurns,
    Vector2i? InteractionApproach);

public sealed record KsProcgenPackOmission(string PackId, string Reason);

public sealed class KsProcgenFurnishingResult
{
    public KsProcgenFurnishingStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenEntityProposal> Entities { get; init; } = [];
    public IReadOnlyList<KsProcgenPackOmission> Omissions { get; init; } = [];
    public IReadOnlyList<Vector2i> ProtectedPassageCells { get; init; } = [];
    public int CandidateProbes { get; init; }
}

/// <summary>
/// Initial coherent singleton-pack proposal. All active entries in each selected pack are tried
/// atomically near one anchor; no partial workstation core survives a failed candidate.
/// </summary>
public static class KsProcgenFurnishingPlanner
{
    private static readonly Vector2i[] Faces = [new(0, -1), new(-1, 0), new(0, 1), new(1, 0)];

    public static KsProcgenFurnishingResult Plan(
        IPrototypeManager prototypeManager,
        KsProcgenThemedRegion region,
        KsProcgenPartitionResult partition,
        int seed,
        IReadOnlySet<Vector2i>? inspectedWalls = null,
        int maxCandidateProbes = 4_096,
        int maxRoomCells = 512)
    {
        if (prototypeManager == null || region == null || partition == null ||
            partition.Status == KsProcgenPartitionStatus.InvalidInput ||
            maxCandidateProbes <= 0 || maxCandidateProbes > 4_096 ||
            maxRoomCells <= 0 || maxRoomCells > 65_536)
            return Failure(KsProcgenFurnishingStatus.InvalidInput, "InvalidFurnishingInput");

        var floor = new HashSet<Vector2i>(region.FloorCells);
        if (floor.Count != region.FloorCells.Count || floor.Count == 0 ||
            floor.Any(cell => !partition.FloorCells.Contains(cell)))
            return Failure(KsProcgenFurnishingStatus.InvalidInput, "InvalidFurnishingFloor");
        if (region.Kind == KsProcgenZoneKind.Passage)
        {
            if (region.UnplacedRequiredPacks.Count > 0)
                return Failure(KsProcgenFurnishingStatus.MandatoryUnmet, "MandatoryPackInPassage");
            return new KsProcgenFurnishingResult
            {
                Status = KsProcgenFurnishingStatus.Proposed,
                ProtectedPassageCells = KsProcgenGeometry.SortCells(floor),
            };
        }

        if (floor.Count > maxRoomCells)
        {
            if (region.UnplacedRequiredPacks.Count > 0)
                return Failure(KsProcgenFurnishingStatus.BudgetExceeded, "FurnishingRoomCellBudget");
            var skipped = new List<KsProcgenPackOmission>();
            if (region.Theme.DominantEntityPackId != null)
                skipped.Add(new KsProcgenPackOmission(region.Theme.DominantEntityPackId, "FurnishingRoomCellBudget"));
            skipped.AddRange(region.Theme.SupportingEntityPackIds.Select(id =>
                new KsProcgenPackOmission(id, "FurnishingRoomCellBudget")));
            return new KsProcgenFurnishingResult
            {
                Status = KsProcgenFurnishingStatus.Sparse,
                Omissions = skipped,
                ProtectedPassageCells = KsProcgenGeometry.SortCells(floor),
            };
        }

        var terminals = new HashSet<Vector2i>();
        foreach (var door in partition.DoorOpenings)
        {
            if (floor.Contains(door.Threshold)) terminals.Add(door.Threshold);
            if (floor.Contains(door.InsideApproach)) terminals.Add(door.InsideApproach);
            if (floor.Contains(door.OutsideApproach)) terminals.Add(door.OutsideApproach);
        }
        if (terminals.Count == 0)
            terminals.Add(KsProcgenGeometry.SortCells(floor)[0]);

        var protectedCells = new HashSet<Vector2i>(terminals);
        var root = KsProcgenGeometry.SortCells(terminals)[0];
        foreach (var terminal in KsProcgenGeometry.SortCells(terminals))
        {
            var path = KsProcgenTraversal.FindCleanPath(root, terminal, floor, new HashSet<Vector2i>());
            if (path.Count == 0)
                return Failure(KsProcgenFurnishingStatus.InvalidInput, "FurnishingRoomRouteUnavailable");
            protectedCells.UnionWith(path);
        }

        var selectedPacks = new List<string>();
        if (region.Theme.DominantEntityPackId != null)
            selectedPacks.Add(region.Theme.DominantEntityPackId);
        selectedPacks.AddRange(region.Theme.SupportingEntityPackIds
            .Where(id => id != region.Theme.DominantEntityPackId)
            .OrderBy(id => id, StringComparer.Ordinal));
        if (region.UnplacedRequiredPacks.Any(required =>
                required.MinimumCount != 1 || !selectedPacks.Contains(required.PackId)))
            return Failure(KsProcgenFurnishingStatus.MandatoryUnmet, "UnsupportedMandatoryPackCount");

        var walls = new HashSet<Vector2i>(partition.WallCells);
        if (inspectedWalls != null)
            walls.UnionWith(inspectedWalls);
        var occupied = new HashSet<Vector2i>();
        var blocked = new HashSet<Vector2i>();
        var entities = new List<KsProcgenEntityProposal>();
        var omissions = new List<KsProcgenPackOmission>();
        var probes = 0;
        Vector2i? dominantAnchor = null;
        foreach (var packId in selectedPacks)
        {
            if (!prototypeManager.TryIndex<KsProcgenEntityPackPrototype>(packId, out var pack))
                return Failure(KsProcgenFurnishingStatus.InvalidInput, "UnknownFurnishingPack");
            var entries = pack.Entries.Where(entry => entry.Weight > 0f)
                .OrderBy(entry => RoleOrder(entry.Role))
                .ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0 || entries.Any(entry => entry.Footprint.Count != 1 ||
                entry.Footprint[0] != new Vector2i(0, 0)))
            {
                if (IsRequired(region, packId))
                    return Failure(KsProcgenFurnishingStatus.MandatoryUnmet, "UnsupportedFurnishingFootprint");
                omissions.Add(new KsProcgenPackOmission(packId, "UnsupportedFurnishingFootprint"));
                continue;
            }

            var placed = TryPack(packId, entries, floor, protectedCells, walls,
                occupied, blocked, dominantAnchor, seed, region.Id, maxCandidateProbes, ref probes,
                out var accepted, out var anchor, out var exhausted);
            if (exhausted)
                return Failure(KsProcgenFurnishingStatus.BudgetExceeded, "FurnishingCandidateBudget");
            if (!placed)
            {
                if (IsRequired(region, packId))
                    return Failure(KsProcgenFurnishingStatus.MandatoryUnmet, "RequiredPackCannotFit");
                omissions.Add(new KsProcgenPackOmission(packId, "PackCannotFit"));
                if (packId == region.Theme.DominantEntityPackId)
                {
                    foreach (var support in selectedPacks.Where(id => id != packId))
                        omissions.Add(new KsProcgenPackOmission(support, "DominantPackUnavailable"));
                    break;
                }
                continue;
            }

            if (dominantAnchor == null)
                dominantAnchor = anchor;
            foreach (var entity in accepted)
            {
                entities.Add(entity);
                occupied.Add(entity.Cell);
                if (entity.Movement != KsProcgenMovementClass.Clear)
                    blocked.Add(entity.Cell);
            }
        }

        return new KsProcgenFurnishingResult
        {
            Status = omissions.Count == 0 ? KsProcgenFurnishingStatus.Proposed : KsProcgenFurnishingStatus.Sparse,
            Entities = entities,
            Omissions = omissions,
            ProtectedPassageCells = KsProcgenGeometry.SortCells(protectedCells),
            CandidateProbes = probes,
        };
    }

    private static bool TryPack(
        string packId,
        IReadOnlyList<KsProcgenEntityEntry> entries,
        IReadOnlySet<Vector2i> floor,
        IReadOnlySet<Vector2i> protectedCells,
        IReadOnlySet<Vector2i> walls,
        IReadOnlySet<Vector2i> alreadyOccupied,
        IReadOnlySet<Vector2i> alreadyBlocked,
        Vector2i? preferredAnchor,
        int seed,
        string regionId,
        int maximumProbes,
        ref int probes,
        out IReadOnlyList<KsProcgenEntityProposal> accepted,
        out Vector2i? acceptedAnchor,
        out bool exhausted)
    {
        accepted = [];
        acceptedAnchor = null;
        exhausted = false;
        var centerX = floor.Sum(cell => (long) cell.X);
        var centerY = floor.Sum(cell => (long) cell.Y);
        var anchors = floor.Where(cell => !protectedCells.Contains(cell) && !alreadyOccupied.Contains(cell) &&
                                          (!preferredAnchor.HasValue || Distance(cell, preferredAnchor.Value) <= 4))
            .OrderBy(cell => preferredAnchor.HasValue
                ? Distance(cell, preferredAnchor.Value)
                : Math.Abs(cell.X * (long) floor.Count - centerX) +
                  Math.Abs(cell.Y * (long) floor.Count - centerY))
            .ThenBy(cell => CellRank(seed, regionId, packId, cell))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        foreach (var anchor in anchors)
        {
            var trial = new List<KsProcgenEntityProposal>();
            var occupied = new HashSet<Vector2i>(alreadyOccupied);
            var blocked = new HashSet<Vector2i>(alreadyBlocked);
            var chairs = new HashSet<Vector2i>();
            var failed = false;
            foreach (var entry in entries)
            {
                var candidates = trial.Count == 0 ? new[] { anchor } :
                    floor.Where(cell => Distance(cell, anchor) <= 4)
                        .OrderBy(cell => Distance(cell, anchor))
                        .ThenBy(cell => CellRank(seed, regionId, entry.Id, cell))
                        .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
                KsProcgenEntityProposal? chosen = null;
                foreach (var cell in candidates)
                {
                    if (++probes > maximumProbes)
                    {
                        exhausted = true;
                        return false;
                    }
                    if (protectedCells.Contains(cell) || occupied.Contains(cell))
                        continue;
                    var primary = trial.FirstOrDefault(proposal =>
                        proposal.Role == KsProcgenEntityRole.PrimaryFurniture);
                    if (entry.Role == KsProcgenEntityRole.Seat && primary != null &&
                        Distance(primary.Cell, cell) != 1)
                        continue;

                    var turns = entry.AllowedQuarterTurns.Distinct().OrderBy(turn => turn).ToArray();
                    if (turns.Length == 0)
                        continue;
                    Vector2i? approach = null;
                    var turn = turns[0];
                    if (entry.Role == KsProcgenEntityRole.Seat)
                    {
                        var chair = KsProcgenInteractionFacingPlanner.FindChairApproach(cell,
                            floor, blocked, protectedCells);
                        if (chair.Status != KsProcgenInteractionStatus.Ready)
                            continue;
                        approach = chair.Approach;
                    }
                    else if (entry.RequiresInteractionApproach)
                    {
                        var associatedChair = chairs.Count == 1 ? chairs.First() : (Vector2i?) null;
                        var facing = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(cell,
                            floor, walls, blocked, chairs, protectedCells, turns,
                            seed, $"{regionId}/{packId}/{entry.Id}", associatedChair);
                        if (facing.Status != KsProcgenInteractionStatus.Ready)
                            continue;
                        if (associatedChair.HasValue && entries.Any(candidate =>
                                candidate.Role == KsProcgenEntityRole.PrimaryFurniture) &&
                            facing.Facing!.Approach != associatedChair.Value)
                            continue;
                        turn = facing.Facing!.QuarterTurns;
                        approach = facing.Facing.Approach;
                    }

                    chosen = new KsProcgenEntityProposal(packId, entry.Id, entry.Entity,
                        entry.Role, entry.Movement, cell, turn, approach);
                    break;
                }

                if (chosen == null)
                {
                    failed = true;
                    break;
                }

                trial.Add(chosen);
                occupied.Add(chosen.Cell);
                if (chosen.Movement != KsProcgenMovementClass.Clear)
                    blocked.Add(chosen.Cell);
                if (chosen.Role == KsProcgenEntityRole.Seat && chosen.Movement == KsProcgenMovementClass.Clear)
                    chairs.Add(chosen.Cell);
            }

            if (failed)
                continue;
            for (var index = 0; index < trial.Count; index++)
            {
                if (trial[index].Role != KsProcgenEntityRole.Seat)
                    continue;
                var machine = trial.FirstOrDefault(entity => entity.Role == KsProcgenEntityRole.Equipment &&
                    entity.InteractionApproach == trial[index].Cell);
                if (machine == null)
                    continue;
                var turn = Array.IndexOf(Faces, machine.Cell - trial[index].Cell);
                var seatEntry = entries.First(entry => entry.Id == trial[index].EntryId);
                if (turn < 0 || !seatEntry.AllowedQuarterTurns.Contains(turn))
                {
                    failed = true;
                    break;
                }
                trial[index] = trial[index] with { QuarterTurns = turn };
            }

            if (failed || !ValidateTrial(trial, floor, walls, protectedCells, alreadyBlocked, seed, regionId))
                continue;
            accepted = trial;
            acceptedAnchor = anchor;
            return true;
        }

        return false;
    }

    private static bool ValidateTrial(
        IReadOnlyList<KsProcgenEntityProposal> trial,
        IReadOnlySet<Vector2i> floor,
        IReadOnlySet<Vector2i> walls,
        IReadOnlySet<Vector2i> protectedCells,
        IReadOnlySet<Vector2i> alreadyBlocked,
        int seed,
        string regionId)
    {
        var blocked = new HashSet<Vector2i>(alreadyBlocked);
        foreach (var entity in trial.Where(entity => entity.Movement != KsProcgenMovementClass.Clear))
            blocked.Add(entity.Cell);
        var chairs = trial.Where(entity => entity.Role == KsProcgenEntityRole.Seat &&
                                           entity.Movement == KsProcgenMovementClass.Clear)
            .Select(entity => entity.Cell).ToHashSet();
        foreach (var entity in trial)
        {
            if (entity.Role == KsProcgenEntityRole.Seat &&
                KsProcgenInteractionFacingPlanner.FindChairApproach(entity.Cell, floor,
                    blocked, protectedCells).Status != KsProcgenInteractionStatus.Ready)
                return false;
            if (entity.Role != KsProcgenEntityRole.Seat && entity.InteractionApproach.HasValue)
            {
                var facing = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(entity.Cell,
                    floor, walls, blocked, chairs, protectedCells, [entity.QuarterTurns],
                    seed, $"{regionId}/{entity.PackId}/{entity.EntryId}");
                if (facing.Status != KsProcgenInteractionStatus.Ready ||
                    facing.Facing!.Approach != entity.InteractionApproach.Value)
                    return false;
            }
        }

        return true;
    }

    private static int RoleOrder(KsProcgenEntityRole role) => role switch
    {
        KsProcgenEntityRole.PrimaryFurniture => 0,
        KsProcgenEntityRole.Seat => 1,
        KsProcgenEntityRole.Equipment => 2,
        _ => 3,
    };

    private static bool IsRequired(KsProcgenThemedRegion region, string packId) =>
        region.UnplacedRequiredPacks.Any(required => required.PackId == packId);

    private static int Distance(Vector2i first, Vector2i second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private static ulong CellRank(int seed, string regionId, string packId, Vector2i cell)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(seed);
        hash.AddString("furnishing-cell");
        hash.AddString(regionId);
        hash.AddString(packId);
        hash.AddInt(cell.X);
        hash.AddInt(cell.Y);
        return hash.Value;
    }

    private static KsProcgenFurnishingResult Failure(KsProcgenFurnishingStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Furnishing could not satisfy this room's content contract."),
    };
}
