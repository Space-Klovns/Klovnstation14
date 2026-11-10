using System.Diagnostics.CodeAnalysis;
using Content.Client._KS14.AdminMusic;
using Content.Client._KS14.ZLevel.Audio;
using Content.Shared._KS14.Cinematics;
using Content.Shared.GameTicking;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cinematics;

/// <summary>
///     Plays the cinematics the server starts. While one runs, every bit of UI is covered and, if the cinematic
///         asks for it, all audio except its own is silenced.
/// </summary>
public sealed partial class KsCinematicSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private KsAdminMusicManager _adminMusicManager = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private KsZLevelAudioSystem _zLevelAudioSystem = default!;

    private KsCinematicControl? _control;
    private bool _silencingAudio;

    /// <summary>
    ///     Scene sounds still to play, with when they play, in order.
    /// </summary>
    private readonly Queue<(TimeSpan Start, SoundSpecifier Sound)> _pendingSounds = new();

    /// <summary>
    ///     The path of the cinematic's continued sound, which is never silenced, or empty if it has none.
    /// </summary>
    private string _continuedSoundPath = string.Empty;

    /// <summary>
    ///     Audio started by the cinematic itself, which is never silenced.
    /// </summary>
    private readonly HashSet<EntityUid> _cinematicAudio = new();

    public bool Active => _control != null;

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

        while (_pendingSounds.TryPeek(out var pending) && _control.Elapsed >= pending.Start)
        {
            _pendingSounds.Dequeue();
            PlayCinematicSound(_audioSystem.ResolveSound(pending.Sound), pending.Sound.Params);
        }

        // Keep the cinematic on top of anything that got added to the root since it started.
        _control.SetPositionLast();
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        Stop();
    }

    [SubscribeNetworkEvent]
    private void OnPlayCinematic(KsPlayCinematicEvent ev)
    {
        Stop();

        if (!ProtoMan.TryIndex(ev.Cinematic, out var cinematic))
        {
            Log.Error($"Server played cinematic {ev.Cinematic}, which this client doesn't have.");
            return;
        }

        if (ev.SceneDurations.Length != cinematic.Scenes.Count)
        {
            Log.Error($"Server played cinematic {ev.Cinematic} with {ev.SceneDurations.Length} scenes, " +
                      $"but this client's has {cinematic.Scenes.Count}.");
            return;
        }

        var rsiPath = SpriteSpecifierSerializer.TextureRoot / cinematic.Sprite;

        // TryGetResource remembers a missing RSI, unlike GetResource, which would retry the load
        //      and dump a stack trace every time.
        if (!_resourceCache.TryGetResource<RSIResource>(rsiPath, out var resource))
        {
            Log.Error($"Couldn't load {rsiPath}, skipping cinematic {ev.Cinematic}.");
            return;
        }

        var shots = new KsCinematicShot[cinematic.Scenes.Count];
        var sounds = new List<(TimeSpan Start, SoundSpecifier Sound)>();
        var sceneStart = TimeSpan.Zero;
        for (var i = 0; i < cinematic.Scenes.Count; i++)
        {
            var scene = cinematic.Scenes[i];
            if (!TryGetState(resource.RSI, rsiPath, scene.State, out var state))
                return;

            shots[i] = new KsCinematicShot(state, ev.SceneDurations[i]);

            foreach (var sceneSound in scene.Sounds)
            {
                sounds.Add((sceneStart + sceneSound.Offset, sceneSound.Sound));
            }

            sceneStart += ev.SceneDurations[i];
        }

        // A scene's sound may play after a later scene's has, so they're queued in the order they play.
        sounds.Sort((first, second) => first.Start.CompareTo(second.Start));
        foreach (var sound in sounds)
        {
            _pendingSounds.Enqueue(sound);
        }

        _control = new KsCinematicControl(shots, cinematic.FadeIn, cinematic.FadeOut);
        _userInterfaceManager.RootControl.AddChild(_control);
        _userInterfaceManager.ReleaseKeyboardFocus();

        if (cinematic.SilenceAudio)
        {
            _silencingAudio = true;
            _adminMusicManager.Muted = true;
            _zLevelAudioSystem.SilenceStream = ShouldSilenceStream;
        }

        if (ev.ContinuedSound is { } continuedSound)
            ContinueSound(continuedSound);
    }

    /// <summary>
    ///     Keeps a sound that was already playing audible, and plays the rest of it to anyone who never heard it
    ///         start (off-station, or the sound never played at all).
    /// </summary>
    private void ContinueSound(KsCinematicContinuedSound continuedSound)
    {
        _continuedSoundPath = _audioSystem.GetAudioPath(continuedSound.Sound) ?? string.Empty;

        if (!IsContinuedSoundPlaying() && continuedSound.Offset < _audioSystem.GetAudioLength(continuedSound.Sound))
        {
            PlayCinematicSound(continuedSound.Sound,
                continuedSound.Params.WithPlayOffset((float)continuedSound.Offset.TotalSeconds));
        }
    }

    private void Stop()
    {
        _pendingSounds.Clear();
        _cinematicAudio.Clear();
        _continuedSoundPath = string.Empty;

        if (_silencingAudio)
        {
            _silencingAudio = false;
            _adminMusicManager.Muted = false;
            // Silenced streams get their volume back from the audio system on its next update.
            _zLevelAudioSystem.SilenceStream = null;
        }

        _control?.Orphan();
        _control = null;
    }

    private void PlayCinematicSound(ResolvedSoundSpecifier sound, AudioParams audioParams)
    {
        if (_audioSystem.PlayGlobal(sound, Filter.Local(), false, audioParams) is { } played)
            _cinematicAudio.Add(played.Entity);
    }

    private bool IsContinuedSoundPlaying()
    {
        var query = AllEntityQuery<AudioComponent>();
        while (query.MoveNext(out var audioComponent))
        {
            if (IsContinuedSound(audioComponent) && audioComponent.State == AudioState.Playing)
                return true;
        }

        return false;
    }

    private bool IsContinuedSound(AudioComponent audioComponent)
    {
        return _continuedSoundPath != string.Empty && audioComponent.FileName == _continuedSoundPath;
    }

    /// <summary>
    ///     Silences every stream but the cinematic's own while it silences audio. Silencing streams in the audio
    ///         system's own processing, rather than zeroing them after it has already restored their volume, means
    ///         OpenAL never mixes a sliver of them at full gain, which made everything that started mid-cinematic
    ///         crackle. Runs on the audio system's parallel job, so it must only read shared state.
    /// </summary>
    private bool ShouldSilenceStream(EntityUid uid, AudioComponent audioComponent)
    {
        return !_cinematicAudio.Contains(uid) && !IsContinuedSound(audioComponent);
    }

    private bool TryGetState(RSI rsi, ResPath rsiPath, string name, [NotNullWhen(true)] out RSI.State? state)
    {
        if (rsi.TryGetState(name, out state))
            return true;

        Log.Error($"{rsiPath} is missing the '{name}' state, skipping the cinematic.");
        return false;
    }
}
