using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.NPC;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Being shot gives the shooter away. With <see cref="NpcPerceptionComponent.RevealAttackers"/>, whatever hurts the
///         NPC (a player, or a turret, but never the turret's owner) becomes known to it wherever it is on its map, so it can turn and fire back, and nobody can pick it off from
///         where it cannot see them. The knowledge is a plain visible contact that holds for
///         <see cref="NpcPerceptionComponent.RevealDuration"/> after the last hit and then decays like any other
///         sighting, and it reaches the squad through the usual callouts.
/// </summary>
public sealed partial class NpcPerceptionSystem
{
    [Dependency] private KsKillCreditSystem _ksKillCreditSystem = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    [SubscribeLocalEvent]
    // A by-value event: there is no Entity<T> form of handler for one.
    private void OnDamageChanged(EntityUid uid, NpcPerceptionComponent component, DamageChangedEvent args)
    {
        if (!component.RevealAttackers ||
            !args.DamageIncreased ||
            args.DamageDelta is not { } delta ||
            delta.GetTotal() <= 0)
            return;

        if (GetRevealedAttacker(uid, args.Origin) is { } attackerUid)
            Reveal((uid, component), attackerUid);
    }

    /// <summary>
    ///     What a hit gives away: the thing that shot, never whoever answers for it. A turret's bullet reveals the
    ///         turret where it stands, not the player who set it up, who may be anywhere. Null if there is nothing
    ///         to turn on: it is gone, dead, a friend, or on another map.
    /// </summary>
    private EntityUid? GetRevealedAttacker(EntityUid uid, EntityUid? origin)
    {
        // The shooter behind a bullet, or whatever set off an explosion: not the bullet.
        if (_ksKillCreditSystem.GetAttacker(origin) is not { } attackerUid || TerminatingOrDeleted(attackerUid))
            return null;

        // A gun in someone's hands is its holder shooting: a beam names the gun.
        if (_containerSystem.TryGetOuterContainer(attackerUid, Transform(attackerUid), out var container))
            attackerUid = container.Owner;

        if (attackerUid == uid ||
            // A mob has to be alive. A machine, a turret, has no life to check: it is there until it is deleted.
            _mobStateQuery.HasComp(attackerUid) && !_mobStateSystem.IsAlive(attackerUid) ||
            // Known wherever it is, but not on another map, where there is no way to turn towards it.
            Transform(attackerUid).MapID != Transform(uid).MapID ||
            _npcFactionSystem.IsEntityFriendly(uid, attackerUid))
            return null;

        return attackerUid;
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
    ///     A reveal that has run out without the target coming into sight: it is lost where it last fired from, and
    ///         nothing more. It was never looked at, so nothing was made of it (no <see cref="NpcContact.Traits"/>), and
    ///         it did not vanish from under the NPC's eyes, so it is not suspected of hiding in a locker nearby as a
    ///         hostile that had been in sight would be (<see cref="LoseSight"/>): a turret does not climb into one.
    /// </summary>
    private NpcContact LoseRevealed(Entity<NpcPerceptionComponent> entity, NpcContact contact)
    {
        return ToLost(contact with
        {
            ObserverWasMoving = IsAdvancing(entity.Owner),
            RevealedUntil = default,
            Traits = NpcContactTraits.None,
        });
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
