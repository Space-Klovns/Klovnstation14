using System.Numerics;
using Content.Server.Spawners.Components;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Budget

    /// <summary>
    ///     A wave's threat budget against <paramref name="defenders"/> living defenders, before anything carried over.
    /// </summary>
    public static float GetWaveBudget(KsFieldCommanderPhase phase, int defenders, float adaptiveMultiplier)
    {
        if (defenders <= 0)
            return 0f;

        return phase.Budget * (1f + phase.BudgetPerExtraDefender * (defenders - 1)) * adaptiveMultiplier;
    }

    /// <summary>
    ///     How many of the commander's units may be alive at once against <paramref name="defenders"/> living defenders.
    /// </summary>
    public static int GetMaxAlive(KsFieldCommanderRuleComponent component, int defenders)
    {
        var maxAlive = component.MaxAlive + (int)MathF.Floor(component.MaxAlivePerDefender * defenders);
        return Math.Min(maxAlive, component.HardMaxAlive);
    }

    /// <summary>
    ///     Eases off after the defenders took a beating, and presses harder after a wave that cost them nothing.
    /// </summary>
    public static float GetNextAdaptiveMultiplier(KsFieldCommanderRuleComponent component, int defenders, int defendersLost)
    {
        var multiplier = component.AdaptiveMultiplier;

        if (defendersLost == 0)
            multiplier *= component.AdaptiveStepUp;
        else if (defenders <= 0 || (float)defendersLost / (float)(defenders + defendersLost) >= component.HeavyLossFraction)
            multiplier *= component.AdaptiveStepDown;

        return Math.Clamp(multiplier, component.MinAdaptiveMultiplier, component.MaxAdaptiveMultiplier);
    }

    /// <summary>
    ///     Buys assets with <paramref name="budget"/>, at random by weight, until nothing more is affordable or another
    ///         purchase would put more than <paramref name="capacity"/> units in the field. Returns the indices bought
    ///         into <paramref name="purchases"/> and what was left of the budget.
    /// </summary>
    public float BuyAssets(KsFieldCommanderRuleComponent component,
        float budget,
        int capacity,
        Dictionary<int, int> aliveByAsset,
        List<int> purchases)
    {
        var candidates = new List<int>();

        while (true)
        {
            candidates.Clear();
            var totalWeight = 0f;

            for (var i = 0; i < component.Assets.Count; i++)
            {
                var asset = component.Assets[i];

                if (asset.MinPhase > component.PhaseIndex ||
                    asset.Cost > budget ||
                    asset.Count > capacity ||
                    asset.Weight <= 0f)
                    continue;

                if (asset.MaxAlive > 0 &&
                    aliveByAsset.GetValueOrDefault(i) + asset.Count > asset.MaxAlive)
                    continue;

                candidates.Add(i);
                totalWeight += asset.Weight;
            }

            if (candidates.Count == 0)
                return budget;

            var roll = _robustRandom.NextFloat() * totalWeight;
            var picked = candidates[^1];
            foreach (var candidate in candidates)
            {
                roll -= component.Assets[candidate].Weight;
                if (roll > 0f)
                    continue;

                picked = candidate;
                break;
            }

            var pickedAsset = component.Assets[picked];
            budget -= MathF.Max(pickedAsset.Cost, 0.01f);
            capacity -= pickedAsset.Count;
            aliveByAsset[picked] = aliveByAsset.GetValueOrDefault(picked) + pickedAsset.Count;
            purchases.Add(picked);
        }
    }

    #endregion

    #region Counting

    /// <summary>
    ///     Positions of every living defender, and how many defenders are dead.
    /// </summary>
    private void CollectLivingDefenders(KsFieldCommanderRuleComponent component, List<MapCoordinates> livingDefenders, out int deadDefenders)
    {
        deadDefenders = 0;

        var query = EntityQueryEnumerator<ScenarioFactionMemberComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var memberUid, out var memberComponent, out var mobStateComponent, out var transformComponent))
        {
            if (memberComponent.Id != component.DefenderFaction)
                continue;

            if (mobStateComponent.CurrentState == MobState.Dead)
            {
                deadDefenders++;
                continue;
            }

            livingDefenders.Add(_transformSystem.GetMapCoordinates(memberUid, transformComponent));
        }
    }

    /// <summary>
    ///     The commander's living units, in total and by what they were bought as.
    /// </summary>
    private int CountLivingUnits(EntityUid ruleUid, Dictionary<int, int>? aliveByAsset)
    {
        var count = 0;
        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent, MobStateComponent>();
        while (query.MoveNext(out var unitComponent, out var mobStateComponent))
        {
            if (unitComponent.RuleUid != ruleUid ||
                mobStateComponent.CurrentState == MobState.Dead)
                continue;

            count++;

            if (aliveByAsset != null && unitComponent.AssetIndex >= 0)
                aliveByAsset[unitComponent.AssetIndex] = aliveByAsset.GetValueOrDefault(unitComponent.AssetIndex) + 1;
        }

        return count;
    }

    private void CollectDefenderObjectives(KsFieldCommanderRuleComponent component, List<MapCoordinates> objectives)
    {
        var query = EntityQueryEnumerator<ScenarioObjectiveComponent, TransformComponent>();
        while (query.MoveNext(out var objectiveUid, out var objectiveComponent, out var transformComponent))
        {
            if (objectiveComponent.FactionId != component.DefenderFaction ||
                objectiveComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            objectives.Add(_transformSystem.GetMapCoordinates(objectiveUid, transformComponent));
        }
    }

    private int CountDefenderObjectives(KsFieldCommanderRuleComponent component, EntityUid? exceptUid)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ScenarioObjectiveComponent>();
        while (query.MoveNext(out var objectiveUid, out var objectiveComponent))
        {
            if (objectiveUid != exceptUid &&
                objectiveComponent.FactionId == component.DefenderFaction &&
                objectiveComponent.LifeStage <= ComponentLifeStage.Running)
                count++;
        }

        return count;
    }

    #endregion

    #region Safe zones

    /// <summary>
    ///     Safe zone markers on the map if there are any, or else every spawn point near the defenders' objectives.
    /// </summary>
    private void FindSafeZones(KsFieldCommanderRuleComponent component)
    {
        component.SafeZones.Clear();

        var markerQuery = EntityQueryEnumerator<KsFieldCommanderSafeZoneComponent, TransformComponent>();
        while (markerQuery.MoveNext(out var markerUid, out var markerComponent, out var transformComponent))
        {
            component.SafeZones.Add(new KsFieldCommanderSafeZone(
                _transformSystem.GetMapCoordinates(markerUid, transformComponent),
                markerComponent.Radius ?? component.SafeZoneRadius));
        }

        if (component.SafeZones.Count > 0)
            return;

        var objectives = new List<MapCoordinates>();
        CollectDefenderObjectives(component, objectives);

        var spawnQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out var spawnUid, out _, out var transformComponent))
        {
            var spawnCoordinates = _transformSystem.GetMapCoordinates(spawnUid, transformComponent);

            // Spawn points somewhere else entirely (another station, an admin room) are none of the commander's business.
            if (!IsNearAny(spawnCoordinates, objectives, component.MaxSpawnDistance))
                continue;

            component.SafeZones.Add(new KsFieldCommanderSafeZone(spawnCoordinates, component.SafeZoneRadius));
        }
    }

    /// <summary>
    ///     Whether <paramref name="coordinates"/> is inside a safe zone, or within <paramref name="margin"/> of one.
    /// </summary>
    public static bool IsInSafeZone(KsFieldCommanderRuleComponent component, MapCoordinates coordinates, float margin = 0f)
    {
        foreach (var zone in component.SafeZones)
        {
            if (zone.Center.MapId != coordinates.MapId)
                continue;

            var radius = zone.Radius + margin;
            if ((zone.Center.Position - coordinates.Position).LengthSquared() < radius * radius)
                return true;
        }

        return false;
    }

    private static bool IsNearAny(MapCoordinates coordinates, List<MapCoordinates> others, float range)
    {
        foreach (var other in others)
        {
            if (other.MapId == coordinates.MapId &&
                (other.Position - coordinates.Position).LengthSquared() < range * range)
                return true;
        }

        return false;
    }

    #endregion

    #region Placement

    /// <summary>
    ///     A free tile in a ring around the defenders' objectives, far enough from any defender and out of every
    ///         defender's sight when <paramref name="hidden"/>, and never in a safe zone.
    /// </summary>
    private bool TryFindPlacement(KsFieldCommanderRuleComponent component,
        List<MapCoordinates> anchors,
        List<MapCoordinates> defenders,
        float minDistance,
        float maxDistance,
        float minObjectiveDistance,
        float minDefenderDistance,
        bool hidden,
        out EntityCoordinates coordinates)
    {
        coordinates = default;

        if (anchors.Count == 0)
            return false;

        for (var attempt = 0; attempt < component.PlacementAttempts; attempt++)
        {
            var anchor = _robustRandom.Pick(anchors);
            var offset = _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(minDistance, maxDistance);
            var candidate = new MapCoordinates(anchor.Position + offset, anchor.MapId);

            if (IsInSafeZone(component, candidate) ||
                IsNearAny(candidate, anchors, minObjectiveDistance) ||
                IsNearAny(candidate, defenders, minDefenderDistance))
                continue;

            if (!TryGetFreeTile(candidate, out var tileCoordinates, out var tileMapCoordinates))
                continue;

            if (hidden && IsSeenByDefenders(component, tileMapCoordinates, defenders))
                continue;

            coordinates = tileCoordinates;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The centre of the tile under <paramref name="candidate"/>, if it is solid ground that nothing stands on.
    /// </summary>
    private bool TryGetFreeTile(MapCoordinates candidate, out EntityCoordinates tileCoordinates, out MapCoordinates tileMapCoordinates)
    {
        tileCoordinates = default;
        tileMapCoordinates = default;

        if (!_mapSystem.TryFindGridAt(candidate, out var gridUid, out var gridComponent))
            return false;

        var indices = _mapSystem.TileIndicesFor(gridUid, gridComponent, candidate);
        var tileRef = _mapSystem.GetTileRef(gridUid, gridComponent, indices);

        if (tileRef.Tile.IsEmpty ||
            _turfSystem.IsSpace(tileRef) ||
            _turfSystem.IsTileBlocked(tileRef, CollisionGroup.MobMask))
            return false;

        tileCoordinates = _mapSystem.GridTileToLocal(gridUid, gridComponent, indices);
        tileMapCoordinates = _transformSystem.ToMapCoordinates(tileCoordinates);
        return true;
    }

    private bool IsSeenByDefenders(KsFieldCommanderRuleComponent component, MapCoordinates coordinates, List<MapCoordinates> defenders)
    {
        foreach (var defender in defenders)
        {
            if (defender.MapId != coordinates.MapId ||
                (defender.Position - coordinates.Position).LengthSquared() > component.DefenderSightRange * component.DefenderSightRange)
                continue;

            if (_npcLineOfSightSystem.InLineOfSight(defender, coordinates, component.DefenderSightRange))
                return true;
        }

        return false;
    }

    #endregion

    #region Waves

    private void SendWave(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderPhase phase)
    {
        var defenders = new List<MapCoordinates>();
        CollectLivingDefenders(rule.Comp, defenders, out var deadDefenders);

        if (defenders.Count == 0)
            return;

        var defendersLost = Math.Max(0, deadDefenders - rule.Comp.DeadDefendersAtLastWave);
        if (rule.Comp.WavesSent > 0)
            rule.Comp.AdaptiveMultiplier = GetNextAdaptiveMultiplier(rule.Comp, defenders.Count, defendersLost);

        rule.Comp.DeadDefendersAtLastWave = deadDefenders;

        var aliveByAsset = new Dictionary<int, int>();
        var alive = CountLivingUnits(rule.Owner, aliveByAsset);
        var capacity = GetMaxAlive(rule.Comp, defenders.Count) - alive;

        var budget = rule.Comp.CarriedBudget + GetWaveBudget(phase, defenders.Count, rule.Comp.AdaptiveMultiplier);
        var purchases = new List<int>();
        var leftover = BuyAssets(rule.Comp, budget, capacity, aliveByAsset, purchases);

        // Saving up lets a later wave afford something big, but never more than one wave's worth.
        rule.Comp.CarriedBudget = MathF.Min(leftover, phase.Budget);

        var strike = phase.StrikeChance > 0f && _robustRandom.Prob(Math.Clamp(phase.StrikeChance, 0f, 1f));

        if (purchases.Count == 0 && !strike)
            return;

        rule.Comp.WavesSent++;

        var objectives = new List<MapCoordinates>();
        CollectDefenderObjectives(rule.Comp, objectives);

        var sent = Deliver(rule, purchases, objectives, defenders);

        if (sent > 0)
        {
            AnnounceRandom(rule.Comp, rule.Comp.WaveAnnouncements, major: false, ("count", sent));

            var announced = new HashSet<LocId>();
            foreach (var assetIndex in purchases)
            {
                if (rule.Comp.Assets[assetIndex].Announcement is { } assetAnnouncement && announced.Add(assetAnnouncement))
                    Announce(rule.Comp, assetAnnouncement, major: false);
            }
        }

        if (strike)
            TryStrike(rule.Comp, objectives, defenders);
    }

    /// <summary>
    ///     Sends the purchased assets in, in groups: on foot from out of sight, or by pod. Returns how many units went.
    /// </summary>
    private int Deliver(Entity<KsFieldCommanderRuleComponent> rule,
        List<int> purchases,
        List<MapCoordinates> objectives,
        List<MapCoordinates> defenders)
    {
        var anchors = objectives.Count > 0 ? objectives : defenders;
        var footGroup = new List<int>();
        var podGroup = new List<int>();
        var sent = 0;

        foreach (var assetIndex in purchases)
        {
            var asset = rule.Comp.Assets[assetIndex];
            var group = asset.Delivery == KsFieldCommanderDelivery.Pod ? podGroup : footGroup;
            var limit = asset.Delivery == KsFieldCommanderDelivery.Pod ? rule.Comp.MaxPerPod : rule.Comp.MaxPerGroup;

            if (group.Count > 0 && CountGroup(rule.Comp, group) + asset.Count > limit)
            {
                sent += DeliverGroup(rule, group, asset.Delivery, anchors, defenders);
                group.Clear();
            }

            group.Add(assetIndex);
        }

        sent += DeliverGroup(rule, footGroup, KsFieldCommanderDelivery.Foot, anchors, defenders);
        sent += DeliverGroup(rule, podGroup, KsFieldCommanderDelivery.Pod, anchors, defenders);
        return sent;
    }

    private static int CountGroup(KsFieldCommanderRuleComponent component, List<int> group)
    {
        var count = 0;
        foreach (var assetIndex in group)
            count += component.Assets[assetIndex].Count;

        return count;
    }

    private int DeliverGroup(Entity<KsFieldCommanderRuleComponent> rule,
        List<int> group,
        KsFieldCommanderDelivery delivery,
        List<MapCoordinates> anchors,
        List<MapCoordinates> defenders)
    {
        if (group.Count == 0)
            return 0;

        var component = rule.Comp;

        if (delivery == KsFieldCommanderDelivery.Foot &&
            TryFindPlacement(component, anchors, defenders,
                component.MinSpawnDistance, component.MaxSpawnDistance,
                minObjectiveDistance: component.MinSpawnDistance,
                minDefenderDistance: component.MinDefenderDistance,
                hidden: true,
                out var footCoordinates))
        {
            return SpawnGroup(rule, group, footCoordinates, podUid: null);
        }

        // Nowhere out of sight to walk in from, or meant to come by air anyway.
        if (!TryFindPlacement(component, anchors, defenders,
                component.MinPodObjectiveDistance, component.MaxSpawnDistance,
                minObjectiveDistance: component.MinPodObjectiveDistance,
                minDefenderDistance: component.MinPodDefenderDistance,
                hidden: false,
                out var podCoordinates))
            return 0;

        var podUid = SpawnAtPosition(component.PodPrototype, podCoordinates);
        return SpawnGroup(rule, group, podCoordinates, podUid);
    }

    private int SpawnGroup(Entity<KsFieldCommanderRuleComponent> rule, List<int> group, EntityCoordinates coordinates, EntityUid? podUid)
    {
        var spawned = 0;

        foreach (var assetIndex in group)
        {
            var asset = rule.Comp.Assets[assetIndex];
            for (var i = 0; i < asset.Count; i++)
            {
                if (SpawnUnit(rule, asset, assetIndex, coordinates) is not { } unitUid)
                    continue;

                spawned++;

                if (podUid is { } pod && !_supplyPodPassengerSystem.TryBoard(unitUid, pod))
                    Log.Warning($"{ToPrettyString(unitUid)} could not board {ToPrettyString(pod)}, and arrives beside it.");
            }
        }

        rule.Comp.UnitsSent += spawned;
        return spawned;
    }

    /// <summary>
    ///     Spawns one unit of <paramref name="asset"/> on the commander's side.
    /// </summary>
    private EntityUid? SpawnUnit(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderAsset asset, int assetIndex, EntityCoordinates coordinates)
    {
        EntityUid unitUid;

        if (asset.Humanoid is { } humanoid)
            unitUid = _randomHumanoidSystem.SpawnRandomHumanoid(humanoid, coordinates, Loc.GetString(asset.HumanoidName));
        else if (asset.Entity is { } entity)
            unitUid = SpawnAtPosition(entity, coordinates);
        else
            return null;

        // Joins the side so that what it captures is the commander's, and counted for the round.
        EnsureComp<ScenarioFactionMemberComponent>(unitUid).Id = rule.Comp.Faction;

        var unitComponent = EnsureComp<KsFieldCommanderUnitComponent>(unitUid);
        unitComponent.RuleUid = rule.Owner;
        unitComponent.AssetIndex = assetIndex;
        unitComponent.GiveOrders = asset.GiveOrders;

        return unitUid;
    }

    #endregion

    #region Reinforcements

    /// <summary>
    ///     Drops a pod of supplies beside one of the defenders' safe zones.
    /// </summary>
    private void DropReinforcements(KsFieldCommanderRuleComponent component, EntProtoId podPrototype)
    {
        if (component.SafeZones.Count == 0)
            return;

        var zone = _robustRandom.Pick(component.SafeZones);

        for (var attempt = 0; attempt < component.PlacementAttempts; attempt++)
        {
            var offset = _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(1f, MathF.Max(1.5f, zone.Radius * 0.6f));
            var candidate = new MapCoordinates(zone.Center.Position + offset, zone.Center.MapId);

            if (!TryGetFreeTile(candidate, out var tileCoordinates, out _))
                continue;

            SpawnAtPosition(podPrototype, tileCoordinates);
            return;
        }
    }

    #endregion

    #region Orders

    private void GiveOrders(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var defenders = new List<MapCoordinates>();
        CollectLivingDefenders(rule.Comp, defenders, out _);

        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent, MobStateComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent, out var mobStateComponent))
        {
            if (unitComponent.RuleUid != rule.Owner ||
                !unitComponent.GiveOrders ||
                mobStateComponent.CurrentState != MobState.Alive)
                continue;

            OrderUnit(rule.Comp, unitUid, defenders);
        }
    }

    /// <summary>
    ///     Points a unit at the nearest defender outside the safe zones, or at the nearest defender objective when
    ///         every defender is safe. The commander watches from orbit, so it always knows where they are.
    /// </summary>
    private void OrderUnit(KsFieldCommanderRuleComponent component, EntityUid unitUid, List<MapCoordinates> defenders)
    {
        if (HasComp<Content.Server._KS14.SupplyPod.Passengers.KsSupplyPodPassengerComponent>(unitUid))
            return;

        var unitCoordinates = _transformSystem.GetMapCoordinates(unitUid);

        MapCoordinates? target = null;
        var bestDistance = float.MaxValue;

        foreach (var defender in defenders)
        {
            if (defender.MapId != unitCoordinates.MapId || IsInSafeZone(component, defender))
                continue;

            var distance = (defender.Position - unitCoordinates.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            target = defender;
        }

        if (target == null)
        {
            var objectives = new List<MapCoordinates>();
            CollectDefenderObjectives(component, objectives);

            foreach (var objective in objectives)
            {
                if (objective.MapId != unitCoordinates.MapId || IsInSafeZone(component, objective))
                    continue;

                var distance = (objective.Position - unitCoordinates.Position).LengthSquared();
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                target = objective;
            }
        }

        if (target is not { } targetCoordinates)
            return;

        _npcSensorSystem.AddEffect(unitUid,
            Content.Server._KS14.NPC.Systems.NpcSensorSystem.DisturbanceCoordinatesSensorKey,
            _transformSystem.ToCoordinates(targetCoordinates));
    }

    #endregion

    #region Strikes

    /// <summary>
    ///     Drops a missile on something the defenders built, or on a defender, outside the safe zones and away from
    ///         the defenders' own objectives.
    /// </summary>
    private bool TryStrike(KsFieldCommanderRuleComponent component, List<MapCoordinates> objectives, List<MapCoordinates> defenders)
    {
        var targets = new List<MapCoordinates>();

        var builtQuery = EntityQueryEnumerator<Content.Shared._KS14.RCD.KsRcdBuiltComponent, TransformComponent>();
        while (builtQuery.MoveNext(out var builtUid, out _, out var transformComponent))
        {
            targets.Add(_transformSystem.GetMapCoordinates(builtUid, transformComponent));
        }

        targets.AddRange(defenders);

        // Shuffled rather than picked, so that one bad candidate doesn't call the strike off.
        _robustRandom.Shuffle(targets);

        foreach (var target in targets)
        {
            if (IsInSafeZone(component, target, component.StrikeSafeMargin) ||
                IsNearAny(target, objectives, component.StrikeSafeMargin))
                continue;

            if (!TryGetStrikeTile(target, out var strikeCoordinates))
                continue;

            SpawnAtPosition(component.StrikePrototype, strikeCoordinates);
            AnnounceRandom(component, component.StrikeAnnouncements, major: false);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The tile under <paramref name="target"/>, whatever stands on it: a strike is meant to land on things.
    /// </summary>
    private bool TryGetStrikeTile(MapCoordinates target, out EntityCoordinates strikeCoordinates)
    {
        strikeCoordinates = default;

        if (!_mapSystem.TryFindGridAt(target, out var gridUid, out var gridComponent))
            return false;

        var indices = _mapSystem.TileIndicesFor(gridUid, gridComponent, target);
        var tileRef = _mapSystem.GetTileRef(gridUid, gridComponent, indices);

        if (tileRef.Tile.IsEmpty)
            return false;

        strikeCoordinates = _mapSystem.GridTileToLocal(gridUid, gridComponent, indices);
        return true;
    }

    #endregion
}
