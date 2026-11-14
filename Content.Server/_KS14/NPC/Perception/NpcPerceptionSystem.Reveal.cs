using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.NPC;
using Content.Shared.Damage.Systems;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Being shot gives the shooter away. With <see cref="NpcPerceptionComponent.RevealAttackers"/>, whoever hurts the
///         NPC becomes known to it wherever they are, so it can turn and fire back, and nobody can pick it off from
///         where it cannot see them. The knowledge is a plain visible contact that holds for
///         <see cref="NpcPerceptionComponent.RevealDuration"/> after the last hit and then decays like any other
///         sighting, and it reaches the squad through the usual callouts.
/// </summary>
public sealed partial class NpcPerceptionSystem
{
    [Dependency] private KsKillCreditSystem _ksKillCreditSystem = default!;

    [SubscribeLocalEvent]
    // A by-value event: there is no Entity<T> form of handler for one.
    private void OnDamageChanged(EntityUid uid, NpcPerceptionComponent component, DamageChangedEvent args)
    {
        if (!component.RevealAttackers ||
            !args.DamageIncreased ||
            args.DamageDelta is not { } delta ||
            delta.GetTotal() <= 0)
            return;

        // The hand behind a bullet or a turret, not the bullet.
        if (_ksKillCreditSystem.ResolveCredit(args.Origin) is not { } attackerUid ||
            attackerUid == uid ||
            TerminatingOrDeleted(attackerUid) ||
            !_mobStateSystem.IsAlive(attackerUid) ||
            _npcFactionSystem.IsEntityFriendly(uid, attackerUid))
            return;

        Reveal((uid, component), attackerUid);
    }

    /// <summary>
    ///     Makes <paramref name="entity"/> know <paramref name="attackerUid"/> is there, and where, as if it had seen
    ///         it, for <see cref="NpcPerceptionComponent.RevealDuration"/>.
    /// </summary>
    private void Reveal(Entity<NpcPerceptionComponent> entity, EntityUid attackerUid)
    {
        var now = _gameTiming.CurTime;
        entity.Comp.KnownDead.Remove(attackerUid);

        var hasContact = entity.Comp.Contacts.TryGetValue(attackerUid, out var contact);
        var wasEngaged = hasContact && contact is { State: NpcContactState.Visible, Reacted: true };

        var revealed = Sight(entity,
            attackerUid,
            hasContact ? contact : null,
            conspicuous: true,
            _physicsSystem.GetMapLinearVelocity(attackerUid),
            seenLight: float.NaN, // not seen: worked out if it's ever needed
            alert: true,
            now);
        entity.Comp.Contacts[attackerUid] = revealed with { RevealedUntil = now + entity.Comp.RevealDuration };

        // With no target yet its sight is the short one: take the one it fights with, or a hit from beyond the first
        //      would be known of and not answered.
        if (_htnQuery.TryComp(entity.Owner, out var htnComponent) &&
            !htnComponent.Blackboard.ContainsKey("Target"))
            htnComponent.Blackboard.SetValue("Target", attackerUid);

        if (!wasEngaged)
            _npcSensorSystem.RequestReplan(entity.Owner);
    }

    /// <summary>
    ///     A revealed contact's update: still known where it is now, wherever that is, until the reveal runs out.
    /// </summary>
    private ContactOutcome UpdateRevealed(Entity<NpcPerceptionComponent> entity,
        EntityUid targetUid,
        NpcContact contact,
        TimeSpan now)
    {
        var reactedBefore = contact is { State: NpcContactState.Visible, Reacted: true };

        var updated = Sight(entity,
            targetUid,
            contact,
            conspicuous: true,
            _physicsSystem.GetMapLinearVelocity(targetUid),
            seenLight: float.NaN, // not seen: worked out if it's ever needed
            alert: true,
            now);
        entity.Comp.Contacts[targetUid] = updated with { RevealedUntil = contact.RevealedUntil };

        return updated.Reacted && !reactedBefore
            ? ContactOutcome.Replan | ContactOutcome.NewlyReacted
            : ContactOutcome.None;
    }
}
