using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Temperature;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Chemistry.Explosion;

/// <summary>
///     Warms and cools <see cref="KsCookOffComponent"/> containers with the air around them, and sets them off once what
///         they hold is hot enough. See <see cref="KsSolutionExplosionSystem"/>.
/// </summary>
public sealed partial class KsCookOffSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private KsSolutionExplosionSystem _solutionExplosionSystem = default!;

    /// <summary>
    ///     Temperature differences below this, in kelvin, are left alone.
    /// </summary>
    private const float MinimumDelta = 0.5f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsCookOffComponent>();
        while (query.MoveNext(out var uid, out var cookOffComponent))
        {
            if (curTime < cookOffComponent.NextCheck || cookOffComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            var seconds = (float)cookOffComponent.CheckInterval.TotalSeconds;
            cookOffComponent.NextCheck = curTime + cookOffComponent.CheckInterval;
            Check((uid, cookOffComponent), seconds);
        }
    }

    private void Check(Entity<KsCookOffComponent> entity, float seconds)
    {
        if (!_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.Solution, out var solutionEntity, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
            return;

        ExchangeHeatWithAir(entity, solutionEntity.Value, seconds);

        // Strictly hotter: something kept at exactly its cook-off point (the incendiary grenade's pre-heated fuel, held at
        //     the 400 K a puddle of it lights at) is meant to hold until it is released.
        if (_solutionExplosionSystem.GetCookOffTemperature(solution) is not { } cookOffTemperature ||
            solution.Temperature <= cookOffTemperature)
            return;

        if (_solutionExplosionSystem.TryExplodeSolution(entity.Owner, entity.Comp.Solution))
            RemCompDeferred(entity.Owner, entity.Comp);
    }

    /// <summary>
    ///     Stepped exactly, so the solution never ends up past the temperature it and the air would settle at, and every
    ///         joule it gains the air loses.
    /// </summary>
    private void ExchangeHeatWithAir(Entity<KsCookOffComponent> entity, Entity<Content.Shared.Chemistry.Components.SolutionComponent> solutionEntity, float seconds)
    {
        if (_atmosphereSystem.GetContainingMixture(entity.Owner, excite: false) is not { Immutable: false } mixture)
            return;

        var solution = solutionEntity.Comp.Solution;
        if (MathF.Abs(mixture.Temperature - solution.Temperature) < MinimumDelta)
            return;

        // Same (scaled) capacity AtmosphereSystem.AddHeat divides by, as the puddle exchange uses.
        var airCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        var solutionCapacity = solution.GetHeatCapacity(ProtoMan);
        if (airCapacity < Atmospherics.MinimumHeatCapacity || solutionCapacity <= 0f)
            return;

        var heat = KsThermalTransfer.ExchangeQuery(solutionCapacity, solution.Temperature, airCapacity, mixture.Temperature,
            entity.Comp.Conductance, seconds);
        if (heat == 0f)
            return;

        mixture.Temperature -= heat / airCapacity;
        _solutionContainerSystem.AddThermalEnergy(solutionEntity, heat);
    }
}
