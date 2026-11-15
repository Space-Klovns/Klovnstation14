using Content.Shared._KS14.Klovnmed;
using Content.Shared._KS14.Klovnmed.Dismemberment;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.ActionBlocker;
using Content.Shared.Body;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Alligators;

public sealed partial class KsAlligatorSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private BodyHierarchySystem _bodyHierarchySystem = default!;
    [Dependency] private DismembermentSystem _dismembermentSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private OrganSearchSystem _organSearchSystem = default!;
    [Dependency] private SharedBuckleSystem _buckleSystem = default!;
    [Dependency] private SharedGravitySystem _gravitySystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private StandingStateSystem _standingSystem = default!;

    private const float FirstBiteStamina = 5f;
    private const float KnockdownStamina = 8f;

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
            if (!HasComp<HumanoidProfileComponent>(victimUid) || HasComp<GodmodeComponent>(victimUid))
                continue;

            if (_standingSystem.IsDown(victimUid))
            {
                if (TryLatch(alligator, victimUid))
                    break;
                continue;
            }

            if (!TryComp<StaminaComponent>(victimUid, out var stamina))
                continue;

            var fatigue = _staminaSystem.GetStaminaDamage(victimUid, stamina);
            if (fatigue >= KnockdownStamina)
                _stunSystem.TryKnockdown(victimUid, alligator.Comp.BiteKnockdown);
            else
                _staminaSystem.TakeStaminaDamage(victimUid, fatigue < FirstBiteStamina ? FirstBiteStamina : KnockdownStamina - FirstBiteStamina,
                    component: stamina, source: alligator.Owner, visual: false);
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

        // CanBuckle raises StrapAttemptEvent, which permits only the victim selected by this bite.
        alligator.Comp.Victim = victimUid;
        if (!_buckleSystem.TryBuckle(victimUid, null, alligator.Owner, buckleComp: buckle, popup: false))
        {
            alligator.Comp.Victim = null;
            return false;
        }

        alligator.Comp.Leg = legUid;
        alligator.Comp.RollEndsAt = _gameTiming.CurTime + alligator.Comp.RollDuration;
        alligator.Comp.NextRollAt = alligator.Comp.RollEndsAt + alligator.Comp.RollRecovery;
        _actionBlockerSystem.UpdateCanMove(alligator.Owner);

        if (HasComp<InventoryComponent>(victimUid))
            _inventorySystem.TryUnequip(victimUid, "shoes", silent: true, force: true);

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-alligator-death-roll-start",
            ("alligator", alligator.Owner), ("victim", victimUid)), victimUid, PopupType.LargeCaution);
        return true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<KsAlligatorComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Victim is not { } victimUid)
                continue;

            var alligator = (uid, component);
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
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-alligator-death-roll-complete",
                ("alligator", uid), ("victim", victimUid)), victimUid, PopupType.LargeCaution);
        }
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

        alligator.Comp.Victim = null;
        alligator.Comp.Leg = null;

        if (!TerminatingOrDeleted(victimUid) && TryComp<BuckleComponent>(victimUid, out var buckle) &&
            buckle.BuckledTo == alligator.Owner)
        {
            _buckleSystem.Unbuckle((victimUid, buckle), null);
        }

        if (!TerminatingOrDeleted(alligator.Owner))
            _actionBlockerSystem.UpdateCanMove(alligator.Owner);
    }
}
