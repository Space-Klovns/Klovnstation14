using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._KS14.NPC.HTN.Preconditions;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._KS14.NPC;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Keeps each <see cref="NpcPerceptionComponent"/> NPC's beliefs about the hostiles around it up to date: which it
///         can see, which it has lost and where, which it saw (or suspects) hiding in a locker, and which a squadmate
///         called out. This is the only place NPC sight is worked out - HTN reads the result through the public API
///         below, and is told to replan when something worth reacting to changes.
/// </summary>
/// <remarks>
///     The beliefs are deliberately beliefs, not the truth: a hostile that slips out of a locker unseen is still
///         believed to be inside it until someone checks, and one that ran out of sight is remembered where it was,
///         not wherever it is now.
/// </remarks>
public sealed partial class NpcPerceptionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcLightDetectionSystem _npcLightDetectionSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStealthSystem _stealthSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcPerceptionComponent> _perceptionQuery = default!;
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;
    [Dependency] private EntityQuery<EntityStorageComponent> _entityStorageQuery = default!;
    [Dependency] private EntityQuery<StealthComponent> _stealthQuery = default!;
    [Dependency] private EntityQuery<NPCSteeringComponent> _steeringQuery = default!;
    [Dependency] private EntityQuery<NPCJukeComponent> _jukeQuery = default!;

    /// <summary>
    ///     Hostiles the NPC could possibly see this update: in range, dead or alive.
    /// </summary>
    private readonly HashSet<EntityUid> _candidates = new();

    /// <summary>
    ///     Every hostile to look at this update: the candidates, plus everything already remembered.
    /// </summary>
    private readonly HashSet<EntityUid> _targets = new();

    /// <summary>
    ///     Hostiles out of sight whose reaction came due this update: glimpsed, then gone before the NPC got round
    ///         to reacting. Called out like ones in sight, since the squad cannot hear about them any other way.
    /// </summary>
    private readonly List<EntityUid> _newlyReactedUnseen = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<NpcPerceptionComponent, ActiveNPCComponent>();

        while (enumerator.MoveNext(out var uid, out var perceptionComponent, out _))
        {
            if (now < perceptionComponent.NextUpdate)
                continue;

            perceptionComponent.NextUpdate = now + perceptionComponent.UpdateInterval;
            UpdatePerception((uid, perceptionComponent), now);
        }
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<NpcPerceptionComponent> entity, ref MapInitEvent args)
    {
        // Spread across the interval, so a squad spawned together does not all look around on the same tick.
        entity.Comp.NextUpdate = _gameTiming.CurTime + entity.Comp.UpdateInterval * _robustRandom.NextDouble();
    }

    /// <summary>
    ///     Runs one perception update for <paramref name="uid"/> right now, whether or not it is due, and whether
    ///         or not the NPC is awake. For tests.
    /// </summary>
    internal void UpdateNow(EntityUid uid)
    {
        if (_perceptionQuery.TryComp(uid, out var perceptionComponent))
            UpdatePerception((uid, perceptionComponent), _gameTiming.CurTime);
    }

    /// <summary>
    ///     Makes <paramref name="uid"/> believe <paramref name="contact"/> about <paramref name="targetUid"/>, as if it
    ///         had seen it. For tests that need a belief without staging the scene that would produce it.
    /// </summary>
    internal void SetContact(EntityUid uid, EntityUid targetUid, NpcContact contact)
    {
        if (_perceptionQuery.TryComp(uid, out var perceptionComponent))
            perceptionComponent.Contacts[targetUid] = contact;
    }

    private void UpdatePerception(Entity<NpcPerceptionComponent> entity, TimeSpan now)
    {
        if (!_mobStateSystem.IsAlive(entity.Owner))
            return;

        var alert = IsAlert(entity);
        var range = GetVisionRange(entity.Owner);
        var observerMapCoordinates = _transformSystem.GetMapCoordinates(entity.Owner);

        _candidates.Clear();
        _npcFactionSystem.GetNearbyHostiles(entity.Owner, range, _candidates);

        // Loops rather than UnionWith, which takes an IEnumerable and so boxes the enumerator.
        _targets.Clear();
        foreach (var targetUid in _candidates)
        {
            _targets.Add(targetUid);
        }

        foreach (var targetUid in entity.Comp.Contacts.Keys)
        {
            _targets.Add(targetUid);
        }

        var replan = false;
        var newlyReacted = false;
        _newlyReactedUnseen.Clear();

        // Contacts is written to from here on, but never enumerated again until the next update.
        foreach (var targetUid in _targets)
        {
            if (TerminatingOrDeleted(targetUid))
            {
                replan |= Forget(entity, targetUid);
                continue;
            }

            // Dead: known only once seen, or told. See NpcPerceptionSystem.Deaths.cs.
            if (_mobStateSystem.IsDead(targetUid))
            {
                replan |= UpdateDead(entity, targetUid, observerMapCoordinates, range, now);
                continue;
            }

            // Alive after all: brought back since.
            entity.Comp.KnownDead.Remove(targetUid);

            var hasContact = entity.Comp.Contacts.TryGetValue(targetUid, out var contact);
            var wasVisible = hasContact && contact.State == NpcContactState.Visible;

            // Remembered somewhere that no longer exists - its grid deleted - so there is nowhere left to look.
            if (hasContact && !wasVisible && TerminatingOrDeleted(contact.LastKnownCoordinates.EntityId))
            {
                Forget(entity, targetUid);
                continue;
            }

            var isCandidate = _candidates.Contains(targetUid);
            var conspicuous = false;
            var targetVelocity = System.Numerics.Vector2.Zero;
            var seen = (isCandidate || wasVisible) &&
                CanSee(entity, observerMapCoordinates, targetUid, range, wasVisible ? contact : null, now, out conspicuous, out targetVelocity);

            // In plain sight, but no longer a hostile: retaliation wore off, or it was told to be ignored. Not a
            //      hostile that vanished, so neither lost nor hiding - just not worth remembering.
            if (wasVisible && seen && !isCandidate)
            {
                replan |= Forget(entity, targetUid);
                continue;
            }

            if (isCandidate && seen)
            {
                var reactedBefore = wasVisible && contact.Reacted;
                contact = Sight(entity, targetUid, hasContact ? contact : null, conspicuous, targetVelocity, alert, now);
                entity.Comp.Contacts[targetUid] = contact;

                if (contact.Reacted && !reactedBefore)
                {
                    replan = true;
                    newlyReacted = true;
                }

                continue;
            }

            if (!hasContact)
                continue;

            // Noticed, then out of sight before the reaction came: the NPC still reacts, just to where it was.
            if (!contact.Reacted &&
                contact.State != NpcContactState.Reported &&
                contact.ReactAt != default &&
                now >= contact.ReactAt)
            {
                contact = contact with { Reacted = true };
                entity.Comp.Contacts[targetUid] = contact;
                _newlyReactedUnseen.Add(targetUid);
                replan = true;
                newlyReacted = true;
            }

            replan |= UpdateUnseen(entity, targetUid, contact, observerMapCoordinates, range, now);
        }

        UpdateCallouts(entity, now, force: newlyReacted);

        if (replan)
            _npcSensorSystem.RequestReplan(entity.Owner);
    }

    /// <summary>
    ///     Records that the target is in sight right now, starting a new sighting if it was not already. A new
    ///         sighting starts the reaction clock - unless the NPC had already reacted to it, or already noticed it,
    ///         a moment ago: a target that ducks in and out of view is the same target, not a new surprise each time.
    /// </summary>
    /// <remarks>
    ///     The reaction time is a delay, not an exposure requirement. The clock starts the moment the target is
    ///         noticed and runs out whether or not it is still in sight then; see <see cref="NpcContact.ReactAt"/>.
    /// </remarks>
    private NpcContact Sight(Entity<NpcPerceptionComponent> entity,
        EntityUid targetUid,
        NpcContact? previous,
        bool conspicuous,
        Vector2 targetVelocity,
        bool alert,
        TimeSpan now)
    {
        TimeSpan firstSeen;
        TimeSpan lastConspicuous;
        TimeSpan reactAt;
        bool reacted;

        if (previous is { State: NpcContactState.Visible } visible)
        {
            firstSeen = visible.FirstSeen;
            lastConspicuous = conspicuous ? now : visible.LastConspicuous;
            reactAt = visible.ReactAt;
            reacted = visible.Reacted;
        }
        else if (previous is { State: not NpcContactState.Reported } seenBefore &&
            (seenBefore.Reacted || seenBefore.ReactAt != default) &&
            now - seenBefore.LastSeen <= entity.Comp.ReactionForgetTime)
        {
            // Only out of view for a moment: still the same target, so its reaction - done or under way - carries
            //      on. A callout is not a sighting, so a Reported contact never gets here.
            firstSeen = seenBefore.FirstSeen;
            lastConspicuous = now;
            reactAt = seenBefore.ReactAt;
            reacted = seenBefore.Reacted;
        }
        else
        {
            firstSeen = now;
            lastConspicuous = now;
            reactAt = alert ? now : now + GetReactionTime(entity, targetUid);
            reacted = false;
        }

        if (!reacted)
            reacted = alert || now >= reactAt;

        return new NpcContact(NpcContactState.Visible,
            firstSeen,
            LastSeen: now,
            lastConspicuous,
            _transformSystem.GetMoverCoordinates(targetUid),
            targetVelocity,
            ContainerUid: null,
            reacted,
            reactAt);
    }

    /// <summary>
    ///     Moves a remembered target that is not in sight along: from seen to lost or hidden, from hidden to lost
    ///         once its hiding place is seen to be empty, and out of memory once it has been gone long enough.
    ///         Returns whether the change is worth replanning for.
    /// </summary>
    private bool UpdateUnseen(Entity<NpcPerceptionComponent> entity,
        EntityUid targetUid,
        NpcContact contact,
        MapCoordinates observerMapCoordinates,
        float range,
        TimeSpan now)
    {
        switch (contact.State)
        {
            case NpcContactState.Visible:
                entity.Comp.Contacts[targetUid] = LoseSight(entity, targetUid, contact, observerMapCoordinates, range);
                return true;

            case NpcContactState.Concealed:
            case NpcContactState.Suspected:
                if (now - contact.LastSeen > entity.Comp.ConcealedMemoryTime)
                    return Forget(entity, targetUid);

                return UpdateHidden(entity, targetUid, contact, observerMapCoordinates, range);

            default:
                if (now - contact.LastSeen > entity.Comp.MemoryTime)
                    return Forget(entity, targetUid);

                return false;
        }
    }

    /// <summary>
    ///     Drops the target from memory. Returns whether it was in sight, which is worth replanning for.
    /// </summary>
    private bool Forget(Entity<NpcPerceptionComponent> entity, EntityUid targetUid)
    {
        return entity.Comp.Contacts.Remove(targetUid, out var contact) && contact.State == NpcContactState.Visible;
    }

    private bool IsAlert(Entity<NpcPerceptionComponent> entity)
    {
        if (entity.Comp.AlertMarker is { } alertMarker &&
            _htnQuery.TryComp(entity.Owner, out var htnComponent) &&
            HasVirtualMarkerPrecondition.HasMarker(htnComponent.Blackboard, alertMarker, EntityManager))
            return true;

        return _npcSquadSystem.IsEngaged(entity.Owner, entity.Comp.SquadAlertWindow);
    }

    private float GetVisionRange(EntityUid uid)
    {
        if (!_htnQuery.TryComp(uid, out var htnComponent))
            return 0f;

        var blackboard = htnComponent.Blackboard;
        return blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
    }

    private TimeSpan GetReactionTime(Entity<NpcPerceptionComponent> entity, EntityUid targetUid)
    {
        var lightLevel = _npcLightDetectionSystem.GetPerceivedLightLevel(entity.Owner, targetUid, entity.Comp.ProximityRange);
        return entity.Comp.ReactionTime * (1f + entity.Comp.DarknessReactionScale * (1f - lightLevel));
    }

    #region Public API

    /// <summary>
    ///     Makes <paramref name="observer"/> give up on <paramref name="targetUid"/>, as when a search for it has come
    ///         up empty. It is noticed afresh, reaction time and all, if it shows itself again.
    /// </summary>
    public void ForgetContact(Entity<NpcPerceptionComponent?> observer, EntityUid targetUid)
    {
        if (_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false) &&
            observer.Comp.Contacts.Remove(targetUid))
            _npcSensorSystem.RequestReplan(observer.Owner);
    }

    public bool TryGetContact(Entity<NpcPerceptionComponent?> observer, EntityUid targetUid, out NpcContact contact)
    {
        contact = default;
        return _perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false) &&
            observer.Comp.Contacts.TryGetValue(targetUid, out contact);
    }

    /// <summary>
    ///     Whether <paramref name="observer"/> can see <paramref name="targetUid"/> right now. True for an NPC
    ///         without perception, which has no opinion.
    /// </summary>
    public bool IsVisible(Entity<NpcPerceptionComponent?> observer, EntityUid targetUid)
    {
        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false))
            return true;

        return observer.Comp.Contacts.TryGetValue(targetUid, out var contact) && contact.State == NpcContactState.Visible;
    }

    /// <summary>
    ///     Adds every hostile <paramref name="observer"/> knows of in one of <paramref name="states"/> to
    ///         <paramref name="results"/>. With <paramref name="reactedOnly"/>, only those it has reacted to.
    /// </summary>
    public void GetContacts(Entity<NpcPerceptionComponent?> observer,
        List<NpcContactState> states,
        bool reactedOnly,
        HashSet<EntityUid> results)
    {
        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false))
            return;

        foreach (var (targetUid, contact) in observer.Comp.Contacts)
        {
            if (states.Contains(contact.State) && (!reactedOnly || contact.Reacted))
                results.Add(targetUid);
        }
    }

    /// <summary>
    ///     Whether <paramref name="observer"/> knows of any hostile in one of <paramref name="states"/>, last seen (or
    ///         called out) no longer than <paramref name="maxAge"/> ago, if given, and believed within
    ///         <paramref name="maxDistance"/> tiles of it, if above 0.
    /// </summary>
    public bool HasContact(Entity<NpcPerceptionComponent?> observer,
        List<NpcContactState> states,
        TimeSpan? maxAge,
        float maxDistance = 0f)
    {
        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false))
            return false;

        var now = _gameTiming.CurTime;
        var observerMapCoordinates = maxDistance > 0f ? _transformSystem.GetMapCoordinates(observer.Owner) : MapCoordinates.Nullspace;

        foreach (var contact in observer.Comp.Contacts.Values)
        {
            if (!states.Contains(contact.State) || maxAge is { } age && now - contact.LastSeen > age)
                continue;

            if (maxDistance <= 0f)
                return true;

            var contactMapCoordinates = _transformSystem.ToMapCoordinates(contact.LastKnownCoordinates);
            if (contactMapCoordinates.MapId == observerMapCoordinates.MapId &&
                (contactMapCoordinates.Position - observerMapCoordinates.Position).LengthSquared() <= maxDistance * maxDistance)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="observer"/> can see any hostile it has reacted to: it is in a fight.
    /// </summary>
    public bool InCombatContact(Entity<NpcPerceptionComponent?> observer)
    {
        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false))
            return false;

        foreach (var contact in observer.Comp.Contacts.Values)
        {
            if (contact is { State: NpcContactState.Visible, Reacted: true })
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The hostile <paramref name="observer"/> lost sight of most recently, of those it had reacted to and lost no
    ///         longer than <paramref name="maxAge"/> ago.
    /// </summary>
    public bool TryGetLatestLostContact(Entity<NpcPerceptionComponent?> observer,
        TimeSpan maxAge,
        out EntityUid targetUid,
        out NpcContact contact)
    {
        targetUid = default;
        contact = default;

        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false))
            return false;

        var now = _gameTiming.CurTime;
        var found = false;

        foreach (var (candidateUid, candidate) in observer.Comp.Contacts)
        {
            if (candidate is not { State: NpcContactState.Lost, Reacted: true } ||
                now - candidate.LastSeen > maxAge ||
                found && candidate.LastSeen <= contact.LastSeen)
                continue;

            targetUid = candidateUid;
            contact = candidate;
            found = true;
        }

        return found;
    }

    /// <summary>
    ///     Whether <paramref name="observer"/> has seen, or been told of, any hostile within <paramref name="maxAge"/>.
    /// </summary>
    public bool HasRecentContact(Entity<NpcPerceptionComponent?> observer, TimeSpan maxAge)
    {
        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false))
            return false;

        var now = _gameTiming.CurTime;

        foreach (var contact in observer.Comp.Contacts.Values)
        {
            if (now - contact.LastSeen <= maxAge)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Where <paramref name="observer"/> believes <paramref name="targetUid"/> is: on the target itself while it
    ///         is in sight, where it was last seen or called out once it is not, or the storage it is believed to be
    ///         hiding in - which is also returned as <paramref name="containerUid"/>.
    /// </summary>
    public bool TryGetBelievedCoordinates(Entity<NpcPerceptionComponent?> observer,
        EntityUid targetUid,
        out EntityCoordinates coordinates,
        [NotNullWhen(true)] out NpcContactState? state,
        out EntityUid? containerUid)
    {
        coordinates = default;
        state = null;
        containerUid = null;

        if (!TryGetContact(observer, targetUid, out var contact))
            return false;

        state = contact.State;
        containerUid = contact.ContainerUid;

        if (contact.State != NpcContactState.Visible && TerminatingOrDeleted(contact.LastKnownCoordinates.EntityId))
            return false;

        coordinates = contact.State switch
        {
            NpcContactState.Visible => new EntityCoordinates(targetUid, Vector2.Zero),
            NpcContactState.Concealed or NpcContactState.Suspected when contact.ContainerUid is { } storageUid && !TerminatingOrDeleted(storageUid)
                => _transformSystem.GetMoverCoordinates(storageUid),
            _ => contact.LastKnownCoordinates,
        };

        return true;
    }

    #endregion
}
