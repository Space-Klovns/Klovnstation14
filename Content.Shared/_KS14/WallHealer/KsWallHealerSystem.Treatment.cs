using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Access.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Events;
using Content.Shared.Damage;
using Content.Shared.Emag.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Containers;

namespace Content.Shared._KS14.WallHealer;

public sealed partial class KsWallHealerSystem
{
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
            // A copy, as damage modifiers scale the specifier they are given in place.
            _damageableSystem.ChangeDamage(patient, new DamageSpecifier(healer.Comp.EmaggedDamage), interruptsDoAfters: false, origin: healer);
            _forensicsSystem.TransferDna(healer, patient);
            _audioSystem.PlayPredicted(healer.Comp.EmaggedSound, healer, patient);

            if (ShouldGiveFeedback(healer.Comp.TreatmentCount, healer.Comp.SuccessFeedbackInterval))
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-emagged-treatment"), healer, patient, type: PopupType.MediumCaution);

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

                var missingLevel = bloodPool.TargetBloodLevel - GetPreciseBloodLevel((patient, bloodstreamComponent));
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

                var levelBefore = GetPreciseBloodLevel((patient, bloodstreamComponent));
                _bloodstreamSystem.TryRegulateBloodLevel((patient, bloodstreamComponent), FixedPoint2.New(amount), bloodPool.TargetBloodLevel);
                var levelAfter = GetPreciseBloodLevel((patient, bloodstreamComponent));

                return MathF.Max(levelAfter - levelBefore, 0f) * (float)bloodstreamComponent.BloodReferenceSolution.Volume;
            }
            default:
                return 0f;
        }
    }

    /// <summary>
    ///     The patient's blood level as a fraction of normal volume, like
    ///         <see cref="SharedBloodstreamSystem.GetBloodLevel"/> but without rounding to a <see cref="FixedPoint2"/>:
    ///         at hundredths of a 600u bloodstream that is 6u, more than one treatment restores.
    /// </summary>
    private float GetPreciseBloodLevel(Entity<BloodstreamComponent> patient)
    {
        var referenceSolution = patient.Comp.BloodReferenceSolution;
        if (referenceSolution.Volume <= FixedPoint2.Zero
            || !_solutionContainerSystem.TryGetSolution(patient.Owner, patient.Comp.BloodSolutionName, out _, out var bloodSolution))
            return 0f;

        var level = patient.Comp.MaxVolumeModifier;
        foreach (var (reagentId, referenceQuantity) in referenceSolution.Contents)
        {
            if (referenceQuantity > FixedPoint2.Zero)
                level = MathF.Min(level, (float)bloodSolution.GetTotalPrototypeQuantity(reagentId.Prototype) / (float)referenceQuantity);
        }

        return level;
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
}
