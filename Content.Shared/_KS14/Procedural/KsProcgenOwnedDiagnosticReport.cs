using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenDiagnosticCell(int X, int Y);
public sealed record KsProcgenDiagnosticDoor(string FirstZoneId, string SecondZoneId,
    KsProcgenDiagnosticCell Threshold, KsProcgenDiagnosticCell InsideApproach, KsProcgenDiagnosticCell OutsideApproach);
public sealed record KsProcgenDiagnosticRoom(string Id, string Kind, IReadOnlyList<string> EntranceGroups,
    string ThemeId, string TilePackId, string WallPackId, string? LightingPackId, IReadOnlyList<KsProcgenDiagnosticCell> Cells);
public sealed record KsProcgenDiagnosticEntity(string RegionId, string PackId, string EntryId,
    string EntityId, string? CoreId, string? AssemblyId, string? VariantId, int ClusterIndex,
    string Role, string Movement, int QuarterTurns, KsProcgenDiagnosticCell Cell,
    IReadOnlyList<KsProcgenDiagnosticCell> Footprint, KsProcgenDiagnosticCell? Approach);
public sealed record KsProcgenDiagnosticWork(int ExpandedCells, int FurnishingProbes, int OrderProbes,
    int PartitionMerges, int PartitionSeamProbes, int FixtureSelections, int ConfigurationProbes, int LayoutProbes);

/// <summary>Detached bounded author review data; no field is a publication or engine-success receipt.</summary>
public sealed class KsProcgenOwnedDiagnosticReport
{
    public int SchemaVersion => 1;
    public string RequestId { get; init; } = string.Empty;
    public int Seed { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }
    public string SemanticHash { get; init; } = "0";
    public string? ConfigurationId { get; init; }
    public bool EntriesTruncated { get; init; }
    public bool GeometryTruncated { get; init; }
    public bool Published => false;
    public bool EngineAccessVerified => false;
    public bool GasClosureVerified => false;
    public bool WorkingLightingVerified => false;
    public bool GraphWorkFullyMetered => false;
    public bool SearchComplete => false;
    public KsProcgenDiagnosticWork Work { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    public IReadOnlyList<KsProcgenConstraintOutcome> Constraints { get; init; } = [];
    public IReadOnlyList<KsProcgenOwnedStageOutcome> Stages { get; init; } = [];
    public IReadOnlyList<KsProcgenOwnedConfigurationAttempt> Attempts { get; init; } = [];
    public IReadOnlyList<KsProcgenSizeGoalPartitionAttempt> SizeGoalAttempts { get; init; } = [];
    public IReadOnlyList<KsProcgenOwnedFallback> Fallbacks { get; init; } = [];
    public IReadOnlyList<KsProcgenSizeMixOutcome> SizeMix { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> RequestedCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> FloorCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> WallCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> ProtectedCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> PreservedCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> VoidCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> EntranceCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticCell> WindowCells { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticDoor> Doors { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticRoom> Rooms { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticEntity> Entities { get; init; } = [];
}

public static class KsProcgenOwnedDiagnosticReportBuilder
{
    public static KsProcgenOwnedDiagnosticReport Build(KsProcgenRequest request, KsProcgenOwnedInteriorResult result,
        int maximumEntries = 4096, int maximumGeometryCells = 8192)
    {
        if (request == null || result == null || maximumEntries is < 1 or > 65_536 || maximumGeometryCells is < 1 or > 65_536)
            throw new ArgumentException("Diagnostic report requires a request, result and bounded limits.");
        var entries = 0;
        var entriesTruncated = false;
        var geometryTruncated = false;
        IReadOnlyList<T> TakeEntries<T>(IEnumerable<T> source)
        {
            var copied = new List<T>();
            foreach (var entry in source)
            {
                if (entries >= maximumEntries) { entriesTruncated = true; break; }
                entries++;
                copied.Add(entry);
            }
            return KsProcgenReadOnly.Copy(copied);
        }
        IReadOnlyList<KsProcgenDiagnosticCell> Cells(IEnumerable<Vector2i> source)
        {
            var sorted = KsProcgenGeometry.SortCells(source.Distinct());
            if (sorted.Count > maximumGeometryCells) geometryTruncated = true;
            return sorted.Take(maximumGeometryCells).Select(cell => new KsProcgenDiagnosticCell(cell.X, cell.Y)).ToReadOnlyList();
        }
        var normalized = KsProcgenGeometry.TryNormalize(request, out var shape, out var normalizationIssue);
        var candidate = normalized && result.Status == KsProcgenOwnedPlanStatus.Candidate && result.SemanticHash != 0 &&
            result.Floor?.Status == KsProcgenEntranceSceneStatus.Candidate && result.Floor.Accepted != null &&
            result.Partition?.Status == KsProcgenEntranceSceneStatus.Candidate &&
            result.Structure?.Status == KsProcgenGroupWindowStatus.Planned && result.Lighting != null &&
            result.Themes?.Status == KsProcgenThemeAssignmentStatus.Selected && result.Access?.Status == KsProcgenEntranceSceneStatus.Candidate;
        var constraints = new List<KsProcgenConstraintOutcome>
        {
            new("DeclaredShape", normalized ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Missed, true,
                ReasonCode: normalizationIssue?.Code),
            new("PlannedCleanAccess", candidate ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Unverified, true),
            new("EngineCleanAccess", KsProcgenConstraintState.Unverified, true, ReasonCode: "EngineAccessNotVerified"),
            new("GasClosure", KsProcgenConstraintState.Unverified, true, ReasonCode: "GasClosureNotVerified"),
            new("WorkingLighting", KsProcgenConstraintState.Unverified, false, ReasonCode: "WorkingLightingNotVerified"),
            new("FullWorkAccounting", KsProcgenConstraintState.Unverified, false, ReasonCode: "WorkNotFullyMetered"),
        };
        foreach (var outcome in result.SizeMix)
            constraints.Add(new($"RoomSize/{outcome.GoalId}", outcome.AchievedCount == outcome.RequestedCount
                ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Missed,
                !(request.FallbackPolicy?.AllowRoomSizeShortfall ?? true),
                outcome.RequestedCount.ToString(CultureInfo.InvariantCulture), outcome.AchievedCount.ToString(CultureInfo.InvariantCulture)));
        var constraintRecords = TakeEntries(constraints);
        var stages = TakeEntries(result.Stages);
        var attempts = TakeEntries(result.Attempts.Select(attempt => attempt with { Fallbacks = TakeEntries(attempt.Fallbacks) }));
        var goalAttempts = TakeEntries(result.Partition?.SizeGoalAttempts ?? []);
        var fallbacks = TakeEntries(result.Fallbacks);
        var sizes = TakeEntries(result.SizeMix);
        var requested = Cells(shape?.TargetCells ?? []);
        var preserved = Cells(shape?.PreservedCells ?? []);
        var voids = Cells(shape?.VoidCells ?? []);
        var entrances = Cells(shape?.Entrances.SelectMany(entrance => entrance.ThresholdCells) ?? []);
        var floor = Cells(candidate ? result.Partition!.FloorCells : []);
        var walls = Cells(candidate ? result.Partition!.WallCells.Concat(result.Structure?.RemainingWalls.Select(wall => wall.Cell) ?? []) : []);
        var protectedCells = Cells(candidate ? result.Partition!.ProtectedCells : []);
        var windows = Cells(candidate ? result.Structure?.Windows.Select(window => window.Cell) ?? [] : []);
        KsProcgenDiagnosticCell Cell(Vector2i cell) => new(cell.X, cell.Y);
        var doors = TakeEntries(candidate ? result.Partition!.Regions.SelectMany(region => region.Partition.DoorOpenings)
            .Select(door => new KsProcgenDiagnosticDoor(door.FirstZoneId, door.SecondZoneId, Cell(door.Threshold),
                Cell(door.InsideApproach), Cell(door.OutsideApproach))) : []);
        var rooms = TakeEntries(candidate ? result.Themes!.Regions.OrderBy(region => region.Id, StringComparer.Ordinal)
            .Select(region => new KsProcgenDiagnosticRoom(region.Id, region.Kind.ToString(), KsProcgenReadOnly.Copy(region.EntranceGroupIds),
                region.Theme.ThemeId, region.Theme.TilePackId, region.Theme.WallPackId, region.Theme.LightingPackId, Cells(region.FloorCells))) : []);
        var entities = TakeEntries(candidate ? result.Furnishings.OrderBy(region => region.RegionId, StringComparer.Ordinal)
            .SelectMany(region => region.Proposal.Entities.OrderBy(entity => entity.PackId, StringComparer.Ordinal)
                .ThenBy(entity => entity.ClusterIndex).ThenBy(entity => entity.EntryId, StringComparer.Ordinal)
                .Select(entity => new KsProcgenDiagnosticEntity(region.RegionId, entity.PackId, entity.EntryId, entity.EntityId,
                    entity.CoreId, entity.AssemblyId, entity.VariantId, entity.ClusterIndex, entity.Role.ToString(), entity.Movement.ToString(),
                    entity.QuarterTurns, Cell(entity.Cell), Cells(entity.OccupiedCells),
                    entity.InteractionApproach is { } approach ? Cell(approach) : null))) : []);
        return new()
        {
            RequestId = string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 512 ? "[invalid-request]" : request.RequestId,
            Seed = request.Seed, Status = candidate ? result.Status.ToString() :
                result.Status == KsProcgenOwnedPlanStatus.Candidate ? "InvalidArtifact" : result.Status.ToString(),
            ReasonCode = normalizationIssue?.Code ?? result.Issue?.Code ??
                (!candidate && result.Status == KsProcgenOwnedPlanStatus.Candidate ? "IncompleteOwnedDiagnosticArtifact" : null),
            SemanticHash = candidate ? result.SemanticHash.ToString(CultureInfo.InvariantCulture) : "0",
            ConfigurationId = candidate ? result.Partition!.Analysis?.ConfigurationId : null,
            EntriesTruncated = entriesTruncated, GeometryTruncated = geometryTruncated,
            Work = new(result.ExpandedCells, result.FurnishingProbes, result.OrderProbes, result.PartitionMerges,
                result.PartitionSeamProbes, result.FixtureSelections, result.ConfigurationProbes, result.LayoutProbes),
            Constraints = constraintRecords, Stages = stages, Attempts = attempts, SizeGoalAttempts = goalAttempts,
            Fallbacks = fallbacks, SizeMix = sizes, RequestedCells = requested, FloorCells = floor,
            WallCells = walls, ProtectedCells = protectedCells, PreservedCells = preserved, VoidCells = voids,
            EntranceCells = entrances, WindowCells = windows, Doors = doors, Rooms = rooms, Entities = entities,
        };
    }
}
