using Content.Shared.Body;
using Content.Shared.Movement.Components;

namespace Content.Shared._KS14.WormWithoutOrgans;

// TODO LCDC: optimise too somehow

public sealed class WormWithoutOrgansSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WormWithoutOrgansComponent, OrganInsertedIntoEvent>(OnOrganInserted);
        SubscribeLocalEvent<WormWithoutOrgansComponent, OrganRemovedFromEvent>(OnOrganRemoved);
    }

    private static bool HasAllRequiredOrgans(Entity<WormWithoutOrgansComponent> entity, BodyComponent bodyComponent)
    {
        foreach (var requiredCategory in entity.Comp.Categories)
        {
            if (bodyComponent.PresentOrganCategories.ContainsKey(requiredCategory))
                continue;

            return false;
        }

        return true;
    }

    private void OnOrganInserted(Entity<WormWithoutOrgansComponent> entity, ref OrganInsertedIntoEvent args)
    {
        if (!HasAllRequiredOrgans(entity, args.BodyComponent))
            return;

        RemComp<WormComponent>(entity);
    }

    private void OnOrganRemoved(Entity<WormWithoutOrgansComponent> entity, ref OrganRemovedFromEvent args)
    {
        if (HasComp<WormComponent>(entity) ||
            HasAllRequiredOrgans(entity, args.BodyComponent))
            return;

        AddComp<WormComponent>(entity);
    }
}
