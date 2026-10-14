using System.Linq;
using System.Collections.Immutable;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Containers;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenRoomFixtureStatus : byte
{
    Observed,
    Rejected,
    InvalidInput,
    BudgetExceeded,
    UnsupportedContent,
}

/// <summary>Fresh geometry/filter observation; no joints, event vetoes, settling, door use or publication proof.</summary>
public sealed record KsProcgenRoomFixtureReport(KsProcgenRoomFixtureStatus Status, string Reason,
    int ScannedEntities, int FixturePairs, int ExpandedCells, int FloorProbes,
    IReadOnlyList<Vector2i> ReachedTerminals)
{
    public bool EngineAccessVerified => false;
    public bool Published => false;
}

/// <summary>
/// Inspects initialized owned room scenes without stepping physics or moving an actor. Continuous
/// fixture sweeps model fixed-orientation cardinal translations, with conservative full-floor bounds.
/// Doors, vaulting, joints and operational interaction are deliberately separate validation gates.
/// </summary>
public sealed partial class KsProcgenRoomFixtureSystem : EntitySystem
{
    [Dependency] private KsProcgenAssemblyStageSystem _stages = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    private sealed record Shape(EntityUid Owner, IPhysShape Geometry, Transform Pose, int Layer, int Mask);

    public KsProcgenRoomFixtureReport Check(KsProcgenAssemblyStage stage,
        int maximumFixturePairs = 16_384, int maximumScannedEntities = 131_072)
    {
        var work = new Work(maximumFixturePairs, maximumScannedEntities, 0, 0);
        if (!ValidBudget(work))
            return work.Report("InvalidRoomFixtureBudget");
        if (!TryScene(stage, work, out var scene, out var issue))
            return work.Report(issue!);
        var structuralWalls = stage.Walls.Values.ToHashSet();
        for (var first = 0; first < scene.Count; first++)
        for (var second = first + 1; second < scene.Count; second++)
        {
            var a = scene[first];
            var b = scene[second];
            // A declared wall lattice is a structural union: neighboring wall fixture skins
            // may touch/overlap. Every wall remains an obstacle for members and actor sweeps.
            if (a.Owner == b.Owner || structuralWalls.Contains(a.Owner) && structuralWalls.Contains(b.Owner))
                continue;
            if (!Pair(a, a.Pose, Vector2.Zero, b, work, out var overlaps, out issue))
                return work.Report(issue!);
            if (overlaps)
                return work.Report("RoomHardFixtureOverlap");
        }
        return work.Report("RoomMemberHardFixturesDisjoint", observed: true);
    }

    public KsProcgenRoomFixtureReport CheckCardinalAccess(KsProcgenAssemblyStage stage, EntityUid actorUid,
        Vector2i root, IReadOnlyList<Vector2i> terminals, int maximumFixturePairs = 16_384,
        int maximumScannedEntities = 131_072, int maximumExpandedCells = 4096, int maximumFloorProbes = 65_536)
    {
        var work = new Work(maximumFixturePairs, maximumScannedEntities, maximumExpandedCells, maximumFloorProbes);
        if (!ValidBudget(work) || terminals == null || terminals.Count is < 1 or > 1024)
            return work.Report("InvalidRoomFixtureInput");
        if (!TryScene(stage, work, out var scene, out var issue))
            return work.Report(issue!);
        if (stage.Terrain == null || !stage.Terrain.Floor.Contains(root) ||
            terminals.Any(cell => !stage.Terrain.Floor.Contains(cell)) ||
            !TryComp(actorUid, out MetaDataComponent? actorMetadata) ||
            actorMetadata.EntityLifeStage != EntityLifeStage.MapInitialized ||
            EntityManager.IsQueuedForDeletion(actorUid) || !TryComp(actorUid, out TransformComponent? _))
            return work.Report("InvalidRoomActorInput");
        if (_containers.TryGetContainingContainer(actorUid, out _))
            return work.Report("RoomActorContained");
        var actor = new List<Shape>();
        if (!AddShapes(actorUid, actor, out issue) || actor.Count is < 1 or > 8)
            return work.Report(issue ?? "RoomActorFixturesUnavailable");
        if (scene.Any(shape => shape.Owner == actorUid))
            return work.Report("RoomActorMustBeExternal");
        var floor = stage.Terrain.Floor;
        var blocked = stage.DeclaredBlockingCells.Union(stage.Terrain.Blocked).ToHashSet();
        var visits = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();
        var edgeCache = new Dictionary<(Vector2i, Vector2i), bool>();
        var desired = terminals.ToHashSet();
        bool Clear(Vector2i from, Vector2i to, out bool clear)
        {
            clear = false;
            if (blocked.Contains(from) || blocked.Contains(to))
                return true;
            var translation = new Vector2(to.X - from.X, to.Y - from.Y);
            foreach (var shape in actor)
            {
                var pose = new Transform(new Vector2(from.X + 0.5f, from.Y + 0.5f), shape.Pose.Quaternion2D);
                var startBounds = shape.Geometry.ComputeAABB(pose, 0);
                var endBounds = shape.Geometry.ComputeAABB(new Transform(pose.Position + translation, pose.Quaternion2D), 0);
                var left = Math.Min(startBounds.Left, endBounds.Left);
                var right = Math.Max(startBounds.Right, endBounds.Right);
                var bottom = Math.Min(startBounds.Bottom, endBounds.Bottom);
                var top = Math.Max(startBounds.Top, endBounds.Top);
                if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(bottom) || !float.IsFinite(top) ||
                    Math.Abs(left) > 1_000_001f || Math.Abs(right) > 1_000_001f ||
                    Math.Abs(bottom) > 1_000_001f || Math.Abs(top) > 1_000_001f ||
                    right - left > 16f || top - bottom > 16f)
                {
                    issue = "UnsupportedRoomActorBounds";
                    return false;
                }
                // Conservative: the entire swept AABB must be floor, including boundary contact.
                for (var y = (int) Math.Floor(bottom); y <= (int) Math.Floor(top); y++)
                for (var x = (int) Math.Floor(left); x <= (int) Math.Floor(right); x++)
                {
                    if (work.FloorProbes >= work.MaximumFloorProbes)
                    {
                        issue = "RoomFloorProbeBudget";
                        return false;
                    }
                    work.FloorProbes++;
                    if (!floor.Contains(new Vector2i(x, y)))
                        return true;
                }
                foreach (var obstacle in scene)
                {
                    if (!Pair(shape, pose, translation, obstacle, work, out var overlaps, out issue))
                        return false;
                    if (overlaps)
                        return true;
                }
            }
            clear = true;
            return true;
        }
        if (!Clear(root, root, out var rootClear))
            return work.Report(issue!);
        if (!rootClear)
            return work.Report("RoomActorRootBlocked");
        visits.Add(root);
        queue.Enqueue(root);
        var directions = new Vector2i[] { new(0, -1), new(-1, 0), new(1, 0), new(0, 1) };
        while (queue.TryDequeue(out var cell))
        {
            if (work.ExpandedCells >= work.MaximumExpandedCells)
                return work.Report("RoomAccessExpandedBudget");
            work.ExpandedCells++;
            desired.Remove(cell);
            if (desired.Count == 0)
                return work.Report("RoomFixedActorCardinalClearance", observed: true, reached: terminals);
            foreach (var direction in directions)
            {
                var next = cell + direction;
                if (!floor.Contains(next) || visits.Contains(next))
                    continue;
                if (!edgeCache.TryGetValue((cell, next), out var clear))
                {
                    if (!Clear(cell, next, out clear))
                        return work.Report(issue!);
                    edgeCache[(cell, next)] = edgeCache[(next, cell)] = clear;
                }
                if (!clear)
                    continue;
                visits.Add(next);
                queue.Enqueue(next);
            }
        }
        // Partial connectivity is never returned as a successful access witness.
        return work.Report("RoomActorTerminalsDisconnected");
    }

    private bool TryScene(KsProcgenAssemblyStage stage, Work work, out List<Shape> scene, out string? issue)
    {
        scene = new();
        issue = null;
        if (stage == null || stage.Phase != KsProcgenAssemblyStagePhase.Initialized || stage.Terrain == null ||
            !_stages.Verify(stage))
        {
            issue = "InvalidInitializedRoomScene";
            return false;
        }
        var owned = stage.SpawnedEntities.ToHashSet();
        var query = AllEntityQuery<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform))
        {
            if (work.ScannedEntities >= work.MaximumScannedEntities)
            {
                issue = "RoomEntityScanBudget";
                return false;
            }
            work.ScannedEntities++;
            if (transform.MapUid != stage.MapUid)
                continue;
            if (!owned.Contains(uid))
            {
                issue = "RoomSceneForeignEntity";
                return false;
            }
            if (uid == stage.MapUid || uid == stage.GridUid || _containers.TryGetContainingContainer(uid, out _))
                continue;
            if (!TryComp(uid, out MetaDataComponent? metadata) ||
                metadata.EntityLifeStage != EntityLifeStage.MapInitialized || EntityManager.IsQueuedForDeletion(uid))
            {
                issue = "RoomSceneLifecycleDrift";
                return false;
            }
            if (!AddShapes(uid, scene, out issue))
                return false;
        }
        return true;
    }

    private bool AddShapes(EntityUid uid, List<Shape> shapes, out string? issue)
    {
        issue = null;
        if (!TryComp<PhysicsComponent>(uid, out var body) || !body.CanCollide)
            return true;
        if (!TryComp<FixturesComponent>(uid, out var fixtures))
            return true;
        if (fixtures.Fixtures.Count > 64)
        {
            issue = "RoomFixtureCountBudget";
            return false;
        }
        var pose = _physics.GetPhysicsTransform(uid);
        foreach (var fixture in fixtures.Fixtures.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
        {
            if (!fixture.Hard)
                continue;
            if (shapes.Count >= 256)
            {
                issue = "RoomFixtureCountBudget";
                return false;
            }
            if (fixture.Shape.ChildCount != 1 || !KsProcgenConvexShapeOverlap.TryCheck(fixture.Shape, pose,
                    fixture.Shape, pose, out _, out _))
            {
                issue = "UnsupportedRoomFixtureShape";
                return false;
            }
            shapes.Add(new(uid, fixture.Shape, pose, fixture.CollisionLayer, fixture.CollisionMask));
        }
        return true;
    }

    private static bool Pair(Shape actor, Transform pose, Vector2 translation, Shape obstacle, Work work,
        out bool overlaps, out string? issue)
    {
        overlaps = false;
        issue = null;
        if (work.FixturePairs >= work.MaximumFixturePairs)
        {
            issue = "RoomFixturePairBudget";
            return false;
        }
        work.FixturePairs++;
        // Engine fixture filtering accepts either direction's mask/layer match.
        if ((actor.Mask & obstacle.Layer) == 0 && (obstacle.Mask & actor.Layer) == 0)
            return true;
        return KsProcgenConvexShapeOverlap.TrySweep(actor.Geometry, pose, translation,
            obstacle.Geometry, obstacle.Pose, out overlaps, out issue);
    }

    private static bool ValidBudget(Work work) => work.MaximumFixturePairs is >= 0 and <= 65_536 &&
        work.MaximumScannedEntities is >= 1 and <= 131_072 && work.MaximumExpandedCells is >= 0 and <= 65_536 &&
        work.MaximumFloorProbes is >= 0 and <= 65_536;

    private sealed class Work(int maximumFixturePairs, int maximumScannedEntities,
        int maximumExpandedCells, int maximumFloorProbes)
    {
        public int MaximumFixturePairs = maximumFixturePairs;
        public int MaximumScannedEntities = maximumScannedEntities;
        public int MaximumExpandedCells = maximumExpandedCells;
        public int MaximumFloorProbes = maximumFloorProbes;
        public int ScannedEntities;
        public int FixturePairs;
        public int ExpandedCells;
        public int FloorProbes;

        public KsProcgenRoomFixtureReport Report(string reason, bool observed = false,
            IReadOnlyList<Vector2i>? reached = null) => new(observed ? KsProcgenRoomFixtureStatus.Observed :
            reason.EndsWith("Budget", StringComparison.Ordinal) ? KsProcgenRoomFixtureStatus.BudgetExceeded :
            reason.StartsWith("Unsupported", StringComparison.Ordinal) ? KsProcgenRoomFixtureStatus.UnsupportedContent :
            reason.StartsWith("Invalid", StringComparison.Ordinal) ? KsProcgenRoomFixtureStatus.InvalidInput :
            KsProcgenRoomFixtureStatus.Rejected, reason, ScannedEntities, FixturePairs, ExpandedCells, FloorProbes,
            (reached ?? Array.Empty<Vector2i>()).ToImmutableArray());
    }
}
