using Content.Server._KS14.NPC.Utility;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.Perception;

public sealed partial class NpcPerceptionSystem
{
    /// <summary>
    ///     Calls out what the NPC can see to its squad, every <see cref="NpcPerceptionComponent.CalloutInterval"/>
    ///         or at once with <paramref name="force"/>, when it has just reacted to something. Squadmates that
    ///         cannot see a called-out hostile themselves learn where it is (<see cref="NpcContactState.Reported"/>),
    ///         and the squad's threat follows the caller's main target.
    /// </summary>
    private void UpdateCallouts(Entity<NpcPerceptionComponent> entity, TimeSpan now, bool force)
    {
        if (!NpcThrottle.TryConsume(ref entity.Comp.NextCallout, now, entity.Comp.CalloutInterval, force))
            return;

        if (!entity.Comp.CallsOutContacts || !_npcSquadSystem.TryGetSquad(entity.Owner, out _))
            return;

        // One threat report per caller per callout, for its main target: several members each reporting every
        //      hostile they see would drag the squad's threat back and forth between them.
        if (TryGetPrimaryContact(entity, out var primaryContact))
            _npcSquadSystem.ReportContact(entity.Owner, primaryContact.LastKnownCoordinates);

        foreach (var (targetUid, contact) in entity.Comp.Contacts)
        {
            // In sight, or only just reacted to after slipping out of it: either way, news the squad needs.
            if (!contact.Reacted ||
                contact.State != NpcContactState.Visible && !_newlyReactedUnseen.Contains(targetUid))
                continue;

            var ev = new NpcContactCalloutEvent(entity.Owner, targetUid, contact.LastKnownCoordinates, contact.LastSeen);
            _npcSquadSystem.CallOut(entity.Owner, ref ev);
        }
    }

    /// <summary>
    ///     A squadmate called out a hostile. Only news if the listener cannot see it itself: what it sees beats what it
    ///         is told.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnContactCallout(Entity<NpcPerceptionComponent> entity, ref NpcContactCalloutEvent args)
    {
        if (!entity.Comp.HearsCallouts)
            return;

        var known = entity.Comp.Contacts.TryGetValue(args.TargetUid, out var contact);
        if (known && contact.State == NpcContactState.Visible)
            return;

        // What it saw itself, as recently, beats being told: kept, with the way it was going, which a callout has not got.
        //      And an older sighting than one it was already told of tells it nothing: callouts need not come in order.
        if (known && (contact.State == NpcContactState.Reported ? contact.LastSeen > args.LastSeen : contact.LastSeen >= args.LastSeen))
            return;

        // As of when the caller saw it, not when this one heard: a hunt goes by when it was really lost (NpcHunt.LostAt).
        entity.Comp.Contacts[args.TargetUid] = new NpcContact(NpcContactState.Reported,
            FirstSeen: args.LastSeen,
            LastSeen: args.LastSeen,
            LastConspicuous: default,
            args.Coordinates,
            LastKnownVelocity: default,
            ContainerUid: null,
            Reacted: false);

        if (!known || contact.State != NpcContactState.Reported)
            _npcSensorSystem.RequestReplan(entity.Owner);
    }

    /// <summary>
    ///     The contact the NPC is fighting: its current target, if it can see it, otherwise the nearest hostile it
    ///         can see and has reacted to. Failing those, one it has only just reacted to out of sight.
    /// </summary>
    private bool TryGetPrimaryContact(Entity<NpcPerceptionComponent> entity, out NpcContact primaryContact)
    {
        primaryContact = default;

        if (_htnQuery.TryComp(entity.Owner, out var htnComponent) &&
            htnComponent.Blackboard.TryGetValue<EntityUid>("Target", out var currentTargetUid, EntityManager) &&
            entity.Comp.Contacts.TryGetValue(currentTargetUid, out var currentContact) &&
            currentContact is { State: NpcContactState.Visible, Reacted: true })
        {
            primaryContact = currentContact;
            return true;
        }

        var ownerPosition = _transformSystem.GetMapCoordinates(entity.Owner).Position;
        var best = NpcBestOf<NpcContact>.Lowest();

        foreach (var contact in entity.Comp.Contacts.Values)
        {
            if (contact.State != NpcContactState.Visible || !contact.Reacted)
                continue;

            var distanceSquared = (_transformSystem.ToMapCoordinates(contact.LastKnownCoordinates).Position - ownerPosition).LengthSquared();
            best.Offer(contact, distanceSquared);
        }

        var found = best.Found;
        if (found)
            primaryContact = best.Value!;
        else if (_newlyReactedUnseen.Count > 0)
            found = entity.Comp.Contacts.TryGetValue(_newlyReactedUnseen[0], out primaryContact);

        return found;
    }
}
