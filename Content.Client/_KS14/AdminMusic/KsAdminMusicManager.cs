using System.Linq;
using Content.Client._KS14.AdminMusic.UI;
using Content.Shared._KS14.AdminMusic;
using Robust.Client;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._KS14.AdminMusic;

public sealed partial class KsAdminMusicManager : IPostInjectInit
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IClientNetManager _netManager = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IBaseClient _client = default!;

    private ISawmill _sawmill = default!;

    private readonly Dictionary<KsAdminMusicEntry, KsAdminMusicEntryData> _activeEntryData = [];

    /// <summary>
    ///     Set of entries that have been ended early. This is used to make sure that
    ///         new incoming entries (from server) that have already been closed, will be closed.
    /// </summary>
    private readonly HashSet<KsAdminMusicEntry> _endedEntries = [];

    public void Initialise()
    {
        _client.RunLevelChanged += OnRunLevelChanged;
        _netManager.RegisterNetMessage<KsAdminMusicDataMessage>(OnDataUpdate);
    }

    public void Shutdown()
    {
        foreach (var (_, data) in _activeEntryData)
        {
            if (data.AudioSource is not { } audioSource)
                continue;

            audioSource.StopPlaying();
            audioSource.Dispose();
        }

        _activeEntryData.Clear();
        _activeEntryData.TrimExcess();

        _endedEntries.Clear();
        _endedEntries.TrimExcess();
    }

    void IPostInjectInit.PostInject()
    {
        // make sure this is the same as server pls
        _sawmill = Logger.GetSawmill("ks.adminmusic");
    }

    private void AddAndPlayEntry(KsAdminMusicEntry entry)
    {
        var playbackPosition = (float)(_gameTiming.ServerTime /* this is sus */ - entry.StartTime).TotalSeconds;
        if (playbackPosition < 0f)
            playbackPosition = 0f;

        if (!_resourceCache.TryGetResource<AudioResource>(entry.SoundPath, out var audioResource))
        {
            _sawmill.Error($"Tried to load AudioResource from path {entry.SoundPath}, but failed.");
            return;
        }

        // apparently this entry is already playing, grim
        if (_activeEntryData.TryGetValue(entry, out var existingEntryData) &&
            existingEntryData.AudioSource is { } audioSource)
        {
            audioSource.Restart();
        }
        else
        {
            audioSource = _audioManager.CreateAudioSource(audioResource)!;
            audioSource.Global = true;
            audioSource.Volume = entry.Volume;

            audioSource.StartPlaying();
        }

        audioSource.PlaybackPosition = playbackPosition;

        var entryData = new KsAdminMusicEntryData(audioSource, null, audioResource);
        _activeEntryData[entry] = entryData;

        if (ContainerControl is { })
            AddToPopupContainer(entry);
    }

    private void RemoveEntry(KsAdminMusicEntry entry)
    {
        if (!_activeEntryData.TryGetValue(entry, out var entryData))
            return;

        entryData.Popup?.Orphan();
        _activeEntryData.Remove(entry);

        if (entryData.AudioSource is { } audioSource)
            audioSource.Dispose();

        entryData.AudioSource = null;
        entryData.Popup = null;
    }

    private void OnRunLevelChanged(object? sender, RunLevelChangedEventArgs args)
    {
        if (args.NewLevel != ClientRunLevel.Initialize)
            return;

        foreach (var entry in _activeEntryData.Keys)
            RemoveEntry(entry);
    }

    private void OnDataUpdate(KsAdminMusicDataMessage message)
    {
        var updatedSet = message.Entries.ToHashSet();

        // TODO LCDC: ADMINMUSIC: support changing playback position here im lazy rn

        var activeEntries = _activeEntryData.Keys;
        var addedEntries = updatedSet.Except(activeEntries);
        var removedEntries = activeEntries.Except(updatedSet);

        foreach (var addedEntry in addedEntries)
        {
            if (_endedEntries.Contains(addedEntry))
                continue;

            AddAndPlayEntry(addedEntry);
        }

        foreach (var removedEntry in removedEntries)
        {
            RemoveEntry(removedEntry);
            _endedEntries.Remove(removedEntry);
        }
    }

    public sealed class KsAdminMusicEntryData(IAudioSource? audioSource, KsAdminMusicPopup? popup, AudioResource audioResource)
    {
        public IAudioSource? AudioSource = audioSource;

        public KsAdminMusicPopup? Popup = popup;

        public AudioResource AudioResource = audioResource;
    }
}
