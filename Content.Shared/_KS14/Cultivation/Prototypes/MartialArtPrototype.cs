using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Prototypes;

public enum MartialArtKind : byte
{
    /// <summary>
    ///     Pure physical training, paid for with stamina. Anyone can learn these.
    /// </summary>
    Body,

    /// <summary>
    ///     Internal arts that move qi. Only cultivators can learn these, and they cost qi.
    /// </summary>
    Qi,
}

/// <summary>
///     A martial art learned from its own manual, alongside (not instead of) a cultivator's laws.
/// </summary>
[Prototype]
public sealed partial class MartialArtPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public MartialArtKind Kind;

    /// <summary>
    ///     The lowest realm a cultivator must have reached to grasp a qi art.
    /// </summary>
    [DataField]
    public CultivationRealm MinRealm = CultivationRealm.QiCondensation;

    /// <summary>
    ///     Dark arts mark whoever practises them.
    /// </summary>
    [DataField]
    public bool Forbidden;

    [DataField]
    public List<EntProtoId> Actions = new();

    /// <summary>
    ///     Passive changes to the body, added when the art is learned.
    /// </summary>
    [DataField]
    public ComponentRegistry Components = new();
}
