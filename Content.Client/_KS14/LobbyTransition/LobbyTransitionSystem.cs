using Content.Client.Gameplay;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.LobbyTransition;

public sealed partial class LobbyTransitionSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ClientGameTicker _gameTicker = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    private LobbyTransitionOverlay? _overlay = null;

    private static readonly TimeSpan TransitionDuration = TimeSpan.FromSeconds(0.8d);
    private TimeSpan _transitionFinishTime = TimeSpan.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        _stateManager.OnStateChanged += OnStateChanged;
        _systemCollectionHookManager.HookAction(OnHook);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_overlay is not { } ||
            _gameTiming.CurTime < _transitionFinishTime)
            return;

        _overlay.ArtTexture = null;
    }

    private void OnStateChanged(StateChangedEventArgs args)
    {
        if (_overlay is not { } ||
            args.OldState is not LobbyState ||
            args.NewState is not GameplayStateBase ||
            !_prototypeManager.TryIndex(_gameTicker.LobbyBackground, out var backgroundProto))
            return;

        _transitionFinishTime = _gameTiming.CurTime + TransitionDuration;

        _overlay.ArtTexture = _resourceCache.GetResource<TextureResource>(backgroundProto.Background);
        _overlay.TransitionFinishTime = _transitionFinishTime;
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
