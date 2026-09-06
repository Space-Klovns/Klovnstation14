using Content.Shared.GameTicking;
using Content.Shared._Mono.PhosphorNightVision;
using Content.Shared._Mono.Overlays;
using Content.Client._Mono.Overlays;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Client.Audio;
using Content.Shared._KS14.IoC;

namespace Content.Client._Mono.PhosphorNightVision;

/// <inheritdoc/>
public sealed partial class PhosphorNightVisionSystem : SharedPhosphorNightVisionSystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private SystemCollectionHookManager _collectionHookManager = default!;

    private PhosphorNightVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new PhosphorNightVisionOverlay();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeLocalEvent<PhosphorNightVisionComponent, AfterAutoHandleStateEvent>(OnNvHandleState);

        _collectionHookManager.HookAction(collection =>
        {
            collection.InjectDependencies(_overlay);
            _overlay.Initialise();

            _overlayMan.AddOverlay(_overlay);
        });
    }

    // KS14: move overlay removal here
    public override void Shutdown()
    {
        _overlayMan.RemoveOverlay(_overlay);
        base.Shutdown();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        RefreshOverlay(args.Entity);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        Deactivate(_player.LocalEntity);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var localPlayer = _player.LocalSession?.AttachedEntity;
        if (localPlayer != null)
            Deactivate(localPlayer.Value);
    }


    private void OnNvHandleState(Entity<PhosphorNightVisionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshOverlay(ent);
    }

    /// <summary>
    /// Update the state of the overlay. Add/remove/modify based on <see cref="PhosphorNightVisionComponent"/>s if any.
    /// </summary>
    /// <param name="entity">The entity to have an overlay added/removed from.</param>
    /// <param name="entities">A list of entities with a <see cref="PhosphorNightVisionComponent"/>.</param>
    private bool TryUpdate(EntityUid entity, HashSet<Entity<PhosphorNightVisionComponent>> entities)
    {
        if (entity != _player.LocalSession?.AttachedEntity)
            return true;

        // Find the component with the lowest noise.
        Entity<PhosphorNightVisionComponent> nvEntity = default;
        foreach (var ent in entities)
        {
            if (!ent.Comp.Enabled)
                continue;

            if (ent.Comp.RelayOverlay == (ent.Owner == entity))
                continue;

            nvEntity = ent;

            // Take the first priority component
            if (ent.Comp.Prioritized)
                break;
        }

        // There is no active night vision components, so we disable the overlay.
        if (nvEntity == default)
            return false;

        // Relay all the settings from the component.
        _overlay.SetParameters(
            nvEntity.Comp!.LightingColor,
            nvEntity.Comp.PhosphorColor,
            nvEntity.Comp.Amplification,
            nvEntity.Comp.IsCone,
            nvEntity.Comp.ConeAngle,
            nvEntity.Comp.ConeFeather,
            nvEntity.Comp.ConeDistance,
            nvEntity.Comp.ConeDistanceFeather,
            //nvision.ViewAngle
            nvEntity.Comp.WearAnimationDuration
        );

        if (_overlay.Enabled)
            return true;

        _overlay.Enabled = true;
        _overlay.SetAnimationTimeNow();

        // ffs this sucks
        _audioSystem.PlayLocal(nvEntity.Comp.OnSound, nvEntity, entity);
        return true;
    }

    private void Deactivate(EntityUid? ent, Entity<PhosphorNightVisionComponent>? activeNvEntity = null)
    {
        if (ent != _player.LocalSession?.AttachedEntity ||
            !_overlay.Enabled)
            return;

        _overlay.Enabled = false;
        _overlay.SetAnimationTimeNow();

        // ffs this sucks
        if (activeNvEntity is { })
            _audioSystem.PlayLocal(activeNvEntity.Value.Comp.OffSound, activeNvEntity.Value, ent);
    }

    public override void RefreshOverlay(EntityUid target, Entity<PhosphorNightVisionComponent>? activeNvEntity = null)
    {
        if (target != _player.LocalSession?.AttachedEntity)
            return;

        var ev = new RefreshPhosphorNightVisionEvent();
        RaiseLocalEvent(target, ref ev);

        if (activeNvEntity is { })
            ev.Entities.Add(activeNvEntity.Value);

        if (ev.Entities.Count == 0 ||
            !TryUpdate(target, ev.Entities))
        {
            Deactivate(target, activeNvEntity: activeNvEntity);
        }
    }
}
