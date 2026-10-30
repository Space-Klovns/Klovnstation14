using Content.Shared._KS14.Temperature;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Server.Fluids.EntitySystems;

/// <summary>
///     Heat conducted directly between puddles on neighbouring tiles. A spill across several tiles is one body of liquid,
///         so its tiles share their heat through it, not only through the air over them.
/// </summary>
public sealed partial class PuddleSystem
{
    /// <summary>
    ///     Thermal conductance between two neighbouring puddles that each cover their whole tile, in W/K. Thinner
    ///         puddles touch along a shallower edge and conduct less.
    /// </summary>
    private const float PuddleConductance = 20f;

    /// <summary>
    ///     The neighbours a puddle can conduct to. Each pair's step works with a quarter of each puddle's heat capacity,
    ///         so that its exchanges with all four together still can't carry it past the temperature they settle at.
    /// </summary>
    private const int PuddleConductionNeighbours = 4;

    private static readonly Vector2i[] PuddleConductionOffsets = [new(1, 0), new(0, 1)];

    /// <summary>
    ///     Queues the heat that flows between this puddle and its neighbours above and to the right, so that each pair is
    ///         counted once. Taken from the temperatures at the start of the pass, as the air exchange is.
    /// </summary>
    private void ConductHeatToNeighbours(Entity<PuddleComponent> puddle, Dictionary<Vector2i, Entity<PuddleComponent>> tilePuddles, Vector2i indices, float deltaTime)
    {
        if (!_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution) ||
            solution.Volume <= FixedPoint2.Zero ||
            puddle.Comp.Solution is not { } solutionEntity)
            return;

        foreach (var offset in PuddleConductionOffsets)
        {
            if (!tilePuddles.TryGetValue(indices + offset, out var neighbor) ||
                !_solutionContainerSystem.ResolveSolution(neighbor.Owner, neighbor.Comp.SolutionName, ref neighbor.Comp.Solution, out var neighborSolution) ||
                neighborSolution.Volume <= FixedPoint2.Zero ||
                neighbor.Comp.Solution is not { } neighborSolutionEntity)
                continue;

            if (MathF.Abs(solution.Temperature - neighborSolution.Temperature) < AirHeatExchangeMinimumDelta)
                continue;

            var capacity = solution.GetHeatCapacity(ProtoMan) / PuddleConductionNeighbours;
            var neighborCapacity = neighborSolution.GetHeatCapacity(ProtoMan) / PuddleConductionNeighbours;

            var edgeDepth = MathF.Min(
                MathF.Min(1f, (float)solution.Volume / AirHeatExchangeReferenceVolume),
                MathF.Min(1f, (float)neighborSolution.Volume / AirHeatExchangeReferenceVolume));

            var heat = KsThermalTransfer.ExchangeQuery(capacity, solution.Temperature, neighborCapacity, neighborSolution.Temperature,
                PuddleConductance * edgeDepth, deltaTime);

            if (heat == 0f)
                continue;

            _pendingAirHeatExchanges.Add((solutionEntity, heat));
            _pendingAirHeatExchanges.Add((neighborSolutionEntity, -heat));
        }
    }
}
