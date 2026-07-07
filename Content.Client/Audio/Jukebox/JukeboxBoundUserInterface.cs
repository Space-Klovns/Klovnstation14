using Content.Shared._sin.Audio.Jukebox; // _sin addition
using Content.Shared.Audio.Jukebox;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Audio.Jukebox;

public sealed partial class JukeboxBoundUserInterface : BoundUserInterface
{
    [Dependency] private IPrototypeManager _protoManager = default!;

    [ViewVariables]
    private JukeboxMenu? _menu;

    public JukeboxBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<JukeboxMenu>();

        _menu.OnPlayPressed += args =>
        {
            if (args)
            {
                SendMessage(new JukeboxPlayingMessage());
            }
            else
            {
                SendMessage(new JukeboxPauseMessage());
            }
        };

        _menu.OnStopPressed += () =>
        {
            SendMessage(new JukeboxStopMessage());
        };

        _menu.OnSongSelected += SelectSong;

        _menu.SetTime += SetTime;
        // _sin start
        _menu.SetVolume += vol => SendMessage(new JukeboxSetVolumeMessage(vol));
        _menu.OnAutoplayChanged += enabled => SendMessage(new JukeboxSetAutoplayMessage(enabled));
        _menu.OnQueueChanged += queue => SendMessage(new JukeboxSetQueueMessage(queue));
        // _sin end
        PopulateMusic();
        Reload();
    }

    /// <summary>
    /// Reloads the attached menu if it exists.
    /// </summary>
    public void Reload()
    {
        if (_menu == null || !EntMan.TryGetComponent(Owner, out JukeboxComponent? jukebox))
            return;

        _menu.SetAudioStream(jukebox.AudioStream);
        _menu.SetVolumeSliderValue(jukebox.Volume); //_ sin

        if (_protoManager.TryIndex /* _sin: TryIndex instead of Resolve */(jukebox.SelectedSongId, out var songProto))
        {
            var length = EntMan.System<AudioSystem>().GetAudioLength(songProto.Path.Path.ToString());
            _menu.SetSelectedSong(songProto.Name, (float)length.TotalSeconds);
        }
        else
        {
            _menu.SetSelectedSong(string.Empty, 0f);
        }

        _menu.SetAutoplayEnabled(jukebox.AutoplayEnabled); // _sin addition
    }

    public void PopulateMusic()
    {
        _menu?.Populate(_protoManager.EnumeratePrototypes<JukeboxPrototype>());
        _menu?.PopulateGroups(_protoManager.EnumeratePrototypes<BoomboxGroupPrototype>());
    }

    public void SelectSong(ProtoId<JukeboxPrototype> songid)
    {
        SendMessage(new JukeboxSelectedMessage(songid));
        // _sin start
        // Send queue only if autoplay is enabled — otherwise the first queued track
        // will play immediately when the user turns on AutoPlay.
        if (_menu?.AutoplayEnabled == true)
            _menu?.SendQueueUpdate();
        // _sin end
    }

    public void SetTime(float time)
    {
        // _sin start: remove previous code, and don't predict anything
        // Note: we intentionally do NOT do client-side prediction here (setting audioComp.PlaybackPosition).
        // The audio system recalculates position from AudioStart every frame, so the prediction would get
        // undone immediately, causing an audible double-seek. The lock timer in JukeboxMenu prevents the
        // slider from bouncing while we wait for the server to update AudioStart.
        SendMessage(new JukeboxSetTimeMessage(time));
        // _sin end
    }
}
