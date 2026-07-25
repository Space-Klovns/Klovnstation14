using Content.Client.Overlays;
using Content.Shared.GameTicking;
using Content.Shared._Mono.PhosphorNightVision;
using Content.Shared._Mono.Overlays;
using Content.Client._Mono.Overlays;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Mono.PhosphorNightVision;

/// <inheritdoc/>
public sealed partial class PhosphorNightVisionSystem : SharedPhosphorNightVisionSystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IPlayerManager _player = default!;

    private PhosphorNightVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new PhosphorNightVisionOverlay();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<PhosphorNightVisionComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        _overlayMan.AddOverlay(_overlay); // KS14: moved here
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

    private void OnHandleState(Entity<PhosphorNightVisionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshOverlay(ent);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var localPlayer = _player.LocalSession?.AttachedEntity;
        if (localPlayer != null)
            Deactivate(localPlayer.Value);
    }

    /// <summary>
    /// Update the state of the overlay. Add/remove/modify based on <see cref="PhosphorNightVisionComponent"/>s if any.
    /// </summary>
    /// <param name="entity">The entity to have an overlay added/removed from.</param>
    /// <param name="entities">A list of entities with a <see cref="PhosphorNightVisionComponent"/>.</param>
    private void Update(EntityUid entity, List<Entity<PhosphorNightVisionComponent>> entities)
    {
        if (entity != _player.LocalSession?.AttachedEntity)
            return;

        // Find the component with the lowest noise.
        PhosphorNightVisionComponent? nvision = null;
        foreach (var ent in entities)
        {
            if (!ent.Comp.Enabled)
                continue;

            if (ent.Comp.RelayOverlay == (ent.Owner == entity))
                continue;

            nvision = ent.Comp;

            // Take the first priority component
            if (ent.Comp.Prioritized)
                break;
        }

        // There is no active night vision components, so we disable the overlay.
        if (nvision == null)
        {
            Deactivate(entity);
            return;
        }

        // Relay all the settings from the component.
        _overlay.SetParameters(
            nvision.LightingColor,
            nvision.PhosphorColor,
            nvision.Amplification,
            nvision.PhosphorEffect,
            nvision.IsCone,
            nvision.ConeAngle,
            nvision.ConeFeather,
            nvision.ConeDistance,
            nvision.ConeDistanceFeather,
            //nvision.ViewAngle
            nvision.WearAnimationDuration
        );

        // KS14: moved overlay add to init

        // KS14
        if (!_overlay.Enabled)
        {
            _overlay.Enabled = true;
            _overlay.SetAnimationTimeNow();
        }
    }

    private void Deactivate(EntityUid? ent)
    {
        if (ent != _player.LocalSession?.AttachedEntity)
            return;

        // KS14: moved overlay removal to shutdown

        // KS14
        if (_overlay.Enabled)
        {
            _overlay.Enabled = false;
            _overlay.SetAnimationTimeNow();
        }
    }

    protected override void RefreshOverlay(EntityUid target)
    {
        if (target != _player.LocalSession?.AttachedEntity)
            return;
        var ev = new RefreshPhosphorNightVisionEvent();
        RaiseLocalEvent(target, ref ev);

        if (ev.Entities.Count > 0)
            Update(target, ev.Entities);
        else
            Deactivate(target);
    }
}
