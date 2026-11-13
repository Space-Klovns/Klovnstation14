using Robust.Shared.Map;
using Robust.Shared.Spawners;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    /// <summary>
    ///     How close to its bombardment an uplink taken out still counts as having lasted: its own timer deletes it at
    ///         about that moment, and that is the commander's victory, not the defenders'.
    /// </summary>
    private static readonly TimeSpan UplinkLastedTolerance = TimeSpan.FromSeconds(1);

    #region Uplink

    /// <summary>
    ///     Lands the uplink far out from the defenders, with a guard, and tells everyone roughly where it is.
    /// </summary>
    private void DeployUplink(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderPhase phase, TimeSpan curTime)
    {
        var component = rule.Comp;

        if (component.UplinkPrototype is not { } uplinkPrototype || component.UplinkUid != null)
            return;

        var battlefield = SurveyBattlefield(component);

        // Far enough out that reaching it is an expedition, but nowhere near a defender when it lands.
        if (!TryFindPlacement(component, battlefield,
                component.MaxSpawnDistance * component.UplinkDistanceFraction, component.MaxSpawnDistance,
                minObjectiveDistance: component.MinSpawnDistance,
                minDefenderDistance: component.MinDefenderDistance,
                hidden: false,
                out var uplinkCoordinates,
                outsideFog: true))
        {
            Log.Warning("Field commander found nowhere to land its uplink, and will try again with the next wave.");
            return;
        }

        var uplinkUid = SpawnAtPosition(uplinkPrototype, uplinkCoordinates);
        component.UplinkUid = uplinkUid;
        component.UplinkDeployedTime = curTime;

        // The uplink is the objective the commander was holding back; from here on, losing it loses the round.
        _scenarioSystem.SetObjectivePending(component.Faction, pending: false);

        var bombardmentIn = TimeSpan.Zero;
        if (TryComp<TimedDespawnComponent>(uplinkUid, out var timedDespawnComponent))
        {
            bombardmentIn = TimeSpan.FromSeconds(timedDespawnComponent.Lifetime);
            component.UplinkBombardmentTime = curTime + bombardmentIn;
        }

        var uplinkMapCoordinates = _transformSystem.ToMapCoordinates(uplinkCoordinates);
        SpawnUplinkGuard(rule, phase, uplinkMapCoordinates);
        LiftFogTo(component, uplinkMapCoordinates);

        var offset = uplinkMapCoordinates.Position - GetBaseCentre(component, battlefield.Anchors);
        Transmit(component, component.UplinkAnnouncement, major: true, component.UplinkIntel,
            ("direction", GetCompassDirection(offset)),
            ("distance", (int)MathF.Round(offset.Length())),
            ("minutes", (int)Math.Ceiling(bombardmentIn.TotalMinutes)));
    }

    /// <summary>
    ///     Units that stay by the uplink rather than going after the defenders.
    /// </summary>
    private void SpawnUplinkGuard(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderPhase phase, MapCoordinates uplinkCoordinates)
    {
        if (phase.UplinkGuardBudget <= 0f)
            return;

        var purchases = new List<int>();
        BuyAssets(rule.Comp, phase.UplinkGuardBudget, rule.Comp.HardMaxAlive, new Dictionary<int, int>(), purchases);

        var uplinkEntityCoordinates = _transformSystem.ToCoordinates(uplinkCoordinates);

        foreach (var assetIndex in purchases)
        {
            for (var i = 0; i < rule.Comp.Assets[assetIndex].Count; i++)
            {
                // Beside it if there's room, or on it.
                var guardCoordinates = TryFindFreeTileAround(uplinkCoordinates, 1.5f, rule.Comp.UplinkGuardRadius, rule.Comp.PlacementAttempts, out var tileCoordinates)
                    ? tileCoordinates
                    : uplinkEntityCoordinates;

                SpawnUnit(rule, assetIndex, guardCoordinates, holdPosition: true);
            }
        }
    }

    private void UpdateUplink(KsFieldCommanderRuleComponent component, TimeSpan curTime)
    {
        if (component.UplinkWarned ||
            component.UplinkDestroyedTime != null ||
            component.UplinkBombardmentTime is not { } bombardmentTime ||
            curTime < bombardmentTime - component.UplinkWarningBefore)
            return;

        component.UplinkWarned = true;

        Transmit(component, taunt: null, major: false, component.UplinkWarningIntel,
            ("minutes", (int)Math.Ceiling(component.UplinkWarningBefore.TotalMinutes)));
    }

    private void OnUplinkLost(Entity<KsFieldCommanderRuleComponent> rule)
    {
        if (rule.Comp.UplinkBombardmentTime is { } bombardmentTime &&
            _gameTiming.CurTime >= bombardmentTime - UplinkLastedTolerance)
            return;

        rule.Comp.UplinkDestroyedTime = _gameTiming.CurTime;
        RewardUplink(rule);
    }

    #endregion
}
