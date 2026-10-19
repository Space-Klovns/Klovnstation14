using Content.Client.Gameplay;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.LobbyTransition;

public sealed partial class LobbyTransitionSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private ClientGameTicker _gameTicker = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    private LobbyTransitionOverlay? _overlay = null;

    /// <summary>
    ///     How long the fade-out takes, in unhitched frame time.
    /// </summary>
    private const float FadeDuration = 0.8f;

    /// <summary>
    ///     The most a single frame may advance the fade by. A hitch mid-fade then slows the fade rather than
    ///         skipping part of it; generous enough that a machine running at 10fps still fades at full speed.
    /// </summary>
    private const float MaxFadeStep = 0.1f;

    /// <summary>
    ///     Joining a round floods PVS with entities, so the first ticks of gameplay take longer than a tick period
    ///         to process and the simulation falls behind real time. The game counts as lagging while, after this
    ///         frame's ticks have run, it is still more than this many tick periods behind. One period of that is
    ///         the ordinary leftover the game loop carries between frames; anything past it is tick work that took
    ///         longer than the tick it was for. Rendering is not counted, so a machine that draws slowly but
    ///         simulates fine is not mistaken for lagging.
    /// </summary>
    private const double MaxSettledTickBacklog = 2d;

    /// <summary>
    ///     How long, in real time, the game must go without lagging before the fade starts.
    /// </summary>
    private const float RequiredSettledTime = 0.25f;

    /// <summary>
    ///     If the game never settles, stop holding the art opaque after this long and fade anyway.
    /// </summary>
    private const float HoldTimeout = 3f;

    private LobbyTransitionPhase _phase = LobbyTransitionPhase.Idle;
    private float _heldTime;
    private float _settledTime;
    private float _fadeProgress;

    public override void Initialize()
    {
        base.Initialize();

        _stateManager.OnStateChanged += OnStateChanged;
        _systemCollectionHookManager.HookAction(OnHook);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_overlay is not { })
            return;

        // real time, so neither the timescale nor tick catch-up distorts it
        var realFrameTime = (float)_gameTiming.RealFrameTime.TotalSeconds;

        switch (_phase)
        {
            case LobbyTransitionPhase.Holding:
                _heldTime += realFrameTime;

                // FrameUpdate runs after this frame's ticks, so this is how far the simulation is still behind
                var tickBacklog = (_gameTiming.RealTime - _gameTiming.LastTick) / _gameTiming.TickPeriod;
                var lagging = tickBacklog > MaxSettledTickBacklog;

                // nothing is worth revealing until we have something to look at
                _settledTime = lagging || _playerManager.LocalEntity is not { } ? 0f : _settledTime + realFrameTime;

                if (_settledTime >= RequiredSettledTime)
                {
                    _phase = LobbyTransitionPhase.Fading;
                }
                else if (_heldTime >= HoldTimeout)
                {
                    Log.Error($"Lobby transition art was held opaque for {HoldTimeout}s without the game settling (tick backlog {tickBacklog:F1} periods, local entity {(_playerManager.LocalEntity is { } ? "attached" : "missing")}); fading out anyway.");
                    _phase = LobbyTransitionPhase.Fading;
                }

                break;
            case LobbyTransitionPhase.Fading:
                _fadeProgress += MathF.Min(realFrameTime, MaxFadeStep) / FadeDuration;
                if (_fadeProgress >= 1f)
                {
                    _phase = LobbyTransitionPhase.Idle;
                    _overlay.ArtTexture = null;
                    return;
                }

                break;
            default:
                return;
        }

        _overlay.Alpha = 1f - _fadeProgress;
    }

    private void OnStateChanged(StateChangedEventArgs args)
    {
        if (_overlay is not { } ||
            args.OldState is not LobbyState ||
            args.NewState is not GameplayStateBase ||
            !ProtoMan.TryIndex(_gameTicker.LobbyBackground, out var backgroundProto))
            return;

        _phase = LobbyTransitionPhase.Holding;
        _heldTime = 0f;
        _settledTime = 0f;
        _fadeProgress = 0f;

        _overlay.ArtTexture = _resourceCache.GetResource<TextureResource>(backgroundProto.Background);
        _overlay.Alpha = 1f;
    }

    private void OnHook(IDependencyCollection dependencyCollection)
    {
        _overlay = new LobbyTransitionOverlay();
        dependencyCollection.InjectDependencies(_overlay, oneOff: true);

        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _stateManager.OnStateChanged -= OnStateChanged;
        _overlayManager.RemoveOverlay<LobbyTransitionOverlay>();
    }
}

internal enum LobbyTransitionPhase : byte
{
    Idle,
    Holding,
    Fading,
}
