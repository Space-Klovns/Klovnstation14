using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared._KS14.Occlusion;
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
/// <remarks>
///     On a grid, line of sight walks the tiles a ray crosses and tests the occluders anchored on them, read live from
///         the grid's own index of what is anchored where: nothing is cached, so a door shutting or a wall going up is
///         seen at once. The few occluders that walk cannot find - unanchored, or reaching past their own tile - are
///         marked with <see cref="NpcIrregularOccluderComponent"/> and tested on their own. Anything not on a grid, and
///         any grid whose map has not been initialised (and so whose occluders are not marked yet), falls back to the
///         occluder tree. The answer is the same as <see cref="InLineOfSightByOccluderTree"/>'s, save for a ray lying
///         exactly along a tile edge, which the tree wrongly finds blocked by walls anywhere on that line: see
///         <see cref="SegmentTouches"/>.
/// </remarks>
public sealed partial class NpcLineOfSightSystem : EntitySystem
{
    [Dependency] private OccluderSystem _occluderSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<NpcIrregularOccluderComponent> _irregularOccluderQuery = default!;
    [Dependency] private EntityQuery<OccluderComponent> _occluderQuery = default!;
    [Dependency] private EntityQuery<OccluderTreeComponent> _occluderTreeQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    /// <summary>
    ///     How far, in tiles, a ray may pass from a tile and still have it walked. Walking a few tiles too many costs a
    ///         lookup each; missing one a ray only grazes - a diagonal through a corner, a ray along a tile edge - would
    ///         see through a wall the occluder tree says blocks it. Comfortably above float error over a ray's length.
    /// </summary>
    private const float TileWalkSlack = 1e-3f;

    /// <summary>
    ///     How far, in tiles, an anchored occluder's bounds may reach past its own tile and still be found by the walk.
    /// </summary>
    private const float OwnTileSlack = 1e-3f;

    /// <summary>
    ///     A ray direction component smaller than this is taken as zero: the ray is parallel to the other axis.
    /// </summary>
    private const float AxisParallel = 1e-6f;

    /// <summary>
    ///     How far, in radians, a sight field widens the directions each occluder covers, so a ray only grazing one
    ///         still tests it. Far above the rounding in working out a direction.
    /// </summary>
    private const float SightFieldAngleSlack = 1e-3f;

    /// <summary>
    ///     The furthest a sight field gathers for. Past this, gathering everything in range costs more than it saves,
    ///         and a field falls back to ordinary checks.
    /// </summary>
    private const float SightFieldMaxRange = 32f;

    /// <summary>
    ///     The slices each occluder in the field being built covers, first to last; the last may run past the end and
    ///         wraps round. Reused.
    /// </summary>
    private readonly List<(int First, int Last)> _sightFieldSpans = new();

    /// <summary>
    ///     The occluder trees one ray crosses, with the ray in each one's frame. Reused, so it stops allocating once
    ///         grown.
    /// </summary>
    private readonly List<TreeRay> _lineOfSightTrees = new();

    public override void Initialize()
    {
        base.Initialize();

        // A global hook rather than a subscription: the occluder tree system already takes OccluderComponent's
        //      MoveEvent, and only one system may.
        _transformSystem.OnGlobalMoveEvent += OnGlobalMove;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _transformSystem.OnGlobalMoveEvent -= OnGlobalMove;
    }

    #region Irregular occluders

    [SubscribeLocalEvent]
    private void OnOccluderMapInit(Entity<OccluderComponent> entity, ref MapInitEvent args)
    {
        UpdateIrregular(entity.Owner, entity.Comp, Transform(entity));
    }

    [SubscribeLocalEvent]
    private void OnOccluderAnchorChanged(Entity<OccluderComponent> entity, ref AnchorStateChangedEvent args)
    {
        if (args.Detaching)
            return;

        UpdateIrregular(entity.Owner, entity.Comp, args.Transform);
    }

    private void OnGlobalMove(ref MoveEvent args)
    {
        // An unanchored occluder is marked already, wherever it goes. An anchored one moves when it is turned or
        //      re-anchored, either of which can take it past its own tile.
        if (!args.Component.Anchored || !_occluderQuery.TryComp(args.Sender, out var occluderComponent))
            return;

        UpdateIrregular(args.Sender, occluderComponent, args.Component);
    }

    private void UpdateIrregular(EntityUid uid, OccluderComponent occluderComponent, TransformComponent transformComponent)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (!transformComponent.Anchored || !FitsOwnTile(occluderComponent, transformComponent))
            EnsureComp<NpcIrregularOccluderComponent>(uid);
        else if (_irregularOccluderQuery.HasComp(uid))
            RemComp<NpcIrregularOccluderComponent>(uid);
    }

    /// <summary>
    ///     Whether an anchored occluder's bounds stay within the tile it is anchored on, where walking that tile finds it.
    /// </summary>
    private bool FitsOwnTile(OccluderComponent occluderComponent, TransformComponent transformComponent)
    {
        if (transformComponent.GridUid is not { } gridUid ||
            transformComponent.ParentUid != gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return false;

        var tileSize = (float) mapGridComponent.TileSize;
        var tile = _mapSystem.LocalToTile(gridUid, mapGridComponent, transformComponent.Coordinates);
        var bounds = GetTreeBounds(occluderComponent, transformComponent.LocalPosition, transformComponent.LocalRotation);
        var slack = OwnTileSlack * tileSize;

        return bounds.Left >= tile.X * tileSize - slack &&
            bounds.Bottom >= tile.Y * tileSize - slack &&
            bounds.Right <= (tile.X + 1) * tileSize + slack &&
            bounds.Top <= (tile.Y + 1) * tileSize + slack;
    }

    #endregion

    #region Line of sight

    /// <summary>
    ///     The same answer as <see cref="ExamineSystemShared.InRangeUnOccluded(MapCoordinates, MapCoordinates, float, ExamineSystemShared.Ignored?)"/>
    ///         with no predicate, without its allocation, and on a grid without the occluder tree: see the remarks on
    ///         <see cref="NpcLineOfSightSystem"/>.
    /// </summary>
    /// <remarks>
    ///     Main thread only: the scratch list is a field on the system, shared by every call. NPC systems never run
    ///         anywhere else.
    /// </remarks>
    public bool InLineOfSight(MapCoordinates origin, MapCoordinates other, float range)
    {
        return InLineOfSight(origin, other, range, walkTiles: true);
    }

    /// <summary>
    ///     <see cref="InLineOfSight(MapCoordinates, MapCoordinates, float)"/> through the occluder tree alone, as it
    ///         worked before the tile walk. What the walk is checked against.
    /// </summary>
    internal bool InLineOfSightByOccluderTree(MapCoordinates origin, MapCoordinates other, float range)
    {
        return InLineOfSight(origin, other, range, walkTiles: false);
    }

    private bool InLineOfSight(MapCoordinates origin, MapCoordinates other, float range, bool walkTiles)
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

        // The direction before the length is capped, or it is no longer a unit vector.
        var ray = new Ray(origin.Position, direction / length);
        length = MathF.Min(length, ExamineSystemShared.MaxRaycastRange);
        var end = origin.Position + ray.Direction * length;
        var bounds = new Box2(Vector2.Min(origin.Position, end), Vector2.Max(origin.Position, end));

        // Trees can only be queried with their pending moves applied; the engine's own queries do this first too. The
        //      walk needs it as much: it reads which tree each occluder is in to tell whether the tree has it.
        _occluderSystem.UpdateTreePositions();

        _lineOfSightTrees.Clear();
        var gridState = (_lineOfSightTrees, _occluderTreeQuery);
        _mapSystem.FindGridsIntersecting(origin.MapId,
            bounds,
            ref gridState,
            static (EntityUid gridUid, MapGridComponent _, ref (List<TreeRay> Trees, EntityQuery<OccluderTreeComponent> TreeQuery) state) =>
            {
                if (state.TreeQuery.TryComp(gridUid, out var treeComponent))
                    state.Trees.Add(new TreeRay(gridUid, treeComponent));

                return true;
            },
            includeMap: false);

        if (_mapSystem.TryGetMap(origin.MapId, out var mapUid) &&
            _occluderTreeQuery.TryComp(mapUid, out var mapTreeComponent) &&
            mapTreeComponent.Tree.Count != 0)
            _lineOfSightTrees.Add(new TreeRay(mapUid.Value, mapTreeComponent));

        // Occluders are marked at map init, so only an initialised map's are all marked.
        var mapInitialized = _mapSystem.IsInitialized(origin.MapId);

        var anyWalked = false;
        for (var i = 0; i < _lineOfSightTrees.Count; i++)
        {
            var treeRay = _lineOfSightTrees[i];
            var (_, treeRotation, invMatrix) = _transformSystem.GetWorldPositionRotationInvMatrix(treeRay.Uid);
            treeRay.Ray = new Ray(Vector2.Transform(ray.Position, invMatrix), new Angle(-treeRotation.Theta).RotateVec(ray.Direction));

            MapGridComponent? mapGridComponent = null;
            treeRay.Walked = walkTiles &&
                mapInitialized &&
                _mapGridQuery.TryComp(treeRay.Uid, out mapGridComponent);
            _lineOfSightTrees[i] = treeRay;

            if (treeRay.Walked)
            {
                anyWalked = true;
                if (TileWalkBlocks((treeRay.Uid, mapGridComponent!), treeRay.Ray, length, origin.Position, other.Position))
                    return false;

                continue;
            }

            if (TreeQueryBlocks(treeRay, length, origin.Position, other.Position, discardUnreachable: walkTiles))
                return false;
        }

        return !anyWalked || !IrregularOccludersBlock(length, origin.Position, other.Position);
    }

    /// <summary>
    ///     Whether an occluder in <paramref name="treeRay"/>'s tree blocks it, through the tree. Stops at the first
    ///         occluder that blocks, rather than collecting every hit and checking them after.
    /// </summary>
    /// <param name="discardUnreachable">
    ///     Whether to throw out hits the ray cannot reach, which the tree reports for a ray along a tile edge: see
    ///         <see cref="SegmentTouches"/>. Off only for <see cref="InLineOfSightByOccluderTree"/>, which is the tree as
    ///         it is.
    /// </param>
    private bool TreeQueryBlocks(TreeRay treeRay, float length, Vector2 origin, Vector2 other, bool discardUnreachable)
    {
        var hitState = new RayHitState(this, origin, other, length, treeRay.Ray, discardUnreachable);
        treeRay.Tree.Tree.QueryRay(ref hitState,
            static (ref RayHitState state, in ComponentTreeEntry<OccluderComponent> value, in Vector2 _, float distance) =>
            {
                if (distance > state.MaxLength ||
                    state.DiscardUnreachable &&
                    !SegmentTouches(state.Ray, state.MaxLength, GetTreeBounds(value.Component, value.Transform.LocalPosition, value.Transform.LocalRotation)) ||
                    !state.System.Blocks(value.Uid, value.Component, state.Origin, state.Other))
                    return true;

                state.Blocked = true;
                return false;
            },
            treeRay.Ray);

        return hitState.Blocked;
    }

    /// <summary>
    ///     Whether an occluder anchored on a tile <paramref name="ray"/> crosses, in <paramref name="grid"/>'s frame,
    ///         blocks it. Sweeps the ray's tiles a column at a time: in each column, the rows between where the ray
    ///         enters it and where it leaves, widened by <see cref="TileWalkSlack"/>.
    /// </summary>
    private bool TileWalkBlocks(Entity<MapGridComponent> grid, Ray ray, float length, Vector2 origin, Vector2 other)
    {
        var tileSize = (float) grid.Comp.TileSize;
        var start = ray.Position / tileSize;
        var end = (ray.Position + ray.Direction * length) / tileSize;

        var minX = MathF.Min(start.X, end.X);
        var maxX = MathF.Max(start.X, end.X);
        var deltaX = end.X - start.X;
        var slope = MathF.Abs(deltaX) > 1e-6f ? (end.Y - start.Y) / deltaX : 0f;
        var vertical = MathF.Abs(deltaX) <= 1e-6f;

        var lastColumn = (int) MathF.Floor(maxX + TileWalkSlack);
        for (var column = (int) MathF.Floor(minX - TileWalkSlack); column <= lastColumn; column++)
        {
            float lowY;
            float highY;

            if (vertical)
            {
                lowY = MathF.Min(start.Y, end.Y);
                highY = MathF.Max(start.Y, end.Y);
            }
            else
            {
                var fromY = start.Y + (MathF.Max(column - TileWalkSlack, minX) - start.X) * slope;
                var toY = start.Y + (MathF.Min(column + 1 + TileWalkSlack, maxX) - start.X) * slope;
                lowY = MathF.Min(fromY, toY);
                highY = MathF.Max(fromY, toY);
            }

            var lastRow = (int) MathF.Floor(highY + TileWalkSlack);
            for (var row = (int) MathF.Floor(lowY - TileWalkSlack); row <= lastRow; row++)
            {
                if (TileBlocks(grid, new Vector2i(column, row), ray, length, origin, other))
                    return true;
            }
        }

        return false;
    }

    private bool TileBlocks(Entity<MapGridComponent> grid, Vector2i tile, Ray ray, float length, Vector2 origin, Vector2 other)
    {
        var anchoredEnumerator = _mapSystem.GetAnchoredEntities(grid.Owner, grid.Comp, tile);
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            // In the grid's tree is what the tree query would have found: enabled, and with its last move applied.
            if (!_occluderQuery.TryComp(anchoredUid, out var occluderComponent) ||
                occluderComponent.TreeUid != grid.Owner ||
                !_transformQuery.TryComp(anchoredUid.Value, out var occluderTransform))
                continue;

            var bounds = GetTreeBounds(occluderComponent, occluderTransform.LocalPosition, occluderTransform.LocalRotation);
            if (SegmentTouches(ray, length, bounds) &&
                Blocks(anchoredUid.Value, occluderComponent, origin, other))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether one of the occluders the tile walk cannot find blocks the ray, in any of the walked trees.
    /// </summary>
    private bool IrregularOccludersBlock(float length, Vector2 origin, Vector2 other)
    {
        // All of them, paused or not, as the tree has them all.
        var irregularEnumerator = AllEntityQuery<NpcIrregularOccluderComponent, OccluderComponent, TransformComponent>();
        while (irregularEnumerator.MoveNext(out var occluderUid, out _, out var occluderComponent, out var occluderTransform))
        {
            if (occluderComponent.TreeUid is not { } occluderTreeUid)
                continue;

            foreach (var treeRay in _lineOfSightTrees)
            {
                if (treeRay.Uid != occluderTreeUid || !treeRay.Walked)
                    continue;

                var (position, rotation) = occluderTransform.ParentUid == occluderTreeUid
                    ? (occluderTransform.LocalPosition, occluderTransform.LocalRotation)
                    : _transformSystem.GetRelativePositionRotation(occluderTransform, occluderTreeUid);

                var bounds = GetTreeBounds(occluderComponent, position, rotation);
                if (SegmentTouches(treeRay.Ray, length, bounds) &&
                    Blocks(occluderUid, occluderComponent, origin, other))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether the first <paramref name="length"/> of <paramref name="ray"/> touches <paramref name="bounds"/>.
    /// </summary>
    /// <remarks>
    ///     The occluder tree's own test, <see cref="Ray.Intersects(Box2, out float, out Vector2)"/>, so the two agree to
    ///         the last rounding of a ray through a tile corner - except for a ray parallel to an axis, as one along a
    ///         tile edge is. That test gives such a ray a NaN or zero distance to every box on its line, behind its start
    ///         and past its end included, so the tree finds a wall anywhere along the line in the way. Those get a plain
    ///         slab test of the segment against the box, edges included.
    /// </remarks>
    private static bool SegmentTouches(Ray ray, float length, Box2 bounds)
    {
        if (MathF.Abs(ray.Direction.X) >= AxisParallel && MathF.Abs(ray.Direction.Y) >= AxisParallel)
            return ray.Intersects(bounds, out var distance, out _) && distance <= length;

        return KsSegmentBounds.Touches(ray.Position, ray.Position + ray.Direction * length, bounds);
    }

    /// <summary>
    ///     An occluder's bounds in its tree's frame, worked out as the occluder tree works them out.
    /// </summary>
    private static Box2 GetTreeBounds(OccluderComponent occluderComponent, Vector2 position, Angle rotation)
    {
        return new Box2Rotated(occluderComponent.LocalBounds.Translated(position), rotation, position).CalcBoundingBox();
    }

    /// <summary>
    ///     Whether an occluder a ray crosses blocks it. As the examine check: one does not block a ray that starts or
    ///         ends inside it.
    /// </summary>
    private bool Blocks(EntityUid occluderUid, OccluderComponent occluderComponent, Vector2 origin, Vector2 other)
    {
        if (!_transformQuery.TryComp(occluderUid, out var occluderTransform))
            return true;

        return !_occluderSystem.ContainsPoint(occluderComponent, occluderTransform, origin) &&
            !_occluderSystem.ContainsPoint(occluderComponent, occluderTransform, other);
    }

    #region Sight fields

    /// <summary>
    ///     Gathers into <paramref name="field"/> everything that could block sight from <paramref name="origin"/>
    ///         within <paramref name="range"/>, for checking many targets from there with
    ///         <see cref="InLineOfSight(NpcSightField, MapCoordinates)"/>.
    /// </summary>
    /// <remarks>
    ///     Gathers what the tile walk would find for every ray out of the viewpoint - the occluders anchored on every
    ///         tile in range, and the irregular ones - and files each under the directions from the viewpoint it
    ///         covers. A field is only built where the walk alone answers for every such ray: on an initialised map, on
    ///         a grid, with no other grid or occluders off a grid in range. Anywhere else it falls back to checking
    ///         each target the ordinary way.
    /// </remarks>
    public void BuildSightField(MapCoordinates origin, float range, NpcSightField field)
    {
        field.Origin = origin;
        field.Range = range;
        field.Fallback = true;
        field.Occluders.Clear();
        field.Surrounding.Clear();
        field.BucketItems.Clear();
        Array.Clear(field.BucketStarts);

        if (origin.MapId == MapId.Nullspace ||
            range <= 0f ||
            range > SightFieldMaxRange ||
            !_mapSystem.IsInitialized(origin.MapId) ||
            !_mapSystem.TryFindGridAt(origin, out var gridUid, out var mapGridComponent))
            return;

        // As every ray's own check does first.
        _occluderSystem.UpdateTreePositions();

        // How far any ray in range can reach, with the range check's rounding allowance and some to spare.
        var reach = range + 0.02f;
        var worldBounds = new Box2(origin.Position - new Vector2(reach), origin.Position + new Vector2(reach));

        var otherState = (GridUid: gridUid, TreeQuery: _occluderTreeQuery, Other: false);
        _mapSystem.FindGridsIntersecting(origin.MapId,
            worldBounds,
            ref otherState,
            static (EntityUid otherGridUid, MapGridComponent _, ref (EntityUid GridUid, EntityQuery<OccluderTreeComponent> TreeQuery, bool Other) state) =>
            {
                if (otherGridUid == state.GridUid ||
                    !state.TreeQuery.TryComp(otherGridUid, out var treeComponent) ||
                    treeComponent.Tree.Count == 0)
                    return true;

                state.Other = true;
                return false;
            },
            includeMap: false);

        if (otherState.Other ||
            _mapSystem.TryGetMap(origin.MapId, out var mapUid) &&
            _occluderTreeQuery.TryComp(mapUid, out var mapTreeComponent) &&
            mapTreeComponent.Tree.Count != 0)
            return;

        field.Fallback = false;
        field.GridUid = gridUid;
        (_, field.GridRotation, field.InvWorldMatrix) = _transformSystem.GetWorldPositionRotationInvMatrix(gridUid);

        // In the grid's frame from here, as each ray is tested.
        var viewpoint = Vector2.Transform(origin.Position, field.InvWorldMatrix);
        var tileSize = (float) mapGridComponent.TileSize;
        var firstTile = new Vector2i((int) MathF.Floor((viewpoint.X - reach) / tileSize), (int) MathF.Floor((viewpoint.Y - reach) / tileSize));
        var lastTile = new Vector2i((int) MathF.Floor((viewpoint.X + reach) / tileSize), (int) MathF.Floor((viewpoint.Y + reach) / tileSize));

        for (var x = firstTile.X; x <= lastTile.X; x++)
        {
            for (var y = firstTile.Y; y <= lastTile.Y; y++)
            {
                var anchoredEnumerator = _mapSystem.GetAnchoredEntities(gridUid, mapGridComponent, new Vector2i(x, y));
                while (anchoredEnumerator.MoveNext(out var anchoredUid))
                {
                    // The same occluders, by the same test, as the tile walk.
                    if (!_occluderQuery.TryComp(anchoredUid, out var occluderComponent) ||
                        occluderComponent.TreeUid != gridUid ||
                        !_transformQuery.TryComp(anchoredUid.Value, out var occluderTransform))
                        continue;

                    AddToSightField(field, viewpoint, reach, anchoredUid.Value, occluderComponent,
                        GetTreeBounds(occluderComponent, occluderTransform.LocalPosition, occluderTransform.LocalRotation));
                }
            }
        }

        var irregularEnumerator = AllEntityQuery<NpcIrregularOccluderComponent, OccluderComponent, TransformComponent>();
        while (irregularEnumerator.MoveNext(out var occluderUid, out _, out var occluderComponent, out var occluderTransform))
        {
            if (occluderComponent.TreeUid != gridUid)
                continue;

            var (position, rotation) = occluderTransform.ParentUid == gridUid
                ? (occluderTransform.LocalPosition, occluderTransform.LocalRotation)
                : _transformSystem.GetRelativePositionRotation(occluderTransform, gridUid);

            AddToSightField(field, viewpoint, reach, occluderUid, occluderComponent, GetTreeBounds(occluderComponent, position, rotation));
        }

        IndexSightField(field, viewpoint);
    }

    private static void AddToSightField(NpcSightField field, Vector2 viewpoint, float reach, EntityUid uid, OccluderComponent occluderComponent, Box2 bounds)
    {
        // Out of reach of every ray in range.
        var nearest = Vector2.Clamp(viewpoint, bounds.BottomLeft, bounds.TopRight);
        if ((nearest - viewpoint).LengthSquared() > reach * reach)
            return;

        field.Occluders.Add((uid, occluderComponent, bounds));
    }

    /// <summary>
    ///     Files each of the field's occluders under the slices of direction from <paramref name="viewpoint"/> it covers.
    /// </summary>
    private void IndexSightField(NpcSightField field, Vector2 viewpoint)
    {
        const int buckets = NpcSightField.Buckets;
        const float bucketsPerRadian = buckets / MathF.Tau;

        _sightFieldSpans.Clear();
        var counts = field.BucketStarts;

        for (var i = 0; i < field.Occluders.Count; i++)
        {
            var bounds = field.Occluders[i].Bounds;

            // Round the viewpoint, or too near it to tell: in every direction.
            if (bounds.Enlarged(SightFieldAngleSlack).Contains(viewpoint))
            {
                field.Surrounding.Add(i);
                _sightFieldSpans.Add((0, -1));
                continue;
            }

            // A box the viewpoint is outside of spans less than half a turn, so its corners' directions, measured from
            //      its centre's, give the directions it covers without wrapping round.
            var centre = bounds.Center - viewpoint;
            var centreAngle = MathF.Atan2(centre.Y, centre.X);
            var lowest = float.MaxValue;
            var highest = float.MinValue;

            Widen(bounds.BottomLeft);
            Widen(bounds.BottomRight);
            Widen(bounds.TopLeft);
            Widen(bounds.TopRight);

            var first = (int) MathF.Floor((centreAngle + lowest - SightFieldAngleSlack + MathF.PI) * bucketsPerRadian);
            var last = (int) MathF.Floor((centreAngle + highest + SightFieldAngleSlack + MathF.PI) * bucketsPerRadian);

            if (last - first >= buckets - 1)
            {
                field.Surrounding.Add(i);
                _sightFieldSpans.Add((0, -1));
                continue;
            }

            _sightFieldSpans.Add((first, last));
            for (var bucket = first; bucket <= last; bucket++)
            {
                counts[WrapBucket(bucket)]++;
            }

            continue;

            void Widen(Vector2 corner)
            {
                var offset = corner - viewpoint;
                var angle = MathF.Atan2(offset.Y, offset.X) - centreAngle;
                if (angle > MathF.PI)
                    angle -= MathF.Tau;
                else if (angle < -MathF.PI)
                    angle += MathF.Tau;

                lowest = MathF.Min(lowest, angle);
                highest = MathF.Max(highest, angle);
            }
        }

        // Counts into starts, shifted one along so filling can bump each start up to the next slice's.
        var running = 0;
        for (var bucket = 0; bucket < buckets; bucket++)
        {
            var count = counts[bucket];
            counts[bucket] = running;
            running += count;
        }

        counts[buckets] = running;
        CollectionsMarshal.SetCount(field.BucketItems, running);
        var items = CollectionsMarshal.AsSpan(field.BucketItems);

        for (var i = 0; i < _sightFieldSpans.Count; i++)
        {
            var (first, last) = _sightFieldSpans[i];
            for (var bucket = first; bucket <= last; bucket++)
            {
                items[counts[WrapBucket(bucket)]++] = i;
            }
        }

        // Filling moved each start to its slice's end, which is the next slice's start: shift them all back one.
        for (var bucket = buckets; bucket > 0; bucket--)
        {
            counts[bucket] = counts[bucket - 1];
        }

        counts[0] = 0;
    }

    private static int WrapBucket(int bucket)
    {
        return (bucket % NpcSightField.Buckets + NpcSightField.Buckets) % NpcSightField.Buckets;
    }

    /// <summary>
    ///     Whether <paramref name="other"/> is in line of sight of the viewpoint <paramref name="field"/> was built for,
    ///         within its range. The same answer as
    ///         <see cref="InLineOfSight(MapCoordinates, MapCoordinates, float)"/> from there, as long as nothing that
    ///         occludes has changed since the field was built.
    /// </summary>
    public bool InLineOfSight(NpcSightField field, MapCoordinates other)
    {
        var origin = field.Origin;
        if (field.Fallback)
            return InLineOfSight(origin, other, field.Range);

        if (other.MapId != origin.MapId)
            return false;

        // From here on, exactly as InLineOfSight works the ray out.
        var direction = other.Position - origin.Position;
        var length = direction.Length();

        if (length > field.Range + 0.01f)
            return false;

        if (MathHelper.CloseTo(length, 0f))
            return true;

        // The direction before the length is capped, or it is no longer a unit vector.
        var ray = new Ray(origin.Position, direction / length);
        length = MathF.Min(length, ExamineSystemShared.MaxRaycastRange);
        var gridRay = new Ray(Vector2.Transform(ray.Position, field.InvWorldMatrix), new Angle(-field.GridRotation.Theta).RotateVec(ray.Direction));

        foreach (var index in field.Surrounding)
        {
            if (SightFieldOccluderBlocks(field, index, gridRay, length, other.Position))
                return false;
        }

        var bucket = WrapBucket((int) MathF.Floor((MathF.Atan2(gridRay.Direction.Y, gridRay.Direction.X) + MathF.PI) * (NpcSightField.Buckets / MathF.Tau)));
        for (var i = field.BucketStarts[bucket]; i < field.BucketStarts[bucket + 1]; i++)
        {
            if (SightFieldOccluderBlocks(field, field.BucketItems[i], gridRay, length, other.Position))
                return false;
        }

        return true;
    }

    private bool SightFieldOccluderBlocks(NpcSightField field, int index, Ray gridRay, float length, Vector2 other)
    {
        var (uid, occluderComponent, bounds) = field.Occluders[index];
        return SegmentTouches(gridRay, length, bounds) && Blocks(uid, occluderComponent, field.Origin.Position, other);
    }

    #endregion

    private struct TreeRay(EntityUid uid, OccluderTreeComponent tree)
    {
        public readonly EntityUid Uid = uid;
        public readonly OccluderTreeComponent Tree = tree;
        public Ray Ray;

        /// <summary>
        ///     Whether this tree was walked tile by tile, rather than queried.
        /// </summary>
        public bool Walked;
    }

    private struct RayHitState(NpcLineOfSightSystem system, Vector2 origin, Vector2 other, float maxLength, Ray ray, bool discardUnreachable)
    {
        public readonly NpcLineOfSightSystem System = system;
        public readonly Vector2 Origin = origin;
        public readonly Vector2 Other = other;
        public readonly float MaxLength = maxLength;
        public readonly Ray Ray = ray;
        public readonly bool DiscardUnreachable = discardUnreachable;
        public bool Blocked;
    }

    #endregion

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
