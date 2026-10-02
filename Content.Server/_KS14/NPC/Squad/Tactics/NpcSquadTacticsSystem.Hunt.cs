using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Hunting a hostile the squad has lost. A hostile is only truly lost once this has run its course: watched for,
///         gone after, and searched for everywhere it could be, without being found.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    /// <summary>
    ///     How close to the spot it was sent to a member has to be for that to count as there.
    /// </summary>
    private const float OrderRange = 1f;

    /// <summary>
    ///     How close to a turn on its way round the room a member has to get before it is sent on to the next. A
    ///         little short of arriving, so it rounds the corner instead of stopping at it.
    /// </summary>
    private const float WaypointAdvanceRange = 2f;

    /// <summary>
    ///     How close to a turn a member is sent: it is moved on before getting there anyway.
    /// </summary>
    private const float WaypointOrderRange = 1.5f;

    private static readonly Vector2i[] CardinalOffsets =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    ];

    private readonly List<EntityUid> _freeMembers = new();

    /// <summary>
    ///     Starts hunting whichever hostile a member lost most recently, if any was lost recently enough to go after.
    /// </summary>
    private bool TryStartHunt(EntityUid issuerUid, NpcSquadTacticsSettings settings, TimeSpan now, [NotNullWhen(true)] out NpcHunt? hunt)
    {
        hunt = null;

        EntityUid? spotterUid = null;
        EntityUid targetUid = default;
        var latest = default(Perception.NpcContact);

        foreach (var memberUid in _members)
        {
            if (!_npcPerceptionSystem.TryGetLatestLostContact(memberUid, settings.HuntStartAge, out var lostUid, out var contact) ||
                spotterUid != null && contact.LastSeen <= latest.LastSeen)
                continue;

            spotterUid = memberUid;
            targetUid = lostUid;
            latest = contact;
        }

        if (spotterUid is not { } spotter)
            return false;

        if (!_npcPerceptionSystem.TryGetPredictedCoordinates(spotter, targetUid, out var predictedCoordinates))
            predictedCoordinates = latest.LastKnownCoordinates;

        hunt = new NpcHunt
        {
            TargetUid = targetUid,
            Settings = settings,
            LastKnownCoordinates = latest.LastKnownCoordinates,
            PredictedCoordinates = predictedCoordinates,
            StartedAt = now,
            PhaseStartedAt = now,
        };

        EnsureComp<NpcHuntComponent>(issuerUid).Hunt = hunt;
        return true;
    }

    /// <summary>
    ///     Starts hunting the disturbance the squad last heard of, if whoever decides is cautious enough: the leader,
    ///         or an NPC on its own. Below that, the squad just goes to it, as it always has.
    /// </summary>
    private bool TryStartDisturbanceHunt(EntityUid issuerUid,
        EntityUid? leaderUid,
        NpcSquadTacticsSettings settings,
        TimeSpan now,
        [NotNullWhen(true)] out NpcHunt? hunt)
    {
        hunt = null;

        if (settings.CautionMeter is not { } cautionMeter ||
            !_pendingDisturbanceQuery.TryComp(issuerUid, out var disturbanceComponent) ||
            now - disturbanceComponent.ReportedAt > settings.HuntStartAge ||
            TerminatingOrDeleted(disturbanceComponent.Coordinates.EntityId) ||
            _npcMeterSystem.GetValue(leaderUid ?? issuerUid, cautionMeter) < settings.CautiousHuntThreshold)
            return false;

        hunt = new NpcHunt
        {
            TargetUid = null,
            Settings = settings,
            LastKnownCoordinates = disturbanceComponent.Coordinates,
            PredictedCoordinates = disturbanceComponent.Coordinates,
            StartedAt = now,
            PhaseStartedAt = now,
        };

        RemComp<NpcPendingDisturbanceComponent>(issuerUid);
        EnsureComp<NpcHuntComponent>(issuerUid).Hunt = hunt;
        return true;
    }

    private void EndHunt(EntityUid issuerUid)
    {
        if (_huntQuery.HasComp(issuerUid))
        {
            RemComp<NpcHuntComponent>(issuerUid);

            if (_npcHuntDebugSystem.IsTracking(issuerUid, _members))
                _npcHuntDebugSystem.SendEnded(issuerUid, _members);
        }

        ClearOrders(issuerUid, _members);
    }

    /// <summary>
    ///     Moves the hunt along. Each phase that finishes falls through to the next in the same update, so nobody
    ///         stands about for half a second between being told to stop waiting and being told what to do next.
    /// </summary>
    private void UpdateHunt(EntityUid issuerUid, EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (hunt.Phase != NpcHuntPhase.Exhausted && now - hunt.StartedAt >= settings.HuntTimeout)
            Exhaust(leaderUid, hunt, now);

        if (hunt.Phase == NpcHuntPhase.Watch)
        {
            // Nobody to watch for after a disturbance: straight on to going in.
            if (hunt.TargetUid is { } targetUid && now - hunt.PhaseStartedAt < settings.WatchTime)
            {
                IssueWatchOrders(issuerUid, hunt, targetUid, now);
                return;
            }

            PrepareSearch(leaderUid, hunt, settings, now);
        }

        // Whatever phase it is in from here on, what members can see of the room is seen.
        if (hunt.Phase != NpcHuntPhase.Exhausted)
            UpdateCoverage(hunt, settings);

        if (hunt.Phase == NpcHuntPhase.Stage && !UpdateStage(issuerUid, hunt, settings, now))
            return;

        if (hunt.Phase == NpcHuntPhase.Breach && !UpdateBreach(issuerUid, hunt, settings, now))
            return;

        if (hunt.Phase == NpcHuntPhase.Search && !UpdateSearch(issuerUid, leaderUid, hunt, settings, now))
            return;

        if (now - hunt.PhaseStartedAt >= settings.HoldAreaTime)
        {
            EndHunt(issuerUid);
            return;
        }

        foreach (var memberUid in _members)
        {
            SetOrder(memberUid, issuerUid, NpcOrderKind.HoldArea, hunt.PredictedCoordinates, Angle.Zero, OrderRange, null, now);
        }
    }

    private void SetPhase(NpcHunt hunt, NpcHuntPhase phase, TimeSpan now)
    {
        hunt.Phase = phase;
        hunt.PhaseStartedAt = now;
    }

    #region Watch

    /// <summary>
    ///     Right after losing the hostile: a member that was on the move when it lost sight carries on after it, to
    ///         where it should be by now. One that was holding stays put, facing that way, in case it peeks back out.
    ///         One that never saw it carries on with whatever it was doing.
    /// </summary>
    private void IssueWatchOrders(EntityUid issuerUid, NpcHunt hunt, EntityUid targetUid, TimeSpan now)
    {
        var lastKnownMapCoordinates = _transformSystem.ToMapCoordinates(hunt.LastKnownCoordinates);
        var predictedMapCoordinates = _transformSystem.ToMapCoordinates(hunt.PredictedCoordinates);

        foreach (var memberUid in _members)
        {
            if (!_npcPerceptionSystem.TryGetContact(memberUid, targetUid, out var contact) ||
                contact.State != NpcContactState.Lost)
            {
                ClearOrder(memberUid, issuerUid);
                continue;
            }

            var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
            var towardsPredicted = GetFacing(memberMapCoordinates, predictedMapCoordinates, _transformSystem.GetWorldRotation(memberUid));

            if (contact.ObserverWasMoving)
            {
                // Keep facing the way it ran, once there.
                var facing = GetFacing(lastKnownMapCoordinates, predictedMapCoordinates, towardsPredicted);
                SetOrder(memberUid,
                    issuerUid,
                    NpcOrderKind.Investigate,
                    hunt.PredictedCoordinates,
                    ToLocalFacing(hunt.PredictedCoordinates, facing),
                    OrderRange,
                    null,
                    now);
                continue;
            }

            var standCoordinates = _transformSystem.GetMoverCoordinates(memberUid);
            SetOrder(memberUid,
                issuerUid,
                NpcOrderKind.Watch,
                standCoordinates,
                ToLocalFacing(standCoordinates, towardsPredicted),
                OrderRange,
                null,
                now);
        }
    }

    #endregion

    #region Stage and breach

    /// <summary>
    ///     Spreads members across the ways in, so that with several they come at it from more than one side at once,
    ///         each the cheapest pairing first. The cost is the walk round the room - a way in a member cannot reach
    ///         without crossing the room is not one it is given: stacking up on the far door by walking past the hostile
    ///         to get there gives the game away - plus how far the way in is from where the hostile should be, weighted
    ///         by <see cref="NpcSquadTacticsSettings.EntranceTargetPreference"/>, so the ways in nearest it are taken
    ///         first. Members left over once every reachable way in has someone stack up on whichever costs least.
    /// </summary>
    private void AssignEntrances(NpcHunt hunt, NpcSquadTacticsSettings settings)
    {
        hunt.StagedMembers.Clear();

        if (hunt.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);

        // Rooms and doorways are what a route round the room must keep off.
        var avoidTiles = new HashSet<Vector2i>(hunt.RoomTiles);
        avoidTiles.UnionWith(hunt.ThresholdTiles);

        // Every member's way round to every entrance, once: a handful of each, and only when a hunt sets up.
        var routes = new (List<Vector2i> Waypoints, int Length)?[_members.Count, hunt.Entrances.Count];

        // How far in from each way in the hostile should be, counted as extra walking.
        var entranceCosts = new float[hunt.Entrances.Count];
        for (var e = 0; e < hunt.Entrances.Count; e++)
        {
            if (hunt.Entrances[e].BreachCoordinates.TryDistance(EntityManager, _transformSystem, hunt.PredictedCoordinates, out var toTarget))
                entranceCosts[e] = toTarget * settings.EntranceTargetPreference;
        }

        for (var m = 0; m < _members.Count; m++)
        {
            var memberTransform = Transform(_members[m]);
            if (memberTransform.GridUid != gridUid)
                continue;

            var memberTile = _mapSystem.TileIndicesFor(grid, memberTransform.Coordinates);

            for (var e = 0; e < hunt.Entrances.Count; e++)
            {
                var waypoints = new List<Vector2i>();
                if (_npcSquadCoverSystem.TryFindRouteAround(_members[m],
                        grid,
                        memberTile,
                        hunt.Entrances[e].StageTile,
                        avoidTiles,
                        settings.MaxStageDistance,
                        waypoints,
                        out var length))
                    routes[m, e] = (waypoints, length);
            }
        }

        _freeMembers.Clear();
        for (var m = 0; m < _members.Count; m++)
        {
            _freeMembers.Add(_members[m]);
        }

        while (_freeMembers.Count > 0)
        {
            if (!TryPickEntrance(hunt, routes, entranceCosts, unusedOnly: true, out var memberIndex, out var entranceIndex) &&
                !TryPickEntrance(hunt, routes, entranceCosts, unusedOnly: false, out memberIndex, out entranceIndex))
                break;

            var staging = new NpcHuntStaging { EntranceIndex = entranceIndex };
            staging.Waypoints.AddRange(routes[_members.IndexOf(_freeMembers[memberIndex]), entranceIndex]!.Value.Waypoints);

            hunt.StagedMembers[_freeMembers[memberIndex]] = staging;
            _freeMembers.RemoveAt(memberIndex);
        }
    }

    /// <summary>
    ///     The cheapest pairing, among members still to be placed, with a way in they can reach round the room: the
    ///         walk, plus <paramref name="entranceCosts"/> for the way in.
    /// </summary>
    private bool TryPickEntrance(NpcHunt hunt,
        (List<Vector2i> Waypoints, int Length)?[,] routes,
        float[] entranceCosts,
        bool unusedOnly,
        out int memberIndex,
        out int entranceIndex)
    {
        memberIndex = -1;
        entranceIndex = -1;
        var bestCost = float.MaxValue;

        for (var e = 0; e < hunt.Entrances.Count; e++)
        {
            if (unusedOnly && IsEntranceUsed(hunt, e))
                continue;

            for (var f = 0; f < _freeMembers.Count; f++)
            {
                if (routes[_members.IndexOf(_freeMembers[f]), e] is not { } route)
                    continue;

                var cost = route.Length + entranceCosts[e];
                if (cost >= bestCost)
                    continue;

                bestCost = cost;
                memberIndex = f;
                entranceIndex = e;
            }
        }

        return memberIndex >= 0;
    }

    private static bool IsEntranceUsed(NpcHunt hunt, int entranceIndex)
    {
        foreach (var staging in hunt.StagedMembers.Values)
        {
            if (staging.EntranceIndex == entranceIndex)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Members make their way round to their spots outside the room, turn by turn, and wait there. Once everyone is
    ///         in place - or they have waited long enough for anyone who is not - they all go in at once. Returns
    ///         whether they do.
    /// </summary>
    private bool UpdateStage(EntityUid issuerUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var everyoneInPlace = true;

        foreach (var memberUid in _members)
        {
            if (!hunt.StagedMembers.TryGetValue(memberUid, out var staging) || hunt.GridUid is not { } gridUid)
            {
                // Joined since, or no way round to any way in: it searches once the rest go in.
                ClearOrder(memberUid, issuerUid);
                continue;
            }

            var entrance = hunt.Entrances[staging.EntranceIndex];
            var memberCoordinates = Transform(memberUid).Coordinates;

            AdvanceStaging(staging, gridUid, memberCoordinates, entrance.StageCoordinates, settings);

            if (staging.NextWaypoint < staging.Waypoints.Count)
            {
                // On the way round: to the next turn, facing on along the way.
                var turnTile = staging.Waypoints[staging.NextWaypoint];
                var onwards = staging.NextWaypoint + 1 < staging.Waypoints.Count
                    ? GetTileCoordinates(gridUid, staging.Waypoints[staging.NextWaypoint + 1]).Position
                    : entrance.StageCoordinates.Position;
                var turnCoordinates = GetTileCoordinates(gridUid, turnTile);

                SetOrder(memberUid,
                    issuerUid,
                    NpcOrderKind.Stage,
                    turnCoordinates,
                    (onwards - turnCoordinates.Position).ToWorldAngle(),
                    WaypointOrderRange,
                    null,
                    now);

                everyoneInPlace = false;
                continue;
            }

            SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, entrance.StageCoordinates, entrance.LocalFacing, OrderRange, null, now);

            everyoneInPlace &= memberCoordinates.TryDistance(EntityManager, _transformSystem, entrance.StageCoordinates, out var distance) &&
                distance <= settings.StageArriveRange;
        }

        if (!everyoneInPlace && now - hunt.PhaseStartedAt < settings.StageTimeout)
            return false;

        SetPhase(hunt, NpcHuntPhase.Breach, now);
        return true;
    }

    /// <summary>
    ///     Moves a staging member on past every turn it no longer needs: all of them once it is at its waiting spot,
    ///         however it got there, and otherwise every turn up to the furthest one it has got close to. Never back:
    ///         a member that has got ahead of its route - pushed along, or taking its own way - is not sent back to a
    ///         turn behind it.
    /// </summary>
    private void AdvanceStaging(NpcHuntStaging staging,
        EntityUid gridUid,
        EntityCoordinates memberCoordinates,
        EntityCoordinates stageCoordinates,
        NpcSquadTacticsSettings settings)
    {
        if (memberCoordinates.TryDistance(EntityManager, _transformSystem, stageCoordinates, out var toSpot) &&
            toSpot <= settings.StageArriveRange)
        {
            staging.NextWaypoint = staging.Waypoints.Count;
            return;
        }

        for (var i = staging.Waypoints.Count - 1; i >= staging.NextWaypoint; i--)
        {
            if (!memberCoordinates.TryDistance(EntityManager, _transformSystem, GetTileCoordinates(gridUid, staging.Waypoints[i]), out var toTurn) ||
                toTurn > WaypointAdvanceRange)
                continue;

            staging.NextWaypoint = i + 1;
            return;
        }
    }

    /// <summary>
    ///     Members go in through their own ways in. Once all of them are inside, or have had long enough to be, the
    ///         search starts. Returns whether it does.
    /// </summary>
    private bool UpdateBreach(EntityUid issuerUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var everyoneInside = true;

        foreach (var memberUid in _members)
        {
            if (!hunt.StagedMembers.TryGetValue(memberUid, out var staging))
                continue;

            var entrance = hunt.Entrances[staging.EntranceIndex];
            SetOrder(memberUid, issuerUid, NpcOrderKind.Breach, entrance.BreachCoordinates, entrance.LocalFacing, OrderRange, null, now);

            everyoneInside &= IsInRoom(hunt, memberUid);
        }

        if (!everyoneInside && now - hunt.PhaseStartedAt < settings.BreachTimeout)
            return false;

        SetPhase(hunt, NpcHuntPhase.Search, now);
        return true;
    }

    #endregion

    /// <summary>
    ///     Gives up on the hostile: everyone forgets it, and the squad's threat is cleared so it stands down once the
    ///         members have held the area a little while.
    /// </summary>
    private void Exhaust(EntityUid? leaderUid, NpcHunt hunt, TimeSpan now)
    {
        SetPhase(hunt, NpcHuntPhase.Exhausted, now);

        if (hunt.TargetUid is { } targetUid)
        {
            foreach (var memberUid in _members)
            {
                _npcPerceptionSystem.ForgetContact(memberUid, targetUid);
            }
        }

        if (leaderUid is { } leader)
            _npcSquadSystem.ClearThreat(leader);
    }

    /// <summary>
    ///     The centre of <paramref name="tile"/> on <paramref name="gridUid"/>, relative to the grid.
    /// </summary>
    private EntityCoordinates GetTileCoordinates(EntityUid gridUid, Vector2i tile)
    {
        return _mapGridQuery.TryComp(gridUid, out var mapGridComponent)
            ? new EntityCoordinates(gridUid, _mapSystem.TileCenterToVector((gridUid, mapGridComponent), tile))
            : new EntityCoordinates(gridUid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
    }

    private bool IsInRoom(NpcHunt hunt, EntityUid uid)
    {
        if (hunt.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return false;

        var transformComponent = Transform(uid);
        return transformComponent.GridUid == gridUid &&
            hunt.RoomTiles.Contains(_mapSystem.TileIndicesFor((gridUid, mapGridComponent), transformComponent.Coordinates));
    }
}
