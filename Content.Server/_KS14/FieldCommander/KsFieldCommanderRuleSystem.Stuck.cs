using System.Linq;
using Content.Server._KS14.Dissolve;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.SupplyPod.Passengers;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Robust.Shared.Map;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Stuck units

    [Dependency] private KsDissolveSystem _dissolveSystem = default!;
    [Dependency] private NPCSteeringSystem _npcSteeringSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private HTNSystem _htnSystem = default!;
    [Dependency] private EntityQuery<NPCSteeringComponent> _steeringQuery = default!;
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;

    private readonly List<Entity<KsFieldCommanderUnitComponent>> _stuckCheckUnits = new();

    /// <summary>
    ///     Which recovery step a unit that has got nowhere for <paramref name="stuckFor"/> is due.
    /// </summary>
    public static KsFieldCommanderStuckStage GetStuckStage(KsFieldCommanderRuleComponent component, TimeSpan stuckFor)
    {
        if (stuckFor >= component.StuckRelocateAfter)
            return KsFieldCommanderStuckStage.Relocate;

        if (stuckFor >= component.StuckNudgeAfter)
            return KsFieldCommanderStuckStage.Nudge;

        return stuckFor >= component.StuckReorderAfter
            ? KsFieldCommanderStuckStage.Reorder
            : KsFieldCommanderStuckStage.None;
    }

    /// <summary>
    ///     Forgets a unit's progress: it starts counting afresh from here.
    /// </summary>
    private void ResetStuck(KsFieldCommanderUnitComponent unitComponent)
    {
        unitComponent.LastProgressTime = _gameTiming.CurTime;
        unitComponent.BestDistance = float.MaxValue;
        unitComponent.GoalCoordinates = null;
        unitComponent.CheckedCoordinates = null;
        unitComponent.StuckStage = KsFieldCommanderStuckStage.None;
        unitComponent.StuckReason = KsFieldCommanderStuckReason.None;
        unitComponent.NudgeHeldSince = null;
    }

    /// <summary>
    ///     Looks at how far each of the rule's units has got since the last orders, and takes the next recovery step
    ///         with any that is getting nowhere. Each step is logged with what the unit was doing, for finding out why.
    /// </summary>
    private void CheckStuckUnits(Entity<KsFieldCommanderRuleComponent> rule, Battlefield battlefield)
    {
        _stuckCheckUnits.Clear();

        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            if (unitComponent.RuleUid == rule.Owner && IsUnitAlive(unitUid))
                _stuckCheckUnits.Add((unitUid, unitComponent));
        }

        // Gathered first: a step may take a unit off the field, and send another.
        foreach (var unit in _stuckCheckUnits)
        {
            if (!TerminatingOrDeleted(unit) && unit.Comp.LifeStage <= ComponentLifeStage.Running)
                CheckStuck(rule, unit, battlefield);
        }

        _stuckCheckUnits.Clear();
    }

    private void CheckStuck(Entity<KsFieldCommanderRuleComponent> rule, Entity<KsFieldCommanderUnitComponent> unit, Battlefield battlefield)
    {
        var component = rule.Comp;
        var unitComponent = unit.Comp;
        var curTime = _gameTiming.CurTime;
        var position = _transformSystem.GetMapCoordinates(unit);

        if (IsExcusedFromStuck(component, unit, position, battlefield, curTime) ||
            !TryGetStuckGoal(unit, out var goal))
        {
            ResetStuck(unitComponent);
            return;
        }

        var distance = goal.MapId == position.MapId ? (goal.Position - position.Position).Length() : float.MaxValue;

        // Got there: whatever it does there, it is not stuck.
        if (distance <= component.StuckArrivedDistance)
        {
            ResetStuck(unitComponent);
            unitComponent.GoalCoordinates = goal;
            unitComponent.BestDistance = distance;
            unitComponent.CheckedCoordinates = position;
            return;
        }

        bool progressed;
        if (unitComponent.GoalCoordinates is not { } oldGoal || !IsWithin(oldGoal, goal, component.StuckProgressDistance))
        {
            // Going somewhere new: nothing to measure against yet, so getting anywhere at all counts.
            progressed = unitComponent.CheckedCoordinates is not { } oldPosition ||
                         !IsWithin(oldPosition, position, component.StuckProgressDistance);
            unitComponent.GoalCoordinates = goal;
            unitComponent.BestDistance = distance;
        }
        else
        {
            progressed = distance <= unitComponent.BestDistance - component.StuckProgressDistance;
            unitComponent.BestDistance = MathF.Min(unitComponent.BestDistance, distance);
        }

        unitComponent.CheckedCoordinates = position;

        if (progressed)
        {
            unitComponent.LastProgressTime = curTime;
            unitComponent.StuckStage = KsFieldCommanderStuckStage.None;
            unitComponent.StuckReason = KsFieldCommanderStuckReason.None;
            unitComponent.NudgeHeldSince = null;
            return;
        }

        var stage = GetStuckStage(component, curTime - unitComponent.LastProgressTime);
        var reason = KsFieldCommanderStuckReason.NoProgress;

        // Steering that has given up on a path won't find one by waiting: on to the next step now.
        if (_steeringQuery.TryComp(unit, out var steeringComponent) &&
            (steeringComponent.Status == SteeringStatus.NoPath || steeringComponent.FailedPathCount >= NPCSteeringComponent.FailedPathLimit))
        {
            var next = (KsFieldCommanderStuckStage)Math.Min((int)unitComponent.StuckStage + 1, (int)KsFieldCommanderStuckStage.Relocate);
            if (next > stage)
                stage = next;

            reason = KsFieldCommanderStuckReason.NoPath;
        }

        // A nudge held back by a defender watching is still owed, whatever the clock says: stay at the step reached.
        var nudgeHeld = unitComponent.NudgeHeldSince != null;
        if (nudgeHeld && stage < unitComponent.StuckStage)
            stage = unitComponent.StuckStage;

        // Each step is taken once; a held-back nudge and the last step are tried again, for as long as a defender is
        // watching.
        if (stage == KsFieldCommanderStuckStage.None ||
            stage <= unitComponent.StuckStage && stage != KsFieldCommanderStuckStage.Relocate && !nudgeHeld)
            return;

        unitComponent.StuckStage = stage;
        unitComponent.StuckReason = reason;

        Log.Info($"Field commander unit stuck, {stage}: {DescribeStuckUnit(unit)}");

        switch (stage)
        {
            case KsFieldCommanderStuckStage.Reorder:
                ReorderStuckUnit(component, unit, battlefield, position, goal);
                break;
            case KsFieldCommanderStuckStage.Nudge:
                NudgeStuckUnit(component, unit, battlefield, position, goal);
                break;
            case KsFieldCommanderStuckStage.Relocate:
                // Watched too long to be taken off the field: a nudge still owed can at least get it unstuck.
                if (!RelocateStuckUnit(rule, unit, battlefield, position) && unitComponent.NudgeHeldSince != null)
                    NudgeStuckUnit(component, unit, battlefield, position, goal);
                break;
        }
    }

    /// <summary>
    ///     Whether a unit has a reason to stand where it is: still in its pod, in a fight, or just hurt.
    /// </summary>
    private bool IsExcusedFromStuck(KsFieldCommanderRuleComponent component,
        Entity<KsFieldCommanderUnitComponent> unit,
        MapCoordinates position,
        Battlefield battlefield,
        TimeSpan curTime)
    {
        if (unit.Comp.LastDamagedTime is { } damagedTime && curTime - damagedTime < component.StuckDamageGrace)
            return true;

        if (HasComp<KsSupplyPodPassengerComponent>(unit) ||
            _npcPerceptionSystem.InCombatContact(unit.Owner))
            return true;

        // Units with no perception of their own (a borg) are in a fight when a defender is close and in sight.
        foreach (var defender in battlefield.Defenders)
        {
            if (IsWithin(defender, position, component.StuckContactRange) &&
                _npcLineOfSightSystem.InLineOfSight(defender, position, component.StuckContactRange))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Where a unit is going: where it is steering to, or else where it was last ordered.
    /// </summary>
    private bool TryGetStuckGoal(Entity<KsFieldCommanderUnitComponent> unit, out MapCoordinates goal)
    {
        if (_steeringQuery.TryComp(unit, out var steeringComponent) && steeringComponent.Coordinates.IsValid(EntityManager))
        {
            goal = _transformSystem.ToMapCoordinates(steeringComponent.Coordinates);
            return true;
        }

        if (unit.Comp.OrderCoordinates is { } orderCoordinates && orderCoordinates.IsValid(EntityManager))
        {
            goal = _transformSystem.ToMapCoordinates(orderCoordinates);
            return true;
        }

        goal = default;
        return false;
    }

    /// <summary>
    ///     Points a unit that takes orders at the nearest anchor other than the one it is stuck going to, drops its
    ///         path, and has it plan again.
    /// </summary>
    private void ReorderStuckUnit(KsFieldCommanderRuleComponent component,
        Entity<KsFieldCommanderUnitComponent> unit,
        Battlefield battlefield,
        MapCoordinates position,
        MapCoordinates goal)
    {
        if (unit.Comp.GiveOrders)
        {
            MapCoordinates? next = null;
            var nextDistance = float.MaxValue;

            foreach (var anchor in battlefield.Anchors)
            {
                if (anchor.MapId != position.MapId ||
                    IsWithin(anchor, goal, component.StuckArrivedDistance) ||
                    IsInSafeZone(component, anchor))
                    continue;

                var anchorDistance = (anchor.Position - position.Position).LengthSquared();
                if (anchorDistance >= nextDistance)
                    continue;

                next = anchor;
                nextDistance = anchorDistance;
            }

            OrderUnitTo(unit, next ?? goal);
        }

        if (_steeringQuery.TryComp(unit, out var steeringComponent))
        {
            steeringComponent.PathfindToken?.Cancel();
            steeringComponent.PathfindToken = null;
            steeringComponent.CurrentPath.Clear();
            steeringComponent.FailedPathCount = 0;
        }

        _npcSensorSystem.RequestReplan(unit);
    }

    /// <summary>
    ///     Throws a unit's steering away, which fails the move it was on and so has it plan again, and moves it onto
    ///         the free tile beside it nearest where it was going, where no defender can see it go. Seen, the move
    ///         waits for a later check; held back longer than <see cref="KsFieldCommanderRuleComponent.StuckNudgeInSightAfter"/>,
    ///         it is made in sight anyway.
    /// </summary>
    private void NudgeStuckUnit(KsFieldCommanderRuleComponent component,
        Entity<KsFieldCommanderUnitComponent> unit,
        Battlefield battlefield,
        MapCoordinates position,
        MapCoordinates goal)
    {
        var curTime = _gameTiming.CurTime;
        var firstTry = unit.Comp.NudgeHeldSince == null;

        // Nobody sees steering go: that much happens at once.
        if (firstTry)
            _npcSteeringSystem.Unregister(unit);

        var forced = unit.Comp.NudgeHeldSince is { } heldSince &&
                     curTime - heldSince >= component.StuckNudgeInSightAfter;
        var seenHere = !forced && IsSeenByDefenders(component, position, battlefield.Defenders);

        EntityCoordinates? best = null;
        var bestDistance = float.MaxValue;
        var anyFree = false;

        for (var x = -1; x <= 1 && !seenHere; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var candidate = new MapCoordinates(position.Position + new System.Numerics.Vector2(x, y), position.MapId);
                if (!TryGetFreeTile(candidate, out var tileCoordinates, out var tileMapCoordinates))
                    continue;

                anyFree = true;

                var candidateDistance = (tileMapCoordinates.Position - goal.Position).LengthSquared();
                if (candidateDistance >= bestDistance ||
                    !forced && IsSeenByDefenders(component, tileMapCoordinates, battlefield.Defenders))
                    continue;

                best = tileCoordinates;
                bestDistance = candidateDistance;
            }
        }

        if (best is { } bestCoordinates)
        {
            _transformSystem.SetCoordinates(unit, bestCoordinates);
            if (forced)
                Log.Info($"Field commander unit {ToPrettyString(unit)} was held back from a nudge for too long, and was moved in a defender's sight.");
        }
        else if (seenHere || anyFree)
        {
            // Somewhere to go, but a defender would see it: try again at the next check.
            if (firstTry)
            {
                unit.Comp.NudgeHeldSince = curTime;
                _npcSensorSystem.RequestReplan(unit);
            }

            Log.Info($"Field commander unit {ToPrettyString(unit)} is stuck but a defender would see it nudged, and stays for now.");
            return;
        }

        unit.Comp.NudgeHeldSince = null;
        _npcSensorSystem.RequestReplan(unit);
    }

    /// <summary>
    ///     Out of every defender's sight, dissolves a unit away and sends one like it from where a wave on foot would
    ///         come from, or puts its cost back in the budget if there is nowhere. Seen, it waits for the next check.
    /// </summary>
    /// <returns>Whether it was taken off the field; false while a defender sees it.</returns>
    private bool RelocateStuckUnit(Entity<KsFieldCommanderRuleComponent> rule,
        Entity<KsFieldCommanderUnitComponent> unit,
        Battlefield battlefield,
        MapCoordinates position)
    {
        var component = rule.Comp;
        if (IsSeenByDefenders(component, position, battlefield.Defenders))
        {
            Log.Info($"Field commander unit {ToPrettyString(unit)} is stuck but in sight of a defender, and stays for now.");
            return false;
        }

        var assetIndex = unit.Comp.AssetIndex;
        var giveOrders = unit.Comp.GiveOrders;

        // No longer the commander's: it stops counting at once, and a kill during the dissolve pays nothing.
        RemComp<KsFieldCommanderUnitComponent>(unit);
        _npcSteeringSystem.Unregister(unit);
        if (_htnQuery.TryComp(unit, out var htnComponent))
            _htnSystem.SetHTNEnabled((unit, htnComponent), false);
        _dissolveSystem.Dissolve(unit);

        if (assetIndex < 0 || assetIndex >= component.Assets.Count)
            return true;

        if (TryFindPlacement(component, battlefield,
                component.MinSpawnDistance, component.MaxSpawnDistance,
                minObjectiveDistance: component.MinSpawnDistance,
                minDefenderDistance: component.MinDefenderDistance,
                hidden: true,
                out var coordinates,
                outsideFog: true) &&
            SpawnUnit(rule, assetIndex, coordinates, holdPosition: !giveOrders, countSent: false) is { } replacementUid)
        {
            component.StuckUnitsRelocated++;
            var replacement = new Entity<KsFieldCommanderUnitComponent>(replacementUid, Comp<KsFieldCommanderUnitComponent>(replacementUid));
            if (replacement.Comp.GiveOrders)
                OrderUnit(component, replacement, battlefield);

            Log.Info($"Field commander unit {ToPrettyString(unit)} was sent again as {ToPrettyString(replacementUid)} at {_transformSystem.ToMapCoordinates(coordinates)}.");
            return true;
        }

        component.CarriedBudget += component.Assets[assetIndex].Cost;
        component.StuckUnitsRefunded++;
        Log.Info($"Field commander unit {ToPrettyString(unit)} had nowhere to be sent again; its cost went back in the budget.");
        return true;
    }

    /// <summary>
    ///     How many of a rule's units are stuck now, at any step.
    /// </summary>
    public int CountStuckUnits(EntityUid ruleUid)
    {
        var count = 0;
        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            if (unitComponent.RuleUid == ruleUid &&
                unitComponent.StuckStage != KsFieldCommanderStuckStage.None &&
                IsUnitAlive(unitUid))
                count++;
        }

        return count;
    }

    /// <summary>
    ///     Every unit of a rule that has got nowhere for at least <paramref name="minimum"/>, longest first, described.
    /// </summary>
    public List<string> DescribeStuckUnits(EntityUid ruleUid, TimeSpan minimum)
    {
        var curTime = _gameTiming.CurTime;
        var units = new List<Entity<KsFieldCommanderUnitComponent>>();

        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            // The dead are not checked, and lie where they fell.
            if (unitComponent.RuleUid == ruleUid &&
                curTime - unitComponent.LastProgressTime >= minimum &&
                IsUnitAlive(unitUid))
                units.Add((unitUid, unitComponent));
        }

        return units
            .OrderBy(unit => unit.Comp.LastProgressTime)
            .Select(DescribeStuckUnit)
            .ToList();
    }

    /// <summary>
    ///     What a unit is doing, as far as getting stuck goes: for the log and <c>ks_fcstuck</c>.
    /// </summary>
    private string DescribeStuckUnit(Entity<KsFieldCommanderUnitComponent> unit)
    {
        var unitComponent = unit.Comp;
        var position = _transformSystem.GetMapCoordinates(unit);

        var steering = "-";
        if (_steeringQuery.TryComp(unit, out var steeringComponent))
        {
            steering = Loc.GetString("ks-fieldcommander-stuck-steering",
                ("status", steeringComponent.Status.ToString()),
                ("failed", steeringComponent.FailedPathCount),
                ("path", steeringComponent.CurrentPath.Count),
                ("pathfinding", steeringComponent.Pathfind ? 1 : 0),
                ("target", steeringComponent.Coordinates.IsValid(EntityManager)
                    ? FormatPosition(_transformSystem.ToMapCoordinates(steeringComponent.Coordinates))
                    : "-"),
                ("range", steeringComponent.Range.ToString("0.0")));
        }

        var task = "-";
        if (_htnQuery.TryComp(unit, out var htnComponent))
        {
            task = !htnComponent.Enabled
                ? Loc.GetString("ks-fieldcommander-stuck-htn-disabled")
                : htnComponent.Plan is { } plan
                    ? Loc.GetString("ks-fieldcommander-stuck-htn-plan",
                        ("operator", plan.CurrentOperator.GetType().Name),
                        ("index", plan.Index),
                        ("count", plan.Tasks.Count),
                        ("branches", string.Join(".", plan.BranchTraversalRecord)))
                    : Loc.GetString("ks-fieldcommander-stuck-htn-none");
        }

        return Loc.GetString("ks-fieldcommander-stuck-unit",
            ("unit", ToPrettyString(unit)),
            ("position", FormatPosition(position)),
            ("goal", unitComponent.GoalCoordinates is { } goal ? FormatPosition(goal) : "-"),
            ("best", unitComponent.BestDistance == float.MaxValue ? "-" : unitComponent.BestDistance.ToString("0.0")),
            ("seconds", FormatSeconds(_gameTiming.CurTime - unitComponent.LastProgressTime)),
            ("stage", unitComponent.StuckStage.ToString()),
            ("reason", unitComponent.StuckReason.ToString()),
            ("steering", steering),
            ("task", task));
    }

    private static string FormatPosition(MapCoordinates coordinates)
    {
        return $"({coordinates.Position.X:0.0}, {coordinates.Position.Y:0.0})";
    }

    #endregion
}
