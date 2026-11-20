using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluids.Evaporation;

/// <summary>
///     Folds every active <see cref="KsMinimumEvaporationRuleComponent"/> into one table of minimum rates on
///         <see cref="SharedPuddleSystem"/>, the highest per reagent. Rebuilt only when a rule starts or ends (server),
///         its state arrives (client), or it shuts down: round restart deletes rule entities without ending them.
/// </summary>
public sealed partial class KsMinimumEvaporationSystem : EntitySystem
{
    [Dependency] private SharedPuddleSystem _puddleSystem = default!;

    /// <summary>
    ///     Scratch for <see cref="Rebuild"/>, kept to save allocating one each time.
    /// </summary>
    private readonly Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> _rates = [];

    /// <summary>
    ///     Starts or stops a rule's minimum rates. Server only in practice: the client learns it from state.
    /// </summary>
    public void SetActive(Entity<KsMinimumEvaporationRuleComponent> rule, bool active)
    {
        if (rule.Comp.Active == active)
            return;

        rule.Comp.Active = active;
        Dirty(rule);
        Rebuild();
    }

    [SubscribeLocalEvent]
    private void OnAfterHandleState(Entity<KsMinimumEvaporationRuleComponent> rule, ref AfterAutoHandleStateEvent args)
    {
        Rebuild();
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsMinimumEvaporationRuleComponent> rule, ref ComponentShutdown args)
    {
        if (rule.Comp.Active)
            Rebuild();
    }

    private void Rebuild()
    {
        _rates.Clear();

        var ruleEnumerator = AllEntityQuery<KsMinimumEvaporationRuleComponent>();
        while (ruleEnumerator.MoveNext(out var ruleComponent))
        {
            // The one shutting down is still stored.
            if (!ruleComponent.Active || ruleComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            foreach (var (reagent, rate) in ruleComponent.MinimumRates)
            {
                if (!_rates.TryGetValue(reagent, out var currentRate) || rate > currentRate)
                    _rates[reagent] = rate;
            }
        }

        _puddleSystem.KsSetMinimumEvaporationRates(_rates);
    }
}
