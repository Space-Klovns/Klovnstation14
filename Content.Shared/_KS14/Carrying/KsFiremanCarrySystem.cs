using System.Numerics;
using Content.Shared._KS14.Grab;
using Content.Shared._KS14.KsPopup;
using Content.Shared._KS14.Resist;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Bed.Sleep;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Strip;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Carrying;

/// <summary>
///     tgstation's fireman carry. Alt-click someone lying down, or drag them onto yourself, to lift them onto your
///         back; no grab is needed. They ride along until you put them down, fall over yourself, or they wriggle free.
/// </summary>
public sealed partial class KsFiremanCarrySystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private KsGrabSystem _grabSystem = default!;
    [Dependency] private KsPopupSystem _ksPopupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedVirtualItemSystem _virtualItemSystem = default!;
    [Dependency] private StandingStateSystem _standingStateSystem = default!;

    /// <summary>
    ///     Priority of the carry among alt-click verbs: above the rest, so alt-clicking someone you can carry lifts them.
    /// </summary>
    private const int CarryVerbPriority = 10;

    private static readonly KsAudiencePopups StartPopups = new("ks-carry-start-user", "ks-carry-start-target", "ks-carry-start-others");
    private static readonly KsAudiencePopups FailPopups = new("ks-carry-fail-user", "ks-carry-fail-target", "ks-carry-fail-others");
    private static readonly KsAudiencePopups DonePopups = new("ks-carry-done-user", "ks-carry-done-target", "ks-carry-done-others");
    private static readonly KsAudiencePopups DropPopups = new("ks-carry-drop-user", "ks-carry-drop-target", "ks-carry-drop-others");
    private static readonly KsAudiencePopups EscapePopups = new("ks-carry-escape-user", "ks-carry-escape-target", "ks-carry-escape-others");
    private static readonly KsAudiencePopups ThrowPopups = new("ks-carry-throw-user", "ks-carry-throw-target", "ks-carry-throw-others");

    public override void Initialize()
    {
        base.Initialize();

        // Dragging someone onto yourself opens their pockets otherwise; a carry, when possible, comes first.
        SubscribeLocalEvent<KsGrabbableComponent, CanDropDraggedEvent>(OnCanDropDragged, before: [typeof(SharedStrippableSystem)]);
        SubscribeLocalEvent<KsGrabbableComponent, DragDropDraggedEvent>(OnDragDropDragged, before: [typeof(SharedStrippableSystem)]);
    }

    #region Lifting

    private void OnCanDropDragged(Entity<KsGrabbableComponent> victim, ref CanDropDraggedEvent args)
    {
        if (args.Target != args.User || !CanCarry(args.User, victim, popup: false))
            return;

        args.CanDrop = true;
        args.Handled = true;
    }

    private void OnDragDropDragged(Entity<KsGrabbableComponent> victim, ref DragDropDraggedEvent args)
    {
        if (args.Handled || args.Target != args.User || !CanCarry(args.User, victim, popup: false))
            return;

        args.Handled = true;
        TryStartCarry(args.User, victim);
    }

    /// <summary>
    ///     Alt-clicking someone you can carry lifts them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGetAlternativeVerbs(EntityUid uid, KsGrabbableComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !CanCarry(args.User, uid, popup: false))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ks-carry-verb"),
            Act = () => TryStartCarry(user, uid),
            Priority = CarryVerbPriority,
        });
    }

    /// <summary>
    ///     Starts lifting <paramref name="victim"/> onto <paramref name="carrier"/>'s back.
    /// </summary>
    public bool TryStartCarry(EntityUid carrier, EntityUid victim)
    {
        if (!TryComp<KsFiremanCarrierComponent>(carrier, out var carrierComp) || !CanCarry(carrier, victim, popup: true))
            return false;

        var doAfter = new DoAfterArgs(EntityManager, carrier, carrierComp.CarryTime, new KsFiremanCarryDoAfterEvent(),
            carrier, target: victim)
        {
            BreakOnMove = true,
            BlockDuplicate = true,
            NeedHand = false,
            // The lift stops the moment it stops being possible: they got up, a hand filled, one of them was put away.
            ExtraCheck = () => CanCarry(carrier, victim, popup: false),
            AttemptFrequency = AttemptFrequency.EveryTick,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfter))
            return false;

        PopupCarry(carrier, victim, user: carrier, StartPopups);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnCarryDoAfter(EntityUid uid, KsFiremanCarrierComponent component, KsFiremanCarryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } victim)
            return;

        args.Handled = true;
        if (!CanCarry(uid, victim, popup: true))
        {
            PopupCarry(uid, victim, user: uid, FailPopups);
            return;
        }

        StartCarrying((uid, component), victim);
    }

    /// <summary>
    ///     Whether <paramref name="carrier"/> can lift <paramref name="victim"/> right now. No grip is needed: anyone
    ///         lying down within reach can be lifted, held or not.
    /// </summary>
    public bool CanCarry(EntityUid carrier, EntityUid victim, bool popup)
    {
        if (carrier == victim || !TryComp<KsFiremanCarrierComponent>(carrier, out var carrierComponent) ||
            HasComp<KsCarryingComponent>(carrier) || HasComp<KsBeingCarriedComponent>(carrier) ||
            HasComp<KsBeingCarriedComponent>(victim))
        {
            return false;
        }

        if (!_standingStateSystem.IsDown(victim) || _standingStateSystem.IsDown(carrier))
        {
            if (popup)
            {
                _popupSystem.PopupEntity(
                    KsPopupMessage.Create("ks-carry-need-prone", ("carried", Identity.Entity(victim, EntityManager))), carrier, carrier);
            }

            return false;
        }

        if (_containerSystem.IsEntityInContainer(carrier) || _containerSystem.IsEntityInContainer(victim))
            return false;

        // A carry takes hands; one holding the grab is freed by it.
        var freeHands = 0;
        foreach (var hand in _handsSystem.EnumerateHands(carrier))
        {
            if (!_handsSystem.TryGetHeldItem(carrier, hand, out var held) ||
                CompOrNull<VirtualItemComponent>(held)?.BlockingEntity == victim)
            {
                freeHands++;
            }
        }

        if (freeHands < carrierComponent.HandsNeeded)
        {
            if (popup)
            {
                _popupSystem.PopupEntity(
                    KsPopupMessage.Create("ks-carry-need-hands", ("carried", Identity.Entity(victim, EntityManager))), carrier, carrier);
            }

            return false;
        }

        return true;
    }

    private void StartCarrying(Entity<KsFiremanCarrierComponent> carrier, EntityUid victim)
    {
        // The carry replaces the grab, and its hand.
        if (TryComp<PullableComponent>(victim, out var pullable))
            _pullingSystem.TryStopPull(victim, pullable);

        var carrying = EnsureComp<KsCarryingComponent>(carrier);
        carrying.Carried = victim;
        Dirty(carrier, carrying);

        var carried = EnsureComp<KsBeingCarriedComponent>(victim);
        carried.Carrier = carrier;
        Dirty(victim, carried);

        for (var i = 0; i < carrier.Comp.HandsNeeded; i++)
        {
            _virtualItemSystem.TrySpawnVirtualItemInHand(victim, carrier, silent: true);
        }

        _physicsSystem.SetCanCollide(victim, false);
        _transformSystem.SetCoordinates(victim, new EntityCoordinates(carrier, Vector2.Zero));

        _actionBlockerSystem.UpdateCanMove(victim);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(carrier);
        _alertsSystem.ShowAlert(carrier.Owner, carrier.Comp.CarryingAlert);
        _alertsSystem.ShowAlert(victim, carrier.Comp.CarriedAlert);

        PopupCarry(carrier, victim, user: carrier, DonePopups);

        var started = new KsCarryStartedEvent(carrier);
        RaiseLocalEvent(victim, ref started);
        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(carrier):user} started carrying {ToPrettyString(victim):target}");
    }

    #endregion

    #region Putting down

    /// <summary>
    ///     Puts <paramref name="carried"/> down next to whoever carries them.
    /// </summary>
    public void StopCarrying(EntityUid carried)
    {
        // Everything else happens as the component shuts down, so deleting either of them cleans up too.
        if (TryComp<KsBeingCarriedComponent>(carried, out var beingCarried) && beingCarried.LifeStage <= ComponentLifeStage.Running)
            RemComp(carried, beingCarried);
    }

    [SubscribeLocalEvent]
    private void OnCarriedShutdown(EntityUid uid, KsBeingCarriedComponent component, ComponentShutdown args)
    {
        // Visuals listen for this rather than the shutdown, which only one system may subscribe to.
        var ended = new KsCarryEndedEvent(component.Carrier);
        RaiseLocalEvent(uid, ref ended);

        // A removal arriving from the server brings the rest of the cleanup with it.
        if (_gameTiming.ApplyingState)
            return;

        var carrier = component.Carrier;
        if (TryComp<KsCarryingComponent>(carrier, out var carrying) && carrying.Carried == uid &&
            carrying.LifeStage <= ComponentLifeStage.Running)
        {
            RemComp(carrier, carrying);
        }

        var carrierComponent = CompOrNull<KsFiremanCarrierComponent>(carrier);
        var carriedAlive = !TerminatingOrDeleted(uid);
        var carrierAlive = !TerminatingOrDeleted(carrier);

        if (carriedAlive)
        {
            if (Transform(uid).ParentUid == carrier)
            {
                // A carrier being deleted can't be stood next to, but mustn't take its child along either.
                if (carrierAlive)
                    _transformSystem.PlaceNextTo(uid, carrier);
                else
                    _transformSystem.AttachToGridOrMap(uid);
            }

            _physicsSystem.SetCanCollide(uid, true);
            _actionBlockerSystem.UpdateCanMove(uid);
            if (carrierComponent != null)
                _alertsSystem.ClearAlert(uid, carrierComponent.CarriedAlert);
        }

        if (!carrierAlive)
            return;

        _virtualItemSystem.DeleteInHandsMatching(carrier, uid);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(carrier);
        if (carrierComponent != null)
            _alertsSystem.ClearAlert(carrier, carrierComponent.CarryingAlert);

        if (carriedAlive)
        {
            PopupCarry(carrier, uid, user: carrier, DropPopups);
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(carrier):user} stopped carrying {ToPrettyString(uid):target}");
        }
    }

    [SubscribeLocalEvent]
    private void OnCarrierVirtualItemDeleted(EntityUid uid, KsCarryingComponent component, VirtualItemDeletedEvent args)
    {
        // Not while a state is applied: that takes the client's predicted hand item away only to put the server's
        // in its place, and nobody has let go.
        if (args.BlockingEntity == component.Carried && !_gameTiming.ApplyingState)
            StopCarrying(component.Carried);
    }

    [SubscribeLocalEvent]
    private void OnStopCarryingAlert(EntityUid uid, KsCarryingComponent component, KsStopCarryingAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        StopCarrying(component.Carried);
    }

    [SubscribeLocalEvent]
    private void OnCarrierDowned(EntityUid uid, KsCarryingComponent component, DownedEvent args)
    {
        StopCarrying(component.Carried);
    }

    [SubscribeLocalEvent]
    private void OnCarrierInserted(EntityUid uid, KsCarryingComponent component, EntGotInsertedIntoContainerMessage args)
    {
        StopCarrying(component.Carried);
    }

    /// <summary>
    ///     Someone carried is the carrier's transform child, and children are deleted with their parent: put them down
    ///         before that, so a gibbed carrier doesn't take them along.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCarrierTerminating(Entity<KsCarryingComponent> carrier, ref EntityTerminatingEvent args)
    {
        StopCarrying(carrier.Comp.Carried);
    }

    [SubscribeLocalEvent]
    private void OnCarrierShutdown(EntityUid uid, KsCarryingComponent component, ComponentShutdown args)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (CompOrNull<KsBeingCarriedComponent>(component.Carried)?.Carrier == uid)
            StopCarrying(component.Carried);
    }

    [SubscribeLocalEvent]
    private void OnCarriedInserted(EntityUid uid, KsBeingCarriedComponent component, EntGotInsertedIntoContainerMessage args)
    {
        StopCarrying(uid);
    }

    #endregion

    #region Being carried

    [SubscribeLocalEvent]
    private void OnCarriedUpdateCanMove(EntityUid uid, KsBeingCarriedComponent component, UpdateCanMoveEvent args)
    {
        args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnCarriedStandAttempt(EntityUid uid, KsBeingCarriedComponent component, StandAttemptEvent args)
    {
        args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnCarriedMoveInput(Entity<KsBeingCarriedComponent> carried, ref MoveInputEvent args)
    {
        if (args.HasDirectionalMovement)
            TryEscape(carried);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_netManager.IsClient)
            return;

        // Holding a direction keeps wriggling, and starts again if the attempt was interrupted.
        var query = EntityQueryEnumerator<KsBeingCarriedComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out var carried, out var mover))
        {
            if (mover.HasDirectionalMovement && !HasComp<ActiveDoAfterComponent>(uid))
                TryEscape((uid, carried));
        }
    }

    [SubscribeLocalEvent]
    private void OnEscapeAlert(EntityUid uid, KsBeingCarriedComponent component, KsEscapeCarryAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryEscape((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnEscapeDoAfter(EntityUid uid, KsBeingCarriedComponent component, KsEscapeCarryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (CanWriggle(uid))
            StopCarrying(uid);
    }

    /// <summary>
    ///     Whether someone carried is awake and able to wriggle. Someone in crit, asleep or stunned just hangs there.
    /// </summary>
    public bool CanWriggle(EntityUid carried)
    {
        return !_mobStateSystem.IsIncapacitated(carried) && !HasComp<StunnedComponent>(carried) &&
               !HasComp<SleepingComponent>(carried);
    }

    /// <summary>
    ///     Starts wriggling off the carrier's back.
    /// </summary>
    public void TryEscape(Entity<KsBeingCarriedComponent> carried)
    {
        if (!TryComp<KsFiremanCarrierComponent>(carried.Comp.Carrier, out var carrier))
        {
            StopCarrying(carried);
            return;
        }

        if (!CanWriggle(carried))
            return;

        var doAfter = new DoAfterArgs(EntityManager, carried, carrier.EscapeTime, new KsEscapeCarryDoAfterEvent(), carried,
            target: carried.Comp.Carrier)
        {
            BlockDuplicate = true,
            NeedHand = false,
            // Can't interact while knocked down and carried, so this checks the state that matters instead.
            RequireCanInteract = false,
            ExtraCheck = () => CanWriggle(carried),
            AttemptFrequency = AttemptFrequency.EveryTick,
            // They ride on the carrier, so they move with them: only the carrier's own movement counts.
            BreakOnMove = false,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfter))
            PopupCarry(carried.Comp.Carrier, carried, user: carried, EscapePopups);
    }

    /// <summary>
    ///     Throwing a hand that carries someone throws them off your back.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBeforeThrow(Entity<KsCarryingComponent> carrier, ref BeforeThrowEvent args)
    {
        if (args.Cancelled || CompOrNull<VirtualItemComponent>(args.ItemUid)?.BlockingEntity != carrier.Comp.Carried)
            return;

        // Dropping the hand afterwards puts them down, collision and all, just before they fly.
        var carried = carrier.Comp.Carried;
        args.ItemUid = carried;
        _grabSystem.ThrowBody(carried, carrier);

        PopupCarry(carrier, carried, user: carrier, ThrowPopups);
        _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(carrier):user} threw {ToPrettyString(carried):target} off their back");
    }

    [SubscribeLocalEvent]
    private void OnResistAttempt(Entity<KsBeingCarriedComponent> carried, ref KsResistAttemptEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryEscape(carried);
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(EntityUid uid, KsCarryingComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (TryComp<KsFiremanCarrierComponent>(uid, out var carrier))
            args.ModifySpeed(carrier.SpeedModifier, carrier.SpeedModifier);
    }

    #endregion

    /// <summary>
    ///     Shows a carry's popups at the carried, each with <c>carrier</c> and <c>carried</c>.
    /// </summary>
    /// <param name="user">Whichever of the two acts: the carrier lifting, or the carried wriggling.</param>
    private void PopupCarry(EntityUid carrier, EntityUid carried, EntityUid user, KsAudiencePopups popups)
    {
        _ksPopupSystem.PopupUserTargetOthers(carried, user, user == carried ? carrier : carried, popups, PopupType.Small,
            ("carrier", Identity.Entity(carrier, EntityManager)),
            ("carried", Identity.Entity(carried, EntityManager)));
    }
}
