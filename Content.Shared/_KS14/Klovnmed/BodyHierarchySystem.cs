using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Hierarchy;
using Content.Shared.Body;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Klovnmed;

public sealed class BodyHierarchySystem : BaseHierarchySystem<BodyComponent, OrganComponent>
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public const string ConstContainerId = "body_organs"; // for compatibility

    public override void Initialize()
    {
        base.Initialize();
        ContainerId = ConstContainerId;

        SubscribeLocalEvent<OrganComponent, ContainerIsRemovingAttemptEvent>(OnOrganElementRemovingAttempt);
    }

    /// <returns>True if the entity was found.</returns>
    public bool TryGetOrgan(Entity<BodyComponent?> entity, ProtoId<OrganCategoryPrototype> category, [NotNullWhen(true)] out Entity<OrganComponent>? organEntity)
    {
        if (!HierarchyQuery.Resolve(entity, ref entity.Comp, logMissing: false) ||
            !entity.Comp.PresentOrganCategories.TryGetValue(category, out var foundOrganEntity))
        {
            organEntity = null;
            return false;
        }

        organEntity = foundOrganEntity;
        return true;
    }

    /// <returns>True if the body was found.</returns>
    public bool TryGetBody(Entity<OrganComponent?> entity, [NotNullWhen(true)] out Entity<BodyComponent>? bodyEntity)
    {
        if (!ElementQuery.Resolve(entity, ref entity.Comp, logMissing: false) ||
            entity.Comp.HierarchyUid is not { } bodyUid)
        {
            bodyEntity = null;
            return false;
        }

        bodyEntity = (bodyUid, HierarchyQuery.GetComponent(bodyUid));
        return true;
    }

    private void OnOrganElementRemovingAttempt(Entity<OrganComponent> entity, ref ContainerIsRemovingAttemptEvent args)
    {
        if (args.Container.ID != ConstContainerId)
            return;

        if (_gameTiming.ApplyingState)
            return;

        // i just want contents to be visible not removable. Still interactable and whatnot doe
        args.Cancel();
    }

    protected override void AddElementToHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> addedEntity)
    {
        base.AddElementToHierarchy(hierarchyEntity, addedEntity);

        if (addedEntity.Comp.Category is { } addedCategory)
        {
            if (hierarchyEntity.Comp.PresentOrganCategories.ContainsKey(addedCategory))
            {
                DebugTools.Assert($"Organ category {addedCategory.Id} is already present in entity {ToPrettyString(hierarchyEntity)}! Added organ: {ToPrettyString(addedEntity)}");
                Log.Error($"Organ category {addedCategory.Id} is already present in entity {ToPrettyString(hierarchyEntity)}! Added organ: {ToPrettyString(addedEntity)}");
            }

            hierarchyEntity.Comp.PresentOrganCategories[addedCategory] = addedEntity;
        }

        var body = new OrganInsertedIntoEvent(addedEntity, hierarchyEntity, addedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotInsertedEvent(hierarchyEntity, hierarchyEntity, addedEntity);
        RaiseLocalEvent(addedEntity, ref ev);
    }

    protected override void RemoveElementFromHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> removedEntity)
    {
        base.RemoveElementFromHierarchy(hierarchyEntity, removedEntity);

        // lets just make the jolly assumption that an organs category wont change for no reason while its inside
        if (removedEntity.Comp.Category is { } removedCategory)
            hierarchyEntity.Comp.PresentOrganCategories.Remove(removedCategory);

        var body = new OrganRemovedFromEvent(removedEntity, hierarchyEntity, removedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotRemovedEvent(hierarchyEntity, hierarchyEntity, removedEntity);
        RaiseLocalEvent(removedEntity, ref ev);
    }
}
