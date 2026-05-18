using Content.Shared.Chat;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Shared._KS14.OreVent.Drone;

public abstract class SharedOreVentDroneSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedChatSystem _chatSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreVentDroneComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<OreVentDroneComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.VentUid is not { } ventUid)
            return;

        var ev = new OreVentDroneDestroyedEvent(entity.Owner);
        RaiseLocalEvent(ventUid, ref ev);
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

        entity.Comp.VentUid = null;
        Dirty(entity);

        _appearanceSystem.SetData(entity.Owner, OreVentDroneVisuals.Movement, OreVentDroneMovement.Dipping);

        // Yes this is horrible too
        var timedDespawnComponent = EntityManager.ComponentFactory.GetComponent<TimedDespawnComponent>();
        timedDespawnComponent.Lifetime = 4f;
        AddComp(entity, timedDespawnComponent);

        if (_robustRandom.Prob(0.25f))
        {
            _chatSystem.TryEmoteWithChat(entity.Owner, "Flip", ignoreActionBlocker: true, networkedFilter: Filter.Pvs(entity.Owner, entityManager: EntityManager));
            _audioSystem.PlayPvs(new SoundPathSpecifier("/Audio/Machines/twobeep.ogg"), entity.Owner);
        }
    }
}
