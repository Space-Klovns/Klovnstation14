using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenMode : byte
{
    Procedural,
    Prefabs,
    Hybrid,
}

public enum KsProcgenGeometryMode : byte
{
    Footprint,
    InteriorFill,
}

public enum KsProcgenConnectivityPolicy : byte
{
    SingleNetwork,
    PerIsland,
    DeclaredNetworks,
}

public enum KsProcgenStatus : byte
{
    Success,
    Degraded,
    NoOp,
    InvalidInput,
    NoFeasiblePlan,
    BudgetExceeded,
    Cancelled,
    MaterializationFailed,
}

/// <summary>
/// A half-open tile rectangle. The upper coordinate is excluded.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenTileRect
{
    [DataField(required: true)] public Vector2i Min;
    [DataField(required: true)] public Vector2i Max;
}

/// <summary>
/// Multiple ways to describe the same exact-cell target. Text uses top row at greatest Y.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenShapeSpec
{
    [DataField] public List<Vector2i> Cells = new();
    [DataField] public List<KsProcgenTileRect> AddRectangles = new();
    [DataField] public List<KsProcgenTileRect> SubtractRectangles = new();
    [DataField] public List<Vector2i> EnvelopeCells = new();
    [DataField] public List<Vector2i> PreservedCells = new();
    [DataField] public List<Vector2i> VoidCells = new();
    [DataField] public string? TextMask;
    [DataField] public Vector2i TextOrigin;
}

[DataDefinition]
public sealed partial class KsProcgenLimits
{
    [DataField] public int MaxCells = 65_536;
    [DataField] public int MaxAbsoluteCoordinate = 1_000_000;
}

/// <summary>
/// An explicit fixed-content reservation. Source/fingerprint identity is declared here;
/// inspection and copying of the actual authored content are later staging obligations.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenConstantPortSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public Vector2i Threshold;
    [DataField(required: true)] public Vector2i OutwardNormal;
}

[DataDefinition]
public sealed partial class KsProcgenConstantRegionSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string SourceId = string.Empty;
    [DataField(required: true)] public string ContentFingerprint = string.Empty;
    [DataField(required: true)] public List<Vector2i> LocalCells = new();
    [DataField] public Vector2i Origin;
    [DataField] public int QuarterTurns;
    [DataField] public List<KsProcgenConstantPortSpec> Ports = new();
}

/// <summary>
/// A soft count target for preliminary procedural room zones, not a fixed-size placement lattice.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenRoomSizeGoal
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public int MinCells = 4;
    [DataField] public int MaxCells = 12;
    [DataField] public int TargetCount = 1;
}

[DataDefinition]
public sealed partial class KsProcgenWindowGoal
{
    [DataField] public float ExteriorWindowFraction = 0.25f;
    [DataField] public bool HardFraction;
    [DataField] public int ToleranceCells = 1;
    [DataField] public int MinimumCount;
    [DataField] public int? MaximumCount;
}

/// <summary>
/// Permission for existing bounded, soft planning fallbacks. Disabling a permission promotes that
/// outcome to a planning failure; it does not relax an immutable or request-hard constraint.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenFallbackPolicy
{
    [DataField] public bool AllowMergedPartition = true;
    [DataField] public bool AllowRoomSizeShortfall = true;
    [DataField] public bool AllowSparseFurnishing = true;
    [DataField] public bool AllowSparseLighting = true;
}

/// <summary>
/// Phase A request contract. Later phases add library, theme, port, and constant-region fields.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenRequest
{
    [DataField(required: true)] public string RequestId = string.Empty;
    [DataField] public int Seed;
    [DataField] public KsProcgenMode Mode = KsProcgenMode.Procedural;
    [DataField] public KsProcgenGeometryMode GeometryMode = KsProcgenGeometryMode.Footprint;
    [DataField] public KsProcgenConnectivityPolicy ConnectivityPolicy = KsProcgenConnectivityPolicy.SingleNetwork;
    [DataField] public KsProcgenShapeSpec Shape = new();
    [DataField] public KsProcgenLimits Limits = new();
    [DataField] public List<Vector2i> RootCells = new();
    [DataField] public List<KsProcgenConstantRegionSpec> ConstantRegions = new();
    [DataField] public KsProcgenEntranceRequestSpec? EntranceDomain;
    [DataField] public List<KsProcgenRoomSizeGoal> SizeMix = new();
    [DataField] public KsProcgenWindowGoal WindowGoal = new();
    [DataField] public KsProcgenFallbackPolicy FallbackPolicy = new();
}

public sealed record KsProcgenIssue(string Code, string Message);
