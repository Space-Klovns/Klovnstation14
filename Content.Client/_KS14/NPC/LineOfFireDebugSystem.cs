using Content.Shared._KS14.IoC;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Client side of the line of fire debug overlay. Keeps the latest shots from
///         <see cref="Content.Server._KS14.NPC.LineOfFire.NpcLineOfFireDebugSystem"/>, and adds or removes
///         <see cref="LineOfFireDebugOverlay"/> as the server says.
/// </summary>
public sealed partial class LineOfFireDebugSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    public LineOfFireDebugDataMessage? Latest;

    private LineOfFireDebugOverlay? _overlay;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _overlay = new LineOfFireDebugOverlay();
        dependencyCollection.InjectDependencies(_overlay, oneOff: true);

        if (_enabled)
            _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Latest = null;
        _overlayManager.RemoveOverlay<LineOfFireDebugOverlay>();
    }

    [SubscribeNetworkEvent]
    private void OnState(LineOfFireDebugStateMessage message)
    {
        _enabled = message.Enabled;
        Latest = null;

        if (message.Enabled)
        {
            if (_overlay != null && !_overlayManager.HasOverlay<LineOfFireDebugOverlay>())
                _overlayManager.AddOverlay(_overlay);
        }
        else
        {
            _overlayManager.RemoveOverlay<LineOfFireDebugOverlay>();
        }
    }

    [SubscribeNetworkEvent]
    private void OnData(LineOfFireDebugDataMessage message)
    {
        Latest = message;
    }
}
