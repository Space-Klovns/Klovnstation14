using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenDiagnosticCell(int X, int Y);
public sealed record KsProcgenDiagnosticDoor(string FirstZoneId, string SecondZoneId,
    KsProcgenDiagnosticCell Threshold, KsProcgenDiagnosticCell InsideApproach, KsProcgenDiagnosticCell OutsideApproach);
public sealed record KsProcgenDiagnosticRoom(string Id, string Kind, IReadOnlyList<string> EntranceGroups,
    string ThemeId, string TilePackId, string WallPackId, string? LightingPackId, IReadOnlyList<KsProcgenDiagnosticCell> Cells)
{
    public bool CellsTruncated { get; init; }
}
public sealed record KsProcgenDiagnosticEntity(string RegionId, string PackId, string EntryId,
    string EntityId, string? CoreId, string? AssemblyId, string? VariantId, int ClusterIndex,
    string Role, string Movement, int QuarterTurns, KsProcgenDiagnosticCell Cell,
    IReadOnlyList<KsProcgenDiagnosticCell> Footprint, KsProcgenDiagnosticCell? Approach)
{
    public bool FootprintTruncated { get; init; }
}
public sealed record KsProcgenDiagnosticWork(int ExpandedCells, int FurnishingProbes, int OrderProbes,
    int PartitionMerges, int PartitionSeamProbes, int FixtureSelections, int ConfigurationProbes, int LayoutProbes);
public sealed record KsProcgenDiagnosticMember(string MemberId, string PackId, string CoreId, int CopyIndex,
    string AssemblyId, string VariantId, string SourceMemberId, string Origin);
public sealed record KsProcgenDiagnosticPreference(string RegionId, int MissedAuthoredRelations, long DistancePenalty, bool SearchTruncated);
public sealed record KsProcgenDiagnosticDistance(string RegionId, KsProcgenDiagnosticMember Subject, KsProcgenDiagnosticMember Target,
    int MinimumDistance, int MaximumDistance, long Distance, long Penalty)
{
    public string Metric => "ManhattanAnchors";
}

/// <summary>Detached bounded author review data; no field is a publication or engine-success receipt.</summary>
public sealed class KsProcgenOwnedDiagnosticReport
{
    public int SchemaVersion => 3;
    public string RequestId { get; init; } = string.Empty;
    public int Seed { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }
    public string SemanticHash { get; init; } = "0";
    public string? ConfigurationId { get; init; }
    public bool EntriesTruncated { get; init; }
    public bool GeometryTruncated { get; init; }
    public bool InputWorkTruncated { get; init; }
    public int ProjectionInputElements { get; init; }
    public int EntriesWritten { get; init; }
    public int GeometryCellsWritten { get; init; }
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
    public IReadOnlyList<KsProcgenDiagnosticPreference> Preferences { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticDistance> Distances { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticSupportedEntity> SupportedEntities { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticOperatingAccess> OperatingAccess { get; init; } = [];
    public IReadOnlyList<KsProcgenDiagnosticRelation> Relations { get; init; } = [];
}
