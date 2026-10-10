using System.Numerics;
using Content.Shared.NPC;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Room detection. Works per tile on the pathfinding navmesh, reading each tile's centre poly: a room is
///         the walkable area reachable from a seed without crossing a door or a narrow gap, and small enough
///         to be one. The doors and gaps bounding it are its thresholds.
/// </summary>
public sealed partial class NpcSquadCoverSystem
{
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    private static readonly Vector2i[] CardinalOffsets =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    ];

    private static readonly Vector2i[] NeighbourOffsets =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    ];

    private enum TileKind : byte
    {
        Blocked,
        Walkable,
        Door,
        Space,
    }

    /// <summary>
    ///     Scratch state for one analysis. Tile kinds are cached for its duration only, since doors and walls
    ///         come and go between analyses.
    /// </summary>
    private sealed class RoomAnalysis
    {
        public required Entity<MapGridComponent> Grid;
        public required int CollisionLayer;
        public required int CollisionMask;

        public readonly Dictionary<Vector2i, TileKind> TileKinds = new();
        public readonly HashSet<Vector2i> RoomTiles = new();
        public readonly HashSet<Vector2i> ThresholdTiles = new();
        public readonly HashSet<Vector2i> BoundaryTiles = new();
        public readonly Queue<Vector2i> Frontier = new();
    }

    /// <summary>
    ///     Finds the room containing <paramref name="seedTile"/>, filling <paramref name="plan"/>'s room tiles
    ///         and thresholds. Returns false if the seed is not in anything recognisable as a room.
    /// </summary>
    /// <param name="awayFromTile">
    ///     If the seed is not itself floor - a threat last seen in a doorway, say - the neighbouring floor tile
    ///         farthest from this is used instead, which is the far side of that doorway from the squad.
    /// </param>
    private bool TryAnalyseRoom(
        Entity<MapGridComponent> grid,
        Vector2i seedTile,
        Vector2i awayFromTile,
        int collisionLayer,
        int collisionMask,
        NpcSquadCoverSettings settings,
        NpcSquadCoverPlan plan,
        List<Vector2i> exposureTiles)
    {
        var analysis = new RoomAnalysis
        {
            Grid = grid,
            CollisionLayer = collisionLayer,
            CollisionMask = collisionMask,
        };

        if (!TryResolveSeed(analysis, seedTile, awayFromTile, out seedTile))
            return false;

        // Doors alone bound most rooms. Only when that leaks out into something too big to be one does it
        //      look for open archways too, since gap detection can mistake a nook for a way in.
        if (!TryFlood(analysis, seedTile, settings.MaxRoomTiles, gapsAreThresholds: false) &&
            !TryFlood(analysis, seedTile, settings.MaxRoomTiles, gapsAreThresholds: true))
            return false;

        if (analysis.ThresholdTiles.Count == 0)
            return false;

        plan.GridUid = grid.Owner;
        plan.RoomTiles.UnionWith(analysis.RoomTiles);
        plan.IsHallway = GetShortSide(analysis.RoomTiles) <= settings.HallwayWidth;

        BuildThresholds(analysis, plan);

        foreach (var boundaryTile in analysis.BoundaryTiles)
        {
            if (IsExposure(analysis, boundaryTile))
                exposureTiles.Add(boundaryTile);
        }

        return plan.Thresholds.Count > 0;
    }

    private bool TryResolveSeed(RoomAnalysis analysis, Vector2i seedTile, Vector2i awayFromTile, out Vector2i resolvedTile)
    {
        resolvedTile = seedTile;

        if (GetTileKind(analysis, seedTile) == TileKind.Walkable)
            return true;

        var found = false;
        var bestDistance = float.MinValue;

        foreach (var offset in NeighbourOffsets)
        {
            var neighbourTile = seedTile + offset;
            if (GetTileKind(analysis, neighbourTile) != TileKind.Walkable)
                continue;

            var distance = (neighbourTile - awayFromTile).LengthSquared;
            if (distance <= bestDistance)
                continue;

            resolvedTile = neighbourTile;
            bestDistance = distance;
            found = true;
        }

        return found;
    }

    /// <summary>
    ///     Floods walkable tiles from <paramref name="seedTile"/>, stopping at doors (and narrow gaps, if
    ///         <paramref name="gapsAreThresholds"/>), which are recorded as thresholds. False if it exceeds
    ///         <paramref name="maxTiles"/>.
    /// </summary>
    private bool TryFlood(RoomAnalysis analysis, Vector2i seedTile, int maxTiles, bool gapsAreThresholds)
    {
        analysis.RoomTiles.Clear();
        analysis.ThresholdTiles.Clear();
        analysis.BoundaryTiles.Clear();
        analysis.Frontier.Clear();

        analysis.RoomTiles.Add(seedTile);
        analysis.Frontier.Enqueue(seedTile);

        while (analysis.Frontier.TryDequeue(out var tile))
        {
            foreach (var offset in CardinalOffsets)
            {
                var neighbourTile = tile + offset;

                if (analysis.RoomTiles.Contains(neighbourTile) ||
                    analysis.ThresholdTiles.Contains(neighbourTile) ||
                    analysis.BoundaryTiles.Contains(neighbourTile))
                    continue;

                switch (GetTileKind(analysis, neighbourTile))
                {
                    case TileKind.Door:
                        analysis.ThresholdTiles.Add(neighbourTile);
                        continue;
                    case TileKind.Blocked:
                    case TileKind.Space:
                        analysis.BoundaryTiles.Add(neighbourTile);
                        continue;
                }

                if (gapsAreThresholds && IsGap(analysis, neighbourTile))
                {
                    analysis.ThresholdTiles.Add(neighbourTile);
                    continue;
                }

                analysis.RoomTiles.Add(neighbourTile);

                if (analysis.RoomTiles.Count > maxTiles)
                    return false;

                analysis.Frontier.Enqueue(neighbourTile);
            }
        }

        return true;
    }

    /// <summary>
    ///     An archway: a walkable tile walled in on both sides, in a passage at most two tiles deep that opens
    ///         out at both ends. The depth limit is what tells an archway from a corridor.
    /// </summary>
    private bool IsGap(RoomAnalysis analysis, Vector2i tile)
    {
        return IsGapAlong(analysis, tile, across: new Vector2i(1, 0), along: new Vector2i(0, 1)) ||
            IsGapAlong(analysis, tile, across: new Vector2i(0, 1), along: new Vector2i(1, 0));
    }

    private bool IsGapAlong(RoomAnalysis analysis, Vector2i tile, Vector2i across, Vector2i along)
    {
        if (!IsFlanked(analysis, tile, across))
            return false;

        const int maxDepth = 2;
        var depth = 1;

        foreach (var direction in new[] { along, -along })
        {
            var current = tile + direction;

            while (IsFlanked(analysis, current, across))
            {
                if (++depth > maxDepth)
                    return false;

                current += direction;
            }

            // The passage has to open into walkable space at each end, not dead-end into a wall.
            if (GetTileKind(analysis, current) != TileKind.Walkable)
                return false;
        }

        return true;
    }

    private bool IsFlanked(RoomAnalysis analysis, Vector2i tile, Vector2i across)
    {
        return GetTileKind(analysis, tile) == TileKind.Walkable &&
            IsSolid(GetTileKind(analysis, tile + across)) &&
            IsSolid(GetTileKind(analysis, tile - across));
    }

    private static bool IsSolid(TileKind kind)
    {
        return kind is TileKind.Blocked or TileKind.Space;
    }

    /// <summary>
    ///     Groups adjacent threshold tiles (double doors, an airlock beside a firelock) into one threshold each.
    /// </summary>
    private void BuildThresholds(RoomAnalysis analysis, NpcSquadCoverPlan plan)
    {
        var unvisited = new HashSet<Vector2i>(analysis.ThresholdTiles);
        var clusterFrontier = new Queue<Vector2i>();

        while (unvisited.Count > 0)
        {
            var clusterTiles = new List<Vector2i>();

            using (var enumerator = unvisited.GetEnumerator())
            {
                enumerator.MoveNext();
                clusterFrontier.Enqueue(enumerator.Current);
            }

            unvisited.Remove(clusterFrontier.Peek());

            while (clusterFrontier.TryDequeue(out var tile))
            {
                clusterTiles.Add(tile);

                foreach (var offset in CardinalOffsets)
                {
                    if (unvisited.Remove(tile + offset))
                        clusterFrontier.Enqueue(tile + offset);
                }
            }

            var center = Vector2.Zero;
            var inward = Vector2.Zero;

            foreach (var tile in clusterTiles)
            {
                center += _mapSystem.TileCenterToVector(analysis.Grid, tile);

                foreach (var offset in CardinalOffsets)
                {
                    if (analysis.RoomTiles.Contains(tile + offset))
                        inward += (Vector2)offset;
                }
            }

            center /= clusterTiles.Count;

            // Opposing sides cancel out for a threshold the room wraps around; any direction into the room will do.
            if (inward.LengthSquared() < 0.01f)
                continue;

            plan.Thresholds.Add(new NpcSquadThreshold(clusterTiles, center, Vector2.Normalize(inward)));
        }
    }

    private bool IsExposure(RoomAnalysis analysis, Vector2i tile)
    {
        if (GetTileKind(analysis, tile) == TileKind.Space)
            return true;

        var anchoredEnumerator = _mapSystem.GetAnchoredEntities(analysis.Grid, tile);
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (_tagSystem.HasTag(anchoredUid.Value, WindowTag))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Same rules as <c>PathfindingSystem.GetTileCost</c>: doors and climbables can be passed, other hard
    ///         collision cannot.
    /// </summary>
    private TileKind GetTileKind(RoomAnalysis analysis, Vector2i tile)
    {
        if (analysis.TileKinds.TryGetValue(tile, out var kind))
            return kind;

        kind = ClassifyTile(analysis, tile);
        analysis.TileKinds[tile] = kind;
        return kind;
    }

    private TileKind ClassifyTile(RoomAnalysis analysis, Vector2i tile)
    {
        if (!_mapSystem.TryGetTile(analysis.Grid.Comp, tile, out var gridTile) || gridTile.IsEmpty)
            return TileKind.Space;

        var tileCenter = _mapSystem.TileCenterToVector(analysis.Grid, tile);
        if (_pathfindingSystem.GetPoly(new EntityCoordinates(analysis.Grid, tileCenter)) is not { } poly ||
            !poly.IsValid())
            return TileKind.Blocked;

        var flags = poly.Data.Flags;

        if ((flags & PathfindingBreadcrumbFlag.Space) != 0x0)
            return TileKind.Space;

        if ((flags & PathfindingBreadcrumbFlag.Door) != 0x0)
            return TileKind.Door;

        var collides = (analysis.CollisionLayer & poly.Data.CollisionMask) != 0x0 ||
            (analysis.CollisionMask & poly.Data.CollisionLayer) != 0x0;

        if (collides && (flags & PathfindingBreadcrumbFlag.Climb) == 0x0)
            return TileKind.Blocked;

        return TileKind.Walkable;
    }

    private static int GetShortSide(HashSet<Vector2i> tiles)
    {
        var min = new Vector2i(int.MaxValue, int.MaxValue);
        var max = new Vector2i(int.MinValue, int.MinValue);

        foreach (var tile in tiles)
        {
            min = Vector2i.ComponentMin(min, tile);
            max = Vector2i.ComponentMax(max, tile);
        }

        return Math.Min(max.X - min.X, max.Y - min.Y) + 1;
    }

    /// <summary>
    ///     How many of <paramref name="tile"/>'s eight neighbours are not part of the room - walls to put your
    ///         back to.
    /// </summary>
    private static int CountSolidNeighbours(NpcSquadCoverPlan plan, Vector2i tile)
    {
        var count = 0;

        foreach (var offset in NeighbourOffsets)
        {
            if (!plan.RoomTiles.Contains(tile + offset))
                count++;
        }

        return count;
    }
}
