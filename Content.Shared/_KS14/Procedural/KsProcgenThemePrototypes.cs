using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

[DataDefinition]
public sealed partial class KsProcgenWeightedPackReference
{
    [DataField(required: true)] public string Pack = string.Empty;
    [DataField] public float Weight = 1f;
    [DataField] public int MinimumCount;
}

[DataDefinition]
public sealed partial class KsProcgenThemeGoals
{
    [DataField] public bool CoherentContents = true;
    [DataField] public bool KeepCenterOpen = true;
    [DataField] public bool AllowLooseItems = true;
    [DataField] public float FurnishingDensity = 0.2f;
    [DataField] public float LightingCoverage = 0.85f;
}

/// <summary>
/// A reusable interior style. This selects content; it does not prescribe a room shape or fixture location.
/// </summary>
[Prototype("ksProcgenRoomTheme")]
public sealed partial class KsProcgenRoomThemePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField] public string? Parent;
    [DataField] public string? FallbackTheme;
    [DataField] public List<string> Tags = new();
    [DataField] public List<string> CompatibleRegionTags = new();
    [DataField] public List<KsProcgenWeightedPackReference>? TilePacks;
    [DataField] public List<KsProcgenWeightedPackReference>? WallPacks;
    [DataField] public List<KsProcgenWeightedPackReference>? LightingPacks;
    [DataField] public List<KsProcgenWeightedPackReference>? EntityPacks;
    [DataField] public KsProcgenThemeGoals? Goals;
}

[DataDefinition]
public sealed partial class KsProcgenTilePalette
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string PrimaryTile = string.Empty;
    [DataField] public string? AccentTile;
    [DataField] public float AccentFraction;
    [DataField] public float Weight = 1f;
}

[Prototype("ksProcgenTilePack")]
public sealed partial class KsProcgenTilePackPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField] public List<string> Tags = new();
    [DataField(required: true)] public List<KsProcgenTilePalette> Palettes = new();
}

[DataDefinition]
public sealed partial class KsProcgenWallFamily
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string InteriorWall = string.Empty;
    [DataField] public string? HullWall;
    [DataField] public string? Door;
    [DataField] public string? Window;
    [DataField] public float Weight = 1f;
}

[Prototype("ksProcgenWallPack")]
public sealed partial class KsProcgenWallPackPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField] public List<string> Tags = new();
    [DataField(required: true)] public List<KsProcgenWallFamily> Families = new();
}

public enum KsProcgenLightingSupply : byte
{
    SelfContained,
    CallerSupplied,
}

[DataDefinition]
public sealed partial class KsProcgenLightFixture
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Entity = string.Empty;
    [DataField] public float Weight = 1f;
    [DataField] public int PreferredSpacing = 4;
    [DataField] public KsProcgenLightingSupply Supply = KsProcgenLightingSupply.SelfContained;
}

[Prototype("ksProcgenLightingPack")]
public sealed partial class KsProcgenLightingPackPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField] public List<string> Tags = new();
    [DataField(required: true)] public List<KsProcgenLightFixture> Fixtures = new();
}

public enum KsProcgenEntityRole : byte
{
    PrimaryFurniture,
    Seat,
    Storage,
    Equipment,
    TaskLight,
    Consumable,
    Decoration,
}

public enum KsProcgenMovementClass : byte
{
    Clear,
    Blocks,
    VaultRequired,
}

[DataDefinition]
public sealed partial class KsProcgenEntityEntry
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Entity = string.Empty;
    [DataField] public float Weight = 1f;
    [DataField] public KsProcgenEntityRole Role;
    [DataField] public int MinimumCount;
    [DataField] public KsProcgenMovementClass Movement = KsProcgenMovementClass.Blocks;
    [DataField] public List<Vector2i> Footprint = [new(0, 0)];
    [DataField] public List<int> AllowedQuarterTurns = [0, 1, 2, 3];
    [DataField] public bool RequiresInteractionApproach;
}

[Prototype("ksProcgenEntityPack")]
public sealed partial class KsProcgenEntityPackPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField] public List<string> Tags = new();
    [DataField] public List<string> ActivityTags = new();
    [DataField] public List<string> CompatibleSupportTags = new();
    [DataField] public List<KsProcgenEntityEntry> Entries = new();
    [DataField] public List<KsProcgenAssemblyReference> Assemblies = new();
}
