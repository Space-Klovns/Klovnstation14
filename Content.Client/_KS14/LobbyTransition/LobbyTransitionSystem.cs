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
    ///     Joining a round floods PVS with entities, which stutters the first moments of gameplay. PVS counts as
    ///         settled once the client's entity count grows by no more than <see cref="MaxSettledEntityGrowth"/>
    ///         over one window of this length. Measured in entities rather than frame times, so it means the same
    ///         thing on every machine however fast it renders.
    /// </summary>
    private const float SettleWindow = 0.125f;

    /// <summary>
    ///     Fraction of the current entity count that may still arrive within one <see cref="SettleWindow"/>
    ///         for PVS to count as settled; there is always some trickle from things spawning and moving about.
    /// </summary>
    private const float MaxSettledEntityGrowth = 0.01f;

    /// <summary>
    ///     Floor for <see cref="MaxSettledEntityGrowth"/>, so a near-empty map does not demand zero arrivals.
    /// </summary>
    private const int MinSettledEntityGrowth = 10;

    /// <summary>
    ///     If the game never settles, stop holding the art opaque after this long and fade anyway.
    /// </summary>
    private const float HoldTimeout = 3f;

    private LobbyTransitionPhase _phase = LobbyTransitionPhase.Idle;
    private float _heldTime;
    private float _windowTime;
    private int _windowStartEntityCount;
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
                _windowTime += realFrameTime;

                if (_heldTime >= HoldTimeout)
                {
                    Log.Error($"Lobby transition art was held opaque for {HoldTimeout}s without PVS settling (entity count {EntityManager.EntityCount}, local entity {(_playerManager.LocalEntity is { } ? "attached" : "missing")}); fading out anyway.");
                    _phase = LobbyTransitionPhase.Fading;
                    break;
                }

                if (_windowTime < SettleWindow)
                    break;

                var entityCount = EntityManager.EntityCount;
                var entityGrowth = entityCount - _windowStartEntityCount;
                var maxEntityGrowth = Math.Max((int)(entityCount * MaxSettledEntityGrowth), MinSettledEntityGrowth);

                // nothing is worth revealing until we have something to look at
                if (_playerManager.LocalEntity is { } && entityGrowth <= maxEntityGrowth)
                {
                    _phase = LobbyTransitionPhase.Fading;
                    break;
                }

                _windowTime = 0f;
                _windowStartEntityCount = entityCount;
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
        _windowTime = 0f;
        _windowStartEntityCount = EntityManager.EntityCount;
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
