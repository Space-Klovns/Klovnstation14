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
    ///         free tile beside it, level with (within <see cref="KsLevelTolerance"/>) or below every neighbour it could
    ///         flow into.
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

        foreach (var neighborUid in args.Neighbors)
        {
            if (!_puddleQuery.TryGetComponent(neighborUid, out var neighborPuddleComponent) ||
                !_solutionContainerSystem.ResolveSolution(neighborUid, neighborPuddleComponent.SolutionName, ref neighborPuddleComponent.Solution, out var neighborSolution) ||
                CanFullyEvaporate(neighborSolution))
                continue;

            if (neighborSolution.Volume + KsLevelTolerance < overflowVolume + KsGetOverflowVolume(neighborPuddleComponent, neighborSolution))
                return true;
        }

        return false;
    }
}
