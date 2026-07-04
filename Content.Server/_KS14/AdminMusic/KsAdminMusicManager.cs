

using Content.Shared._KS14.AdminMusic;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._KS14.AdminMusic;

public sealed class KsAdminMusicManager
{
    [Dependency] private readonly IServerNetManager _netManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly List<KsAdminMusicEntry> _activeEntries = [];

    public void Initialise()
    {
        _netManager.RegisterNetMessage<KsAdminMusicDataMessage>();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public void AddEntry(KsAdminMusicEntry entry)
    {
        _activeEntries.Add(entry);
        SendDataUpdateToAll();
    }

    public void RemoveEntry(KsAdminMusicEntry entry)
    {
        _activeEntries.Remove(entry);
        SendDataUpdateToAll();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (_activeEntries.Count == 0)
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
