using Content.Shared.Body;
using Content.Shared.Eye.Blinding.Systems;

namespace Content.Shared._KS14.Klovnmed.RequiresOrganToSee;

public sealed partial class RequiresOrganToSeeSystem : EntitySystem
{
    [Dependency] private BlindableSystem _blindableSystem = default!;
    [Dependency] private BodyHierarchySystem _bodyHierarchySystem = default!;

    [SubscribeLocalEvent]
    private void OnOrganInsertedInto(Entity<RequiresOrganToSeeComponent> entity, ref OrganInsertedIntoEvent args)
    {
        if (args.OrganComponent.Category != entity.Comp.Category)
            return;

        _blindableSystem.UpdateIsBlind(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnOrganRemovedFrom(Entity<RequiresOrganToSeeComponent> entity, ref OrganRemovedFromEvent args)
    {
        if (args.OrganComponent.Category != entity.Comp.Category)
            return;

        _blindableSystem.UpdateIsBlind(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnSeeAttempt(Entity<RequiresOrganToSeeComponent> entity, ref CanSeeAttemptEvent args)
    {
        if (args.Cancelled ||
            _bodyHierarchySystem.TryGetOrgan(entity.Owner, entity.Comp.Category, out _))
            return;

        args.Cancel();
    }
}
