using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Chemistry.SolutionHeatExchange;
using Content.Shared.Chemistry.Components;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map;

namespace Content.Server.Chemistry.EntitySystems;

internal sealed partial class VaporSystem
{
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private KsSolutionHeatExchangeSystem _heatExchangeSystem = default!;

    /// <summary>
    ///     Equalises the temperature of the vapor's solution with that of a puddle on the tile, if any.
    /// </summary>
    private void EqualizeTemperatureWithPuddle(Entity<SolutionComponent> vapor, TileRef tile)
    {
        if (!_puddleSystem.TryGetCachedPuddle(tile, out Entity<PuddleComponent> puddle))
            return;

        if (!SolutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out _))
            return;

        _heatExchangeSystem.TryEqualizeTemperature(vapor, puddle.Comp.Solution.Value);
    }
}
