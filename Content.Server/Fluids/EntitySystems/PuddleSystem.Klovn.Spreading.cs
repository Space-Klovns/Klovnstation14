using Content.Server.Spreader;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{
    /// <summary>
    ///     How much lower than a puddle a neighbour has to be for anything to flow into it.
    /// </summary>
    /// <remarks>
    ///     Splitting a solution rounds to 0.01u, so puddles levelling out never quite get there: they pass 0.01u back
    ///         and forth between them for as long as they overflow.
    /// </remarks>
    public static readonly FixedPoint2 KsLevelTolerance = FixedPoint2.New(0.02f);

    /// <summary>
    ///     Wraps <see cref="OnPuddleSpread"/>, which it skips when nothing can flow anywhere.
    /// </summary>
    private void KsOnPuddleSpread(Entity<PuddleComponent> entity, ref SpreadNeighborsEvent args)
    {
        if (KsCanOverflowAnywhere(entity, ref args))
            OnPuddleSpread(entity, ref args);
    }

    /// <summary>
    ///     Whether <see cref="OnPuddleSpread"/> should run for this puddle: false only for an overflowing puddle with no
    ///         free tile beside it, which <see cref="OnPuddleSpread"/> would move nothing out of: it is level with (within
    ///         <see cref="KsLevelTolerance"/>) or below every neighbour, or neither of its transfers has anything to give
    ///         the ones that are lower.
    /// </summary>
    /// <remarks>
    ///     Such a puddle stays an active spreader, as any overflowing puddle does, and would split off its overflow
    ///         and add it straight back every spread, changing its solution each time for nothing. In a closed room with
    ///         more than a thin reagent's overflow volume per tile, that is every puddle in it, every second. The
    ///         neighbour test is the one both of <see cref="OnPuddleSpread"/>'s transfers make, less the tolerance, so
    ///         this skips only spreads that would have moved nothing or a rounding error.
    /// </remarks>
    private bool KsCanOverflowAnywhere(Entity<PuddleComponent> entity, ref SpreadNeighborsEvent args)
    {
        if (args.NeighborFreeTiles.Count > 0 ||
            !_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.SolutionName, ref entity.Comp.Solution, out var solution))
            return true;

        // Nothing to overflow: OnPuddleSpread stops this puddle being an active spreader.
        var overflowVolume = solution.Volume - KsGetOverflowVolume(entity.Comp, solution);
        if (overflowVolume <= FixedPoint2.Zero)
            return true;

        // What its second transfer shares between this puddle and its neighbours.
        var totalVolume = FixedPoint2.Zero;
        foreach (var neighborUid in args.Neighbors)
        {
            if (KsTryGetSpreadNeighbor(neighborUid, out _, out var neighborSolution))
                totalVolume += neighborSolution.Volume;
        }

        foreach (var neighborUid in args.Neighbors)
        {
            if (!KsTryGetSpreadNeighbor(neighborUid, out var neighborPuddleComponent, out var neighborSolution))
                continue;

            // Its first transfer: a neighbour below its own overflow volume is topped up to it.
            var neighborOverflowVolume = KsGetOverflowVolume(neighborPuddleComponent, neighborSolution);
            if (neighborSolution.Volume < neighborOverflowVolume)
                return true;

            var sourceVolume = overflowVolume + neighborOverflowVolume;
            if (neighborSolution.Volume + KsLevelTolerance >= sourceVolume)
                continue;

            // Its second: a lower neighbour is brought up to the average of them all, which is rounded down to 0.01u. So
            //   a puddle a few 0.01u above neighbours that are level with each other has an average no higher than
            //   they already are, and gives them nothing however far past the tolerance it is.
            var idealAverageVolume = (totalVolume + sourceVolume) / (args.Neighbors.Count + 1);
            if (idealAverageVolume <= sourceVolume && idealAverageVolume > neighborSolution.Volume)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     A neighbouring puddle <see cref="OnPuddleSpread"/> would consider flowing into, and what it holds.
    /// </summary>
    private bool KsTryGetSpreadNeighbor(
        EntityUid neighborUid,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PuddleComponent? neighborPuddleComponent,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Content.Shared.Chemistry.Components.Solution? neighborSolution)
    {
        neighborSolution = null;
        return _puddleQuery.TryGetComponent(neighborUid, out neighborPuddleComponent) &&
            _solutionContainerSystem.ResolveSolution(neighborUid, neighborPuddleComponent.SolutionName, ref neighborPuddleComponent.Solution, out neighborSolution) &&
            !CanFullyEvaporate(neighborSolution);
    }
}
