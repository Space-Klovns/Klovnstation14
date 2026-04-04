using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Hierarchy;

// TODO: ughh optimise this
// I like how it works though im very proud of this goidacode

public abstract class BaseHierarchySystem<THierarchyComp, TElementComp> : EntitySystem
    where THierarchyComp : Component, IHierarchyComponent
    where TElementComp : Component, IHierarchyElementComponent
{
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    public abstract string ContainerId { get; }
    private EntityQuery<THierarchyComp> _hierarchyQuery;
    private EntityQuery<TElementComp> _elementQuery;

    public override void Initialize()
    {
        base.Initialize();

        _hierarchyQuery = GetEntityQuery<THierarchyComp>();
        _elementQuery = GetEntityQuery<TElementComp>();

        // Networking

        // The rest

        SubscribeLocalEvent<THierarchyComp, ComponentInit>(OnHierarchyInit);
        SubscribeLocalEvent<TElementComp, ComponentInit>(OnElementInit);

        SubscribeLocalEvent<THierarchyComp, ComponentAdd>(OnHierarchyAdd);
        SubscribeLocalEvent<TElementComp, ComponentAdd>(OnElementAdd);

        SubscribeLocalEvent<THierarchyComp, EntityTerminatingEvent>(OnHierarchyTerminating);
        SubscribeLocalEvent<THierarchyComp, ComponentShutdown>(OnHierarchyShutdown);

        SubscribeLocalEvent<TElementComp, EntInsertedIntoContainerMessage>(OnEntInsertedIntoElementMessage);
        SubscribeLocalEvent<TElementComp, EntRemovedFromContainerMessage>(OnEntRemovedFromElementMessage);
        SubscribeLocalEvent<TElementComp, EntParentChangedMessage>(OnElementParentChanged);

        SubscribeLocalEvent<TElementComp, ComponentShutdown>(OnElementShutdown);
    }

    private void OnGetState(Entity<TElementComp> entity, ref ComponentGetState args)
    {

    }

    private void UpdateElementChildrenNewHierarchy(Entity<TElementComp> elementEntity, EntityUid? newHierarchyUid)
    {
        if (elementEntity.Comp.HierarchyUid is { } oldHierarchyUid)
            RemoveElementFromHierarchy((oldHierarchyUid, _hierarchyQuery.GetComponent(oldHierarchyUid)), elementEntity);

        Entity<THierarchyComp>? newHierarchyEntity = newHierarchyUid == null ? null : (newHierarchyUid.Value, _hierarchyQuery.GetComponent(newHierarchyUid.Value));
        if (newHierarchyEntity is { })
            AddElementToHierarchy(newHierarchyEntity.Value, elementEntity);

        elementEntity.Comp.HierarchyUid = newHierarchyUid;
        foreach (var childUid in elementEntity.Comp.ChildUids)
            RecursivelyUpdateDescendants(
                (childUid, _elementQuery.GetComponent(childUid)),
                newHierarchyEntity
            );
    }

    [MustCallBase(true)]
    protected virtual void OnElementParentChanged(Entity<TElementComp> elementEntity, ref EntParentChangedMessage args)
    {
        var newParentUid = args.Transform.ParentUid;
        if (!_containerSystem.HasContainer(newParentUid, ContainerId, containerManager: null))
        {
            if (elementEntity.Comp.HierarchyUid != null)
                UpdateElementChildrenNewHierarchy(elementEntity, null);

            return;
        }

        if (_hierarchyQuery.HasComponent(newParentUid)) // use new hierarchy parent as hierarchy
        {
            if (newParentUid == elementEntity.Comp.HierarchyUid)
                return;

            UpdateElementChildrenNewHierarchy(elementEntity, newParentUid);
        }
        else if (_elementQuery.TryGetComponent(newParentUid, out var newElementParentComponent)) // use hierarchy of new element parent
        {
            if (newParentUid == newElementParentComponent.HierarchyUid)
                return;

            UpdateElementChildrenNewHierarchy(elementEntity, newElementParentComponent.HierarchyUid);
        }
    }

    private void OnEntInsertedIntoElementMessage(Entity<TElementComp> newContainerEntity, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ContainerId ||
            !_elementQuery.HasComponent(args.Entity))
            return;

        AddDirectChild(newContainerEntity, args.Entity);
    }

    private void OnEntRemovedFromElementMessage(Entity<TElementComp> oldContainerEntity, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ContainerId ||
            !_elementQuery.HasComponent(args.Entity))
            return;

        RemoveDirectChild(oldContainerEntity, args.Entity);
    }

    private void OnHierarchyInit(Entity<THierarchyComp> hierarchyEntity, ref ComponentInit args)
    {
        hierarchyEntity.Comp.Container = _containerSystem.EnsureContainer<Container>(hierarchyEntity.Owner, ContainerId);
    }

    private void OnElementInit(Entity<TElementComp> elementEntity, ref ComponentInit args)
    {
        elementEntity.Comp.Container = _containerSystem.EnsureContainer<Container>(elementEntity.Owner, ContainerId);
    }

    private void OnHierarchyAdd(Entity<THierarchyComp> hierarchyEntity, ref ComponentAdd args)
    {
        hierarchyEntity.Comp.RecursiveChildUids = [];
    }

    private void OnElementAdd(Entity<TElementComp> elementEntity, ref ComponentAdd args)
    {
        elementEntity.Comp.HierarchyUid = null;
        elementEntity.Comp.ChildUids = [];
    }

    private void OnElementShutdown(Entity<TElementComp> elementEntity, ref ComponentShutdown args)
    {
        if (elementEntity.Comp.HierarchyUid is not { } hierarchyUid)
            return;

        _hierarchyQuery.GetComponent(hierarchyUid).RecursiveChildUids.Remove(elementEntity);

        // Removal from any parent element (if present) is handled by containers and whatnot
    }

    protected virtual void OnHierarchyTerminating(Entity<THierarchyComp> hierarchyEntity, ref EntityTerminatingEvent args)
    {
        // IIRC, this is to prevent a tree-update spam as each of the entity's children get detached to nullspace.
        RemComp(hierarchyEntity, hierarchyEntity.Comp);
    }

    private void OnHierarchyShutdown(Entity<THierarchyComp> hierarchyEntity, ref ComponentShutdown args)
    {
        for (var i = hierarchyEntity.Comp.RecursiveChildUids.Count - 1; i > -1; i--)
        {
            var childUid = hierarchyEntity.Comp.RecursiveChildUids[i];
            if (!_elementQuery.TryGetComponent(childUid, out var elementComponent))
            {
                hierarchyEntity.Comp.RecursiveChildUids.RemoveAt(i);
                continue;
            }

            elementComponent.HierarchyUid = null;
            RemoveElementFromHierarchy(hierarchyEntity, (childUid, elementComponent));
        }
    }

    [MustCallBase(true)]
    protected virtual void AddElementToHierarchy(Entity<THierarchyComp> hierarchyEntity, Entity<TElementComp> addedEntity)
    {
        if (hierarchyEntity.Comp.RecursiveChildUids.Contains(addedEntity))
        {
            DebugTools.Assert($"Element entity {ToPrettyString(addedEntity.Owner)} already contained in hierarchy {ToPrettyString(hierarchyEntity.Owner)}!");
            return;
        }

        hierarchyEntity.Comp.RecursiveChildUids.Add(addedEntity);

        var addedEv = new HierarchyElementAddedEvent<TElementComp>(addedEntity);
        RaiseLocalEvent(hierarchyEntity, ref addedEv);
    }

    [MustCallBase(true)]
    protected virtual void RemoveElementFromHierarchy(Entity<THierarchyComp> hierarchyEntity, Entity<TElementComp> removedEntity)
    {
        hierarchyEntity.Comp.RecursiveChildUids.Remove(removedEntity);

        var addedEv = new HierarchyElementRemovedEvent<TElementComp>(removedEntity);
        RaiseLocalEvent(hierarchyEntity, ref addedEv);
    }

    [MustCallBase(true)]
    protected virtual void AddDirectChild(Entity<TElementComp> elementEntity, EntityUid childUid)
    {
        if (elementEntity.Comp.ChildUids.Contains(childUid))
        {
            DebugTools.Assert($"Element entity {ToPrettyString(childUid)} already contained in element {ToPrettyString(elementEntity.Owner)}!");
            return;
        }

        elementEntity.Comp.ChildUids.Add(childUid);
    }

    [MustCallBase(true)]
    protected virtual void RemoveDirectChild(Entity<TElementComp> elementEntity, EntityUid childUid)
    {
        elementEntity.Comp.ChildUids.Remove(childUid);
    }

    /// <summary>
    ///     Recursively sets new tree of the descendants of this.
    ///         Assumes the first thing this is called on is the first descendant, not the actual
    ///         parent of the descendants.
    /// </summary>
    protected virtual void RecursivelyUpdateDescendants(Entity<TElementComp> elementEntity, Entity<THierarchyComp>? newHierarchyEntity)
    {
        if (elementEntity.Comp.HierarchyUid is { } oldHierarchyUid)
            RemoveElementFromHierarchy((oldHierarchyUid, _hierarchyQuery.GetComponent(oldHierarchyUid)), elementEntity);

        if (newHierarchyEntity is { })
            AddElementToHierarchy(newHierarchyEntity.Value, elementEntity);

        elementEntity.Comp.HierarchyUid = newHierarchyEntity;

        foreach (var childUid in elementEntity.Comp.ChildUids)
            RecursivelyUpdateDescendants((childUid, _elementQuery.GetComponent(childUid)), newHierarchyEntity);
    }
}

/// <summary>
///     Raised on a hierarchy when an element was added in any part of it.
/// </summary>
[ByRefEvent]
public record struct HierarchyElementAddedEvent<TElementComp>(Entity<TElementComp> AddedEntity) where TElementComp : Component, IHierarchyElementComponent;

/// <summary>
///     Raised on a hierarchy when an element was removed from any part of it.
/// </summary>
[ByRefEvent]
public record struct HierarchyElementRemovedEvent<TElementComp>(Entity<TElementComp> RemovedEntity) where TElementComp : Component, IHierarchyElementComponent;

[Serializable, NetSerializable]
public sealed class HierarchyComponentState<THierarchyComp> : ComponentState where THierarchyComp : Component, IHierarchyComponent
{
    public List<NetEntity>? RecursiveEntities = null;
}

[Serializable, NetSerializable]
public sealed class HierarchyElementComponentState<TElementComp> : ComponentState where TElementComp : Component, IHierarchyElementComponent
{
    public NetEntity? HierarchyEntity = null;
    public List<NetEntity>? ChildEntities = null;
}
