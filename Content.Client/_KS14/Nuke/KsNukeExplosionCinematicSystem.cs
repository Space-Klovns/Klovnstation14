using Content.Client._KS14.AdminMusic;
using Content.Client._KS14.Cinematics;
using Content.Shared._KS14.Nuke;
using Content.Shared.GameTicking;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Nuke;

/// <summary>
///     Plays the SS13-style station explosion cinematic when a nuke goes off.
///     While it runs, every bit of UI is covered and all audio except the cinematic's own is silenced.
/// </summary>
public sealed partial class KsNukeExplosionCinematicSystem : EntitySystem
{
    private const string RsiPath = "/Textures/_KS14/Cinematic/station_explosion.rsi";
    private static readonly ResolvedSoundSpecifier ExplosionSound = new ResolvedPathSpecifier("/Audio/Effects/explosionfar.ogg");
    private const float ExplosionVolume = -6f;
    private static readonly TimeSpan FadeIn = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(1);

    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private KsAdminMusicManager _adminMusic = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private AudioSystem _audioSystem = default!;

    private KsCinematicControl? _control;
    private string _alarmPath = string.Empty;
    private TimeSpan _explosionDelay;
    private bool _explosionPlayed;

    /// <summary>
    ///     Audio started by the cinematic itself, which is never silenced.
    /// </summary>
    private readonly HashSet<EntityUid> _cinematicAudio = new();

    public bool Active => _control != null;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<KsNukeExplosionCinematicEvent>(OnCinematic);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Stop());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Stop();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_control == null)
            return;

        if (_control.Finished)
        {
            Stop();
            return;
        }

        if (!_explosionPlayed && _control.Elapsed >= _explosionDelay)
        {
            _explosionPlayed = true;
            PlayCinematicSound(ExplosionSound, AudioParams.Default.WithVolume(ExplosionVolume));
        }

        // Keep the cinematic on top of anything that got added to the root since it started.
        _control.SetPositionLast();
    }

    private void OnCinematic(KsNukeExplosionCinematicEvent ev)
    {
        Stop();

        var rsi = _resourceCache.GetResource<RSIResource>(RsiPath).RSI;
        var explode = GetState(rsi, "station_explode_fade_red");
        var summary = ev.Ending switch
        {
            KsNukeExplosionEnding.TotalA => "summary_totala",
            _ => "summary_nukewin",
        };

        var intro = KsNukeExplosionCinematicTimings.IntroDuration;
        KsCinematicScene[] scenes =
        [
            new(GetState(rsi, "intro_nuke"), intro),
            new(GetState(rsi, "station_intact"), ev.ExplosionDelay - intro),
            new(explode, TimeSpan.FromSeconds(explode.AnimationLength)),
            new(GetState(rsi, summary), KsNukeExplosionCinematicTimings.SummaryDuration),
        ];

        _explosionDelay = ev.ExplosionDelay;
        _explosionPlayed = false;
        _alarmPath = _audio.GetAudioPath(ev.AlarmSound) ?? string.Empty;

        _control = new KsCinematicControl(scenes, FadeIn, FadeOut);
        _ui.RootControl.AddChild(_control);
        _ui.ReleaseKeyboardFocus();
        _adminMusic.Muted = true;
        _audioSystem.ProcessStreamOverride += ProcessStream;

        // Players who never heard the alarm (off-station, admin detonation) get the rest of it locally.
        if (!IsAlarmPlaying() && ev.AlarmOffset < _audio.GetAudioLength(ev.AlarmSound))
        {
            PlayCinematicSound(ev.AlarmSound,
                AudioParams.Default.
                    WithVolume(-2f).
                        WithPlayOffset((float)ev.AlarmOffset.TotalSeconds));
        }
    }

    private void Stop()
    {
        if (_control == null)
            return;

        _control.Orphan();
        _control = null;
        _cinematicAudio.Clear();
        _adminMusic.Muted = false;
        // Silenced streams get their volume back from the audio system on its next tick.
        _audioSystem.ProcessStreamOverride -= ProcessStream;
    }

    private void PlayCinematicSound(ResolvedSoundSpecifier sound, AudioParams audioParams)
    {
        if (_audio.PlayGlobal(sound, Filter.Local(), false, audioParams) is { } played)
            _cinematicAudio.Add(played.Entity);
    }

    private bool IsAlarmPlaying()
    {
        var query = AllEntityQuery<AudioComponent>();
        while (query.MoveNext(out var audio))
        {
            if (IsAlarm(audio) && audio.State == AudioState.Playing)
                return true;
        }

        return false;
    }

    private bool IsAlarm(AudioComponent audio)
    {
        return _alarmPath != string.Empty && audio.FileName == _alarmPath;
    }

    /// <summary>
    ///     Replaces the audio system's per-stream processing while the cinematic runs. Silencing streams here,
    ///     rather than zeroing them after the audio system has already restored their volume, means OpenAL
    ///     never mixes a sliver of them at full gain, which made everything that started mid-cinematic
    ///     (like the nuke's own explosion) crackle.
    ///     Runs on the audio system's parallel job, so it must only read shared state.
    /// </summary>
    private void ProcessStream(EntityUid uid, AudioComponent audio, TransformComponent transform, MapCoordinates listener)
    {
        if (!audio.Started)
        {
            audio.Started = true;
            audio.StartPlaying();
        }

        // Everything the cinematic plays, and the alarm, is global, so there's no positioning to do.
        if (_cinematicAudio.Contains(uid) || IsAlarm(audio))
            audio.Volume = audio.Params.Volume;
        else
            audio.Gain = 0f;
    }

    private static RSI.State GetState(RSI rsi, string name)
    {
        if (!rsi.TryGetState(name, out var state))
            throw new InvalidOperationException($"{RsiPath} is missing the '{name}' state.");

        return state;
    }
}
