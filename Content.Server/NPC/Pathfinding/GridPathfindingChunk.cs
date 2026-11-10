using Content.Server._KS14.NPC.Pathfinding; // KS14
using Content.Shared.NPC;

namespace Content.Server.NPC.Pathfinding;

public sealed class GridPathfindingChunk
{
    // TODO: Make this a 1d array
    [ViewVariables]
    public readonly PathfindingBreadcrumb[,] Points = new PathfindingBreadcrumb[
        (SharedPathfindingSystem.ChunkSize) * SharedPathfindingSystem.SubStep,
        (SharedPathfindingSystem.ChunkSize) * SharedPathfindingSystem.SubStep];

    [ViewVariables]
    public Vector2i Origin;

    [ViewVariables]
    public readonly List<PathPoly>[] Polygons = new List<PathPoly>[SharedPathfindingSystem.ChunkSize * SharedPathfindingSystem.ChunkSize];

    /// <summary>
    /// Store the recalculated polygons to know what needs changing.
    /// </summary>
    internal readonly List<PathPoly>[] BufferPolygons = new List<PathPoly>[SharedPathfindingSystem.ChunkSize * SharedPathfindingSystem.ChunkSize];

    /// <summary>
    /// The relevant polygon for this chunk's portals
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<PathPortal, PathPoly> PortalPolys = new();

    /// <summary>
    /// This chunk's portals.
    /// </summary>
    [ViewVariables]
    public readonly List<PathPortal> Portals = new();

    // KS14 start
    /// <summary>
    /// This chunk's coarse maps, one per path profile, built as searches need them. Dropped whenever it or a neighbour
    /// is rebuilt. Built and read on the search threads, under a lock on the chunk; dropped on the main thread, when
    /// no search is running. See PathfindingSystem.Klovn.Hierarchy.cs.
    /// </summary>
    public List<PathChunkAbstraction>? KsAbstractions;
    // KS14 end

    public GridPathfindingChunk()
    {
        for (var x = 0; x < Polygons.Length; x++)
        {
            Polygons[x] = new List<PathPoly>();
            BufferPolygons[x] = new List<PathPoly>();
        }
    }
}
