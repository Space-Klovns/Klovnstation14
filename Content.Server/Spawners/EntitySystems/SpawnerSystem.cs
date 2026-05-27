using Content.Server.Spawners.Components;
using Robust.Shared.Map; // KS14: spawn radius
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Spawners.EntitySystems;

public sealed class SpawnerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TimedSpawnerComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<TimedSpawnerComponent>();
        while (query.MoveNext(out var uid, out var timedSpawner))
        {
            if (timedSpawner.NextFire > curTime)
                continue;

            OnTimerFired(uid, timedSpawner);

            timedSpawner.NextFire += timedSpawner.IntervalSeconds;
        }
    }

    private void OnMapInit(Entity<TimedSpawnerComponent> ent, ref MapInitEvent args)
    {
        // KS14 Start
        if (ent.Comp.SpawnImmediately)
        {
            ent.Comp.NextFire = _timing.CurTime;
            return;
        }
        // KS14 End

        ent.Comp.NextFire = _timing.CurTime + ent.Comp.IntervalSeconds;
    }

    private void OnTimerFired(EntityUid uid, TimedSpawnerComponent component)
    {
        if (!_random.Prob(component.Chance))
            return;

        var number = _random.Next(component.MinimumEntitiesSpawned, component.MaximumEntitiesSpawned);
        var baseCoordinates = Transform(uid).Coordinates; // KS14: spawn radius: coordinates -> baseCoordinates

        for (var i = 0; i < number; i++)
        {
            var entity = _random.Pick(component.Prototypes);

            // KS14 Start: spawn radius
            EntityCoordinates coordinates;
            if (component.MaxRadius > 0)
            {
                var minSq = component.MinRadius * component.MinRadius;
                var maxSq = component.MaxRadius * component.MaxRadius;

                var distance = MathF.Sqrt(
                    _random.NextFloat(minSq, maxSq));

                coordinates = baseCoordinates.WithPosition(baseCoordinates.Position + _random.NextAngle().RotateVec(new System.Numerics.Vector2(distance, 0f)));
            }
            else
                coordinates = baseCoordinates;
            // KS14 End: spawn radius

            SpawnAtPosition(entity, coordinates);
        }
    }
}
