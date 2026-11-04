using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluidmos.Reactions;

/// <summary>
///     A chemical reaction that runs inside a fluid-capable pipe mixture, at a rate, rather than all at once as a
///         beaker's <see cref="Content.Shared.Chemistry.Reaction.ReactionPrototype"/> does. Beaker reactions never run in
///         pipes, and these never run in beakers.
/// </summary>
/// <remarks>
///     <para>
///         Each step converts <c>ξ = n (1 - exp(-k dt))</c> reaction units, where <c>n</c> is how many units of
///             reaction the limiting reactant allows and <c>k</c> follows Arrhenius from <see cref="Rate"/> at
///             <see cref="ReferenceTemperature"/>: <c>k = Rate exp(ActivationTemperature (1/Tref - 1/T))</c>. A unit of
///             reaction consumes each reactant's <see cref="KsFluidReactionReagent.Amount"/> units and makes each
///             product's, so the amounts should balance by mass.
///     </para>
///     <para>
///         At least one reactant must be unlinked or condensed: candidate reactions are found from the reagents the
///             mixture's fluid holds, and a linked reagent's vapor is gas moles, not an entry.
///     </para>
/// </remarks>
[Prototype]
public sealed partial class KsFluidReactionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, KsFluidReactionReagent> Reactants = new();

    /// <summary>
    ///     What a unit of reaction makes. <see cref="KsFluidReactionPhase.Any"/> picks vapor above the product's boiling
    ///         point at the mixture's pressure and liquid below it, leaving freezing to the phase step.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, KsFluidReactionReagent> Products = new();

    /// <summary>
    ///     Kelvin. The reaction does not run below this.
    /// </summary>
    [DataField]
    public float MinTemp;

    /// <summary>
    ///     Kelvin. The reaction does not run above this.
    /// </summary>
    [DataField]
    public float MaxTemp = float.PositiveInfinity;

    /// <summary>
    ///     Fraction of the limiting reactant converted per second at <see cref="ReferenceTemperature"/>, while it is
    ///         small. First order, so it never overshoots however long the step.
    /// </summary>
    [DataField]
    public float Rate = 0.1f;

    /// <summary>
    ///     Kelvin at which the reaction runs at exactly <see cref="Rate"/>.
    /// </summary>
    [DataField]
    public float ReferenceTemperature = 293.15f;

    /// <summary>
    ///     <c>Ea / R</c>, in kelvin: how steeply the rate climbs with temperature. Zero runs at <see cref="Rate"/>
    ///         whatever the temperature.
    /// </summary>
    [DataField]
    public float ActivationTemperature;

    /// <summary>
    ///     Joules absorbed per unit of reaction, on the game's specific-heat scale (as latent heat is): positive is
    ///         endothermic and cools the mixture, negative is exothermic and heats it. Phase changes the reaction makes
    ///         (a liquid reactant into a vapor product) are paid for on top of this, from each reagent's latent heats.
    /// </summary>
    [DataField]
    public float Enthalpy;

    /// <summary>
    ///     A solid catalyst bed the reaction needs. It then runs only in the pipe net of a catalytic reactor holding a
    ///         bed of it, scaled by the bed's share of the net and its activity, and never on its own.
    /// </summary>
    [DataField]
    public ProtoId<KsFluidCatalystPrototype>? Catalyst;

    /// <summary>
    ///     A reagent that must be present but is not consumed: a homogeneous catalyst, or a diluent such as steam.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype>? CatalystReagent;

    /// <summary>
    ///     Least units of <see cref="CatalystReagent"/>, in any phase, for the reaction to run.
    /// </summary>
    [DataField]
    public float CatalystReagentUnits = 1f;

    /// <summary>
    ///     Least partial pressure, in kPa, of each listed reagent's vapor for the reaction to run, such as hydrogen for
    ///         hydrotreating.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, float> MinPartialPressure = new();
}

[DataDefinition]
public sealed partial class KsFluidReactionReagent
{
    /// <summary>
    ///     Units per unit of reaction.
    /// </summary>
    [DataField(required: true)]
    public float Amount;

    /// <summary>
    ///     For a reactant, the phase it reacts from; <see cref="KsFluidReactionPhase.Any"/> takes from every phase in
    ///         proportion. For a product, the phase it is made in.
    /// </summary>
    [DataField]
    public KsFluidReactionPhase Phase = KsFluidReactionPhase.Any;
}

public enum KsFluidReactionPhase : byte
{
    Any,
    Solid,
    Liquid,
    Vapor,
}
