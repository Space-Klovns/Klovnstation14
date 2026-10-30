using Content.Server._KS14.NPC.Squad;
using Content.Shared.Mobs;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.KillZones;

/// <summary>
///     Spatial memory of where NPCs' own have been gunned down, as in F.E.A.R.: a spot where an ally went down is a
///         kill zone, and for a while NPCs of the same factions choose not to stand in one. Anything picking a position
///         opts in by reading <see cref="GetDanger"/> - <c>TacticalPositionOperator.KillZoneAvoidance</c>, and the
///         squad cover setting of the same name.
/// </summary>
/// <remarks>
///     A zone is worked out once, when someone goes down: a flood over the floor around them, never through a wall
///         and stopping at doorways, so it is the room or corridor they fell in. Reading one is then a tile lookup.
///         Zones are kept on the grid as its tiles (<see cref="NpcKillZonesComponent"/>). Expired ones are skipped when
///         read and dropped the next time a zone is added there, so nothing needs updating. Off any grid, nobody marks
///         anything: there is no floor to flood.
/// </remarks>
public sealed partial class NpcKillZoneSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcKillZonesComponent> _killZonesQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    /// <summary>
    ///     A zone added this close (in tiles) to one of the same factions refreshes it instead: going critical and then
    ///         dying in one spot is one kill zone.
    /// </summary>
    private const int MergeDistance = 1;

    private readonly Dictionary<Vector2i, int> _floodSteps = new();

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<NpcKillZoneOnDownComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead) ||
            !_factionMemberQuery.TryComp(entity.Owner, out var factionMemberComponent) ||
            factionMemberComponent.Factions.Count == 0)
            return;

        AddZone(entity.Owner, Transform(entity.Owner).Coordinates, entity.Comp.Reach, entity.Comp.Duration, factionMemberComponent.Factions);
    }

    /// <summary>
    ///     Marks a kill zone around <paramref name="coordinates"/> for <paramref name="factions"/>: every tile of floor
    ///         within <paramref name="reach"/> steps of it, as <paramref name="walkerUid"/> would walk it.
    /// </summary>
    public void AddZone(EntityUid walkerUid,
        EntityCoordinates coordinates,
        int reach,
        TimeSpan duration,
        IReadOnlyCollection<ProtoId<NpcFactionPrototype>> factions)
    {
        if (TerminatingOrDeleted(coordinates.EntityId) ||
            _transformSystem.GetGrid(coordinates) is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);
        var center = _mapSystem.TileIndicesFor(grid, coordinates);
        var now = _gameTiming.CurTime;
        var zonesComponent = EnsureComp<NpcKillZonesComponent>(gridUid);

        zonesComponent.Zones.RemoveAll(zone => zone.ExpiresAt <= now);

        foreach (var zone in zonesComponent.Zones)
        {
            var apart = zone.Center - center;
            if (Math.Abs(apart.X) > MergeDistance || Math.Abs(apart.Y) > MergeDistance || !zone.Factions.SetEquals(factions))
                continue;

            zone.ExpiresAt = now + duration;
            return;
        }

        _floodSteps.Clear();
        _npcSquadCoverSystem.FloodTiles(walkerUid, grid, center, Math.Max(0, reach), _floodSteps);

        var tiles = new Dictionary<Vector2i, float>(_floodSteps.Count);
        foreach (var (tile, steps) in _floodSteps)
        {
            // 1 where they fell, falling off a step at a time towards the edge.
            tiles[tile] = 1f - steps / (float) (reach + 1);
        }

        zonesComponent.Zones.Add(new NpcKillZone
        {
            Center = center,
            Tiles = tiles,
            ExpiresAt = now + duration,
            Factions = new HashSet<ProtoId<NpcFactionPrototype>>(factions),
        });
    }

    /// <summary>
    ///     How much <paramref name="coordinates"/> lies in a kill zone that <paramref name="observerUid"/> cares about -
    ///         one where someone of its own factions went down: 1 where they fell, less towards a zone's edge, and 0
    ///         outside every zone. The worst zone counts.
    /// </summary>
    public float GetDanger(EntityUid observerUid, EntityCoordinates coordinates)
    {
        if (TerminatingOrDeleted(coordinates.EntityId) ||
            _transformSystem.GetGrid(coordinates) is not { } gridUid ||
            !_killZonesQuery.TryComp(gridUid, out var zonesComponent) ||
            zonesComponent.Zones.Count == 0 ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent) ||
            !_factionMemberQuery.TryComp(observerUid, out var factionMemberComponent))
            return 0f;

        var tile = _mapSystem.TileIndicesFor((gridUid, mapGridComponent), coordinates);
        var now = _gameTiming.CurTime;
        var danger = 0f;

        foreach (var zone in zonesComponent.Zones)
        {
            if (zone.ExpiresAt > now &&
                zone.Tiles.TryGetValue(tile, out var tileDanger) &&
                SharesFaction(zone, factionMemberComponent))
                danger = MathF.Max(danger, tileDanger);
        }

        return danger;
    }

    /// <summary>
    ///     Every zone still in force on <paramref name="gridUid"/>: its tiles with their danger, and how long it has
    ///         left. For debugging.
    /// </summary>
    public void GetZones(EntityUid gridUid, List<(Vector2i Center, IReadOnlyDictionary<Vector2i, float> Tiles, float SecondsLeft)> results)
    {
        if (!_killZonesQuery.TryComp(gridUid, out var zonesComponent))
            return;

        var now = _gameTiming.CurTime;
        foreach (var zone in zonesComponent.Zones)
        {
            if (zone.ExpiresAt > now)
                results.Add((zone.Center, zone.Tiles, (float) (zone.ExpiresAt - now).TotalSeconds));
        }
    }

    private static bool SharesFaction(NpcKillZone zone, NpcFactionMemberComponent factionMemberComponent)
    {
        foreach (var faction in factionMemberComponent.Factions)
        {
            if (zone.Factions.Contains(faction))
                return true;
        }

        return false;
    }
}
