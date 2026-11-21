using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Prototypes;

/// <summary>
///     What on the station counts towards one of the five elements, for the feng shui of a cultivation site.
///         An entity counts if it is (or inherits from) one of the prototypes, its prototype's name starts with one of
///         the prefixes, it has one of the components, or it is a stack of one of the materials.
/// </summary>
[Prototype]
public sealed partial class FengShuiElementPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public CultivationElement Element;

    [DataField]
    public List<EntProtoId> Prototypes = new();

    /// <summary>
    ///     Whole families by name, for kinds whose shared parent is abstract (every potted plant, every toolbox, every ore).
    /// </summary>
    [DataField]
    public List<string> Prefixes = new();

    /// <summary>
    ///     Component names, as written in prototypes.
    /// </summary>
    [DataField]
    public List<string> Components = new();

    [DataField]
    public List<ProtoId<StackPrototype>> Stacks = new();

    /// <summary>
    ///     Does anything burning count? Fire is fire.
    /// </summary>
    [DataField]
    public bool Burning;
}
