using System.Linq;
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
    ///     An explosion is an open flame to the puddle on a tile it passes over, so fuel it spilled or swept across
    ///         catches (<c>CheckTemperatureTileEffect.LitByFlame</c>), rather than only being warmed. The puddle's tile
    ///         effects run then and there, so nothing has to remember the flame until its next update.
    /// </summary>
    private void SparkPuddle(Entity<MapGridComponent> grid, Vector2i tile, float temperature)
    {
        var tileRef = _map.GetTileRef(grid, tile);
        if (_puddleSystem.TryGetPuddle(tileRef, out var puddleUid))
            _puddleSystem.KsSparkPuddle(puddleUid, temperature);
    }
}
