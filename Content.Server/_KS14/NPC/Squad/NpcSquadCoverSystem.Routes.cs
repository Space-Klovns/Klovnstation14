using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Routes that keep out of somewhere: the way round a room, to another of its doors, without cutting through it.
///         The pathfinder only knows the shortest way, which for the door on the far side of a room is usually
///         straight through the room - exactly where a squad stacking up outside it must not go.
/// </summary>
public sealed partial class NpcSquadCoverSystem
{
    /// <summary>
    ///     How finely a straight stretch of route is checked when cutting corners off it, in tiles.
    /// </summary>
    private const float RouteLineStep = 0.25f;

    private readonly Queue<Vector2i> _routeFrontier = new();
    private readonly Dictionary<Vector2i, Vector2i> _routeParents = new();
    private readonly List<Vector2i> _routeTiles = new();

    /// <summary>
    ///     Finds a way from <paramref name="fromTile"/> to <paramref name="toTile"/>, as <paramref name="walkerUid"/>
    ///         would walk it, that never steps on <paramref name="avoidTiles"/>, and fills
    ///         <paramref name="waypoints"/> with the turns along it - the tiles to walk to one after another, so that
    ///         walking straight between them never strays into the avoided tiles. <paramref name="toTile"/> itself is
    ///         not included. False if there is no such way within <paramref name="maxLength"/> tiles.
    /// </summary>
    /// <remarks>
    ///     A flood over the same tile classification as room detection: floor and doors can be walked, anything
    ///         else cannot. The start is always allowed, so a walker standing in a doorway can leave it.
    /// </remarks>
    internal bool TryFindRouteAround(EntityUid walkerUid,
        Entity<MapGridComponent> grid,
        Vector2i fromTile,
        Vector2i toTile,
        HashSet<Vector2i> avoidTiles,
        int maxLength,
        List<Vector2i> waypoints,
        out int length)
    {
        waypoints.Clear();
        length = 0;

        var (collisionLayer, collisionMask) = GetRoomCollision(walkerUid);
        var analysis = new RoomAnalysis
        {
            Grid = grid,
            CollisionLayer = collisionLayer,
            CollisionMask = collisionMask,
        };

        if (!IsRoutable(analysis, toTile, avoidTiles) && !TryResolveRouteGoal(analysis, avoidTiles, ref toTile))
            return false;

        _routeFrontier.Clear();
        _routeParents.Clear();
        _routeParents[fromTile] = fromTile;
        _routeFrontier.Enqueue(fromTile);

        var found = fromTile == toTile;
        var maxTiles = maxLength * maxLength;

        while (!found && _routeFrontier.TryDequeue(out var tile))
        {
            foreach (var offset in CardinalOffsets)
            {
                var neighbourTile = tile + offset;
                if (_routeParents.ContainsKey(neighbourTile) || !IsRoutable(analysis, neighbourTile, avoidTiles))
                    continue;

                _routeParents[neighbourTile] = tile;

                if (neighbourTile == toTile)
                {
                    found = true;
                    break;
                }

                if (_routeParents.Count <= maxTiles)
                    _routeFrontier.Enqueue(neighbourTile);
            }
        }

        if (!found)
            return false;

        // Back from the goal to the start.
        _routeTiles.Clear();
        for (var tile = toTile; tile != fromTile; tile = _routeParents[tile])
        {
            _routeTiles.Add(tile);
        }

        _routeTiles.Add(fromTile);
        _routeTiles.Reverse();

        length = _routeTiles.Count - 1;
        if (length > maxLength)
            return false;

        // Cut every corner that can be cut: from each turn, on to the furthest tile still in a straight, clear line.
        var current = 0;
        while (current < _routeTiles.Count - 1)
        {
            var next = current + 1;
            for (var candidate = _routeTiles.Count - 1; candidate > current + 1; candidate--)
            {
                if (!IsClearLine(analysis, _routeTiles[current], _routeTiles[candidate], avoidTiles))
                    continue;

                next = candidate;
                break;
            }

            if (next < _routeTiles.Count - 1)
                waypoints.Add(_routeTiles[next]);

            current = next;
        }

        return true;
    }

    private bool IsRoutable(RoomAnalysis analysis, Vector2i tile, HashSet<Vector2i> avoidTiles)
    {
        return !avoidTiles.Contains(tile) && GetTileKind(analysis, tile) is TileKind.Walkable or TileKind.Door;
    }

    /// <summary>
    ///     A goal on a tile that cannot be walked - a waiting spot against a wall, say - is moved to a walkable
    ///         neighbour.
    /// </summary>
    private bool TryResolveRouteGoal(RoomAnalysis analysis, HashSet<Vector2i> avoidTiles, ref Vector2i goalTile)
    {
        foreach (var offset in NeighbourOffsets)
        {
            if (!IsRoutable(analysis, goalTile + offset, avoidTiles))
                continue;

            goalTile += offset;
            return true;
        }

        return false;
    }

    private bool IsClearLine(RoomAnalysis analysis, Vector2i fromTile, Vector2i toTile, HashSet<Vector2i> avoidTiles)
    {
        var from = new Vector2(fromTile.X + 0.5f, fromTile.Y + 0.5f);
        var to = new Vector2(toTile.X + 0.5f, toTile.Y + 0.5f);
        var steps = (int) MathF.Ceiling((to - from).Length() / RouteLineStep);

        for (var i = 1; i < steps; i++)
        {
            var point = Vector2.Lerp(from, to, i / (float) steps);
            var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));

            if (!IsRoutable(analysis, tile, avoidTiles))
                return false;
        }

        return true;
    }
}
