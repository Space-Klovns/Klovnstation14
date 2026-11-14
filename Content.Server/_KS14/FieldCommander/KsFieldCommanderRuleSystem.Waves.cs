using System.Numerics;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    /// <summary>
    ///     The least a purchase takes off the budget, so that buying assets that cost nothing still comes to an end.
    /// </summary>
    private const float MinPurchaseCost = 0.01f;

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
        var maxAlive = component.MaxAlive + (int)MathF.Floor(component.MaxAlivePerDefender * defenders) + GetEndlessExtraAlive(component);
        return Math.Min(maxAlive, component.HardMaxAlive);
    }

    /// <summary>
    ///     Eases off after the defenders took a beating, and presses harder after a wave that cost them nothing.
    /// </summary>
    public static float GetNextAdaptiveMultiplier(KsFieldCommanderRuleComponent component, float multiplier, int defenders, int defendersLost)
    {
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
            budget -= MathF.Max(pickedAsset.Cost, MinPurchaseCost);
            capacity -= pickedAsset.Count;
            aliveByAsset[picked] = aliveByAsset.GetValueOrDefault(picked) + pickedAsset.Count;
            purchases.Add(picked);
        }
    }

    #endregion

    #region Battlefield

    /// <summary>
    ///     What the commander knows of the field at one moment. It watches from orbit, so it always knows where the
    ///         defenders are.
    /// </summary>
    /// <param name="Defenders">Where every living defender is.</param>
    /// <param name="DeadDefenders">How many defenders are dead.</param>
    /// <param name="Objectives">Where every defender objective still standing is.</param>
    private readonly record struct Battlefield(List<MapCoordinates> Defenders, int DeadDefenders, List<MapCoordinates> Objectives)
    {
        /// <summary>
        ///     What everything is placed around: the defenders' objectives, or the defenders themselves once none is
        ///         left.
        /// </summary>
        public List<MapCoordinates> Anchors => Objectives.Count > 0 ? Objectives : Defenders;
    }

    private Battlefield SurveyBattlefield(KsFieldCommanderRuleComponent component)
    {
        var defenders = new List<MapCoordinates>();
        var deadDefenders = 0;

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

            defenders.Add(_transformSystem.GetMapCoordinates(memberUid, transformComponent));
        }

        var objectives = new List<MapCoordinates>();
        CollectDefenderObjectives(component, objectives);

        return new Battlefield(defenders, deadDefenders, objectives);
    }

    /// <summary>
    ///     Where every defender objective still standing is, but <paramref name="exceptUid"/>.
    /// </summary>
    private void CollectDefenderObjectives(KsFieldCommanderRuleComponent component, List<MapCoordinates> objectives, EntityUid? exceptUid = null)
    {
        var query = EntityQueryEnumerator<ScenarioObjectiveComponent, TransformComponent>();
        while (query.MoveNext(out var objectiveUid, out var objectiveComponent, out var transformComponent))
        {
            // One being removed, as a lost objective is, no longer stands.
            if (objectiveUid == exceptUid ||
                objectiveComponent.FactionId != component.DefenderFaction ||
                objectiveComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            objectives.Add(_transformSystem.GetMapCoordinates(objectiveUid, transformComponent));
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

            if (aliveByAsset != null)
                aliveByAsset[unitComponent.AssetIndex] = aliveByAsset.GetValueOrDefault(unitComponent.AssetIndex) + 1;
        }

        return count;
    }

    #endregion

    #region Waves

    /// <summary>
    ///     Schedules the next wave, and tells the HUD.
    /// </summary>
    private void SetNextWave(KsFieldCommanderRuleComponent component, TimeSpan time)
    {
        component.NextWaveTime = time;
        UpdateWaveStatus(component);
    }

    /// <summary>
    ///     Finds the map the wave status lives on, once the rule has a battlefield, and puts it there.
    /// </summary>
    private void PrepareWaveStatus(KsFieldCommanderRuleComponent component)
    {
        var objectives = new List<MapCoordinates>();
        CollectDefenderObjectives(component, objectives);
        if (objectives.Count == 0 || _mapSystem.GetMapOrInvalid(objectives[0].MapId) is not { Valid: true } mapUid)
            return;

        component.WaveStatusMapUid = mapUid;
        UpdateWaveStatus(component);
    }

    private void UpdateWaveStatus(KsFieldCommanderRuleComponent component)
    {
        if (component.WaveStatusMapUid is not { } mapUid || TerminatingOrDeleted(mapUid))
            return;

        var statusComponent = EnsureComp<KsFieldCommanderWaveStatusComponent>(mapUid);
        statusComponent.Wave = component.WavesSent;
        statusComponent.NextWaveTime = component.NextWaveTime == TimeSpan.MaxValue ? null : component.NextWaveTime;
        Dirty(mapUid, statusComponent);
    }

    private void ClearWaveStatus(KsFieldCommanderRuleComponent component)
    {
        if (component.WaveStatusMapUid is not { } mapUid)
            return;

        component.WaveStatusMapUid = null;
        if (!TerminatingOrDeleted(mapUid))
            RemComp<KsFieldCommanderWaveStatusComponent>(mapUid);
    }

    private void SendWave(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderPhase phase)
    {
        var battlefield = SurveyBattlefield(rule.Comp);
        var defenderCount = battlefield.Defenders.Count;

        if (defenderCount == 0)
            return;

        var defendersLost = Math.Max(0, battlefield.DeadDefenders - rule.Comp.DeadDefendersAtLastWave);
        if (rule.Comp.WavesSent > 0)
            rule.Comp.AdaptiveMultiplier = GetNextAdaptiveMultiplier(rule.Comp, rule.Comp.AdaptiveMultiplier, defenderCount, defendersLost);

        rule.Comp.DeadDefendersAtLastWave = battlefield.DeadDefenders;

        var aliveByAsset = new Dictionary<int, int>();
        var capacity = GetMaxAlive(rule.Comp, defenderCount) - CountLivingUnits(rule.Owner, aliveByAsset);

        var budgetScale = GetEndlessBudgetScale(rule.Comp);
        var budget = rule.Comp.CarriedBudget + GetWaveBudget(phase, defenderCount, rule.Comp.AdaptiveMultiplier) * budgetScale;
        var purchases = new List<int>();
        var leftover = BuyAssets(rule.Comp, budget, capacity, aliveByAsset, purchases);

        var arrivalSum = Vector2.Zero;
        var arrivalCount = 0;
        var sent = Deliver(rule, purchases, battlefield, ref arrivalSum, ref arrivalCount);

        // Saving up lets a later wave afford something big, but never more than one wave's worth. A wave that found
        //      nowhere to go keeps what it would have spent.
        rule.Comp.CarriedBudget = MathF.Min(sent > 0 ? leftover : budget, phase.Budget * budgetScale);

        var struck = _robustRandom.Prob(Math.Clamp(phase.StrikeChance, 0f, 1f)) && TryStrike(rule.Comp, battlefield);

        if (sent == 0 && !struck)
            return;

        rule.Comp.WavesSent++;
        if (rule.Comp.Endless)
            rule.Comp.EndlessWavesSent++;

        UpdateWaveStatus(rule.Comp);

        if (sent == 0)
            return;

        // Where the wave as a whole is coming from, as the defenders' sensors would see it.
        var direction = arrivalCount > 0
            ? GetCompassDirection(arrivalSum / arrivalCount - GetBaseCentre(rule.Comp, battlefield.Anchors))
            : string.Empty;

        Transmit(rule.Comp, PickRandom(rule.Comp.WaveAnnouncements), major: false, rule.Comp.WaveIntel,
            ("count", sent), ("direction", direction), ("number", rule.Comp.WavesSent));

        // Once each for whatever came, however many of it.
        var reported = new HashSet<KsFieldCommanderAsset>();
        foreach (var assetIndex in purchases)
        {
            var asset = rule.Comp.Assets[assetIndex];
            if (reported.Add(asset))
                Transmit(rule.Comp, asset.Announcement, major: false, asset.Intel);
        }
    }

    /// <summary>
    ///     Sends the purchased assets in, in groups: on foot from out of sight, or by pod. Returns how many units went,
    ///         and adds where each group arrived to <paramref name="arrivalSum"/>.
    /// </summary>
    private int Deliver(Entity<KsFieldCommanderRuleComponent> rule,
        List<int> purchases,
        Battlefield battlefield,
        ref Vector2 arrivalSum,
        ref int arrivalCount)
    {
        var footGroup = new List<int>();
        var podGroup = new List<int>();
        var sent = 0;

        foreach (var assetIndex in purchases)
        {
            var asset = rule.Comp.Assets[assetIndex];
            var byPod = asset.Delivery == KsFieldCommanderDelivery.Pod;
            var group = byPod ? podGroup : footGroup;
            var limit = byPod ? rule.Comp.MaxPerPod : rule.Comp.MaxPerGroup;

            if (group.Count > 0 && CountGroup(rule.Comp, group) + asset.Count > limit)
            {
                sent += DeliverGroup(rule, group, asset.Delivery, battlefield, ref arrivalSum, ref arrivalCount);
                group.Clear();
            }

            group.Add(assetIndex);
        }

        sent += DeliverGroup(rule, footGroup, KsFieldCommanderDelivery.Foot, battlefield, ref arrivalSum, ref arrivalCount);
        sent += DeliverGroup(rule, podGroup, KsFieldCommanderDelivery.Pod, battlefield, ref arrivalSum, ref arrivalCount);
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
        Battlefield battlefield,
        ref Vector2 arrivalSum,
        ref int arrivalCount)
    {
        if (group.Count == 0)
            return 0;

        var component = rule.Comp;
        EntityCoordinates coordinates;
        EntityUid? podUid = null;

        if (delivery == KsFieldCommanderDelivery.Foot &&
            TryFindPlacement(component, battlefield,
                component.MinSpawnDistance, component.MaxSpawnDistance,
                minObjectiveDistance: component.MinSpawnDistance,
                minDefenderDistance: component.MinDefenderDistance,
                hidden: true,
                out var footCoordinates,
                outsideFog: true))
        {
            coordinates = footCoordinates;
        }
        // Nowhere out of sight to walk in from, or meant to come by air anyway.
        else if (TryFindPlacement(component, battlefield,
                     component.MinPodObjectiveDistance, component.MaxSpawnDistance,
                     minObjectiveDistance: component.MinPodObjectiveDistance,
                     minDefenderDistance: component.MinPodDefenderDistance,
                     hidden: false,
                     out var podCoordinates))
        {
            coordinates = podCoordinates;
            podUid = SpawnAtPosition(component.PodPrototype, podCoordinates);
        }
        else
        {
            return 0;
        }

        arrivalSum += _transformSystem.ToMapCoordinates(coordinates).Position;
        arrivalCount++;
        return SpawnGroup(rule, group, coordinates, podUid);
    }

    private int SpawnGroup(Entity<KsFieldCommanderRuleComponent> rule, List<int> group, EntityCoordinates coordinates, EntityUid? podUid)
    {
        var spawned = 0;

        foreach (var assetIndex in group)
        {
            var asset = rule.Comp.Assets[assetIndex];
            for (var i = 0; i < asset.Count; i++)
            {
                if (SpawnUnit(rule, assetIndex, coordinates) is not { } unitUid)
                    continue;

                spawned++;

                if (podUid is { } pod && !_supplyPodPassengerSystem.TryBoard(unitUid, pod))
                    Log.Warning($"{ToPrettyString(unitUid)} could not board {ToPrettyString(pod)}, and arrives beside it.");
            }
        }

        return spawned;
    }

    /// <summary>
    ///     Spawns one unit of the asset at <paramref name="assetIndex"/> on the commander's side, and counts it sent.
    /// </summary>
    /// <param name="holdPosition">
    ///     Never gives it orders, whatever its asset says: it fights whoever comes, and nothing sends it away.
    /// </param>
    private EntityUid? SpawnUnit(Entity<KsFieldCommanderRuleComponent> rule, int assetIndex, EntityCoordinates coordinates, bool holdPosition = false)
    {
        var asset = rule.Comp.Assets[assetIndex];
        EntityUid unitUid;

        if (asset.Humanoid is { } humanoid)
            unitUid = _randomHumanoidSystem.SpawnRandomHumanoid(humanoid, coordinates, Loc.GetString(asset.HumanoidName));
        else if (asset.Entity is { } entity)
            unitUid = SpawnAtPosition(entity, coordinates);
        else
            return null;

        // Joins the side so that what it captures is the commander's, and counted for the round.
        var factionMemberComponent = EnsureComp<ScenarioFactionMemberComponent>(unitUid);
        factionMemberComponent.Id = rule.Comp.Faction;
        Dirty(unitUid, factionMemberComponent);

        var unitComponent = EnsureComp<KsFieldCommanderUnitComponent>(unitUid);
        unitComponent.RuleUid = rule.Owner;
        unitComponent.AssetIndex = assetIndex;
        unitComponent.GiveOrders = asset.GiveOrders && !holdPosition;

        rule.Comp.UnitsSent++;
        return unitUid;
    }

    #endregion

    #region Reinforcements

    /// <summary>
    ///     Drops a pod of supplies beside one of the defenders' safe zones, well inside it.
    /// </summary>
    private void DropReinforcements(KsFieldCommanderRuleComponent component, EntProtoId podPrototype)
    {
        if (component.SafeZones.Count == 0)
            return;

        var zone = _robustRandom.Pick(component.SafeZones);
        if (TryFindFreeTileAround(zone.Center, 1f, MathF.Max(1.5f, zone.Radius * 0.6f), component.PlacementAttempts, out var tileCoordinates))
            SpawnAtPosition(podPrototype, tileCoordinates);
    }

    #endregion
}
