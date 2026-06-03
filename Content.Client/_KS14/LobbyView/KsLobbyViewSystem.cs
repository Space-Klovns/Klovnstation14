using System.Numerics;
using Content.Client.Eye;
using Content.Client.Lobby;
using Content.Shared._KS14.LobbyView;
using Robust.Client.Graphics;
using Robust.Client.State;

namespace Content.Client._KS14.LobbyView;

public sealed class KsLobbyViewSystem : KsSharedLobbyViewSystem
{
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly EyeLerpingSystem _eyeLerpingSystem = default!;

    private LobbyState? _lobbyState = null;
    private bool _inLobby = false;

    private Entity<KsLobbyViewComponent, EyeComponent>? _currentViewEntity = null;

    public override void Initialize()
    {
        base.Initialize();

        _stateManager.OnStateChanged += OnStateChanged;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_inLobby ||
            _lobbyState?.Lobby is not { } lobbyGui)
            return;

        TryGetBestLobbyView(out var entity);

        if (_currentViewEntity == entity)
            return;

        if (_currentViewEntity is { } oldViewEntity)
            _eyeLerpingSystem.RemoveEye(oldViewEntity.Owner);

        if (entity is not { } newViewEntity)
        {
            lobbyGui.KsSetCameraView(null);
            return;
        }

        lobbyGui.KsViewport.ViewportSize = _clyde.ScreenSize;
        lobbyGui.KsViewport.MaxSize = Vector2.Max(entity.Value.Comp1.SizePixels / lobbyGui.KsViewport.UIScale, lobbyGui.KsViewport.MinSize);

        _currentViewEntity = entity;
        _eyeLerpingSystem.AddEye(newViewEntity.Owner, newViewEntity.Comp2);

        lobbyGui.KsSetCameraView(newViewEntity.Comp2.Eye);
    }

    private void OnStateChanged(StateChangedEventArgs args)
    {
        switch (args.NewState)
        {
            case LobbyState lobbyState:
                _inLobby = true;
                _lobbyState = lobbyState;
                break;
            default:
                _inLobby = false;
                _lobbyState = null;

                if (args.OldState is LobbyState oldLobbyState)
                    oldLobbyState.Lobby?.KsSetCameraView(null);

                if (_currentViewEntity is { } oldViewEntity)
                    _eyeLerpingSystem.RemoveEye(oldViewEntity.Owner);

                _currentViewEntity = null;
                break;
        }
    }
}
