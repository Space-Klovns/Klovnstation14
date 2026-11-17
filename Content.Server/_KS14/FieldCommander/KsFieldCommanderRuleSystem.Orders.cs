using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.SupplyPod.Passengers;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Orders

    private void GiveOrders(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var battlefield = SurveyBattlefield(rule.Comp);

        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent, MobStateComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent, out var mobStateComponent))
        {
            if (unitComponent.RuleUid != rule.Owner ||
                !unitComponent.GiveOrders ||
                mobStateComponent.CurrentState != MobState.Alive)
                continue;

            OrderUnit(rule.Comp, unitUid, battlefield);
        }
    }

    /// <summary>
    ///     Points a unit at the nearest defender outside the safe zones, or at the nearest defender objective when
    ///         every defender is safe.
    /// </summary>
    private void OrderUnit(KsFieldCommanderRuleComponent component, EntityUid unitUid, Battlefield battlefield)
    {
        // Still in its pod: it gets its orders on the way out.
        if (HasComp<KsSupplyPodPassengerComponent>(unitUid))
            return;

        var unitCoordinates = _transformSystem.GetMapCoordinates(unitUid);

        if ((FindNearestOutsideSafeZones(component, unitCoordinates, battlefield.Defenders) ??
             FindNearestOutsideSafeZones(component, unitCoordinates, battlefield.Objectives)) is not { } target)
            return;

        _npcSensorSystem.AddEffect(unitUid, NpcSensorSystem.DisturbanceCoordinatesSensorKey, _transformSystem.ToCoordinates(target));
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
