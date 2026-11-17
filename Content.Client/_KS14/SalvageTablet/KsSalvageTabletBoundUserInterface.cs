using Content.Client._KS14.Guidebook;
using Content.Client.UserInterface;
using Content.Shared._KS14.SalvageTablet;
using Robust.Client.Timing;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.SalvageTablet;

/// <summary>
///     The salvage tablet's interface. It draws from the last state the server sent and from the tablet's own networked
///         component, so <see cref="Update"/> is where everything is drawn: after every state, and whenever the component
///         changes (<see cref="KsSalvageTabletUiSystem"/>).
///
///     The account lives on the server alone, so its handlers can't be predicted. Tracking, dropping and logging out are
///         replayed onto each state until the server has answered them instead, so the window doesn't snap back.
/// </summary>
public sealed partial class KsSalvageTabletBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private IClientGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private KsSalvageTabletWindow? _window;
    private KsSalvageTabletTaskWindow? _taskWindow;
    private KsSalvageTabletOrderWindow? _orderWindow;
    private BuiPredictionState? _prediction;
    private KsSalvageTabletBuiState _state = new();

    protected override void Open()
    {
        base.Open();

        _prediction = new BuiPredictionState(this, _gameTiming);
        _window = this.CreateWindow<KsSalvageTabletWindow>();
        _window.OnLogin += () =>
        {
            PlaySound(tabletComponent => tabletComponent.LoginSound);
            SendMessage(new KsSalvageTabletLoginMessage());
        };
        _window.OnLoggedIn += () => PlaySound(tabletComponent => tabletComponent.LoggedInSound);
        _window.OnLogout += () => SendPredicted(new KsSalvageTabletLogoutMessage());
        _window.OnShowTask += ShowTask;
        _window.OnShowOrders += ShowOrders;

        Update();
    }

    /// <summary>
    ///     Sends a message, and applies it to what is shown until the server has handled it.
    /// </summary>
    private void SendPredicted(BoundUserInterfaceMessage message)
    {
        _prediction!.SendMessage(message);
        Replay(message);
        Update();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not KsSalvageTabletBuiState tabletState)
            return;

        _state = tabletState;
        if (_prediction == null)
            return;

        foreach (var message in _prediction.MessagesToReplay())
        {
            Replay(message);
        }
    }

    /// <summary>
    ///     What a message the server hasn't answered yet will do to the state.
    /// </summary>
    private void Replay(BoundUserInterfaceMessage message)
    {
        switch (message)
        {
            case KsSalvageTabletTrackTaskMessage track:
                foreach (var task in _state.Tasks)
                {
                    if (task.Task == track.Task)
                        task.Tracked = true;
                }

                break;
            case KsSalvageTabletDropTaskMessage drop:
                _state.Tasks.RemoveAll(task => task.Task == drop.Task);
                break;
            case KsSalvageTabletLogoutMessage:
                _state.LoggedIn = false;
                break;
        }
    }

    public override void Update()
    {
        base.Update();

        if (_window == null || !EntMan.TryGetComponent<KsSalvageTabletComponent>(Owner, out var tabletComponent))
            return;

        _window.UpdateState(_state, tabletComponent);

        if (!_state.LoggedIn)
        {
            _taskWindow?.Close();
            _orderWindow?.Close();
            return;
        }

        if (_taskWindow is { IsOpen: true })
        {
            // The task on show was finished, failed or dropped.
            if (FindTask(_taskWindow.Task) is { } shown)
                _taskWindow.SetTask(shown, _state.NextDropTime, CanTrackMore(tabletComponent));
            else
                _taskWindow.Close();
        }

        if (_orderWindow is { IsOpen: true })
            _orderWindow.UpdateState(_state.Points, GetRank(tabletComponent), _state.NextOrderTime, tabletComponent.MaxOrderAmount);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is KsSalvageTabletOrderResultMessage result)
            _orderWindow?.OrderResult(result.Accepted);
    }

    private KsSalvageActiveTask? FindTask(ProtoId<KsSalvageTaskPrototype> taskId)
    {
        foreach (var task in _state.Tasks)
        {
            if (task.Task == taskId)
                return task;
        }

        return null;
    }

    private bool CanTrackMore(KsSalvageTabletComponent tabletComponent)
    {
        return KsSalvageTabletWindow.TrackedCount(_state) < tabletComponent.MaxTrackedTasks;
    }

    private int GetRank(KsSalvageTabletComponent tabletComponent)
    {
        return _prototypeManager.TryIndex(tabletComponent.RankLadder, out var ladderPrototype) ? ladderPrototype.GetRank(_state.Reputation) : 0;
    }

    /// <summary>
    ///     The details of a task, in a window of their own. Made for this interface and disposed with it, so it closes
    ///         with the tablet, but closing it leaves the tablet open.
    /// </summary>
    private void ShowTask(ProtoId<KsSalvageTaskPrototype> taskId)
    {
        if (FindTask(taskId) is not { } task || !EntMan.TryGetComponent<KsSalvageTabletComponent>(Owner, out var tabletComponent))
            return;

        if (_taskWindow == null)
        {
            _taskWindow = this.WithGuide(this.CreateDisposableControl<KsSalvageTabletTaskWindow>());
            _taskWindow.OnTrack += shownTaskId =>
            {
                PlaySound(component => component.TrackSound);
                SendPredicted(new KsSalvageTabletTrackTaskMessage(shownTaskId));
            };
            _taskWindow.OnDrop += shownTaskId => SendPredicted(new KsSalvageTabletDropTaskMessage(shownTaskId));
        }

        _taskWindow.SetTask(task, _state.NextDropTime, CanTrackMore(tabletComponent));
        if (!_taskWindow.IsOpen)
            _taskWindow.OpenCentered();

        PlaySound(component => component.TaskInfoSound);
    }

    /// <summary>
    ///     The order window, made and disposed as the task window is.
    /// </summary>
    private void ShowOrders()
    {
        if (!EntMan.TryGetComponent<KsSalvageTabletComponent>(Owner, out var tabletComponent))
            return;

        if (_orderWindow == null)
        {
            _orderWindow = this.WithGuide(this.CreateDisposableControl<KsSalvageTabletOrderWindow>());
            _orderWindow.OnOrder += cart => SendMessage(new KsSalvageTabletOrderMessage(cart));
        }

        _orderWindow.UpdateState(_state.Points, GetRank(tabletComponent), _state.NextOrderTime, tabletComponent.MaxOrderAmount);
        if (!_orderWindow.IsOpen)
            _orderWindow.OpenCentered();
    }

    /// <summary>
    ///     Plays one of the tablet's sounds to whoever is looking at it, and nobody else.
    /// </summary>
    private void PlaySound(Func<KsSalvageTabletComponent, SoundSpecifier?> pickSound)
    {
        if (EntMan.TryGetComponent<KsSalvageTabletComponent>(Owner, out var tabletComponent))
            EntMan.System<SharedAudioSystem>().PlayGlobal(pickSound(tabletComponent), Filter.Local(), recordReplay: false);
    }
}
