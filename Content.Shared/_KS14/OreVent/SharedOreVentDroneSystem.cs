using Content.Shared.Chat;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Shared._KS14.OreVent;

public abstract class SharedOreVentDroneSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedChatSystem _chatSystem = default!;

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
    }

    public void Escape(Entity<OreVentDroneComponent?> entity)
    {
        if (!Resolve(entity.Owner, ref entity.Comp))
            return;

        entity.Comp.VentUid = EntityUid.Invalid;
        Dirty(entity);

        _appearanceSystem.SetData(entity.Owner, OreVentDroneVisuals.Movement, OreVentDroneMovement.Dipping);

        // Yes this is horrible too
        var timedDespawnComponent = EntityManager.ComponentFactory.GetComponent<TimedDespawnComponent>();
        timedDespawnComponent.Lifetime = 4f;
        AddComp(entity, timedDespawnComponent);

        if (_robustRandom.Prob(0.5f))
            _chatSystem.TryEmoteWithChat(entity.Owner, "Flip", ignoreActionBlocker: true);
    }
}
