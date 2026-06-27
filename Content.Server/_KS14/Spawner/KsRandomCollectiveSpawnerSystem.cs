
using Content.Shared.GameTicking;

namespace Content.Server._KS14.Spawner;

public sealed class KsRandomCollectiveSpawnerSystem : EntitySystem
{
    private HashSet<EntityUid> _globalCache = [];

    public override void Initialize()
    {
        base.Initialize();
    }

    private HashSet<EntityUid> GetOrAddCache(TransformComponent transformComponent, KsRandomCollectiveSpawnerComponent spawnerComponent)
    {
        if ()
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _globalCache.Clear();

    }

    private void OnStartup(Entity<KsRandomCollectiveSpawnerComponent> entity, ref ComponentStartup args)
    {

    }

    private void OnShutdown(Entity<KsRandomCollectiveSpawnerComponent> entity, ref ComponentShutdown args)
    {

    }

    private void OnMapInit()
    {

    }
}
