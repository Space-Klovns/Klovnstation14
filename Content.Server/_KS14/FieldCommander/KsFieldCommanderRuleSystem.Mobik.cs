using Content.Server.Ghost.Roles.Components;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    /// <summary>
    ///     How long after his pod found nowhere to land it tries again.
    /// </summary>
    private static readonly TimeSpan MobikRetryInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How often an admin's wave, queued for the ending, is tried while it waits for room.
    /// </summary>
    private static readonly TimeSpan MobikQueuedWaveInterval = TimeSpan.FromSeconds(5);

    #region Mobik

    // The ending (see KsFieldCommanderRuleComponent.Mobik). The uplink taken out, the round is held open while the
    //      commander's last units are dealt with; then he is announced, comes by pod after a delay, and his death pays
    //      the defenders and lets the round end, or the vote on endless mode open, as the uplink's loss did before.
    //      Every step is taken on the commander's tick, so a pause holds each of them.

    /// <summary>
    ///     Whether the ending is under way: the commander sends nothing of its own until it is over.
    /// </summary>
    private static bool IsMobikEnding(KsFieldCommanderRuleComponent component)
    {
        return component.MobikStage is not (KsFieldCommanderMobikStage.None or KsFieldCommanderMobikStage.Done);
    }

    /// <summary>
    ///     The uplink was taken out: holds the round open for the ending, if this commander has one and has not had it
    ///         yet. False, and the round goes on as it would have, if not.
    /// </summary>
    private bool TryBeginMobik(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var component = rule.Comp;
        if (component.Mobik == null ||
            component.MobikStage != KsFieldCommanderMobikStage.None ||
            component.Endless ||
            // The scenario would otherwise end the round over the lost uplink as soon as this returns.
            !_scenarioSystem.SetObjectivePending(component.Faction, pending: true))
            return false;

        component.MobikStage = KsFieldCommanderMobikStage.AwaitingClear;
        SetNextWave(component, TimeSpan.MaxValue);
        return true;
    }

    /// <summary>
    ///     For admins: announces the mobik now and sends him after his delay, without waiting for the last wave to be
    ///         dealt with. This is the ending: the commander sends nothing more of its own, and his death ends the round
    ///         as it would at the end. False if this commander has none, or he has been announced already.
    /// </summary>
    public bool TrySendMobik(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var component = rule.Comp;
        if (component.Mobik == null ||
            component.MobikStage is not (KsFieldCommanderMobikStage.None or KsFieldCommanderMobikStage.AwaitingClear))
            return false;

        SetNextWave(component, TimeSpan.MaxValue);
        AnnounceMobik(rule, GetCommanderTime(component));
        return true;
    }

    /// <summary>
    ///     Takes the ending's next step when it is due. Not called while paused.
    /// </summary>
    private void UpdateMobik(Entity<KsFieldCommanderRuleComponent> rule, TimeSpan curTime)
    {
        var component = rule.Comp;
        switch (component.MobikStage)
        {
            case KsFieldCommanderMobikStage.AwaitingClear:
                // A wave an admin queued still goes, and is dealt with like the rest, before he does.
                if (component.QueuedWaves.Count > 0)
                {
                    if (curTime >= component.NextWaveTime || component.NextWaveTime == TimeSpan.MaxValue)
                    {
                        ForceWave(rule);
                        SetNextWave(component, curTime + MobikQueuedWaveInterval);
                    }

                    return;
                }

                if (component.NextWaveTime != TimeSpan.MaxValue)
                    SetNextWave(component, TimeSpan.MaxValue);

                // Counted afresh each time: a unit taken off the field for being stuck is sent again under a new uid.
                if (CountLivingUnits(rule.Owner, null) > 0)
                    return;

                AnnounceMobik(rule, curTime);
                return;

            case KsFieldCommanderMobikStage.Announced:
                if (component.MobikArrivalTime is { } arrivalTime && curTime < arrivalTime)
                    return;

                if (!TryDeliverMobik(rule))
                    component.MobikArrivalTime = curTime + MobikRetryInterval;
                return;

            case KsFieldCommanderMobikStage.Arrived:
                if (component.MobikUid is { } mobikUid &&
                    !TerminatingOrDeleted(mobikUid) &&
                    HasComp<KsFieldCommanderUnitComponent>(mobikUid))
                    return;

                // Gone without being killed, deleted by an admin: the round must not wait for him forever.
                Log.Warning($"{ToPrettyString(rule.Owner)}'s mobik left the field without being killed; the round ends without him.");
                FinishMobik(rule);
                return;

            case KsFieldCommanderMobikStage.Killed:
                if (component.Mobik is { Aftermath.Count: > 0 } mobik)
                {
                    component.MobikStage = KsFieldCommanderMobikStage.Aftermath;
                    component.AftermathStepIndex = 0;
                    component.NextAftermathTime = curTime + mobik.Aftermath[0].Delay;
                    UpdateAftermath(rule, curTime);
                    return;
                }

                FinishMobik(rule);
                return;

            case KsFieldCommanderMobikStage.Aftermath:
                UpdateAftermath(rule, curTime);
                return;

            case KsFieldCommanderMobikStage.AftermathClear:
                // Counted afresh each time, for the same reason as above.
                if (CountLivingUnits(rule.Owner, null) > 0)
                    return;

                FinishMobik(rule);
                return;
        }
    }

    private void AnnounceMobik(Entity<KsFieldCommanderRuleComponent> rule, TimeSpan curTime)
    {
        var component = rule.Comp;
        var mobik = component.Mobik!;

        component.MobikStage = KsFieldCommanderMobikStage.Announced;
        component.MobikArrivalTime = curTime + mobik.Delay;

        if (mobik.Announcement is { } announcement)
        {
            _chatSystem.DispatchGlobalAnnouncement(Loc.GetString(announcement),
                Loc.GetString(mobik.Sender),
                playSound: true,
                announcementSound: component.MajorSound,
                colorOverride: component.AnnouncementColor);
        }

        Transmit(component, taunt: null, major: false, mobik.Intel,
            ("seconds", (int)Math.Ceiling(mobik.Delay.TotalSeconds)));
    }

    /// <summary>
    ///     Drops him in by pod. False, to be tried again shortly, if there is nowhere to land or nobody to send him at.
    /// </summary>
    private bool TryDeliverMobik(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var component = rule.Comp;
        var battlefield = SurveyBattlefield(component);
        if (battlefield.Defenders.Count == 0 ||
            !TryLandPod(component, battlefield, out var coordinates, out var podUid))
            return false;

        if (SpawnMobik(rule, coordinates) is not { } mobikUid)
        {
            QueueDel(podUid);
            return false;
        }

        BoardPod(mobikUid, podUid);
        component.MobikStage = KsFieldCommanderMobikStage.Arrived;
        Log.Info($"{ToPrettyString(rule.Owner)} sent its mobik, {ToPrettyString(mobikUid)}, by pod to {_transformSystem.ToMapCoordinates(coordinates)}.");
        return true;
    }

    /// <summary>
    ///     Spawns him as one of the commander's units, never a ghost role, and keeps track of him.
    /// </summary>
    private EntityUid? SpawnMobik(Entity<KsFieldCommanderRuleComponent> rule, Robust.Shared.Map.EntityCoordinates coordinates)
    {
        var component = rule.Comp;
        if (component.Mobik is not { } mobik ||
            SpawnAssetUnit(rule, mobik.Unit, assetIndex: -1, coordinates, mobik: true, holdPosition: false, countSent: false) is not { } mobikUid)
            return null;

        // Always the AI: the joke is that he has to fight, not that a player gets to.
        RemComp<GhostRoleComponent>(mobikUid);
        RemComp<GhostTakeoverAvailableComponent>(mobikUid);

        component.MobikUid = mobikUid;
        return mobikUid;
    }

    /// <summary>
    ///     A unit the ending sent is stuck where no defender can see it: sends it again, as what it was, from where a
    ///         wave on foot would come from, if there is anywhere, and keeps it stuck where it is if not.
    /// </summary>
    /// <returns>Whether it was taken off the field.</returns>
    private bool RelocateEndingUnit(Entity<KsFieldCommanderRuleComponent> rule,
        Entity<KsFieldCommanderUnitComponent> unit,
        Battlefield battlefield)
    {
        var component = rule.Comp;
        if (!TryFindPlacement(component, battlefield,
                component.MinSpawnDistance, component.MaxSpawnDistance,
                minObjectiveDistance: component.MinSpawnDistance,
                minDefenderDistance: component.MinDefenderDistance,
                hidden: true,
                out var coordinates,
                outsideFog: true))
            return false;

        var wasMobik = unit.Comp.Mobik;
        var step = unit.Comp.AftermathStep;
        var group = unit.Comp.AftermathGroup;
        var index = unit.Comp.AssetIndex;
        TakeOffField(unit);

        var replacement = wasMobik
            ? SpawnMobik(rule, coordinates)
            : SpawnAftermathUnit(rule, step, group, index, coordinates);

        if (replacement is not { } replacementUid)
        {
            // What it was spawns nothing any more: the round goes on without it rather than waiting for it.
            if (wasMobik)
                component.MobikUid = null;
            return true;
        }

        component.StuckUnitsRelocated++;
        var replacementEntity = new Entity<KsFieldCommanderUnitComponent>(replacementUid, Comp<KsFieldCommanderUnitComponent>(replacementUid));
        if (replacementEntity.Comp.GiveOrders)
            OrderUnit(component, replacementEntity, battlefield);

        Log.Info($"Field commander unit {ToPrettyString(unit)} was sent again as {ToPrettyString(replacementUid)} at {_transformSystem.ToMapCoordinates(coordinates)}.");
        return true;
    }

    /// <summary>
    ///     He died: every defender still standing is paid, and the ending is over on the commander's next tick.
    /// </summary>
    private void OnMobikKilled(Entity<KsFieldCommanderRuleComponent> rule, Entity<KsFieldCommanderUnitComponent> unit)
    {
        var component = rule.Comp;
        if (component.MobikStage != KsFieldCommanderMobikStage.Arrived ||
            component.MobikUid != unit.Owner ||
            component.Mobik is not { } mobik)
            return;

        component.MobikStage = KsFieldCommanderMobikStage.Killed;
        component.MobikKilled = true;
        RewardLivingDefenders(rule, mobik.BonusPoints, MobikReason);
        Transmit(component, taunt: null, major: false, mobik.KilledIntel);
    }

    /// <summary>
    ///     Lets the round end as the uplink's loss would have ended it: through the vote on endless mode where that
    ///         applies, or at once, won by the defenders.
    /// </summary>
    private void FinishMobik(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var component = rule.Comp;
        component.MobikStage = KsFieldCommanderMobikStage.Done;
        component.MobikUid = null;
        component.MobikArrivalTime = null;
        component.NextAftermathTime = null;

        if (TryStartEndlessVote(rule))
            return;

        _scenarioSystem.SetObjectivePending(component.Faction, pending: false);
        _scenarioSystem.CheckObjectiveRoundEnd();
    }

    #endregion
}
