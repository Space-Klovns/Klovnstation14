using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Client side of a debug overlay fed one frame at a time per squad (or lone NPC). Keeps the latest frame of each,
///         drops one not heard of for <see cref="FrameLifetime"/>, and adds or removes <typeparamref name="TOverlay"/> as
///         the server says. The subclass subscribes to its three messages and passes them to <see cref="HandleState"/>,
///         <see cref="HandleData"/> and <see cref="HandleEnded"/>.
/// </summary>
public abstract partial class NpcDebugFrameSystem<TFrame, TOverlay> : EntitySystem
    where TOverlay : Overlay, new()
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    /// <summary>
    ///     A frame not heard of for this long is dropped: its squad is gone, or asleep.
    /// </summary>
    private static readonly TimeSpan FrameLifetime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     The latest frame of each, by squad (or lone NPC), with when it arrived.
    /// </summary>
    public readonly Dictionary<NetEntity, (TFrame Frame, TimeSpan ReceivedAt)> Frames = new();

    private TOverlay? _overlay;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _overlay = new TOverlay();
        dependencyCollection.InjectDependencies(_overlay, oneOff: true);

        if (_enabled)
            _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Frames.Clear();
        _overlayManager.RemoveOverlay<TOverlay>();
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

    protected void HandleState(bool enabled)
    {
        _enabled = enabled;
        Frames.Clear();

        if (enabled)
        {
            if (_overlay != null && !_overlayManager.HasOverlay<TOverlay>())
                _overlayManager.AddOverlay(_overlay);
        }
        else
        {
            _overlayManager.RemoveOverlay<TOverlay>();
        }
    }

    protected void HandleData(NetEntity issuer, TFrame frame)
    {
        Frames[issuer] = (frame, _gameTiming.RealTime);
    }

    protected void HandleEnded(NetEntity issuer)
    {
        Frames.Remove(issuer);
    }
}
