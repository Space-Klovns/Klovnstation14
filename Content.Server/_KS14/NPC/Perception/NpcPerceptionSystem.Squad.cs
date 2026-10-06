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
        if (!force && now < entity.Comp.NextCallout)
            return;

        entity.Comp.NextCallout = now + entity.Comp.CalloutInterval;

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

            var ev = new NpcContactCalloutEvent(entity.Owner, targetUid, contact.LastKnownCoordinates);
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

        var now = _gameTiming.CurTime;
        entity.Comp.Contacts[args.TargetUid] = new NpcContact(NpcContactState.Reported,
            FirstSeen: now,
            LastSeen: now,
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
        var bestDistanceSquared = float.MaxValue;
        var found = false;

        foreach (var contact in entity.Comp.Contacts.Values)
        {
            if (contact.State != NpcContactState.Visible || !contact.Reacted)
                continue;

            var distanceSquared = (_transformSystem.ToMapCoordinates(contact.LastKnownCoordinates).Position - ownerPosition).LengthSquared();
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            primaryContact = contact;
            found = true;
        }

        if (!found && _newlyReactedUnseen.Count > 0)
            found = entity.Comp.Contacts.TryGetValue(_newlyReactedUnseen[0], out primaryContact);

        return found;
    }
}
