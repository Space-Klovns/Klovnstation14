using Content.Shared.Sticky;

namespace Content.Shared._KS14.Sticky;

public sealed partial class WallmountOnStickSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnStuck(Entity<ComponentsOnStickComponent> entity, ref EntityStuckEvent args)
    {
        if (entity.Comp.ComponentsGotAdded)
            return;

        if (entity.Comp.RequiresOccluder &&
            !HasComp<OccluderComponent>(args.Target))
            return;

        EntityManager.AddComponents(entity.Owner, entity.Comp.Components);

        entity.Comp.ComponentsGotAdded = true;
        Dirty(entity);
    }

    [SubscribeLocalEvent]
    private void OnUnstuck(Entity<ComponentsOnStickComponent> entity, ref EntityUnstuckEvent args)
    {
        if (!entity.Comp.ComponentsGotAdded)
            return;

        EntityManager.RemoveComponents(args.Target, entity.Comp.Components);

        entity.Comp.ComponentsGotAdded = false;
        Dirty(entity);
    }
}
