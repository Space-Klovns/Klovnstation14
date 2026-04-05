using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Hierarchy;
using Content.Shared.Body;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Klovnmed;

public sealed class BodyHierarchySystem : BaseHierarchySystem<BodyComponent, OrganComponent>
{
    public const string ConstContainerId = "body_organs"; // for compatibility
    public override string ContainerId => ConstContainerId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OrganComponent, ContainerIsRemovingAttemptEvent>(OnOrganElementRemovingAttempt);
    }

    public bool TryGetOrgan(Entity<BodyComponent?> entity, ProtoId<OrganCategoryPrototype> category, [NotNullWhen(true)] out EntityUid? organUid)
    {
        if (!HierarchyQuery.Resolve(entity, ref entity.Comp))
        {
            organUid = null;
            return false;
        }

        foreach (var childUid in entity.Comp.RecursiveChildUids)
        {
            var organComponent = ElementQuery.GetComponent(childUid);
            if (organComponent.Category is not { } childCategory ||
                childCategory != category)
                continue;

            organUid = childUid;
            return true;
        }

        organUid = null;
        return false;
    }

    private void OnOrganElementRemovingAttempt(Entity<OrganComponent> entity, ref ContainerIsRemovingAttemptEvent args)
    {
        if (args.Container.ID != ContainerId)
            return;

        // i just want contents to be visible not removable. Still interactable and whatnot doe
        args.Cancel();
    }

    protected override void AddElementToHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> addedEntity)
    {
        base.AddElementToHierarchy(hierarchyEntity, addedEntity);

        if (addedEntity.Comp.Category is { } addedCategory)
        {
            hierarchyEntity.Comp.PresentOrganCategories[addedCategory] =
                hierarchyEntity.Comp.PresentOrganCategories.TryGetValue(addedCategory, out var count) ?
                    count + 1 :
                    1;
        }

        var body = new OrganInsertedIntoEvent(addedEntity, hierarchyEntity, addedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotInsertedEvent(hierarchyEntity, hierarchyEntity, addedEntity);
        RaiseLocalEvent(addedEntity, ref ev);

        addedEntity.Comp.Container.ShowContents = true;
    }

    protected override void RemoveElementFromHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> removedEntity)
    {
        base.RemoveElementFromHierarchy(hierarchyEntity, removedEntity);

        // lets just make the jolly assumption that an organs category wont change for no reason while its inside
        if (removedEntity.Comp.Category is { } removedCategory)
        {
            var newCount = hierarchyEntity.Comp.PresentOrganCategories[removedCategory] - 1;
            if (newCount == 0)
                hierarchyEntity.Comp.PresentOrganCategories.Remove(removedCategory);
            else
                hierarchyEntity.Comp.PresentOrganCategories[removedCategory] = newCount;
        }

        var body = new OrganRemovedFromEvent(removedEntity, hierarchyEntity, removedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotRemovedEvent(hierarchyEntity, hierarchyEntity, removedEntity);
        RaiseLocalEvent(removedEntity, ref ev);

        removedEntity.Comp.Container.ShowContents = false;
    }
}
