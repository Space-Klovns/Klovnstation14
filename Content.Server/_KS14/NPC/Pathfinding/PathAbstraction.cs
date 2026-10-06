using Content.Server.NPC.Pathfinding;

namespace Content.Server._KS14.NPC.Pathfinding;

/// <summary>
///     What a path request can walk through: everything that decides a poly's cost, bar the NPC's own avoided tiles.
///         Requests with the same profile share the coarse map built for it.
/// </summary>
public readonly record struct PathProfile(PathFlags Flags, int CollisionLayer, int CollisionMask);

/// <summary>
///     A navmesh chunk: its grid, and its origin in chunks.
/// </summary>
public readonly record struct PathChunkKey(EntityUid GraphUid, Vector2i Origin)
{
    public static PathChunkKey Of(PathPoly poly)
    {
        return new PathChunkKey(poly.GraphUid, poly.ChunkOrigin);
    }
}

/// <summary>
///     The coarse map of one navmesh chunk, for one <see cref="PathProfile"/>: its walkable polys split into
///         <see cref="PathRegion"/>s, each with the <see cref="PathCluster"/>s - its polys on the chunk's edge - it
///         leaves the chunk through, and what it costs to walk from each poly to each of them. Built when a search first
///         needs it, and dropped when the chunk or a neighbour is rebuilt. Only read once built, so searches on several
///         threads may share it.
/// </summary>
public sealed class PathChunkAbstraction
{
    public readonly PathProfile Profile;

    public readonly List<PathRegion> Regions = new();

    /// <summary>
    ///     Each walkable poly's region, and its index there.
    /// </summary>
    public readonly Dictionary<PathPoly, (PathRegion Region, int Index)> RegionOf =
        PathfindingSystem.NewPolyDictionary<(PathRegion Region, int Index)>();

    public PathChunkAbstraction(PathProfile profile)
    {
        Profile = profile;
    }
}

/// <summary>
///     Walkable polys of one chunk that connect without leaving it.
/// </summary>
public sealed class PathRegion
{
    public readonly PathChunkAbstraction Abstraction;
    public readonly PathChunkKey Chunk;
    public readonly List<PathPoly> Polys = new();

    /// <summary>
    ///     What it costs to step onto each poly, per unit of distance, by index in <see cref="Polys"/>.
    /// </summary>
    public readonly List<float> Modifiers = new();

    public readonly List<PathCluster> Clusters = new();

    /// <summary>
    ///     Each poly's cluster, by index in <see cref="Polys"/>: its index in <see cref="Clusters"/>, or -1 for a poly
    ///         not on the chunk's edge.
    /// </summary>
    public readonly List<int> ClusterOfPoly = new();

    public PathRegion(PathChunkAbstraction abstraction, PathChunkKey chunk)
    {
        Abstraction = abstraction;
        Chunk = chunk;
    }
}

/// <summary>
///     A poly of a region with a neighbour in another chunk: a way out of the region, and in. One poly each, rather
///         than every poly along an edge together, so what it costs to walk on from one is exact.
/// </summary>
public sealed class PathCluster
{
    public readonly PathRegion Region;
    public readonly int Index;

    /// <summary>
    ///     The poly's index in the region.
    /// </summary>
    public readonly int PolyIndex;

    /// <summary>
    ///     The cheapest walk, staying in the region, from each of its polys to this one, by index in the region.
    /// </summary>
    public float[] DistanceTo = Array.Empty<float>();

    /// <summary>
    ///     The region's other clusters, by <see cref="Index"/>, with the cheapest walk from each to this one, staying in
    ///         the region. Only the walks that do not go as cheaply by way of another cluster: the rest add nothing to a
    ///         search, and in a room, where most walks from one edge to another are as cheap through some third poly on
    ///         the edge, they are most of them.
    /// </summary>
    public readonly List<(int Index, float Cost)> Incoming = new();

    public PathCluster(PathRegion region, int index, int polyIndex)
    {
        Region = region;
        Index = index;
        PolyIndex = polyIndex;
    }
}
