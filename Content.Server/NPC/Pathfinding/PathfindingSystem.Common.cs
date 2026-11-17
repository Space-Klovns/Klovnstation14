using Content.Server._KS14.NPC.Dangers; // KS14
using Content.Shared.NPC;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /*
     * Code that is common to all pathfinding methods.
     */

    /// <summary>
    /// Maximum amount of nodes we're allowed to expand.
    /// </summary>
    private const int NodeLimit = 512;

    private sealed class PathComparer : IComparer<ValueTuple<float, PathPoly>>
    {
        public int Compare((float, PathPoly) x, (float, PathPoly) y)
        {
            return y.Item1.CompareTo(x.Item1);
        }
    }

    private static readonly PathComparer PathPolyComparer = new();

    private List<PathPoly> ReconstructPath(Dictionary<PathPoly, PathPoly> path, PathPoly currentNodeRef)
    {
        var running = new List<PathPoly> { currentNodeRef };
        while (path.ContainsKey(currentNodeRef))
        {
            var previousCurrent = currentNodeRef;
            currentNodeRef = path[currentNodeRef];
            path.Remove(previousCurrent);
            running.Add(currentNodeRef);
        }

        running.Reverse();
        return running;
    }

    private float GetTileCost(PathRequest request, PathPoly start, PathPoly end)
    {
        // KS14 start: what it costs to step onto a poly is split out below, for the coarse maps
        //      (PathfindingSystem.Klovn.Hierarchy.cs), which have no request; only the avoided tiles are the request's own
        // A door this NPC has found it cannot get through is a wall to it
        if (request.AvoidedTiles != null && IsAvoided(request.AvoidedTiles, end))
            return 0f;

        var modifier = GetTileModifier(request.Flags, request.CollisionLayer, request.CollisionMask, end);
        if (modifier.Equals(0f))
            return 0f;

        // Fire and the like: see PathfindingSystem.Klovn.Danger.cs
        if (request.AvoidedDangers != NpcTileDangerKind.None)
            modifier *= GetDangerCost(request, end);

        return modifier * OctileDistance(end, start);
    }

    /// <summary>
    /// KS14: what a climb costs on top of walking, per unit of distance. Climbing takes a do-after, standing still and
    /// open, so a climber walks round unless that is much further. The poly of a railing is a quarter of a tile deep, so
    /// stepping over one costs about four tiles' walking; a table, a whole tile deep, about twelve.
    /// </summary>
    public const float ClimbCost = 12f;

    /// <summary>
    /// KS14: the weight a glass table is judged against, for whether climbing it breaks it: about a person's (a human's
    /// fixture comes to some 70). The navmesh is shared, so it is one weight for every climber.
    /// </summary>
    public const float ClimberMass = 70f;

    /// <summary>
    /// KS14: whether a climber with these flags climbs onto a climbable poly with <paramref name="polyFlags"/>: not onto
    /// something that breaks under it, a glass table, unless it has <see cref="PathFlags.ClimbingBreakables"/>.
    /// </summary>
    public static bool CanClimbOnto(PathFlags flags, PathfindingBreadcrumbFlag polyFlags)
    {
        return (polyFlags & PathfindingBreadcrumbFlag.ClimbBreaks) == 0x0 ||
            (flags & PathFlags.ClimbingBreakables) != 0x0;
    }

    /// <summary>
    /// What stepping onto <paramref name="end"/> costs per unit of distance for a request with these flags and
    /// collision, or 0 if it cannot.
    /// </summary>
    internal static float GetTileModifier(PathFlags flags, int collisionLayer, int collisionMask, PathPoly end)
    {
        // KS14 end
        var modifier = 1f;

        // TODO
        if ((end.Data.Flags & PathfindingBreadcrumbFlag.Space) != 0x0)
        {
            return 0f;
        }

        if ((collisionLayer /* KS14: request.CollisionLayer -> collisionLayer */ & end.Data.CollisionMask) != 0x0 ||
            (collisionMask /* KS14: request.CollisionMask -> collisionMask */ & end.Data.CollisionLayer) != 0x0)
        {
            var isDoor = (end.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isAccess = (end.Data.Flags & PathfindingBreadcrumbFlag.Access) != 0x0;
            var isClimb = (end.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            // KS14 start: a bolted, welded or blast door opens for nobody and pries for nothing; only smashing it is a way
            var isShut = (end.Data.Flags & (PathfindingBreadcrumbFlag.Bolted | PathfindingBreadcrumbFlag.Welded | PathfindingBreadcrumbFlag.Sealed)) != 0x0;
            if (isDoor && isShut)
            {
                if ((flags /* KS14: request.Flags -> flags */ & PathFlags.Smashing) == 0x0 || end.Data.Damage <= 0f)
                    return 0f;

                modifier += 10f + end.Data.Damage / 10f;
            }
            // Shutters: no hand opens them, so it takes prying or smashing, and nothing else gets anywhere walking up to them
            else if (isDoor && (end.Data.Flags & PathfindingBreadcrumbFlag.PryOnly) != 0x0)
            {
                if ((flags & (PathFlags.Prying | PathFlags.PryingTool)) != 0x0)
                    modifier += 10f;
                else if ((flags & PathFlags.Smashing) != 0x0 && end.Data.Damage > 0f)
                    modifier += 10f + end.Data.Damage / 10f;
                else
                    return 0f;
            }
            // KS14 end
            // TODO: Handling power + door prying
            // Door we should be able to open
            else /* KS14: added else */ if (isDoor)
            {
                if (!isAccess && (flags /* KS14: request.Flags -> flags */ & PathFlags.Interact) != 0x0)
                    modifier += 0.5f;
                else if (isAccess && (flags /* KS14: request.Flags -> flags */ & PathFlags.Prying) != 0x0)
                    modifier += 10f;
                else
                    // Last ditch—try to bump the door if it's the only feasible option.
                    modifier += 20f;
            }
            // KS14 start: pushing a closet aside is quicker, and quieter, than smashing it
            else if ((flags & PathFlags.Pushing) != 0x0 && (end.Data.Flags & PathfindingBreadcrumbFlag.Pushable) != 0x0)
            {
                modifier += PushCost;
            }
            // KS14 end
            else if ((flags /* KS14: request.Flags -> flags */ & PathFlags.Smashing) != 0x0 && end.Data.Damage > 0f)
            {
                // Breaking stuff should be usually last resort, especially because we WILL try to punch walls.
                modifier += 10f + end.Data.Damage / 10f;
            }
            else if (isClimb && (flags /* KS14: request.Flags -> flags */ & PathFlags.Climbing) != 0x0 &&
                CanClimbOnto(flags, end.Data.Flags) /* KS14: glass tables */)
            {
                modifier += ClimbCost; // KS14: was 0.5, which made climbing all but free; see ClimbCost
            }
            else
            {
                return 0f;
            }
        }

        return modifier; // KS14: modifier * OctileDistance(end, start) -> modifier, see GetTileCost
    }

    #region Simplifier

    public List<PathPoly> Simplify(List<PathPoly> vertices, float tolerance = 0)
    {
        // TODO: Needs more work
        if (vertices.Count <= 3)
            return vertices;

        var simplified = new List<PathPoly>();

        for (var i = 0; i < vertices.Count; i++)
        {
            // No wraparound for negative sooooo
            var prev = vertices[i == 0 ? vertices.Count - 1 : i - 1];
            var current = vertices[i];
            var next = vertices[(i + 1) % vertices.Count];

            var prevData = prev.Data;
            var currentData = current.Data;
            var nextData = next.Data;

            // If they collinear, continue
            if (i != 0 && i != vertices.Count - 1 &&
                prevData.Equals(currentData) &&
                currentData.Equals(nextData) &&
                IsCollinear(prev, current, next, tolerance))
            {
                continue;
            }

            simplified.Add(current);
        }

        // Farseer didn't seem to handle straight lines and nuked all points
        if (simplified.Count == 0)
        {
            simplified.Add(vertices[0]);
            simplified.Add(vertices[^1]);
        }

        // Check LOS and cut out more nodes
        // TODO: Grid cast
        // https://github.com/recastnavigation/recastnavigation/blob/c5cbd53024c8a9d8d097a4371215e3342d2fdc87/Detour/Source/DetourNavMeshQuery.cpp#L2455
        // Essentially you just do a raycast but a specialised version.

        return simplified;
    }

    private bool IsCollinear(PathPoly prev, PathPoly current, PathPoly next, float tolerance)
    {
        return FloatInRange(Area(prev, current, next), -tolerance, tolerance);
    }

    private float Area(PathPoly a, PathPoly b, PathPoly c)
    {
        var (ax, ay) = a.Box.Center;
        var (bx, by) = b.Box.Center;
        var (cx, cy) = c.Box.Center;

        return ax * (by - cy) + bx * (cy - ay) + cx * (ay - by);
    }

    private bool FloatInRange(float value, float min, float max)
    {
        return (value >= min && value <= max);
    }

    #endregion
}
