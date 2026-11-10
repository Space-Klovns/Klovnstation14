using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluidmos.Reactions;

/// <summary>
///     A solid catalyst that a catalytic reactor holds as a bed. Reactions naming it run only in the pipe net of a reactor
///         holding it, as fast as the bed's share of that net and its activity allow.
/// </summary>
[Prototype]
public sealed partial class KsFluidCatalystPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    ///     Reagents that poison or foul the bed: the activity, out of 1, it loses per unit it takes up. Sulfur on platinum,
    ///         coke on zeolite.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, float> Poisons = new();

    /// <summary>
    ///     Fraction of each poison in its net a bed filling the whole net takes up per second. The poison is bound to the
    ///         bed, so it leaves the mixture.
    /// </summary>
    [DataField]
    public float PoisonUptakeRate = 0.2f;
}
