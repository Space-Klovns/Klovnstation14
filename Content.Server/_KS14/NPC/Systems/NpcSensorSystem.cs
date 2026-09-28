using Content.Server._KS14.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC.Systems;
using Content.Shared.Trigger;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Systems;

public sealed partial class NpcSensorSystem : SharedNpcSensorSystem
{
    [Dependency] private EntityLookupSystem _lookupSystem = default!;
    [Dependency] private HTNSystem _htnSystem = default!;
    [Dependency] private NPCSystem _npcSystem = default!;

    [Dependency] private EntityQuery<NpcSensorsComponent> _sensorsQuery = default!;
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;

    private const string DisturbanceCoordinatesSensorKey = "__Sensor__Disturbance.TargetCoordinates";

    [SubscribeLocalEvent]
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

    /// <summary>
    ///     Makes <paramref name="uid"/> replan on its next HTN update rather than when its plan cooldown runs out,
    ///         so fresh sensor data is acted on straight away. Whether the new plan replaces the running one is
    ///         still up to the planner: the root's Sensors branch only plans while sensor data is pending, and
    ///         only wins against branches below it.
    /// </summary>
    /// <remarks>
    ///     Sleeping NPCs are left asleep. They sleep for a reason (dead, critical, player-controlled), and their
    ///         pending data waits for them either way.
    /// </remarks>
    public void TryImmediatelyUpdatePlan(EntityUid uid)
    {
        if (!_htnQuery.TryComp(uid, out var htnComponent) ||
            !htnComponent.Enabled ||
            !_npcSystem.IsAwake(uid, htnComponent))
            return;

        _htnSystem.Replan(htnComponent);
    }

    public void AddEffect(Entity<NpcSensorsComponent?> entity, string key, object value)
    {
        if (!_sensorsQuery.Resolve(entity.Owner, ref entity.Comp))
            return;

        entity.Comp.AggregatedEffects[key] = value;
        TryImmediatelyUpdatePlan(entity.Owner);
    }

    public void AddEffects(Entity<NpcSensorsComponent?> entity, IEnumerable<(string, object)> effects)
    {
        if (!_sensorsQuery.Resolve(entity.Owner, ref entity.Comp))
            return;

        foreach (var (key, value) in effects)
            entity.Comp.AggregatedEffects[key] = value;

        TryImmediatelyUpdatePlan(entity.Owner);
    }

    public void AddEffects(Entity<NpcSensorsComponent?> entity, Dictionary<string, object> effects)
    {
        if (!_sensorsQuery.Resolve(entity.Owner, ref entity.Comp))
            return;

        foreach (var (key, value) in effects)
            entity.Comp.AggregatedEffects[key] = value;

        TryImmediatelyUpdatePlan(entity.Owner);
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
