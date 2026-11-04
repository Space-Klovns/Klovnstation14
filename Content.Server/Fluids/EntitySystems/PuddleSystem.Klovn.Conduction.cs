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

    private static readonly Vector2i[] PuddleConductionOffsets = [new(1, 0), new(0, 1)];

    private static readonly Vector2i[] PuddleNeighbourOffsets = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    /// <summary>
    ///     What a puddle exchanges heat with this pass: each neighbouring puddle, and what is over it unless that is sealed
    ///         off. Every step works with the puddle's heat capacity split evenly between these.
    /// </summary>
    /// <remarks>
    ///     Every step in a pass starts from the same temperatures and they are applied together, so each can carry only
    ///         its own share as far as its partner's temperature, and together they stay between their partners'. With
    ///         the whole capacity in each, a small hot puddle between cold neighbours in cold air was carried past all of
    ///         them, below absolute zero. Splitting only between the contacts it has leaves a puddle with one alone its
    ///         whole capacity, as before.
    /// </remarks>
    private int CountHeatContacts(Dictionary<Vector2i, Entity<PuddleComponent>> tilePuddles, Vector2i indices, bool exchangesWithAir)
    {
        var contacts = exchangesWithAir ? 1 : 0;
        foreach (var offset in PuddleNeighbourOffsets)
        {
            if (tilePuddles.TryGetValue(indices + offset, out var neighbor) &&
                _solutionContainerSystem.ResolveSolution(neighbor.Owner, neighbor.Comp.SolutionName, ref neighbor.Comp.Solution, out var neighborSolution) &&
                neighborSolution.Volume > FixedPoint2.Zero)
                contacts++;
        }

        return Math.Max(contacts, 1);
    }

    /// <summary>
    ///     Whether the puddle on a tile exchanges heat with what is over it, which even vacuum does, by radiation: only a
    ///         foam blanket seals the puddle in.
    /// </summary>
    private bool ExchangesWithAir(EntityUid gridUid, Vector2i indices)
    {
        return !_foamBlanketSystem.TryGetBlanket(gridUid, indices, out _);
    }

    /// <summary>
    ///     Queues the heat that flows between this puddle and its neighbours above and to the right, so that each pair is
    ///         counted once. Taken from the temperatures at the start of the pass, as the air exchange is.
    /// </summary>
    private void ConductHeatToNeighbours(
        Entity<PuddleComponent> puddle,
        EntityUid gridUid,
        Dictionary<Vector2i, Entity<PuddleComponent>> tilePuddles,
        Vector2i indices,
        float deltaTime)
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

            var neighborIndices = indices + offset;
            var capacity = solution.GetHeatCapacity(ProtoMan) /
                CountHeatContacts(tilePuddles, indices, ExchangesWithAir(gridUid, indices));
            var neighborCapacity = neighborSolution.GetHeatCapacity(ProtoMan) /
                CountHeatContacts(tilePuddles, neighborIndices, ExchangesWithAir(gridUid, neighborIndices));

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
