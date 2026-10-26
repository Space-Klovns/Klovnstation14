using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public static class KsProcgenOwnedDiagnosticReportBuilder
{
    public static KsProcgenOwnedDiagnosticReport Build(KsProcgenRequest request, KsProcgenOwnedInteriorResult result,
        int maximumEntries = 4096, int maximumGeometryCells = 8192, int maximumInputElements = 1_000_000)
    {
        if (request == null || result == null || maximumEntries is < 1 or > 65_536 || maximumGeometryCells is < 1 or > 65_536 ||
            maximumInputElements is < 1 or > 16_777_216)
            throw new ArgumentException("Diagnostic report requires bounded projection allowances.");
        var budget = new KsProcgenDiagnosticProjectionBudget(maximumEntries, maximumGeometryCells, maximumInputElements);
        var requestId = string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 512 ? "[invalid-request]" : request.RequestId;
        try
        {
            return Project(request, result, requestId, budget);
        }
        catch (KsProcgenDiagnosticProjectionLimitException)
        {
            // Never expose a partly assembled candidate after source processing stops.
            return new() { RequestId = requestId, Seed = request.Seed, Status = "BudgetExceeded", ReasonCode = "DiagnosticProjectionWorkBudget",
                InputWorkTruncated = true, EntriesTruncated = true, GeometryTruncated = true, ProjectionInputElements = budget.InputElements,
                Work = new(result.ExpandedCells, result.FurnishingProbes, result.OrderProbes, result.PartitionMerges,
                    result.PartitionSeamProbes, result.FixtureSelections, result.ConfigurationProbes, result.LayoutProbes) };
        }
    }

    private static KsProcgenOwnedDiagnosticReport Project(KsProcgenRequest request, KsProcgenOwnedInteriorResult result,
        string requestId, KsProcgenDiagnosticProjectionBudget budget)
    {
        var normalized = KsProcgenGeometry.TryNormalize(request, out var shape, out var normalizationIssue);
        var candidate = normalized && result.Status == KsProcgenOwnedPlanStatus.Candidate && result.SemanticHash != 0 &&
            result.Floor?.Status == KsProcgenEntranceSceneStatus.Candidate && result.Floor.Accepted != null &&
            result.Partition?.Status == KsProcgenEntranceSceneStatus.Candidate &&
            result.Structure?.Status == KsProcgenGroupWindowStatus.Planned && result.Lighting != null &&
            result.Themes?.Status == KsProcgenThemeAssignmentStatus.Selected && result.Access?.Status == KsProcgenEntranceSceneStatus.Candidate;
        var constraints = new List<KsProcgenConstraintOutcome>
        {
            new("DeclaredShape", normalized ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Missed, true, ReasonCode: normalizationIssue?.Code),
            new("PlannedCleanAccess", candidate ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Unverified, true),
            new("EngineCleanAccess", KsProcgenConstraintState.Unverified, true, ReasonCode: "EngineAccessNotVerified"),
            new("GasClosure", KsProcgenConstraintState.Unverified, true, ReasonCode: "GasClosureNotVerified"),
            new("WorkingLighting", KsProcgenConstraintState.Unverified, false, ReasonCode: "WorkingLightingNotVerified"),
            new("FullWorkAccounting", KsProcgenConstraintState.Unverified, false, ReasonCode: "WorkNotFullyMetered"),
        };
        foreach (var outcome in budget.Read(result.SizeMix))
            constraints.Add(new($"RoomSize/{outcome.GoalId}", outcome.AchievedCount == outcome.RequestedCount ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Missed,
                !(request.FallbackPolicy?.AllowRoomSizeShortfall ?? true), outcome.RequestedCount.ToString(CultureInfo.InvariantCulture),
                outcome.AchievedCount.ToString(CultureInfo.InvariantCulture)));
        var constraintRecords = budget.Take(constraints);
        var stages = budget.Take(result.Stages);
        var attempts = budget.Take(result.Attempts, attempt => attempt with { Fallbacks = budget.Take(attempt.Fallbacks) });
        var goalAttempts = budget.Take(result.Partition?.SizeGoalAttempts ?? []);
        var fallbacks = budget.Take(result.Fallbacks);
        var sizes = budget.Take(result.SizeMix);
        var requested = budget.Geometry(shape?.TargetCells ?? []).Cells;
        var preserved = budget.Geometry(shape?.PreservedCells ?? []).Cells;
        var voids = budget.Geometry(shape?.VoidCells ?? []).Cells;
        var entrances = budget.Geometry(shape?.Entrances.SelectMany(entrance => entrance.ThresholdCells) ?? []).Cells;
        var floor = budget.Geometry(candidate ? result.Partition!.FloorCells : []).Cells;
        var walls = budget.Geometry(candidate ? result.Partition!.WallCells.Concat(result.Structure?.RemainingWalls.Select(wall => wall.Cell) ?? []) : []).Cells;
        var protectedCells = budget.Geometry(candidate ? result.Partition!.ProtectedCells : []).Cells;
        var windows = budget.Geometry(candidate ? result.Structure?.Windows.Select(window => window.Cell) ?? [] : []).Cells;
        KsProcgenDiagnosticCell Cell(Vector2i cell) => new(cell.X, cell.Y);
        var doors = budget.Take(candidate ? budget.Read(result.Partition!.Regions).SelectMany(region => budget.Read(region.Partition.DoorOpenings)) : [],
            door => new KsProcgenDiagnosticDoor(door.FirstZoneId, door.SecondZoneId, Cell(door.Threshold), Cell(door.InsideApproach), Cell(door.OutsideApproach)));
        var regions = candidate ? budget.Read(result.Themes!.Regions).OrderBy(region => region.Id, StringComparer.Ordinal).ToArray() : [];
        var rooms = budget.Take(regions, region =>
        {
            var geometry = budget.Geometry(region.FloorCells);
            return new KsProcgenDiagnosticRoom(region.Id, region.Kind.ToString(), budget.Read(region.EntranceGroupIds),
                region.Theme.ThemeId, region.Theme.TilePackId, region.Theme.WallPackId, region.Theme.LightingPackId, geometry.Cells) { CellsTruncated = geometry.Truncated };
        });
        var furnishingRegions = candidate ? budget.Read(result.Furnishings).OrderBy(region => region.RegionId, StringComparer.Ordinal).ToArray() : [];
        var entities = budget.Take(furnishingRegions.SelectMany(region => budget.Read(region.Proposal.Entities)
            .OrderBy(entity => entity.PackId, StringComparer.Ordinal).ThenBy(entity => entity.ClusterIndex).ThenBy(entity => entity.EntryId, StringComparer.Ordinal)
            .Select(entity => (region.RegionId, Entity: entity))), item =>
        {
            var entity = item.Entity; var geometry = budget.Geometry(entity.OccupiedCells);
            return new KsProcgenDiagnosticEntity(item.RegionId, entity.PackId, entity.EntryId, entity.EntityId, entity.CoreId, entity.AssemblyId,
                entity.VariantId, entity.ClusterIndex, entity.Role.ToString(), entity.Movement.ToString(), entity.QuarterTurns,
                Cell(entity.Cell), geometry.Cells, entity.InteractionApproach is { } approach ? Cell(approach) : null) { FootprintTruncated = geometry.Truncated };
        });
        var contexts = furnishingRegions.Where(region => region.Proposal.Layered != null)
            .Select(region => new KsProcgenSupportedReviewContext(region.RegionId, region.Proposal.Layered!, budget)).ToArray();
        var preferences = budget.Take(contexts, context => new KsProcgenDiagnosticPreference(context.RegionId,
            context.Layered.Placement.PreferenceScore.MissedRelations, context.Layered.Placement.PreferenceScore.DistancePenalty,
            furnishingRegions.Single(region => region.RegionId == context.RegionId).Proposal.PreferenceSearchTruncated));
        var distances = budget.Take(contexts.SelectMany(context => budget.Read(context.Layered.Placement.RootPreferenceOutcomes)
            .Select(outcome => (Context: context, Outcome: outcome))), item => new KsProcgenDiagnosticDistance(item.Context.RegionId,
                item.Context.Member(item.Outcome.Subject), item.Context.Member(item.Outcome.Target), item.Outcome.MinimumDistance,
                item.Outcome.MaximumDistance, item.Outcome.Distance, item.Outcome.Penalty));
        var members = budget.Take(contexts.SelectMany(context => budget.Read(context.Layered.Access.Poses!.Members)
            .OrderBy(pose => pose.Support.MemberId, StringComparer.Ordinal).Select(pose => (Context: context, Pose: pose))),
            item => item.Context.Entity(item.Pose, budget));
        var access = budget.Take(contexts.SelectMany(context => budget.Read(context.Layered.Access.Access)
            .OrderBy(witness => witness.MemberId, StringComparer.Ordinal).Select(witness => (Context: context, Witness: witness))),
            item => new KsProcgenDiagnosticOperatingAccess(item.Context.RegionId, item.Context.Member(item.Witness.MemberId), item.Witness.Layer.ToString(),
                item.Witness.QuarterTurns, Cell(item.Witness.Approach), item.Witness.AssociatedSeatMemberId,
                budget.Geometry(item.Witness.CleanPath.Cells, preserveOrder: true)));
        var relations = budget.Take(contexts.SelectMany(context => budget.Read(context.Layered.Access.Relations)
            .OrderBy(witness => witness.RelationId, StringComparer.Ordinal).Select(witness => (Context: context, Witness: witness))), item =>
        {
            var witness = item.Witness;
            return new KsProcgenDiagnosticRelation(item.Context.RegionId, witness.RelationId, item.Context.Member(witness.SubjectId),
                witness.TargetId == null ? null : item.Context.Member(witness.TargetId), witness.Kind.ToString(), witness.Severity.ToString(), witness.State.ToString(),
                witness.ReasonCode, witness.MinimumDistance, witness.MaximumDistance, witness.PathDistance,
                budget.Geometry(witness.CleanPath.Cells, preserveOrder: true));
        });
        return new()
        {
            RequestId = requestId, Seed = request.Seed, Status = candidate ? result.Status.ToString() : result.Status == KsProcgenOwnedPlanStatus.Candidate ? "InvalidArtifact" : result.Status.ToString(),
            ReasonCode = normalizationIssue?.Code ?? result.Issue?.Code ?? (!candidate && result.Status == KsProcgenOwnedPlanStatus.Candidate ? "IncompleteOwnedDiagnosticArtifact" : null),
            SemanticHash = candidate ? result.SemanticHash.ToString(CultureInfo.InvariantCulture) : "0", ConfigurationId = candidate ? result.Partition!.Analysis?.ConfigurationId : null,
            EntriesTruncated = budget.EntriesTruncated, GeometryTruncated = budget.GeometryTruncated, EntriesWritten = budget.Entries,
            GeometryCellsWritten = budget.Cells, ProjectionInputElements = budget.InputElements,
            Work = new(result.ExpandedCells, result.FurnishingProbes, result.OrderProbes, result.PartitionMerges, result.PartitionSeamProbes,
                result.FixtureSelections, result.ConfigurationProbes, result.LayoutProbes), Constraints = constraintRecords, Stages = stages, Attempts = attempts,
            SizeGoalAttempts = goalAttempts, Fallbacks = fallbacks, SizeMix = sizes, RequestedCells = requested, FloorCells = floor, WallCells = walls,
            ProtectedCells = protectedCells, PreservedCells = preserved, VoidCells = voids, EntranceCells = entrances, WindowCells = windows,
            Doors = doors, Rooms = rooms, Entities = entities, Preferences = preferences, Distances = distances,
            SupportedEntities = members, OperatingAccess = access, Relations = relations,
        };
    }
}
