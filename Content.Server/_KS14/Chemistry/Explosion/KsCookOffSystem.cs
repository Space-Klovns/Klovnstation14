using Content.Server._KS14.Atmos;
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
    [Dependency] private KsAirHeatExchangeSystem _airHeatExchangeSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private KsSolutionExplosionSystem _solutionExplosionSystem = default!;

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

    private void ExchangeHeatWithAir(Entity<KsCookOffComponent> entity, Entity<Content.Shared.Chemistry.Components.SolutionComponent> solutionEntity, float seconds)
    {
        var solution = solutionEntity.Comp.Solution;
        var heat = _airHeatExchangeSystem.ExchangeWithSurroundings(entity.Owner,
            solution.GetHeatCapacity(ProtoMan),
            solution.Temperature,
            entity.Comp.Conductance,
            entity.Comp.RadiatingArea,
            seconds);

        if (heat != 0f)
            _solutionContainerSystem.AddThermalEnergy(solutionEntity, heat);
    }
}
