using Content.Shared._KS14.NPC;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// Tracks which player sessions have <see cref="Content.Server._KS14.NPC.Commands.TacticalPositionDebugCommand"/>
/// toggled on - either for every NPC using the tactical position query, or scoped to a single tracked entity -
/// and lets <see cref="Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.TacticalPositionOperator"/> push
/// debug frames (candidate scores, the chosen candidate, live claims) to them.
///
/// Deliberately lazy: <see cref="IsTracking"/> is the gate the operator checks, per-owner, before doing any of
/// the extra bookkeeping (recording every candidate's score, snapshotting the claim table) needed to build a
/// debug frame - none of that work happens for an NPC nobody is watching, including when every subscriber is
/// scoped to a different single entity.
/// </summary>
public sealed partial class NpcTacticalPositionDebugSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;

    /// <summary>
    /// Null value = tracking every entity; a concrete value = scoped to that one entity only.
    /// </summary>
    private readonly Dictionary<ICommonSession, EntityUid?> _debuggingSessions = new();

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _debuggingSessions.Clear();
    }

    /// <summary>
    /// Returns true if any subscribed session would receive a debug frame for <paramref name="owner"/> -
    /// either tracking everything, or scoped specifically to it. Callers should skip building a debug frame
    /// entirely when this is false.
    /// </summary>
    public bool IsTracking(EntityUid owner)
    {
        foreach (var target in _debuggingSessions.Values)
        {
            if (target is null || target == owner)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Toggles debugging for <paramref name="session"/>. If <paramref name="target"/> matches the session's
    /// current scope (both null, or the same entity), debugging is turned off; otherwise the session is
    /// (re)subscribed scoped to <paramref name="target"/> (null = every entity). Returns the resulting state.
    /// </summary>
    public (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        if (_debuggingSessions.TryGetValue(session, out var currentTarget) && currentTarget == target)
        {
            _debuggingSessions.Remove(session);
            RaiseNetworkEvent(new TacticalPositionDebugStateMessage { Enabled = false }, session.Channel);
            return (false, target);
        }

        _debuggingSessions[session] = target;
        RaiseNetworkEvent(new TacticalPositionDebugStateMessage
        {
            Enabled = true,
            Target = target is { } uid ? GetNetEntity(uid) : null,
        }, session.Channel);
        return (true, target);
    }

    /// <summary>
    /// Broadcasts one debug frame to every subscribed session tracking <paramref name="owner"/> (either
    /// untargeted, or scoped to it specifically). Only call this when <see cref="IsTracking"/> for that owner
    /// is true - callers are expected to skip building the (potentially sizeable) candidate list otherwise.
    /// </summary>
    public void SendDebugFrame(
        EntityUid owner,
        List<TacticalPositionDebugCandidate> candidates,
        EntityCoordinates? chosen,
        IReadOnlyList<TacticalPositionDebugClaim> claims)
    {
        if (_debuggingSessions.Count == 0)
            return;

        TacticalPositionDebugDataMessage? message = null;

        foreach (var (session, target) in _debuggingSessions)
        {
            if (target is { } specificTarget && specificTarget != owner)
                continue;

            message ??= new TacticalPositionDebugDataMessage
            {
                Owner = GetNetEntity(owner),
                Candidates = candidates,
                Chosen = chosen is { } coordinates ? GetNetCoordinates(coordinates) : null,
                Claims = new List<TacticalPositionDebugClaim>(claims),
            };

            RaiseNetworkEvent(message, session.Channel);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Disconnected)
            return;

        _debuggingSessions.Remove(e.Session);
    }

    /// <summary>
    /// Unlike <see cref="OnPlayerStatusChanged"/>, sessions stay connected across a round restart, so a silent
    /// clear here would leave their client-side overlay stuck showing stale data from the previous round -
    /// tell each of them explicitly before dropping the server-side state.
    /// </summary>
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var session in _debuggingSessions.Keys)
        {
            RaiseNetworkEvent(new TacticalPositionDebugStateMessage { Enabled = false }, session.Channel);
        }

        _debuggingSessions.Clear();
    }
}
