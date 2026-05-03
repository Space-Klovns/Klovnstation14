using Content.Shared._KS14.Klovnmed.OrganAttachmentOperation;
using Content.Shared.Body;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Klovnmed.OrganAttachmentConstruction;

/// <summary>
///     Handles <see cref="OrganAttachmentConstructionComponent"/>, and
///         networking the state of current construction node using it.
/// </summary>
public abstract class SharedOrganAttachmentConstructionSystem : EntitySystem
{
    [Dependency] private readonly BodyHierarchySystem _bodyHierarchySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OrganAttachmentConstructionComponent, OrganAttachmentGetCategoriesEvent>(OnGetCategories);
        SubscribeLocalEvent<OrganAttachmentConstructionComponent, CanAttachOrganEvent>(OnCanAttachOrgan);
    }

    private void OnGetCategories(Entity<OrganAttachmentConstructionComponent> entity, ref OrganAttachmentGetCategoriesEvent args)
    {
        foreach (var item in entity.Comp.AlwaysAttachable)
            args.Add(item.Key);
    }

    private void OnCanAttachOrgan(Entity<OrganAttachmentConstructionComponent> entity, ref CanAttachOrganEvent args)
    {
        if (args.Cancelled ||
            entity.Comp.AlwaysAttachable.ContainsKey(args.Category))
            return;

        if (entity.Comp.BaseOrgansAlwaysAttachable)
        {
            // Base organs are always allowed
            if (args.Component is { } &&
                args.Component.BaseOrganCategories.Contains(args.Category))
                return;
        }

        if (entity.Comp.OrgansAlwaysAttachableWhenInBody &&
            _bodyHierarchySystem.TryGetBody(entity.Owner, out _))
            return;

        if (entity.Comp.NetNode is not { } currentNode)
            goto cancelled;

        if (entity.Comp.NodeMap.TryGetValue(currentNode, out var categoryWhitelist))
        {
            // if whitelsit is null then all categories are allowed
            if (categoryWhitelist is not { })
                return;

            if (!categoryWhitelist.Contains(args.Category))
                goto cancelled;

            return;
        }

    cancelled:
        args.Cancelled = true;
        return;
    }

    public void AddAttachableCategory(Entity<OrganAttachmentConstructionComponent?> entity, ProtoId<OrganCategoryPrototype> category)
    {
        if (!Resolve(entity, ref entity.Comp))
            return;

        // TODO LCDC COLLECTIONSMARSHAL: COLLECTIONSMARSHAL PLEASE SAVE US ONE DAY

        var count = entity.Comp.AlwaysAttachable.GetOrNew(category);
        count += 1;

        entity.Comp.AlwaysAttachable[category] = count;
        DirtyField(entity, entity.Comp, nameof(entity.Comp.AlwaysAttachable));
    }

    public void RemoveAttachableCategory(Entity<OrganAttachmentConstructionComponent?> entity, ProtoId<OrganCategoryPrototype> category)
    {
        if (!Resolve(entity, ref entity.Comp) ||
            !entity.Comp.AlwaysAttachable.TryGetValue(category, out var count))
            return;

        count -= 1;
        if (count == 0)
            entity.Comp.AlwaysAttachable.Remove(category);
        else
            entity.Comp.AlwaysAttachable[category] = count;

        DirtyField(entity, entity.Comp, nameof(entity.Comp.AlwaysAttachable));
    }
}
