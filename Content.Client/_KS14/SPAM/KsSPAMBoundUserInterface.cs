using Content.Shared._KS14.SPAM;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._KS14.SPAM;

public sealed class KsSPAMBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private KsSPAMWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsSPAMWindow>();
        _window.OnLogin += () =>
        {
            PlaySound(spamComponent => spamComponent.LoginSound);
            SendMessage(new KsSPAMLoginMessage());
        };
        _window.OnLoggedIn += () => PlaySound(spamComponent => spamComponent.LoggedInSound);
        _window.OnLogout += () => SendMessage(new KsSPAMLogoutMessage());
        _window.OnTaskShown += () => PlaySound(spamComponent => spamComponent.TaskInfoSound);
        _window.OnTrack += taskId =>
        {
            PlaySound(spamComponent => spamComponent.TrackSound);
            SendMessage(new KsSPAMTrackTaskMessage(taskId));
        };
        _window.OnDrop += taskId => SendMessage(new KsSPAMDropTaskMessage(taskId));
        _window.OnOrder += cart => SendMessage(new KsSPAMOrderMessage(cart));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not KsSPAMBuiState spamState || !EntMan.TryGetComponent<KsSPAMComponent>(Owner, out var spamComponent))
            return;

        _window?.UpdateState(spamState, spamComponent);
    }

    /// <summary>
    ///     Plays one of the tablet's sounds to whoever is looking at it, and nobody else.
    /// </summary>
    private void PlaySound(Func<KsSPAMComponent, SoundSpecifier?> pickSound)
    {
        if (EntMan.TryGetComponent<KsSPAMComponent>(Owner, out var spamComponent))
            EntMan.System<SharedAudioSystem>().PlayGlobal(pickSound(spamComponent), Filter.Local(), recordReplay: false);
    }

    protected override void Dispose(bool disposing)
    {
        // The window is disposed with this, and a disposed window never says it closed.
        if (disposing)
            _window?.CloseSubWindows();

        base.Dispose(disposing);
    }
}
