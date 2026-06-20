using System.Numerics;
using Content.Server.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Random;

namespace Content.Server._KS14.GridSpawner;

public sealed class KsGridSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly ShuttleSystem _shuttleSystem = default!;

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

            position += _robustRandom.NextAngle().RotateVec(new Vector2(distance, 0f));
            // Align grid position with tiles
            position = new(MathF.Floor(position.X), MathF.Floor(position.Y));
        }

        if (_mapLoaderSystem.TryLoadGrid(transformComponent.MapID, entity.Comp.Path, out var gridEntity, offset: position, rot: entity.Comp.Rotation))
            _shuttleSystem.Smimsh(gridEntity.Value.Owner, grid: gridEntity.Value.Comp);

        RemComp(entity, entity.Comp);
    }
}
