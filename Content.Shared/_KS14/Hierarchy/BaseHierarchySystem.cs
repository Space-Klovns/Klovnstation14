using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Containers;

namespace Content.Shared._KS14.Hierarchy;

// TODO: ughh optimise this

public abstract class BaseHierarchySystem<THierarchyComp, TElementComp> : EntitySystem
    where THierarchyComp : Component, IHierarchyComponent
    where TElementComp : Component, IHierarchyElementComponent
{
    [Dependency] protected readonly SharedContainerSystem ContainerSystem = default!;

    /// <summary>
    ///     Should be set on the system. Internal container id value will not update
    ///         after this is set.
    /// </summary>
    public abstract string ContainerId { get; }
    private string _containerId = default!;

    protected EntityQuery<THierarchyComp> HierarchyQuery;
    protected EntityQuery<TElementComp> ElementQuery;

    public override void Initialize()
    {
        base.Initialize();

        _containerId = ContainerId;

        HierarchyQuery = GetEntityQuery<THierarchyComp>();
        ElementQuery = GetEntityQuery<TElementComp>();

        SubscribeLocalEvent<THierarchyComp, ComponentAdd>(OnHierarchyAdd);
        SubscribeLocalEvent<TElementComp, ComponentAdd>(OnElementAdd);

        SubscribeLocalEvent<THierarchyComp, ComponentInit>(OnHierarchyInit);
        SubscribeLocalEvent<TElementComp, ComponentInit>(OnElementInit);

        SubscribeLocalEvent<THierarchyComp, EntityTerminatingEvent>(OnHierarchyTerminating);
        SubscribeLocalEvent<THierarchyComp, ComponentShutdown>(OnHierarchyShutdown);
        SubscribeLocalEvent<TElementComp, EntityTerminatingEvent>(OnElementTerminating);

        SubscribeLocalEvent<THierarchyComp, EntRemovedFromContainerMessage>(OnElementRemovedFromHierarchy);
        SubscribeLocalEvent<TElementComp, EntRemovedFromContainerMessage>(OnElementRemovedFromElement);
        SubscribeLocalEvent<TElementComp, EntGotInsertedIntoContainerMessage>(OnElementGotInsertedIntoContainer);

        SubscribeLocalEvent<TElementComp, ComponentShutdown>(OnElementShutdown);
    }

    public bool TryGetHierarchyEntityOfElement(Entity<TElementComp?> elementEntity, [NotNullWhen(true)] out Entity<THierarchyComp>? hierarchyEntity)
    {
        if (!ElementQuery.Resolve(elementEntity, ref elementEntity.Comp) ||
            elementEntity.Comp.HierarchyUid is not { } hierarchyUid)
        {
            hierarchyEntity = null;
            return false;
        }

        hierarchyEntity = (hierarchyUid, HierarchyQuery.GetComponent(hierarchyUid));
        return true;
    }

    private void UpdateElementChildrenNewHierarchy(Entity<TElementComp> elementEntity, EntityUid? newHierarchyUid)
    {
        if (elementEntity.Comp.HierarchyUid is { } oldHierarchyUid)
            RemoveElementFromHierarchy((oldHierarchyUid, HierarchyQuery.GetComponent(oldHierarchyUid)), elementEntity);

        Entity<THierarchyComp>? newHierarchyEntity = newHierarchyUid == null ? null : (newHierarchyUid.Value, HierarchyQuery.GetComponent(newHierarchyUid.Value));
        if (newHierarchyEntity is { })
            AddElementToHierarchy(newHierarchyEntity.Value, elementEntity);

        elementEntity.Comp.HierarchyUid = newHierarchyUid;
        foreach (var childUid in elementEntity.Comp.ChildUids)
            RecursivelyUpdateDescendants(
                (childUid, ElementQuery.GetComponent(childUid)),
                newHierarchyEntity
            );
    }

    private void OnElementRemovedFromHierarchy(Entity<THierarchyComp> parentHierarchyEntity, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != _containerId)
            return;

        UpdateElementChildrenNewHierarchy((args.Entity, ElementQuery.GetComponent(args.Entity)), null);
    }

    private void OnElementRemovedFromElement(Entity<TElementComp> parentElementEntity, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != _containerId)
            return;

        RemoveDirectChild(parentElementEntity, args.Entity);

        if (parentElementEntity.Comp.HierarchyUid != null)
            UpdateElementChildrenNewHierarchy((args.Entity, ElementQuery.GetComponent(args.Entity)), null);
    }

    private void OnElementGotInsertedIntoContainer(Entity<TElementComp> elementEntity, ref EntGotInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != _containerId)
            return;

        elementEntity.Comp.Container = ContainerSystem.EnsureContainer<Container>(elementEntity.Owner, _containerId);
        var newParentUid = args.Container.Owner;
        if (HierarchyQuery.HasComponent(newParentUid)) // use new hierarchy parent as hierarchy
        {
            if (newParentUid == elementEntity.Comp.HierarchyUid)
                return;

            UpdateElementChildrenNewHierarchy(elementEntity, newParentUid);
        }
        else if (ElementQuery.TryGetComponent(newParentUid, out var newElementParentComponent)) // use hierarchy of new element parent
        {
            if (newParentUid == newElementParentComponent.HierarchyUid)
                return;

            UpdateElementChildrenNewHierarchy(elementEntity, newElementParentComponent.HierarchyUid);
            AddDirectChild((newParentUid, newElementParentComponent), elementEntity);
        }
        else
            throw new InvalidOperationException("Hierarchy element got into some bullshit container with valid container id but neither a hierarchy nor element component and we are just bailing");
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

    private void OnHierarchyInit(Entity<THierarchyComp> hierarchyEntity, ref ComponentInit args)
    {
        hierarchyEntity.Comp.Container = ContainerSystem.EnsureContainer<Container>(hierarchyEntity.Owner, _containerId);
    }

    private void OnElementInit(Entity<TElementComp> elementEntity, ref ComponentInit args)
    {
        elementEntity.Comp.Container = ContainerSystem.EnsureContainer<Container>(elementEntity.Owner, _containerId);
    }

    private void OnElementShutdown(Entity<TElementComp> elementEntity, ref ComponentShutdown args)
    {
        if (elementEntity.Comp.HierarchyUid is not { } hierarchyUid)
            return;

        HierarchyQuery.GetComponent(hierarchyUid).RecursiveChildUids.Remove(elementEntity);

        // Removal from any parent element (if present) is handled by containers and whatnot
    }

    protected virtual void OnHierarchyTerminating(Entity<THierarchyComp> hierarchyEntity, ref EntityTerminatingEvent args)
    {
        // IIRC, this is to prevent a tree-update spam as each of the entity's children get detached to nullspace.
        hierarchyEntity.Comp.RecursiveChildUids.Clear();
    }

    protected virtual void OnElementTerminating(Entity<TElementComp> elementEntity, ref EntityTerminatingEvent args)
    {
        if (elementEntity.Comp.HierarchyUid is { } hierarchyUid &&
            HierarchyQuery.TryGetComponent(hierarchyUid, out var hierarchyComponent)) // because comp may be deleted
        {
            if (TerminatingOrDeleted(hierarchyUid))
            {
                // skip the rest of RemoveElementFromHierarchy
                hierarchyComponent.RecursiveChildUids.Remove(elementEntity);
                elementEntity.Comp.HierarchyUid = null;
            }
            else // Raise events
                RemoveElementFromHierarchy((hierarchyUid, hierarchyComponent), elementEntity);
        }

        // Same here but raise events if necessary too
        elementEntity.Comp.ChildUids.Clear();

        // We dont need to do recursive BS here because every child entity is going to be terminated too if the parent is getting terminated
    }

    private void OnHierarchyShutdown(Entity<THierarchyComp> hierarchyEntity, ref ComponentShutdown args)
    {
        for (var i = hierarchyEntity.Comp.RecursiveChildUids.Count - 1; i > -1; i--)
        {
            var childUid = hierarchyEntity.Comp.RecursiveChildUids[i];
            if (!ElementQuery.TryGetComponent(childUid, out var elementComponent))
            {
                hierarchyEntity.Comp.RecursiveChildUids.RemoveAt(i);
                continue;
            }

            elementComponent.HierarchyUid = null;
            RemoveElementFromHierarchy(hierarchyEntity, (childUid, elementComponent));
        }
    }

    /// <summary>
    ///     Called when the entitys <see cref="THierarchyComp.RecursiveChildUids"/> was updated.
    ///         Not called when state is being applied.
    /// </summary>
    protected virtual void UpdateHierarchyEntityState(Entity<THierarchyComp> entity) { }

    /// <summary>
    ///     Called when the entitys <see cref="TElementComp.ChildUids"/> was updated.
    ///         Not called when state is being applied.
    /// </summary>
    protected virtual void UpdateElementEntityChildren(Entity<TElementComp> entity) { }

    /// <summary>
    ///     Called when the entitys <see cref="TElementComp.HierarchyUid"/> was updated.
    ///         Not called when state is being applied.
    /// </summary>
    protected virtual void UpdateElementEntityHierarchy(Entity<TElementComp> entity) { }

    [MustCallBase(true)]
    protected virtual void AddElementToHierarchy(Entity<THierarchyComp> hierarchyEntity, Entity<TElementComp> addedEntity)
    {
        if (hierarchyEntity.Comp.RecursiveChildUids.Contains(addedEntity))
        {
            //DebugTools.Assert($"Element entity {ToPrettyString(addedEntity.Owner)} already contained in hierarchy {ToPrettyString(hierarchyEntity.Owner)}!");
            Log.Error($"Element entity {ToPrettyString(addedEntity.Owner)} already contained in element {ToPrettyString(hierarchyEntity.Owner)}!");
            return;
        }

        hierarchyEntity.Comp.RecursiveChildUids.Add(addedEntity);

        var addedEv = new HierarchyElementAddedEvent<TElementComp>(addedEntity);
        RaiseLocalEvent(hierarchyEntity, ref addedEv);

        UpdateHierarchyEntityState(hierarchyEntity);
    }

    [MustCallBase(true)]
    protected virtual void RemoveElementFromHierarchy(Entity<THierarchyComp> hierarchyEntity, Entity<TElementComp> removedEntity)
    {
        hierarchyEntity.Comp.RecursiveChildUids.Remove(removedEntity);

        var removedEv = new HierarchyElementRemovedEvent<TElementComp>(removedEntity);
        RaiseLocalEvent(hierarchyEntity, ref removedEv);

        UpdateHierarchyEntityState(hierarchyEntity);
    }

    [MustCallBase(true)]
    protected virtual void AddDirectChild(Entity<TElementComp> elementEntity, EntityUid childUid)
    {
        if (elementEntity.Comp.ChildUids.Contains(childUid))
        {
            //DebugTools.Assert($"Element entity {ToPrettyString(childUid)} already contained in element {ToPrettyString(elementEntity.Owner)}!");
            Log.Error($"Element entity {ToPrettyString(childUid)} already contained in element {ToPrettyString(elementEntity.Owner)}!");
            return;
        }

        elementEntity.Comp.ChildUids.Add(childUid);
        UpdateElementEntityChildren(elementEntity);
    }

    [MustCallBase(true)]
    protected virtual void RemoveDirectChild(Entity<TElementComp> elementEntity, EntityUid childUid)
    {
        elementEntity.Comp.ChildUids.Remove(childUid);
        UpdateElementEntityChildren(elementEntity);
    }

    /// <summary>
    ///     Recursively sets new tree of the descendants of this.
    ///         Assumes the first thing this is called on is the first descendant, not the actual
    ///         parent of the descendants.
    /// </summary>
    protected virtual void RecursivelyUpdateDescendants(Entity<TElementComp> elementEntity, Entity<THierarchyComp>? newHierarchyEntity)
    {
        if (elementEntity.Comp.HierarchyUid is { } oldHierarchyUid)
            RemoveElementFromHierarchy((oldHierarchyUid, HierarchyQuery.GetComponent(oldHierarchyUid)), elementEntity);

        if (newHierarchyEntity is { })
            AddElementToHierarchy(newHierarchyEntity.Value, elementEntity);

        elementEntity.Comp.HierarchyUid = newHierarchyEntity;
        UpdateElementEntityHierarchy(elementEntity);

        foreach (var childUid in elementEntity.Comp.ChildUids)
            RecursivelyUpdateDescendants((childUid, ElementQuery.GetComponent(childUid)), newHierarchyEntity);
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
