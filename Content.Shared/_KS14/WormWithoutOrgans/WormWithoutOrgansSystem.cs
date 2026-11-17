using Content.Shared.Body;
using Content.Shared.Movement.Components;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.WormWithoutOrgans;

public sealed partial class WormWithoutOrgansSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

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

    /// <summary>
    ///     Whether gaining or losing this organ can change <see cref="HasAllRequiredOrgans"/>.
    /// </summary>
    private static bool IsRequiredOrgan(Entity<WormWithoutOrgansComponent> entity, OrganComponent organComponent)
        => organComponent.Category is { } category && entity.Comp.Categories.Contains(category);

    [SubscribeLocalEvent]
    private void OnOrganInserted(Entity<WormWithoutOrgansComponent> entity, ref OrganInsertedIntoEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (!IsRequiredOrgan(entity, args.OrganComponent) ||
            !HasComp<WormComponent>(entity) ||
            !HasAllRequiredOrgans(entity, args.BodyComponent))
            return;

        RemComp<WormComponent>(entity);
    }

    [SubscribeLocalEvent]
    private void OnOrganRemoved(Entity<WormWithoutOrgansComponent> entity, ref OrganRemovedFromEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (!IsRequiredOrgan(entity, args.OrganComponent) ||
            HasComp<WormComponent>(entity) ||
            HasAllRequiredOrgans(entity, args.BodyComponent))
            return;

        AddComp<WormComponent>(entity);
    }
}
