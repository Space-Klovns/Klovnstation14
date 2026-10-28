using System.Linq;
using Content.Server._KS14.Fluids.HotPuddle;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Temperature.Systems;
using Content.Shared.Temperature.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private TemperatureSystem _temperatureSystem = default!;
    [Dependency] private SolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private KsHotPuddleSystem _hotPuddleSystem = default!;

    [Dependency] private EntityQuery<TemperatureComponent> _temperatureQuery = default!;

    private const float JoulesPerFirestack = 62_000;

    private void ExposeToHeat(EntityUid uid, float firestacks)
    {
        var heat = firestacks * JoulesPerFirestack;
        if (_temperatureQuery.TryGetComponent(uid, out var temperatureComponent))
            _temperatureSystem.ChangeHeat(uid, heat, ignoreHeatResistance: false, temperature: temperatureComponent);

        foreach (var (_, soln) in _solutionContainerSystem.EnumerateSolutions(uid).ToArray())
            _solutionContainerSystem.AddThermalEnergy(soln, heat);
    }

    /// <summary>
    ///     How long the flame an explosion leaves in a puddle lasts: past the puddle's next tile-effect update, which is
    ///         what lights it.
    /// </summary>
    private static readonly TimeSpan PuddleSparkDuration = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     An explosion is an open flame to the puddle on a tile it passes over, so fuel it spilled or swept across
    ///         catches (<c>CheckTemperatureTileEffect.LitByFlame</c>), rather than only being warmed.
    /// </summary>
    private void SparkPuddle(Entity<MapGridComponent> grid, Vector2i tile, float temperature)
    {
        var tileRef = _map.GetTileRef(grid, tile);
        if (_puddleSystem.TryGetPuddle(tileRef, out var puddleUid))
            _hotPuddleSystem.SetFlame(puddleUid, temperature, _timing.CurTime + PuddleSparkDuration);
    }
}
