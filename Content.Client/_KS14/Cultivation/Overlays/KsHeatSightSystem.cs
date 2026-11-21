using Content.Shared._KS14.Cultivation.Body;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._KS14.Cultivation.Overlays;

/// <summary>
///     Shows the heat sight overlay while the local player has <see cref="KsHeatSightComponent"/>.
/// </summary>
public sealed partial class KsHeatSightSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    private KsHeatSightOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KsHeatSightComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsHeatSightComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KsHeatSightComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<KsHeatSightComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Hide();
    }

    private void OnStartup(Entity<KsHeatSightComponent> entity, ref ComponentStartup args)
    {
        if (_playerManager.LocalEntity == entity.Owner)
            Show();
    }

    private void OnShutdown(Entity<KsHeatSightComponent> entity, ref ComponentShutdown args)
    {
        if (_playerManager.LocalEntity == entity.Owner)
            Hide();
    }

    private void OnAttached(Entity<KsHeatSightComponent> entity, ref LocalPlayerAttachedEvent args)
    {
        Show();
    }

    private void OnDetached(Entity<KsHeatSightComponent> entity, ref LocalPlayerDetachedEvent args)
    {
        Hide();
    }

    private void Show()
    {
        _overlay ??= new KsHeatSightOverlay();
        if (!_overlayManager.HasOverlay<KsHeatSightOverlay>())
            _overlayManager.AddOverlay(_overlay);
    }

    private void Hide()
    {
        if (_overlay != null)
            _overlayManager.RemoveOverlay(_overlay);
    }
}
