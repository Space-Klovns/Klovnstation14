using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.Debug;

/// <summary>
///     The players watching one NPC debug overlay, and the bookkeeping every such overlay shares: who is watching and
///         with what scope, dropping them on disconnect or when they lose the debug flag, and telling their clients to
///         stop at a round restart. Owned by a debug system, which calls <see cref="Initialize"/> and
///         <see cref="Shutdown"/> from its own, and turns the state it is told to send into its own message.
/// </summary>
public sealed class NpcDebugSessions
{
    private readonly IAdminManager _adminManager;
    private readonly IPlayerManager _playerManager;
    private readonly Action<ICommonSession, bool, EntityUid?> _sendState;

    /// <summary>
    ///     Null value = everything; otherwise only this entity (or, for the squad ones, its squad).
    /// </summary>
    private readonly Dictionary<ICommonSession, EntityUid?> _sessions = new();

    /// <param name="adminManager">Used to check the debug flag.</param>
    /// <param name="playerManager">Used to see disconnects.</param>
    /// <param name="sendState">
    ///     Sends the state message to a session: whether it is on, and with what scope (null when turning it off).
    /// </param>
    public NpcDebugSessions(IAdminManager adminManager, IPlayerManager playerManager, Action<ICommonSession, bool, EntityUid?> sendState)
    {
        _adminManager = adminManager;
        _playerManager = playerManager;
        _sendState = sendState;
    }

    public int Count => _sessions.Count;

    /// <summary>
    ///     Every watching session and its scope. Iterate with foreach; the struct enumerator does not allocate.
    /// </summary>
    public Dictionary<ICommonSession, EntityUid?> Watching => _sessions;

    public void Initialize()
    {
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        _adminManager.OnPermsChanged += OnAdminPermsChanged;
    }

    public void Shutdown()
    {
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _adminManager.OnPermsChanged -= OnAdminPermsChanged;
        _sessions.Clear();
    }

    /// <summary>
    ///     Toggles debugging for <paramref name="session"/>. The same scope again turns it off; anything else switches to
    ///         that scope. Returns the resulting state.
    /// </summary>
    public (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        if (_sessions.TryGetValue(session, out var currentTarget) && currentTarget == target)
        {
            _sessions.Remove(session);
            _sendState(session, false, null);
            return (false, target);
        }

        _sessions[session] = target;
        _sendState(session, true, target);
        return (true, target);
    }

    /// <summary>
    ///     Whether anyone is watching everything, or <paramref name="owner"/> in particular.
    /// </summary>
    public bool IsWatching(EntityUid owner)
    {
        foreach (var target in _sessions.Values)
        {
            if (Matches(target, owner))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether anyone is watching everything, or <paramref name="owner"/> or one of its <paramref name="members"/>.
    /// </summary>
    public bool IsWatching(EntityUid owner, List<EntityUid> members)
    {
        foreach (var target in _sessions.Values)
        {
            if (Matches(target, owner, members))
                return true;
        }

        return false;
    }

    public static bool Matches(EntityUid? target, EntityUid owner)
    {
        return target is null || target == owner;
    }

    public static bool Matches(EntityUid? target, EntityUid owner, List<EntityUid> members)
    {
        return target is not { } targetUid || targetUid == owner || members.Contains(targetUid);
    }

    /// <summary>
    ///     Sessions survive a round restart, so tell each client to drop its overlay rather than leave it showing the last
    ///         round's data.
    /// </summary>
    public void ClearAndNotify()
    {
        foreach (var session in _sessions.Keys)
        {
            _sendState(session, false, null);
        }

        _sessions.Clear();
    }

    /// <summary>
    ///     Deadminned, or no longer allowed the command that turned this on: it stops, rather than streaming on to them.
    /// </summary>
    private void OnAdminPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (_adminManager.HasAdminFlag(args.Player, AdminFlags.Debug) || !_sessions.Remove(args.Player))
            return;

        _sendState(args.Player, false, null);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _sessions.Remove(e.Session);
    }
}
