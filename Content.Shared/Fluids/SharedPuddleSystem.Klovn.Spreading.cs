using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    /// <summary>
    ///     The lowest overflow volume a puddle can have, whatever its reagents say: the smallest amount a solution
    ///         stores. Zero would divide by zero in the puddle's appearance and overflow all of it.
    /// </summary>
    public static readonly FixedPoint2 KsMinimumOverflowVolume = FixedPoint2.Epsilon;

    /// <summary>
    ///     The volume a puddle overflows onto its neighbours above. Each reagent with a lower
    ///         <see cref="ReagentPrototype.KsSpreadVolume"/> pulls the puddle's own
    ///         <see cref="PuddleComponent.OverflowVolume"/> towards it by its share of the puddle: a puddle of nothing
    ///         else spreads at that reagent's volume, and a trace of it in a pool of blood changes almost nothing.
    /// </summary>
    /// <remarks>
    ///     This is linear in each reagent's quantity, so the overflow volume of two solutions mixed is the
    ///         volume-weighted average of theirs (<see cref="KsGetMixedOverflowVolume"/>).
    /// </remarks>
    public FixedPoint2 KsGetOverflowVolume(PuddleComponent puddleComponent, Solution? solution)
    {
        var overflowVolume = puddleComponent.OverflowVolume;
        if (solution == null || solution.Volume <= FixedPoint2.Zero)
            return overflowVolume;

        var reduction = 0f;
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (ProtoMan.Index<ReagentPrototype>(reagent.Prototype).KsSpreadVolume is { } spreadVolume &&
                spreadVolume < overflowVolume)
                reduction += (float)(overflowVolume - spreadVolume) * (float)quantity / (float)solution.Volume;
        }

        return FixedPoint2.Max(overflowVolume - FixedPoint2.New(reduction), KsMinimumOverflowVolume);
    }

    /// <inheritdoc cref="KsGetOverflowVolume(PuddleComponent, Solution?)"/>
    public FixedPoint2 KsGetOverflowVolume(Entity<PuddleComponent> puddle)
    {
        _solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution);
        return KsGetOverflowVolume(puddle.Comp, solution);
    }

    /// <summary>
    ///     The overflow volume the puddle would have with <paramref name="added"/> mixed into it.
    /// </summary>
    public FixedPoint2 KsGetMixedOverflowVolume(Entity<PuddleComponent> puddle, Solution added)
    {
        _solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution);
        var puddleVolume = solution?.Volume ?? FixedPoint2.Zero;
        var totalVolume = puddleVolume + added.Volume;
        if (totalVolume <= FixedPoint2.Zero)
            return puddle.Comp.OverflowVolume;

        var puddleOverflowVolume = (float)KsGetOverflowVolume(puddle.Comp, solution);
        var addedOverflowVolume = (float)KsGetOverflowVolume(puddle.Comp, added);
        var mixed = (puddleOverflowVolume * (float)puddleVolume + addedOverflowVolume * (float)added.Volume) / (float)totalVolume;
        return FixedPoint2.Max(FixedPoint2.New(mixed), KsMinimumOverflowVolume);
    }
}
