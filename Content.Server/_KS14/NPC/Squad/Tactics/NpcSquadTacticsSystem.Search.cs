using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.NPC;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Searching where a lost hostile went. Two kinds of looking: going to the lockers it could be in, and where it was,
///         one at a time; and sweeping the room, until every tile of its floor has been seen from close enough. The
///         sweep is what finds someone tucked round the corner of an L-shaped room, out of sight of the door.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    /// <summary>
    ///     How close to a locker a member is sent: near enough to open it, without trying to stand where it is.
    /// </summary>
    private const float LockerOrderRange = 1.2f;

    /// <summary>
    ///     Two search spots closer together than this, in tiles, are one spot.
    /// </summary>
    private const float SearchPointSpacing = 1.5f;

    /// <summary>
    ///     How far apart, in tiles, members sweeping the room try to keep the tiles they head for, so they spread out
    ///         rather than all walking to the same corner.
    /// </summary>
    private const float SweepSpacing = 4f;

    private readonly HashSet<Entity<EntityStorageComponent>> _nearbyStorages = new();
    private readonly List<(EntityUid StorageUid, float DistanceSquared)> _lockers = new();
    private readonly List<Vector2i> _newlySeenTiles = new();

    #region Setting up

    /// <summary>
    ///     The watch is over and the hostile has not shown itself: work out where it went - which room, and the ways
    ///         into it - and what in there needs checking, then either stack up outside or go straight in.
    /// </summary>
    private void PrepareSearch(EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var walkerUid = leaderUid ?? _members[0];
        var walkerCoordinates = Transform(walkerUid).Coordinates;
        var coverSettings = _squadMemberQuery.TryComp(walkerUid, out var squadMemberComponent) ? squadMemberComponent.Cover : new NpcSquadCoverSettings();

        var room = new NpcSquadCoverPlan();
        var foundRoom = _npcSquadCoverSystem.TryFindRoom(walkerUid, hunt.PredictedCoordinates, walkerCoordinates, coverSettings, room) ||
            _npcSquadCoverSystem.TryFindRoom(walkerUid, hunt.LastKnownCoordinates, walkerCoordinates, coverSettings, room);

        Entity<MapGridComponent>? grid = null;

        if (foundRoom && _mapGridQuery.TryComp(room.GridUid, out var mapGridComponent))
        {
            grid = (room.GridUid, mapGridComponent);
            hunt.GridUid = room.GridUid;
            hunt.RoomTiles.UnionWith(room.RoomTiles);
            hunt.UnseenTiles.UnionWith(room.RoomTiles);

            foreach (var threshold in room.Thresholds)
            {
                hunt.ThresholdTiles.UnionWith(threshold.Tiles);

                var stagePosition = threshold.Center - threshold.InwardNormal * settings.StageDistance;
                hunt.Entrances.Add(new NpcHuntEntrance(
                    new EntityCoordinates(room.GridUid, stagePosition),
                    new EntityCoordinates(room.GridUid, threshold.Center + threshold.InwardNormal * settings.BreachDepth),
                    _mapSystem.TileIndicesFor(grid.Value, new EntityCoordinates(room.GridUid, stagePosition)),
                    threshold.InwardNormal.ToWorldAngle()));
            }
        }

        BuildSearchPoints(walkerUid, hunt, grid, settings);

        var anyoneInside = false;
        foreach (var memberUid in _members)
        {
            anyoneInside |= IsInRoom(hunt, memberUid);
        }

        // Stacking up is for a team going into a room they are not in yet. Anyone already inside, or anyone on their
        //      own, just starts looking.
        if (settings.CanBreach && _members.Count >= 2 && hunt.Entrances.Count > 0 && !anyoneInside)
        {
            SetPhase(hunt, NpcHuntPhase.Stage, now);
            AssignEntrances(hunt, settings);

            if (hunt.StagedMembers.Count > 0)
                return;
        }

        SetPhase(hunt, NpcHuntPhase.Search, now);
    }

    /// <summary>
    ///     The spots worth checking one at a time, most likely first: lockers anyone believes it is in, then every other
    ///         closed locker it could have got into, then where it should be by now and where it was last seen. The rest
    ///         of the room is swept.
    /// </summary>
    private void BuildSearchPoints(EntityUid walkerUid, NpcHunt hunt, Entity<MapGridComponent>? grid, NpcSquadTacticsSettings settings)
    {
        var lastKnownMapCoordinates = _transformSystem.ToMapCoordinates(hunt.LastKnownCoordinates);
        var predictedMapCoordinates = _transformSystem.ToMapCoordinates(hunt.PredictedCoordinates);

        // Lockers a member saw it get into, or vanish beside.
        foreach (var memberUid in _members)
        {
            if (hunt.TargetUid is { } targetUid &&
                _npcPerceptionSystem.TryGetContact(memberUid, targetUid, out var contact) &&
                contact.State is NpcContactState.Concealed or NpcContactState.Suspected &&
                contact.ContainerUid is { } believedStorageUid)
                AddLockerPoint(hunt, believedStorageUid);
        }

        // Every closed locker in the room, or near where it was lost if it was not lost in a room.
        var searchCenter = predictedMapCoordinates;
        var searchRadius = settings.SearchRadius;

        if (grid is { } gridEntity && hunt.RoomTiles.Count > 0)
        {
            var worldMatrix = _transformSystem.GetWorldMatrix(gridEntity);
            var center = Vector2.Zero;

            foreach (var tile in hunt.RoomTiles)
            {
                center += _mapSystem.TileCenterToVector(gridEntity, tile);
            }

            center /= hunt.RoomTiles.Count;
            searchCenter = new MapCoordinates(Vector2.Transform(center, worldMatrix), predictedMapCoordinates.MapId);
            searchRadius = 0f;

            foreach (var tile in hunt.RoomTiles)
            {
                searchRadius = MathF.Max(searchRadius, (_mapSystem.TileCenterToVector(gridEntity, tile) - center).Length() + 1f);
            }
        }

        _nearbyStorages.Clear();
        _entityLookupSystem.GetEntitiesInRange(searchCenter, searchRadius, _nearbyStorages, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Uncontained);

        _lockers.Clear();
        foreach (var storage in _nearbyStorages)
        {
            if (storage.Comp.Open ||
                !_entityStorageSystem.CanOpen(walkerUid, storage.Owner, silent: true) ||
                grid is { } roomGrid && hunt.RoomTiles.Count > 0 && !IsByRoom(hunt, roomGrid, storage.Owner))
                continue;

            var storageMapPosition = _transformSystem.GetMapCoordinates(storage.Owner).Position;
            _lockers.Add((storage.Owner, (storageMapPosition - lastKnownMapCoordinates.Position).LengthSquared()));
        }

        _lockers.Sort((a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        foreach (var (storageUid, _) in _lockers)
        {
            AddLockerPoint(hunt, storageUid);
        }

        AddSpotPoint(hunt, NpcSearchPointKind.Predicted, hunt.PredictedCoordinates);
        AddSpotPoint(hunt, NpcSearchPointKind.LastKnown, hunt.LastKnownCoordinates);

        if (hunt.SearchPoints.Count > settings.MaxSearchPoints)
            hunt.SearchPoints.RemoveRange(settings.MaxSearchPoints, hunt.SearchPoints.Count - settings.MaxSearchPoints);
    }

    private void AddLockerPoint(NpcHunt hunt, EntityUid storageUid)
    {
        foreach (var point in hunt.SearchPoints)
        {
            if (point.StorageUid == storageUid)
                return;
        }

        hunt.SearchPoints.Add(new NpcHuntSearchPoint
        {
            Kind = NpcSearchPointKind.Locker,
            Coordinates = new EntityCoordinates(storageUid, Vector2.Zero),
            StorageUid = storageUid,
        });
    }

    private void AddSpotPoint(NpcHunt hunt, NpcSearchPointKind kind, EntityCoordinates coordinates)
    {
        foreach (var point in hunt.SearchPoints)
        {
            if (point.StorageUid == null &&
                point.Coordinates.TryDistance(EntityManager, _transformSystem, coordinates, out var distance) &&
                distance < SearchPointSpacing)
                return;
        }

        hunt.SearchPoints.Add(new NpcHuntSearchPoint
        {
            Kind = kind,
            Coordinates = coordinates,
        });
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is in the room or right up against it. A wall locker is on the wall's tile,
    ///         which room detection never counts as part of the room, even though the locker opens into it.
    /// </summary>
    private bool IsByRoom(NpcHunt hunt, Entity<MapGridComponent> grid, EntityUid uid)
    {
        var transformComponent = Transform(uid);
        if (transformComponent.GridUid != grid.Owner)
            return false;

        var tile = _mapSystem.TileIndicesFor(grid, transformComponent.Coordinates);
        if (hunt.RoomTiles.Contains(tile))
            return true;

        foreach (var offset in CardinalOffsets)
        {
            if (hunt.RoomTiles.Contains(tile + offset))
                return true;
        }

        return false;
    }

    #endregion

    #region Searching

    /// <summary>
    ///     Marks every room tile a member can see from within <see cref="NpcSquadTacticsSettings.ClearRange"/> as seen.
    ///         Worked out in the grid's own frame, then put into the world with the grid's current transform, so it is
    ///         right on a moving grid too.
    /// </summary>
    private void UpdateCoverage(NpcHunt hunt, NpcSquadTacticsSettings settings)
    {
        if (hunt.UnseenTiles.Count == 0 ||
            hunt.GridUid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);
        var (_, _, worldMatrix, invWorldMatrix) = _transformSystem.GetWorldPositionRotationMatrixWithInv(gridUid);
        var clearRangeSquared = settings.ClearRange * settings.ClearRange;

        _newlySeenTiles.Clear();

        foreach (var memberUid in _members)
        {
            var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
            var memberLocalPosition = Vector2.Transform(memberMapCoordinates.Position, invWorldMatrix);

            foreach (var tile in hunt.UnseenTiles)
            {
                var tileLocalPosition = _mapSystem.TileCenterToVector(grid, tile);
                if ((tileLocalPosition - memberLocalPosition).LengthSquared() > clearRangeSquared)
                    continue;

                var tileMapCoordinates = new MapCoordinates(Vector2.Transform(tileLocalPosition, worldMatrix), memberMapCoordinates.MapId);
                if (_npcLineOfSightSystem.InLineOfSight(memberMapCoordinates, tileMapCoordinates, settings.ClearRange))
                    _newlySeenTiles.Add(tile);
            }
        }

        foreach (var tile in _newlySeenTiles)
        {
            hunt.UnseenTiles.Remove(tile);
        }
    }

    /// <summary>
    ///     How much of the room's floor has been seen, from 0 to 1. A hunt with no room has nothing to sweep.
    /// </summary>
    internal static float GetCoverage(NpcHunt hunt)
    {
        return hunt.RoomTiles.Count == 0 ? 1f : 1f - hunt.UnseenTiles.Count / (float) hunt.RoomTiles.Count;
    }

    /// <summary>
    ///     Members check the lockers and spots, each the nearest one nobody else is on, and those left over sweep the
    ///         room, each heading for the nearest unseen tile away from where the others are heading. Anything any
    ///         member has seen from close enough counts as checked, so most of a room is searched just by walking into
    ///         it. Nothing left to check means the hostile is not here. Returns whether the search is over.
    /// </summary>
    private bool UpdateSearch(EntityUid issuerUid, EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var pointsLeft = false;

        foreach (var point in hunt.SearchPoints)
        {
            if (point.Cleared)
                continue;

            if (point.AssigneeUid is { } assigneeUid && (!_members.Contains(assigneeUid) || now - point.AssignedAt >= settings.SearchPointTimeout))
            {
                // Gone, or could not get there: either way, nobody is checking it any more. A member that ran out of
                //      time on it is not sent back.
                point.AssigneeUid = null;
                point.Cleared = _members.Contains(assigneeUid);
            }

            if (!point.Cleared)
                point.Cleared = IsSearched(point, settings);

            if (point.Cleared)
            {
                point.AssigneeUid = null;
                continue;
            }

            pointsLeft = true;
        }

        UpdateSweeps(hunt, settings, now);

        if (!pointsLeft && GetCoverage(hunt) >= settings.SearchCoverage)
        {
            Exhaust(leaderUid, hunt, now);
            return true;
        }

        AssignSearchPoints(hunt, now);
        AssignSweeps(hunt, settings, now);

        foreach (var memberUid in _members)
        {
            if (TryGetAssignedPoint(hunt, memberUid, out var point))
            {
                SetOrder(memberUid,
                    issuerUid,
                    NpcOrderKind.Search,
                    point.Coordinates,
                    Angle.Zero,
                    point.StorageUid == null ? OrderRange : LockerOrderRange,
                    point.StorageUid,
                    now);
                continue;
            }

            if (hunt.Sweeps.TryGetValue(memberUid, out var sweep) && hunt.GridUid is { } gridUid)
            {
                SetOrder(memberUid, issuerUid, NpcOrderKind.Search, GetTileCoordinates(gridUid, sweep.Tile), Angle.Zero, OrderRange, null, now);
                continue;
            }

            // Nothing left for it: keep an eye on the room while the others finish.
            var standCoordinates = _transformSystem.GetMoverCoordinates(memberUid);
            var worldFacing = GetFacing(_transformSystem.GetMapCoordinates(memberUid),
                _transformSystem.ToMapCoordinates(hunt.PredictedCoordinates),
                _transformSystem.GetWorldRotation(memberUid));

            SetOrder(memberUid, issuerUid, NpcOrderKind.Watch, standCoordinates, ToLocalFacing(standCoordinates, worldFacing), OrderRange, null, now);
        }

        return false;
    }

    /// <summary>
    ///     Whether any member has had a good look at <paramref name="point"/>.
    /// </summary>
    private bool IsSearched(NpcHuntSearchPoint point, NpcSquadTacticsSettings settings)
    {
        if (point.StorageUid is { } storageUid)
        {
            if (TerminatingOrDeleted(storageUid) || !_entityStorageQuery.TryComp(storageUid, out var storageComponent))
                return true;

            // Somebody shut it, or it is open and somebody saw inside.
            if (!storageComponent.Open)
                return false;
        }
        else if (TerminatingOrDeleted(point.Coordinates.EntityId))
        {
            return true;
        }

        var pointMapCoordinates = _transformSystem.ToMapCoordinates(point.Coordinates);

        foreach (var memberUid in _members)
        {
            var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);

            if (memberMapCoordinates.MapId == pointMapCoordinates.MapId &&
                (pointMapCoordinates.Position - memberMapCoordinates.Position).LengthSquared() <= settings.ClearRange * settings.ClearRange &&
                _npcLineOfSightSystem.InLineOfSight(memberMapCoordinates, pointMapCoordinates, settings.ClearRange))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Hands each spot nobody is checking, most likely first, to the nearest member not checking anything.
    /// </summary>
    private void AssignSearchPoints(NpcHunt hunt, TimeSpan now)
    {
        _freeMembers.Clear();

        foreach (var memberUid in _members)
        {
            if (!TryGetAssignedPoint(hunt, memberUid, out _))
                _freeMembers.Add(memberUid);
        }

        foreach (var point in hunt.SearchPoints)
        {
            if (_freeMembers.Count == 0)
                return;

            if (point.Cleared || point.AssigneeUid != null)
                continue;

            var pointMapCoordinates = _transformSystem.ToMapCoordinates(point.Coordinates);
            var bestIndex = -1;
            var bestDistanceSquared = float.MaxValue;

            for (var i = 0; i < _freeMembers.Count; i++)
            {
                var distanceSquared = (_transformSystem.GetMapCoordinates(_freeMembers[i]).Position - pointMapCoordinates.Position).LengthSquared();
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestIndex = i;
            }

            point.AssigneeUid = _freeMembers[bestIndex];
            point.AssignedAt = now;
            hunt.Sweeps.Remove(_freeMembers[bestIndex]);
            _freeMembers.RemoveAt(bestIndex);
        }
    }

    /// <summary>
    ///     Drops sweeps that are done - the tile has been seen - and gives up on ones that are taking too long, which
    ///         most likely means the tile cannot be got to.
    /// </summary>
    private void UpdateSweeps(NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        _freeMembers.Clear();

        foreach (var (memberUid, sweep) in hunt.Sweeps)
        {
            if (!_members.Contains(memberUid) || !hunt.UnseenTiles.Contains(sweep.Tile))
            {
                _freeMembers.Add(memberUid);
                continue;
            }

            if (now - sweep.AssignedAt < settings.SearchPointTimeout)
                continue;

            hunt.UnseenTiles.Remove(sweep.Tile);
            _freeMembers.Add(memberUid);
        }

        foreach (var memberUid in _freeMembers)
        {
            hunt.Sweeps.Remove(memberUid);
        }
    }

    /// <summary>
    ///     Sends every member with nothing else to check towards the nearest unseen tile, keeping clear of the tiles the
    ///         others are sweeping towards where it can.
    /// </summary>
    private void AssignSweeps(NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (hunt.UnseenTiles.Count == 0 ||
            hunt.GridUid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);
        var invWorldMatrix = _transformSystem.GetInvWorldMatrix(gridUid);

        foreach (var memberUid in _members)
        {
            if (hunt.Sweeps.ContainsKey(memberUid) || TryGetAssignedPoint(hunt, memberUid, out _))
                continue;

            var memberLocalPosition = Vector2.Transform(_transformSystem.GetMapCoordinates(memberUid).Position, invWorldMatrix);
            Vector2i? best = null;
            Vector2i? bestCrowded = null;
            var bestDistanceSquared = float.MaxValue;
            var bestCrowdedDistanceSquared = float.MaxValue;

            foreach (var tile in hunt.UnseenTiles)
            {
                var tilePosition = _mapSystem.TileCenterToVector(grid, tile);
                var distanceSquared = (tilePosition - memberLocalPosition).LengthSquared();

                if (IsNearOtherSweep(hunt, grid, tilePosition))
                {
                    if (distanceSquared < bestCrowdedDistanceSquared)
                    {
                        bestCrowdedDistanceSquared = distanceSquared;
                        bestCrowded = tile;
                    }

                    continue;
                }

                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                best = tile;
            }

            if ((best ?? bestCrowded) is { } chosen)
                hunt.Sweeps[memberUid] = new NpcHuntSweep(chosen, now);
        }
    }

    private bool IsNearOtherSweep(NpcHunt hunt, Entity<MapGridComponent> grid, Vector2 tilePosition)
    {
        foreach (var sweep in hunt.Sweeps.Values)
        {
            if ((_mapSystem.TileCenterToVector(grid, sweep.Tile) - tilePosition).LengthSquared() < SweepSpacing * SweepSpacing)
                return true;
        }

        return false;
    }

    private static bool TryGetAssignedPoint(NpcHunt hunt, EntityUid memberUid, [NotNullWhen(true)] out NpcHuntSearchPoint? point)
    {
        foreach (var candidate in hunt.SearchPoints)
        {
            if (candidate.Cleared || candidate.AssigneeUid != memberUid)
                continue;

            point = candidate;
            return true;
        }

        point = null;
        return false;
    }

    #endregion
}
