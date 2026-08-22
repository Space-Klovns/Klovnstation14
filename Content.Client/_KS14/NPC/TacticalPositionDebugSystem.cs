using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._KS14.NPC;

/// <summary>
/// Client side of the tactical position debug overlay. Stores the most recent debug frame per NPC
/// (see <see cref="Content.Server._KS14.NPC.Systems.NpcTacticalPositionDebugSystem"/>) and adds/removes
/// <see cref="TacticalPositionDebugOverlay"/> in response to the server telling us whether we're subscribed.
/// </summary>
public sealed class TacticalPositionDebugSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    private static readonly TimeSpan FrameLifetime = TimeSpan.FromSeconds(2);

    private readonly Dictionary<NetEntity, (TimeSpan Expiry, TacticalPositionDebugDataMessage Data)> _frames = new();

    public IReadOnlyDictionary<NetEntity, (TimeSpan Expiry, TacticalPositionDebugDataMessage Data)> Frames => _frames;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TacticalPositionDebugStateMessage>(OnState);
        SubscribeNetworkEvent<TacticalPositionDebugDataMessage>(OnData);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _frames.Clear();
        _overlayManager.RemoveOverlay<TacticalPositionDebugOverlay>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.RealTime;
        List<NetEntity>? expired = null;

        foreach (var (owner, frame) in _frames)
        {
            if (now < frame.Expiry)
                continue;

            expired ??= new List<NetEntity>();
            expired.Add(owner);
        }

        if (expired == null)
            return;

        foreach (var owner in expired)
        {
            _frames.Remove(owner);
        }
    }

    private void OnState(TacticalPositionDebugStateMessage message)
    {
        if (message.Enabled)
        {
            if (!_overlayManager.HasOverlay<TacticalPositionDebugOverlay>())
                _overlayManager.AddOverlay(new TacticalPositionDebugOverlay(this, EntityManager, _eyeManager));
        }
        else
        {
            _frames.Clear();
            _overlayManager.RemoveOverlay<TacticalPositionDebugOverlay>();
        }
    }

    private void OnData(TacticalPositionDebugDataMessage message)
    {
        _frames[message.Owner] = (_gameTiming.RealTime + FrameLifetime, message);
    }
}
