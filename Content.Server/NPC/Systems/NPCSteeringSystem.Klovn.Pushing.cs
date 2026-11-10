// KS14: added in this fork
using Content.Server._KS14.NPC.Pushing;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.NPC;
using Robust.Shared.Physics;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Pushing loose things - closets, crates - out of the way, on a tile the navmesh marked as blocked only by them
///         (<see cref="PathfindingBreadcrumbFlag.Pushable"/>). See <see cref="NpcPushSystem"/>.
/// </summary>
public sealed partial class NPCSteeringSystem
{
    [Dependency] private NpcPushSystem _npcPushSystem = default!;

    /// <summary>
    ///     Whether <paramref name="poly"/> is in the way only of things <paramref name="steering"/> pushes out of it.
    /// </summary>
    private static bool IsPushThrough(NPCSteeringComponent steering, PathPoly poly)
    {
        return (poly.Data.Flags & PathfindingBreadcrumbFlag.Pushable) != 0x0 &&
            (steering.Flags & PathFlags.Pushing) != 0x0;
    }

    /// <summary>
    ///     Whether <paramref name="steering"/> pushes things out of its way and something loose is on
    ///         <paramref name="poly"/> now, in the way of <paramref name="uid"/>. Looked at afresh, not taken from the
    ///         navmesh: what it pushes moves, and a tile that was free when the path was made - the next one along a
    ///         corridor - may not be by the time it gets there.
    /// </summary>
    private bool HasLooseBlocker(EntityUid uid, NPCSteeringComponent steering, PathPoly poly)
    {
        if ((steering.Flags & PathFlags.Pushing) == 0x0 ||
            !_fixturesQuery.TryComp(uid, out var fixturesComponent) ||
            !_physicsQuery.TryComp(uid, out var physicsComponent))
            return false;

        var nearbyUids = _entSetPool.Get();
        _lookup.GetLocalEntitiesIntersecting(poly.GraphUid, poly.Box.Enlarged(-0.04f), nearbyUids, flags: LookupFlags.Dynamic);

        var blocked = false;
        foreach (var nearbyUid in nearbyUids)
        {
            if (nearbyUid != uid &&
                _physicsQuery.TryComp(nearbyUid, out var nearbyBody) &&
                nearbyBody.BodyType == BodyType.Dynamic &&
                _physics.IsCurrentlyHardCollidable((uid, fixturesComponent, physicsComponent), nearbyUid))
            {
                blocked = true;
                break;
            }
        }

        _entSetPool.Return(nearbyUids);
        return blocked;
    }

    /// <summary>
    ///     Pushes what is on <paramref name="poly"/> out of the way, nearest first, until nothing is left on it. On the
    ///         first thing <paramref name="uid"/> cannot push, it drops its path for one that goes round, as for a door it
    ///         goes round (<see cref="Content.Server._KS14.NPC.Doors.NpcDoorSystem.TryDetourAroundDoor"/>). If there is
    ///         no way round, that path is not found, and steering gives up.
    /// </summary>
    private SteeringObstacleStatus TryPushObstacles(EntityUid uid, NPCSteeringComponent steering, PathPoly poly)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixturesComponent))
            return SteeringObstacleStatus.Failed;

        var (layer, mask) = _physics.GetHardCollision(uid, fixturesComponent);
        var nearbyUids = _entSetPool.Get();
        _lookup.GetLocalEntitiesIntersecting(poly.GraphUid, poly.Box.Enlarged(-0.04f), nearbyUids, flags: LookupFlags.Dynamic);

        var ourPosition = _transform.GetWorldPosition(uid);
        EntityUid? nearestUid = null;
        var nearestDistance = float.MaxValue;
        EntityUid? unpushableUid = null;

        foreach (var nearbyUid in nearbyUids)
        {
            if (nearbyUid == uid ||
                !_physicsQuery.TryComp(nearbyUid, out var nearbyBody) ||
                !nearbyBody.Hard ||
                !nearbyBody.CanCollide ||
                nearbyBody.BodyType != BodyType.Dynamic ||
                (nearbyBody.CollisionMask & layer) == 0x0 && (nearbyBody.CollisionLayer & mask) == 0x0)
            {
                continue;
            }

            if (!_npcPushSystem.IsPushable(nearbyUid, uid))
            {
                unpushableUid = nearbyUid;
                break;
            }

            var distance = (_transform.GetWorldPosition(nearbyUid) - ourPosition).LengthSquared();
            if (distance >= nearestDistance)
                continue;

            nearestUid = nearbyUid;
            nearestDistance = distance;
        }

        _entSetPool.Return(nearbyUids);

        if (unpushableUid != null)
            return GoRound(uid, steering, unpushableUid.Value);

        // Pushed clear, or moved off by itself.
        if (nearestUid is not { } obstacleUid)
            return SteeringObstacleStatus.Completed;

        if (_npcPushSystem.TryPush(uid, obstacleUid))
            return SteeringObstacleStatus.Continuing;

        return GoRound(uid, steering, obstacleUid);
    }

    private SteeringObstacleStatus GoRound(EntityUid uid, NPCSteeringComponent steering, EntityUid obstacleUid)
    {
        _npcPushSystem.ReportUnpushable(uid, obstacleUid);
        steering.CurrentPath.Clear();
        return SteeringObstacleStatus.Continuing;
    }
}
