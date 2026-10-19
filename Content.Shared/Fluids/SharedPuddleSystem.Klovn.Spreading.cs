using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    /// <summary>
    ///     The volume a puddle overflows onto its neighbours above: its own <see cref="PuddleComponent.OverflowVolume"/>,
    ///         or the lowest <see cref="ReagentPrototype.KsSpreadVolume"/> of the reagents in it, whichever is lower.
    /// </summary>
    public FixedPoint2 KsGetOverflowVolume(PuddleComponent puddleComponent, Solution? solution)
    {
        var overflowVolume = puddleComponent.OverflowVolume;
        if (solution == null)
            return overflowVolume;

        foreach (var (reagent, _) in solution.Contents)
        {
            if (ProtoMan.Index<ReagentPrototype>(reagent.Prototype).KsSpreadVolume is { } spreadVolume)
                overflowVolume = FixedPoint2.Min(overflowVolume, spreadVolume);
        }

        return overflowVolume;
    }

    /// <inheritdoc cref="KsGetOverflowVolume(PuddleComponent, Solution?)"/>
    public FixedPoint2 KsGetOverflowVolume(Entity<PuddleComponent> puddle)
    {
        _solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution);
        return KsGetOverflowVolume(puddle.Comp, solution);
    }
}
