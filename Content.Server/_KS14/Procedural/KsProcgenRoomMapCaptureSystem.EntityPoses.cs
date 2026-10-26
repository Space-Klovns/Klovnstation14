using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    private static bool TryResolveSourcePoses(KsProcgenCapturedRoomMap map, Dictionary<int, SourceEntity> entities,
        EntityWork work, CancellationToken cancellationToken, out IReadOnlyList<SourcePose>? ordered, out KsProcgenIssue? issue)
    {
        ordered = null; issue = null;
        var grids = map.Grids.Select(grid => grid.YamlUid).ToHashSet();
        var resolved = new Dictionary<int, SourcePose>();
        foreach (var start in entities.Keys.OrderBy(uid => uid))
        {
            var path = new List<SourceEntity>(); var seen = new HashSet<int>(); var current = start;
            while (current != 0 && !resolved.ContainsKey(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!work.Spend()) return EntityBudget(out issue);
                if (!entities.TryGetValue(current, out var entity))
                    return Reject("MissingRoomEntityParent", "A transform parent is absent from the same captured source.", out issue);
                if (!seen.Add(current)) return Reject("CyclicRoomEntityParents", "Source transform parents must form an acyclic hierarchy.", out issue);
                if (path.Count >= work.Limits.MaximumParentDepth)
                    return Reject("RoomEntityExtractionDepthBudget", "A source parent chain exceeds its depth allowance.", out issue);
                path.Add(entity); current = entity.Parent;
            }
            for (var index = path.Count - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!work.Spend()) return EntityBudget(out issue);
                var entity = path[index];
                var parent = entity.Parent == 0 ? null : resolved[entity.Parent];
                var depth = (parent?.Depth ?? 0) + 1;
                if (depth > work.Limits.MaximumParentDepth)
                    return Reject("RoomEntityExtractionDepthBudget", "A resolved source parent chain exceeds its depth allowance.", out issue);
                if (grids.Contains(entity.Uid))
                {
                    if (parent?.GridUid > 0) return Reject("UnsupportedNestedRoomEntityGrid", "Room extraction does not flatten nested grids.", out issue);
                    resolved.Add(entity.Uid, new(entity, entity.Uid, Vector2.Zero, 0.0, depth)); continue;
                }
                var gridUid = parent?.GridUid ?? 0;
                // Outside every grid only hierarchy validity matters. Grid-local frames restart at
                // their own origin and do not inherit a source map's world translation/rotation.
                var position = gridUid == 0 ? Vector2.Zero : parent!.Position + new Angle(parent.Rotation).RotateVec(entity.Position);
                var rotation = gridUid == 0 ? 0.0 : parent!.Rotation + entity.Rotation;
                if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || Math.Abs((double) position.X) > 1_000_000.0 ||
                    Math.Abs((double) position.Y) > 1_000_000.0 || !double.IsFinite(rotation))
                    return Reject("RoomEntityExtractionCoordinateRange", "Composed native poses exceed supported finite source coordinates.", out issue);
                resolved.Add(entity.Uid, new(entity, gridUid, position, rotation, depth));
            }
        }
        ordered = resolved.Values.OrderBy(pose => pose.Depth).ThenBy(pose => pose.Entity.Uid).ToArray(); return true;
    }
}
