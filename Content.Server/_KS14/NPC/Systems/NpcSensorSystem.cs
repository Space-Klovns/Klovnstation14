using Content.Server._KS14.NPC.Components;
using Content.Shared._KS14.NPC.Systems;
using Content.Shared.Trigger;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Systems;

public sealed class NpcSensorSystem : SharedNpcSensorSystem
{
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;

    [Dependency] private readonly EntityQuery<NpcSensorsComponent> _sensorsQuery = default!;

    private const string DisturbanceCoordinatesSensorKey = "__Sensor__Disturbance.TargetCoordinates";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcDisturbOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<NpcDisturbOnTriggerComponent> entity, ref TriggerEvent args)
    {
        EntityCoordinates coordinates;
        if (entity.Comp.TargetUser)
        {
            if (args.User is not { } userUid)
                return;

            coordinates = Transform(userUid).Coordinates;
        }
        else
            coordinates = Transform(entity.Owner).Coordinates;

        DoDisturbance(coordinates, entity.Comp.Radius);
    }

    public void AddEffect(Entity<NpcSensorsComponent?> entity, string key, object value)
    {
        if (!_sensorsQuery.Resolve(entity.Owner, ref entity.Comp))
            return;

        entity.Comp.AggregatedEffects[key] = value;
    }

    public void AddEffects(Entity<NpcSensorsComponent?> entity, IEnumerable<(string, object)> effects)
    {
        if (!_sensorsQuery.Resolve(entity.Owner, ref entity.Comp))
            return;

        foreach (var (key, value) in effects)
            entity.Comp.AggregatedEffects[key] = value;
    }

    public void AddEffects(Entity<NpcSensorsComponent?> entity, Dictionary<string, object> effects)
    {
        if (!_sensorsQuery.Resolve(entity.Owner, ref entity.Comp))
            return;

        foreach (var (key, value) in effects)
            entity.Comp.AggregatedEffects[key] = value;
    }

    public override void DoDisturbance(EntityCoordinates coordinates, float radius, EntityUid? source = null)
    {
        var entities = _lookupSystem.GetEntitiesInRange<NpcSensorsComponent>(coordinates, radius, flags: LookupFlags.Approximate | LookupFlags.Sundries | LookupFlags.Dynamic | LookupFlags.Uncontained);
        foreach (var entity in entities)
        {
            if (entity.Owner == source)
                continue;

            AddEffect(entity!, DisturbanceCoordinatesSensorKey, coordinates);
        }
    }
}
