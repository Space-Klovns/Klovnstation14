using Content.Server.NPC.Pathfinding;

namespace Content.Server._KS14.NPC.Pathfinding;

/// <summary>
///     The open set of a <see cref="TacticalPathRequest"/>'s flood: a binary min-heap of polys by cost so far, cheapest
///         first. Exists so it can be pooled - the engine's <c>PriorityQueue</c> reallocates its array on
///         <c>Clear</c> and on shrinking, and .NET's is not allowed by the sandbox - and <see cref="Clear"/> here
///         keeps its capacity, so a pooled one stops allocating once it has grown to fit a flood.
/// </summary>
public sealed class TacticalFrontier
{
    private readonly List<(float Cost, PathPoly Poly)> _heap = new();

    public int Count => _heap.Count;

    public void Clear()
    {
        _heap.Clear();
    }

    public void Add(float cost, PathPoly poly)
    {
        _heap.Add((cost, poly));

        var index = _heap.Count - 1;
        while (index > 0)
        {
            var parentIndex = (index - 1) / 2;
            if (_heap[parentIndex].Cost <= _heap[index].Cost)
                break;

            (_heap[parentIndex], _heap[index]) = (_heap[index], _heap[parentIndex]);
            index = parentIndex;
        }
    }

    public PathPoly Peek()
    {
        return _heap[0].Poly;
    }

    public PathPoly Take()
    {
        var poly = _heap[0].Poly;
        var lastIndex = _heap.Count - 1;

        _heap[0] = _heap[lastIndex];
        _heap.RemoveAt(lastIndex);

        var index = 0;
        while (true)
        {
            var leftIndex = index * 2 + 1;
            if (leftIndex >= _heap.Count)
                break;

            var rightIndex = leftIndex + 1;
            var cheapestIndex = rightIndex < _heap.Count && _heap[rightIndex].Cost < _heap[leftIndex].Cost
                ? rightIndex
                : leftIndex;

            if (_heap[index].Cost <= _heap[cheapestIndex].Cost)
                break;

            (_heap[index], _heap[cheapestIndex]) = (_heap[cheapestIndex], _heap[index]);
            index = cheapestIndex;
        }

        return poly;
    }
}
