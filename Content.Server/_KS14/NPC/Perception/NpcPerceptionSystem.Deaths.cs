using Content.Shared._KS14.NPC;
using Content.Shared.Mobs;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Hostiles dying. An NPC only knows a hostile is dead once it has reason to: it sees the body, sees it die, or
///         killed it, or a squadmate that knows tells it. Until then a hostile that died out of everyone's sight is
///         believed alive, remembered and hunted like any other lost hostile - until somebody finds the body.
/// </summary>
public sealed partial class NpcPerceptionSystem
{
    /// <summary>
    ///     Whether <paramref name="observer"/> knows <paramref name="targetUid"/> to be dead.
    /// </summary>
    public bool IsKnownDead(Entity<NpcPerceptionComponent?> observer, EntityUid targetUid)
    {
        return _perceptionQuery.Resolve(observer.Owner, ref observer.Comp, logMissing: false) &&
            observer.Comp.KnownDead.TryGetValue(targetUid, out var forgetAt) &&
            forgetAt > _gameTiming.CurTime;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> confirmed a death within <paramref name="within"/> that it has not called out
    ///         yet.
    /// </summary>
    public bool HasPendingKillCallout(EntityUid uid, TimeSpan within)
    {
        return _perceptionQuery.TryComp(uid, out var perceptionComponent) &&
            perceptionComponent.ConfirmedKillAt is { } confirmedAt &&
            _gameTiming.CurTime - confirmedAt <= within;
    }

    /// <summary>
    ///     <paramref name="uid"/> has called out the death it confirmed.
    /// </summary>
    public void ClearPendingKillCallout(EntityUid uid)
    {
        if (_perceptionQuery.TryComp(uid, out var perceptionComponent))
            perceptionComponent.ConfirmedKillAt = null;
    }

    /// <summary>
    ///     Whoever killed a hostile knows it did, wherever the body fell: out of sight behind a wall from a grenade,
    ///         say. Only a hostile it knew of - a stray round killing something it never saw tells it nothing.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead ||
            args.Origin is not { } killerUid ||
            !_perceptionQuery.TryComp(killerUid, out var killerComponent) ||
            !killerComponent.Contacts.ContainsKey(args.Target) ||
            !_mobStateSystem.IsAlive(killerUid))
            return;

        ConfirmDead((killerUid, killerComponent), args.Target, _gameTiming.CurTime);
    }

    /// <summary>
    ///     A hostile that is dead, as one update sees it. Seen dead - or seen dying - it is confirmed. Otherwise, as far
    ///         as the NPC knows, it is wherever it was last seen, alive. Returns whether the change is worth replanning
    ///         for.
    /// </summary>
    private bool UpdateDead(Entity<NpcPerceptionComponent> entity,
        EntityUid targetUid,
        MapCoordinates observerMapCoordinates,
        float range,
        TimeSpan now)
    {
        // Already known: nothing about it is news. The contact goes, if a callout from someone who does not know put it
        //      back.
        if (IsKnownDead(entity.AsNullable(), targetUid))
            return Forget(entity, targetUid);

        var hasContact = entity.Comp.Contacts.TryGetValue(targetUid, out var contact);
        var watched = hasContact && contact.State == NpcContactState.Visible ? contact : (NpcContact?) null;

        if (CanSee(entity, observerMapCoordinates, targetUid, range, watched, now, out _, out _))
        {
            ConfirmDead(entity, targetUid, now);
            return true;
        }

        // Died out of its sight: as far as it knows, still out there.
        return hasContact && UpdateUnseen(entity, targetUid, contact, observerMapCoordinates, range, now);
    }

    /// <summary>
    ///     <paramref name="entity"/> now knows <paramref name="targetUid"/> is dead, and so does its squad: the hostile is
    ///         dropped from their beliefs rather than lost, so nobody goes looking for it. If it, or anyone in its
    ///         squad, believed the hostile alive, that is news, and it calls it out.
    /// </summary>
    private void ConfirmDead(Entity<NpcPerceptionComponent> entity, EntityUid targetUid, TimeSpan now)
    {
        var news = LearnDead(entity, targetUid, now);

        if (entity.Comp.SharesKills)
        {
            var ev = new NpcKillCalloutEvent(entity.Owner, targetUid);
            _npcSquadSystem.CallOut(entity.Owner, ref ev);
            news |= ev.News;
        }

        if (news)
        {
            entity.Comp.ConfirmedKillAt = now;
            _npcSensorSystem.RequestReplan(entity.Owner);
        }
    }

    /// <summary>
    ///     A squadmate knows a hostile is dead, so now this one does too - if it is listening.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnKillCallout(Entity<NpcPerceptionComponent> entity, ref NpcKillCalloutEvent args)
    {
        if (entity.Comp.HearsCallouts)
            args.News |= LearnDead(entity, args.TargetUid, _gameTiming.CurTime);
    }

    /// <summary>
    ///     Remembers <paramref name="targetUid"/> as dead and forgets it as a hostile. Returns whether
    ///         <paramref name="entity"/> believed it alive until now.
    /// </summary>
    private bool LearnDead(Entity<NpcPerceptionComponent> entity, EntityUid targetUid, TimeSpan now)
    {
        // Pruned here, not on a timer: nothing else needs them gone. Removing during enumeration is allowed for a
        //      Dictionary.
        foreach (var (deadUid, forgetAt) in entity.Comp.KnownDead)
        {
            if (forgetAt <= now)
                entity.Comp.KnownDead.Remove(deadUid);
        }

        entity.Comp.KnownDead[targetUid] = now + entity.Comp.MemoryTime;

        if (!entity.Comp.Contacts.Remove(targetUid))
            return false;

        _npcSensorSystem.RequestReplan(entity.Owner);
        return true;
    }
}
