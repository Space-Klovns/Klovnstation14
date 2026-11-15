using Content.Shared._KS14.SalvageTablet;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._KS14.SalvageTablet;

public sealed class KsSalvageTabletBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private KsSalvageTabletWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsSalvageTabletWindow>();
        _window.OnLogin += () =>
        {
            PlaySound(tabletComponent => tabletComponent.LoginSound);
            SendMessage(new KsSalvageTabletLoginMessage());
        };
        _window.OnLoggedIn += () => PlaySound(tabletComponent => tabletComponent.LoggedInSound);
        _window.OnLogout += () => SendMessage(new KsSalvageTabletLogoutMessage());
        _window.OnTaskShown += () => PlaySound(tabletComponent => tabletComponent.TaskInfoSound);
        _window.OnTrack += taskId =>
        {
            PlaySound(tabletComponent => tabletComponent.TrackSound);
            SendMessage(new KsSalvageTabletTrackTaskMessage(taskId));
        };
        _window.OnDrop += taskId => SendMessage(new KsSalvageTabletDropTaskMessage(taskId));
        _window.OnOrder += cart => SendMessage(new KsSalvageTabletOrderMessage(cart));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not KsSalvageTabletBuiState tabletState || !EntMan.TryGetComponent<KsSalvageTabletComponent>(Owner, out var tabletComponent))
            return;

        _window?.UpdateState(tabletState, tabletComponent);
    }

    /// <summary>
    ///     Plays one of the tablet's sounds to whoever is looking at it, and nobody else.
    /// </summary>
    private void PlaySound(Func<KsSalvageTabletComponent, SoundSpecifier?> pickSound)
    {
        if (EntMan.TryGetComponent<KsSalvageTabletComponent>(Owner, out var tabletComponent))
            EntMan.System<SharedAudioSystem>().PlayGlobal(pickSound(tabletComponent), Filter.Local(), recordReplay: false);
    }

    protected override void Dispose(bool disposing)
    {
        // The window is disposed with this, and a disposed window never says it closed.
        if (disposing)
            _window?.CloseSubWindows();

        base.Dispose(disposing);
    }
}
