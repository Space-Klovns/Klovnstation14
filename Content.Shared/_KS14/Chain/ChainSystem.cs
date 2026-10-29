using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Robust.Shared.Collections;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Chain;

/*
    So currently chain edges *also* have chain link components
    This is intentional and it sucks
*/

/// <summary>
///     If you want to break a chain, remove <see cref="ChainLinkComponent"/>
///         from one of the links or delete it.
/// </summary>
public sealed partial class ChainSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedJointSystem _jointSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;

    [Dependency] private EntityQuery<ChainLinkComponent> _linkQuery = default!;
    [Dependency] private EntityQuery<ChainEdgeComponent> _edgeQuery = default!;
    [Dependency] private EntityQuery<JointComponent> _jointQuery = default!;

    private const string StretchJointSuffix = "chainstretch";

    /// <summary>
    ///     Most links walked looking for a segment's ends, against a chain that somehow loops.
    /// </summary>
    private const int MaxStretchWalk = 1000;

    /// <returns><see cref="ChainEdgeComponent.LinkUids"/>, but without any edges.</returns>
    public ValueList<EntityUid> GetLinksWithoutEdges(Entity<ChainEdgeComponent?> firstEdgeEntity)
    {
        if (!_edgeQuery.Resolve(firstEdgeEntity, ref firstEdgeEntity.Comp))
            return [];

        var list = new ValueList<EntityUid>();
        foreach (var linkUid in firstEdgeEntity.Comp.LinkUids)
        {
            if (linkUid == firstEdgeEntity.Owner ||
                linkUid == firstEdgeEntity.Comp.OtherEdgeUid)
                continue;

            list.Add(linkUid);
        }

        return list;
    }

    /// <returns>True if this entity is linked to anything.</returns>
    public bool HasAdjacentLink(Entity<ChainLinkComponent?> linkEntity)
    {
        if (!_linkQuery.Resolve(linkEntity, ref linkEntity.Comp, logMissing: false))
            return false;

        return linkEntity.Comp.PreviousLinkUid is { } || linkEntity.Comp.NextLinkUid is { };
    }

    /// <returns>True if the chain was segmented.</returns>
    public bool TryBreakChainFrom(Entity<ChainLinkComponent?> linkEntity, bool removeJoints = true)
    {
        if (!_linkQuery.Resolve(linkEntity, ref linkEntity.Comp, logMissing: false))
            return false;

        BreakChainFrom(linkEntity!, removeJoints: removeJoints);

        if (linkEntity.Comp.LifeStage < ComponentLifeStage.Stopping)
            RemComp(linkEntity, linkEntity.Comp);

        return true;
    }

    /// <returns>True if the chain was never segmented.</returns>
    public bool IsChainFullyIntact(EntityUid linkUid)
    {
        if (!_linkQuery.TryGetComponent(linkUid, out var linkComponent))
            return false;

        foreach (var edgeUid in linkComponent.EdgeUids)
        {
            var edgeComponent = _edgeQuery.GetComponent(edgeUid);
            return !edgeComponent.Broken;
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnEdgeShutdown(Entity<ChainEdgeComponent> entity, ref ComponentShutdown args)
    {
        foreach (var linkUid in entity.Comp.LinkUids)
        {
            // The link may already be gone, when a whole chain is deleted at once (a map being deleted)
            if (_linkQuery.TryGetComponent(linkUid, out var linkComponent))
                linkComponent.EdgeUids.Remove(entity.Owner);
        }

        if (!Terminating(entity.Owner))
            RemComp<ChainLinkComponent>(entity.Owner);

        // Now handle edges

        var broken = entity.Comp.Broken;
        if (entity.Comp.OtherEdgeUid != EntityUid.Invalid &&
            _edgeQuery.TryGetComponent(entity.Comp.OtherEdgeUid, out var otherEdgeComponent))
        {
            otherEdgeComponent.OtherEdgeUid = EntityUid.Invalid;

            if (!broken)
            {
                var otherBrokenEv = new ChainInitiallyBrokenEvent(entity, otherEdgeComponent);
                RaiseLocalEvent(entity.Comp.OtherEdgeUid, ref otherBrokenEv);

                otherEdgeComponent.Broken = true;
                Dirty(entity.Comp.OtherEdgeUid, otherEdgeComponent);
            }
        }

        if (broken)
            return;

        var ourBrokenEv = new ChainInitiallyBrokenEvent(entity, entity);
        RaiseLocalEvent(entity.Owner, ref ourBrokenEv);

        entity.Comp.Broken = true;
        Dirty(entity);
    }

    [SubscribeLocalEvent]
    private void OnLinkShutdown(Entity<ChainLinkComponent> entity, ref ComponentShutdown args)
    {
        BreakChainFrom(entity, removeJoints: true);
    }

    [SubscribeLocalEvent]
    private void OnJointRemoved(Entity<ChainLinkComponent> ourEntity, ref JointRemovedEvent args)
    {
        // A stretch joint going says nothing about the chain: it is taken off and put back as the chain changes
        if (args.Joint.ID.EndsWith(StretchJointSuffix))
        {
            if (ourEntity.Comp.StretchJointId == args.Joint.ID)
                ourEntity.Comp.StretchJointId = null;

            return;
        }

        // This actually handles edges being broken

        // if its already being deleted, the chain is already broken
        if (TerminatingOrDeleted(ourEntity.Owner) ||
            ourEntity.Comp.LifeStage > ComponentLifeStage.Running) // after running is stopping
            return;

        // so, the chain has broken inbetween us and the other entity

        if (_linkQuery.TryGetComponent(args.OtherEntity, out var otherComponent))
        {
            var segmentedEv = new ChainSegmentedEvent(ourEntity);
            if (otherComponent.NextLinkUid == ourEntity.Owner) // We are the forwards link
            {
                BreakChainBackwards(ourEntity.Owner, ref segmentedEv, removeJoints: false);
                BreakChainForwards(args.OtherEntity, ref segmentedEv, removeJoints: false);
            }
            else if (otherComponent.PreviousLinkUid == ourEntity.Owner) // We are the backwards link
            {
                BreakChainBackwards(args.OtherEntity, ref segmentedEv, removeJoints: false);
                BreakChainForwards(ourEntity.Owner, ref segmentedEv, removeJoints: false);
            }
            else
            {
                // Already disconnected (both ends of a joint get this event, the first one handled it).
                //      Going on would sever links that are still connected to other neighbours.
                return;
            }

            // Update chain edges
            BreakEdges(ourEntity);

            RefreshStretch(ourEntity.Owner);
            RefreshStretch(args.OtherEntity);
        }
    }

    // Containers relay a contained body's joints to the container's owner, so a stretch joint between two ends in the
    //      same container (both held by one person) would join that owner to itself, which crashes the physics solver.
    //      Stretch joints therefore only exist while neither end is contained.
    [SubscribeLocalEvent]
    private void OnLinkInserted(Entity<ChainLinkComponent> entity, ref EntGotInsertedIntoContainerMessage args)
        => RefreshStretch(entity.Owner);

    [SubscribeLocalEvent]
    private void OnLinkRemoved(Entity<ChainLinkComponent> entity, ref EntGotRemovedFromContainerMessage args)
        => RefreshStretch(entity.Owner);

    private void BreakChainFrom(Entity<ChainLinkComponent> entity, bool removeJoints = false)
    {
        var segmentedEv = new ChainSegmentedEvent(entity);
        var previousUid = entity.Comp.PreviousLinkUid;
        var nextUid = entity.Comp.NextLinkUid;

        // Segment the chain backwards
        if (entity.Comp.PreviousLinkUid is { } directlyPreviousUid)
            BreakChainForwards(directlyPreviousUid, ref segmentedEv, removeJoints: removeJoints);

        // Segment the chain forwards
        if (entity.Comp.NextLinkUid is { } directlyNextUid)
            BreakChainBackwards(directlyNextUid, ref segmentedEv, removeJoints: removeJoints);

        // Update chain edges
        if (_edgeQuery.TryGetComponent(entity.Owner, out var edgeComponent) &&
            edgeComponent.LifeStage < ComponentLifeStage.Stopping)
            RemComp(entity.Owner, edgeComponent);

        BreakEdges(entity);

        // This link is out of the chain now, so its stretch joint, if it was an end, goes too
        RemoveStretch(entity);

        if (previousUid is { })
            RefreshStretch(previousUid.Value);

        if (nextUid is { })
            RefreshStretch(nextUid.Value);
    }

    /// <summary>
    ///     Updates this link for the FORWARDS adjacent link having been broken.
    ///         Assumes the forwards link exists.
    /// </summary>
    private void BreakChainForwards(EntityUid uid, ref ChainSegmentedEvent segmentedEv, bool removeJoints = false)
    {
        // Idk why this is needed but sometimes the comp doesnt exist. Or is in the wrong lifestage GEEEEEEEEEEEEEEEEEEEEEEEEEEEEEG!
        if (!_linkQuery.TryGetComponent(uid, out var previousLinkComponent) ||
            previousLinkComponent.LifeStage > ComponentLifeStage.Stopping)
            return;

        if (removeJoints && previousLinkComponent.NextLinkJointId is { })
            _jointSystem.RemoveJoint(uid, previousLinkComponent.NextLinkJointId);

        OnAdjacentLinkBroken(uid, ref segmentedEv);

        previousLinkComponent.NextLinkUid = null;
        previousLinkComponent.NextLinkJointId = null;

        DirtyField(uid, previousLinkComponent, nameof(previousLinkComponent.NextLinkUid));

        // Incase this is an edge
        if (_edgeQuery.TryGetComponent(uid, out var edgeComponent) &&
            edgeComponent.LifeStage < ComponentLifeStage.Stopping)
            RemComp(uid, edgeComponent);
    }

    /// <summary>
    ///     Updates this link for the BACKWARDS adjacent link having been broken.
    ///         Assumes the backwards link exists.
    /// </summary>
    private void BreakChainBackwards(EntityUid uid, ref ChainSegmentedEvent segmentedEv, bool removeJoints = false)
    {
        if (!_linkQuery.TryGetComponent(uid, out var nextLinkComponent))
            return;

        if (removeJoints && nextLinkComponent.PreviousLinkJointId is { })
            _jointSystem.RemoveJoint(uid, nextLinkComponent.PreviousLinkJointId);

        OnAdjacentLinkBroken(uid, ref segmentedEv);

        nextLinkComponent.PreviousLinkUid = null;
        nextLinkComponent.PreviousLinkJointId = null;

        DirtyField(uid, nextLinkComponent, nameof(nextLinkComponent.PreviousLinkUid));

        // Incase this is an edge #2
        if (_edgeQuery.TryGetComponent(uid, out var edgeComponent) &&
            edgeComponent.LifeStage < ComponentLifeStage.Stopping)
            RemComp(uid, edgeComponent);
    }

    private void BreakEdges(Entity<ChainLinkComponent> entity)
    {
        foreach (var edgeUid in entity.Comp.EdgeUids.ToArray())
        {
            if (!_edgeQuery.TryGetComponent(edgeUid, out var edgeComponent)) // sometimes edge component doesnt get its shutdown called before this, so a deleted entity might not be removed from the list of edges yet
                continue;

            // first time the chain was segmented
            if (!edgeComponent.Broken)
            {
                var brokenEv = new ChainInitiallyBrokenEvent(entity, edgeComponent);
                RaiseLocalEvent(edgeUid, ref brokenEv);

                edgeComponent.Broken = true;

                Dirty(edgeUid, edgeComponent);
            }

            edgeComponent.LinkUids.Remove(entity);
        }
    }

    private void OnAdjacentLinkBroken(EntityUid uid, ref ChainSegmentedEvent ev) => RaiseLocalEvent(uid, ref ev);

    /// <summary>
    ///     Adds a joint between two entities.
    ///         Returns the joint created.
    ///
    ///     Meant for individual chain links.
    /// </summary>
    private DistanceJoint ConnectTwoLink(EntityUid firstUid, EntityUid secondUid, Vector2 offset)
    {
        var joint = _jointSystem.CreateDistanceJoint(firstUid, secondUid, anchorA: offset, anchorB: -offset, id: _gameTiming.CurTick.ToString() + firstUid.ToString() + "chain");

        joint.CollideConnected = false;
        joint.MinLength = offset.Y * 0.95f;
        joint.Length = joint.MinLength;
        joint.MaxLength = offset.Y;

        return joint;
    }

    /// <summary>
    ///     Puts the stretch joint of the segment <paramref name="linkUid"/> is in where it should be: between the
    ///         segment's two ends if it has at least three links and neither end is in a container, and nowhere otherwise.
    ///         Takes any stretch joint the ends had off first, since a break changes which links are the ends.
    /// </summary>
    /// <remarks>
    ///     A stretch joint keeps a long chain from stretching apart link by link, which is jank: each link joint only
    ///         holds its own two links together.
    /// </remarks>
    private void RefreshStretch(EntityUid linkUid)
    {
        // Joints are networked: the server's are the only ones
        if (_netManager.IsClient ||
            !TryGetLiveLink(linkUid, out var linkComponent))
            return;

        var start = (Uid: linkUid, Comp: linkComponent);
        var end = start;
        var count = 1;

        while (start.Comp.PreviousLinkUid is { } previousUid && TryGetLiveLink(previousUid, out var previousComponent) && count <= MaxStretchWalk)
        {
            start = (previousUid, previousComponent);
            count++;
        }

        while (end.Comp.NextLinkUid is { } nextUid && TryGetLiveLink(nextUid, out var nextComponent) && count <= MaxStretchWalk)
        {
            end = (nextUid, nextComponent);
            count++;
        }

        RemoveStretch((start.Uid, start.Comp));
        RemoveStretch((end.Uid, end.Comp));

        // Two links are held together by their own joint already
        if (count < 3 ||
            TerminatingOrDeleted(start.Uid) ||
            TerminatingOrDeleted(end.Uid) ||
            _containerSystem.IsEntityInContainer(start.Uid) ||
            _containerSystem.IsEntityInContainer(end.Uid))
            return;

        // The links' own joint gives the chain's measurements
        if (start.Comp.NextLinkJointId is not { } linkJointId ||
            !_jointQuery.TryGetComponent(start.Uid, out var jointComponent) ||
            !jointComponent.GetJoints.TryGetValue(linkJointId, out var linkJoint) ||
            linkJoint is not DistanceJoint linkDistanceJoint)
            return;

        // As long as the chain can get when pulled straight: every link joint at its longest, and every link in between
        //      spanning the distance between its two anchors. Never shorter, so it never fights the link joints.
        var linkSpan = (linkDistanceJoint.LocalAnchorA - linkDistanceJoint.LocalAnchorB).Length();
        var maxLength = (count - 1) * linkDistanceJoint.MaxLength + (count - 2) * linkSpan;

        var stretchJoint = _jointSystem.CreateDistanceJoint(
            start.Uid,
            end.Uid,
            anchorA: linkDistanceJoint.LocalAnchorA,
            anchorB: linkDistanceJoint.LocalAnchorB,
            id: _gameTiming.CurTick.ToString() + start.Uid.ToString() + StretchJointSuffix);

        stretchJoint.CollideConnected = false;
        stretchJoint.MinLength = 0f;
        stretchJoint.MaxLength = maxLength;
        stretchJoint.Length = maxLength;

        start.Comp.StretchJointId = stretchJoint.ID;
        end.Comp.StretchJointId = stretchJoint.ID;
    }

    private void RemoveStretch(Entity<ChainLinkComponent> entity)
    {
        if (entity.Comp.StretchJointId is not { } stretchJointId)
            return;

        // The other end's id is cleared by OnJointRemoved
        entity.Comp.StretchJointId = null;
        _jointSystem.RemoveJoint(entity.Owner, stretchJointId);
    }

    private bool TryGetLiveLink(EntityUid uid, [NotNullWhen(true)] out ChainLinkComponent? linkComponent)
    {
        return _linkQuery.TryGetComponent(uid, out linkComponent) &&
            linkComponent.LifeStage <= ComponentLifeStage.Running;
    }

    /// <summary>
    ///     Throws if the chains length isn't at least 1.
    /// </summary>
    /// <returns>List of chain entities spawned.</returns>
    public List<EntityUid> SpawnChain(
        EntProtoId entProtoId,
        EntityCoordinates coordinates,
        int length,
        float yOffset,
        out EntityUid startUid,
        out EntityUid endUid
    )
    {
        if (length < 1)
            throw new ArgumentOutOfRangeException(nameof(length), "Chain must have at least one link!");

        // Currently client is just NO JUST NO
        if (_netManager.IsClient)
        {
            startUid = EntityUid.Invalid;
            endUid = EntityUid.Invalid;
            return [];
        }

        var offset = new Vector2(0f, yOffset);
        Entity<ChainLinkComponent>? lastEntity = null;

        var entities = new List<EntityUid>();
        var firstUid = EntityUid.Invalid;

        for (var i = 0; i < length; i++)
        {
            var linkUid = PredictedSpawnAtPosition(entProtoId, coordinates);
            entities.Add(linkUid);

            var linkComponent = AddComp<ChainLinkComponent>(linkUid);

            if (lastEntity == null)
            {
                lastEntity = (linkUid, linkComponent);

                firstUid = linkUid;
                linkComponent.EdgeUids.Add(firstUid); // as its the first entity

                continue;
            }

            var lastToHereJoint = ConnectTwoLink(lastEntity.Value, linkUid, offset);

            // form basically a linked list
            linkComponent.PreviousLinkUid = lastEntity;
            lastEntity.Value.Comp.NextLinkUid = linkUid;

            // and for joint ids
            lastEntity.Value.Comp.NextLinkJointId = lastToHereJoint.ID;
            linkComponent.PreviousLinkJointId = lastToHereJoint.ID;

            linkComponent.EdgeUids.Add(firstUid); // as its the first entity
            lastEntity = (linkUid, linkComponent);
        }

        startUid = entities[0];
        var startEdgeComponent = AddComp<ChainEdgeComponent>(startUid);
        startEdgeComponent.LinkUids = [.. entities]; // clone the list

        endUid = entities[entities.Count - 1];
        var endEdgeComponent = AddComp<ChainEdgeComponent>(endUid);
        endEdgeComponent.LinkUids = [.. entities]; // clone the list.. again

        startEdgeComponent.OtherEdgeUid = endUid;
        endEdgeComponent.OtherEdgeUid = startUid;

        // Every link answers to both edges, or breaking the chain would only ever break the start edge
        foreach (var linkUid in entities)
            _linkQuery.GetComponent(linkUid).EdgeUids.Add(endUid);

        RefreshStretch(startUid);
        return entities;
    }

    /// <summary>
    ///     This time the start and end chain entities already exist.
    /// </summary>
    /// <returns>List of chain entities spawned, and the entities that were provided.</returns>
    public List<EntityUid> SpawnChainInbetween(
        EntProtoId entProtoId,
        EntityCoordinates coordinates,
        int inbetweenLength,
        float yOffset,
        EntityUid startUid,
        EntityUid endUid
    )
    {
        // Currently client is just NO JUST NO
        if (_netManager.IsClient)
            return [];

        var offset = new Vector2(0f, yOffset);
        Entity<ChainLinkComponent> lastEntity = (startUid, AddComp<ChainLinkComponent>(startUid));
        lastEntity.Comp.EdgeUids.Add(startUid);
        lastEntity.Comp.EdgeUids.Add(endUid);

        var entities = new List<EntityUid>() { startUid, endUid };

        void LastToCurrent(EntityUid currentLinkUid, ChainLinkComponent currentLinkComponent)
        {
            var lastToHereJoint = ConnectTwoLink(lastEntity, currentLinkUid, offset);

            // form basically a linked list
            currentLinkComponent.PreviousLinkUid = lastEntity;
            lastEntity.Comp.NextLinkUid = currentLinkUid;

            // and for joint ids
            lastEntity.Comp.NextLinkJointId = lastToHereJoint.ID;
            currentLinkComponent.PreviousLinkJointId = lastToHereJoint.ID;

            currentLinkComponent.EdgeUids.Add(startUid); // as its the first entity
            currentLinkComponent.EdgeUids.Add(endUid); // as its the first entity

            lastEntity = (currentLinkUid, currentLinkComponent);
        }

        for (var i = 0; i < inbetweenLength; i++)
        {
            var linkUid = PredictedSpawnAtPosition(entProtoId, coordinates);
            entities.Add(linkUid);
            var linkComponent = AddComp<ChainLinkComponent>(linkUid);

            LastToCurrent(linkUid, linkComponent);
        }

        // finally, link the last link to the end
        LastToCurrent(endUid, AddComp<ChainLinkComponent>(endUid));

        var startEdgeComponent = AddComp<ChainEdgeComponent>(startUid);
        startEdgeComponent.LinkUids = [.. entities]; // clone the list

        var endEdgeComponent = AddComp<ChainEdgeComponent>(endUid);
        endEdgeComponent.LinkUids = [.. entities]; // clone the list.. again

        startEdgeComponent.OtherEdgeUid = endUid;
        endEdgeComponent.OtherEdgeUid = startUid;

        RefreshStretch(startUid);
        return entities;
    }

    /// <summary>
    ///     Does nothing if the start/end entities are already linked.
    /// </summary>
    /// <returns>Whether the chain was spawned or not.</returns>
    public bool TrySpawnChainInbetween(
        EntProtoId entProtoId,
        EntityCoordinates coordinates,
        int inbetweenLength,
        float yOffset,
        EntityUid startUid,
        EntityUid endUid,
        [NotNullWhen(true)] out List<EntityUid>? entities
    )
    {
        if (_linkQuery.HasComponent(startUid) ||
            _linkQuery.HasComponent(endUid))
        {
            entities = null;
            return false;
        }

        entities = SpawnChainInbetween(
            entProtoId,
            coordinates,
            inbetweenLength,
            yOffset,
            startUid,
            endUid
        );
        return true;
    }
}
