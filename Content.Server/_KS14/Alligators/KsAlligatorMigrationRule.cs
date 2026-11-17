using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Alligators;

/// <summary>
///     Spawns alligators on distinct, unobstructed maintenance tiles on an event-eligible station.
/// </summary>
public sealed partial class KsAlligatorMigrationRule : StationEventSystem<KsAlligatorMigrationRuleComponent>
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    private static readonly EntProtoId Alligator = "KsMobSpaceAlligator";
    private static readonly LocId Announcement = "station-event-ks-alligator-migration-announcement";

    protected override void Started(EntityUid uid, KsAlligatorMigrationRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.SpawnCount <= 0 || !TryGetRandomStation(out var station) ||
            !TryComp<StationDataComponent>(station, out var data))
        {
            return;
        }

        var maintenanceTiles = new HashSet<int>();
        foreach (var tile in component.MaintenanceTiles)
            maintenanceTiles.Add(Proto.Index(tile).TileId);

        var candidates = new List<TileRef>();
        foreach (var gridUid in data.Grids)
        {
            if (!TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            foreach (var tile in _map.GetAllTiles(gridUid, grid))
            {
                if (maintenanceTiles.Contains(tile.Tile.TypeId))
                    candidates.Add(tile);
            }
        }

        // Check for obstructions only as each shuffled tile is considered, until there are enough spawns.
        RobustRandom.Shuffle(candidates);
        var spawned = 0;
        foreach (var tile in candidates)
        {
            if (_turf.IsTileBlocked(tile, CollisionGroup.MobMask | CollisionGroup.MobLayer))
                continue;

            Spawn(Alligator, _map.GridTileToLocal(tile.GridUid, Comp<MapGridComponent>(tile.GridUid), tile.GridIndices));
            if (++spawned >= component.SpawnCount)
                break;
        }

        if (spawned > 0)
            ChatSystem.DispatchFilteredAnnouncement(Filter.Empty().AddWhere(GameTicker.UserHasJoinedGame), Loc.GetString(Announcement));
    }
}
