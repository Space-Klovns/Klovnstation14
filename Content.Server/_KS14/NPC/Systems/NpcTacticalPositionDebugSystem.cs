using Content.Shared._KS14.NPC;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// Tracks which player sessions have <see cref="Content.Server._KS14.NPC.Commands.TacticalPositionDebugCommand"/>
/// toggled on, and lets <see cref="Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.TacticalPositionOperator"/>
/// push debug frames (candidate scores, the chosen candidate, live claims) to them.
///
/// Deliberately lazy: <see cref="AnyDebugging"/> is the gate the operator checks before doing any of the
/// extra bookkeeping (recording every candidate's score, snapshotting the claim table) needed to build a
/// debug frame - none of that work happens while nobody is watching.
/// </summary>
public sealed partial class NpcTacticalPositionDebugSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;

    private readonly HashSet<ICommonSession> _debuggingSessions = new();

    public bool AnyDebugging => _debuggingSessions.Count > 0;

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _debuggingSessions.Clear();
    }

    /// <summary>
    /// Flips debugging on/off for <paramref name="session"/> and returns the new state.
    /// </summary>
    public bool Toggle(ICommonSession session)
    {
        var enabled = !_debuggingSessions.Remove(session);

        if (enabled)
            _debuggingSessions.Add(session);

        RaiseNetworkEvent(new TacticalPositionDebugStateMessage { Enabled = enabled }, session.Channel);
        return enabled;
    }

    /// <summary>
    /// Broadcasts one debug frame to every subscribed session. Only call this when <see cref="AnyDebugging"/>
    /// is true - callers are expected to skip building the (potentially sizeable) candidate list entirely
    /// otherwise.
    /// </summary>
    public void SendDebugFrame(
        EntityUid owner,
        List<TacticalPositionDebugCandidate> candidates,
        EntityCoordinates? chosen,
        IReadOnlyList<TacticalPositionDebugClaim> claims)
    {
        if (_debuggingSessions.Count == 0)
            return;

        var message = new TacticalPositionDebugDataMessage
        {
            Owner = GetNetEntity(owner),
            Candidates = candidates,
            Chosen = chosen is { } coordinates ? GetNetCoordinates(coordinates) : null,
            Claims = new List<TacticalPositionDebugClaim>(claims),
        };

        foreach (var session in _debuggingSessions)
        {
            RaiseNetworkEvent(message, session.Channel);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Disconnected)
            return;

        _debuggingSessions.Remove(e.Session);
    }
}
