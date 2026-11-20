using Content.Server.GameTicking.Rules;
using Content.Shared._KS14.Fluids.Evaporation;
using Content.Shared.GameTicking.Components;
using Robust.Server.GameStates;

namespace Content.Server._KS14.Fluids.Evaporation;

/// <summary>
///     Runs a <see cref="KsMinimumEvaporationRuleComponent"/> as a game rule: sends it to every client, as evaporation is
///         predicted and a rule lives in nullspace, out of anyone's view, and applies its rates while it is active.
///         Its shutdown, which round restart reaches without ending it, is <see cref="KsMinimumEvaporationSystem"/>'s.
/// </summary>
public sealed partial class KsMinimumEvaporationRuleSystem : GameRuleSystem<KsMinimumEvaporationRuleComponent>
{
    [Dependency] private PvsOverrideSystem _pvsOverrideSystem = default!;
    [Dependency] private KsMinimumEvaporationSystem _minimumEvaporationSystem = default!;

    // The override goes with the entity when it is deleted.
    protected override void Added(EntityUid uid, KsMinimumEvaporationRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);
        _pvsOverrideSystem.AddGlobalOverride(uid);
    }

    protected override void Started(EntityUid uid, KsMinimumEvaporationRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        _minimumEvaporationSystem.SetActive((uid, component), true);
    }

    protected override void Ended(EntityUid uid, KsMinimumEvaporationRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        _minimumEvaporationSystem.SetActive((uid, component), false);
    }
}
