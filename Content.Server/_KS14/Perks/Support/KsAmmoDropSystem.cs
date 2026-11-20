using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.Companions;
using Content.Shared._KS14.Perks.Support;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Perks.Support;

/// <summary>
///     The Support perk's ammo drop (<see cref="KsAmmoDropAbilityComponent"/>): a kill may leave a resupply
///         (<see cref="KsAmmoPickupComponent"/>) that refills a friendly's gun when they walk over it.
/// </summary>
public sealed partial class KsAmmoDropSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private KsKillCreditSystem _killCreditSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticQuery = default!;
    [Dependency] private EntityQuery<KsAmmoDropAbilityComponent> _dropAbilityQuery = default!;
    [Dependency] private EntityQuery<KsCompanionComponent> _companionQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearby = new();

    // MobStateChangedEvent is raised by value, so a plain broadcast handler.
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead ||
            args.OldMobState == MobState.Dead ||
            !TryGetDropper(_killCreditSystem.GetAttacker(args.Origin), out var dropper) ||
            dropper.Owner == args.Target ||
            _npcFactionSystem.IsEntityFriendly(dropper.Owner, args.Target) ||
            !_robustRandom.Prob(Math.Clamp(dropper.Comp.Chance, 0f, 1f)))
            return;

        var pickupUid = Spawn(dropper.Comp.Pickup, _transformSystem.GetMapCoordinates(args.Target));
        var pickupComponent = EnsureComp<KsAmmoPickupComponent>(pickupUid);
        if (_factionMemberQuery.TryComp(dropper.Owner, out var factionMemberComponent))
            pickupComponent.Factions = new HashSet<ProtoId<NpcFactionPrototype>>(factionMemberComponent.Factions);
    }

    /// <summary>
    ///     Who the kill counts for: the attacker, whoever holds it (a gun fires a beam as itself), or the owner of a
    ///         companion that made it.
    /// </summary>
    private bool TryGetDropper(EntityUid? attackerUid, out Entity<KsAmmoDropAbilityComponent> dropper)
    {
        dropper = default;
        if (attackerUid is not { } uid || Deleted(uid))
            return false;

        if (_containerSystem.TryGetContainingContainer(uid, out var container))
            uid = container.Owner;

        if (_companionQuery.TryComp(uid, out var companionComponent) && companionComponent.OwnerUid is { } ownerUid)
            uid = ownerUid;

        if (!_dropAbilityQuery.TryComp(uid, out var dropComponent))
            return false;

        dropper = (uid, dropComponent);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsAmmoPickupComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pickupComponent, out var transformComponent))
        {
            if (curTime < pickupComponent.NextCheck)
                continue;

            pickupComponent.NextCheck = curTime + pickupComponent.CheckInterval;

            _nearby.Clear();
            _entityLookupSystem.GetEntitiesInRange(transformComponent.Coordinates, pickupComponent.Radius, _nearby);
            foreach (var taker in _nearby)
            {
                if (taker.Comp.CurrentState != MobState.Alive ||
                    !IsFriendly(pickupComponent, taker.Owner) ||
                    !TryRefillHeldGun(taker.Owner))
                    continue;

                _audioSystem.PlayPvs(pickupComponent.PickupSound, taker.Owner);
                _popupSystem.PopupEntity(KsPopupMessage.Create(pickupComponent.PickupPopup), taker.Owner, taker.Owner);
                QueueDel(uid);
                break;
            }
        }
    }

    private bool IsFriendly(KsAmmoPickupComponent pickupComponent, EntityUid takerUid)
    {
        return _factionMemberQuery.TryComp(takerUid, out var factionMemberComponent) &&
               pickupComponent.Factions.Overlaps(factionMemberComponent.Factions);
    }

    /// <summary>
    ///     Fills the gun in <paramref name="userUid"/>'s active hand: its magazine (or its own ballistic store) up to
    ///         capacity with that store's own rounds, or its battery to full. False if there is no such gun, or nothing
    ///         to fill.
    /// </summary>
    public bool TryRefillHeldGun(EntityUid userUid)
    {
        if (_handsSystem.GetActiveItem(userUid) is not { } gunUid || !HasComp<GunComponent>(gunUid))
            return false;

        if (_containerSystem.TryGetContainer(gunUid, SharedGunSystem.MagazineSlot, out var magazineContainer) &&
            magazineContainer is ContainerSlot { ContainedEntity: { } magazineUid } &&
            TryRefillBallistic(magazineUid))
            return true;

        if (TryRefillBallistic(gunUid))
            return true;

        return HasComp<BatteryAmmoProviderComponent>(gunUid) && TryRecharge(gunUid);
    }

    private bool TryRefillBallistic(EntityUid uid)
    {
        if (!_ballisticQuery.TryComp(uid, out var ballisticComponent) ||
            ballisticComponent.Proto == null)
            return false;

        var missing = ballisticComponent.Capacity - ballisticComponent.Entities.Count - ballisticComponent.UnspawnedCount;
        if (missing <= 0)
            return false;

        // Unspawned rounds are the store's own type.
        _gunSystem.SetBallisticUnspawned((uid, ballisticComponent), ballisticComponent.UnspawnedCount + missing);
        return true;
    }

    private bool TryRecharge(EntityUid uid)
    {
        var getCharge = new GetChargeEvent();
        RaiseLocalEvent(uid, ref getCharge);
        if (getCharge.MaxCharge <= 0f || getCharge.CurrentCharge >= getCharge.MaxCharge)
            return false;

        var changeCharge = new ChangeChargeEvent(getCharge.MaxCharge - getCharge.CurrentCharge);
        RaiseLocalEvent(uid, ref changeCharge);
        return true;
    }
}
