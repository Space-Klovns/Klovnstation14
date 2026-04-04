using Content.Shared._KS14.Hierarchy;
using Content.Shared.Body;
using Robust.Shared.Containers;

namespace Content.Shared._KS14.Klovnmed;

public sealed class BodyHierarchySystem : BaseHierarchySystem<BodyComponent, OrganComponent>
{
    public const string ConstContainerId = "body_organs";

    public override string ContainerId => ConstContainerId; // for compatibility
    public override bool Replicated => true;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OrganComponent, ContainerIsRemovingAttemptEvent>(OnOrganElementRemovingAttempt);
    }

    private void OnOrganElementRemovingAttempt(Entity<OrganComponent> entity, ref ContainerIsRemovingAttemptEvent args)
    {
        if (args.Container.ID != ContainerId)
            return;

        // I just want contents to be visible not removable. Still interactable and whatnot doe
        args.Cancel();
    }

    protected override void AddElementToHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> addedEntity)
    {
        base.AddElementToHierarchy(hierarchyEntity, addedEntity);

        var body = new OrganInsertedIntoEvent(addedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotInsertedEvent(hierarchyEntity);
        RaiseLocalEvent(addedEntity, ref ev);

        addedEntity.Comp.Container.ShowContents = true;
    }

    protected override void RemoveElementFromHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> removedEntity)
    {
        base.RemoveElementFromHierarchy(hierarchyEntity, removedEntity);

        var body = new OrganRemovedFromEvent(removedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotRemovedEvent(hierarchyEntity);
        RaiseLocalEvent(removedEntity, ref ev);

        removedEntity.Comp.Container.ShowContents = false;
    }

    protected override void AddDirectChild(Entity<OrganComponent> elementEntity, EntityUid childUid)
    {
        base.AddDirectChild(elementEntity, childUid);
    }

    protected override void RemoveDirectChild(Entity<OrganComponent> elementEntity, EntityUid childUid)
    {
        base.RemoveDirectChild(elementEntity, childUid);
    }

    protected override void RecursivelyUpdateDescendants(Entity<OrganComponent> elementEntity, Entity<BodyComponent>? newHierarchyEntity)
    {
        base.RecursivelyUpdateDescendants(elementEntity, newHierarchyEntity);
    }
}
