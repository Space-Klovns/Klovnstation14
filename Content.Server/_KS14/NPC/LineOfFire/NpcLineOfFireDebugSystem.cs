using Content.Server.NPC.Components;
using Content.Shared._KS14.NPC;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.LineOfFire;

/// <summary>
///     Tracks which players have <see cref="LineOfFireDebugCommand"/> on, and sends them how far the shot of every NPC with
///         a gun on a target would get. On its own timer, as this has nothing of its own to hook into: the ranged combat
///         update only asks yes or no. Nothing is built with nobody watching.
/// </summary>
public sealed partial class NpcLineOfFireDebugSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>
    ///     Null value = every NPC; otherwise only this one.
    /// </summary>
    private readonly Dictionary<ICommonSession, EntityUid?> _debuggingSessions = new();

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        _adminManager.OnPermsChanged += OnAdminPermsChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _adminManager.OnPermsChanged -= OnAdminPermsChanged;
        _debuggingSessions.Clear();
    }

    /// <summary>
    ///     Toggles line of fire debugging for <paramref name="session"/>. The same scope again turns it off; anything else
    ///         switches to that scope. Returns the resulting state.
    /// </summary>
    public (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        if (_debuggingSessions.TryGetValue(session, out var currentTarget) && currentTarget == target)
        {
            _debuggingSessions.Remove(session);
            RaiseNetworkEvent(new LineOfFireDebugStateMessage { Enabled = false }, session.Channel);
            return (false, target);
        }

        _debuggingSessions[session] = target;
        RaiseNetworkEvent(new LineOfFireDebugStateMessage { Enabled = true }, session.Channel);
        return (true, target);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_debuggingSessions.Count == 0)
            return;

        var now = _gameTiming.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;

        foreach (var (session, target) in _debuggingSessions)
        {
            var message = new LineOfFireDebugDataMessage();

            var enumerator = EntityQueryEnumerator<NPCRangedCombatComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out var rangedComponent, out var transformComponent))
            {
                if ((target == null || target == uid) && !TerminatingOrDeleted(rangedComponent.Target))
                    message.Shots.Add(BuildShot(uid, rangedComponent.Target, transformComponent));
            }

            RaiseNetworkEvent(message, session.Channel);
        }
    }

    private LineOfFireDebugShot BuildShot(EntityUid uid, EntityUid targetUid, TransformComponent transformComponent)
    {
        var shotMask = _npcLineOfFireSystem.GetShotMask(uid);
        var from = GetNetCoordinates(transformComponent.Coordinates);
        var to = GetNetCoordinates(_transformSystem.GetMoverCoordinates(targetUid));

        if (_npcLineOfFireSystem.TryGetFirstHit(_transformSystem.GetMapCoordinates(uid), _transformSystem.GetMapCoordinates(targetUid), shotMask, out var hit))
            return new LineOfFireDebugShot(from, GetNetCoordinates(_transformSystem.GetMoverCoordinates(hit.HitEntity)), false, shotMask.ToString(), MetaData(hit.HitEntity).EntityName);

        return new LineOfFireDebugShot(from, to, true, shotMask.ToString(), null);
    }

    /// <summary>
    ///     Deadminned, or no longer allowed the command that turned this on: it stops, rather than streaming on to them.
    /// </summary>
    private void OnAdminPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (_adminManager.HasAdminFlag(args.Player, AdminFlags.Debug) || !_debuggingSessions.Remove(args.Player))
            return;

        RaiseNetworkEvent(new LineOfFireDebugStateMessage { Enabled = false }, args.Player.Channel);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _debuggingSessions.Remove(e.Session);
    }

    /// <summary>
    ///     Sessions survive a round restart, so tell each client to drop its overlay.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var session in _debuggingSessions.Keys)
        {
            RaiseNetworkEvent(new LineOfFireDebugStateMessage { Enabled = false }, session.Channel);
        }

        _debuggingSessions.Clear();
    }
}
