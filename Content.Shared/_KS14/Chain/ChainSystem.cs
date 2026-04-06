using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Chain;

/// <summary>
///     If you want to break a chain, remove <see cref="ChainLinkComponent"/>
///         from one of the links or delete it.
/// </summary>
public sealed class ChainSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedJointSystem _jointSystem = default!;

    private EntityQuery<ChainLinkComponent> _linkQuery;
    private EntityQuery<ChainEdgeComponent> _edgeQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChainEdgeComponent, ComponentShutdown>(OnEdgeShutdown);
        SubscribeLocalEvent<ChainLinkComponent, ComponentShutdown>(OnLinkShutdown);
        SubscribeLocalEvent<ChainLinkComponent, JointRemovedEvent>(OnJointRemoved);

        _linkQuery = GetEntityQuery<ChainLinkComponent>();
        _edgeQuery = GetEntityQuery<ChainEdgeComponent>();
    }

    /// <returns>True if this entity is linked to anything.</returns>
    public bool HasAdjacentLink(Entity<ChainLinkComponent?> linkEntity)
    {
        if (!_linkQuery.Resolve(linkEntity, ref linkEntity.Comp))
            return false;

        return linkEntity.Comp.PreviousLinkUid is { } || linkEntity.Comp.NextLinkUid is { };
    }

    /// <returns>True if the chain was segmented.</returns>
    public bool TryBreakChainFrom(Entity<ChainLinkComponent?> linkEntity, bool removeJoints = true)
    {
        if (!_linkQuery.Resolve(linkEntity, ref linkEntity.Comp))
            return false;

        BreakChainFrom(linkEntity!, removeJoints: removeJoints);
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

    private void OnEdgeShutdown(Entity<ChainEdgeComponent> entity, ref ComponentShutdown args)
    {
        foreach (var linkUid in entity.Comp.LinkUids)
        {
            var linkComponent = _linkQuery.GetComponent(linkUid);
            linkComponent.EdgeUids.Remove(entity.Owner);
        }
    }

    private void OnLinkShutdown(Entity<ChainLinkComponent> entity, ref ComponentShutdown args)
    {
        BreakChainFrom(entity, removeJoints: true);
    }

    private void OnJointRemoved(Entity<ChainLinkComponent> ourEntity, ref JointRemovedEvent args)
    {
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
            else // We are the backwards link
            {
                BreakChainBackwards(args.OtherEntity, ref segmentedEv, removeJoints: false);
                BreakChainForwards(ourEntity.Owner, ref segmentedEv, removeJoints: false);
            }

            // Update chain edges
            BreakEdges(ourEntity);
        }
    }

    private void BreakChainFrom(Entity<ChainLinkComponent> entity, bool removeJoints = false)
    {
        var segmentedEv = new ChainSegmentedEvent(entity);

        // Segment the chain backwards
        if (entity.Comp.PreviousLinkUid is { } directlyPreviousUid)
            BreakChainForwards(directlyPreviousUid, ref segmentedEv, removeJoints: removeJoints);

        // Segment the chain forwards
        if (entity.Comp.NextLinkUid is { } directlyNextUid)
            BreakChainBackwards(directlyNextUid, ref segmentedEv, removeJoints: removeJoints);

        // Update chain edges
        RemComp<ChainEdgeComponent>(entity.Owner);
        BreakEdges(entity);
    }

    private void TrySafeRemoveJoint(EntityUid uid, string jointId)
    {
        if (!TryComp<JointComponent>(uid, out var jointComponent) ||
            !jointComponent.GetJoints.ContainsKey(jointId))
            return;

        _jointSystem.RemoveJoint(uid, jointId);
    }

    /// <summary>
    ///     Updates this link for the FORWARDS adjacent link having been broken.
    ///         Assumes the forwards link exists.
    /// </summary>
    private void BreakChainForwards(EntityUid uid, ref ChainSegmentedEvent segmentedEv, bool removeJoints = false)
    {
        var previousLinkComponent = _linkQuery.GetComponent(uid);
        if (removeJoints && previousLinkComponent.NextLinkJointId is { })
            _jointSystem.RemoveJoint(uid, previousLinkComponent.NextLinkJointId);

        OnAdjacentLinkBroken(uid, ref segmentedEv);

        previousLinkComponent.NextLinkUid = null;
        previousLinkComponent.NextLinkJointId = null;

        DirtyField(uid, previousLinkComponent, nameof(previousLinkComponent.NextLinkUid));
    }

    /// <summary>
    ///     Updates this link for the BACKWARDS adjacent link having been broken.
    ///         Assumes the backwards link exists.
    /// </summary>
    private void BreakChainBackwards(EntityUid uid, ref ChainSegmentedEvent segmentedEv, bool removeJoints = false)
    {
        var nextLinkComponent = _linkQuery.GetComponent(uid);
        if (removeJoints && nextLinkComponent.PreviousLinkJointId is { })
            _jointSystem.RemoveJoint(uid, nextLinkComponent.PreviousLinkJointId);

        OnAdjacentLinkBroken(uid, ref segmentedEv);

        nextLinkComponent.PreviousLinkUid = null;
        nextLinkComponent.PreviousLinkJointId = null;

        DirtyField(uid, nextLinkComponent, nameof(nextLinkComponent.PreviousLinkUid));
    }

    private void BreakEdges(Entity<ChainLinkComponent> entity)
    {
        var brokenEv = new ChainInitiallyBrokenEvent(entity);
        foreach (var edgeUid in entity.Comp.EdgeUids)
        {
            if (!_edgeQuery.TryGetComponent(edgeUid, out var edgeComponent)) // sometimes edge component doesnt get its shutdown called before this, so a deleted entity might not be removed from the list of edges yet
                continue;

            // first time the chain was segmented
            if (!edgeComponent.Broken)
            {
                RaiseLocalEvent(edgeUid, ref brokenEv);

                edgeComponent.Broken = true;
                Dirty(edgeUid, edgeComponent);
            }

            edgeComponent.LinkUids.Remove(entity);
        }
    }

    private void OnAdjacentLinkBroken(EntityUid uid, ref ChainSegmentedEvent ev) => RaiseLocalEvent(uid, ref ev);

    /// <summary>
    ///     Adds joints between two entities.
    ///         Returns the joint created.
    /// </summary>
    public Joint ConnectTwo(EntityUid firstUid, EntityUid secondUid, Vector2 offset)
    {
        var joint = _jointSystem.CreateDistanceJoint(firstUid, secondUid, anchorA: offset, anchorB: -offset, id: _gameTiming.CurTime.ToString() + firstUid.ToString());

        joint.CollideConnected = false;
        joint.MinLength = offset.Y * 0.95f;
        joint.Length = joint.MinLength;
        joint.MaxLength = offset.Y;

        return joint;
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

            var lastToHereJoint = ConnectTwo(lastEntity.Value, linkUid, offset);

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
            var lastToHereJoint = ConnectTwo(lastEntity, currentLinkUid, offset);

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
