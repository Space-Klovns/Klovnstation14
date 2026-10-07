using System.Diagnostics.CodeAnalysis;
using Content.Server.NPC.Components;
using Content.Shared._KS14.NPC;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

public sealed partial class NpcPerceptionSystem
{
    /// <summary>
    ///     How far ahead, in seconds of its last velocity, a target that has just vanished is looked for before
    ///         deciding it vanished in plain sight. Long enough that someone who ran round a corner is behind it by
    ///         then, short enough that someone who stopped is still where they were.
    /// </summary>
    private const float VanishLookahead = 0.5f;

    private readonly HashSet<Entity<EntityStorageComponent>> _nearbyStorages = new();

    /// <summary>
    ///     A target that was in sight last update and is not now. Seen climbing into a locker - it is in one now,
    ///         and was in sight a moment ago - it is concealed there. Gone in plain sight right next to a closed
    ///         locker, it is suspected to be in that. Otherwise it is simply lost, remembered where it was.
    /// </summary>
    private NpcContact LoseSight(Entity<NpcPerceptionComponent> entity,
        EntityUid targetUid,
        NpcContact contact,
        MapCoordinates observerMapCoordinates,
        float range)
    {
        contact = contact with { ObserverWasMoving = IsAdvancing(entity.Owner) };

        if (TryGetStorageAround(targetUid, out var storageUid))
            return contact with { State = NpcContactState.Concealed, ContainerUid = storageUid };

        // In something that is not a locker - carried in a bag, in a mech, down a disposal chute: not somewhere
        //      anyone could go and look, so just lost.
        if (!_containerSystem.IsEntityOrParentInContainer(targetUid) &&
            VanishedInPlainSight(contact, observerMapCoordinates, range) &&
            TryGetClosedStorageNear(contact.LastKnownCoordinates, entity.Comp.SuspicionRange, out storageUid))
            return contact with { State = NpcContactState.Suspected, ContainerUid = storageUid };

        return contact with { State = NpcContactState.Lost, ContainerUid = null };
    }

    /// <summary>
    ///     Whether the NPC is going somewhere: steering towards a destination, rather than standing, or juking about
    ///         one spot in a firefight.
    /// </summary>
    private bool IsAdvancing(EntityUid uid)
    {
        return _steeringQuery.TryComp(uid, out var steeringComponent) &&
            steeringComponent.Status == SteeringStatus.Moving &&
            !_jukeQuery.HasComp(uid);
    }

    /// <summary>
    ///     Checks on a target believed to be hiding. A hiding place seen open and empty means it is not there after
    ///         all: a concealed target is then lost (it was definitely around), and a suspected one forgotten. A
    ///         hiding place that is gone leaves the same. Returns whether that is worth replanning for.
    /// </summary>
    private bool UpdateHidden(Entity<NpcPerceptionComponent> entity,
        EntityUid targetUid,
        NpcContact contact,
        MapCoordinates observerMapCoordinates,
        float range)
    {
        var storageGone = contact.ContainerUid is not { } storageUid || TerminatingOrDeleted(storageUid);

        if (!storageGone &&
            !(_entityStorageQuery.TryComp(contact.ContainerUid!.Value, out var storageComponent) &&
                storageComponent.Open &&
                _npcLineOfSightSystem.InLineOfSight(observerMapCoordinates, _transformSystem.GetMapCoordinates(contact.ContainerUid.Value), range)))
            return false;

        if (contact.State == NpcContactState.Suspected)
            entity.Comp.Contacts.Remove(targetUid);
        else
            entity.Comp.Contacts[targetUid] = contact with { State = NpcContactState.Lost, ContainerUid = null };

        return true;
    }

    /// <summary>
    ///     The nearest <see cref="EntityStorageComponent"/> the target is inside, at any depth: a locker, a crate, a
    ///         body bag. The nearest rather than the outermost, so a target in a bag that is in a locker is in the
    ///         locker, but one carried in a bag by someone is in nothing anyone can search.
    /// </summary>
    private bool TryGetStorageAround(EntityUid targetUid, [NotNullWhen(true)] out EntityUid? storageUid)
    {
        storageUid = null;
        var uid = targetUid;

        while (_containerSystem.TryGetContainingContainer(uid, out var container))
        {
            if (_entityStorageQuery.HasComp(container.Owner))
            {
                storageUid = container.Owner;
                return true;
            }

            uid = container.Owner;
        }

        return false;
    }

    /// <summary>
    ///     Whether the NPC can still see the spot the target should be at by now, had it carried on as it was going.
    ///         Someone who ran round a corner should be behind it, out of sight; someone who stopped dead and
    ///         vanished is somewhere they can see, and is not there.
    /// </summary>
    private bool VanishedInPlainSight(NpcContact contact, MapCoordinates observerMapCoordinates, float range)
    {
        if (TerminatingOrDeleted(contact.LastKnownCoordinates.EntityId))
            return false;

        var lastKnownMapCoordinates = _transformSystem.ToMapCoordinates(contact.LastKnownCoordinates);
        var expectedMapCoordinates = lastKnownMapCoordinates.Offset(contact.LastKnownVelocity * VanishLookahead);

        return _npcLineOfSightSystem.InLineOfSight(observerMapCoordinates, expectedMapCoordinates, range);
    }

    private bool TryGetClosedStorageNear(EntityCoordinates coordinates, float range, [NotNullWhen(true)] out EntityUid? storageUid)
    {
        storageUid = null;

        if (TerminatingOrDeleted(coordinates.EntityId))
            return false;

        var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
        var bestDistanceSquared = float.MaxValue;

        _nearbyStorages.Clear();
        _entityLookupSystem.GetEntitiesInRange(mapCoordinates, range, _nearbyStorages, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Uncontained);

        foreach (var storage in _nearbyStorages)
        {
            if (storage.Comp.Open)
                continue;

            var distanceSquared = (_transformSystem.GetMapCoordinates(storage.Owner).Position - mapCoordinates.Position).LengthSquared();
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            storageUid = storage.Owner;
        }

        return storageUid != null;
    }
}
