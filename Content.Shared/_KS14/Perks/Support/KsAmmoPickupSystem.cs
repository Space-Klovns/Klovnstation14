using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     Taking an ammo resupply (<see cref="KsAmmoPickupComponent"/>) by walking over it. The server checks every living
///         mob near it; a client checks only its own player, every tick it predicts, so the refill, the counter, the
///         sound and the popup are there at once, and re-applied on each re-predicted tick until the server's take
///         arrives. Dropping it on a kill is the server's <c>KsAmmoDropSystem</c>.
/// </summary>
/// <remarks>
///     A range poll rather than a sensor fixture's collide events: "nothing to fill, it stays" means a mob already
///         standing on it takes it once they switch to an empty gun, which a start-collide never hears again, and a
///         contact list replayed through state resets brings its own crashes (the prediction skill). Both sides poll on
///         the same ticks (<see cref="KsAmmoPickupComponent.CheckInterval"/> counted from the tick number), so the
///         client's predicted take, popup and sound land on the server's tick and are matched to its copies.
/// </remarks>
public sealed partial class KsAmmoPickupSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearby = new();

    public void SetFactions(Entity<KsAmmoPickupComponent> entity, IEnumerable<ProtoId<NpcFactionPrototype>> factions)
    {
        entity.Comp.Factions = new HashSet<ProtoId<NpcFactionPrototype>>(factions);
        Dirty(entity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A client predicts only its own player's take: nobody else's gun is predicted there.
        EntityUid? onlyTakerUid = null;
        if (_netManager.IsClient)
        {
            if (_playerManager.LocalEntity is not { } localUid)
                return;

            onlyTakerUid = localUid;
        }

        var tick = (long)_gameTiming.CurTick.Value;
        var tickRate = (double)_gameTiming.TickRate;
        var query = EntityQueryEnumerator<KsAmmoPickupComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pickupComponent, out var transformComponent))
        {
            var intervalTicks = Math.Max(1L, (long)Math.Round(pickupComponent.CheckInterval.TotalSeconds * tickRate));
            if ((tick + GetNetEntity(uid).Id) % intervalTicks != 0 || EntityManager.IsQueuedForDeletion(uid))
                continue;

            _nearby.Clear();
            _entityLookupSystem.GetEntitiesInRange(transformComponent.Coordinates, pickupComponent.Radius, _nearby);
            foreach (var taker in _nearby)
            {
                if (onlyTakerUid is { } localUid && taker.Owner != localUid)
                    continue;

                if (taker.Comp.CurrentState != MobState.Alive ||
                    !IsFriendly(pickupComponent, taker.Owner) ||
                    !TryRefillHeldGun(taker.Owner))
                    continue;

                _audioSystem.PlayPredicted(pickupComponent.PickupSound, taker.Owner, taker.Owner);
                _popupSystem.PopupEntity(KsPopupMessage.Create(pickupComponent.PickupPopup), taker.Owner, taker.Owner);
                PredictedQueueDel(uid);
                break;
            }
        }
    }

    /// <summary>
    ///     Whether <paramref name="takerUid"/> is on a side that may take it. A client only ever asks about its own player,
    ///         and goes by what the server last said of them: NPC factions aren't sent to clients, which would otherwise
    ///         judge by the factions of the mob's prototype and take a pickup the server then puts back.
    /// </summary>
    private bool IsFriendly(KsAmmoPickupComponent pickupComponent, EntityUid takerUid)
    {
        if (_netManager.IsClient)
            return pickupComponent.LocalPlayerMayTake;

        return _factionMemberQuery.TryComp(takerUid, out var factionMemberComponent) &&
               pickupComponent.Factions.Overlaps(factionMemberComponent.Factions);
    }

    [SubscribeLocalEvent]
    private void OnGetState(Entity<KsAmmoPickupComponent> entity, ref ComponentGetState args)
    {
        var mayTake = args.Player?.AttachedEntity is { } playerUid && IsFriendly(entity.Comp, playerUid);
        args.State = new KsAmmoPickupComponentState(entity.Comp.Radius, entity.Comp.CheckInterval, mayTake);
    }

    [SubscribeLocalEvent]
    private void OnHandleState(Entity<KsAmmoPickupComponent> entity, ref ComponentHandleState args)
    {
        if (args.Current is not KsAmmoPickupComponentState state)
            return;

        entity.Comp.Radius = state.Radius;
        entity.Comp.CheckInterval = state.CheckInterval;
        entity.Comp.LocalPlayerMayTake = state.MayTake;
    }

    /// <summary>
    ///     Fills the gun in <paramref name="userUid"/>'s active hand: its magazine (or its own ballistic store) up to
    ///         capacity with that store's own rounds, or its battery to full. False if there is no such gun, or nothing
    ///         to fill. Filling to the top, rather than by an amount, is what makes it safe to re-predict.
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
