using Content.Shared.Atmos;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     Constants for reagents carried inside atmos mixtures. Constants rather than cvars, so the hot path does no lookup.
/// </summary>
public static class KsFluidmos
{
    /// <summary>
    ///     Litres of atmos volume one unit of liquid or solid takes up, for displacement (1u = 5 mL). A 200 L node holds
    ///         40,000u, so displacement and the hydraulic term are safety nets, not something normal play meets.
    /// </summary>
    public const float LitersPerUnit = 0.005f;

    /// <summary>
    ///     Units of liquid or solid below which an amount is dropped, as <see cref="Atmospherics.GasMinMoles"/> is for gas.
    /// </summary>
    public const float MinLiquidUnits = 0.001f;

    /// <summary>
    ///     Moles of unlinked vapor below which an amount is dropped.
    /// </summary>
    public const float MinVaporMoles = Atmospherics.GasMinMoles;

    /// <summary>
    ///     The least fraction of a mixture's volume always left to its gas, however much liquid it holds.
    /// </summary>
    public const float MinFreeVolumeFraction = 0.02f;

    /// <summary>
    ///     kPa of pressure added per percent of the volume that liquid and solid overfill past
    ///         <see cref="MinFreeVolumeFraction"/>. Makes pumps stop feeding a full net, and overpressure mechanics fire.
    /// </summary>
    public const float HydraulicStiffness = 5000f;

    /// <summary>
    ///     Most kPa the hydraulic term may add. Overfill is unbounded (a deleted pipe shrinks its net, a merge never checks
    ///         capacity), and an infinite pressure becomes NaN in the transfer helpers that divide by it.
    /// </summary>
    public const float MaxHydraulicPressure = 10f * Atmospherics.MaxOutputPressure;

    /// <summary>
    ///     How many moles a unit of liquid counts as in a transfer: tunes how fast liquid flows with the gas.
    /// </summary>
    public const float LiquidPseudoMolesPerUnit = 0.1f;

    /// <summary>
    ///     Default orifice conductance of a passive device for liquid, in units per second per √kPa: 100 kPa across a
    ///         gate moves 100 u/s.
    /// </summary>
    public const float DefaultLiquidConductance = 10f;

    /// <summary>
    ///     Grams in a unit, for mole counts. The same convention <c>BoilTileEffect</c> uses.
    /// </summary>
    public const float GramsPerUnit = 1f;

    /// <summary>
    ///     Molar mass, in g/mol, of a reagent that sets none and has no linked gas.
    /// </summary>
    public const float DefaultMolarMass = 100f;

    /// <summary>
    ///     Default <c>k = ΔS_vap / R</c>, dimensionless, for a reagent that sets none: Trouton's rule, 88 J/(mol K).
    ///         Sets the steepness of the saturation-pressure curve and nothing else.
    /// </summary>
    /// <remarks>
    ///     A real, per-mole value. Never derive it from the game-scale <see cref="LatentHeatRatio"/> latent heat per
    ///         unit: that gives water <c>k ≈ 3.1</c> and a <c>Psat(293 K)</c> near 43 kPa instead of about 2.3, so rooms
    ///         would hold roughly 20 times too much humidity.
    /// </remarks>
    public const float DefaultVaporizationEntropy = 10.6f;

    /// <summary>
    ///     Default <c>k_fus = ΔS_fus / R</c>, dimensionless, for a reagent that sets none: water's, 22 J/(mol K). Sets how
    ///         far solutes depress its freezing point: about 10 K with a tenth of its moles something else.
    /// </summary>
    public const float DefaultFusionEntropy = 2.65f;

    /// <summary>
    ///     Hertz–Knudsen evaporation/condensation coefficient inside pipes. A tuning knob, not a physical constant: set so a
    ///         200 L node of water at 300 K comes most of the way to saturation in a few seconds (90% in about three).
    /// </summary>
    public const float HertzKnudsenAlpha = 0.0013f;

    /// <summary>
    ///     Hertz–Knudsen coefficient for a puddle evaporating into its tile, calibrated so a tile-covering water puddle on
    ///         a dry 293 K tile at one atmosphere dries at upstream's 0.3 u/s (EvaporationAmount 1 times water's
    ///         evaporationSpeed 0.3): that is 0.0167 mol/s, over a flux of about 171 mol/(m² s) per unit of α.
    /// </summary>
    public const float PuddleHertzKnudsenAlpha = 0.000097f;

    /// <summary>
    ///     Volume, in units, at which a puddle covers its whole tile, for its evaporating area. The same reference the
    ///         puddle–air heat exchange uses for its coverage.
    /// </summary>
    public const float PuddleReferenceVolume = 20f;

    /// <summary>
    ///     Liquid–gas interface area, in m² per litre of mixture volume (0.5 m² per 200 L pipe node).
    /// </summary>
    public const float PipeInterfaceAreaPerLiter = 0.0025f;

    /// <summary>
    ///     The most, in kelvin, a single phase step's latent heat may change a mixture's temperature by.
    /// </summary>
    public const float MaxPhaseDeltaT = 5f;

    /// <summary>
    ///     Default latent heat of vaporisation per unit, as a multiple of <c>BoilingPoint * SpecificHeat</c>, calibrated
    ///         to give water exactly <c>BoilTileEffect</c>'s 540 J/u. Game-scale energy only; never used for the curve.
    /// </summary>
    public const float LatentHeatRatio = 540f / 373.15f;

    /// <summary>
    ///     Default heat of fusion per unit, as a multiple of <c>MeltingPoint * SpecificHeat</c>: Richards' rule,
    ///         calibrated to water's 334 J/g over 4.18 J/(g K).
    /// </summary>
    public const float FusionHeatRatio = 80f / 273.15f;

    /// <summary>
    ///     Units of liquid a device buffers before spilling them as a puddle.
    /// </summary>
    public const float SpillThresholdUnits = 1f;

    /// <summary>
    ///     Units per second a siphoning vent pump drains from the puddle on its tile.
    /// </summary>
    public const float PuddleDrainRate = 5f;

    /// <summary>
    ///     Units of unlinked vapor a device buffers before releasing them as smoke.
    /// </summary>
    public const float SmokeThresholdUnits = 0.5f;

    /// <summary>
    ///     Boiling points at or above this are "never boils" sentinels (UnstableMutagen), not temperatures.
    /// </summary>
    public const float NonVolatileBoilingPoint = 1e6f;

    /// <summary>
    ///     Saturation pressure, in kPa, below which a reagent is treated as not evaporating at all.
    /// </summary>
    public const float MinActivePressure = 0.01f;

    /// <summary>
    ///     Fraction past saturation linked vapor on a tile must reach before it condenses, so a puddle and its tile do
    ///         not oscillate.
    /// </summary>
    public const float SupersaturationMargin = 0.05f;

    /// <summary>
    ///     How strongly a supercritical gas dissolves reagents: the log of the factor their saturation pressure is raised
    ///         by, per unit of reduced solvent density past the critical point
    ///         (<see cref="KsPhaseMath.SupercriticalLogEnhancement"/>). Carbon dioxide at 313 K does nothing below about
    ///         12 MPa, multiplies it by about 6 at 15 MPa and 600 at 20 MPa, and reaches the cap by 25 MPa.
    /// </summary>
    public const float SupercriticalSolvation = 6f;

    /// <summary>
    ///     Largest log of the supercritical enhancement, about 160,000 times, so no density makes it infinite.
    /// </summary>
    public const float MaxSupercriticalLogEnhancement = 12f;
}
