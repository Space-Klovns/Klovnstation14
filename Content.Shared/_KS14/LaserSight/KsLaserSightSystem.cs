namespace Content.Shared._KS14.LaserSight;

public sealed partial class KsLaserSightSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsLaserSightComponent> entity, ref MapInitEvent args)
    {
        // The server switches this on while it has a target; until then it must not show, whatever the YAML says.
        if (entity.Comp.OnlyWhileTargeting)
            SetEnabled(entity.AsNullable(), false);
    }

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
