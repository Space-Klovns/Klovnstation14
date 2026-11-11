using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Uplink

    /// <summary>
    ///     Lands the uplink far out from the defenders, with a guard, and tells everyone roughly where it is.
    /// </summary>
    private void DeployUplink(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderPhase phase, TimeSpan curTime)
    {
        var component = rule.Comp;

        if (component.UplinkPrototype is not { } uplinkPrototype || component.UplinkUid != null)
            return;

        var objectives = new List<MapCoordinates>();
        CollectDefenderObjectives(component, objectives);

        var defenders = new List<MapCoordinates>();
        CollectLivingDefenders(component, defenders, out _);

        var anchors = objectives.Count > 0 ? objectives : defenders;

        // Far enough out that reaching it is an expedition, but nowhere near a defender when it lands.
        if (!TryFindPlacement(component, anchors, defenders,
                component.MaxSpawnDistance * 0.6f, component.MaxSpawnDistance,
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

        TimeSpan? bombardmentIn = null;
        if (TryComp<TimedDespawnComponent>(uplinkUid, out var timedDespawnComponent))
        {
            bombardmentIn = TimeSpan.FromSeconds(timedDespawnComponent.Lifetime);
            component.UplinkBombardmentTime = curTime + bombardmentIn.Value;
        }

        SpawnUplinkGuard(rule, phase, uplinkCoordinates);
        LiftFogTo(component, _transformSystem.ToMapCoordinates(uplinkCoordinates));

        if (component.UplinkAnnouncement is { } announcement)
            Announce(component, announcement, major: true);

        var uplinkMapCoordinates = _transformSystem.ToMapCoordinates(uplinkCoordinates);
        var origin = GetBaseCentre(component, anchors);
        var offset = uplinkMapCoordinates.Position - origin;

        Intel(component, component.UplinkIntel,
            ("direction", GetCompassDirection(offset)),
            ("distance", (int)MathF.Round(offset.Length())),
            ("minutes", (int)Math.Ceiling(bombardmentIn?.TotalMinutes ?? 0)));
    }

    /// <summary>
    ///     Units that stay by the uplink rather than going after the defenders.
    /// </summary>
    private void SpawnUplinkGuard(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderPhase phase, EntityCoordinates uplinkCoordinates)
    {
        if (phase.UplinkGuardBudget <= 0f)
            return;

        var purchases = new List<int>();
        BuyAssets(rule.Comp, phase.UplinkGuardBudget, rule.Comp.HardMaxAlive, new Dictionary<int, int>(), purchases);

        var uplinkMapCoordinates = _transformSystem.ToMapCoordinates(uplinkCoordinates);

        foreach (var assetIndex in purchases)
        {
            var asset = rule.Comp.Assets[assetIndex];

            for (var i = 0; i < asset.Count; i++)
            {
                var guardCoordinates = uplinkCoordinates;
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var offset = _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(1.5f, 4f);
                    if (!TryGetFreeTile(new MapCoordinates(uplinkMapCoordinates.Position + offset, uplinkMapCoordinates.MapId), out var tileCoordinates, out _))
                        continue;

                    guardCoordinates = tileCoordinates;
                    break;
                }

                if (SpawnUnit(rule, asset, assetIndex, guardCoordinates) is not { } guardUid)
                    continue;

                rule.Comp.UnitsSent++;

                // Guards hold their ground; they fight whoever comes, and nothing sends them away.
                Comp<KsFieldCommanderUnitComponent>(guardUid).GiveOrders = false;
            }
        }
    }

    /// <summary>
    ///     The middle of what the defenders hold, to give directions from.
    /// </summary>
    private static Vector2 GetBaseCentre(KsFieldCommanderRuleComponent component, List<MapCoordinates> anchors)
    {
        var sum = Vector2.Zero;
        var count = 0;

        foreach (var zone in component.SafeZones)
        {
            sum += zone.Center.Position;
            count++;
        }

        if (count == 0)
        {
            foreach (var anchor in anchors)
            {
                sum += anchor.Position;
                count++;
            }
        }

        return count > 0 ? sum / count : Vector2.Zero;
    }

    private void UpdateUplink(KsFieldCommanderRuleComponent component, TimeSpan curTime)
    {
        if (component.UplinkWarned ||
            component.UplinkDestroyedTime != null ||
            component.UplinkBombardmentTime is not { } bombardmentTime ||
            curTime < bombardmentTime - component.UplinkWarningBefore)
            return;

        component.UplinkWarned = true;

        Intel(component, component.UplinkWarningIntel, ("minutes", (int)Math.Ceiling(component.UplinkWarningBefore.TotalMinutes)));
    }

    private void OnUplinkLost(Entity<KsFieldCommanderRuleComponent> rule)
    {
        // Lasting until its timer ran out is the commander's victory, not a loss.
        if (rule.Comp.UplinkBombardmentTime is { } bombardmentTime &&
            _gameTiming.CurTime >= bombardmentTime - TimeSpan.FromSeconds(1))
            return;

        rule.Comp.UplinkDestroyedTime = _gameTiming.CurTime;
        RewardUplink(rule);
    }

    #endregion

    #region Announcements

    private void Announce(KsFieldCommanderRuleComponent component, LocId messageId, bool major, params (string, object)[] arguments)
    {
        _chatSystem.DispatchGlobalAnnouncement(Loc.GetString(messageId, arguments),
            Loc.GetString(component.Sender),
            playSound: true,
            announcementSound: major ? component.MajorSound : component.RadioSound,
            colorOverride: component.AnnouncementColor);
    }

    /// <summary>
    ///     Queues the defenders' intelligence on what just happened, to follow the commander's taunt as a reply. The
    ///         text is formatted now, from the state it describes.
    /// </summary>
    private void Intel(KsFieldCommanderRuleComponent component, LocId? messageId, params (string, object)[] arguments)
    {
        if (messageId is not { } id)
            return;

        component.PendingIntel.Add(new KsFieldCommanderPendingIntel(_gameTiming.CurTime + component.IntelDelay, Loc.GetString(id, arguments)));
    }

    /// <summary>
    ///     Sends whatever intelligence is due.
    /// </summary>
    private void SendDueIntel(KsFieldCommanderRuleComponent component, TimeSpan curTime)
    {
        for (var i = 0; i < component.PendingIntel.Count; i++)
        {
            if (component.PendingIntel[i].At > curTime)
                continue;

            SendIntel(component, component.PendingIntel[i].Message);
            component.PendingIntel.RemoveAt(i);
            i--;
        }
    }

    private void SendIntel(KsFieldCommanderRuleComponent component, string message)
    {
        _chatSystem.DispatchGlobalAnnouncement(message,
            Loc.GetString(component.IntelSender),
            playSound: true,
            announcementSound: component.IntelSound,
            colorOverride: component.IntelColor);
    }

    /// <summary>
    ///     A compass direction for an offset from the defenders' base, for intelligence to give bearings in.
    /// </summary>
    private static string GetCompassDirection(Vector2 offset)
    {
        return offset.GetDir().ToString().ToLowerInvariant();
    }

    private void AnnounceRandom(KsFieldCommanderRuleComponent component, List<LocId> messageIds, bool major, params (string, object)[] arguments)
    {
        if (messageIds.Count == 0)
            return;

        Announce(component, _robustRandom.Pick(messageIds), major, arguments);
    }

    #endregion
}
