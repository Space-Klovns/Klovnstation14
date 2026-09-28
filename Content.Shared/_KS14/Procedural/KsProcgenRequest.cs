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
}

public sealed record KsProcgenIssue(string Code, string Message);
