using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Prototypes;

/// <summary>
///     A cultivation law: a body of techniques built on one of the five phases, learned from its manual.
/// </summary>
[Prototype]
public sealed partial class CultivationLawPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>
    ///     What taking this law as your Root Law changes, shown when it is learned.
    /// </summary>
    [DataField(required: true)]
    public LocId RootDescription;

    [DataField(required: true)]
    public CultivationElement Element;

    [DataField]
    public Color Color = Color.White;

    /// <summary>
    ///     Forbidden laws are the dark arts. Their manuals are never handed out at the start of a round, and anyone
    ///         who studies one is marked by it on examine.
    /// </summary>
    [DataField]
    public bool Forbidden;

    /// <summary>
    ///     The techniques this law teaches, and the realm each one opens at.
    /// </summary>
    [DataField]
    public List<CultivationTechniqueEntry> Techniques = new();
}

[DataDefinition]
public sealed partial class CultivationTechniqueEntry
{
    [DataField(required: true)]
    public CultivationRealm Realm;

    [DataField(required: true)]
    public EntProtoId Action;
}
