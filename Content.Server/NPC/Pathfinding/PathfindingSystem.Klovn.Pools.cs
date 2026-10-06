// KS14: added in this fork
using Content.Server._KS14.NPC.Pathfinding;
using Microsoft.Extensions.ObjectPool;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>
    /// How many of each piece of search state the pools keep. Enough for the searches usually in flight at once; past
    /// that, searches allocate their own and the extras are simply dropped when they finish.
    /// </summary>
    private const int SearchPoolSize = 64;

    // Every search grows these to several hundred entries, and NPCs make one on every replan. Pooled, they are allocated
    //      once and reused; the pools are thread-safe, and searches run in parallel.
    private readonly ObjectPool<Dictionary<PathPoly, float>> _polyCostPool =
        new DefaultObjectPool<Dictionary<PathPoly, float>>(new PolyDictionaryPolicy<float>(), SearchPoolSize);

    private readonly ObjectPool<Dictionary<PathPoly, PathPoly>> _polyParentPool =
        new DefaultObjectPool<Dictionary<PathPoly, PathPoly>>(new PolyDictionaryPolicy<PathPoly>(), SearchPoolSize);

    private readonly ObjectPool<PathPolyFrontier> _polyFrontierPool =
        new DefaultObjectPool<PathPolyFrontier>(new PathPolyFrontierPolicy(), SearchPoolSize);

    /// <summary>
    /// Creates a map keyed by poly identity, as every search's maps are.
    /// </summary>
    /// <remarks>
    /// A search only ever meets the instances in the navmesh, so identity is all it needs, and it is far cheaper than
    /// <see cref="PathPoly"/>'s own equality, which hashes and compares its grid, chunk, tile, box and data on every
    /// lookup. A rebuild replaces the polys it touches with new instances and marks the old ones invalid, which a
    /// search spanning it already checks for.
    /// </remarks>
    internal static Dictionary<PathPoly, TValue> NewPolyDictionary<TValue>()
    {
        return new Dictionary<PathPoly, TValue>(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Hands an A* request pooled search state, which goes back in <see cref="ReturnAStarSearchState"/> once it has
    /// finished.
    /// </summary>
    private void RentAStarSearchState(AStarPathRequest request)
    {
        request.CostSoFar = _polyCostPool.Get();
        request.CameFrom = _polyParentPool.Get();
        request.PolyFrontier = _polyFrontierPool.Get();
    }

    /// <summary>
    /// Gives a finished A* request's search state back to the pools. Called once nothing reads it any more - after the
    /// route debug has sent its costs - and leaves the request's references to it empty, so that anything touching it
    /// afterwards fails loudly rather than reading another search.
    /// </summary>
    private void ReturnAStarSearchState(AStarPathRequest request)
    {
        _polyCostPool.Return(request.CostSoFar);
        _polyParentPool.Return(request.CameFrom);

        if (request.PolyFrontier != null)
            _polyFrontierPool.Return(request.PolyFrontier);

        ReturnHeuristic(request);

        request.CostSoFar = default!;
        request.CameFrom = default!;
        request.PolyFrontier = null;
    }

    private sealed class PolyDictionaryPolicy<TValue> : PooledObjectPolicy<Dictionary<PathPoly, TValue>>
    {
        public override Dictionary<PathPoly, TValue> Create()
        {
            return NewPolyDictionary<TValue>();
        }

        public override bool Return(Dictionary<PathPoly, TValue> dictionary)
        {
            dictionary.Clear();
            return true;
        }
    }

    private sealed class PathPolyFrontierPolicy : PooledObjectPolicy<PathPolyFrontier>
    {
        public override PathPolyFrontier Create()
        {
            return new PathPolyFrontier();
        }

        public override bool Return(PathPolyFrontier frontier)
        {
            frontier.Clear();
            return true;
        }
    }
}
