using Content.Server._KS14.Dissolve;
using Content.Shared._KS14.Companions;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Companions;

/// <summary>
///     Works <see cref="KsCombatMedicComponent"/>: finds the worst hurt friendly near the medic, goes to them if it is a
///         companion, readies its injector with a beep, and injects.
/// </summary>
public sealed partial class KsCombatMedicSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private KsCompanionSystem _companionSystem = default!;
    [Dependency] private KsDissolveSystem _dissolveSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _candidates = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsCombatMedicComponent>();
        while (query.MoveNext(out var uid, out var medicComponent))
        {
            var medic = new Entity<KsCombatMedicComponent>(uid, medicComponent);

            // A patient deleted is let go at once, mid-windup too (the injection wasted, as on reaching it gone): a
            //     companion must not be left following an entity that no longer exists.
            if (medicComponent.Patient is { } currentPatientUid && TerminatingOrDeleted(currentPatientUid))
            {
                if (medicComponent.InjectAt != null)
                    FinishInjection(medic, curTime);
                else
                    SetPatient(medic, null);

                continue;
            }

            if (medicComponent.InjectAt is { } injectAt)
            {
                if (curTime >= injectAt)
                    FinishInjection(medic, curTime);

                continue;
            }

            if (curTime < medicComponent.NextScan)
                continue;

            medicComponent.NextScan = curTime + medicComponent.ScanInterval;

            if (curTime < medicComponent.NextInject ||
                !_mobStateSystem.IsAlive(uid) ||
                _dissolveSystem.IsDissolving(uid) ||
                _dissolveSystem.IsMaterialising(uid))
                continue;

            if (FindPatient(medic) is not { } patientUid)
            {
                SetPatient(medic, null);
                continue;
            }

            SetPatient(medic, patientUid);

            if (!InReach(medic, patientUid))
                continue;

            // The tell: the injector comes up, with a beep.
            medicComponent.InjectAt = curTime + medicComponent.Windup;
            _audioSystem.PlayPvs(medicComponent.WindupSound, uid);
            _popupSystem.PopupEntity(
                KsPopupMessage.Create("ks-combat-medic-readies", ("medic", uid), ("patient", patientUid)),
                uid,
                PopupType.Small);
        }
    }

    private void FinishInjection(Entity<KsCombatMedicComponent> medic, TimeSpan curTime)
    {
        medic.Comp.InjectAt = null;
        medic.Comp.NextInject = curTime + medic.Comp.Cooldown;

        var patientUid = medic.Comp.Patient;
        SetPatient(medic, null);

        // Gone, dead or out of reach by now: the injection is wasted, and the cooldown runs anyway.
        if (patientUid is not { } uid ||
            TerminatingOrDeleted(uid) ||
            _mobStateSystem.IsDead(uid) ||
            !_mobStateSystem.IsAlive(medic.Owner) ||
            !InReach(medic, uid))
            return;

        var solution = _mobStateSystem.IsCritical(uid) ? medic.Comp.CriticalInjection : medic.Comp.Injection;
        if (!_bloodstreamSystem.TryAddToBloodstream(uid, solution.Clone()))
            return;

        _audioSystem.PlayPvs(medic.Comp.InjectSound, uid);
        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-combat-medic-injects", ("medic", medic.Owner), ("patient", uid)),
            uid,
            PopupType.Medium);
    }

    /// <summary>
    ///     The friendly most in need: critical before merely hurt, then the most damaged.
    /// </summary>
    private EntityUid? FindPatient(Entity<KsCombatMedicComponent> medic)
    {
        _candidates.Clear();
        _entityLookupSystem.GetEntitiesInRange(_transformSystem.GetMapCoordinates(medic.Owner), medic.Comp.SeekRange, _candidates);

        EntityUid? bestUid = null;
        var bestCritical = false;
        var bestDamage = FixedPoint2.Zero;
        foreach (var candidate in _candidates)
        {
            var state = candidate.Comp.CurrentState;
            if (candidate.Owner == medic.Owner ||
                state == MobState.Dead ||
                !_bloodstreamQuery.HasComp(candidate.Owner) ||
                !_npcFactionSystem.IsEntityFriendly(medic.Owner, candidate.Owner))
                continue;

            var damage = _damageableSystem.GetTotalDamage(candidate.Owner);
            if (damage < medic.Comp.MinDamage)
                continue;

            var critical = state == MobState.Critical;
            if (bestUid != null && (bestCritical && !critical || bestCritical == critical && damage <= bestDamage))
                continue;

            bestUid = candidate.Owner;
            bestCritical = critical;
            bestDamage = damage;
        }

        _candidates.Clear();
        return bestUid;
    }

    private bool InReach(Entity<KsCombatMedicComponent> medic, EntityUid patientUid)
    {
        return _interactionSystem.InRangeUnobstructed(medic.Owner, patientUid, medic.Comp.InjectRange);
    }

    /// <summary>
    ///     A companion goes to its patient, and back to its owner without one.
    /// </summary>
    private void SetPatient(Entity<KsCombatMedicComponent> medic, EntityUid? patientUid)
    {
        if (medic.Comp.Patient == patientUid)
            return;

        medic.Comp.Patient = patientUid;
        _companionSystem.SetFollowOverride(medic.Owner, patientUid);
    }
}
