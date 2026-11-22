using Content.Server._KS14.Atmos;
using Content.Server._KS14.TileEffects;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     Puddles and ice sheets wasting away where nothing would otherwise clear them: in vacuum, where they boil themselves
///         cold and stall, and under a map's own air, which no room shelters them from
///         (<see cref="KsCCVars.AtmosPuddleVacuumDissipationRate"/>, <see cref="KsCCVars.AtmosPuddleExposedDissipationRate"/>).
/// </summary>
/// <remarks>
///     Rides on the once-a-second heat exchange of puddles and ice (<see cref="KsAirHeatExchangeSystem"/>), which has
///         already read each one's tile air: <see cref="GetRate"/> only looks at that air, and nothing walks the puddles
///         for this alone. The exchange queues what goes and calls <see cref="Dissipate"/> once it has finished walking.
/// </remarks>
public sealed partial class KsFluidDissipationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private KsTileEffectAtmosSystem _tileEffectAtmosSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    private float _vacuumRate;
    private float _exposedRate;
    private float _vacuumPressure;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.AtmosPuddleVacuumDissipationRate, value => _vacuumRate = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.AtmosPuddleExposedDissipationRate, value => _exposedRate = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.AtmosPuddleVacuumPressure, value => _vacuumPressure = value, invokeImmediately: true);
    }

    /// <summary>
    ///     Units per second a puddle or sheet under <paramref name="mixture"/> loses: the vacuum rate in air thinner than
    ///         the vacuum pressure, space included, the exposed rate under any other map's air, and nothing in a room's own
    ///         air or on a sealed tile.
    /// </summary>
    /// <param name="mixture">The tile's air, as <see cref="KsAirHeatExchangeSystem.ExchangeWithTile(EntityUid, Vector2i, float, float, float, float, float, out GasMixture?, float?, float)"/> read it.</param>
    public float GetRate(GasMixture? mixture)
    {
        if (mixture == null)
            return 0f;

        // Cheapest first: with both off, which is the only case that matters for cost, this is all it does.
        if (_vacuumRate <= 0f && _exposedRate <= 0f)
            return 0f;

        if (mixture.Pressure < _vacuumPressure)
            return MathF.Max(_vacuumRate, 0f);

        return mixture.Immutable ? MathF.Max(_exposedRate, 0f) : 0f;
    }

    /// <summary>
    ///     Takes up to <paramref name="units"/> off a solution, each reagent in proportion, into the tile's air as its gas
    ///         where it has one. Immutable air and vacuum simply lose it, as they lose anything put into them
    ///         (<see cref="KsTileEffectAtmosSystem"/>). Call it outside any walk of the puddles or sheets: it updates the
    ///         solution, and an emptied puddle deletes itself.
    /// </summary>
    public void Dissipate(Entity<SolutionComponent> solutionEntity, EntityUid gridUid, Vector2i indices, float units)
    {
        var solution = solutionEntity.Comp.Solution;
        if (units <= 0f || solution.Volume <= FixedPoint2.Zero)
            return;

        // At least the smallest amount a solution holds, or a trace would never quite go.
        var quantity = FixedPoint2.Max(FixedPoint2.New(units), FixedPoint2.Epsilon);
        var temperature = solution.Temperature;
        var removed = _solutionContainerSystem.SplitSolution(solutionEntity, quantity);

        foreach (var (reagent, reagentQuantity) in removed.Contents)
        {
            if (!_propertiesSystem.TryGetProperties(reagent.Prototype, out var properties) || !properties.Linked)
                continue;

            var reagentUnits = (float)reagentQuantity;
            _tileEffectAtmosSystem.TryAddGas(gridUid,
                indices,
                (Gas)properties.GasIndex,
                reagentUnits * properties.MolesPerUnit,
                sensibleEnergy: reagentUnits * properties.SpecificHeat * temperature);
        }
    }
}
