using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.EntitySerialization;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._KS14.Procedural;

/// <summary>
/// Restores a captured room graph inside an existing owned allocation transaction. The caller owns
/// cleanup, terrain, physical validation and eventual map initialization. No presentation or publication.
/// </summary>
public sealed partial class KsProcgenRoomInstantiationSystem : EntitySystem
{
    [Dependency] private KsProcgenPrototypeManifestSystem _definitions = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private IDependencyCollection _dependencies = default!;

    internal sealed record PreparedMember(KsProcgenExtractedMapEntity Source, EntityPrototype? Prototype,
        Dictionary<string, MappingDataNode> Overrides, Dictionary<string, MappingDataNode> Defaults, HashSet<string> Removed);

    internal bool TryInstantiate(KsProcgenRoomEntityExtraction extraction, string roomId, EntityUid frameUid,
        Vector2i origin, int quarterTurns, List<EntityUid> allocations, int maximumAllocations,
        out IReadOnlyDictionary<int, EntityUid>? members, out KsProcgenIssue? issue,
        int maximumImageCharacters = 4_194_304, CancellationToken cancellationToken = default)
    {
        members = null; issue = null;
        if (!CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
        if (maximumImageCharacters is < 0 or > 67_108_864)
            return Reject("RoomInstantiationContext", "Room copying requires a bounded image allowance.", out issue);
        if (!TryPrepare(extraction, roomId, origin, quarterTurns, maximumAllocations - allocations.Count,
                out var plan, out issue, maximumImageCharacters: maximumImageCharacters, cancellationToken: cancellationToken)) return false;
        return TryInstantiatePrepared(plan!, frameUid, allocations, maximumAllocations, out members, out issue,
            cancellationToken: cancellationToken);
    }

    /// <summary>Consumes one private preparation exactly once, rechecking the world before allocating.</summary>
    internal bool TryInstantiatePrepared(PreparedRoom plan, EntityUid frameUid, List<EntityUid> allocations,
        int maximumAllocations, out IReadOnlyDictionary<int, EntityUid>? members, out KsProcgenIssue? issue,
        CancellationToken cancellationToken = default)
    {
        members = null; issue = null;
        if (plan == null) return Reject("RoomInstantiationPreparedInput", "A prepared room is required.", out issue);
        var extraction = plan.Extraction; var room = plan.Room; var roomId = room.Room.RoomAssetId;
        var origin = plan.Origin; var quarterTurns = plan.QuarterTurns; var prepared = plan.Members;
        if (!CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
        var mapUid = Transform(frameUid).MapUid!.Value;
        var mapId = Transform(frameUid).MapID;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (prepared.Count > maximumAllocations - allocations.Count)
                return Reject("RoomInstantiationAllocationBudget", "Prepared room identities exceed the remaining allocation allowance.", out issue);
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            if (!plan.TryConsume(this))
                return Reject("RoomInstantiationPreparedConsumed", "Prepared rooms belong to one system and can be consumed only once.", out issue);
            // One native context per room copy. No aliases from another copy or source world survive.
            var context = CreateInstanceContext();
            context.UidMap.Add(room.Room.GridYamlUid, frameUid);
            foreach (var reference in room.Entities.SelectMany(entity => entity.References).Where(reference => reference.Kind == KsProcgenMapReferenceKind.Map))
            {
                context.AllocatedMapIds[reference.TargetYamlUid] = mapId;
            }
            var result = new Dictionary<int, EntityUid>();
            var loading = new Dictionary<int, EntityPrototype?>();
            foreach (var member in prepared)
            {
                if (!CheckBoundary(out issue)) return false;
                if (allocations.Count >= maximumAllocations) return Reject("RoomInstantiationAllocationBudget", "No room allocation allowance remains.", out issue);
                var uid = _definitions.AllocateCaptured(member.Prototype, out var loadingPrototype);
                result.Add(member.Source.YamlUid, uid); loading.Add(member.Source.YamlUid, loadingPrototype);
                context.UidMap.Add(member.Source.YamlUid, uid);
            }
            foreach (var member in prepared)
            {
                if (!CheckBoundary(out issue)) return false;
                context.KsLoadCapturedEntity(result[member.Source.YamlUid], loading[member.Source.YamlUid], member.Overrides, member.Removed, member.Defaults);
            }
            foreach (var member in prepared)
            {
                if (!CheckBoundary(out issue)) return false;
                var uid = result[member.Source.YamlUid];
                // A raw deserialized transform has not populated its cached MapID yet. Passing
                // the owned map explicitly prevents the nullspace default from running MapInit.
                EntityManager.InitializeAndStartEntity(uid, mapId: mapId);
            }
            // A later member's startup can alter an earlier member. Verify the complete graph
            // only after all startup callbacks have returned, before publishing any member IDs.
            if (!CheckBoundary(out issue)) return false;
            if (!VerifyGraph(extraction, roomId, frameUid, origin, quarterTurns, result, out issue, cancellationToken: cancellationToken)) return false;
            if (!CheckBoundary(out issue)) return false;
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            members = new System.Collections.ObjectModel.ReadOnlyDictionary<int, EntityUid>(result); return true;

            bool CheckBoundary(out KsProcgenIssue? boundaryIssue)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CheckContext(frameUid, allocations, maximumAllocations, out boundaryIssue)) return false;
                if (Transform(frameUid).MapUid != mapUid ||
                    !_maps.TryGetMap(mapId, out var actualMapUid) || actualMapUid != mapUid ||
                    !_maps.IsPaused(mapId) || _maps.IsInitialized(mapId))
                    return Reject("RoomInstantiationContextDrift", "The owned room frame or transaction changed during copying.", out boundaryIssue);
                boundaryIssue = null; return true;
            }
        }
        catch (OperationCanceledException)
        { return Reject("RoomInstantiationCancelled", "Room copying was cancelled; the caller must retire its captured transaction.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Captured room copy failed: {exception}");
            return Reject("RoomInstantiationEngineFailure", "Captured component loading failed; the caller must retire its captured transaction.", out issue);
        }
    }

    private bool CheckContext(EntityUid frameUid, List<EntityUid> allocations, int maximumAllocations,
        out KsProcgenIssue? issue)
    {
        if (_terrain.CheckContext(frameUid, allocations, maximumAllocations, out issue)) return true;
        return Reject(issue?.Code == "RoomTerrainAllocationBudget" ? "RoomInstantiationAllocationBudget" : "RoomInstantiationContext",
            "Room copying requires an owned paused uninitialized unit grid and a remaining allocation allowance.", out issue);
    }

    internal bool VerifyGraph(KsProcgenRoomEntityExtraction extraction, string roomId, EntityUid frameUid,
        Vector2i origin, int quarterTurns, IReadOnlyDictionary<int, EntityUid> members, out KsProcgenIssue? issue,
        CancellationToken cancellationToken = default)
    {
        issue = null;
        var room = extraction.Rooms.SingleOrDefault(room => room.Room.RoomAssetId == roomId);
        if (room == null || members.Count != room.Entities.Count || !TryComp(frameUid, out TransformComponent? frameTransform))
            return Reject("RoomInstantiationMemberDrift", "The copied room no longer has its complete member graph.", out issue);
        foreach (var source in room.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!members.TryGetValue(source.YamlUid, out var uid) || !TryComp(uid, out TransformComponent? transform) ||
                !TryComp(uid, out MetaDataComponent? metadata))
                return Reject("RoomInstantiationMemberDrift", "Native startup removed an authored room member.", out issue);
            var root = source.ParentYamlUid == room.Room.GridYamlUid;
            if (!root && !members.ContainsKey(source.ParentYamlUid))
                return Reject("RoomInstantiationMemberDrift", "A copied room member lost its authored parent.", out issue);
            var expectedParent = root ? frameUid : members[source.ParentYamlUid];
            var position = root ? RotatePoint(source.RoomPosition, quarterTurns) + new Vector2((float) origin.X, (float) origin.Y) : source.LocalPosition;
            var rotation = source.LocalRotationRadians + (root ? KsProcgenRotation.ToEngineAngle(quarterTurns).Theta : 0.0);
            var preservePose = root || (metadata.Flags & MetaDataFlags.InContainer) == MetaDataFlags.None;
            if ((metadata.EntityPrototype?.ID ?? string.Empty) != source.PrototypeId ||
                metadata.EntityLifeStage != EntityLifeStage.Initialized || EntityManager.IsQueuedForDeletion(uid) ||
                Terminating(uid) || transform.ParentUid != expectedParent || transform.MapUid != frameTransform.MapUid ||
                transform.Anchored != source.Anchored || transform.NoLocalRotation != source.NoLocalRotation ||
                preservePose && (Vector2.Distance(transform.LocalPosition, position) > 0.00001f ||
                    Math.Abs(Angle.ShortestDistance(transform.LocalRotation, new Angle(rotation)).Theta) > 0.000001))
                return Reject("RoomInstantiationMemberDrift", "Native startup changed an authored pose, anchoring, lifecycle or room membership.", out issue);
            if (source.PrototypeId.Length != 0 && !_definitions.MatchesSnapshot(extraction.Definitions, metadata.EntityPrototype!, out issue)) return false;
        }
        return true;
    }

    internal EntityDeserializer CreateInstanceContext()
    {
        // Entity-system dependencies include systems (such as SharedMapSystem) that are not
        // registered in the root application dependency collection.
        var context = new EntityDeserializer(_dependencies, new MappingDataNode(), new DeserializationOptions { LogInvalidEntities = false })
        { KsStrictReferences = true };
        context.Result.Version = 7;
        return context;
    }

    private static Vector2 RotatePoint(Vector2 point, int quarterTurns)
    {
        var relative = point - new Vector2(0.5f, 0.5f);
        return (quarterTurns switch { 0 => relative, 1 => new(relative.Y, -relative.X),
            2 => -relative, _ => new(-relative.Y, relative.X) }) + new Vector2(0.5f, 0.5f);
    }

    private static bool ImageBudget(out KsProcgenIssue? issue) => Reject("RoomInstantiationImageBudget", "Captured per-copy images exceed their cumulative character allowance.", out issue);
    private static bool Reject(string code, string message, out KsProcgenIssue? issue)
    { issue = new(code, message); return false; }
}
