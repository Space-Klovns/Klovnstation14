

using Content.Shared._KS14.AdminMusic;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Enums;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.AdminMusic;

public sealed class KsAdminMusicManager : IPostInjectInit
{
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IServerNetManager _netManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    private AudioSystem _audioSystem = default!;

    private ISawmill _sawmill = default!;

    public IReadOnlySet<KsAdminMusicEntry> ActiveEntries => _activeEntries;
    private readonly HashSet<KsAdminMusicEntry> _activeEntries = [];
    private readonly Dictionary<KsAdminMusicEntry, TimeSpan> _entryEndTimes = [];

    public void Initialise()
    {
        _netManager.RegisterNetMessage<KsAdminMusicDataMessage>();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public void Update()
    {
        var oldActiveCount = _activeEntries.Count;
        var curTime = _gameTiming.CurTime;

        foreach (var (entry, endTime) in _entryEndTimes)
        {
            if (curTime < endTime)
                continue;

            RemoveEntryNoUpdate(entry);
        }

        // only send data update if something actually changed
        if (oldActiveCount == _activeEntries.Count)
            return;

        SendDataUpdateToAll();
    }

    void IPostInjectInit.PostInject()
    {
        // make sure this is the same as client pls
        _sawmill = Logger.GetSawmill("ks.adminmusic");
    }

    public void AddEntry(KsAdminMusicEntry entry)
    {
        _audioSystem ??= _entitySystemManager.GetEntitySystem<AudioSystem>();
        var audioLength = _audioSystem.GetAudioLength(new ResolvedPathSpecifier(entry.SoundPath));
        if (audioLength <= TimeSpan.Zero)
        {
            _sawmill.Error($"Admin music had zero or negative audio length ({audioLength})! Path: {entry.SoundPath}");
            return;
        }

        _activeEntries.Add(entry);
        SendDataUpdateToAll();

        _entryEndTimes[entry] = _gameTiming.RealTime + audioLength;
    }

    public void RemoveEntry(KsAdminMusicEntry entry)
    {
        RemoveEntryNoUpdate(entry);
        SendDataUpdateToAll();
    }

    public void RemoveEntryNoUpdate(KsAdminMusicEntry entry)
    {
        _activeEntries.Remove(entry);
        _activeEntries.TrimExcess();

        _entryEndTimes.Remove(entry);
        _entryEndTimes.TrimExcess();
    }

    public void RemoveAllEntries()
    {
        _activeEntries.Clear();
        _activeEntries.TrimExcess();
        SendDataUpdateToAll();

        _entryEndTimes.Clear();
        _entryEndTimes.TrimExcess();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Connected ||
            _activeEntries.Count == 0)
            return;

        SendDataUpdate(args.Session);
    }

    private KsAdminMusicDataMessage FormMessage()
    {
        if (_activeEntries.Count > byte.MaxValue)
            throw new InvalidOperationException("Why the fuck do you have more than 255 admin music tracks playing at once? Only up to 255 are supported for literally no good reason ever.");

        return new KsAdminMusicDataMessage
        {
            EntryCount = (byte)_activeEntries.Count,
            Entries = [.. _activeEntries]
        };
    }

    private void SendDataUpdate(ICommonSession session)
        => _netManager.ServerSendMessage(FormMessage(), session.Channel);

    private void SendDataUpdateToAll()
        => _netManager.ServerSendToAll(FormMessage());
}
