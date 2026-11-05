using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Temperature;
using Content.Shared.Atmos;
using Robust.Shared.Configuration;

namespace Content.Server._KS14.Atmos;

/// <summary>
///     Heat flowing between something warm or cold and the air around it, by conduction and thermal radiation. Puddles,
///         ice sheets, foam blankets and open containers all go through here, so they all treat every kind of air alike:
/// </summary>
/// <remarks>
///     <list type="table">
///         <item><term>Air with moles in it</term><description>Exchanged exactly, and the air takes what the body gives.</description></item>
///         <item><term>Immutable air (a map's atmosphere)</term><description>A bath: it takes any heat and stays as it was.</description></item>
///         <item><term>Vacuum</term><description>Nothing to conduct to, so the body only radiates, to the cosmic background.</description></item>
///         <item><term>No mixture at all</term><description>A tile with no air, such as one inside a wall, exchanges nothing.</description></item>
///     </list>
///     <para>Each call steps the body by <paramref name="seconds"/> from the temperatures it is given and never overshoots,
///         however strong the radiation. Heat capacities follow the puddle code's convention: the air's scaled capacity,
///         which is what <see cref="AtmosphereSystem.AddHeat"/> divides by, against a solution's joules.</para>
/// </remarks>
public sealed partial class KsAirHeatExchangeSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;

    /// <summary>
    ///     Temperature differences below this, in kelvin, are left alone.
    /// </summary>
    private const float MinimumDelta = 0.5f;

    /// <summary>
    ///     A step that changes a tile's air by at least this, in kelvin, wakes the tile, so that atmos spreads the change.
    ///         Smaller nudges settle in the next time the tile is active anyway.
    /// </summary>
    private const float ExciteDelta = 1f;

    /// <summary>
    ///     <see cref="KsCCVars.AtmosPuddleEmissivity"/>.
    /// </summary>
    private float _emissivity;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.AtmosPuddleEmissivity, value => _emissivity = value, invokeImmediately: true);
    }

    /// <summary>
    ///     Exchanges heat between a body lying on a tile and that tile's air.
    /// </summary>
    /// <param name="capacity">The body's heat capacity, or the share of it this exchange may work with, in J/K.</param>
    /// <param name="temperature">The body's temperature, in kelvin.</param>
    /// <param name="conductance">Thermal conductance to the air, in W/K. Air is needed for this to count.</param>
    /// <param name="radiatingArea">The body's radiating area, in m².</param>
    /// <param name="emissivity">How well the body radiates, from 0 to 1, if not <see cref="KsCCVars.AtmosPuddleEmissivity"/>.</param>
    /// <param name="maxHeat">The most joules that may flow into the body, for a body that does something else with the rest.</param>
    /// <returns>The joules that flowed into the body, which the caller applies: negative when it gave heat away.</returns>
    public float ExchangeWithTile(
        EntityUid gridUid,
        Vector2i indices,
        float capacity,
        float temperature,
        float conductance,
        float radiatingArea,
        float seconds,
        float? emissivity = null,
        float maxHeat = float.PositiveInfinity)
    {
        var mapUid = Transform(gridUid).MapUid;
        var mixture = _atmosphereSystem.GetTileMixture((gridUid, null, null), mapUid, indices, excite: false);
        var heat = Exchange(mixture, capacity, temperature, conductance, radiatingArea, seconds, emissivity ?? _emissivity, maxHeat, out var airTemperatureChange);

        if (MathF.Abs(airTemperatureChange) >= ExciteDelta)
            _atmosphereSystem.GetTileMixture((gridUid, null, null), mapUid, indices, excite: true);

        return heat;
    }

    /// <summary>
    ///     Exchanges heat between an entity and the air around it: a locker's, if it is shut in one, or else its tile's, which
    ///         is also the air around whatever carries it.
    /// </summary>
    /// <inheritdoc cref="ExchangeWithTile"/>
    public float ExchangeWithSurroundings(
        EntityUid uid,
        float capacity,
        float temperature,
        float conductance,
        float radiatingArea,
        float seconds)
    {
        // In nullspace there is no air, and asking for some would answer with space's.
        if (Transform(uid).MapUid == null)
            return 0f;

        var mixture = _atmosphereSystem.GetContainingMixture(uid, excite: false);
        var heat = Exchange(mixture, capacity, temperature, conductance, radiatingArea, seconds, _emissivity, float.PositiveInfinity, out var airTemperatureChange);

        if (MathF.Abs(airTemperatureChange) >= ExciteDelta)
            _atmosphereSystem.GetContainingMixture(uid, excite: true);

        return heat;
    }

    private float Exchange(
        GasMixture? mixture,
        float capacity,
        float temperature,
        float conductance,
        float radiatingArea,
        float seconds,
        float emissivity,
        float maxHeat,
        out float airTemperatureChange)
    {
        airTemperatureChange = 0f;
        if (mixture == null || capacity <= 0f)
            return 0f;

        // Moles first: space reports a heat capacity even empty.
        var airCapacity = mixture.TotalMoles > 0f ? _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true) : 0f;
        if (airCapacity < Atmospherics.MinimumHeatCapacity)
            return MathF.Min(ExchangeWithReservoir(capacity, temperature, Atmospherics.TCMB, conductance: 0f, radiatingArea, emissivity, seconds), maxHeat);

        if (mixture.Immutable)
            return MathF.Min(ExchangeWithReservoir(capacity, temperature, mixture.Temperature, conductance, radiatingArea, emissivity, seconds), maxHeat);

        var airTemperature = mixture.Temperature;
        if (MathF.Abs(airTemperature - temperature) < MinimumDelta)
            return 0f;

        var heat = MathF.Min(maxHeat, KsThermalTransfer.ExchangeQuery(capacity, temperature, airCapacity, airTemperature,
            conductance + KsThermalTransfer.RadiativeConductance(temperature, airTemperature, radiatingArea, emissivity), seconds));

        airTemperatureChange = heat / airCapacity;
        mixture.Temperature = airTemperature - airTemperatureChange;
        return heat;
    }

    private static float ExchangeWithReservoir(float capacity, float temperature, float reservoirTemperature, float conductance, float radiatingArea, float emissivity, float seconds)
    {
        if (MathF.Abs(reservoirTemperature - temperature) < MinimumDelta)
            return 0f;

        return KsThermalTransfer.ExchangeWithReservoirQuery(capacity, temperature, reservoirTemperature,
            conductance + KsThermalTransfer.RadiativeConductance(temperature, reservoirTemperature, radiatingArea, emissivity), seconds);
    }
}
