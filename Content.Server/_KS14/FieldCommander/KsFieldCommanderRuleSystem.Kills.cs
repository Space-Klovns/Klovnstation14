using Content.Shared.Destructible;
using Content.Shared.Mobs;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Kills

    [SubscribeLocalEvent]
    private void OnUnitMobStateChanged(Entity<KsFieldCommanderUnitComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            CountUnitKill(entity);
            return;
        }

        // Brought back: killing it again is a new kill.
        if (args.OldMobState == MobState.Dead)
            entity.Comp.Killed = false;
    }

    /// <summary>
    ///     A unit with no mob state to die by (the objective borg) is killed by being destroyed. One that has both is
    ///         counted by whichever comes first.
    /// </summary>
    // DestructionEventArgs is a by-value class event, so the engine can't give this an Entity<T>.
    [SubscribeLocalEvent]
    private void OnUnitDestroyed(EntityUid unitUid, KsFieldCommanderUnitComponent unitComponent, DestructionEventArgs args)
    {
        CountUnitKill((unitUid, unitComponent));
    }

    /// <summary>
    ///     Counts a unit's kill for its rule and pays the defenders who did it, once.
    /// </summary>
    private void CountUnitKill(Entity<KsFieldCommanderUnitComponent> unit)
    {
        if (unit.Comp.Killed)
            return;

        unit.Comp.Killed = true;

        if (!TryComp<KsFieldCommanderRuleComponent>(unit.Comp.RuleUid, out var ruleComponent))
            return;

        ruleComponent.UnitsKilled++;
        RewardUnitKill((unit.Comp.RuleUid, ruleComponent), unit);
    }

    #endregion
}
