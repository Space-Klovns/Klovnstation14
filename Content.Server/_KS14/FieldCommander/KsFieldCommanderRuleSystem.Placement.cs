using System.Numerics;
using Content.Server.Spawners.Components;
using Content.Shared._KS14.RCD;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Safe zones

    /// <summary>
    ///     Safe zone markers on the map if there are any, or else every spawn point near the defenders' objectives.
    /// </summary>
    private void FindSafeZones(KsFieldCommanderRuleComponent component)
    {
        component.SafeZones.Clear();

        var markerQuery = EntityQueryEnumerator<KsFieldCommanderSafeZoneComponent, TransformComponent>();
        while (markerQuery.MoveNext(out var markerUid, out var markerComponent, out var transformComponent))
        {
            component.SafeZones.Add(new KsFieldCommanderSafeZone(
                _transformSystem.GetMapCoordinates(markerUid, transformComponent),
                markerComponent.Radius ?? component.SafeZoneRadius));
        }

        if (component.SafeZones.Count > 0)
            return;

        var objectives = new List<MapCoordinates>();
        CollectDefenderObjectives(component, objectives);

        var spawnQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out var spawnUid, out _, out var transformComponent))
        {
            var spawnCoordinates = _transformSystem.GetMapCoordinates(spawnUid, transformComponent);

            // Spawn points somewhere else entirely (another station, an admin room) are none of the commander's business.
            if (!IsNearAny(spawnCoordinates, objectives, component.MaxSpawnDistance))
                continue;

            component.SafeZones.Add(new KsFieldCommanderSafeZone(spawnCoordinates, component.SafeZoneRadius));
        }
    }

    /// <summary>
    ///     Whether <paramref name="coordinates"/> is inside a safe zone, or within <paramref name="margin"/> of one.
    /// </summary>
    public static bool IsInSafeZone(KsFieldCommanderRuleComponent component, MapCoordinates coordinates, float margin = 0f)
    {
        foreach (var zone in component.SafeZones)
        {
            if (IsWithin(zone.Center, coordinates, zone.Radius + margin))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The spawn areas are for coming back to, not for fortifying: nobody may use an RCD in them.
    /// </summary>
    private void ForbidBuildingInSafeZones(KsFieldCommanderRuleComponent component)
    {
        var zonesByMap = new Dictionary<MapId, List<KsNoBuildZone>>();
        foreach (var safeZone in component.SafeZones)
        {
            if (!zonesByMap.TryGetValue(safeZone.Center.MapId, out var zones))
                zonesByMap[safeZone.Center.MapId] = zones = [];

            zones.Add(new KsNoBuildZone(safeZone.Center.Position, safeZone.Radius));
        }

        foreach (var (mapId, zones) in zonesByMap)
        {
            if (_mapSystem.GetMapOrInvalid(mapId) is { Valid: true } mapUid)
                _noBuildZoneSystem.SetZones(mapUid, zones);
        }
    }

    /// <summary>
    ///     Undoes <see cref="ForbidBuildingInSafeZones"/>.
    /// </summary>
    private void AllowBuildingInSafeZones(KsFieldCommanderRuleComponent component)
    {
        var mapIds = new HashSet<MapId>();
        foreach (var safeZone in component.SafeZones)
        {
            if (mapIds.Add(safeZone.Center.MapId) && _mapSystem.GetMapOrInvalid(safeZone.Center.MapId) is { Valid: true } mapUid)
                _noBuildZoneSystem.SetZones(mapUid, []);
        }
    }

    #endregion

    #region Distances

    /// <summary>
    ///     Whether two points are on the same map and closer than <paramref name="range"/>.
    /// </summary>
    private static bool IsWithin(MapCoordinates a, MapCoordinates b, float range)
    {
        return a.MapId == b.MapId && (a.Position - b.Position).LengthSquared() < range * range;
    }

    private static bool IsNearAny(MapCoordinates coordinates, List<MapCoordinates> others, float range)
    {
        foreach (var other in others)
        {
            if (IsWithin(other, coordinates, range))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The middle of what the defenders hold, to give directions and draw the fog from: their safe zones, or else
    ///         <paramref name="anchors"/>.
    /// </summary>
    private static Vector2 GetBaseCentre(KsFieldCommanderRuleComponent component, List<MapCoordinates> anchors)
    {
        var sum = Vector2.Zero;
        var count = 0;

        foreach (var zone in component.SafeZones)
        {
            sum += zone.Center.Position;
            count++;
        }

        if (count == 0)
        {
            foreach (var anchor in anchors)
            {
                sum += anchor.Position;
                count++;
            }
        }

        return count > 0 ? sum / count : Vector2.Zero;
    }

    #endregion

    #region Placement

    /// <summary>
    ///     A free tile in a ring around the battlefield's anchors, far enough from any defender and out of every
    ///         defender's sight when <paramref name="hidden"/>, and never in a safe zone.
    /// </summary>
    private bool TryFindPlacement(KsFieldCommanderRuleComponent component,
        Battlefield battlefield,
        float minDistance,
        float maxDistance,
        float minObjectiveDistance,
        float minDefenderDistance,
        bool hidden,
        out EntityCoordinates coordinates,
        bool outsideFog = false)
    {
        coordinates = default;

        var anchors = battlefield.Anchors;
        if (anchors.Count == 0)
            return false;

        for (var attempt = 0; attempt < component.PlacementAttempts; attempt++)
        {
            // Everything is judged at the centre of the tile it would really land on, not at the point picked.
            if (!TryGetFreeTile(RandomPointAround(_robustRandom.Pick(anchors), minDistance, maxDistance), out var tileCoordinates, out var tileMapCoordinates))
                continue;

            if (IsInSafeZone(component, tileMapCoordinates) ||
                IsNearAny(tileMapCoordinates, anchors, minObjectiveDistance) ||
                IsNearAny(tileMapCoordinates, battlefield.Defenders, minDefenderDistance))
                continue;

            if (outsideFog && !IsBeyondFog(component, tileMapCoordinates, component.FogSpawnMargin))
                continue;

            if (hidden && IsSeenByDefenders(component, tileMapCoordinates, battlefield.Defenders))
                continue;

            coordinates = tileCoordinates;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     A free tile between <paramref name="minDistance"/> and <paramref name="maxDistance"/> from
    ///         <paramref name="center"/>, with nothing else asked of it.
    /// </summary>
    private bool TryFindFreeTileAround(MapCoordinates center, float minDistance, float maxDistance, int attempts, out EntityCoordinates tileCoordinates)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (TryGetFreeTile(RandomPointAround(center, minDistance, maxDistance), out tileCoordinates, out _))
                return true;
        }

        tileCoordinates = default;
        return false;
    }

    private MapCoordinates RandomPointAround(MapCoordinates center, float minDistance, float maxDistance)
    {
        var offset = _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(minDistance, maxDistance);
        return new MapCoordinates(center.Position + offset, center.MapId);
    }

    /// <summary>
    ///     The centre of the tile under <paramref name="candidate"/>, if it is solid ground that nothing stands on.
    /// </summary>
    private bool TryGetFreeTile(MapCoordinates candidate, out EntityCoordinates tileCoordinates, out MapCoordinates tileMapCoordinates)
    {
        tileMapCoordinates = default;

        if (!TryGetTile(candidate, out tileCoordinates, out var tileRef) ||
            _turfSystem.IsSpace(tileRef) ||
            _turfSystem.IsTileBlocked(tileRef, CollisionGroup.MobMask))
            return false;

        tileMapCoordinates = _transformSystem.ToMapCoordinates(tileCoordinates);
        return true;
    }

    /// <summary>
    ///     The centre of the floor tile under <paramref name="candidate"/>, whatever stands on it.
    /// </summary>
    private bool TryGetTile(MapCoordinates candidate, out EntityCoordinates tileCoordinates, out TileRef tileRef)
    {
        tileCoordinates = default;
        tileRef = default;

        if (!_mapSystem.TryFindGridAt(candidate, out var gridUid, out MapGridComponent? gridComponent))
            return false;

        var indices = _mapSystem.TileIndicesFor(gridUid, gridComponent, candidate);
        tileRef = _mapSystem.GetTileRef(gridUid, gridComponent, indices);

        if (tileRef.Tile.IsEmpty)
            return false;

        tileCoordinates = _mapSystem.GridTileToLocal(gridUid, gridComponent, indices);
        return true;
    }

    private bool IsSeenByDefenders(KsFieldCommanderRuleComponent component, MapCoordinates coordinates, List<MapCoordinates> defenders)
    {
        foreach (var defender in defenders)
        {
            if (IsWithin(defender, coordinates, component.DefenderSightRange) &&
                _npcLineOfSightSystem.InLineOfSight(defender, coordinates, component.DefenderSightRange))
                return true;
        }

        return false;
    }

    #endregion
}
