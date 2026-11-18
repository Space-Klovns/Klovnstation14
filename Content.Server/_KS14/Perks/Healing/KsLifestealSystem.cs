using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.Perks.Healing;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Perks.Healing;

/// <summary>
///     Lifesteal (<see cref="KsLifestealAbilityComponent"/>): damage dealt to a living mob heals whoever dealt it, if
///         they have the ability. What dealt it is <see cref="KsKillCreditSystem.GetAttacker"/>, or whoever holds it.
/// </summary>
public sealed partial class KsLifestealSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private KsKillCreditSystem _killCreditSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<KsLifestealAbilityComponent> _lifestealQuery = default!;

    [SubscribeLocalEvent]
    private void OnDamageDealt(Entity<MobStateComponent> target, ref DamageDealtEvent args)
    {
        // Every hit on every mob: the cheap refusals first.
        if (args.Origin == null ||
            target.Comp.CurrentState == MobState.Dead ||
            !TryGetHolder(_killCreditSystem.GetAttacker(args.Origin), out var holder) ||
            holder.Owner == target.Owner ||
            _npcFactionSystem.IsEntityFriendly(holder.Owner, target.Owner))
            return;

        var dealt = FixedPoint2.Zero;
        foreach (var amount in args.Damage.DamageDict.Values)
        {
            if (amount > FixedPoint2.Zero)
                dealt += amount;
        }

        var heal = dealt * holder.Comp.Fraction;
        if (heal <= FixedPoint2.Zero)
            return;

        var healed = _damageableSystem.HealEvenly(holder.Owner, -heal, origin: holder.Owner);
        if (healed.Empty)
            return;

        var curTime = _gameTiming.CurTime;
        if (curTime < holder.Comp.NextPopup)
            return;

        holder.Comp.NextPopup = curTime + holder.Comp.PopupInterval;
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-healing-lifesteal"), holder.Owner, holder.Owner, PopupType.Small);
    }

    /// <summary>
    ///     The attacker itself, or whoever holds it (a gun fires a beam as itself).
    /// </summary>
    private bool TryGetHolder(EntityUid? attackerUid, out Entity<KsLifestealAbilityComponent> holder)
    {
        holder = default;
        if (attackerUid is not { } uid || Deleted(uid))
            return false;

        if (_lifestealQuery.TryComp(uid, out var lifestealComponent))
        {
            holder = (uid, lifestealComponent);
            return true;
        }

        if (_containerSystem.TryGetContainingContainer(uid, out var container) &&
            _lifestealQuery.TryComp(container.Owner, out lifestealComponent))
        {
            holder = (container.Owner, lifestealComponent);
            return true;
        }

        return false;
    }
}
