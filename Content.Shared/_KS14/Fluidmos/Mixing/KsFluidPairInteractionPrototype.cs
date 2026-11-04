using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluidmos.Mixing;

/// <summary>
///     The Margules <c>A</c> at 298 K between two particular reagents, overriding the <c>A</c> of their families. For named
///         pairs such as ethanol and water, whose azeotrope is worth getting right. Families that are immiscible stay so:
///         the pair then has no effect.
/// </summary>
[Prototype]
public sealed partial class KsFluidPairInteractionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> First;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Second;

    [DataField(required: true)]
    public float Margules;
}
