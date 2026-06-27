using Content.Server.GameTicking;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GameTicking;
using Content.Shared._KS14.LobbyView;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._KS14.LobbyView;

public sealed class KsLobbyViewSystem : KsSharedLobbyViewSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriberSystem = default!;

    private int _lobbyViewCount = 0;
    private bool _lobbyViewEnabled = false;

    private EntityUid _bestUid = EntityUid.Invalid;
    private List<ICommonSession> _sessions = [];

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.LobbyViewEnabled, (enabled) => _lobbyViewEnabled = enabled, invokeImmediately: true);

        SubscribeLocalEvent<PlayerJoinedLobbyEvent>((args) => OnPlayerEnteredLobby(args.PlayerSession));
        SubscribeLocalEvent<KsPlayerLeftLobbyEvent>((args) => OnPlayerExitedLobby(args.PlayerSession));

        SubscribeLocalEvent<KsLobbyViewComponent, ComponentStartup>(OnLobbyViewStartup);
        SubscribeLocalEvent<KsLobbyViewComponent, ComponentShutdown>(OnLobbyViewShutdown);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnPlayerEnteredLobby(ICommonSession session)
    {
        _sessions.Add(session);

        if (_lobbyViewEnabled &&
            _bestUid != EntityUid.Invalid)
            _viewSubscriberSystem.AddViewSubscriber(_bestUid, session);
    }

    private void OnPlayerExitedLobby(ICommonSession session)
    {
        _sessions.Remove(session);

        if (_lobbyViewEnabled &&
            _bestUid != EntityUid.Invalid)
            _viewSubscriberSystem.RemoveViewSubscriber(_bestUid, session);
    }

    private void OnLobbyViewStartup(Entity<KsLobbyViewComponent> entity, ref ComponentStartup args)
    {
        _lobbyViewCount++;

        entity.Comp.Priority = _lobbyViewCount;
        Dirty(entity);

        UpdateLobbyView();
    }

    private void OnLobbyViewShutdown(Entity<KsLobbyViewComponent> entity, ref ComponentShutdown args)
    {
        _lobbyViewCount--;
        UpdateLobbyView();
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _lobbyViewCount = 0;
        _bestUid = EntityUid.Invalid;
    }

    private void UpdateLobbyView()
    {
        foreach (var session in _sessions)
            _viewSubscriberSystem.RemoveViewSubscriber(_bestUid, session);

        if (!TryGetBestLobbyView(out var entity))
        {
            _bestUid = EntityUid.Invalid;
            return;
        }

        _bestUid = entity.Value.Owner;
        foreach (var session in _sessions)
            _viewSubscriberSystem.AddViewSubscriber(_bestUid, session);
    }
}
