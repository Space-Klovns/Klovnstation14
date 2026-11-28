using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Access.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Localizations;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.WallHealer;

/// <summary>
///     Runs <see cref="KsWallHealerComponent"/>: putting hands in and taking them out, treating patients from the pools,
///         recharging them, and handing out and restocking supplies. Everything a player does is predicted; only the
///         recharge, which nobody causes, is left to the server.
/// </summary>
public sealed partial class KsWallHealerSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private EmagSystem _emagSystem = default!;
    [Dependency] private SharedForensicsSystem _forensicsSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private SharedStackSystem _stackSystem = default!;

    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<HandsComponent> _handsQuery = default!;
    [Dependency] private EntityQuery<InjurableComponent> _injurableQuery = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;

    #region Lifecycle

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsWallHealerComponent> healer, ref MapInitEvent args)
    {
        _containerSystem.EnsureContainer<Container>(healer, healer.Comp.SupplyContainerId);

        // A saved station keeps its amounts, unless its pools have changed since.
        if (healer.Comp.PoolAmounts.Count != healer.Comp.Pools.Count)
        {
            healer.Comp.PoolAmounts.Clear();
            foreach (var pool in healer.Comp.Pools)
                healer.Comp.PoolAmounts.Add(pool.Capacity * healer.Comp.InitialFill);
        }

        healer.Comp.NextRecharge = _gameTiming.CurTime;
        Dirty(healer);
        RaisePoolsChanged(healer);
    }

    [SubscribeLocalEvent]
    private void OnPowerChanged(Entity<KsWallHealerComponent> healer, ref PowerChangedEvent args)
    {
        // Losing power puts the recharge off until a while after it comes back.
        if (!args.Powered)
            return;

        var earliestRecharge = _gameTiming.CurTime + healer.Comp.RechargeInterval / 2;
        if (healer.Comp.NextRecharge >= earliestRecharge)
            return;

        healer.Comp.NextRecharge = earliestRecharge;
        Dirty(healer);
    }

    [SubscribeLocalEvent]
    private void OnGotEmagged(Entity<KsWallHealerComponent> healer, ref GotEmaggedEvent args)
    {
        if (!_emagSystem.CompareFlag(args.Type, EmagType.Interaction)
            || _emagSystem.CheckFlag(healer, EmagType.Interaction))
            return;

        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Recharging is nobody's doing, so it is not predicted.
        if (_netManager.IsClient)
            return;

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsWallHealerComponent>();
        while (query.MoveNext(out var uid, out var healerComponent))
        {
            var healer = new Entity<KsWallHealerComponent>(uid, healerComponent);

            if (healerComponent.Patient is { } patient)
            {
                // A patient deleted, or a do-after gone, without its cancellation reaching us.
                if (!IsTreating(healer, patient))
                    EndTreatment(healer);

                continue;
            }

            if (curTime < healerComponent.NextRecharge || !_powerReceiverSystem.IsPowered(uid))
                continue;

            if (Refill(healer, healerComponent.RechargeFraction) <= 0f)
                continue;

            healerComponent.NextRecharge = curTime + healerComponent.RechargeInterval;
            Dirty(healer);
            _audioSystem.PlayPvs(healerComponent.RechargedSound, uid);
        }
    }

    #endregion

    #region Examine

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsWallHealerComponent> healer, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(KsWallHealerComponent)))
        {
            args.PushMarkup(Loc.GetString("ks-wall-healer-examine-supplies",
                ("count", GetSupplyCount(healer)),
                ("item", GetLocalizedSupplyName(healer))));

            if (healer.Comp.Patient is { } patient && !TerminatingOrDeleted(patient))
                args.PushMarkup(Loc.GetString("ks-wall-healer-examine-patient", ("patient", patient)));
        }
    }

    [SubscribeLocalEvent]
    private void OnAllowanceExamined(Entity<KsWallHealerAllowanceComponent> card, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("ks-wall-healer-examine-allowance",
            ("remaining", GetRemainingAllowance(card)),
            ("limit", card.Comp.Limit)));
    }

    #endregion

    #region Hands in and out

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
        BeginTreatment(healer, patient, args.User);
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
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs, out var doAfterId))
            return;

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

    #endregion

    #region Treatment

    /// <summary>
    ///     One treatment of the patient whose hand is in: as much as each pool can give, up to what the patient needs and
    ///         what their ID card allows.
    /// </summary>
    private void Treat(Entity<KsWallHealerComponent> healer, EntityUid patient)
    {
        // Unpowered, it just sits there, and the wait isn't started.
        if (!_powerReceiverSystem.IsPowered(healer.Owner))
            return;

        healer.Comp.NextTreatment = _gameTiming.CurTime + healer.Comp.TreatmentInterval;
        healer.Comp.TreatmentCount++;
        Dirty(healer);

        if (!_injurableQuery.TryComp(patient, out var injurableComponent)
            || injurableComponent.DamageContainer is not { } damageContainer
            || !healer.Comp.TreatableDamageContainers.Contains(damageContainer))
        {
            FailTreatment(healer, patient, "ks-wall-healer-not-organic");
            return;
        }

        var injectEvent = new TargetBeforeInjectEvent(patient, healer, patient);
        RaiseLocalEvent(patient, ref injectEvent);
        if (injectEvent.Cancelled)
        {
            FailTreatment(healer, patient, "ks-wall-healer-blocked");
            return;
        }

        if (_emagSystem.CheckFlag(healer, EmagType.Interaction))
        {
            _damageableSystem.ChangeDamage(patient, healer.Comp.EmaggedDamage, interruptsDoAfters: false, origin: healer);
            _forensicsSystem.TransferDna(healer, patient);
            _audioSystem.PlayPredicted(healer.Comp.EmaggedSound, healer, patient);

            if (ShouldGiveFeedback(healer.Comp.TreatmentCount, healer.Comp.SuccessFeedbackInterval))
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-emagged-treatment"), healer, patient, PopupType.MediumCaution);

            return;
        }

        // What each pool would give, before the ID card has its say.
        var amounts = new float[healer.Comp.Pools.Count];
        var total = 0f;
        var anyEmptyPoolNeeded = false;
        for (var i = 0; i < amounts.Length; i++)
        {
            var pool = healer.Comp.Pools[i];
            var needed = GetNeeded(pool, patient);
            if (needed <= 0f)
                continue;

            var available = GetPoolAmount(healer, i);
            if (available <= 0f)
                anyEmptyPoolNeeded = true;

            amounts[i] = MathF.Min(pool.Capacity * healer.Comp.TreatmentFraction, MathF.Min(available, needed));
            total += amounts[i];
        }

        if (total <= 0f)
        {
            FailTreatment(healer, patient, anyEmptyPoolNeeded ? "ks-wall-healer-needs-recharge" : "ks-wall-healer-healthy");
            return;
        }

        Entity<KsWallHealerAllowanceComponent>? allowance = null;
        if (healer.Comp.RequiresIdCard || healer.Comp.UsesIdAllowance)
        {
            var hasIdCard = _idCardSystem.TryFindIdCard(patient, out var idCard);
            if (!hasIdCard && healer.Comp.RequiresIdCard)
            {
                FailTreatment(healer, patient, "ks-wall-healer-no-id");
                return;
            }

            if (hasIdCard && healer.Comp.UsesIdAllowance)
            {
                allowance = ResolveAllowance(idCard);

                // Only the server adds the allowance, so a client's first prediction for a card goes unlimited.
                if (allowance is { } allowanceEntity)
                {
                    var remaining = GetRemainingAllowance(allowanceEntity);
                    if (remaining <= 0f)
                    {
                        FailTreatment(healer, patient, "ks-wall-healer-allowance-spent", ("card", idCard.Owner));
                        return;
                    }

                    if (total > remaining)
                    {
                        var scale = remaining / total;
                        for (var i = 0; i < amounts.Length; i++)
                            amounts[i] *= scale;
                    }
                }
            }
        }

        var treated = 0f;
        for (var i = 0; i < amounts.Length; i++)
        {
            if (amounts[i] <= 0f)
                continue;

            var given = Give(healer.Comp.Pools[i], patient, amounts[i], healer);
            healer.Comp.PoolAmounts[i] = MathF.Max(GetPoolAmount(healer, i) - given, 0f);
            treated += given;
        }

        if (treated <= 0f)
        {
            FailTreatment(healer, patient, "ks-wall-healer-healthy");
            return;
        }

        if (allowance is { } spentAllowance)
            SpendAllowance(spentAllowance, treated);

        Dirty(healer);
        RaisePoolsChanged(healer);
        _forensicsSystem.TransferDna(healer, patient);
        _audioSystem.PlayPredicted(healer.Comp.TreatedSound, healer, patient);

        if (ShouldGiveFeedback(healer.Comp.TreatmentCount, healer.Comp.SuccessFeedbackInterval))
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-treated"), healer, patient);
    }

    private void FailTreatment(Entity<KsWallHealerComponent> healer, EntityUid patient, string messageId, params (string, object)[] arguments)
    {
        _audioSystem.PlayPredicted(healer.Comp.FailedSound, healer, patient);

        if (!ShouldGiveFeedback(healer.Comp.TreatmentCount, healer.Comp.FailureFeedbackInterval))
            return;

        var messageArguments = new (string, object)[arguments.Length + 1];
        messageArguments[0] = ("healer", healer.Owner);
        arguments.CopyTo(messageArguments, 1);
        _popupSystem.PopupEntity(KsPopupMessage.Create(messageId, messageArguments), healer, patient);
    }

    /// <summary>
    ///     Feedback on the first of every <paramref name="interval"/> treatments, so a patient isn't told the same thing
    ///         every few seconds.
    /// </summary>
    private static bool ShouldGiveFeedback(int treatmentCount, int interval)
    {
        return interval <= 1 || (treatmentCount - 1) % interval == 0;
    }

    /// <summary>
    ///     How much of what <paramref name="pool"/> treats <paramref name="patient"/> is missing.
    /// </summary>
    private float GetNeeded(KsWallHealerPool pool, EntityUid patient)
    {
        switch (pool)
        {
            case KsWallHealerDamagePool damagePool:
            {
                if (!_damageableQuery.TryComp(patient, out var damageableComponent))
                    return 0f;

                var needed = 0f;
                var damage = _damageableSystem.GetPositiveDamage((patient, damageableComponent));
                foreach (var damageType in damagePool.DamageTypes)
                {
                    if (damage.DamageDict.TryGetValue(damageType, out var value))
                        needed += (float)value;
                }

                return needed;
            }
            case KsWallHealerBloodPool bloodPool:
            {
                if (!_bloodstreamQuery.TryComp(patient, out var bloodstreamComponent))
                    return 0f;

                var missingLevel = bloodPool.TargetBloodLevel - _bloodstreamSystem.GetBloodLevel((patient, bloodstreamComponent));
                return MathF.Max(missingLevel, 0f) * (float)bloodstreamComponent.BloodReferenceSolution.Volume;
            }
            default:
                Log.Error($"{nameof(KsWallHealerSystem)} doesn't know how to treat with a {pool.GetType().Name}.");
                return 0f;
        }
    }

    /// <summary>
    ///     Gives <paramref name="patient"/> up to <paramref name="amount"/> from <paramref name="pool"/>, returning how
    ///         much it actually gave.
    /// </summary>
    private float Give(KsWallHealerPool pool, EntityUid patient, float amount, EntityUid healer)
    {
        switch (pool)
        {
            case KsWallHealerDamagePool damagePool:
            {
                if (!_damageableQuery.TryComp(patient, out var damageableComponent))
                    return 0f;

                // Split between the pool's types by how much of each there is, as tg's adjust_*_loss spreads it.
                var damage = _damageableSystem.GetPositiveDamage((patient, damageableComponent));
                var poolDamage = 0f;
                foreach (var damageType in damagePool.DamageTypes)
                {
                    if (damage.DamageDict.TryGetValue(damageType, out var value))
                        poolDamage += (float)value;
                }

                if (poolDamage <= 0f)
                    return 0f;

                var healing = new DamageSpecifier();
                foreach (var damageType in damagePool.DamageTypes)
                {
                    if (damage.DamageDict.TryGetValue(damageType, out var value))
                        healing.DamageDict[damageType] = FixedPoint2.New(-(float)value / poolDamage * amount);
                }

                var change = _damageableSystem.ChangeDamage((patient, damageableComponent),
                    healing,
                    ignoreResistances: true,
                    interruptsDoAfters: false,
                    origin: healer);

                return MathF.Max(-(float)change.GetTotal(), 0f);
            }
            case KsWallHealerBloodPool bloodPool:
            {
                if (!_bloodstreamQuery.TryComp(patient, out var bloodstreamComponent))
                    return 0f;

                var levelBefore = _bloodstreamSystem.GetBloodLevel((patient, bloodstreamComponent));
                _bloodstreamSystem.TryRegulateBloodLevel((patient, bloodstreamComponent), FixedPoint2.New(amount), bloodPool.TargetBloodLevel);
                var levelAfter = _bloodstreamSystem.GetBloodLevel((patient, bloodstreamComponent));

                return MathF.Max(levelAfter - levelBefore, 0f) * (float)bloodstreamComponent.BloodReferenceSolution.Volume;
            }
            default:
                return 0f;
        }
    }

    /// <summary>
    ///     Refills every pool by <paramref name="fraction"/> of its capacity, up to full, returning how much went in.
    /// </summary>
    public float Refill(Entity<KsWallHealerComponent> healer, float fraction)
    {
        var refilled = 0f;
        for (var i = 0; i < healer.Comp.Pools.Count && i < healer.Comp.PoolAmounts.Count; i++)
        {
            var capacity = healer.Comp.Pools[i].Capacity;
            var before = healer.Comp.PoolAmounts[i];
            var after = MathF.Min(before + capacity * fraction, capacity);
            if (after <= before)
                continue;

            healer.Comp.PoolAmounts[i] = after;
            refilled += after - before;
        }

        if (refilled <= 0f)
            return refilled;

        Dirty(healer);
        RaisePoolsChanged(healer);
        return refilled;
    }

    public float GetPoolAmount(Entity<KsWallHealerComponent> healer, int poolIndex)
    {
        return poolIndex < healer.Comp.PoolAmounts.Count ? healer.Comp.PoolAmounts[poolIndex] : 0f;
    }

    /// <summary>
    ///     How full a pool is, from 0 to 1.
    /// </summary>
    public float GetPoolFill(Entity<KsWallHealerComponent> healer, int poolIndex)
    {
        var capacity = healer.Comp.Pools[poolIndex].Capacity;
        return capacity <= 0f ? 0f : Math.Clamp(GetPoolAmount(healer, poolIndex) / capacity, 0f, 1f);
    }

    private void RaisePoolsChanged(Entity<KsWallHealerComponent> healer)
    {
        var ev = new KsWallHealerPoolsChangedEvent();
        RaiseLocalEvent(healer, ref ev);
    }

    #endregion

    #region ID card allowance

    /// <summary>
    ///     The allowance on an ID card, added on the server the first time a station needs it. A client can't add a
    ///         networked component, so it gets null until the server's arrives.
    /// </summary>
    private Entity<KsWallHealerAllowanceComponent>? ResolveAllowance(EntityUid idCard)
    {
        if (TryComp<KsWallHealerAllowanceComponent>(idCard, out var allowanceComponent))
            return (idCard, allowanceComponent);

        if (_netManager.IsClient)
            return null;

        allowanceComponent = EnsureComp<KsWallHealerAllowanceComponent>(idCard);
        allowanceComponent.SpentAt = _gameTiming.CurTime;
        Dirty(idCard, allowanceComponent);
        return (idCard, allowanceComponent);
    }

    /// <summary>
    ///     How much has been spent, after what has recovered since it was last spent.
    /// </summary>
    private float GetSpentAllowance(Entity<KsWallHealerAllowanceComponent> card)
    {
        if (card.Comp.RecoveryTime <= TimeSpan.Zero)
            return 0f;

        var recoveredFraction = (float)((_gameTiming.CurTime - card.Comp.SpentAt) / card.Comp.RecoveryTime);
        return MathF.Max(card.Comp.Spent - card.Comp.Limit * MathF.Max(recoveredFraction, 0f), 0f);
    }

    public float GetRemainingAllowance(Entity<KsWallHealerAllowanceComponent> card)
    {
        return MathF.Max(card.Comp.Limit - GetSpentAllowance(card), 0f);
    }

    private void SpendAllowance(Entity<KsWallHealerAllowanceComponent> card, float amount)
    {
        card.Comp.Spent = GetSpentAllowance(card) + amount;
        card.Comp.SpentAt = _gameTiming.CurTime;
        Dirty(card);
    }

    #endregion

    #region Supplies

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<KsWallHealerComponent> healer, ref InteractUsingEvent args)
    {
        if (args.Handled
            || !_stackQuery.TryComp(args.Used, out var stackComponent)
            || stackComponent.StackTypeId != healer.Comp.SupplyStackType)
            return;

        args.Handled = true;

        var container = _containerSystem.EnsureContainer<Container>(healer, healer.Comp.SupplyContainerId);
        if (!_containerSystem.Insert(args.Used, container))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-restock-failed", ("item", args.Used), ("healer", healer.Owner)), healer, args.User);
            return;
        }

        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-wall-healer-restock", ("item", args.Used), ("healer", healer.Owner)),
            KsPopupMessage.Create("ks-wall-healer-restock-others", ("user", args.User), ("item", args.Used), ("healer", healer.Owner)),
            healer,
            args.User);
    }

    [SubscribeLocalEvent]
    private void OnGetAlternativeVerbs(Entity<KsWallHealerComponent> healer, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ks-wall-healer-verb-take-supply", ("item", GetLocalizedSupplyName(healer))),
            Act = () => TryTakeSupply(healer, user),
        });
    }

    /// <summary>
    ///     Hands <paramref name="user"/> one of the station's supplies.
    /// </summary>
    public bool TryTakeSupply(Entity<KsWallHealerComponent> healer, EntityUid user)
    {
        var itemName = GetSupplyName(healer);

        if (!_powerReceiverSystem.IsPowered(healer.Owner))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-supply-unpowered", ("item", itemName), ("healer", healer.Owner)), healer, user);
            return false;
        }

        if (!_containerSystem.TryGetContainer(healer, healer.Comp.SupplyContainerId, out var container)
            || container.ContainedEntities.Count == 0)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-supply-empty", ("item", itemName), ("healer", healer.Owner)), healer, user);
            return false;
        }

        if (_emagSystem.CheckFlag(healer, EmagType.Interaction))
        {
            var random = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
                (int)_gameTiming.CurTick.Value,
                KsSharedRandomExtensions.GetNetId(healer.Owner, EntityManager),
                KsSharedRandomExtensions.GetNetId(user, EntityManager));

            if (random.NextDouble() < healer.Comp.EmaggedSupplyJamChance)
            {
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-supply-jammed", ("item", itemName)), healer, user, PopupType.SmallCaution);
                return false;
            }
        }

        // The last one stocked comes out first. Whatever is left of its stack stays behind as a new entity, so the one
        // handed over is one that already exists and the pickup can be predicted.
        var item = container.ContainedEntities[^1];
        if (_stackQuery.TryComp(item, out var stackComponent) && stackComponent.Count > 1)
        {
            var remainderCount = stackComponent.Count - 1;
            _stackSystem.SetCount((item, stackComponent), 1);

            var remainder = PredictedSpawnInContainerOrDrop(ProtoMan.Index(stackComponent.StackTypeId).Spawn, healer, healer.Comp.SupplyContainerId);
            _stackSystem.SetCount(remainder, remainderCount);
        }

        _containerSystem.Remove(item, container);
        _handsSystem.PickupOrDrop(user, item);

        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-wall-healer-supply-taken", ("item", item), ("healer", healer.Owner)),
            KsPopupMessage.Create("ks-wall-healer-supply-taken-others", ("user", user), ("item", item), ("healer", healer.Owner)),
            healer,
            user);
        _audioSystem.PlayPredicted(healer.Comp.SupplySound, healer, user);
        return true;
    }

    /// <summary>
    ///     How many supplies the station holds, counting every item in its stacks.
    /// </summary>
    public int GetSupplyCount(Entity<KsWallHealerComponent> healer)
    {
        if (!_containerSystem.TryGetContainer(healer, healer.Comp.SupplyContainerId, out var container))
            return 0;

        var count = 0;
        foreach (var item in container.ContainedEntities)
            count += _stackSystem.GetCount(item);

        return count;
    }

    /// <summary>
    ///     The supplies' name, for a popup, which names it in each recipient's language.
    /// </summary>
    private KsPopupPrototypeName GetSupplyName(Entity<KsWallHealerComponent> healer)
    {
        return new KsPopupPrototypeName(ProtoMan.Index(healer.Comp.SupplyStackType).Spawn);
    }

    /// <summary>
    ///     The supplies' name in the active culture, for examine and verb text.
    /// </summary>
    private string GetLocalizedSupplyName(Entity<KsWallHealerComponent> healer)
    {
        var prototype = ProtoMan.Index<EntityPrototype>(ProtoMan.Index(healer.Comp.SupplyStackType).Spawn);
        return _contentLocalizationManager.GetLocalizedPrototypeName(prototype);
    }

    #endregion
}
