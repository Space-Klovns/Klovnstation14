using Content.Shared._KS14.IoC;
using Content.Shared.StatusEffectNew;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._KS14.ShaderStatusEffect;

/// <summary>
///     Keeps <see cref="KsShaderStatusEffectOverlay"/> active exactly while the local player has at least one
///         status effect with <see cref="KsShaderStatusEffectComponent"/>.
/// </summary>
public sealed partial class KsShaderStatusEffectOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;

    private KsShaderStatusEffectOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        RemoveOverlay();
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _overlay = new KsShaderStatusEffectOverlay();
        dependencyCollection.InjectDependencies(_overlay, oneOff: true);

        RefreshOverlay();
    }

    [SubscribeLocalEvent]
    private void OnApplied(Entity<KsShaderStatusEffectComponent> entity, ref StatusEffectAppliedEvent args)
    {
        if (args.Target != _playerManager.LocalEntity)
            return;

        RefreshOverlay();
    }

    [SubscribeLocalEvent]
    private void OnRemoved(Entity<KsShaderStatusEffectComponent> entity, ref StatusEffectRemovedEvent args)
    {
        if (args.Target != _playerManager.LocalEntity)
            return;

        RefreshOverlay();
    }

    [SubscribeLocalEvent]
    private void OnPlayerAttached(Entity<KsShaderStatusEffectComponent> entity, ref StatusEffectRelayedEvent<LocalPlayerAttachedEvent> args)
    {
        AddOverlay();
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(Entity<KsShaderStatusEffectComponent> entity, ref StatusEffectRelayedEvent<LocalPlayerDetachedEvent> args)
    {
        RemoveOverlay();
    }

    private void RefreshOverlay()
    {
        if (_statusEffectsSystem.HasEffectComp<KsShaderStatusEffectComponent>(_playerManager.LocalEntity))
            AddOverlay();
        else
            RemoveOverlay();
    }

    private void AddOverlay()
    {
        if (_overlay != null)
            _overlayManager.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlayManager.RemoveOverlay(_overlay);
        _overlay.ClearShaders();
    }
}
