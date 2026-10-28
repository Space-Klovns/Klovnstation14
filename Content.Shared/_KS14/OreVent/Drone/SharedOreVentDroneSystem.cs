using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Chat;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Shared._KS14.OreVent.Drone;

public abstract partial class SharedOreVentDroneSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    private static readonly LocId FunnyEndingLine = "ks-specific-orevent-drone-funny-ending-line";

    [SubscribeLocalEvent]
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

    /// <param name="success">Whether it leaves because the vent was tapped, rather than because tapping failed.</param>
    public void Escape(Entity<OreVentDroneComponent?> entity, bool success)
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

        _popupSystem.PopupEntity(
            KsPopupMessage.Create(success ? "ks-specific-orevent-drone-escape-success" : "ks-specific-orevent-drone-escape-failure"),
            entity.Owner,
            type: success ? PopupType.Medium : PopupType.MediumCaution);

        // As tgstation's node drone does, once in a while.
        if (_robustRandom.Prob(entity.Comp.FunnyEndingChance))
        {
            entity.Comp.FunnyEnding = true;
            _chatSystem.TrySendInGameICMessage(entity.Owner, Loc.GetString(FunnyEndingLine), InGameICChatType.Speak, hideChat: false, ignoreActionBlocker: true);
        }

        if (_robustRandom.Prob(0.25f))
        {
            _chatSystem.TryEmoteWithChat(entity.Owner, "Flip", ignoreActionBlocker: true, networkedFilter: Filter.Pvs(entity.Owner, entityManager: EntityManager));
            _audioSystem.PlayPvs(new SoundPathSpecifier("/Audio/Machines/twobeep.ogg"), entity.Owner);
        }
    }

    [SubscribeLocalEvent]
    private void OnDespawn(Entity<OreVentDroneComponent> entity, ref TimedDespawnEvent args)
    {
        if (!entity.Comp.FunnyEnding)
            return;

        // It never made it home. At the coordinates, since the drone is deleted straight after this.
        var coordinates = Transform(entity).Coordinates;
        _audioSystem.PlayPvs(entity.Comp.FunnyEndingSound, coordinates);
        _popupSystem.PopupCoordinates(KsPopupMessage.Create("ks-specific-orevent-drone-funny-ending"), coordinates, type: PopupType.Medium);
    }
}
