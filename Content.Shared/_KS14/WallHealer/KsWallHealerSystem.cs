using Content.Shared.Access.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Forensics.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Localizations;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
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
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private SharedStackSystem _stackSystem = default!;

    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<HandsComponent> _handsQuery = default!;
    [Dependency] private EntityQuery<InjurableComponent> _injurableQuery = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;

    /// <summary>
    ///     Hands someone else put in during the do-after update, to begin treating in ours: (healer, patient, inserter).
    /// </summary>
    private readonly List<(EntityUid Healer, EntityUid Patient, EntityUid Inserter)> _pendingInserts = new();

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

        // Recharging is nobody's doing, so it is not predicted, and neither is someone else's hand going in.
        if (_netManager.IsClient)
            return;

        foreach (var (healerUid, patientUid, inserterUid) in _pendingInserts)
        {
            if (TryComp<KsWallHealerComponent>(healerUid, out var pendingHealerComponent) && !TerminatingOrDeleted(patientUid))
                BeginTreatment((healerUid, pendingHealerComponent), patientUid, inserterUid);
        }

        _pendingInserts.Clear();

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

            // Full or not, it looks again an interval later rather than every tick.
            healerComponent.NextRecharge = curTime + healerComponent.RechargeInterval;
            Dirty(healer);

            if (Refill(healer, healerComponent.RechargeFraction) > 0f)
                _audioSystem.PlayPvs(healerComponent.RechargedSound, uid);
        }
    }

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
}
