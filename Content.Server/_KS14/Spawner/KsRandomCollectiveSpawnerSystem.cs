using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Spawner;

public sealed class KsRandomCollectiveSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsRandomCollectiveSpawnerComponent, ComponentStartup>(OnSpawnerStartup);
        SubscribeLocalEvent<KsRandomCollectiveSpawnerComponent, ComponentShutdown>(OnSpawnerShutdown);
        SubscribeLocalEvent<KsRandomCollectiveScopeComponent, MapInitEvent>(OnScopeMapInit);
    }

    private EntityUid? GetScope(Entity<KsRandomCollectiveSpawnerComponent, TransformComponent> entity)
    {
        var scopeUid = entity.Comp1.Scope switch
        {
            KsRandomCollectiveSpawnScope.Grid => entity.Comp2.GridUid,
            KsRandomCollectiveSpawnScope.Map => entity.Comp2.MapUid,
            _ => throw new InvalidOperationException($"Invalid scope for {nameof(KsRandomCollectiveSpawnerComponent)}: {entity.Comp1.Scope}")
        };

        return scopeUid;
    }

    private void OnSpawnerStartup(Entity<KsRandomCollectiveSpawnerComponent> entity, ref ComponentStartup args)
    {
        if (GetScope((entity, entity, Transform(entity))) is not { } scopeUid)
            return;

        // linq final boss without being linq
        EnsureComp<KsRandomCollectiveScopeComponent>(scopeUid).Spawners.GetOrNew(entity.Comp.ProtoId).Add(entity);
        entity.Comp.AttachedScopeUid = scopeUid;
    }

    private void OnSpawnerShutdown(Entity<KsRandomCollectiveSpawnerComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.AttachedScopeUid is not { } scopeUid ||
            TerminatingOrDeleted(scopeUid) ||
            !TryComp<KsRandomCollectiveScopeComponent>(scopeUid, out var scopeComponent) ||
            !scopeComponent.Spawners.TryGetValue(entity.Comp.ProtoId, out var cache))
            return;

        cache.Remove(entity);
    }

    private void OnScopeMapInit(Entity<KsRandomCollectiveScopeComponent> entity, ref MapInitEvent args)
    {
        if (entity.Comp.Spawners.Count == 0)
            goto fuck;

        foreach (var (spawnedEntProtoId, spawnerUids) in entity.Comp.Spawners)
        {
            if (spawnerUids.Count == 0)
                continue;

            var spawnCoordinates = Transform(_robustRandom.Pick(spawnerUids)).Coordinates;
            foreach (var otherSpawnerUid in spawnerUids)
                QueueDel(otherSpawnerUid);

            Spawn(spawnedEntProtoId, spawnCoordinates);
        }

    fuck:
        RemComp(entity, entity.Comp);
    }
}
