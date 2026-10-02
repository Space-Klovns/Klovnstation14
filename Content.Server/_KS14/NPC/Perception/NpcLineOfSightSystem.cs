using System.Numerics;
using Content.Shared.Examine;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Sight and clearance for NPCs. Unoccluded line of sight, without allocating: what perception sees with, and what
///         squad tactics clear search points with - one implementation, so the two never disagree about what can be
///         seen. And how far something could travel in a straight line before running into something solid.
/// </summary>
public sealed partial class NpcLineOfSightSystem : EntitySystem
{
    [Dependency] private OccluderSystem _occluderSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<OccluderComponent> _occluderQuery = default!;
    [Dependency] private EntityQuery<OccluderTreeComponent> _occluderTreeQuery = default!;

    /// <summary>
    ///     The occluder trees one ray crosses. Reused, so it stops allocating once grown.
    /// </summary>
    private readonly List<Entity<OccluderTreeComponent>> _lineOfSightTrees = new();

    /// <summary>
    ///     The occluders one ray hits. Reused, as above.
    /// </summary>
    private readonly List<EntityUid> _lineOfSightHits = new();

    /// <summary>
    ///     The same answer as <see cref="ExamineSystemShared.InRangeUnOccluded(MapCoordinates, MapCoordinates, float, ExamineSystemShared.Ignored?)"/>
    ///         with no predicate, without its allocation: the engine's occluder ray query builds a fresh list of the
    ///         trees it crosses on every call, about 100 bytes, and perception casts several rays per NPC five times a
    ///         second. This collects the trees into a reused list and walks them itself.
    /// </summary>
    /// <remarks>
    ///     Main thread only: the scratch lists are fields on the system, shared by every call. NPC systems never run
    ///         anywhere else.
    /// </remarks>
    public bool InLineOfSight(MapCoordinates origin, MapCoordinates other, float range)
    {
        if (other.MapId != origin.MapId || other.MapId == MapId.Nullspace)
            return false;

        var direction = other.Position - origin.Position;
        var length = direction.Length();

        // The same rounding allowance the examine check gives.
        if (range > 0f && length > range + 0.01f)
            return false;

        if (MathHelper.CloseTo(length, 0f))
            return true;

        length = MathF.Min(length, ExamineSystemShared.MaxRaycastRange);
        var ray = new Ray(origin.Position, direction / length);
        var end = origin.Position + ray.Direction * length;
        var bounds = new Box2(Vector2.Min(origin.Position, end), Vector2.Max(origin.Position, end));

        // Trees can only be queried with their pending moves applied; the engine's own queries do this first too.
        _occluderSystem.UpdateTreePositions();

        _lineOfSightTrees.Clear();
        var gridState = (_lineOfSightTrees, _occluderTreeQuery);
        _mapSystem.FindGridsIntersecting(origin.MapId,
            bounds,
            ref gridState,
            static (EntityUid gridUid, MapGridComponent _, ref (List<Entity<OccluderTreeComponent>> Trees, EntityQuery<OccluderTreeComponent> TreeQuery) state) =>
            {
                if (state.TreeQuery.TryComp(gridUid, out var treeComponent))
                    state.Trees.Add((gridUid, treeComponent));

                return true;
            },
            includeMap: false);

        if (_mapSystem.TryGetMap(origin.MapId, out var mapUid) &&
            _occluderTreeQuery.TryComp(mapUid, out var mapTreeComponent) &&
            mapTreeComponent.Tree.Count != 0)
            _lineOfSightTrees.Add((mapUid.Value, mapTreeComponent));

        _lineOfSightHits.Clear();
        foreach (var (treeUid, treeComponent) in _lineOfSightTrees)
        {
            var (_, treeRotation, invMatrix) = _transformSystem.GetWorldPositionRotationInvMatrix(treeUid);
            var treeRay = new Ray(Vector2.Transform(ray.Position, invMatrix), new Angle(-treeRotation.Theta).RotateVec(ray.Direction));

            var hitState = (_lineOfSightHits, length);
            treeComponent.Tree.QueryRay(ref hitState,
                static (ref (List<EntityUid> Hits, float MaxLength) state, in ComponentTreeEntry<OccluderComponent> value, in Vector2 _, float distance) =>
                {
                    if (distance <= state.MaxLength)
                        state.Hits.Add(value.Uid);

                    return true;
                },
                treeRay);
        }

        // As the examine check: an occluder does not block a ray that starts or ends inside it.
        foreach (var hitUid in _lineOfSightHits)
        {
            if (!_occluderQuery.TryComp(hitUid, out var occluderComponent) ||
                !TryComp(hitUid, out TransformComponent? hitTransform))
                return false;

            if (_occluderSystem.ContainsPoint(occluderComponent, hitTransform, origin.Position) ||
                _occluderSystem.ContainsPoint(occluderComponent, hitTransform, other.Position))
                continue;

            return false;
        }

        return true;
    }

    /// <summary>
    ///     How far from <paramref name="origin"/> along <paramref name="direction"/>, up to
    ///         <paramref name="maxDistance"/>, before the first thing that collides with <paramref name="collisionMask"/>.
    ///         <paramref name="maxDistance"/> if nothing is in the way.
    /// </summary>
    /// <remarks>
    ///     Allocates: the physics ray query builds its results. Only call it on demand - planning a dive, guessing
    ///         where a lost hostile went - not every update.
    /// </remarks>
    public float GetClearDistance(MapCoordinates origin, Vector2 direction, float maxDistance, int collisionMask)
    {
        if (origin.MapId == MapId.Nullspace || direction.LengthSquared() < 0.0001f || maxDistance <= 0f)
            return 0f;

        var ray = new CollisionRay(origin.Position, Vector2.Normalize(direction), collisionMask);

        // Every hit, not the first: the first is the first the broadphase came across, not the nearest.
        var nearestHit = maxDistance;
        foreach (var hit in _physicsSystem.IntersectRay(origin.MapId, ray, maxDistance, returnOnFirstHit: false))
        {
            nearestHit = MathF.Min(nearestHit, hit.Distance);
        }

        return nearestHit;
    }
}
