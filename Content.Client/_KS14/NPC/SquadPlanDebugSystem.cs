using Content.Shared._KS14.IoC;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Client side of the squad plan debug overlay. Keeps the latest frame of each squad's plan from
///         <see cref="Content.Server._KS14.NPC.Squad.Tactics.NpcSquadPlanDebugSystem"/>, and adds or removes
///         <see cref="SquadPlanDebugOverlay"/> as the server says.
/// </summary>
public sealed partial class SquadPlanDebugSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    /// <summary>
    ///     A plan not heard of for this long is dropped: its squad is gone, or asleep.
    /// </summary>
    private static readonly TimeSpan FrameLifetime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     The latest frame of each plan, by squad (or lone NPC), with when it arrived.
    /// </summary>
    public readonly Dictionary<NetEntity, (SquadPlanDebugDataMessage Frame, TimeSpan ReceivedAt)> Frames = new();

    private SquadPlanDebugOverlay? _overlay;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _overlay = new SquadPlanDebugOverlay();
        dependencyCollection.InjectDependencies(_overlay, oneOff: true);

        if (_enabled)
            _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Frames.Clear();
        _overlayManager.RemoveOverlay<SquadPlanDebugOverlay>();
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
    private void OnState(SquadPlanDebugStateMessage message)
    {
        _enabled = message.Enabled;
        Frames.Clear();

        if (message.Enabled)
        {
            if (_overlay != null && !_overlayManager.HasOverlay<SquadPlanDebugOverlay>())
                _overlayManager.AddOverlay(_overlay);
        }
        else
        {
            _overlayManager.RemoveOverlay<SquadPlanDebugOverlay>();
        }
    }

    [SubscribeNetworkEvent]
    private void OnData(SquadPlanDebugDataMessage message)
    {
        Frames[message.Issuer] = (message, _gameTiming.RealTime);
    }

    [SubscribeNetworkEvent]
    private void OnEnded(SquadPlanDebugEndedMessage message)
    {
        Frames.Remove(message.Issuer);
    }
}
