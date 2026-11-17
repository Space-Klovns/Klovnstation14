using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.ComplexShove;
using Content.Shared._KS14.KsPopup;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared._KS14.Resist;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Mobs.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Pulling.Events;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Grab;

/// <summary>
///     tgstation's grab levels on top of pulling. Ctrl-clicking someone pulls them (passive); ctrl-clicking them
///         again climbs the grabber's <see cref="KsGrabberComponent.Stages"/>: aggressive, neck, strangle. What
///         each stage does is data, see <see cref="KsGrabStagePrototype"/>. Letting go of the pull, by any means,
///         drops the grab back to the start.
/// </summary>
public sealed partial class KsGrabSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private KsPopupSystem _ksPopupSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedVirtualItemSystem _virtualItemSystem = default!;
    [Dependency] private ComplexShoveSystem _complexShoveSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedCombatModeSystem _combatModeSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingStateSystem = default!;

    private static readonly KsAudiencePopups PinPopups = new("ks-grab-pin-user", "ks-grab-pin-target", "ks-grab-pin-others");

    private static readonly KsAudiencePopups ResistSuccessPopups =
        new("ks-grab-resist-success-user", "ks-grab-resist-success-target", "ks-grab-resist-success-others");

    private static readonly KsAudiencePopups ResistFailPopups =
        new("ks-grab-resist-fail-user", "ks-grab-resist-fail-target", "ks-grab-resist-fail-others");

    #region Grabber

    [SubscribeLocalEvent]
    private void OnPullToggleAttempt(Entity<KsGrabberComponent> grabber, ref KsPullToggleAttemptEvent args)
    {
        if (args.Handled || !HasComp<KsGrabbableComponent>(args.Pulled))
            return;

        // Ctrl-clicking whoever you hold tightens the grip; letting go is H, the pulling alert or the verb.
        args.Handled = true;
        TryTighten(grabber, args.Pulled);
    }

    [SubscribeLocalEvent]
    private void OnPullStarted(EntityUid uid, KsGrabberComponent component, PullStartedMessage args)
    {
        Entity<KsGrabberComponent> grabber = (uid, component);
        if (args.PullerUid != grabber.Owner || !HasComp<KsGrabbableComponent>(args.PulledUid))
            return;

        EnterStage(grabber, args.PulledUid, null, 0);

        // A pull can't become an aggressive grip in the same breath.
        grabber.Comp.NextUpgrade = _gameTiming.CurTime + grabber.Comp.UpgradeCooldown;
        Dirty(grabber);
    }

    [SubscribeLocalEvent]
    private void OnPullStopped(EntityUid uid, KsGrabberComponent component, PullStoppedMessage args)
    {
        Entity<KsGrabberComponent> grabber = (uid, component);
        if (args.PullerUid != grabber.Owner)
            return;

        var oldStage = GetStage(grabber);
        grabber.Comp.Stage = 0;
        grabber.Comp.Pinned = false;
        Dirty(grabber);

        if (grabber.Comp.HandItem is { } handItem)
        {
            grabber.Comp.HandItem = null;
            if (!TerminatingOrDeleted(handItem) && _handsSystem.IsHolding(grabber.Owner, handItem) &&
                TryComp<VirtualItemComponent>(handItem, out var virtualItem))
            {
                _virtualItemSystem.DeleteVirtualItem((handItem, virtualItem), grabber);
            }
        }

        var victim = args.PulledUid;
        if (oldStage?.VictimAlert is { } alert)
            _alertsSystem.ClearAlert(victim, alert);

        if (!TerminatingOrDeleted(victim))
            _actionBlockerSystem.UpdateCanMove(victim);

        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(grabber);
    }

    [SubscribeLocalEvent]
    private void OnUpgradeDoAfter(EntityUid uid, KsGrabberComponent component, KsGrabUpgradeDoAfterEvent args)
    {
        Entity<KsGrabberComponent> grabber = (uid, component);
        if (args.Cancelled || args.Handled || args.Target is not { } victim)
            return;

        args.Handled = true;
        if (GetPulled(grabber) != victim || grabber.Comp.Stage != args.FromStage || !CanReach(grabber, victim, args.FromStage + 1))
            return;

        EnterStage(grabber, victim, GetStage(grabber), args.FromStage + 1);
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(EntityUid uid, KsGrabberComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        Entity<KsGrabberComponent> grabber = (uid, component);
        if (GetPulled(grabber) is not { } victim || !HasComp<KsGrabbableComponent>(victim) || GetStage(grabber) is not { } stage)
            return;

        var modifier = stage.GrabberSpeedModifier;
        if (_standingStateSystem.IsDown(victim))
            modifier *= stage.ProneDragSpeedModifier;

        args.ModifySpeed(modifier, modifier);
    }

    /// <summary>
    ///     Throwing the hand that holds someone throws them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBeforeThrow(Entity<KsGrabberComponent> grabber, ref BeforeThrowEvent args)
    {
        if (args.Cancelled || GetPulled(grabber) is not { } victim || GetStage(grabber) is not { AllowsThrow: true } stage ||
            !TryComp<VirtualItemComponent>(args.ItemUid, out var virtualItem) || virtualItem.BlockingEntity != victim)
        {
            return;
        }

        // Someone on their feet has to be brought down first: a shove pins them.
        if (!_standingStateSystem.IsDown(victim))
        {
            args.Cancelled = true;
            PopupGrabber(grabber, victim, "ks-grab-throw-need-down");
            return;
        }

        // Dropping the hand afterwards lets go of the pull, so the victim flies free.
        args.ItemUid = victim;
        args.ThrowSpeed *= stage.ThrowSpeedModifier;
        MarkThrown(victim, grabber);

        PopupGrab(grabber, victim, user: grabber, stage.ThrowPopups, PopupType.MediumCaution);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(grabber):user} threw {ToPrettyString(victim):target} out of a grab");
    }

    /// <summary>
    ///     Tries to move the grip on <paramref name="victim"/> one stage up, at once or after the stage's upgrade time.
    /// </summary>
    public bool TryTighten(Entity<KsGrabberComponent> grabber, EntityUid victim)
    {
        if (GetPulled(grabber) != victim)
            return false;

        // Anything past a pull is an attack.
        if (!_combatModeSystem.IsInCombatMode(grabber))
        {
            PopupGrabber(grabber, victim, "ks-grab-need-combat");
            return false;
        }

        if (_gameTiming.CurTime < grabber.Comp.NextUpgrade)
            return false;

        grabber.Comp.NextUpgrade = _gameTiming.CurTime + grabber.Comp.UpgradeCooldown;
        Dirty(grabber);

        var next = grabber.Comp.Stage + 1;
        if (next >= grabber.Comp.Stages.Count)
        {
            PopupGrabber(grabber, victim, "ks-grab-max");
            return false;
        }

        if (!CanReach(grabber, victim, next))
            return false;

        var nextStage = ProtoMan.Index(grabber.Comp.Stages[next]);
        if (nextStage.UpgradeTime <= TimeSpan.Zero)
        {
            EnterStage(grabber, victim, GetStage(grabber), next);
            return true;
        }

        var doAfter = new DoAfterArgs(EntityManager, grabber, nextStage.UpgradeTime,
            new KsGrabUpgradeDoAfterEvent(grabber.Comp.Stage), grabber, target: victim)
        {
            BreakOnMove = true,
            BlockDuplicate = true,
            NeedHand = false,
            RequireCanInteract = true,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfter))
            return false;

        PopupGrab(grabber, victim, user: grabber, nextStage.StartPopups, PopupType.MediumCaution);
        return true;
    }

    /// <summary>
    ///     Whether the grabber has what reaching <paramref name="stageIndex"/> needs, telling them if not.
    /// </summary>
    private bool CanReach(Entity<KsGrabberComponent> grabber, EntityUid victim, int stageIndex)
    {
        var stage = ProtoMan.Index(grabber.Comp.Stages[stageIndex]);
        if (!IsCloseEnough(grabber, victim, stage))
        {
            PopupGrabber(grabber, victim, "ks-grab-too-far");
            return false;
        }

        if (!stage.TakesHand || IsHoldingInHand(grabber, victim) || _handsSystem.TryGetEmptyHand(grabber.Owner, out _))
            return true;

        PopupGrabber(grabber, victim, "ks-grab-need-hand");
        return false;
    }

    /// <summary>
    ///     Whether the victim is within the stage's <see cref="KsGrabStagePrototype.RequiredDistance"/>.
    /// </summary>
    private bool IsCloseEnough(EntityUid grabber, EntityUid victim, KsGrabStagePrototype stage)
    {
        return stage.RequiredDistance is not { } distance ||
               _transformSystem.InRange(Transform(grabber).Coordinates, Transform(victim).Coordinates, distance);
    }

    private void EnterStage(Entity<KsGrabberComponent> grabber, EntityUid victim, KsGrabStagePrototype? oldStage, int stageIndex)
    {
        var stage = ProtoMan.Index(grabber.Comp.Stages[stageIndex]);
        grabber.Comp.Stage = stageIndex;
        grabber.Comp.NextDamage = _gameTiming.CurTime + stage.DamageInterval;

        if (stage.TakesHand && !IsHoldingInHand(grabber, victim) &&
            _virtualItemSystem.TrySpawnVirtualItemInHand(victim, grabber, out var handItem, silent: true))
        {
            grabber.Comp.HandItem = handItem;
        }

        Dirty(grabber);

        // tgstation: you can't drag something along while held tight yourself.
        if (stage.BlocksVictimMovement && TryComp<PullerComponent>(victim, out var victimPuller) &&
            TryComp<PullableComponent>(victimPuller.Pulling, out var victimPulled))
        {
            _pullingSystem.TryStopPull(victimPuller.Pulling.Value, victimPulled);
        }

        _actionBlockerSystem.UpdateCanMove(victim);
        if (stage.FloorsVictim)
            _stunSystem.TryKnockdown(victim, grabber.Comp.FloorKnockdownTime, drop: false);

        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(grabber);
        UpdateVictimAlert(victim, oldStage, stage);

        if (oldStage != null)
            _audioSystem.PlayPredicted(stage.Sound, victim, grabber, AudioParams.Default.WithVariation(0.05f));

        PopupGrab(grabber, victim, user: grabber, stage.EnterPopups,
            stage.BlocksVictimMovement ? PopupType.MediumCaution : PopupType.Small);

        if (oldStage != null)
        {
            _adminLogger.Add(LogType.Action, stage.Damage != null ? LogImpact.High : LogImpact.Medium,
                $"{ToPrettyString(grabber):user} tightened their grab on {ToPrettyString(victim):target} to {stage.ID}");
        }

        var changed = new KsGrabStageChangedEvent(grabber, victim, oldStage, stage);
        RaiseLocalEvent(grabber, ref changed);
        RaiseLocalEvent(victim, ref changed);
    }

    private void UpdateVictimAlert(EntityUid victim, KsGrabStagePrototype? oldStage, KsGrabStagePrototype stage)
    {
        if (oldStage?.VictimAlert is { } oldAlert && oldAlert != stage.VictimAlert)
            _alertsSystem.ClearAlert(victim, oldAlert);

        if (!TryComp<PullableComponent>(victim, out var pullable))
            return;

        if (stage.VictimAlert is { } alert)
        {
            _alertsSystem.ClearAlert(victim, pullable.PulledAlert);
            _alertsSystem.ShowAlert(victim, alert);
        }
        else
        {
            _alertsSystem.ShowAlert(victim, pullable.PulledAlert);
        }
    }

    private bool IsHoldingInHand(EntityUid grabber, EntityUid victim)
    {
        foreach (var held in _handsSystem.EnumerateHeld(grabber))
        {
            if (TryComp<VirtualItemComponent>(held, out var virtualItem) && virtualItem.BlockingEntity == victim)
                return true;
        }

        return false;
    }

    #endregion

    #region Victim

    [SubscribeLocalEvent]
    private void OnUpdateCanMove(EntityUid victim, KsGrabbableComponent component, UpdateCanMoveEvent args)
    {
        if (GetHeldStage(victim) is { BlocksVictimMovement: true })
            args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnStandAttempt(EntityUid victim, KsGrabbableComponent component, StandAttemptEvent args)
    {
        if (TryGetGrabber(victim, out var grabber, out var stage) && (stage.FloorsVictim || grabber.Value.Comp.Pinned))
            args.Cancel();
    }

    /// <summary>
    ///     Shoving someone you hold tight, while they stand, pins them to the floor for as long as you hold them,
    ///         instead of shoving them away.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShoveAttempt(Entity<KsGrabbableComponent> victim, ref KsShoveAttemptEvent args)
    {
        if (args.Handled || !TryGetGrabber(victim, out var grabber, out var stage) || grabber.Value.Owner != args.Shover ||
            !stage.BlocksVictimMovement || grabber.Value.Comp.Pinned || _standingStateSystem.IsDown(victim.Owner))
        {
            return;
        }

        if (!_stunSystem.TryKnockdown(victim.Owner, grabber.Value.Comp.FloorKnockdownTime, drop: false))
            return;

        args.Handled = true;
        grabber.Value.Comp.Pinned = true;
        Dirty(grabber.Value);

        _audioSystem.PlayPredicted(stage.Sound, victim, args.Shover, AudioParams.Default.WithVariation(0.05f));
        PopupGrab(args.Shover, victim, user: args.Shover, PinPopups, PopupType.MediumCaution);
        _adminLogger.Add(LogType.DisarmedKnockdown, LogImpact.Medium,
            $"{ToPrettyString(args.Shover):user} pinned {ToPrettyString(victim):target} to the floor");
    }

    [SubscribeLocalEvent]
    private void OnResistAttempt(Entity<KsGrabbableComponent> victim, ref KsResistAttemptEvent args)
    {
        if (args.Handled || GetHeldStage(victim) == null)
            return;

        args.Handled = true;
        TryResist(victim);
    }

    [SubscribeLocalEvent]
    private void OnDowned(EntityUid victim, KsGrabbableComponent component, DownedEvent args)
    {
        RefreshGrabberSpeed(victim);
    }

    [SubscribeLocalEvent]
    private void OnStood(EntityUid victim, KsGrabbableComponent component, StoodEvent args)
    {
        RefreshGrabberSpeed(victim);
    }

    /// <summary>
    ///     A victim held tight can't just let go of the pull: clicking the pulled alert resists instead.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAttemptStopPulling(Entity<KsGrabbableComponent> victim, ref AttemptStopPullingEvent args)
    {
        if (args.Cancelled || args.User != victim.Owner || GetHeldStage(victim) is not { BlocksVictimMovement: true })
            return;

        args.Cancelled = true;
        TryResist(victim);
    }

    [SubscribeLocalEvent]
    private void OnMoveInput(Entity<KsGrabbableComponent> victim, ref MoveInputEvent args)
    {
        if (args.HasDirectionalMovement && GetHeldStage(victim) is { BlocksVictimMovement: true })
            TryResist(victim);
    }

    [SubscribeLocalEvent]
    private void OnResistAlert(EntityUid uid, KsGrabbableComponent component, KsResistGrabAlertEvent args)
    {
        Entity<KsGrabbableComponent> victim = (uid, component);
        if (args.Handled)
            return;

        args.Handled = true;
        TryResist(victim);
    }

    /// <summary>
    ///     The victim tries to break the grip. A grip that doesn't hold them in place always lets go.
    /// </summary>
    public bool TryResist(Entity<KsGrabbableComponent> victim)
    {
        if (!TryGetGrabber(victim, out var grabber, out var stage) || !TryComp<PullableComponent>(victim, out var pullable))
            return false;

        // Out cold or stunned, nobody struggles.
        if (_mobStateSystem.IsIncapacitated(victim) || HasComp<StunnedComponent>(victim))
            return false;

        if (!stage.BlocksVictimMovement)
            return _pullingSystem.TryStopPull(victim, pullable);

        if (_gameTiming.CurTime < victim.Comp.NextResist)
            return false;

        victim.Comp.NextResist = _gameTiming.CurTime + victim.Comp.ResistCooldown;
        Dirty(victim);

        var attempt = new KsGrabResistAttemptEvent(grabber.Value, victim, stage.EscapeChance * victim.Comp.EscapeChanceModifier);
        RaiseLocalEvent(victim, ref attempt);
        if (attempt.Cancelled)
            return false;

        // Seeded the same on the client and the server, so the predicted result is the real one.
        var random = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            KsSharedRandomExtensions.GetNetId(victim.Owner, EntityManager),
            KsSharedRandomExtensions.GetNetId(grabber.Value.Owner, EntityManager),
            (int)_gameTiming.CurTick.Value);

        if (random.Prob(Math.Clamp(attempt.Chance, 0f, 1f)))
        {
            PopupGrab(grabber.Value, victim, user: victim, ResistSuccessPopups, PopupType.MediumCaution);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(victim):user} broke free of {ToPrettyString(grabber):target}'s {stage.ID}");

            // No user: a victim held tight isn't allowed to let go themselves.
            _pullingSystem.TryStopPull(victim, pullable);
            return true;
        }

        var staminaDamage = random.NextFloat(stage.ResistFailStaminaDamageMin,
            Math.Max(stage.ResistFailStaminaDamageMin, stage.ResistFailStaminaDamageMax));
        if (staminaDamage > 0f)
            _staminaSystem.TakeStaminaDamage(victim, staminaDamage, source: grabber, visual: false);

        PopupGrab(grabber.Value, victim, user: victim, ResistFailPopups, PopupType.SmallCaution);
        return false;
    }

    private void RefreshGrabberSpeed(EntityUid victim)
    {
        if (TryComp<PullableComponent>(victim, out var pullable) && pullable.Puller is { } puller)
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(puller);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var isClient = _netManager.IsClient;
        var curTime = _gameTiming.CurTime;

        var query = EntityQueryEnumerator<KsGrabberComponent, PullerComponent>();
        while (query.MoveNext(out var uid, out var grabberComponent, out var puller))
        {
            if (puller.Pulling is not { } victim || !TryComp<KsGrabbableComponent>(victim, out var grabbable) ||
                GetStage((uid, grabberComponent)) is not { } stage)
            {
                continue;
            }

            if (stage.HoldDistance is { } holdDistance)
                ReelIn(victim, holdDistance, grabberComponent.ReelRate * frameTime);

            // The rest is the server's alone: predicting damage would only flicker the health bar, as with breathing.
            if (isClient)
                continue;

            // A move key only raises MoveInputEvent when it is pressed, so someone holding a direction would struggle
            // once. Keep struggling for them as often as the cooldown allows, as tgstation does.
            if (stage.BlocksVictimMovement && curTime >= grabbable.NextResist &&
                CompOrNull<InputMoverComponent>(victim)?.HasDirectionalMovement == true &&
                TryResist((victim, grabbable)))
            {
                continue;
            }

            DealStageDamage((uid, grabberComponent), victim, stage, curTime);
        }
    }

    /// <summary>
    ///     Deals the stage's <see cref="KsGrabStagePrototype.Damage"/> once its interval is up.
    /// </summary>
    private void DealStageDamage(Entity<KsGrabberComponent> grabber, EntityUid victim, KsGrabStagePrototype stage,
        TimeSpan curTime)
    {
        // Out of reach, the grip can't choke: nothing is dealt until they're close again.
        if (stage.Damage is not { } damage || curTime < grabber.Comp.NextDamage || !IsCloseEnough(grabber, victim, stage))
            return;

        grabber.Comp.NextDamage = curTime + stage.DamageInterval;
        Dirty(grabber);

        _damageableSystem.ChangeDamage(victim, damage, ignoreResistances: true, interruptsDoAfters: false, origin: grabber);
        if (stage.DamagePopup is { } popup && _robustRandom.Prob(stage.DamagePopupChance))
            _popupSystem.PopupEntity(KsPopupMessage.Create(popup), victim, victim, PopupType.MediumCaution);
    }

    /// <summary>
    ///     Shortens the pull's joint towards <paramref name="holdDistance"/> by <paramref name="reelDistance"/>,
    ///         drawing the victim in a little each tick rather than snapping them there. The pull joint is a hard limit
    ///         at its maximum length, so this is a reel.
    /// </summary>
    private void ReelIn(EntityUid victim, float holdDistance, float reelDistance)
    {
        if (!TryComp<PullableComponent>(victim, out var pullable) || pullable.PullJointId is not { } jointId ||
            !TryComp<JointComponent>(victim, out var joints) || !joints.GetJoints.TryGetValue(jointId, out var joint) ||
            joint is not DistanceJoint distance || distance.MaxLength <= holdDistance)
        {
            return;
        }

        distance.MaxLength = MathF.Max(holdDistance, distance.MaxLength - reelDistance);
        distance.Length = MathF.Min(distance.Length, distance.MaxLength);
        distance.MinLength = MathF.Min(distance.MinLength, distance.Length);

        // A shorter joint doesn't wake anything by itself, and a sleeping body ignores its joints.
        _physicsSystem.WakeBody(distance.BodyAUid);
        _physicsSystem.WakeBody(distance.BodyBUid);
    }

    #region Thrown victims

    /// <summary>
    ///     Marks someone sent flying by <paramref name="thrower"/>, so whatever they slam into knocks them over.
    /// </summary>
    public void MarkThrown(EntityUid victim, EntityUid thrower)
    {
        var thrown = EnsureComp<KsGrabThrownComponent>(victim);
        thrown.Thrower = thrower;
        Dirty(victim, thrown);
    }

    /// <summary>
    ///     A victim thrown into someone, or into a wall or table, is knocked down by it as a shove would knock them down,
    ///         and knocks over whoever they hit. How hard is the thrower's <see cref="KsGrabberComponent"/>'s to say.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThrownHit(Entity<KsGrabThrownComponent> thrown, ref ThrowDoHitEvent args)
    {
        var thrower = thrown.Comp.Thrower;
        if (args.Target == thrower || !TryComp<KsGrabberComponent>(thrower, out var grabber))
            return;

        // Caught by what they hit, as by a closet shutting on them: they're inside it, not slammed against it.
        if (_containerSystem.IsEntityInContainer(thrown))
        {
            RemCompDeferred<KsGrabThrownComponent>(thrown);
            return;
        }

        if (TryComp<KsShoveObstacleComponent>(args.Target, out var obstacle))
        {
            _complexShoveSystem.KnockIntoObstacle(thrower, thrown, (args.Target, obstacle), grabber.ThrowObstaclePopups);

            // Someone in the way is hit as hard, if they can be hurt; a table isn't.
            if (obstacle.CollateralKnockdownTime != null)
                DealImpactDamage(args.Target, grabber.ThrowCollateralDamage, thrower);
        }
        else if (CompOrNull<PhysicsComponent>(args.Target)?.BodyType == BodyType.Static)
        {
            _complexShoveSystem.KnockIntoWall(thrower, thrown, grabber.ThrowWallKnockdownTime, grabber.ThrowWallSound,
                grabber.ThrowWallPopups, grabber.ThrowWallResistedPopups);
        }
        else
        {
            return;
        }

        // Only the first thing they slam into counts.
        RemCompDeferred<KsGrabThrownComponent>(thrown);
        DealImpactDamage(thrown, grabber.ThrowImpactDamage, thrower);
    }

    /// <summary>
    ///     Damage from slamming into something, dealt by the server like the strangle's, so health doesn't flicker.
    /// </summary>
    private void DealImpactDamage(EntityUid uid, DamageSpecifier? damage, EntityUid origin)
    {
        if (_netManager.IsClient || damage == null || damage.Empty)
            return;

        _damageableSystem.ChangeDamage(uid, damage, origin: origin);
    }

    [SubscribeLocalEvent]
    private void OnThrownLand(Entity<KsGrabThrownComponent> thrown, ref LandEvent args)
    {
        RemCompDeferred<KsGrabThrownComponent>(thrown);
    }

    [SubscribeLocalEvent]
    private void OnThrownStop(Entity<KsGrabThrownComponent> thrown, ref StopThrowEvent args)
    {
        RemCompDeferred<KsGrabThrownComponent>(thrown);
    }

    #endregion

    #region Queries

    /// <summary>
    ///     The stage of the grip someone holds <paramref name="grabber"/>'s victim in, if they hold anyone.
    /// </summary>
    public KsGrabStagePrototype? GetStage(Entity<KsGrabberComponent> grabber)
    {
        if (grabber.Comp.Stage < 0 || grabber.Comp.Stage >= grabber.Comp.Stages.Count)
            return null;

        return ProtoMan.Index(grabber.Comp.Stages[grabber.Comp.Stage]);
    }

    /// <summary>
    ///     The stage of the grip <paramref name="victim"/> is held in, or null when nobody grabs them.
    /// </summary>
    public KsGrabStagePrototype? GetHeldStage(EntityUid victim)
    {
        return TryGetGrabber(victim, out _, out var stage) ? stage : null;
    }

    public bool TryGetGrabber(EntityUid victim, [NotNullWhen(true)] out Entity<KsGrabberComponent>? grabber,
        [NotNullWhen(true)] out KsGrabStagePrototype? stage)
    {
        grabber = null;
        stage = null;
        if (!TryComp<PullableComponent>(victim, out var pullable) || pullable.Puller is not { } puller ||
            !TryComp<KsGrabberComponent>(puller, out var grabberComp))
        {
            return false;
        }

        stage = GetStage((puller, grabberComp));
        if (stage == null)
            return false;

        grabber = (puller, grabberComp);
        return true;
    }

    private EntityUid? GetPulled(EntityUid grabber)
    {
        return CompOrNull<PullerComponent>(grabber)?.Pulling;
    }

    #endregion

    /// <summary>
    ///     Shows a grab's popups at the victim, each with <c>grabber</c> and <c>victim</c>.
    /// </summary>
    /// <param name="user">Whichever of the two acts: the grabber tightening, or the victim resisting.</param>
    private void PopupGrab(EntityUid grabber, EntityUid victim, EntityUid user, KsAudiencePopups popups, PopupType type)
    {
        _ksPopupSystem.PopupUserTargetOthers(victim, user, user == victim ? grabber : victim, popups, type,
            ("grabber", Identity.Entity(grabber, EntityManager)),
            ("victim", Identity.Entity(victim, EntityManager)));
    }

    /// <summary>
    ///     Tells only the grabber something about their grip on <paramref name="victim"/>.
    /// </summary>
    private void PopupGrabber(EntityUid grabber, EntityUid victim, LocId message)
    {
        _popupSystem.PopupEntity(KsPopupMessage.Create(message, ("victim", Identity.Entity(victim, EntityManager))),
            grabber, grabber);
    }
}
