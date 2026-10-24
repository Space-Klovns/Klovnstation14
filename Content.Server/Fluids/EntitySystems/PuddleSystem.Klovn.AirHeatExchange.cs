using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Temperature;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server.Fluids.EntitySystems;

/// <summary>
///     Heat exchange between a puddle's solution and the air on its tile, by conduction and thermal radiation.
/// </summary>
/// <remarks>
///     A puddle under a foam blanket is insulated from the air: its heat goes through the blanket instead
///         (<see cref="KsFoamBlanketSystem"/>).
/// </remarks>
public sealed partial class PuddleSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private KsFoamBlanketSystem _foamBlanketSystem = default!;

    /// <summary>
    ///     <see cref="KsCCVars.AtmosPuddleEmissivity"/>.
    /// </summary>
    private float _puddleEmissivity;

    private static readonly TimeSpan AirHeatExchangeInterval = TimeSpan.FromSeconds(1d);

    /// <summary>Thermal conductance between a puddle of at least <see cref="AirHeatExchangeReferenceVolume"/> and its tile's air, in W/K.</summary>
    private const float AirHeatExchangeConductance = 15f;

    /// <summary>Puddle volume at and above which the full conductance applies; smaller puddles scale down linearly.</summary>
    private const float AirHeatExchangeReferenceVolume = 20f;

    /// <summary>Temperature differences below this (in kelvin) are ignored.</summary>
    private const float AirHeatExchangeMinimumDelta = 0.5f;

    /// <summary>
    ///     A pass that changes a tile's air temperature by at least this much (in kelvin) wakes the tile up, so that
    ///     atmos spreads the change. Smaller nudges are left to settle in with the next time the tile is active anyway.
    /// </summary>
    private const float AirHeatExchangeExciteDelta = 1f;

    /// <summary>
    ///     Heat to put into puddle solutions, gathered while walking the puddle cache and applied after it. Applying a
    ///     thermal energy updates the solution's chemicals, which can run reactions that spawn or delete puddles.
    /// </summary>
    private readonly List<(Entity<SolutionComponent> Solution, float Heat)> _pendingAirHeatExchanges = new();

    private TimeSpan _nextAirHeatExchange = TimeSpan.MinValue;
    private TimeSpan _lastAirHeatExchange = TimeSpan.MinValue;

    private void UpdateAirHeatExchange()
    {
        var curTime = _gameTiming.CurTime;
        if (curTime < _nextAirHeatExchange)
            return;

        var deltaTime = _lastAirHeatExchange == TimeSpan.MinValue
            ? (float)AirHeatExchangeInterval.TotalSeconds
            : (float)(curTime - _lastAirHeatExchange).TotalSeconds;

        _lastAirHeatExchange = curTime;
        _nextAirHeatExchange = curTime + AirHeatExchangeInterval;

        if (deltaTime <= 0f)
            return;

        _pendingAirHeatExchanges.Clear();

        foreach (var (gridUid, tilePuddles) in _gridPuddleCache)
        {
            // Resolved once per grid; grids without an atmosphere fall back to the map's (immutable) mixture and are skipped.
            if (!TryComp(gridUid, out GridAtmosphereComponent? gridAtmosphere))
                continue;

            var grid = new Entity<GridAtmosphereComponent?, GasTileOverlayComponent?>(gridUid, gridAtmosphere, null);

            foreach (var (indices, puddle) in tilePuddles)
                ExchangeHeatWithAir(puddle, grid, indices, deltaTime);
        }

        // The cache is no longer being walked, so reactions are free to spawn or delete puddles now.
        foreach (var (solutionEntity, heat) in _pendingAirHeatExchanges)
        {
            if (TerminatingOrDeleted(solutionEntity.Owner))
                continue;

            _solutionContainerSystem.AddThermalEnergy(solutionEntity, heat);
        }

        _pendingAirHeatExchanges.Clear();
    }

    private void ExchangeHeatWithAir(Entity<PuddleComponent> puddle, Entity<GridAtmosphereComponent?, GasTileOverlayComponent?> grid, Vector2i indices, float deltaTime)
    {
        if (!_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution))
            return;

        if (solution.Volume <= FixedPoint2.Zero)
            return;

        if (_atmosphereSystem.GetTileMixture(grid, map: null, indices, excite: false) is not { Immutable: false } mixture)
            return;

        var solutionTemperature = solution.Temperature;
        var airTemperature = mixture.Temperature;
        if (MathF.Abs(airTemperature - solutionTemperature) < AirHeatExchangeMinimumDelta)
            return;

        // Same (scaled) capacity AtmosphereSystem.AddHeat divides by, so capacity * temperature is the air's thermal energy.
        var airCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        var solutionCapacity = solution.GetHeatCapacity(ProtoMan);
        if (airCapacity < Atmospherics.MinimumHeatCapacity || solutionCapacity <= 0f)
            return;

        // Insulated by a blanket over it, which carries the heat on to the air itself.
        if (_foamBlanketSystem.TryGetBlanket(grid.Owner, indices, out _))
            return;

        // A puddle smaller than the reference volume covers that share of the tile, for radiation as for conduction.
        var coverage = MathF.Min(1f, (float)solution.Volume / AirHeatExchangeReferenceVolume);
        var conductance = AirHeatExchangeConductance * coverage +
            KsThermalTransfer.RadiativeConductance(solutionTemperature, airTemperature, coverage, _puddleEmissivity);

        // Stepped exactly, so it never overshoots the shared equilibrium however strong the radiation.
        var heat = KsThermalTransfer.ExchangeQuery(solutionCapacity, solutionTemperature, airCapacity, airTemperature, conductance, deltaTime);

        if (heat == 0f || puddle.Comp.Solution is not { } solutionEntity)
            return;

        var airTemperatureChange = heat / airCapacity;
        mixture.Temperature = airTemperature - airTemperatureChange;
        _pendingAirHeatExchanges.Add((solutionEntity, heat));

        // Wake the tile so atmos spreads the change, but only when it is big enough to matter.
        if (MathF.Abs(airTemperatureChange) >= AirHeatExchangeExciteDelta)
            _atmosphereSystem.GetTileMixture(grid, map: null, indices, excite: true);
    }
}
