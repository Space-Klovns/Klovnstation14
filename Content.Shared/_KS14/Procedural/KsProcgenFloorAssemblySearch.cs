using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public readonly record struct KsProcgenAssemblyContinuationResult(bool Accepted, int Probes, bool Stop);

public delegate KsProcgenAssemblyContinuationResult KsProcgenAssemblyContinuation(
    IReadOnlyList<KsProcgenEntityProposal> entities, IReadOnlyList<KsProcgenAssemblyRelationWitness> witnesses,
    Vector2i anchor, int probes);

/// <summary>
/// Bounded depth-first search over a compiled floor variant. Branches own their placements;
/// only complete legal candidates and their witnesses survive rollback. Engine support is unverified.
/// </summary>
public sealed class KsProcgenFloorAssemblySearch
{
    private readonly KsProcgenResolvedAssembly _assembly;
    private readonly KsProcgenResolvedAssemblyMember[] _members;
    private readonly string _packId;
    private readonly string _coreId;
    private readonly int _assemblyTurn;
    private readonly IReadOnlySet<Vector2i> _floor;
    private readonly IReadOnlySet<Vector2i> _protectedCells;
    private readonly IReadOnlySet<Vector2i> _walls;
    private readonly IReadOnlySet<Vector2i> _alreadyOccupied;
    private readonly IReadOnlySet<Vector2i> _alreadyBlocked;
    private readonly int _seed;
    private readonly string _regionId;
    private readonly int _clusterIndex;
    private readonly KsProcgenRelationPathBudget _pathBudget;
    private readonly List<KsProcgenEntityProposal> _trial = new();
    private int _probeLimit;
    private int _bestScore = -1;
    private readonly KsProcgenAssemblyContinuation? _continuation;

    public int Probes { get; private set; }
    public int Backtracks { get; private set; }
    public bool Exhausted { get; private set; }
    public IReadOnlyList<KsProcgenEntityProposal> Accepted { get; private set; } = [];
    public Vector2i? AcceptedAnchor { get; private set; }
    public IReadOnlyList<KsProcgenAssemblyRelationWitness> Witnesses { get; private set; } = [];

    public KsProcgenFloorAssemblySearch(KsProcgenResolvedAssembly assembly, string packId, string coreId,
        int assemblyTurn, IReadOnlySet<Vector2i> floor, IReadOnlySet<Vector2i> protectedCells,
        IReadOnlySet<Vector2i> walls, IReadOnlySet<Vector2i> alreadyOccupied, IReadOnlySet<Vector2i> alreadyBlocked,
        int seed, string regionId, int clusterIndex, int maximumProbes, int startingProbes,
        KsProcgenRelationPathBudget pathBudget, KsProcgenAssemblyContinuation? continuation = null)
    {
        if (assembly.Members.Count is < 1 or > 64 || floor.Count is < 1 or > 65_536 ||
            protectedCells.Count == 0 || protectedCells.Any(cell => !floor.Contains(cell)) ||
            maximumProbes is < 1 or > 4096 || startingProbes < 0 || startingProbes > maximumProbes ||
            assemblyTurn is < 0 or > 3)
            throw new ArgumentException("Floor assembly search requires normalized bounded inputs.");
        _assembly = assembly;
        _members = assembly.Members.OrderBy(member => member.Id == assembly.AnchorMember ? 0 : 1)
            .ThenBy(member => member.Required ? 0 : 1).ThenBy(member => KsProcgenFurnishingPlanner.RoleOrder(member.Entry.Role))
            .ThenBy(member => member.Id, StringComparer.Ordinal).ToArray();
        _packId = packId;
        _coreId = coreId;
        _assemblyTurn = assemblyTurn;
        _floor = floor;
        _protectedCells = protectedCells;
        _walls = walls;
        _alreadyOccupied = alreadyOccupied;
        _alreadyBlocked = alreadyBlocked;
        _seed = seed;
        _regionId = regionId;
        _clusterIndex = clusterIndex;
        _probeLimit = maximumProbes;
        Probes = startingProbes;
        _pathBudget = pathBudget;
        _continuation = continuation;
    }

    /// <summary>True means a fully satisfied candidate or search exhaustion stops further anchor exploration.</summary>
    public bool ExploreAnchor(Vector2i anchor) => Search(0, anchor);

    private bool Search(int memberIndex, Vector2i anchor)
    {
        if (memberIndex == _members.Length)
            return EvaluateComplete(anchor);
        var member = _members[memberIndex];
        var occupied = new HashSet<Vector2i>(_alreadyOccupied);
        var blocked = new HashSet<Vector2i>(_alreadyBlocked);
        blocked.UnionWith(_walls);
        foreach (var placed in _trial)
        {
            occupied.UnionWith(placed.OccupiedCells);
            if (placed.Movement != KsProcgenMovementClass.Clear)
                blocked.UnionWith(placed.OccupiedCells);
        }
        var chairs = _trial.Where(entity => entity.Role == KsProcgenEntityRole.Seat &&
                                           entity.Movement == KsProcgenMovementClass.Clear)
            .Select(entity => entity.Cell).ToHashSet();
        var cells = memberIndex == 0 ? new[] { anchor } : _floor
            .OrderBy(cell => Distance(cell, anchor))
            .ThenBy(cell => KsProcgenFurnishingPlanner.CellRank(_seed, _regionId, member.Id, cell))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        var turns = member.Entry.AllowedQuarterTurns.Distinct().OrderBy(turn => turn).Where(turn =>
            member.RotationMode == KsProcgenMemberRotation.Independent ||
            turn == (_assemblyTurn + member.LocalQuarterTurns) % 4).ToArray();
        if (!member.Entry.RequiresInteractionApproach && !member.ApproachLanding.HasValue &&
            !_assembly.Relations.Any(relation => relation.Subject == member.Id &&
                                               relation.Kind == KsProcgenRelationKind.FacingTarget))
            turns = turns.DistinctBy(turn => new KsProcgenEntityFootprint(member.Entry.Footprint
                .Select(offset => KsProcgenInteractionFacingPlanner.RotateFootprintOffset(offset, turn))
                .OrderBy(offset => offset.Y).ThenBy(offset => offset.X))).ToArray();
        foreach (var cell in cells)
        foreach (var turn in OrderedTurns(member, cell, turns, occupied, blocked, chairs))
        {
            if (Probes >= _probeLimit)
            {
                Exhausted = true;
                return true;
            }
            Probes++;
            var footprint = Footprint(cell, turn, member.Entry.Footprint);
            if (footprint == null || footprint.Any(tile => !_floor.Contains(tile) || _walls.Contains(tile) ||
                    _protectedCells.Contains(tile) || occupied.Contains(tile)) ||
                member.Entry.Movement != KsProcgenMovementClass.Clear && _trial.Any(placed =>
                    placed.DeclaredApproachLanding.HasValue && footprint.Contains(placed.DeclaredApproachLanding.Value)))
                continue;
            var landing = KsProcgenAssemblyRelationEvaluator.ResolveDeclaredLanding(cell, turn, member.ApproachLanding);
            if (member.ApproachLanding.HasValue && (!landing.HasValue || !_floor.Contains(landing.Value) ||
                blocked.Contains(landing.Value) || member.Entry.Movement != KsProcgenMovementClass.Clear && footprint.Contains(landing.Value)))
                continue;
            var proposal = new KsProcgenEntityProposal(_packId, member.Id, member.Entry.Entity,
                member.Entry.Role, member.Entry.Movement, cell, turn, null, ClusterIndex: _clusterIndex,
                Footprint: new KsProcgenEntityFootprint(footprint), CoreId: _coreId,
                AssemblyId: _assembly.AssemblyId, VariantId: _assembly.VariantId, DeclaredApproachLanding: landing);
            if (!SpatialCompatible(proposal))
                continue;
            var firstApproach = true;
            foreach (var candidate in Approaches(proposal, member, occupied, blocked, chairs))
            {
                // Alternatives for a multi-cell front edge also consume the shared placement budget.
                if (!firstApproach)
                {
                    if (Probes >= _probeLimit)
                    {
                        Exhausted = true;
                        return true;
                    }
                    Probes++;
                }
                firstApproach = false;
                _trial.Add(candidate);
                var stop = Search(memberIndex + 1, anchor);
                _trial.RemoveAt(_trial.Count - 1);
                if (stop)
                    return true;
                Backtracks++;
            }
        }
        if (!member.Required)
            return Search(memberIndex + 1, anchor);
        return false;
    }

    private IEnumerable<KsProcgenEntityProposal> Approaches(KsProcgenEntityProposal proposal,
        KsProcgenResolvedAssemblyMember member, IReadOnlySet<Vector2i> occupied,
        IReadOnlySet<Vector2i> blocked, IReadOnlySet<Vector2i> chairs)
    {
        if (proposal.Role == KsProcgenEntityRole.Seat)
        {
            var result = KsProcgenInteractionFacingPlanner.FindChairApproach(proposal.Cell, _floor, blocked, _protectedCells);
            foreach (var approach in result.Approaches)
                yield return proposal with { InteractionApproach = approach };
            yield break;
        }
        if (!member.Entry.RequiresInteractionApproach)
        {
            yield return proposal;
            yield break;
        }
        var seatRelation = _assembly.Relations.FirstOrDefault(relation =>
            relation.Subject == member.Id && relation.Kind == KsProcgenRelationKind.UsesSeat);
        var associatedSeat = _trial.FirstOrDefault(entity => entity.EntryId == seatRelation?.Target)?.Cell;
        var facingBlocked = new HashSet<Vector2i>(blocked);
        facingBlocked.UnionWith(occupied.Where(tile => tile != associatedSeat));
        var resultFacing = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(proposal.Cell, _floor, _walls,
            facingBlocked, chairs, _protectedCells, [proposal.QuarterTurns], _seed,
            $"{_regionId}/{_packId}/{member.Id}", associatedChair: associatedSeat, localFootprint: member.Entry.Footprint);
        foreach (var facing in resultFacing.Options)
        {
            if (seatRelation?.Severity == KsProcgenRelationSeverity.Required && associatedSeat.HasValue &&
                facing.Approach != associatedSeat.Value)
                continue;
            yield return proposal with { InteractionApproach = facing.Approach };
        }
    }

    private IEnumerable<int> OrderedTurns(KsProcgenResolvedAssemblyMember member, Vector2i cell,
        int[] turns, IReadOnlySet<Vector2i> occupied, IReadOnlySet<Vector2i> blocked,
        IReadOnlySet<Vector2i> chairs)
    {
        if (!member.Entry.RequiresInteractionApproach)
            return turns;
        var seatRelation = _assembly.Relations.FirstOrDefault(relation =>
            relation.Subject == member.Id && relation.Kind == KsProcgenRelationKind.UsesSeat);
        var associatedSeat = _trial.FirstOrDefault(entity => entity.EntryId == seatRelation?.Target)?.Cell;
        var facingBlocked = new HashSet<Vector2i>(blocked);
        facingBlocked.UnionWith(occupied.Where(tile => tile != associatedSeat));
        var facing = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(cell, _floor, _walls,
            facingBlocked, chairs, _protectedCells, turns, _seed, $"{_regionId}/{_packId}/{member.Id}",
            associatedChair: associatedSeat, localFootprint: member.Entry.Footprint);
        // Preserve empty-floor, associated-seat and wall-backing preferences while allowing revisits.
        return facing.Options.Select(option => option.QuarterTurns).Concat(turns).Distinct().ToArray();
    }

    private bool SpatialCompatible(KsProcgenEntityProposal candidate)
    {
        foreach (var relation in _assembly.Relations.Where(relation => relation.Severity == KsProcgenRelationSeverity.Required))
        {
            var subject = relation.Subject == candidate.EntryId ? candidate :
                _trial.FirstOrDefault(entity => entity.EntryId == relation.Subject);
            var target = relation.Target == candidate.EntryId ? candidate :
                _trial.FirstOrDefault(entity => entity.EntryId == relation.Target);
            if (subject == null)
                continue;
            if (relation.Kind == KsProcgenRelationKind.AtCorner &&
                !KsProcgenAssemblyRelationEvaluator.AtCorner(subject.OccupiedCells, _walls))
                return false;
            if (target == null)
                continue;
            if (relation.Kind == KsProcgenRelationKind.AdjacentTo && !subject.OccupiedCells.Any(cell =>
                    target.OccupiedCells.Any(targetCell => Distance(cell, targetCell) == 1)))
                return false;
            if (relation.Kind == KsProcgenRelationKind.FacingTarget && !subject.OccupiedCells.Any(cell =>
                    target.OccupiedCells.Any(targetCell => KsProcgenFurnishingPlanner.FacesTarget(cell, subject.QuarterTurns, targetCell))))
                return false;
        }
        return true;
    }

    private bool EvaluateComplete(Vector2i anchor)
    {
        if (!KsProcgenFurnishingPlanner.ValidateTrial(_trial, _members.Select(member => member.Entry).ToArray(),
                _assembly, _floor, _walls, _protectedCells, _alreadyBlocked, _seed, _regionId))
            return false;
        var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate(_packId, _coreId, _clusterIndex,
            _assembly, _trial, _walls, floor: _floor, blockingCells: _alreadyBlocked, pathBudget: _pathBudget);
        if (evaluation.PathSearchTruncated)
        {
            Exhausted = true;
            return true;
        }
        if (!evaluation.RequiredSatisfied)
            return false;
        if (_continuation != null)
        {
            var continuationResult = _continuation(_trial.ToArray(), evaluation.Witnesses, anchor, Probes);
            if (continuationResult.Probes < Probes || continuationResult.Probes > _probeLimit)
                throw new ArgumentException("Assembly continuations must preserve the shared placement budget.");
            Probes = continuationResult.Probes;
            if (!continuationResult.Accepted)
            {
                Exhausted = continuationResult.Stop;
                return continuationResult.Stop;
            }
            Accepted = _trial.ToArray();
            AcceptedAnchor = anchor;
            Witnesses = evaluation.Witnesses.ToArray();
            return true;
        }
        if (evaluation.PreferredSatisfied > _bestScore)
        {
            if (_bestScore < 0)
                _probeLimit = Math.Min(_probeLimit, Probes + 64);
            _bestScore = evaluation.PreferredSatisfied;
            Accepted = _trial.ToArray();
            AcceptedAnchor = anchor;
            Witnesses = evaluation.Witnesses.ToArray();
        }
        return evaluation.PreferredSatisfied == evaluation.PreferredApplicable;
    }

    private static Vector2i[]? Footprint(Vector2i origin, int turn, IReadOnlyList<Vector2i> offsets)
    {
        var cells = new List<Vector2i>();
        foreach (var offset in offsets)
        {
            var tile = KsProcgenAssemblyRelationEvaluator.ResolveDeclaredLanding(origin, turn, offset);
            if (!tile.HasValue)
                return null;
            cells.Add(tile.Value);
        }
        return cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
    }

    private static long Distance(Vector2i first, Vector2i second) =>
        Math.Abs((long) first.X - second.X) + Math.Abs((long) first.Y - second.Y);
}
