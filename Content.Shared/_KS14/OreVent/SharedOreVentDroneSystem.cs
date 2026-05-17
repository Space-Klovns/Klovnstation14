using Content.Shared._KS14.Deferral;
using Content.Shared.Buckle;

namespace Content.Shared._KS14.OreVent;

public abstract class SharedOreVentDroneSystem : EntitySystem
{
    [Dependency] private readonly SynchronousDeferralSystem _synchronousDeferralSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedBuckleSystem _buckleSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreVentDroneComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<OreVentDroneComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.VentUid == EntityUid.Invalid)
            return;

        var ev = new OreVentDroneDestroyedEvent(entity.Owner);
        RaiseLocalEvent(entity.Comp.VentUid, ref ev);
    }

    public void Arrive(Entity<OreVentDroneComponent?> entity, EntityUid ventUid)
    {
        if (!Resolve(entity.Owner, ref entity.Comp))
            return;

        entity.Comp.VentUid = ventUid;
        Dirty(entity);

        _appearanceSystem.SetData(entity.Owner, OreVentDroneVisuals.Movement, OreVentDroneMovement.Arriving);
        _buckleSystem.TryBuckle(entity.Owner, entity.Owner, ventUid);
    }

    public void Escape(Entity<OreVentDroneComponent?> entity)
    {
        if (!Resolve(entity.Owner, ref entity.Comp))
            return;

        entity.Comp.VentUid = EntityUid.Invalid;
        Dirty(entity);

        if (TryComp<AppearanceComponent>(entity.Owner, out var appearanceComponent))
        {
            _appearanceSystem.SetData(entity.Owner, OreVentDroneVisuals.Progress, 0, component: appearanceComponent);
            _appearanceSystem.SetData(entity.Owner, OreVentDroneVisuals.Movement, OreVentDroneMovement.StartingUp, component: appearanceComponent);
        }

        // Yes this is horrible 2
        _synchronousDeferralSystem.ScheduleForward(() => FinishEscape(entity.Owner), TimeSpan.FromSeconds(1.9d));
    }

    private void FinishEscape(EntityUid uid)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearanceComponent))
            return;

        _appearanceSystem.SetData(uid, OreVentDroneVisuals.Flying, true, component: appearanceComponent);
        _appearanceSystem.SetData(uid, OreVentDroneVisuals.Movement, OreVentDroneMovement.Dipping, component: appearanceComponent);

        QueueDel(uid);
    }
}
