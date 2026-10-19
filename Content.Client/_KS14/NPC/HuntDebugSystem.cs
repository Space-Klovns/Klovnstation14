using Content.Shared._KS14.IoC;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Client side of the hunt debug overlay. Keeps the latest frame of each hunt from
///         <see cref="Content.Server._KS14.NPC.Squad.Tactics.NpcHuntDebugSystem"/>, and adds or removes
///         <see cref="HuntDebugOverlay"/> as the server says.
/// </summary>
public sealed partial class HuntDebugSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    /// <summary>
    ///     A hunt not heard of for this long is dropped: its squad is gone, or asleep.
    /// </summary>
    private static readonly TimeSpan FrameLifetime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     The latest frame of each hunt, by squad (or lone NPC), with when it arrived.
    /// </summary>
    public readonly Dictionary<NetEntity, (HuntDebugDataMessage Frame, TimeSpan ReceivedAt)> Frames = new();

    private HuntDebugOverlay? _overlay;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _overlay = new HuntDebugOverlay();
        dependencyCollection.InjectDependencies(_overlay, oneOff: true);

        if (_enabled)
            _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Frames.Clear();
        _overlayManager.RemoveOverlay<HuntDebugOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (Frames.Count == 0)
            return;

        var now = _gameTiming.RealTime;
        foreach (var (issuer, (_, receivedAt)) in Frames)
        {
            if (now - receivedAt > FrameLifetime)
                Frames.Remove(issuer);
        }
    }

    [SubscribeNetworkEvent]
    private void OnState(HuntDebugStateMessage message)
    {
        _enabled = message.Enabled;
        Frames.Clear();

        if (message.Enabled)
        {
            if (_overlay != null && !_overlayManager.HasOverlay<HuntDebugOverlay>())
                _overlayManager.AddOverlay(_overlay);
        }
        else
        {
            _overlayManager.RemoveOverlay<HuntDebugOverlay>();
        }
    }

    [SubscribeNetworkEvent]
    private void OnData(HuntDebugDataMessage message)
    {
        Frames[message.Issuer] = (message, _gameTiming.RealTime);
    }

    [SubscribeNetworkEvent]
    private void OnEnded(HuntDebugEndedMessage message)
    {
        Frames.Remove(message.Issuer);
    }
}
