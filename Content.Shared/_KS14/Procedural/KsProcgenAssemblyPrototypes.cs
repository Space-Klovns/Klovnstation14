using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenRelationKind : byte
{
    OnSurface,
    InContainer,
    AdjacentTo,
    FacingTarget,
    UsesSeat,
    Near,
    AtCorner,
    FacingOpenSpace,
}

public enum KsProcgenRelationSeverity : byte
{
    Required,
    Preferred,
}

public enum KsProcgenMemberRotation : byte
{
    AssemblyRelative,
    Independent,
}

public enum KsProcgenApproachPolicy : byte
{
    EmptyFloor,
    EmptyOrAssociatedSeat,
}

[DataDefinition]
public sealed partial class KsProcgenAssemblyMember
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public string? Entity;
    [DataField] public string? Binding;
    [DataField] public int MinimumCount = 1;
    [DataField] public int MaximumCount = 1;
    [DataField] public KsProcgenEntityRole Role;
    [DataField] public KsProcgenMovementClass Movement = KsProcgenMovementClass.Blocks;
    [DataField] public List<Vector2i> Footprint = [new(0, 0)];
    [DataField] public List<int> AllowedQuarterTurns = [0, 1, 2, 3];
    [DataField] public KsProcgenMemberRotation RotationMode;
    [DataField] public int LocalQuarterTurns;
    [DataField] public bool RequiresInteractionApproach;
    [DataField] public Vector2i? ApproachLanding;
}

[DataDefinition]
public sealed partial class KsProcgenAssemblyRelation
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Subject = string.Empty;
    [DataField(required: true)] public KsProcgenRelationKind Kind;
    [DataField] public string? Target;
    [DataField] public KsProcgenRelationSeverity Severity;
    [DataField] public int MinimumDistance = 1;
    [DataField] public int MaximumDistance = 4;
    [DataField] public string? Slot;
    [DataField] public string? ContainerId;
    [DataField] public KsProcgenApproachPolicy ApproachPolicy;
}

/// <summary>One complete alternative; it replaces the base core rather than extending it.</summary>
[DataDefinition]
public sealed partial class KsProcgenAssemblyVariant
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string AnchorMember = string.Empty;
    [DataField(required: true)] public List<KsProcgenAssemblyMember> Members = new();
    [DataField] public List<KsProcgenAssemblyRelation> Relations = new();
}

[Prototype]
public sealed partial class KsProcgenAssemblyPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField(required: true)] public string AnchorMember = string.Empty;
    [DataField(required: true)] public List<KsProcgenAssemblyMember> Members = new();
    [DataField] public List<KsProcgenAssemblyRelation> Relations = new();
    [DataField] public List<KsProcgenAssemblyVariant> Variants = new();
}

[DataDefinition]
public sealed partial class KsProcgenAssemblyReference
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Assembly = string.Empty;
    [DataField] public Dictionary<string, string> Bindings = new();
    [DataField] public float Weight = 1f;
    [DataField] public int MinimumCount;
}
