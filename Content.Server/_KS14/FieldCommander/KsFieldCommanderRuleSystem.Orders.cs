using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.SupplyPod.Passengers;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Orders

    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    private void GiveOrders(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var battlefield = SurveyBattlefield(rule.Comp);

        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            if (unitComponent.RuleUid != rule.Owner ||
                !unitComponent.GiveOrders ||
                !IsUnitAlive((unitUid, unitComponent)))
                continue;

            // Sent somewhere else for being stuck (see .Stuck): it keeps that order until it gets somewhere, rather
            //      than being sent straight back to where it could not get.
            if (unitComponent.StuckStage != KsFieldCommanderStuckStage.None && unitComponent.OrderCoordinates != null)
                continue;

            OrderUnit(rule.Comp, (unitUid, unitComponent), battlefield);
        }

        CheckStuckUnits(rule, battlefield);
    }

    /// <summary>
    ///     Whether a unit still counts: one with no mob state (a borg, a drone) until it is destroyed, any other until
    ///         it dies.
    /// </summary>
    private bool IsUnitAlive(Entity<KsFieldCommanderUnitComponent> unit)
    {
        if (unit.Comp.Killed)
            return false;

        return !_mobStateQuery.TryComp(unit, out var mobStateComponent) ||
               mobStateComponent.CurrentState != MobState.Dead;
    }

    /// <summary>
    ///     Points a unit at the nearest defender outside the safe zones, or at the nearest defender objective when
    ///         every defender is safe.
    /// </summary>
    private void OrderUnit(KsFieldCommanderRuleComponent component, Entity<KsFieldCommanderUnitComponent> unit, Battlefield battlefield)
    {
        // Still in its pod: it gets its orders on the way out.
        if (HasComp<KsSupplyPodPassengerComponent>(unit))
            return;

        var unitCoordinates = _transformSystem.GetMapCoordinates(unit);

        if ((FindNearestOutsideSafeZones(component, unitCoordinates, battlefield.Defenders) ??
             FindNearestOutsideSafeZones(component, unitCoordinates, battlefield.Objectives)) is not { } target)
            return;

        var standOff = !unit.Comp.IgnoreStandOff && unit.Comp.AssetIndex < component.Assets.Count ? component.Assets[unit.Comp.AssetIndex].StandOff : 0f;
        OrderUnitTo(unit, GetStandOffCoordinates(unitCoordinates, target, standOff));
    }

    /// <summary>
    ///     Where a unit at <paramref name="from"/> that keeps <paramref name="standOff"/> tiles from
    ///         <paramref name="target"/> is sent: that far from the target on the line back to the unit, so it closes in
    ///         from farther and backs off from nearer. Failing free floor there, the nearest free floor on the way in.
    /// </summary>
    public MapCoordinates GetStandOffCoordinates(MapCoordinates from, MapCoordinates target, float standOff)
    {
        var offset = from.Position - target.Position;
        var length = offset.Length();
        if (!(standOff > 0f) || length < 0.01f || from.MapId != target.MapId)
            return target;

        var direction = offset / length;
        for (var distance = standOff; distance > 0f; distance -= 1f)
        {
            var candidate = new MapCoordinates(target.Position + direction * distance, target.MapId);
            if (TryGetFreeTile(candidate, out _, out var tileMapCoordinates))
                return tileMapCoordinates;
        }

        return target;
    }

    private void OrderUnitTo(Entity<KsFieldCommanderUnitComponent> unit, MapCoordinates target)
    {
        var targetCoordinates = _transformSystem.ToCoordinates(target);
        unit.Comp.OrderCoordinates = targetCoordinates;

        // A unit with nothing to hear orders with (a drone) still has somewhere it is meant to be, for .Stuck.
        if (HasComp<Content.Server._KS14.NPC.Components.NpcSensorsComponent>(unit))
            _npcSensorSystem.AddEffect(unit.Owner, NpcSensorSystem.DisturbanceCoordinatesSensorKey, targetCoordinates);
    }

    private static MapCoordinates? FindNearestOutsideSafeZones(KsFieldCommanderRuleComponent component, MapCoordinates from, List<MapCoordinates> candidates)
    {
        MapCoordinates? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate.MapId != from.MapId)
                continue;

            var distance = (candidate.Position - from.Position).LengthSquared();
            if (distance >= nearestDistance || IsInSafeZone(component, candidate))
                continue;

            nearestDistance = distance;
            nearest = candidate;
        }

        return nearest;
    }

    #endregion
}
