using System.Linq;
using Robust.Shared.Map;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenStageKind : byte { Tiles, Surface, Container, Assembly, Owned }

public enum KsProcgenStageInitializationPhase : byte { Uninitialized, Initializing, Initialized }
public enum KsProcgenStageLifecyclePhase : byte { Uninitialized, Initializing, Initialized, Retired }

/// <summary>Common stage data, retained authoritatively by its map's ownership component.</summary>
public abstract class KsProcgenStageHandle
{
    private protected KsProcgenStageHandle(MapId mapId, EntityUid mapUid, KsProcgenStageKind kind,
        List<EntityUid> spawnedEntities, int maxSpawnedEntities)
    {
        MapId = mapId; MapUid = mapUid; Kind = kind; SpawnedEntities = spawnedEntities; MaxSpawnedEntities = maxSpawnedEntities;
    }
    public MapId MapId { get; }
    public EntityUid MapUid { get; }
    public KsProcgenStageKind Kind { get; }
    public bool Active { get; internal set; } = true;
    public KsProcgenStageInitializationPhase InitializationPhase { get; internal set; }
    public KsProcgenStageLifecyclePhase LifecyclePhase => !Active ? KsProcgenStageLifecyclePhase.Retired :
        InitializationPhase switch
        {
            KsProcgenStageInitializationPhase.Initializing => KsProcgenStageLifecyclePhase.Initializing,
            KsProcgenStageInitializationPhase.Initialized => KsProcgenStageLifecyclePhase.Initialized,
            _ => KsProcgenStageLifecyclePhase.Uninitialized,
        };
    /// <summary>Cumulative recorded allocations, including subsequently deleted initializer entities.</summary>
    public int SpawnedEntityCount => SpawnedEntities.Count;
    public int MaxSpawnedEntities { get; }
    internal bool WasRetained;
    internal List<EntityUid> SpawnedEntities { get; }
}

/// <summary>Server-only volatile ownership. No stage object or entity references are serialized.</summary>
[RegisterComponent]
public sealed partial class KsProcgenStageSessionComponent : Component
{
    public KsProcgenStageHandle? Stage { get; internal set; }
}

/// <summary>
/// One authoritative map/component registry and cleanup path for all preview kinds. This does not
/// establish network/atmos/power activation isolation, publication permission or operational proof.
/// </summary>
public sealed partial class KsProcgenStageOwnershipSystem : EntitySystem
{
    public const int MaximumRetainedPerKind = 16;
    public const int MaximumRetainedStages = 64;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operations = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KsProcgenStageSessionComponent, ComponentShutdown>(OnSessionShutdown);
        SubscribeLocalEvent<KsProcgenStageSessionComponent, EntityTerminatingEvent>(OnSessionTerminating);
    }

    public int RetainedStageCount => Snapshot().Count;
    public int Count(KsProcgenStageKind kind) => Snapshot(kind).Count;

    internal bool CanRetain(KsProcgenStageKind kind) =>
        Count(kind) < MaximumRetainedPerKind && RetainedStageCount < MaximumRetainedStages;

    /// <summary>Complete map transform tree, including contained and paused members; bounded before enqueue.</summary>
    internal string? InspectMapMembers(KsProcgenStageHandle stage, int maximumEntities, string prefix,
        out List<EntityUid> members, out int scannedEntities)
    {
        members = new();
        scannedEntities = 0;
        var owned = stage.SpawnedEntities.ToHashSet();
        var pending = new Stack<(EntityUid Uid, EntityUid Parent)>();
        var visited = new HashSet<EntityUid>();
        pending.Push((stage.MapUid, EntityUid.Invalid));
        while (pending.TryPop(out var entry))
        {
            if (scannedEntities >= maximumEntities) return prefix + "EntityScanBudget";
            scannedEntities++;
            if (!visited.Add(entry.Uid) || !TryComp(entry.Uid, out TransformComponent? transform) ||
                entry.Uid != stage.MapUid && (transform.MapUid != stage.MapUid || transform.ParentUid != entry.Parent))
                return prefix + "SceneMembershipDrift";
            if (!owned.Contains(entry.Uid)) return prefix + "SceneForeignEntity";
            members.Add(entry.Uid);
            using var children = transform.ChildEnumerator;
            while (children.MoveNext(out var childUid))
            {
                if (pending.Count >= maximumEntities - scannedEntities) return prefix + "EntityScanBudget";
                pending.Push((childUid, entry.Uid));
            }
        }
        return null;
    }

    public IReadOnlyList<KsProcgenStageHandle> Snapshot(KsProcgenStageKind? kind = null)
    {
        var stages = new List<KsProcgenStageHandle>();
        // Ordinary queries omit paused entities. Every preview here is deliberately paused.
        var query = AllEntityQuery<KsProcgenStageSessionComponent>();
        while (query.MoveNext(out _, out var session))
            if (session.Stage is { Active: true } stage && (kind == null || stage.Kind == kind)) stages.Add(stage);
        return stages.ToArray();
    }

    internal bool Owns(KsProcgenStageHandle? stage) => stage is { Active: true } &&
        TryComp<KsProcgenStageSessionComponent>(stage.MapUid, out var session) && ReferenceEquals(session.Stage, stage) &&
        _maps.TryGetMap(stage.MapId, out var actualMap) && actualMap == stage.MapUid && !Terminating(stage.MapUid);

    internal void Attach(KsProcgenStageHandle stage)
    {
        if (!stage.Active || stage.WasRetained || Count(stage.Kind) >= MaximumRetainedPerKind || RetainedStageCount >= MaximumRetainedStages ||
            !_maps.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid || Terminating(stage.MapUid) || EntityManager.IsQueuedForDeletion(stage.MapUid))
            throw new InvalidOperationException("The preview cannot acquire map ownership.");
        var session = EnsureComp<KsProcgenStageSessionComponent>(stage.MapUid);
        if (session.Stage != null || !TryComp<KsProcgenStageSessionComponent>(stage.MapUid, out var attached) || !ReferenceEquals(session, attached) ||
            Terminating(stage.MapUid) || EntityManager.IsQueuedForDeletion(stage.MapUid))
            throw new InvalidOperationException("The preview map is already owned or invalidated.");
        session.Stage = stage; stage.WasRetained = true;
    }

    /// <summary>Retired handles can finish cleaning late initialization allocations, never a reused map ID.</summary>
    internal bool Release(KsProcgenStageHandle? stage)
    {
        if (stage == null || !stage.WasRetained) return false;
        var owned = Owns(stage);
        if (!owned && stage.Active) return false;
        stage.Active = false;
        if (TryComp<KsProcgenStageSessionComponent>(stage.MapUid, out var session) && ReferenceEquals(session.Stage, stage)) session.Stage = null;
        Cleanup(stage.MapId, stage.MapUid, stage.SpawnedEntities);
        return owned;
    }

    internal void Cleanup(MapId mapId, EntityUid mapUid, IEnumerable<EntityUid> entities, bool deleteMap = true)
    {
        foreach (var uid in entities.Reverse().ToArray())
            if (uid != mapUid && Exists(uid) && !Terminating(uid)) Del(uid);
        if (deleteMap && _maps.TryGetMap(mapId, out var actualMap) && actualMap == mapUid && !Terminating(mapUid)) _maps.DeleteMap(mapId);
    }

    private void OnSessionShutdown(Entity<KsProcgenStageSessionComponent> entity, ref ComponentShutdown args)
        => Retire(entity);

    private void OnSessionTerminating(Entity<KsProcgenStageSessionComponent> entity, ref EntityTerminatingEvent args)
        => Retire(entity);

    private void Retire(Entity<KsProcgenStageSessionComponent> entity)
    {
        if (entity.Comp.Stage is not { } stage) return;
        entity.Comp.Stage = null;
        if (stage.MapUid != entity.Owner) return;
        stage.Active = false;
        // An external deletion may occur outside any preview operation. Prevent cleanup callbacks
        // from nesting another preview, while permitting cleanup inside an already active operation.
        var entered = _operations.TryEnter(this);
        try
        {
            Cleanup(stage.MapId, stage.MapUid, stage.SpawnedEntities, deleteMap: false);
            if (Exists(stage.MapUid) && !Terminating(stage.MapUid)) QueueDel(stage.MapUid);
        }
        finally { if (entered) _operations.Exit(this); }
    }
}
