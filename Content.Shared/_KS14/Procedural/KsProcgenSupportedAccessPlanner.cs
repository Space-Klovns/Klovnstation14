using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Unit tile centers equal CellCenterOrigin + (X,Y), in the pose planner's common frame.</summary>
public sealed record KsProcgenRoomAccessMask(
    IReadOnlySet<Vector2i> Floor, IReadOnlySet<Vector2i> Walls, IReadOnlySet<Vector2i> Blocking,
    IReadOnlySet<Vector2i> Occupied, IReadOnlySet<Vector2i> Network, Vector2 CellCenterOrigin);

public enum KsProcgenSupportedAccessStatus : byte
{
    Candidate,
    InvalidInput,
    Rejected,
    UnsupportedContent,
    BudgetExceeded,
}

public sealed record KsProcgenSupportedFootprint(string MemberId, IReadOnlyList<Vector2i> Cells);
public sealed record KsProcgenSupportedAccess(
    string MemberId, KsProcgenPlacementLayer Layer, int QuarterTurns, Vector2i Approach,
    string? AssociatedSeatMemberId, KsProcgenRelationPath CleanPath);

public sealed class KsProcgenSupportedAccessPlan
{
    public KsProcgenSupportedAccessStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenAssemblyPosePlan? Poses { get; init; }
    public IReadOnlyList<KsProcgenSupportedFootprint> Footprints { get; init; } = [];
    public IReadOnlyList<KsProcgenSupportedAccess> Access { get; init; } = [];
    public IReadOnlyList<KsProcgenAssemblyRelationWitness> Relations { get; init; } = [];
    public int PreferredRelationsSatisfied { get; init; }
    public int PreferredRelationsApplicable { get; init; }
    public ulong AccessHash { get; init; }
    public int ExpandedCells { get; init; }
    public int LandingProbes { get; init; }
    public bool EnginePlacementVerified => false;
}

/// <summary>
/// Cardinal operating approaches for selected exposed floor/surface members, including machines
/// standing on nontraversible supports. Declared unit footprints are conservatively rasterized.
/// Neither legal layered overlap nor engine interaction range/direction is established here.
/// </summary>
public static class KsProcgenSupportedAccessPlanner
{
    private static readonly Vector2i[] Faces = [new(0, -1), new(-1, 0), new(0, 1), new(1, 0)];

    internal static bool ValidMask(KsProcgenRoomAccessMask room) => room.Floor.Count is >= 1 and <= 4096 &&
        room.Walls.Count <= 8192 && room.Blocking.Count <= 8192 && room.Occupied.Count <= 8192 &&
        room.Network.Count is >= 1 and <= 4096 && FinitePosition(room.CellCenterOrigin) &&
        new[] { room.Floor, room.Walls, room.Blocking, room.Occupied, room.Network }.All(cells =>
            cells.All(cell => Math.Abs((long) cell.X) <= 1_000_000 && Math.Abs((long) cell.Y) <= 1_000_000));

    public static KsProcgenSupportedAccessPlan Plan(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, KsProcgenAssemblyCapabilityReport capabilities,
        IReadOnlyList<KsProcgenFloorRootPose> floorRoots,
        IReadOnlyList<KsProcgenMemberOrientation> orientations, int assemblyQuarterTurns,
        KsProcgenRoomAccessMask room, int maximumExpandedCells = 4096, int maximumLandingProbes = 4096)
    {
        var budget = new KsProcgenRelationPathBudget(maximumExpandedCells: Math.Clamp(maximumExpandedCells, 0, 65_536));
        var landingProbes = 0;
        KsProcgenSupportedAccessPlan Fail(KsProcgenSupportedAccessStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Supported assembly has no accepted operating access geometry."),
            ExpandedCells = budget.ExpandedCells,
            LandingProbes = landingProbes,
        };
        if (maximumExpandedCells is < 0 or > 65_536 || maximumLandingProbes is < 0 or > 65_536 || !ValidMask(room))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedAccessMask");
        var poses = KsProcgenAssemblyPosePlanner.Plan(assembly, selectedMemberIds, capabilities,
            floorRoots, orientations, assemblyQuarterTurns);
        if (poses.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation)
            return Fail(poses.Status == KsProcgenAssemblySupportStatus.InvalidInput ?
                KsProcgenSupportedAccessStatus.InvalidInput : KsProcgenSupportedAccessStatus.Rejected, poses.Issue!.Code);
        var definitions = assembly.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);
        if (assembly.Relations.Any(relation => !Enum.IsDefined(relation.Kind) || !Enum.IsDefined(relation.Severity) ||
                !Enum.IsDefined(relation.ApproachPolicy) || !definitions.ContainsKey(relation.Subject) ||
                relation.MinimumDistance < 0 || relation.MaximumDistance < relation.MinimumDistance || relation.MaximumDistance > 64 ||
                relation.Target != null && !definitions.ContainsKey(relation.Target)))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedAccessRelations");
        if (assembly.Relations.Any(relation => selectedMemberIds.Contains(relation.Subject) && !SupportedRelation(relation.Kind)))
            return Fail(KsProcgenSupportedAccessStatus.UnsupportedContent, "SupportedAccessSpatialRelationUnsupported");
        var projected = new Dictionary<string, HashSet<Vector2i>>(StringComparer.Ordinal);
        var selected = poses.Members.ToDictionary(pose => pose.Support.MemberId, StringComparer.Ordinal);
        var occupied = new HashSet<Vector2i>(room.Occupied);
        var blocked = new HashSet<Vector2i>(room.Blocking);
        blocked.UnionWith(room.Walls);
        foreach (var pose in poses.Members)
        {
            var definition = definitions[pose.Support.MemberId];
            if (!Enum.IsDefined(definition.Entry.Movement) || !Enum.IsDefined(definition.Entry.Role) ||
                definition.Entry.Footprint.Count is < 1 or > 16 ||
                definition.Entry.Footprint.Distinct().Count() != definition.Entry.Footprint.Count ||
                !definition.Entry.Footprint.Contains(Vector2i.Zero) ||
                definition.Entry.Footprint.Any(cell => Math.Abs((long) cell.X) > 8 || Math.Abs((long) cell.Y) > 8) ||
                definition.ApproachLanding is { } declaredLanding &&
                (Math.Abs((long) declaredLanding.X) > 9 || Math.Abs((long) declaredLanding.Y) > 9))
                return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidSupportedAccessFootprint");
            if (!pose.Support.Exposed)
            {
                if (NeedsAccess(definition, assembly))
                    return Fail(KsProcgenSupportedAccessStatus.UnsupportedContent, "ContainedOperatingAccessUnsupported");
                continue;
            }
            var relative = pose.Position - room.CellCenterOrigin;
            if (!FinitePosition(relative))
                return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "SupportedAccessFrameOutOfRange");
            var cells = new HashSet<Vector2i>();
            foreach (var local in definition.Entry.Footprint)
            {
                var rotated = KsProcgenInteractionFacingPlanner.RotateFootprintOffset(local, pose.QuarterTurns);
                var centerX = (double) relative.X + rotated.X;
                var centerY = (double) relative.Y + rotated.Y;
                // Each authored unit box overlaps one aligned tile, or up to four shifted tiles.
                // Strict positive-area overlap excludes merely touching neighboring tile boundaries.
                for (var y = (int) Math.Floor(centerY); y <= (int) Math.Ceiling(centerY + 1.0) - 1; y++)
                for (var x = (int) Math.Floor(centerX); x <= (int) Math.Ceiling(centerX + 1.0) - 1; x++)
                    cells.Add(new(x, y));
            }
            if (cells.Any(cell => !room.Floor.Contains(cell) || room.Walls.Contains(cell)))
                return Fail(KsProcgenSupportedAccessStatus.Rejected, "SupportedFootprintOutsideRoom");
            if (definition.Entry.Role == KsProcgenEntityRole.Seat &&
                (pose.Support.Layer != KsProcgenPlacementLayer.Floor || cells.Count != 1))
                return Fail(KsProcgenSupportedAccessStatus.UnsupportedContent, "SupportedSeatFootprintUnsupported");
            projected.Add(pose.Support.MemberId, cells);
            occupied.UnionWith(cells);
            if (definition.Entry.Movement != KsProcgenMovementClass.Clear)
                blocked.UnionWith(cells);
        }
        var clean = new HashSet<Vector2i>(room.Floor);
        clean.ExceptWith(blocked);
        if (room.Network.Any(cell => !clean.Contains(cell)))
            return Fail(KsProcgenSupportedAccessStatus.Rejected, "SupportedAccessNetworkBlocked");
        var access = new List<KsProcgenSupportedAccess>();
        var choices = new List<(KsProcgenSupportedMemberPose Pose,
            IReadOnlyList<(Vector2i Cell, string? SeatId)> Landings, HashSet<Vector2i> Clean)>();
        foreach (var pose in poses.Members.OrderBy(pose => pose.Support.MemberId, StringComparer.Ordinal))
        {
            var definition = definitions[pose.Support.MemberId];
            if (!pose.Support.Exposed || !NeedsAccess(definition, assembly))
                continue;
            var footprint = projected[pose.Support.MemberId];
            var seat = definition.Entry.Role == KsProcgenEntityRole.Seat;
            var applicable = assembly.Relations.Where(relation => relation.Subject == definition.Id).ToArray();
            var seatRelations = applicable.Where(relation => relation.Kind == KsProcgenRelationKind.UsesSeat).ToArray();
            if (seat && applicable.Any(relation => relation.Kind is KsProcgenRelationKind.UsesSeat or KsProcgenRelationKind.FacingOpenSpace))
                return Fail(KsProcgenSupportedAccessStatus.UnsupportedContent, "ChairOperatingRelationUnsupported");
            var associated = seatRelations.Where(relation => relation.Target != null && projected.ContainsKey(relation.Target) &&
                definitions[relation.Target].Entry is { Role: KsProcgenEntityRole.Seat, Movement: KsProcgenMovementClass.Clear })
                .Select(relation => relation.Target!).Distinct(StringComparer.Ordinal).ToArray();
            if (seatRelations.Any(relation => relation.Severity == KsProcgenRelationSeverity.Required &&
                    !associated.Contains(relation.Target, StringComparer.Ordinal)))
                return Fail(KsProcgenSupportedAccessStatus.Rejected, "SupportedAccessSeatUnavailable");
            var allowed = new List<(Vector2i Cell, string? SeatId)>();
            foreach (var cell in KsProcgenGeometry.SortCells(footprint))
            foreach (var face in seat ? Faces : new[] { Faces[pose.QuarterTurns] })
            {
                var landing = cell + face;
                if (footprint.Contains(landing) || !clean.Contains(landing))
                    continue;
                var seatId = associated.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault(id => projected[id].Contains(landing));
                if (!seat && seatId != null && (room.Occupied.Contains(landing) ||
                    projected.Any(pair => pair.Key != seatId && pair.Value.Contains(landing))))
                    continue;
                if (!seat && (occupied.Contains(landing) && seatId == null ||
                    seatRelations.Any(relation => relation.Severity == KsProcgenRelationSeverity.Required && relation.Target != seatId) ||
                    applicable.Any(relation => relation.Kind == KsProcgenRelationKind.FacingOpenSpace &&
                        relation.Severity == KsProcgenRelationSeverity.Required &&
                        relation.ApproachPolicy == KsProcgenApproachPolicy.EmptyFloor && occupied.Contains(landing))))
                    continue;
                if (definition.ApproachLanding.HasValue)
                {
                    var local = KsProcgenInteractionFacingPlanner.RotateFootprintOffset(definition.ApproachLanding.Value, pose.QuarterTurns);
                    var explicitPosition = pose.Position - room.CellCenterOrigin + new Vector2((float) local.X, (float) local.Y);
                    if (explicitPosition != new Vector2((float) landing.X, (float) landing.Y))
                        continue;
                }
                allowed.Add((landing, seat ? null : seatId));
            }
            var memberClean = new HashSet<Vector2i>(clean);
            // A machine cannot be approached by walking through it or its own support footprint.
            // Chairs may be traversible, but their access proof must not walk through that chair.
            memberClean.ExceptWith(footprint);
            string? ancestor = pose.Support.ParentMemberId;
            while (ancestor != null)
            {
                if (projected.TryGetValue(ancestor, out var supportCells))
                    memberClean.ExceptWith(supportCells);
                ancestor = selected[ancestor].Support.ParentMemberId;
            }
            if (allowed.Count == 0)
                return Fail(KsProcgenSupportedAccessStatus.Rejected, "SupportedCleanApproachUnavailable");
            choices.Add((pose, allowed.Distinct().OrderByDescending(candidate => candidate.SeatId != null)
                .ThenBy(candidate => candidate.Cell.Y).ThenBy(candidate => candidate.Cell.X).ToArray(), memberClean));
        }
        var footprints = projected.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new KsProcgenSupportedFootprint(pair.Key, KsProcgenGeometry.SortCells(pair.Value))).ToList();
        var spatialRelations = assembly.Relations.Where(relation => relation.Kind is not
            (KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer)).ToArray();
        // The existing spatial evaluator uses exact cardinal tile origins. Do not round a fractional
        // support offset into fabricated facing/adjacency evidence, or project hidden contents.
        foreach (var relation in spatialRelations)
        {
            if (!selected.ContainsKey(relation.Subject))
                continue;
            foreach (var endpoint in relation.Target == null ? new[] { relation.Subject } : new[] { relation.Subject, relation.Target })
            {
                if (!selected.TryGetValue(endpoint, out var pose))
                    continue;
                var relative = pose.Position - room.CellCenterOrigin;
                if (!pose.Support.Exposed)
                    return Fail(KsProcgenSupportedAccessStatus.UnsupportedContent, "ContainedSpatialRelationUnsupported");
                if (relative.X != MathF.Truncate(relative.X) || relative.Y != MathF.Truncate(relative.Y))
                    return Fail(KsProcgenSupportedAccessStatus.UnsupportedContent, "FractionalSpatialRelationUnsupported");
            }
        }
        string? exhaustedCode = null;
        var completedModel = false;
        KsProcgenAssemblyRelationEvaluation? EvaluateRelations()
        {
            var trial = poses.Members.Where(pose => pose.Support.Exposed).Select(pose =>
            {
                var member = definitions[pose.Support.MemberId];
                var relative = pose.Position - room.CellCenterOrigin;
                var cell = new Vector2i((int) MathF.Floor(relative.X), (int) MathF.Floor(relative.Y));
                return new KsProcgenEntityProposal("supported-access", member.Id, member.Entry.Entity,
                    member.Entry.Role, member.Entry.Movement, cell, pose.QuarterTurns,
                    access.FirstOrDefault(witness => witness.MemberId == member.Id)?.Approach,
                    Footprint: new KsProcgenEntityFootprint(projected[member.Id]), CoreId: assembly.AssemblyId,
                    AssemblyId: assembly.AssemblyId, VariantId: assembly.VariantId);
            }).ToArray();
            // Selected hidden members have already passed the support planner's required-selection gate.
            // They are absent from this projected spatial domain; omitted optional members remain defined
            // so the evaluator can report NotApplicable subjects versus missing required targets.
            var spatialAssembly = assembly with
            {
                Members = assembly.Members.Where(member => !selected.TryGetValue(member.Id, out var pose) || pose.Support.Exposed).ToArray(),
                Relations = spatialRelations,
            };
            var relationWitnesses = new List<KsProcgenAssemblyRelationWitness>();
            var requiredSatisfied = true;
            var preferredSatisfied = 0;
            var preferredApplicable = 0;
            foreach (var relation in spatialRelations.OrderBy(relation => relation.Id, StringComparer.Ordinal))
            {
                var relationBlocking = new HashSet<Vector2i>(blocked);
                if (relation.Kind == KsProcgenRelationKind.Near)
                {
                    // Even a support authored as clear cannot become a shortcut between the operating
                    // landings of its supported item. Preserve both endpoints' complete support chains.
                    foreach (var endpoint in relation.Target == null ? new[] { relation.Subject } : new[] { relation.Subject, relation.Target })
                    {
                        if (!selected.TryGetValue(endpoint, out var endpointPose))
                            continue;
                        var ancestor = endpointPose.Support.ParentMemberId;
                        while (ancestor != null)
                        {
                            if (projected.TryGetValue(ancestor, out var supportCells))
                                relationBlocking.UnionWith(supportCells);
                            ancestor = selected[ancestor].Support.ParentMemberId;
                        }
                    }
                }
                var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate("supported-access", assembly.AssemblyId,
                    0, spatialAssembly, trial, room.Walls,
                    floor: room.Floor, blockingCells: relationBlocking, pathBudget: budget, relationsToEvaluate: [relation]);
                if (evaluation.PathSearchTruncated)
                {
                    exhaustedCode = "SupportedRelationPathBudget";
                    return null;
                }
                requiredSatisfied &= evaluation.RequiredSatisfied;
                preferredSatisfied += evaluation.PreferredSatisfied;
                preferredApplicable += evaluation.PreferredApplicable;
                relationWitnesses.AddRange(evaluation.Witnesses);
            }
            return new KsProcgenAssemblyRelationEvaluation
            {
                RequiredSatisfied = requiredSatisfied, PreferredSatisfied = preferredSatisfied,
                PreferredApplicable = preferredApplicable, Witnesses = relationWitnesses.AsReadOnly(),
            };
        }
        KsProcgenAssemblyRelationEvaluation? relations = null;
        bool ChooseLandings(int index)
        {
            if (index == choices.Count)
            {
                completedModel = true;
                var evaluation = EvaluateRelations();
                if (evaluation?.RequiredSatisfied != true)
                    return false;
                relations = evaluation;
                return true;
            }
            var choice = choices[index];
            foreach (var candidate in choice.Landings)
            {
                if (landingProbes >= maximumLandingProbes)
                {
                    exhaustedCode = "SupportedLandingProbeBudget";
                    return false;
                }
                landingProbes++;
                var path = FindPath(candidate.Cell, choice.Clean, room.Network, budget);
                if (budget.Truncated)
                {
                    exhaustedCode = "SupportedAccessPathBudget";
                    return false;
                }
                if (path.Cells.Count == 0)
                    continue;
                access.Add(new(choice.Pose.Support.MemberId, choice.Pose.Support.Layer,
                    choice.Pose.QuarterTurns, candidate.Cell, candidate.SeatId, path));
                if (ChooseLandings(index + 1))
                    return true;
                access.RemoveAt(access.Count - 1);
                if (exhaustedCode != null)
                    return false;
            }
            return false;
        }
        if (!ChooseLandings(0))
            return exhaustedCode != null ? Fail(KsProcgenSupportedAccessStatus.BudgetExceeded, exhaustedCode) :
                Fail(KsProcgenSupportedAccessStatus.Rejected, completedModel ?
                    "SupportedSpatialRelationUnmet" : "SupportedCleanApproachUnavailable");
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-supported-access-v4");
        hash.AddInt(unchecked((int) poses.PoseHash));
        hash.AddInt(unchecked((int) (poses.PoseHash >> 32)));
        hash.AddInt(BitConverter.SingleToInt32Bits(room.CellCenterOrigin.X));
        hash.AddInt(BitConverter.SingleToInt32Bits(room.CellCenterOrigin.Y));
        foreach (var cells in new[] { room.Floor, room.Walls, room.Blocking, room.Occupied, room.Network })
            AddCells(KsProcgenGeometry.SortCells(cells));
        hash.AddInt(footprints.Count);
        foreach (var footprint in footprints)
        {
            var definition = definitions[footprint.MemberId];
            hash.AddString(footprint.MemberId);
            hash.AddInt((int) definition.Entry.Movement);
            hash.AddInt((int) definition.Entry.Role);
            AddCells(footprint.Cells);
        }
        hash.AddInt(access.Count);
        foreach (var witness in access)
        {
            hash.AddString(witness.MemberId);
            hash.AddInt((int) witness.Layer);
            hash.AddInt(witness.QuarterTurns);
            hash.AddInt(witness.Approach.X);
            hash.AddInt(witness.Approach.Y);
            hash.AddString(witness.AssociatedSeatMemberId ?? string.Empty);
            AddCells(witness.CleanPath.Cells);
        }
        // Include authored semantics even when the same poses happen to satisfy both declarations.
        hash.AddInt(assembly.Relations.Count);
        foreach (var relation in assembly.Relations.OrderBy(relation => relation.Id, StringComparer.Ordinal))
        {
            hash.AddString(relation.Id);
            hash.AddString(relation.Subject);
            hash.AddString(relation.Target ?? string.Empty);
            hash.AddInt((int) relation.Kind);
            hash.AddInt((int) relation.Severity);
            hash.AddInt((int) relation.ApproachPolicy);
            hash.AddInt(relation.MinimumDistance);
            hash.AddInt(relation.MaximumDistance);
            hash.AddString(relation.Slot ?? string.Empty);
            hash.AddString(relation.ContainerId ?? string.Empty);
        }
        hash.AddInt(relations!.Witnesses.Count);
        foreach (var witness in relations.Witnesses)
        {
            hash.AddString(witness.RelationId);
            hash.AddInt((int) witness.State);
            hash.AddString(witness.ReasonCode ?? string.Empty);
            AddNullableCell(witness.SubjectCell);
            AddNullableCell(witness.TargetCell);
            AddNullableCell(witness.FirstBackingWall);
            AddNullableCell(witness.SecondBackingWall);
            AddNullableCell(witness.InteractionApproach);
            hash.AddInt(witness.PathDistance ?? -1);
            AddCells(witness.CleanPath.Cells);
        }
        return new()
        {
            Status = KsProcgenSupportedAccessStatus.Candidate, Poses = poses,
            Footprints = footprints.AsReadOnly(), Access = access.AsReadOnly(), AccessHash = hash.Value,
            Relations = relations.Witnesses.ToList().AsReadOnly(), PreferredRelationsSatisfied = relations.PreferredSatisfied,
            PreferredRelationsApplicable = relations.PreferredApplicable,
            ExpandedCells = budget.ExpandedCells,
            LandingProbes = landingProbes,
        };

        void AddNullableCell(Vector2i? cell)
        {
            hash.AddInt(cell.HasValue ? 1 : 0);
            if (cell is { } value)
            {
                hash.AddInt(value.X);
                hash.AddInt(value.Y);
            }
        }

        void AddCells(IReadOnlyList<Vector2i> cells)
        {
            hash.AddInt(cells.Count);
            foreach (var cell in cells)
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
        }
    }

    internal static bool SupportedRelation(KsProcgenRelationKind kind) => kind is
        KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer or
        KsProcgenRelationKind.FacingOpenSpace or KsProcgenRelationKind.UsesSeat or
        KsProcgenRelationKind.AdjacentTo or KsProcgenRelationKind.FacingTarget or KsProcgenRelationKind.AtCorner or
        KsProcgenRelationKind.Near;

    private static bool NeedsAccess(KsProcgenResolvedAssemblyMember member, KsProcgenResolvedAssembly assembly) =>
        member.Entry.RequiresInteractionApproach || member.ApproachLanding.HasValue || member.Entry.Role == KsProcgenEntityRole.Seat ||
        assembly.Relations.Any(relation => relation.Subject == member.Id &&
            relation.Kind is KsProcgenRelationKind.UsesSeat or KsProcgenRelationKind.FacingOpenSpace);

    private static bool FinitePosition(Vector2 position) => float.IsFinite(position.X) && float.IsFinite(position.Y) &&
        Math.Abs(position.X) <= 1_000_000f && Math.Abs(position.Y) <= 1_000_000f;

    private static KsProcgenRelationPath FindPath(Vector2i target, IReadOnlySet<Vector2i> clean,
        IReadOnlySet<Vector2i> network, KsProcgenRelationPathBudget budget)
    {
        if (!clean.Contains(target))
            return default;
        var parents = new Dictionary<Vector2i, Vector2i>();
        var pending = new Queue<Vector2i>();
        foreach (var cell in KsProcgenGeometry.SortCells(network))
        {
            if (clean.Contains(cell) && parents.TryAdd(cell, cell))
                pending.Enqueue(cell);
        }
        while (pending.TryDequeue(out var cell))
        {
            if (!budget.Expand())
                return default;
            if (cell == target)
            {
                var path = new List<Vector2i> { cell };
                while (parents[cell] != cell)
                {
                    cell = parents[cell];
                    path.Add(cell);
                }
                path.Reverse();
                return new(path);
            }
            foreach (var face in Faces)
            {
                var next = cell + face;
                if (clean.Contains(next) && parents.TryAdd(next, cell))
                    pending.Enqueue(next);
            }
        }
        return default;
    }
}
