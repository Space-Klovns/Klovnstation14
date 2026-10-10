namespace Content.Shared._KS14.LaserSight;

public sealed partial class KsLaserSightSystem : EntitySystem
{
    public void SetEnabled(Entity<KsLaserSightComponent?> entity, bool enabled)
    {
        if (!Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        if (entity.Comp.Enabled == enabled)
            return;

        entity.Comp.Enabled = enabled;
        Dirty(entity.Owner, entity.Comp);
    }
}
