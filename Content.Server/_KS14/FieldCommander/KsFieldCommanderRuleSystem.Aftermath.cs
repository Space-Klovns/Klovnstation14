using System.Numerics;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared.Light.Components;
using Robust.Shared.Map;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    [Dependency] private PoweredLightSystem _poweredLightSystem = default!;

    #region Aftermath

    // The steps after the mobik's death (KsFieldCommanderMobik.Aftermath), each on the commander's clock, so a pause
    //      holds them. Whatever they send is dealt with before the round may end (see .Mobik).

    /// <summary>
    ///     Takes every step that is due, in order. Once the last is taken, waits for what they sent.
    /// </summary>
    private void UpdateAftermath(Entity<KsFieldCommanderRuleComponent> rule, TimeSpan curTime)
    {
        var component = rule.Comp;
        if (component.Mobik is not { } mobik)
            return;

        while (component.AftermathStepIndex < mobik.Aftermath.Count &&
               component.NextAftermathTime is { } dueTime &&
               curTime >= dueTime)
        {
            var stepIndex = component.AftermathStepIndex;
            component.AftermathStepIndex++;
            component.NextAftermathTime = component.AftermathStepIndex < mobik.Aftermath.Count
                ? curTime + mobik.Aftermath[component.AftermathStepIndex].Delay
                : null;

            TakeAftermathStep(rule, stepIndex);
        }

        if (component.AftermathStepIndex >= mobik.Aftermath.Count)
            component.MobikStage = KsFieldCommanderMobikStage.AftermathClear;
    }

    private void TakeAftermathStep(Entity<KsFieldCommanderRuleComponent> rule, int stepIndex)
    {
        var component = rule.Comp;
        var step = component.Mobik!.Aftermath[stepIndex];
        var battlefield = SurveyBattlefield(component);

        if (step.LightsOut && battlefield.Anchors.Count > 0)
            TurnLightsOut(component, battlefield.Anchors[0].MapId);

        if (step.Announcement is { } announcement)
            SendAftermathAnnouncement(component, announcement);

        if (step.Groups.Count > 0)
            SendAftermathGroups(rule, stepIndex, step, battlefield);
    }

    /// <summary>
    ///     Turns off every powered light on <paramref name="mapId"/> that is on, and remembers it, to turn back on if the
    ///         rule ends while the round goes on.
    /// </summary>
    private void TurnLightsOut(KsFieldCommanderRuleComponent component, MapId mapId)
    {
        var query = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (query.MoveNext(out var lightUid, out var lightComponent, out var transformComponent))
        {
            if (!lightComponent.On || transformComponent.MapID != mapId)
                continue;

            _poweredLightSystem.SetState(lightUid, false, lightComponent);
            component.AftermathLightsOff.Add(lightUid);
        }
    }

    private void RestoreAftermathLights(KsFieldCommanderRuleComponent component)
    {
        foreach (var lightUid in component.AftermathLightsOff)
        {
            if (!TerminatingOrDeleted(lightUid) && TryComp<PoweredLightComponent>(lightUid, out var lightComponent))
                _poweredLightSystem.SetState(lightUid, true, lightComponent);
        }

        component.AftermathLightsOff.Clear();
    }

    /// <summary>
    ///     On the defenders' intelligence channel, in the server's culture if the text has it.
    /// </summary>
    private void SendAftermathAnnouncement(KsFieldCommanderRuleComponent component, KsFieldCommanderAftermathAnnouncement announcement)
    {
        string? text = null;
        if (Loc.DefaultCulture?.Name is { } culture)
            announcement.Text.TryGetValue(culture, out text);

        if (text == null)
        {
            foreach (var value in announcement.Text.Values)
            {
                text = value;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        _chatSystem.DispatchGlobalAnnouncement(text,
            Loc.GetString(announcement.Sender ?? component.IntelSender),
            playSound: announcement.Sound,
            announcementSound: announcement.Sound ? component.IntelSound : null,
            colorOverride: component.IntelColor);
    }

    /// <summary>
    ///     Each group on foot, out of the defenders' sight where there is anywhere, at bearings spread evenly around the
    ///         living defenders, ordered straight at them. Nothing is announced.
    /// </summary>
    private void SendAftermathGroups(Entity<KsFieldCommanderRuleComponent> rule, int stepIndex, KsFieldCommanderAftermathStep step, Battlefield battlefield)
    {
        var component = rule.Comp;
        var center = GetDefendersCentre(component, battlefield);
        var groupCount = step.Groups.Count;
        var firstBearing = _robustRandom.NextAngle();
        var halfArc = new Angle(Math.PI / (2 * groupCount));

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var bearing = firstBearing + new Angle(2 * Math.PI * groupIndex / groupCount);
            if (!TryFindPlacementToward(component, battlefield, center, bearing, halfArc, hidden: true, out var coordinates) &&
                !TryFindPlacementToward(component, battlefield, center, bearing, halfArc, hidden: false, out coordinates))
            {
                Log.Warning($"{ToPrettyString(rule.Owner)} found nowhere to send aftermath group {groupIndex} of step {stepIndex}.");
                continue;
            }

            var units = step.Groups[groupIndex].Units;
            for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                for (var i = 0; i < units[unitIndex].Count; i++)
                {
                    if (SpawnAftermathUnit(rule, stepIndex, groupIndex, unitIndex, coordinates) is not { } unitUid)
                        continue;

                    var unit = new Entity<KsFieldCommanderUnitComponent>(unitUid, Comp<KsFieldCommanderUnitComponent>(unitUid));
                    if (unit.Comp.GiveOrders)
                        OrderUnit(component, unit, battlefield);
                }
            }
        }
    }

    /// <summary>
    ///     Where the living defenders are, on average; the base's centre once none is left.
    /// </summary>
    private static MapCoordinates GetDefendersCentre(KsFieldCommanderRuleComponent component, Battlefield battlefield)
    {
        if (battlefield.Defenders.Count == 0)
        {
            var mapId = battlefield.Anchors.Count > 0 ? battlefield.Anchors[0].MapId : MapId.Nullspace;
            return new MapCoordinates(GetBaseCentre(component, battlefield.Anchors), mapId);
        }

        var sum = Vector2.Zero;
        foreach (var defender in battlefield.Defenders)
            sum += defender.Position;

        return new MapCoordinates(sum / battlefield.Defenders.Count, battlefield.Defenders[0].MapId);
    }

    /// <summary>
    ///     A placement as <see cref="TryFindPlacement"/> finds one for units on foot, but counted from
    ///         <paramref name="center"/> and within <paramref name="halfArc"/> of <paramref name="bearing"/>.
    /// </summary>
    private bool TryFindPlacementToward(KsFieldCommanderRuleComponent component,
        Battlefield battlefield,
        MapCoordinates center,
        Angle bearing,
        Angle halfArc,
        bool hidden,
        out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (center.MapId == MapId.Nullspace)
            return false;

        for (var attempt = 0; attempt < component.PlacementAttempts; attempt++)
        {
            var direction = bearing + new Angle((_robustRandom.NextDouble() * 2 - 1) * halfArc.Theta);
            var candidate = new MapCoordinates(center.Position + direction.ToVec() * _robustRandom.NextFloat(component.MinSpawnDistance, component.MaxSpawnDistance), center.MapId);

            if (!TryGetFreeTile(candidate, out var tileCoordinates, out var tileMapCoordinates) ||
                IsInSafeZone(component, tileMapCoordinates) ||
                IsNearAny(tileMapCoordinates, battlefield.Defenders, component.MinDefenderDistance) ||
                !IsBeyondFog(component, tileMapCoordinates, component.FogSpawnMargin) ||
                hidden && IsSeenByDefenders(component, tileMapCoordinates, battlefield.Defenders))
                continue;

            coordinates = tileCoordinates;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Spawns one of an aftermath group's units as one of the commander's, always the AI.
    /// </summary>
    private EntityUid? SpawnAftermathUnit(Entity<KsFieldCommanderRuleComponent> rule, int stepIndex, int groupIndex, int unitIndex, EntityCoordinates coordinates)
    {
        if (TryGetAftermathAsset(rule.Comp, stepIndex, groupIndex, unitIndex) is not { } asset ||
            SpawnAssetUnit(rule, asset, unitIndex, coordinates, mobik: false, holdPosition: false, countSent: false) is not { } unitUid)
            return null;

        RemComp<GhostRoleComponent>(unitUid);
        RemComp<GhostTakeoverAvailableComponent>(unitUid);

        var unitComponent = Comp<KsFieldCommanderUnitComponent>(unitUid);
        unitComponent.AftermathStep = stepIndex;
        unitComponent.AftermathGroup = groupIndex;
        return unitUid;
    }

    private static KsFieldCommanderAsset? TryGetAftermathAsset(KsFieldCommanderRuleComponent component, int stepIndex, int groupIndex, int unitIndex)
    {
        if (component.Mobik is not { } mobik ||
            stepIndex < 0 || stepIndex >= mobik.Aftermath.Count)
            return null;

        var groups = mobik.Aftermath[stepIndex].Groups;
        if (groupIndex < 0 || groupIndex >= groups.Count)
            return null;

        var units = groups[groupIndex].Units;
        return unitIndex >= 0 && unitIndex < units.Count ? units[unitIndex] : null;
    }

    #endregion
}
