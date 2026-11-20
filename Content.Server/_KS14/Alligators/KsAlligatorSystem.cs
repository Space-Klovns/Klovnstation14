using Content.Shared._KS14.Alligators;
using Content.Shared._KS14.Klovnmed;
using Content.Shared._KS14.Klovnmed.Dismemberment;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Body;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Destructible;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Alligators;

/// <summary>
///     A bite against a prone humanoid can buckle them to the alligator for a death roll.
///     The roll severs the grabbed leg only if the alligator holds on for the full duration.
///     The alligator swallows the leg, and gives it back when butchered or otherwise destroyed.
/// </summary>
public sealed partial class KsAlligatorSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private BodyHierarchySystem _bodyHierarchySystem = default!;
    [Dependency] private DismembermentSystem _dismembermentSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private OrganSearchSystem _organSearchSystem = default!;
    [Dependency] private SharedBuckleSystem _buckleSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedGravitySystem _gravitySystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingSystem = default!;

    /// <summary>
    ///     Holds the legs the alligator has swallowed.
    /// </summary>
    private const string StomachContainerId = "ks_alligator_stomach";

    private static readonly LocId DeathRollStartMessage = "ks-alligator-death-roll-start";
    private static readonly LocId DeathRollCompleteMessage = "ks-alligator-death-roll-complete";

    [SubscribeLocalEvent]
    private void OnMeleeHit(Entity<KsAlligatorComponent> alligator, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.Handled || args.User != alligator.Owner ||
            alligator.Comp.Victim != null || !_mobStateSystem.IsAlive(alligator.Owner))
        {
            return;
        }

        foreach (var victimUid in args.HitEntities)
        {
            if (HasComp<HumanoidProfileComponent>(victimUid) && !HasComp<GodmodeComponent>(victimUid) &&
                _standingSystem.IsDown(victimUid) && TryLatch(alligator, victimUid))
            {
                break;
            }
        }
    }

    private bool TryLatch(Entity<KsAlligatorComponent> alligator, EntityUid victimUid)
    {
        if (_gameTiming.CurTime < alligator.Comp.NextRollAt || _gravitySystem.IsWeightless(alligator.Owner) ||
            !TryComp<BuckleComponent>(victimUid, out var buckle) || buckle.Buckled ||
            !TryComp<BodyComponent>(victimUid, out var body) ||
            !_organSearchSystem.TryGetRandomBodyPartOfType((victimUid, body), BodyPartType.Leg, out var legUid))
        {
            return false;
        }

        // The victim is buckled in front of the jaws, so turn those towards the victim first.
        var previousRotation = _transformSystem.GetWorldRotation(alligator.Owner);
        var toVictim = _transformSystem.GetWorldPosition(victimUid) - _transformSystem.GetWorldPosition(alligator.Owner);
        if (toVictim.LengthSquared() > 0.0001f)
            _transformSystem.SetWorldRotation(alligator.Owner, toVictim.ToWorldAngle());

        // The strap checks the target during TryBuckle, so record it before the attempt.
        alligator.Comp.Victim = victimUid;
        if (!_buckleSystem.TryBuckle(victimUid, null, alligator.Owner, buckleComp: buckle, popup: false))
        {
            alligator.Comp.Victim = null;
            _transformSystem.SetWorldRotation(alligator.Owner, previousRotation);
            return false;
        }

        // Nothing to click: the victim cannot unbuckle themselves from the jaws.
        if (TryComp<StrapComponent>(alligator.Owner, out var strap))
            _alertsSystem.ClearAlert(victimUid, strap.BuckledAlertType);

        var deathRollVictim = EnsureComp<KsDeathRollVictimComponent>(victimUid);
        deathRollVictim.Alligator = alligator.Owner;
        deathRollVictim.StartTime = _gameTiming.CurTime;
        Dirty(victimUid, deathRollVictim);

        alligator.Comp.Leg = legUid;
        alligator.Comp.RollEndsAt = _gameTiming.CurTime + alligator.Comp.RollDuration;
        alligator.Comp.NextRollAt = alligator.Comp.RollEndsAt + alligator.Comp.RollRecovery;
        _actionBlockerSystem.UpdateCanMove(alligator.Owner);

        if (HasComp<InventoryComponent>(victimUid))
            _inventorySystem.TryUnequip(victimUid, "shoes", silent: true, force: true);

        Announce(DeathRollStartMessage, alligator.Owner, victimUid);
        return true;
    }

    /// <summary>
    ///     Shows a message about the alligator and its victim above the victim, to everyone who can see them.
    /// </summary>
    private void Announce(LocId message, EntityUid alligatorUid, EntityUid victimUid)
    {
        _popupSystem.PopupEntity(KsPopupMessage.Create(message, ("alligator", alligatorUid), ("victim", victimUid)),
            victimUid, PopupType.LargeCaution);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<KsAlligatorComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Victim is not { } victimUid)
                continue;

            var alligator = (uid, component);
            // The victim can be freed or lose the selected leg before the timer expires.
            if (!_mobStateSystem.IsAlive(uid) ||
                !TryComp<BuckleComponent>(victimUid, out var buckle) || buckle.BuckledTo != uid ||
                _gravitySystem.IsWeightless(uid) ||
                component.Leg is not { } legUid || TerminatingOrDeleted(legUid) ||
                !_bodyHierarchySystem.TryGetBody(legUid, out var body) || body.Value.Owner != victimUid)
            {
                ReleaseVictim(alligator);
                continue;
            }

            if (_gameTiming.CurTime < component.RollEndsAt)
                continue;

            ReleaseVictim(alligator);
            _dismembermentSystem.DismemberPart(victimUid, legUid, cause: uid);
            _dismembermentSystem.DoFixedEmote(victimUid);
            _containerSystem.Insert(legUid, _containerSystem.EnsureContainer<Container>(uid, StomachContainerId));
            Announce(DeathRollCompleteMessage, uid, victimUid);
        }
    }

    // Raised for butchering as well as gibbing, before the alligator and everything inside it is deleted.
    [SubscribeLocalEvent]
    private void OnDestruction(Entity<KsAlligatorComponent> alligator, ref DestructionEventArgs args)
    {
        if (_containerSystem.TryGetContainer(alligator.Owner, StomachContainerId, out var stomach))
            _containerSystem.EmptyContainer(stomach, force: true);
    }

    [SubscribeLocalEvent]
    private void OnStrapAttempt(Entity<KsAlligatorComponent> alligator, ref StrapAttemptEvent args)
    {
        if (args.User != null || args.Buckle.Owner != alligator.Comp.Victim)
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnUnstrapAttempt(Entity<KsAlligatorComponent> alligator, ref UnstrapAttemptEvent args)
    {
        // A bystander can pull the victim free using the normal unbuckle interaction.
        if (args.User != alligator.Comp.Victim || args.Buckle.Owner != alligator.Comp.Victim)
            return;

        args.Cancelled = true;
        if (args.Popup)
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-alligator-death-roll-held"), args.Buckle.Owner, args.Buckle.Owner);
    }

    [SubscribeLocalEvent]
    private void OnUnstrapped(Entity<KsAlligatorComponent> alligator, ref UnstrappedEvent args)
    {
        if (args.Buckle.Owner == alligator.Comp.Victim)
            ReleaseVictim(alligator);
    }

    [SubscribeLocalEvent]
    private void OnMoveAttempt(Entity<KsAlligatorComponent> alligator, ref UpdateCanMoveEvent args)
    {
        if (alligator.Comp.Victim != null)
            args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnAttackAttempt(Entity<KsAlligatorComponent> alligator, ref AttackAttemptEvent args)
    {
        if (alligator.Comp.Victim != null)
            args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<KsAlligatorComponent> alligator, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            ReleaseVictim(alligator);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsAlligatorComponent> alligator, ref ComponentShutdown args)
    {
        ReleaseVictim(alligator);
    }

    private void ReleaseVictim(Entity<KsAlligatorComponent> alligator)
    {
        if (alligator.Comp.Victim is not { } victimUid)
            return;

        // Clear the latch before unbuckling, which can raise UnstrappedEvent here again.
        alligator.Comp.Victim = null;
        alligator.Comp.Leg = null;
        RemCompDeferred<KsDeathRollVictimComponent>(victimUid);

        if (!TerminatingOrDeleted(victimUid) && TryComp<BuckleComponent>(victimUid, out var buckle) &&
            buckle.BuckledTo == alligator.Owner)
        {
            _buckleSystem.Unbuckle((victimUid, buckle), null);
        }

        if (!TerminatingOrDeleted(alligator.Owner))
            _actionBlockerSystem.UpdateCanMove(alligator.Owner);
    }
}
