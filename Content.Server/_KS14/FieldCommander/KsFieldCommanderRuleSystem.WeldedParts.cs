using Content.Server._KS14.WeldedParts;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Welded parts

    [Dependency] private KsWeldedPartsSystem _weldedPartsSystem = default!;

    /// <summary>
    ///     A unit welded together from parts (a tank and its turret) is one unit: only the hull is counted and paid for, so a
    ///         hit on any part goes on the hull's ledger, for its destruction to pay whoever did it.
    /// </summary>
    // DamageChangedEvent is a by-value class event, so the engine can't give this an Entity<T>. Ordered as OnUnitDamageChanged
    //      is: the hit that destroys the part, and the hull with it, has to be counted by then.
    [SubscribeLocalEvent(before: [typeof(MobThresholdSystem), typeof(Content.Server.Destructible.DestructibleSystem)])]
    private void OnWeldedPartDamageChanged(EntityUid partUid, KsWeldedPartComponent partComponent, DamageChangedEvent args)
    {
        if (!_weldedPartsSystem.TryGetRoot(partUid, out var rootUid) ||
            !TryComp<KsFieldCommanderUnitComponent>(rootUid, out var unitComponent))
            return;

        if (args.DamageIncreased)
            unitComponent.LastDamagedTime = _gameTiming.CurTime;

        if (TryComp<KsFieldCommanderRuleComponent>(unitComponent.RuleUid, out var ruleComponent))
            RecordDamage(rootUid, args, ruleComponent, recordOther: false);
    }

    #endregion
}
