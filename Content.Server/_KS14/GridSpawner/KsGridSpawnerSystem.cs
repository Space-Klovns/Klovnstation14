using System.Numerics;
using Content.Server.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._KS14.GridSpawner;

public sealed class KsGridSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly ShuttleSystem _shuttleSystem = default!;

    // Gridspawner shouldn't rely on MapInitEvent because rn its used to load saltern
    //      via a gridspawner on its planetmap

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsGridSpawnerComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<KsGridSpawnerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsGridSpawnerComponent, EntityUnpausedEvent>(OnEntityUnpaused);
    }

    // Smishing is done on map-init and not startup because
    private void OnMapInit(Entity<KsGridSpawnerComponent> entity, ref MapInitEvent args)
    {
        if (entity.Comp.SpawnedGridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return;

        _shuttleSystem.Smimsh(gridUid, grid: gridComponent);
    }

    private void OnStartup(Entity<KsGridSpawnerComponent> entity, ref ComponentStartup args)
    {
        if (Paused(entity))
            return;

        Doit(entity);
    }

    private void OnEntityUnpaused(Entity<KsGridSpawnerComponent> entity, ref EntityUnpausedEvent args)
    {
        Doit(entity);
    }

    private void Doit(Entity<KsGridSpawnerComponent, TransformComponent?> entity)
    {
        if (entity.Comp1.SpawnedGridUid is { })
            return;

        var transformComponent = entity.Comp2 ?? Transform(entity);
        var position = _transformSystem.GetWorldPosition(transformComponent);

        if (entity.Comp1.SpawnRange is { } spawnRange)
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

        if (_mapLoaderSystem.TryLoadGrid(transformComponent.MapID, entity.Comp1.Path, out var gridEntity, offset: position, rot: entity.Comp1.Rotation))
            entity.Comp1.SpawnedGridUid = gridEntity;

        RemComp(entity, entity.Comp1);
    }
}
