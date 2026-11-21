using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Jianghu;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared._KS14.McqDialogue;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

// Honor duels: a formal challenge, a fight to submission, and face for the winner. Anyone who interferes is
// disgraced, anyone watching learns from it, and each side can wager what they hold.
public sealed partial class JianghuSystem
{
    public static readonly EntProtoId DuelAction = "ActionJianghuDuel";

    private const string WagerOption = "wager";
    private const string NoWagerOption = "no-wager";
    private const string AcceptOption = "accept";
    private const string AcceptWagerOption = "accept-wager";
    private const string RefuseOption = "refuse";

    private static readonly TimeSpan ChallengeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DuelDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DishonorDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DuelInsightCooldown = TimeSpan.FromMinutes(2);

    private const float AcceptRange = 6f;
    private const float FleeRange = 12f;
    private const float SpectatorRange = 7f;

    /// <summary>
    ///     A fighter whose damage reaches this share of their critical threshold has been beaten.
    /// </summary>
    private const float BeatenDamageFraction = 0.75f;

    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private McqDialogueSystem _mcqDialogueSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _spectators = new();

    public bool InDuel(EntityUid uid)
    {
        return HasComp<HonorDuelComponent>(uid);
    }

    #region Challenging

    [SubscribeLocalEvent]
    private void OnCultivatorChallenge(Entity<CultivatorComponent> entity, ref ChallengeDuelActionEvent args)
    {
        if (!args.Handled)
            args.Handled = TryChallenge(entity, args.Target);
    }

    [SubscribeLocalEvent]
    private void OnStylistChallenge(Entity<WuxiaStyleComponent> entity, ref ChallengeDuelActionEvent args)
    {
        if (!args.Handled)
            args.Handled = TryChallenge(entity, args.Target);
    }

    [SubscribeLocalEvent]
    private void OnBodyCultivatorChallenge(Entity<BodyCultivatorComponent> entity, ref ChallengeDuelActionEvent args)
    {
        if (!args.Handled)
            args.Handled = TryChallenge(entity, args.Target);
    }

    /// <summary>
    ///     Bow and issue a challenge. A challenger holding something is first asked whether to wager it.
    /// </summary>
    public bool TryChallenge(EntityUid challengerUid, EntityUid opponentUid)
    {
        if (opponentUid == challengerUid || !_mindSystem.TryGetMind(opponentUid, out _, out _) || !_mobStateSystem.IsAlive(opponentUid))
        {
            _noticeSystem.Notify(challengerUid, Loc.GetString("jianghu-duel-cannot-answer"));
            return false;
        }

        if (RefusesChallenge(challengerUid, opponentUid))
        {
            _noticeSystem.Notify(challengerUid, Loc.GetString("jianghu-duel-beneath-notice", ("target", opponentUid)));
            return false;
        }

        if (IsBusy(challengerUid) || IsBusy(opponentUid))
        {
            _noticeSystem.Notify(challengerUid, Loc.GetString("jianghu-duel-busy"));
            return false;
        }

        var challengeComponent = EnsureComp<DuelChallengeComponent>(challengerUid);
        challengeComponent.Target = opponentUid;
        challengeComponent.ExpiresAt = _gameTiming.CurTime + ChallengeTimeout;
        EnsureComp<DuelChallengedComponent>(opponentUid).Challenger = challengerUid;

        if (WagerCandidate(challengerUid) is { } stakeUid)
        {
            _noticeSystem.Notify(challengerUid, Loc.GetString("jianghu-duel-wager-ask", ("stake", stakeUid)));
            _mcqDialogueSystem.StartDialogue(opponentUid, challengerUid, new McqDialogueData[]
            {
                new("jianghu-duel-option-wager", WagerOption),
                new("jianghu-duel-option-no-wager", NoWagerOption),
            });
            return true;
        }

        SendChallenge((challengerUid, challengeComponent));
        return true;
    }

    private bool IsBusy(EntityUid uid)
    {
        return HasComp<HonorDuelComponent>(uid) || HasComp<DuelChallengeComponent>(uid) || HasComp<DuelChallengedComponent>(uid);
    }

    /// <summary>
    ///     What someone could wager: whatever is in their active hand, if they can let go of it.
    /// </summary>
    private EntityUid? WagerCandidate(EntityUid fighterUid)
    {
        if (_handsSystem.GetActiveItem(fighterUid) is not { } heldUid || HasComp<UnremoveableComponent>(heldUid))
            return null;

        return heldUid;
    }

    /// <summary>
    ///     The challenger answers whether to wager what they hold. The dialogue's source is the one challenged.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnChallengerWagerChosen(Entity<DuelChallengedComponent> entity, ref McqDialogueSelectedEvent args)
    {
        if (args.Actor != entity.Comp.Challenger || !TryComp<DuelChallengeComponent>(args.Actor, out var challengeComponent))
            return;

        if (args.Id == WagerOption)
            HoldStake(args.Actor);

        SendChallenge((args.Actor, challengeComponent));
    }

    private void SendChallenge(Entity<DuelChallengeComponent> challenger)
    {
        var opponentUid = challenger.Comp.Target;
        challenger.Comp.ExpiresAt = _gameTiming.CurTime + ChallengeTimeout;
        _noticeSystem.Announce(challenger, Loc.GetString("jianghu-duel-challenge", ("user", challenger.Owner), ("target", opponentUid)), type: PopupType.Medium);

        var options = new List<McqDialogueData> { new("jianghu-duel-option-accept", AcceptOption) };
        if (WagerCandidate(opponentUid) != null)
            options.Add(new McqDialogueData("jianghu-duel-option-accept-wager", AcceptWagerOption));
        options.Add(new McqDialogueData("jianghu-duel-option-refuse", RefuseOption));
        _mcqDialogueSystem.StartDialogue(challenger, opponentUid, options);
    }

    /// <summary>
    ///     The one challenged answers. The dialogue's source is the challenger.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnChallengeAnswered(Entity<DuelChallengeComponent> entity, ref McqDialogueSelectedEvent args)
    {
        var opponentUid = entity.Comp.Target;
        if (args.Actor != opponentUid)
            return;

        if (args.Id == RefuseOption)
        {
            _noticeSystem.Announce(opponentUid, Loc.GetString("jianghu-duel-refused", ("user", opponentUid), ("target", entity.Owner)));
            CancelChallenge(entity);
            return;
        }

        if (!_transformSystem.InRange((entity.Owner, null), (opponentUid, null), AcceptRange) || !_mobStateSystem.IsAlive(entity) || !_mobStateSystem.IsAlive(opponentUid))
        {
            CancelChallenge(entity);
            return;
        }

        if (args.Id == AcceptWagerOption)
            HoldStake(opponentUid);

        RemComp<DuelChallengeComponent>(entity);
        RemComp<DuelChallengedComponent>(opponentUid);
        StartDuel(entity, opponentUid);
    }

    private void CancelChallenge(Entity<DuelChallengeComponent> challenger)
    {
        RemCompDeferred<DuelChallengedComponent>(challenger.Comp.Target);
        RemCompDeferred<DuelChallengeComponent>(challenger);
        ReturnStake(challenger);
    }

    private void UpdateChallenges(TimeSpan now)
    {
        var challengeQuery = EntityQueryEnumerator<DuelChallengeComponent>();
        while (challengeQuery.MoveNext(out var challengerUid, out var challengeComponent))
        {
            if (now < challengeComponent.ExpiresAt || challengeComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            _noticeSystem.Notify(challengerUid, Loc.GetString("jianghu-duel-unanswered"));
            CancelChallenge((challengerUid, challengeComponent));
        }
    }

    #endregion

    #region Stakes

    /// <summary>
    ///     Take a wager out of its owner's hands until the duel is decided.
    /// </summary>
    public void HoldStake(EntityUid fighterUid)
    {
        if (WagerCandidate(fighterUid) is not { } stakeUid)
            return;

        var stakeContainer = _containerSystem.EnsureContainer<ContainerSlot>(fighterUid, HonorDuelComponent.StakeContainer);
        if (stakeContainer.ContainedEntity != null || !_containerSystem.Insert(stakeUid, stakeContainer))
            return;

        _noticeSystem.Announce(fighterUid, Loc.GetString("jianghu-duel-stake-laid", ("user", fighterUid), ("stake", stakeUid)), type: PopupType.Medium);
    }

    private void ReturnStake(EntityUid fighterUid)
    {
        GiveStake(fighterUid, fighterUid);
    }

    /// <summary>
    ///     Hand whatever the fighter wagered to the receiver, or drop it where it was if the receiver is gone.
    /// </summary>
    private EntityUid? GiveStake(EntityUid fighterUid, EntityUid receiverUid)
    {
        if (!_containerSystem.TryGetContainer(fighterUid, HonorDuelComponent.StakeContainer, out var stakeContainer)
            || stakeContainer.ContainedEntities.Count == 0)
            return null;

        var stakeUid = stakeContainer.ContainedEntities[0];
        _containerSystem.Remove(stakeUid, stakeContainer, force: true);
        if (!TerminatingOrDeleted(receiverUid))
            _handsSystem.PickupOrDrop(receiverUid, stakeUid, checkActionBlocker: false);

        return stakeUid;
    }

    #endregion

    #region The duel

    public void StartDuel(EntityUid oneUid, EntityUid twoUid)
    {
        var endsAt = _gameTiming.CurTime + DuelDuration;
        foreach (var (fighterUid, opponentUid) in new[] { (oneUid, twoUid), (twoUid, oneUid) })
        {
            var duelComponent = EnsureComp<HonorDuelComponent>(fighterUid);
            duelComponent.Opponent = opponentUid;
            duelComponent.EndsAt = endsAt;
            duelComponent.Ended = false;
            // No yield button: walking away from the duel is how you concede.
        }

        _visualsSystem.Wave(oneUid, FxColour.Blood, big: true);
        _noticeSystem.Announce(oneUid, Loc.GetString("jianghu-duel-begins", ("one", oneUid), ("two", twoUid)), type: PopupType.LargeCaution);
    }

    private void UpdateDuels(TimeSpan now)
    {
        var duelQuery = EntityQueryEnumerator<HonorDuelComponent>();
        while (duelQuery.MoveNext(out var fighterUid, out var duelComponent))
        {
            if (duelComponent.Ended)
                continue;

            var opponentUid = duelComponent.Opponent;
            if (IsBeaten(fighterUid))
                FinishDuel(fighterUid, opponentUid, winnerUid: opponentUid, outcome: HonorDuelOutcome.Defeated);
            else if (IsBeaten(opponentUid))
                FinishDuel(fighterUid, opponentUid, winnerUid: fighterUid, outcome: HonorDuelOutcome.Defeated);
            else if (!_transformSystem.InRange((fighterUid, null), (opponentUid, null), FleeRange))
                FinishDuel(fighterUid, opponentUid, winnerUid: opponentUid, outcome: HonorDuelOutcome.Fled);
            else if (now >= duelComponent.EndsAt)
                FinishDuel(fighterUid, opponentUid, winnerUid: null, outcome: HonorDuelOutcome.Draw);
        }
    }

    /// <summary>
    ///     Down, out of stamina, or three quarters of the way to critical condition.
    /// </summary>
    private bool IsBeaten(EntityUid fighterUid)
    {
        if (TerminatingOrDeleted(fighterUid) || !_mobStateSystem.IsAlive(fighterUid))
            return true;

        if (TryComp<StaminaComponent>(fighterUid, out var staminaComponent) && staminaComponent.Critical)
            return true;

        return _mobThresholdSystem.TryGetThresholdForState(fighterUid, MobState.Critical, out var critThreshold)
            && _damageableSystem.GetTotalDamage(fighterUid) >= critThreshold.Value * BeatenDamageFraction;
    }

    /// <summary>
    ///     Decide the duel. With no winner it is a draw and both earn a little face for an honorable fight.
    /// </summary>
    public void FinishDuel(EntityUid oneUid, EntityUid twoUid, EntityUid? winnerUid, HonorDuelOutcome outcome)
    {
        if (!TryComp<HonorDuelComponent>(oneUid, out var oneDuel) || oneDuel.Ended)
            return;

        oneDuel.Ended = true;
        if (TryComp<HonorDuelComponent>(twoUid, out var twoDuel))
            twoDuel.Ended = true;

        EntityUid? loserUid = winnerUid == null ? null : winnerUid == oneUid ? twoUid : oneUid;
        if (winnerUid is not { } winner || loserUid is not { } loser)
        {
            _noticeSystem.Announce(oneUid, Loc.GetString("jianghu-duel-draw", ("one", oneUid), ("two", twoUid)), type: PopupType.Large);
            // A draw earns nothing: otherwise two friends could farm face by drawing over and over.
            ReturnStake(oneUid);
            ReturnStake(twoUid);
        }
        else
        {
            _noticeSystem.Announce(winner, Loc.GetString("jianghu-duel-won", ("winner", winner), ("loser", loser),
                ("how", Loc.GetString($"jianghu-duel-how-{outcome}"))), type: PopupType.LargeCaution);

            var resolvingEvent = new HonorDuelResolvingEvent(winner, loser, 5);
            RaiseLocalEvent(ref resolvingEvent);
            AdjustFace(winner, resolvingEvent.FaceGain, "jianghu-reason-won");
            AdjustFace(loser, -2,
                outcome == HonorDuelOutcome.Fled ? "jianghu-reason-fled" : "jianghu-reason-lost");
            TryDuelInsight(winner, 10f);

            // The winner takes every stake.
            GiveStake(winner, winner);
            GiveStake(loser, winner);

            // Watching masters fight is educational, but only a fight someone actually won: a draw teaches nothing,
            //     so friends can't farm insight by drawing in front of each other.
            _spectators.Clear();
            _entityLookupSystem.GetEntitiesInRange(Transform(winner).Coordinates, SpectatorRange, _spectators);
            foreach (var spectatorEntity in _spectators)
            {
                if (spectatorEntity.Owner != oneUid && spectatorEntity.Owner != twoUid && _mobStateSystem.IsAlive(spectatorEntity))
                    TryDuelInsight(spectatorEntity, 5f);
            }
        }

        EndDuel(oneUid);
        EndDuel(twoUid);

        var finishedEvent = new HonorDuelFinishedEvent(oneUid, twoUid, winnerUid, loserUid, outcome);
        RaiseLocalEvent(ref finishedEvent);
    }

    private void TryDuelInsight(EntityUid uid, float insight)
    {
        if (!_cultivatorQuery.HasComp(uid) || EnsureFaceComponent(uid) is not { } faceComponent
            || _gameTiming.CurTime < faceComponent.NextDuelInsight)
            return;

        faceComponent.NextDuelInsight = _gameTiming.CurTime + DuelInsightCooldown;
        _sharedCultivationSystem.AddInsight(uid, insight);
    }

    private void EndDuel(EntityUid fighterUid)
    {
        if (!TryComp<HonorDuelComponent>(fighterUid, out var duelComponent))
            return;

        RemCompDeferred<HonorDuelComponent>(fighterUid);
    }

    /// <summary>
    ///     A third party who strikes a duelist is disgraced.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDuelistDamaged(Entity<HonorDuelComponent> entity, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || entity.Comp.Ended || args.Origin is not { } meddlerUid
            || meddlerUid == entity.Owner || meddlerUid == entity.Comp.Opponent
            || !_mindSystem.TryGetMind(meddlerUid, out _, out _) || IsDishonored(meddlerUid))
            return;

        Dishonor(meddlerUid, DishonorDuration);
        AdjustFace(meddlerUid, -5, "jianghu-reason-interfered");
        _noticeSystem.Announce(meddlerUid, Loc.GetString("jianghu-duel-interference", ("user", meddlerUid)), type: PopupType.LargeCaution);
    }

    [SubscribeLocalEvent]
    private void OnDuelistTerminating(Entity<HonorDuelComponent> entity, ref EntityTerminatingEvent args)
    {
        // With no opponent left to win it (or one already gone too), the duel just ends quietly.
        if (!entity.Comp.Opponent.IsValid() || TerminatingOrDeleted(entity.Comp.Opponent))
        {
            entity.Comp.Ended = true;
            return;
        }

        FinishDuel(entity, entity.Comp.Opponent, winnerUid: entity.Comp.Opponent, outcome: HonorDuelOutcome.Vanished);
    }

    [SubscribeLocalEvent]
    private void OnDuelistExamined(Entity<HonorDuelComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("jianghu-examine-dueling", ("target", entity.Owner), ("opponent", entity.Comp.Opponent)));
    }

    #endregion
}
