using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether gas crossing a grid's boundary pushes that grid, as it would a rocket.
    ///     Covers both air venting into space and air blowing in from a map that has an atmosphere.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<bool> AtmosSpacingThrust =
        CVarDef.Create("klovn.atmos.spacing_thrust", true, CVar.SERVERONLY);

    /// <summary>
    ///     Scales the impulse produced by <see cref="AtmosSpacingThrust"/>.
    /// </summary>
    /// <remarks>
    ///     <para>1 is the impulse the gas actually carries away, but a grid's physics mass is nothing like the mass
    ///         of the station it is drawn as: grid chunk fixtures use <c>SharedShuttleSystem.TileDensityMultiplier</c>,
    ///         so a tile of hull weighs 0.5 kg to the physics engine, against the 800 kg
    ///         <c>ContentTileDefinition.Mass</c> gives it for shuttle impacts. A tile of room air weighs 3 kg. At 1,
    ///         venting one tile of air moves a 5000-tile station at 0.56 m/s and venting a room reaches 11 m/s.</para>
    ///
    ///     <para>Scaling by 1/1600 would put the impulse back in proportion to that 800 kg/tile figure, which is the
    ///         honest answer - and an invisible one, since a realistic station shrugs a breach off. The default is
    ///         16x that instead, enough that a serious breach visibly shoves the station over a few seconds while a
    ///         small leak still does nothing.</para>
    /// </remarks>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> AtmosSpacingThrustMultiplier =
        CVarDef.Create("klovn.atmos.spacing_thrust_multiplier", 0.008f, CVar.SERVERONLY);

    /// <summary>
    ///     How long, in seconds, a grid takes to be handed the impulse it is owed - the time constant of the
    ///     reservoir drain, so 63% arrives within one of these and 95% within three.
    ///     Momentum is conserved regardless of this value; it only decides how abrupt the shove feels.
    ///     Zero or less applies each impulse the moment it is worked out, which reads as a jolt.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> AtmosSpacingThrustSmoothing =
        CVarDef.Create("klovn.atmos.spacing_thrust_smoothing", 0.5f, CVar.SERVERONLY);

    /// <summary>
    ///     How often, in seconds, something touching a puddle is asked whether it is hot, and so heats the puddle. The
    ///         heat it puts in scales with the interval, so this sets how smoothly a puddle warms, not how fast.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosHotPuddleCheckInterval =
        CVarDef.Create("klovn.atmos.hot_puddle_check_interval", 0.08f, CVar.SERVERONLY);

    /// <summary>
    ///     How well an uncovered puddle, ice sheet or open container radiates heat to the air around it, or into vacuum,
    ///         from 0 (not at all) to 1 (a black body), over its radiating area. Radiation grows with the fourth power of
    ///         temperature, so this mostly decides how hot a burning puddle can get against the fire heating it. 0 turns
    ///         it off.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosPuddleEmissivity =
        CVarDef.Create("klovn.atmos.puddle_emissivity", 0.9f, CVar.SERVERONLY);

    /// <summary>
    ///     How often, in seconds, every puddle's tile effects run: catching fire, boiling, and the rest. Each run covers
    ///         the time since the last, so this sets how quickly a puddle reacts, not how fast anything happens. Every
    ///         puddle with tile effects is updated each time, so halving it doubles that work.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosPuddleTileEffectInterval =
        CVarDef.Create("klovn.atmos.puddle_tile_effect_interval", 0.25f, CVar.SERVERONLY);

    /// <summary>
    ///     How fast, in tiles per second, flame runs across a spill of something an open flame lights (welding fuel,
    ///         napalm), however cold it is. It moves in whole tiles once per puddle tile-effect update
    ///         (<see cref="AtmosPuddleTileEffectInterval"/>), carrying the fraction over, so the interval doesn't change
    ///         the speed. 0 turns it off, so a spill only catches as its tiles heat up.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosFlameSpreadRate =
        CVarDef.Create("klovn.atmos.flame_spread_rate", 6f, CVar.SERVERONLY);

    /// <summary>
    ///     Isentropic efficiency of atmos pumps, from 0 to 1: a pump compressing gas heats it by the ideal adiabatic rise
    ///         over this. Lower is hotter.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosPumpEfficiency =
        CVarDef.Create("klovn.atmos.pump_efficiency", 0.7f, CVar.SERVERONLY);

    /// <summary>
    ///     Share of a pump's compression heat that stays in the gas it moves, as if through an aftercooler; the rest goes
    ///         into the pump's tile air, so no heat is created or destroyed. 1 keeps it all in the gas. The balance knob
    ///         for pump heat: the default leaves pumped gas a little warm and the pump's room warmer.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosPumpCompressionHeatFraction =
        CVarDef.Create("klovn.atmos.pump_compression_heat_fraction", 0.1f, CVar.SERVERONLY);

    /// <summary>
    ///     Share of the compression heat a powered device doesn't keep in its gas that it turns back into electricity, from
    ///         0 to 1, rather than heating its room. The device isn't billed for what it recovers. The balance knob for how
    ///         hot a working pump's room gets. A device off the grid recovers nothing.
    /// </summary>
    /// <remarks>
    ///     The default breaks the second law on purpose: see <c>KsTransferThermoSystem.Apply</c>.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosPumpHeatRecovery =
        CVarDef.Create("klovn.atmos.pump_heat_recovery", 0.95f, CVar.SERVERONLY);

    /// <summary>
    ///     How far past its idle load a compressing device's electrical load can climb, as a multiple of that idle load: at
    ///         4, a 200 W pump draws at most 1 kW. What is left of its work past that goes unpaid. Mind that this adds up:
    ///         an APC with many devices on it stops them climbing past what it can sustain.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> AtmosPumpMaxLoadMultiplier =
        CVarDef.Create("klovn.atmos.pump_max_load_multiplier", 4f, CVar.SERVERONLY);

    /// <summary>
    ///     Share of a cooking-off pipe net's explosive fluid its bursting pipes spray onto their tiles, from 0 to 1, as
    ///         liquid at the net's temperature. The rest goes into the explosions. 0 makes them pure blasts; 1 sprays
    ///         everything and explodes nothing.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> AtmosPipeCookOffSpillFraction =
        CVarDef.Create("klovn.atmos.pipe_cookoff_spill_fraction", 0.3f, CVar.SERVERONLY);
}
