using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     Everything the phase step needs to know about one reagent, resolved from its prototype once, so the hot path does
///         no prototype lookups. Immutable; rebuilt on prototype reload.
/// </summary>
public sealed class KsFluidReagentProperties
{
    public ProtoId<ReagentPrototype> Id { get; init; }

    /// <summary>
    ///     J/(K u), for liquid and solid.
    /// </summary>
    public float SpecificHeat { get; init; }

    /// <summary>
    ///     g/mol.
    /// </summary>
    public float MolarMass { get; init; }

    public float MolesPerUnit { get; init; }

    /// <summary>
    ///     Heat capacity a unit has as vapor, in unscaled J/K: its moles times its gas's molar heat capacity for a linked
    ///         reagent, otherwise its specific heat.
    /// </summary>
    public float VaporHeatCapacityPerUnit { get; init; }

    /// <summary>
    ///     Whether it evaporates at all: it has a boiling point that is not a "never boils" sentinel.
    /// </summary>
    public bool Volatile { get; init; }

    /// <summary>
    ///     Kelvin at one atmosphere. Meaningless unless <see cref="Volatile"/>.
    /// </summary>
    public float BoilingPoint { get; init; }

    /// <summary>
    ///     The saturation-pressure curve's <c>k = ΔS_vap / R</c>. Never derived from <see cref="LatentHeat"/>.
    /// </summary>
    public float VaporizationEntropy { get; init; }

    /// <summary>
    ///     J per unit.
    /// </summary>
    public float LatentHeat { get; init; }

    /// <summary>
    ///     Below this, its saturation pressure is negligible and the phase step skips it.
    /// </summary>
    public float MinActiveTemperature { get; init; }

    public bool Freezable { get; init; }

    /// <summary>
    ///     Kelvin. Meaningless unless <see cref="Freezable"/>.
    /// </summary>
    public float MeltingPoint { get; init; }

    /// <summary>
    ///     J per unit.
    /// </summary>
    public float FusionHeat { get; init; }

    /// <summary>
    ///     The linked gas's index, or -1 for none.
    /// </summary>
    public int GasIndex { get; init; }

    /// <summary>
    ///     Above this no liquid forms at any pressure. The linked gas's van der Waals critical temperature, or infinity.
    /// </summary>
    public float CriticalTemperature { get; init; }

    /// <summary>
    ///     From <see cref="KsPhaseMath.InverseSqrtTwoPiMR"/>.
    /// </summary>
    public float InverseSqrtTwoPiMR { get; init; }

    public Color Color { get; init; }

    public bool Linked => GasIndex >= 0;
}
