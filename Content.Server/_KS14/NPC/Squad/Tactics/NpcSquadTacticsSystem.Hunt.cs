using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._KS14.NPC.Doors;
using Content.Shared._KS14.NPC;
using Content.Shared.Doors.Components;
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

    /// <summary>
    ///     How close to a door a member forcing it is sent: within reach of it, from outside.
    /// </summary>
    private const float BreachOrderRange = 1f;

    /// <summary>
    ///     How far from the spot a member holding an area may pick somewhere to stand.
    /// </summary>
    private const float HoldAreaRange = 6f;

    /// <summary>
    ///     The same, shut out of it. The spot is as close as they can get, and a member should end up there and not just near it.
    /// </summary>
    private const float ShutOutHoldRange = 3f;

    /// <summary>
    ///     How fast members get to a spot outside a room, in tiles a second: at a walk, and not as a plan reckons a sprint.
    /// </summary>
    private const float ShutOutWalkSpeed = 2.5f;

    /// <summary>
    ///     How much further the way there is than it looks.
    /// </summary>
    private const float ShutOutWalkDetour = 1.5f;

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

        if (!_pendingDisturbanceQuery.TryComp(issuerUid, out var disturbanceComponent) ||
            now - disturbanceComponent.ReportedAt > settings.HuntStartAge ||
            TerminatingOrDeleted(disturbanceComponent.Coordinates.EntityId))
            return false;

        var newHunt = new NpcHunt
        {
            TargetUid = null,
            Settings = settings,
            LastKnownCoordinates = disturbanceComponent.Coordinates,
            PredictedCoordinates = disturbanceComponent.Coordinates,
            StartedAt = now,
            PhaseStartedAt = now,
        };

        var cautious = settings.CautionMeter is { } cautionMeter &&
            _npcMeterSystem.GetValue(leaderUid ?? issuerUid, cautionMeter) >= settings.CautiousHuntThreshold;

        // Going to it as it always has is no good if there is no way there: that fails to plan, and it stands there. So however
        //      bold the squad is, that needs seeing to. Only looked at once per thing it hears: it waits here until it is stale.
        if (!cautious && (disturbanceComponent.CheckedReachable || !CheckCutOff(disturbanceComponent, newHunt, leaderUid ?? _members[0])))
            return false;

        hunt = newHunt;

        RemComp<NpcPendingDisturbanceComponent>(issuerUid);
        EnsureComp<NpcHuntComponent>(issuerUid).Hunt = hunt;
        return true;
    }

    private bool CheckCutOff(NpcPendingDisturbanceComponent disturbanceComponent, NpcHunt hunt, EntityUid walkerUid)
    {
        disturbanceComponent.CheckedReachable = true;
        return TryPrepareSealedRoom(hunt, walkerUid, disturbanceComponent.Coordinates);
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

        if (hunt.Phase == NpcHuntPhase.Blast && !UpdateBlast(issuerUid, leaderUid, hunt, settings, now))
            return;

        if (hunt.Phase == NpcHuntPhase.Stage && !UpdateStage(issuerUid, leaderUid, hunt, settings, now))
            return;

        if (hunt.Phase == NpcHuntPhase.Breach && !UpdateBreach(issuerUid, hunt, settings, now))
            return;

        if (hunt.Phase == NpcHuntPhase.Entry && !UpdateEntry(issuerUid, leaderUid, hunt, settings, now))
            return;

        if (hunt.Phase == NpcHuntPhase.Search && !UpdateSearch(issuerUid, leaderUid, hunt, settings, now))
            return;

        if (now - hunt.PhaseStartedAt >= settings.HoldAreaTime + hunt.ShutOutWalk)
        {
            EndHunt(issuerUid);
            return;
        }

        foreach (var memberUid in _members)
        {
            var coordinates = !hunt.ShutOut
                ? hunt.PredictedCoordinates
                : hunt.ShutOutCoordinates ?? _transformSystem.GetMoverCoordinates(memberUid);

            SetOrder(memberUid, issuerUid, NpcOrderKind.HoldArea, coordinates, Angle.Zero, hunt.ShutOut ? ShutOutHoldRange : HoldAreaRange, null, now);
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

    #region Stage and entry

    /// <summary>
    ///     Spreads members across the ways in, so that with several they come at it from more than one side at once,
    ///         each the cheapest pairing first. The cost is the walk round the room - a way in a member cannot reach
    ///         without crossing the room is not one it is given: stacking up on the far door by walking past the hostile
    ///         to get there gives the game away - plus how far the way in is from where the hostile should be, weighted
    ///         by <see cref="NpcSquadTacticsSettings.EntranceTargetPreference"/>, so the ways in nearest it are taken
    ///         first.
    ///     <para>
    ///         Doors decide who can lead where. Each way in first gets a lead: a member that can get its door open, by
    ///             hand (it believes it has the access, see <see cref="NpcDoorSystem"/>) or by forcing it with something
    ///             it carries. A door nobody can get through is nobody's to stack up on. Everyone left over - including
    ///             members who can open nothing themselves - stacks up behind whichever lead costs least.
    ///     </para>
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
            if (hunt.Entrances[e].EntryCoordinates.TryDistance(EntityManager, _transformSystem, hunt.PredictedCoordinates, out var toTarget))
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

        // How each member would get through each way in, if at all.
        var methods = new NpcEntryMethod[_members.Count, hunt.Entrances.Count];
        for (var m = 0; m < _members.Count; m++)
        {
            for (var e = 0; e < hunt.Entrances.Count; e++)
            {
                methods[m, e] = GetEntryMethod(hunt, _members[m], hunt.Entrances[e]);
            }
        }

        _freeMembers.Clear();
        for (var m = 0; m < _members.Count; m++)
        {
            _freeMembers.Add(_members[m]);
        }

        while (_freeMembers.Count > 0)
        {
            var isLead = TryPickEntrance(hunt, routes, entranceCosts, methods, leadsOnly: true, out var memberIndex, out var entranceIndex);
            if (!isLead && !TryPickEntrance(hunt, routes, entranceCosts, methods, leadsOnly: false, out memberIndex, out entranceIndex))
                break;

            var m = _members.IndexOf(_freeMembers[memberIndex]);
            var staging = new NpcHuntStaging
            {
                EntranceIndex = entranceIndex,
                IsLead = isLead,
                Method = isLead ? methods[m, entranceIndex].Method : NpcBreachMethod.None,
                ToolUid = isLead ? methods[m, entranceIndex].ToolUid : null,
                RouteLength = routes[m, entranceIndex]!.Value.Length,
            };
            staging.Waypoints.AddRange(routes[m, entranceIndex]!.Value.Waypoints);

            hunt.StagedMembers[_freeMembers[memberIndex]] = staging;
            _freeMembers.RemoveAt(memberIndex);
        }
    }

    /// <summary>
    ///     How <paramref name="memberUid"/> would get through <paramref name="entrance"/>: walk through (no door, or one
    ///         open), open it by hand, or force it with something it carries - or not at all.
    /// </summary>
    private NpcEntryMethod GetEntryMethod(NpcHunt hunt, EntityUid memberUid, NpcHuntEntrance entrance)
    {
        if (entrance.DoorUid is not { } doorUid || _npcDoorSystem.GetDoorAccess(memberUid, doorUid) != NpcDoorAccess.Locked)
            return new NpcEntryMethod(Usable: true, NpcBreachMethod.None, ToolUid: null);

        if (_npcDoorSystem.TryGetBreachTool(memberUid, doorUid, out var toolUid, out var method))
            return new NpcEntryMethod(Usable: true, method, toolUid);

        return hunt.AllowCharges && _npcChargeSystem.TryGetCharge(memberUid, doorUid, out var charge)
            ? new NpcEntryMethod(Usable: true, NpcBreachMethod.Charge, charge.ChargeUid)
            : default;
    }

    /// <summary>
    ///     How one member would get through one way in. See <see cref="GetEntryMethod"/>.
    /// </summary>
    private readonly record struct NpcEntryMethod(bool Usable, NpcBreachMethod Method, EntityUid? ToolUid);

    /// <summary>
    ///     The cheapest pairing, among members still to be placed, with a way in they can reach round the room: the
    ///         walk, plus <paramref name="entranceCosts"/> for the way in. With <paramref name="leadsOnly"/>, a way in
    ///         nobody leads yet, for a member that can get through it; otherwise, a way in somebody already leads,
    ///         for anyone, to stack up behind them.
    /// </summary>
    private bool TryPickEntrance(NpcHunt hunt,
        (List<Vector2i> Waypoints, int Length)?[,] routes,
        float[] entranceCosts,
        NpcEntryMethod[,] methods,
        bool leadsOnly,
        out int memberIndex,
        out int entranceIndex)
    {
        memberIndex = -1;
        entranceIndex = -1;
        var bestCost = float.MaxValue;

        for (var e = 0; e < hunt.Entrances.Count; e++)
        {
            // Everyone placed so far leads, so a way in in use is one with a lead.
            if (IsEntranceUsed(hunt, e) == leadsOnly)
                continue;

            for (var f = 0; f < _freeMembers.Count; f++)
            {
                var m = _members.IndexOf(_freeMembers[f]);
                if (routes[m, e] is not { } route || leadsOnly && !methods[m, e].Usable)
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
    private bool UpdateStage(EntityUid issuerUid, EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (TryReassignForDoors(leaderUid, hunt, settings, now))
            return false;

        var everyoneInPlace = true;

        foreach (var memberUid in _members)
        {
            if (memberUid == hunt.HeldBackUid)
                continue;

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

        SetPhase(hunt, AnyDoorToForce(hunt) ? NpcHuntPhase.Breach : NpcHuntPhase.Entry, now);
        return true;
    }

    /// <summary>
    ///     A lead's door turned out not to open for it after all - it was refused, and now knows the door for a no-go (see
    ///         <see cref="NpcDoorSystem.ReportRefused"/>) - so the ways in are handed out again, once a hunt: a squadmate
    ///         with a tool, or another door, gets a go. If none is near enough to stack up on - the others only reached
    ///         through another room, say - but one can still be got through, they go in and search, each making its own
    ///         way there. Only with no way in left at all does the hunt give up. Returns whether it handed them out
    ///         again.
    /// </summary>
    private bool TryReassignForDoors(EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (hunt.ReassignedForDoors)
            return false;

        var refused = false;
        foreach (var (memberUid, staging) in hunt.StagedMembers)
        {
            // A lead already inside is through, whatever its door does now.
            if (staging.IsLead &&
                staging.Method == NpcBreachMethod.None &&
                !IsInRoom(hunt, memberUid) &&
                hunt.Entrances[staging.EntranceIndex].DoorUid is { } doorUid &&
                _npcDoorSystem.GetDoorAccess(memberUid, doorUid) == NpcDoorAccess.Locked)
            {
                refused = true;
                break;
            }
        }

        if (!refused)
            return false;

        hunt.ReassignedForDoors = true;
        AssignEntrances(hunt, settings);

        if (hunt.StagedMembers.Count > 0)
            SetPhase(hunt, NpcHuntPhase.Stage, now);
        else if (CanAnyoneGetIn(hunt))
            SetPhase(hunt, NpcHuntPhase.Search, now);
        else
            ExhaustShutOut(leaderUid, hunt, settings, now);

        return true;
    }

    /// <summary>
    ///     Leads force their doors - each with what it was given, put away again after - while everyone else waits where
    ///         they stand. Once every forced door is open, or they have tried long enough, they all go in. A door part way
    ///         through being forced holds that off: going in would call its lead away from it. Returns whether they do.
    /// </summary>
    private bool UpdateBreach(EntityUid issuerUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var allForced = true;
        var breaching = false;

        foreach (var memberUid in _members)
        {
            if (!hunt.StagedMembers.TryGetValue(memberUid, out var staging))
                continue;

            var entrance = hunt.Entrances[staging.EntranceIndex];

            if (GetDoorToForce(staging, entrance) is { } doorUid)
            {
                breaching |= _npcDoorSystem.IsBreaching(memberUid, doorUid);

                SetOrder(memberUid,
                    issuerUid,
                    NpcOrderKind.Breach,
                    Transform(doorUid).Coordinates,
                    entrance.LocalFacing,
                    BreachOrderRange,
                    doorUid,
                    now,
                    toolUid: staging.ToolUid);

                allForced = false;
                continue;
            }

            SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, entrance.StageCoordinates, entrance.LocalFacing, OrderRange, null, now);
        }

        if (!allForced && (breaching || now - hunt.PhaseStartedAt < settings.BreachTimeout))
            return false;

        SetPhase(hunt, NpcHuntPhase.Entry, now);
        return true;
    }

    private bool AnyDoorToForce(NpcHunt hunt)
    {
        foreach (var staging in hunt.StagedMembers.Values)
        {
            if (GetDoorToForce(staging, hunt.Entrances[staging.EntranceIndex]) != null)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The door <paramref name="staging"/>'s member has to force, if it leads its way in with a tool and the door is
    ///         still shut.
    /// </summary>
    private EntityUid? GetDoorToForce(NpcHuntStaging staging, NpcHuntEntrance entrance)
    {
        if (!staging.IsLead ||
            staging.Method is NpcBreachMethod.None or NpcBreachMethod.Charge ||
            entrance.DoorUid is not { } doorUid ||
            !_doorQuery.TryComp(doorUid, out var doorComponent) ||
            doorComponent.State is DoorState.Open or DoorState.Opening)
            return null;

        return doorUid;
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
    private bool UpdateEntry(EntityUid issuerUid, EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (TryReassignForDoors(leaderUid, hunt, settings, now))
            return false;

        var everyoneInside = true;
        var breaching = false;

        foreach (var memberUid in _members)
        {
            if (!hunt.StagedMembers.TryGetValue(memberUid, out var staging))
                continue;

            var entrance = hunt.Entrances[staging.EntranceIndex];
            SetOrder(memberUid, issuerUid, NpcOrderKind.Enter, entrance.EntryCoordinates, entrance.LocalFacing, OrderRange, null, now);

            everyoneInside &= IsInRoom(hunt, memberUid);

            // Forcing its way in, as steering does on a door nobody was sent to force. A new order would stop it part way.
            breaching |= _npcDoorSystem.IsBreaching(memberUid);
        }

        if (!everyoneInside && (breaching || now - hunt.PhaseStartedAt < settings.EntryTimeout))
            return false;

        SetPhase(hunt, NpcHuntPhase.Search, now);
        return true;
    }

    #endregion

    /// <summary>
    ///     Gives up on a hostile there is no getting at, and says where to wait for it instead: as near as they can get, which
    ///         is the nearest way in, or the outside of a wall if none of those is any use.
    /// </summary>
    private void ExhaustShutOut(EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (TryStartBlast(hunt, settings, now))
            return;

        Exhaust(leaderUid, hunt, now);
        hunt.ShutOut = true;

        var walkerUid = leaderUid ?? _members[0];
        var walkerCoordinates = Transform(walkerUid).Coordinates;
        var nearest = float.MaxValue;

        foreach (var entrance in hunt.Entrances)
        {
            if (!walkerCoordinates.TryDistance(EntityManager, _transformSystem, entrance.StageCoordinates, out var distance) || distance >= nearest)
                continue;

            nearest = distance;
            hunt.ShutOutCoordinates = entrance.StageCoordinates;
        }

        if (hunt.ShutOutCoordinates == null && TryFindNearestOutside(hunt, walkerUid, settings, out var coordinates, out _))
            hunt.ShutOutCoordinates = coordinates;

        if (hunt.ShutOutCoordinates is not { } spot)
            return;

        // Everyone gets there before the hold starts to count, so it goes by whoever has furthest to go.
        var furthest = 0f;

        foreach (var memberUid in _members)
        {
            if (Transform(memberUid).Coordinates.TryDistance(EntityManager, _transformSystem, spot, out var distance))
                furthest = Math.Max(furthest, distance);
        }

        hunt.ShutOutWalk = TimeSpan.FromSeconds(furthest * ShutOutWalkDetour / ShutOutWalkSpeed);
    }

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
