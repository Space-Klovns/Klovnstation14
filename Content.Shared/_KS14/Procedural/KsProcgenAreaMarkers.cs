using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Geometry/theme and declared entrance profile; routing and other goals remain pending.</summary>
[Prototype("ksProcgenAreaProfile")]
public sealed partial class KsProcgenAreaProfilePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField] public KsProcgenMode Mode = KsProcgenMode.Procedural;
    [DataField] public KsProcgenGeometryMode GeometryMode = KsProcgenGeometryMode.Footprint;
    [DataField] public KsProcgenConnectivityPolicy ConnectivityPolicy = KsProcgenConnectivityPolicy.SingleNetwork;
    [DataField] public string? Theme;
    [DataField] public string? EntranceConnections;
    [DataField] public KsProcgenLimits Limits = new();
}

/// <summary>Editor paint metadata. Presence alone never starts generation or authorizes writes.</summary>
[RegisterComponent]
public sealed partial class KsProcgenAreaCellComponent : Component
{
    [DataField] public string Channel = "Default";
    [DataField(required: true)] public ProtoId<KsProcgenAreaProfilePrototype> Profile;
    [DataField] public string? BlobId;
}

/// <summary>Entrance labels and directions use the host grid's axes, independent of sprite rotation.</summary>
[RegisterComponent]
public sealed partial class KsProcgenEntranceComponent : Component
{
    [DataField(required: true)] public string PortId = string.Empty;
    [DataField] public string Channel = "Default";
    [DataField] public string? BlobId;
    [DataField] public Vector2i InwardNormal = new(0, 1);
    [DataField] public List<Vector2i> ThresholdOffsets = [new(0, 0)];
    [DataField] public bool OptionalSealable;
}
