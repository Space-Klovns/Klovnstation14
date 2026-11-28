using Content.Shared._KS14.PopupLocalization;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;

namespace Content.Shared._KS14.WallHealer;

public sealed partial class KsWallHealerSystem
{
    [SubscribeLocalEvent]
    private void OnInteractHand(Entity<KsWallHealerComponent> healer, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        TryStartInsert(healer, args.User, args.User);
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnCanDropTarget(Entity<KsWallHealerComponent> healer, ref CanDropTargetEvent args)
    {
        args.CanDrop = CanBePatient(args.Dragged);
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnDragDropTarget(Entity<KsWallHealerComponent> healer, ref DragDropTargetEvent args)
    {
        if (args.Handled)
            return;

        TryStartInsert(healer, args.User, args.Dragged);
        args.Handled = true;
    }

    /// <summary>
    ///     Starts <paramref name="user"/> putting <paramref name="patient"/>'s hand into the station, or takes it out if
    ///         it is already in.
    /// </summary>
    public bool TryStartInsert(Entity<KsWallHealerComponent> healer, EntityUid user, EntityUid patient)
    {
        var self = user == patient;

        if (healer.Comp.Patient == patient)
        {
            if (!self)
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-remove-other", ("patient", patient), ("healer", healer.Owner)), healer, user);

            StopTreatment(healer);
            return true;
        }

        if (healer.Comp.Patient is { } currentPatient && !TerminatingOrDeleted(currentPatient))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-busy",
                    ("self", self),
                    ("patient", patient),
                    ("current", currentPatient),
                    ("healer", healer.Owner)),
                healer, user);
            return false;
        }

        if (!CanBePatient(patient))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-incompatible"), healer, user);
            return false;
        }

        if (!self && !_interactionSystem.InRangeUnobstructed(patient, healer.Owner))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-too-far", ("patient", patient)), healer, user);
            return false;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager,
            user,
            self ? healer.Comp.SelfInsertDelay : healer.Comp.OtherInsertDelay,
            new KsWallHealerInsertHandDoAfterEvent(),
            healer,
            target: patient)
        {
            BreakOnMove = true,
            NeedHand = self,
        };

        return _doAfterSystem.TryStartDoAfter(doAfterArgs);
    }

    [SubscribeLocalEvent]
    private void OnInsertHandDoAfter(Entity<KsWallHealerComponent> healer, ref KsWallHealerInsertHandDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } patient)
            return;

        args.Handled = true;

        // The treatment do-after is the patient's. Starting one on an entity with none running, from inside the
        // do-after system's own update, changes the set it is walking, so someone else's patient waits for our update.
        if (patient == args.User)
            BeginTreatment(healer, patient, args.User);
        else if (_netManager.IsServer)
            _pendingInserts.Add((healer, patient, args.User));
    }

    /// <summary>
    ///     Puts <paramref name="patient"/>'s hand in, and keeps it there with a repeating do-after for as long as they
    ///         stay put.
    /// </summary>
    private void BeginTreatment(Entity<KsWallHealerComponent> healer, EntityUid patient, EntityUid inserter)
    {
        if (healer.Comp.Patient != null || !CanBePatient(patient))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager,
            patient,
            healer.Comp.TreatmentInterval,
            new KsWallHealerTreatmentDoAfterEvent(),
            healer,
            target: healer)
        {
            BreakOnMove = true,
            NeedHand = true,
            BreakOnHandChange = true,
            BreakOnDropItem = false,
            // A patient who is down, stunned or asleep can still be treated; that's who gets dragged over.
            RequireCanInteract = false,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs, out var doAfterId))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-insert-failed", ("patient", patient), ("healer", healer.Owner)), healer, inserter);
            return;
        }

        // A different patient doesn't wait out the last one's interval.
        if (healer.Comp.LastPatient != patient)
            healer.Comp.NextTreatment = _gameTiming.CurTime;

        healer.Comp.Patient = patient;
        healer.Comp.TreatmentDoAfter = doAfterId.Value.Index;
        healer.Comp.LastPatient = null;
        healer.Comp.TreatmentCount = 0;
        Dirty(healer);

        _forensicsSystem.TransferDna(healer, inserter);

        var powered = _powerReceiverSystem.IsPowered(healer.Owner);
        if (inserter == patient)
        {
            _popupSystem.PopupEntity(
                KsPopupMessage.Create("ks-wall-healer-insert-self", ("healer", healer.Owner), ("powered", powered)),
                KsPopupMessage.Create("ks-wall-healer-insert-self-others", ("patient", patient), ("healer", healer.Owner), ("powered", powered)),
                healer,
                patient);
        }
        else
        {
            _popupSystem.PopupEntity(
                KsPopupMessage.Create("ks-wall-healer-insert-other", ("patient", patient), ("healer", healer.Owner), ("powered", powered)),
                KsPopupMessage.Create("ks-wall-healer-insert-other-others", ("user", inserter), ("patient", patient), ("healer", healer.Owner), ("powered", powered)),
                healer,
                inserter);
        }

        if (_gameTiming.CurTime >= healer.Comp.NextTreatment)
            Treat(healer, patient);
    }

    [SubscribeLocalEvent]
    private void OnTreatmentDoAfter(Entity<KsWallHealerComponent> healer, ref KsWallHealerTreatmentDoAfterEvent args)
    {
        // An old do-after, from before the hand came out.
        if (healer.Comp.Patient != args.User || healer.Comp.TreatmentDoAfter != args.DoAfter.Index)
            return;

        if (args.Cancelled)
        {
            if (!TerminatingOrDeleted(args.User))
            {
                _popupSystem.PopupEntity(
                    KsPopupMessage.Create("ks-wall-healer-remove-self", ("healer", healer.Owner)),
                    KsPopupMessage.Create("ks-wall-healer-remove-self-others", ("patient", args.User), ("healer", healer.Owner)),
                    healer,
                    args.User);
            }

            EndTreatment(healer);
            return;
        }

        Treat(healer, args.User);
        args.Repeat = true;
    }

    /// <summary>
    ///     Takes the patient's hand out, through their do-after's cancellation.
    /// </summary>
    public void StopTreatment(Entity<KsWallHealerComponent> healer)
    {
        if (healer.Comp.Patient is not { } patient)
            return;

        if (healer.Comp.TreatmentDoAfter is { } doAfterIndex)
            _doAfterSystem.Cancel(patient, doAfterIndex);

        // Cancelling normally ends the treatment, but not if the do-after was already gone.
        if (healer.Comp.Patient != null)
            EndTreatment(healer);
    }

    private void EndTreatment(Entity<KsWallHealerComponent> healer)
    {
        healer.Comp.LastPatient = healer.Comp.Patient;
        healer.Comp.Patient = null;
        healer.Comp.TreatmentDoAfter = null;

        // It only recharges after a full interval with nobody using it.
        var earliestRecharge = _gameTiming.CurTime + healer.Comp.RechargeInterval;
        if (healer.Comp.NextRecharge < earliestRecharge)
            healer.Comp.NextRecharge = earliestRecharge;

        Dirty(healer);
    }

    private bool IsTreating(Entity<KsWallHealerComponent> healer, EntityUid patient)
    {
        return !TerminatingOrDeleted(patient)
            && healer.Comp.TreatmentDoAfter is { } doAfterIndex
            && _doAfterSystem.IsRunning(patient, doAfterIndex);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> has a hand to put in and can be hurt, so is worth putting in at all. Whether
    ///         the station can actually treat them is decided when it tries.
    /// </summary>
    public bool CanBePatient(EntityUid uid)
    {
        return _handsQuery.HasComp(uid) && _damageableQuery.HasComp(uid);
    }
}
