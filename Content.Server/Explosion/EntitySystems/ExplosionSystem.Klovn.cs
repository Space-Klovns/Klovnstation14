using System.Linq;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Temperature.Systems;
using Content.Shared.Temperature.Components;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private TemperatureSystem _temperatureSystem = default!;
    [Dependency] private SolutionContainerSystem _solutionContainerSystem = default!;

    [Dependency] private EntityQuery<TemperatureComponent> _temperatureQuery = default!;

    private const float JoulesPerFirestack = 3750;

    private void ExposeToHeat(EntityUid uid, float firestacks)
    {
        var heat = firestacks * JoulesPerFirestack;
        if (_temperatureQuery.TryGetComponent(uid, out var temperatureComponent))
            _temperatureSystem.ChangeHeat(uid, heat, ignoreHeatResistance: false, temperature: temperatureComponent);

        foreach (var (_, soln) in _solutionContainerSystem.EnumerateSolutions(uid).ToArray())
            _solutionContainerSystem.AddThermalEnergy(soln, heat);
    }
}
