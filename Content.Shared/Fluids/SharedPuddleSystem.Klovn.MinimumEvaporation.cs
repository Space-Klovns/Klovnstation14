using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    /// <summary>
    ///     The least each listed reagent evaporates at, whatever its prototype says: the highest of every active
    ///         minimum-evaporation rule (<see cref="Content.Shared._KS14.Fluids.Evaporation.KsMinimumEvaporationSystem"/>).
    ///         Empty, as it almost always is, it costs the hot path one count check.
    /// </summary>
    private readonly Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> _ksMinimumEvaporationRates = [];

    /// <summary>
    ///     How fast a reagent evaporates: its prototype's speed, raised to any minimum an active rule sets for it. Every
    ///         reader of a reagent's evaporation goes through this, so a reagent with a minimum counts as evaporating
    ///         for spreading, cleaning bots and examine too, exactly as one with the same speed in its prototype.
    /// </summary>
    public FixedPoint2 KsGetEvaporationSpeed(ReagentPrototype reagentPrototype)
    {
        if (_ksMinimumEvaporationRates.Count == 0 ||
            !_ksMinimumEvaporationRates.TryGetValue(reagentPrototype.ID, out var minimumRate))
            return reagentPrototype.EvaporationSpeed;

        return FixedPoint2.Max(reagentPrototype.EvaporationSpeed, minimumRate);
    }

    /// <summary>
    ///     Replaces the minimum evaporation rates and, if they changed, re-evaluates every puddle once: one that held
    ///         no evaporating reagent until now has no <see cref="EvaporationComponent"/> to tick it.
    /// </summary>
    public void KsSetMinimumEvaporationRates(IReadOnlyDictionary<ProtoId<ReagentPrototype>, FixedPoint2> rates)
    {
        if (KsSameRates(rates))
            return;

        _ksMinimumEvaporationRates.Clear();
        foreach (var (reagent, rate) in rates)
        {
            _ksMinimumEvaporationRates[reagent] = rate;
        }

        var puddleEnumerator = AllEntityQuery<PuddleComponent>();
        while (puddleEnumerator.MoveNext(out var puddleUid, out var puddleComponent))
        {
            // Going anyway: nothing to keep ticking. Round restart needs no check of its own, as a flush deletes every
            // map, puddles and all, before the nullspace entities a rule lives among (EntityManager.FlushEntitiesInternal),
            // and the client deletes in the server's order, so the sweep a deleted rule starts finds no puddles then.
            if (TerminatingOrDeleted(puddleUid) ||
                !_solutionContainerSystem.ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution))
                continue;

            UpdateEvaporation((puddleUid, puddleComponent), solution);
        }
    }

    private bool KsSameRates(IReadOnlyDictionary<ProtoId<ReagentPrototype>, FixedPoint2> rates)
    {
        if (rates.Count != _ksMinimumEvaporationRates.Count)
            return false;

        foreach (var (reagent, rate) in rates)
        {
            if (!_ksMinimumEvaporationRates.TryGetValue(reagent, out var currentRate) || currentRate != rate)
                return false;
        }

        return true;
    }
}
