using Content.Shared._KS14.IoC;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Client side of the NPC squad debug overlay. Keeps the latest squad snapshot from
///         <see cref="Content.Server._KS14.NPC.Squad.NpcSquadDebugSystem"/> and adds or removes
///         <see cref="SquadDebugOverlay"/> as the server says.
/// </summary>
public sealed partial class SquadDebugSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    public SquadDebugDataMessage? Latest { get; private set; }

    private SquadDebugOverlay? _overlay;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _overlay = new SquadDebugOverlay();
        dependencyCollection.InjectDependencies(_overlay, oneOff: true);

        if (_enabled)
            _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Latest = null;
        _overlayManager.RemoveOverlay<SquadDebugOverlay>();
    }

    [SubscribeNetworkEvent]
    private void OnState(SquadDebugStateMessage message)
    {
        _enabled = message.Enabled;

        if (message.Enabled)
        {
            if (_overlay != null && !_overlayManager.HasOverlay<SquadDebugOverlay>())
                _overlayManager.AddOverlay(_overlay);
        }
        else
        {
            Latest = null;
            _overlayManager.RemoveOverlay<SquadDebugOverlay>();
        }
    }

    [SubscribeNetworkEvent]
    private void OnData(SquadDebugDataMessage message)
    {
        Latest = message;
    }
}
