using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>A declared geometric relationship, not evidence of engine support or interaction.</summary>
public sealed record KsProcgenAssemblyRelationWitness(
    string PackId,
    string CoreId,
    string AssemblyId,
    string VariantId,
    int ClusterIndex,
    string RelationId,
    string SubjectId,
    string? TargetId,
    KsProcgenRelationKind Kind,
    KsProcgenRelationSeverity Severity,
    KsProcgenConstraintState State,
    Vector2i? SubjectCell,
    Vector2i? TargetCell,
    string? ReasonCode,
    Vector2i? FirstBackingWall = null,
    Vector2i? SecondBackingWall = null,
    Vector2i? InteractionApproach = null,
    int? MinimumDistance = null,
    int? MaximumDistance = null,
    int? PathDistance = null,
    KsProcgenRelationPath CleanPath = default);

public sealed class KsProcgenAssemblyRelationEvaluation
{
    public bool RequiredSatisfied { get; init; }
    public int PreferredSatisfied { get; init; }
    public int PreferredApplicable { get; init; }
    public IReadOnlyList<KsProcgenAssemblyRelationWitness> Witnesses { get; init; } = [];
    public bool PathSearchTruncated { get; init; }
}

public static class KsProcgenAssemblyRelationEvaluator
{
    private static readonly Vector2i[] Faces = [new(0, -1), new(-1, 0), new(0, 1), new(1, 0)];

    public static bool Supported(KsProcgenRelationKind kind) => kind is
        KsProcgenRelationKind.AdjacentTo or KsProcgenRelationKind.FacingTarget or
        KsProcgenRelationKind.UsesSeat or KsProcgenRelationKind.FacingOpenSpace or
        KsProcgenRelationKind.AtCorner or KsProcgenRelationKind.Near;

    public static bool AtCorner(IReadOnlyList<Vector2i> footprint, IReadOnlySet<Vector2i> walls) =>
        FindCorner(footprint, walls).HasValue;

    private static (Vector2i First, Vector2i Second)? FindCorner(
        IReadOnlyList<Vector2i> footprint, IReadOnlySet<Vector2i> walls)
    {
        // Both perpendicular backing faces must belong to the same footprint corner.
        foreach (var cell in footprint.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        for (var face = 0; face < 4; face++)
        {
            var first = cell + Faces[face];
            var second = cell + Faces[(face + 1) % 4];
            if (!footprint.Contains(first) && !footprint.Contains(second) &&
                walls.Contains(first) && walls.Contains(second))
                return (first, second);
        }
        return null;
    }

    public static KsProcgenAssemblyRelationEvaluation Evaluate(
        string packId,
        string coreId,
        int clusterIndex,
        KsProcgenResolvedAssembly assembly,
        IReadOnlyList<KsProcgenEntityProposal> trial,
        IReadOnlySet<Vector2i> walls,
        IReadOnlySet<Vector2i>? floor = null,
        IReadOnlySet<Vector2i>? blockingCells = null,
        KsProcgenRelationPathBudget? pathBudget = null,
        IReadOnlyList<KsProcgenResolvedAssemblyRelation>? relationsToEvaluate = null)
    {
        var witnesses = new List<KsProcgenAssemblyRelationWitness>();
        var requiredSatisfied = assembly.Members.Where(member => member.Required)
            .All(member => trial.Any(entity => entity.EntryId == member.Id));
        var preferredSatisfied = 0;
        var preferredApplicable = 0;
        var pathSearchTruncated = false;
        HashSet<Vector2i>? clean = null;
        if (floor != null && blockingCells != null)
        {
            clean = new HashSet<Vector2i>(floor);
            clean.ExceptWith(walls);
            clean.ExceptWith(blockingCells);
            foreach (var entity in trial.Where(entity => entity.Movement != KsProcgenMovementClass.Clear))
                clean.ExceptWith(entity.OccupiedCells);
        }
        pathBudget ??= new KsProcgenRelationPathBudget();
        // A selected evaluation subset must retain the complete assembly's association context.
        foreach (var relation in (relationsToEvaluate ?? assembly.Relations).OrderBy(relation => relation.Id, StringComparer.Ordinal))
        {
            var subject = trial.FirstOrDefault(entity => entity.EntryId == relation.Subject);
            var target = trial.FirstOrDefault(entity => entity.EntryId == relation.Target);
            var corner = subject != null && relation.Kind == KsProcgenRelationKind.AtCorner
                ? FindCorner(subject.OccupiedCells, walls) : null;
            KsProcgenConstraintState state;
            string? reason = null;
            int? pathDistance = null;
            var path = default(KsProcgenRelationPath);
            if (!Supported(relation.Kind))
            {
                state = KsProcgenConstraintState.Unverified;
                reason = "AssemblyPlacementUnsupported";
                requiredSatisfied = false;
            }
            else if (subject == null)
            {
                var optional = assembly.Members.Any(member => member.Id == relation.Subject && !member.Required);
                state = optional ? KsProcgenConstraintState.NotApplicable : KsProcgenConstraintState.Missed;
                reason = optional ? "OptionalMemberOmitted" : "RequiredMemberMissing";
                if (!optional)
                    requiredSatisfied = false;
            }
            else if (relation.Kind == KsProcgenRelationKind.Near && target != null)
            {
                if (clean == null)
                {
                    state = KsProcgenConstraintState.Unverified;
                    reason = "NearGeometryUnavailable";
                    requiredSatisfied = false;
                }
                else
                {
                    var near = EvaluateNear(subject, target, assembly, clean, relation, pathBudget);
                    state = near.State;
                    reason = near.Reason;
                    path = near.Path;
                    pathDistance = path.Cells.Count == 0 ? null : path.Cells.Count - 1;
                    if (state == KsProcgenConstraintState.Unverified)
                    {
                        pathSearchTruncated = true;
                        requiredSatisfied = false;
                    }
                }
            }
            else
            {
                var met = relation.Kind switch
                {
                    KsProcgenRelationKind.AtCorner => corner.HasValue,
                    KsProcgenRelationKind.AdjacentTo => target != null && subject.OccupiedCells.Any(cell =>
                        target.OccupiedCells.Any(targetCell => Distance(cell, targetCell) == 1)),
                    KsProcgenRelationKind.FacingTarget => target != null && subject.OccupiedCells.Any(cell =>
                        target.OccupiedCells.Any(targetCell => FacesTarget(cell, subject.QuarterTurns, targetCell))),
                    KsProcgenRelationKind.UsesSeat => target is { Role: KsProcgenEntityRole.Seat,
                        Movement: KsProcgenMovementClass.Clear } && subject.InteractionApproach == target.Cell,
                    KsProcgenRelationKind.FacingOpenSpace => subject.InteractionApproach.HasValue &&
                        (!trial.Any(entity => entity.OccupiedCells.Contains(subject.InteractionApproach.Value)) ||
                         relation.ApproachPolicy == KsProcgenApproachPolicy.EmptyOrAssociatedSeat &&
                         assembly.Relations.Any(seatRelation => seatRelation.Subject == subject.EntryId &&
                             seatRelation.Kind == KsProcgenRelationKind.UsesSeat && trial.Any(entity =>
                                 entity.EntryId == seatRelation.Target && entity.Role == KsProcgenEntityRole.Seat &&
                                 entity.Cell == subject.InteractionApproach.Value &&
                                 entity.Movement == KsProcgenMovementClass.Clear))),
                    _ => false,
                };
                state = met ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Missed;
                if (!met)
                    reason = relation.Target != null && target == null ? "RelationTargetOmitted" : "RelationGeometryUnmet";
            }
            if (state == KsProcgenConstraintState.Missed && relation.Severity == KsProcgenRelationSeverity.Required)
                requiredSatisfied = false;
            if (relation.Severity == KsProcgenRelationSeverity.Preferred &&
                state is KsProcgenConstraintState.Satisfied or KsProcgenConstraintState.Missed)
            {
                preferredApplicable++;
                if (state == KsProcgenConstraintState.Satisfied)
                    preferredSatisfied++;
            }
            witnesses.Add(new KsProcgenAssemblyRelationWitness(packId, coreId, assembly.AssemblyId,
                assembly.VariantId, clusterIndex, relation.Id, relation.Subject, relation.Target,
                relation.Kind, relation.Severity, state, subject?.Cell, target?.Cell, reason,
                FirstBackingWall: corner?.First, SecondBackingWall: corner?.Second,
                InteractionApproach: subject?.InteractionApproach,
                MinimumDistance: relation.Kind == KsProcgenRelationKind.Near ? relation.MinimumDistance : null,
                MaximumDistance: relation.Kind == KsProcgenRelationKind.Near ? relation.MaximumDistance : null,
                PathDistance: pathDistance, CleanPath: path));
        }
        return new KsProcgenAssemblyRelationEvaluation
        {
            RequiredSatisfied = requiredSatisfied,
            PreferredSatisfied = preferredSatisfied,
            PreferredApplicable = preferredApplicable,
            Witnesses = witnesses,
            PathSearchTruncated = pathSearchTruncated,
        };
    }

    private static (KsProcgenConstraintState State, string? Reason, KsProcgenRelationPath Path) EvaluateNear(
        KsProcgenEntityProposal subject,
        KsProcgenEntityProposal target,
        KsProcgenResolvedAssembly assembly,
        IReadOnlySet<Vector2i> clean,
        KsProcgenResolvedAssemblyRelation relation,
        KsProcgenRelationPathBudget budget)
    {
        var start = Landing(subject, assembly);
        var goal = Landing(target, assembly);
        if (!start.HasValue || !goal.HasValue)
            return (KsProcgenConstraintState.Missed, "NearLandingUnavailable", default);
        if (!clean.Contains(start.Value) || !clean.Contains(goal.Value))
            return (KsProcgenConstraintState.Missed, "NearLandingBlocked", default);
        if (Math.Abs((long) start.Value.X - goal.Value.X) + Math.Abs((long) start.Value.Y - goal.Value.Y) >
            relation.MaximumDistance)
            return (KsProcgenConstraintState.Missed, "NearPathUnavailable", default);
        var parents = new Dictionary<Vector2i, Vector2i> { [start.Value] = start.Value };
        var distances = new Dictionary<Vector2i, int> { [start.Value] = 0 };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start.Value);
        while (queue.TryDequeue(out var cell))
        {
            var distance = distances[cell];
            if (cell == goal.Value)
            {
                var cells = new List<Vector2i> { cell };
                while (parents[cell] != cell)
                {
                    cell = parents[cell];
                    cells.Add(cell);
                }
                cells.Reverse();
                return (distance >= relation.MinimumDistance ? KsProcgenConstraintState.Satisfied :
                    KsProcgenConstraintState.Missed, distance >= relation.MinimumDistance ? null :
                    "NearDistanceBelowMinimum", new KsProcgenRelationPath(cells));
            }
            if (distance >= relation.MaximumDistance)
                continue;
            if (!budget.Expand())
                return (KsProcgenConstraintState.Unverified, "NearPathBudget", default);
            foreach (var face in Faces)
            {
                var nextX = (long) cell.X + face.X;
                var nextY = (long) cell.Y + face.Y;
                if (nextX is < int.MinValue or > int.MaxValue || nextY is < int.MinValue or > int.MaxValue)
                    continue;
                var next = new Vector2i((int) nextX, (int) nextY);
                if (!clean.Contains(next) || !parents.TryAdd(next, cell))
                    continue;
                distances[next] = distance + 1;
                queue.Enqueue(next);
            }
        }
        return (KsProcgenConstraintState.Missed, "NearPathUnavailable", default);
    }

    private static Vector2i? Landing(KsProcgenEntityProposal entity, KsProcgenResolvedAssembly assembly)
    {
        if (entity.InteractionApproach.HasValue)
            return entity.InteractionApproach;
        if (entity.DeclaredApproachLanding.HasValue)
            return entity.DeclaredApproachLanding;
        var member = assembly.Members.FirstOrDefault(member => member.Id == entity.EntryId);
        if (member?.ApproachLanding.HasValue == true)
            return ResolveDeclaredLanding(entity.Cell, entity.QuarterTurns, member.ApproachLanding);
        return entity.Movement == KsProcgenMovementClass.Clear ? entity.Cell : null;
    }

    internal static Vector2i? ResolveDeclaredLanding(Vector2i origin, int turns, Vector2i? localLanding)
    {
        if (localLanding is { } local)
        {
            var rotated = KsProcgenInteractionFacingPlanner.RotateFootprintOffset(local, turns);
            var landingX = (long) origin.X + rotated.X;
            var landingY = (long) origin.Y + rotated.Y;
            if (landingX is < int.MinValue or > int.MaxValue || landingY is < int.MinValue or > int.MaxValue)
                return null;
            return new Vector2i((int) landingX, (int) landingY);
        }
        return null;
    }

    private static int Distance(Vector2i first, Vector2i second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private static bool FacesTarget(Vector2i subject, int turn, Vector2i target)
    {
        var delta = target - subject;
        var face = Faces[turn];
        return delta.X * face.Y == delta.Y * face.X && delta.X * face.X + delta.Y * face.Y > 0;
    }
}
