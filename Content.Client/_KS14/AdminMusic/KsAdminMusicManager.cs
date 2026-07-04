

using System.Linq;
using Content.Shared._KS14.AdminMusic;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._KS14.AdminMusic;

public sealed class KsAdminMusicManager : IPostInjectInit
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IClientNetManager _netManager = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    private ISawmill _sawmill = default!;

    private readonly HashSet<KsAdminMusicEntry> _activeEntries = [];
    private readonly Dictionary<KsAdminMusicEntry, IAudioSource> _audioSources = [];

    public readonly HashSet<KsAdminMusicEntry> EndedEntries = [];

    public void Initialise()
    {
        _netManager.RegisterNetMessage<KsAdminMusicDataMessage>(OnDataUpdate);
    }

    public void Shutdown()
    {
        foreach (var (_, source) in _audioSources)
        {
            source.StopPlaying();
            source.Dispose();
        }

        _activeEntries.Clear();
        _audioSources.Clear();
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = Logger.GetSawmill("ks.adminmusic");
    }

    private void AddAndPlayEntry(KsAdminMusicEntry entry)
    {
        var playbackPosition = (float)(_gameTiming.CurTime - entry.StartTime).TotalSeconds;
        if (playbackPosition < 0f)
            playbackPosition = 0f;

        if (!_resourceCache.TryGetResource<AudioResource>(entry.SoundPath, out var audioResource))
        {
            _sawmill.Error($"Tried to load AudioResource from path {entry.SoundPath}, but failed.");
            return;
        }

        // collectionsmarshal help

        if (_audioSources.TryGetValue(entry, out var existingSource))
        {
            existingSource.StopPlaying();
            existingSource.Dispose();
        }

        var audioSource = _audioManager.CreateAudioSource(audioResource)!;
        _audioSources[entry] = audioSource;

        audioSource.Global = true;
        audioSource.PlaybackPosition = playbackPosition;
        audioSource.Volume = entry.Volume;
        audioSource.StartPlaying();
    }

    private void RemoveEntry(KsAdminMusicEntry entry)
    {
        _activeEntries.Remove(entry);

        if (!_audioSources.TryGetValue(entry, out var audioSource))
            return;

        audioSource.StopPlaying();
        audioSource.Dispose();
    }

    private void OnDataUpdate(KsAdminMusicDataMessage message)
    {
        var updatedSet = message.Entries.ToHashSet();

        // TODO LCDC: ADMINMUSIC: support changing playback position here im lazy rn

        var addedEntries = updatedSet.Except(_activeEntries);
        var removedEntries = _activeEntries.Except(updatedSet);

        foreach (var addedEntry in addedEntries)
            AddAndPlayEntry(addedEntry);

        foreach (var removedEntry in removedEntries)
            RemoveEntry(removedEntry);
    }
}
