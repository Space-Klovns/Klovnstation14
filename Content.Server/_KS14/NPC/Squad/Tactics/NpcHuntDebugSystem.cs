using Content.Shared._KS14.NPC;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Tracks which players have <see cref="HuntDebugCommand"/> on - for every hunt, or one squad's - and passes them
///         what <see cref="NpcSquadTacticsSystem"/> works out for a hunt, as it works it out.
/// </summary>
/// <remarks>
///     Lazy, like the tactical position debug: nothing here runs on its own. The tactics system asks
///         <see cref="IsTracking"/> after updating a hunt, and builds a frame only if someone is watching that hunt, so
///         a hunt nobody is watching costs nothing extra.
/// </remarks>
public sealed partial class NpcHuntDebugSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    [Dependency] private EntityQuery<NpcOrderComponent> _orderQuery = default!;

    /// <summary>
    ///     Null value = every hunt; otherwise a squad, or an NPC (meaning its squad's hunt, or its own).
    /// </summary>
    private readonly Dictionary<ICommonSession, EntityUid?> _debuggingSessions = new();

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
    ///     Toggles hunt debugging for <paramref name="session"/>. The same scope again turns it off; anything else
    ///         switches to that scope. Returns the resulting state.
    /// </summary>
    public (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        if (_debuggingSessions.TryGetValue(session, out var currentTarget) && currentTarget == target)
        {
            _debuggingSessions.Remove(session);
            RaiseNetworkEvent(new HuntDebugStateMessage { Enabled = false }, session.Channel);
            return (false, target);
        }

        _debuggingSessions[session] = target;
        RaiseNetworkEvent(new HuntDebugStateMessage
        {
            Enabled = true,
            Target = target is { } uid ? GetNetEntity(uid) : null,
        }, session.Channel);
        return (true, target);
    }

    /// <summary>
    ///     Whether anyone would receive a frame for the hunt <paramref name="issuerUid"/> is on, with
    ///         <paramref name="memberUids"/> taking part. Check before building one.
    /// </summary>
    public bool IsTracking(EntityUid issuerUid, List<EntityUid> memberUids)
    {
        foreach (var target in _debuggingSessions.Values)
        {
            if (Matches(target, issuerUid, memberUids))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Sends what <paramref name="hunt"/> stands at to everyone watching it.
    /// </summary>
    public void SendFrame(EntityUid issuerUid, NpcHunt hunt, List<EntityUid> memberUids)
    {
        HuntDebugDataMessage? message = null;

        foreach (var (session, target) in _debuggingSessions)
        {
            if (!Matches(target, issuerUid, memberUids))
                continue;

            message ??= BuildFrame(issuerUid, hunt, memberUids);
            RaiseNetworkEvent(message, session.Channel);
        }
    }

    /// <summary>
    ///     Tells everyone watching that the hunt <paramref name="issuerUid"/> was on is over.
    /// </summary>
    public void SendEnded(EntityUid issuerUid, List<EntityUid> memberUids)
    {
        foreach (var (session, target) in _debuggingSessions)
        {
            if (Matches(target, issuerUid, memberUids))
                RaiseNetworkEvent(new HuntDebugEndedMessage { Issuer = GetNetEntity(issuerUid) }, session.Channel);
        }
    }

    private static bool Matches(EntityUid? target, EntityUid issuerUid, List<EntityUid> memberUids)
    {
        return target is not { } targetUid || targetUid == issuerUid || memberUids.Contains(targetUid);
    }

    private HuntDebugDataMessage BuildFrame(EntityUid issuerUid, NpcHunt hunt, List<EntityUid> memberUids)
    {
        var now = _gameTiming.CurTime;
        var settings = hunt.Settings;

        var message = new HuntDebugDataMessage
        {
            Issuer = GetNetEntity(issuerUid),
            Phase = hunt.Phase,
            PhaseElapsed = (float) (now - hunt.PhaseStartedAt).TotalSeconds,
            PhaseLimit = (float) (hunt.Phase switch
            {
                NpcHuntPhase.Watch => settings.WatchTime,
                NpcHuntPhase.Stage => settings.StageTimeout,
                NpcHuntPhase.Breach => settings.BreachTimeout,
                NpcHuntPhase.Entry => settings.EntryTimeout,
                NpcHuntPhase.Exhausted => settings.HoldAreaTime,
                _ => TimeSpan.Zero,
            }).TotalSeconds,
            HuntElapsed = (float) (now - hunt.StartedAt).TotalSeconds,
            HuntTimeout = (float) settings.HuntTimeout.TotalSeconds,
            Coverage = NpcSquadTacticsSystem.GetCoverage(hunt),
            RequiredCoverage = settings.SearchCoverage,
            LastKnown = GetNetCoordinates(Snapshot(hunt.LastKnownCoordinates)),
            Predicted = GetNetCoordinates(Snapshot(hunt.PredictedCoordinates)),
            Grid = hunt.GridUid is { } gridUid ? GetNetEntity(gridUid) : null,
        };

        message.RoomTiles.AddRange(hunt.RoomTiles);
        message.UnseenTiles.AddRange(hunt.UnseenTiles);
        message.ThresholdTiles.AddRange(hunt.ThresholdTiles);

        foreach (var entrance in hunt.Entrances)
        {
            message.Entrances.Add(new HuntDebugEntrance(GetNetCoordinates(entrance.StageCoordinates), GetNetCoordinates(entrance.EntryCoordinates)));
        }

        if (hunt.GridUid is { } routeGridUid)
        {
            foreach (var (memberUid, staging) in hunt.StagedMembers)
            {
                if (TerminatingOrDeleted(memberUid))
                    continue;

                var route = new HuntDebugRoute();
                route.Points.Add(GetNetCoordinates(Transform(memberUid).Coordinates));

                for (var i = staging.NextWaypoint; i < staging.Waypoints.Count; i++)
                {
                    var tile = staging.Waypoints[i];
                    route.Points.Add(GetNetCoordinates(GetTileCenter(routeGridUid, tile)));
                }

                route.Points.Add(GetNetCoordinates(hunt.Entrances[staging.EntranceIndex].StageCoordinates));
                message.Routes.Add(route);
            }

            foreach (var (memberUid, sweep) in hunt.Sweeps)
            {
                if (TerminatingOrDeleted(memberUid))
                    continue;

                message.Sweeps.Add(new HuntDebugLine(GetNetCoordinates(Transform(memberUid).Coordinates),
                    GetNetCoordinates(GetTileCenter(routeGridUid, sweep.Tile))));
            }
        }

        foreach (var point in hunt.SearchPoints)
        {
            if (TerminatingOrDeleted(point.Coordinates.EntityId))
                continue;

            NetCoordinates? assignee = point.AssigneeUid is { } assigneeUid && !TerminatingOrDeleted(assigneeUid)
                ? GetNetCoordinates(Transform(assigneeUid).Coordinates)
                : null;

            message.SearchPoints.Add(new HuntDebugSearchPoint(GetNetCoordinates(Snapshot(point.Coordinates)), point.StorageUid != null, point.Cleared, assignee));
        }

        foreach (var memberUid in memberUids)
        {
            if (!_orderQuery.TryComp(memberUid, out var orderComponent) ||
                orderComponent.Order is not { } order ||
                TerminatingOrDeleted(order.Coordinates.EntityId))
                continue;

            message.Orders.Add(new SquadDebugOrder(GetNetCoordinates(Transform(memberUid).Coordinates),
                GetNetCoordinates(Snapshot(order.Coordinates)),
                order.Kind));
        }

        return message;
    }

    private EntityCoordinates GetTileCenter(EntityUid gridUid, Vector2i tile)
    {
        return _mapGridQuery.TryComp(gridUid, out var mapGridComponent)
            ? new EntityCoordinates(gridUid, _mapSystem.TileCenterToVector((gridUid, mapGridComponent), tile))
            : new EntityCoordinates(gridUid, new System.Numerics.Vector2(tile.X + 0.5f, tile.Y + 0.5f));
    }

    /// <summary>
    ///     Coordinates relative to the grid or map rather than to an entity - a locker, say - the client may not have.
    /// </summary>
    private EntityCoordinates Snapshot(EntityCoordinates coordinates)
    {
        return TerminatingOrDeleted(coordinates.EntityId) ? coordinates : _transformSystem.GetMoverCoordinates(coordinates);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _debuggingSessions.Remove(e.Session);
    }

    /// <summary>
    ///     Sessions survive a round restart, so tell each client to drop its overlay rather than leave it showing the
    ///         last round's hunts.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var session in _debuggingSessions.Keys)
        {
            RaiseNetworkEvent(new HuntDebugStateMessage { Enabled = false }, session.Channel);
        }

        _debuggingSessions.Clear();
    }
}
