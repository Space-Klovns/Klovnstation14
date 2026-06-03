using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Random;

namespace Content.Server._KS14.GridSpawner;

public sealed class KsGridSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsGridSpawnerComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<KsGridSpawnerComponent> entity, ref MapInitEvent args)
    {
        var transformComponent = Transform(entity);
        var position = _transformSystem.GetWorldPosition(transformComponent);

        if (entity.Comp.SpawnRange is { } spawnRange)
        {
            var minSq = spawnRange.X * spawnRange.X;
            var maxSq = spawnRange.Y * spawnRange.Y;

            // Uniform distribution O ALGO
            var distance = MathF.Sqrt(
                _robustRandom.NextFloat(minSq, maxSq));

            position += _robustRandom.NextAngle().RotateVec(new System.Numerics.Vector2(distance, 0f));
        }

        _mapLoaderSystem.TryLoadGrid(transformComponent.MapID, entity.Comp.Path, out _, offset: position, rot: entity.Comp.Rotation);
        RemComp(entity, entity.Comp);
    }
}
