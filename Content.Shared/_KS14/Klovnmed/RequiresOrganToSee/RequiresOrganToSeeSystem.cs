using Content.Shared.Body;
using Content.Shared.Eye.Blinding.Systems;

namespace Content.Shared._KS14.Klovnmed.RequiresOrganToSee;

public sealed class RequiresOrganToSeeSystem : EntitySystem
{
    [Dependency] private readonly BlindableSystem _blindableSystem = default!;
    [Dependency] private readonly BodyHierarchySystem _bodyHierarchySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RequiresOrganToSeeComponent, OrganInsertedIntoEvent>(OnOrganInsertedInto);
        SubscribeLocalEvent<RequiresOrganToSeeComponent, OrganRemovedFromEvent>(OnOrganRemovedFrom);

        SubscribeLocalEvent<RequiresOrganToSeeComponent, CanSeeAttemptEvent>(OnSeeAttempt);
    }

    private void OnOrganInsertedInto(Entity<RequiresOrganToSeeComponent> entity, ref OrganInsertedIntoEvent args)
    {
        if (args.OrganComponent.Category != entity.Comp.Category)
            return;

        _blindableSystem.UpdateIsBlind(entity.Owner);
    }

    private void OnOrganRemovedFrom(Entity<RequiresOrganToSeeComponent> entity, ref OrganRemovedFromEvent args)
    {
        if (args.OrganComponent.Category != entity.Comp.Category)
            return;

        _blindableSystem.UpdateIsBlind(entity.Owner);
    }

    private void OnSeeAttempt(Entity<RequiresOrganToSeeComponent> entity, ref CanSeeAttemptEvent args)
    {
        if (_bodyHierarchySystem.TryGetOrgan(entity.Owner, entity.Comp.Category, out _))
            return;

        args.Cancel();
    }
}
