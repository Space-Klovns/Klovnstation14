using Content.Server._KS14.Cargo;
using Content.Server.Cargo.Components;
using Content.Shared._KS14.Cargo;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Systems;

// The checkout list of the cargo console: approved goods still on their buying pallet or telepad.
public sealed partial class CargoSystem
{
    [Dependency] private SharedMapSystem _ksMapSystem = default!;
    [Dependency] private EntityQuery<CargoPalletComponent> _ksPalletQuery = default!;
    [Dependency] private EntityQuery<CargoTelepadComponent> _ksTelepadQuery = default!;

    /// <summary>
    ///     Marks the goods an order just spawned at <paramref name="spawn"/> if that is a buying pallet or a telepad.
    /// </summary>
    private void KsMarkDeliveredOrder(EntityUid goodsUid, CargoOrderData order, EntityCoordinates spawn)
    {
        if (!TryComp<MapGridComponent>(spawn.EntityId, out var gridComponent))
            return;

        var tile = _ksMapSystem.CoordinatesToTile(spawn.EntityId, gridComponent, spawn);
        foreach (var anchoredUid in _ksMapSystem.GetAnchoredEntities((spawn.EntityId, gridComponent), tile))
        {
            var atTelepad = _ksTelepadQuery.HasComp(anchoredUid);
            if (!atTelepad && !(_ksPalletQuery.TryComp(anchoredUid, out var pallet) && (pallet.PalletType & BuySellType.Buy) != 0))
                continue;

            if (_station.GetOwningStation(anchoredUid) is not { } station)
                continue;

            var marker = EnsureComp<KsCargoDeliveredOrderComponent>(goodsUid);
            marker.Order = order;
            marker.DeliveryPoint = anchoredUid;
            marker.Station = station;
            marker.AtTelepad = atTelepad;
            return;
        }
    }

    /// <summary>
    ///     Marked goods that moved since the last update, to be checked once nothing is moving them any more.
    /// </summary>
    private readonly HashSet<EntityUid> _ksMovedGoods = new();
    private readonly List<EntityUid> _ksDrainedMovedGoods = new();

    [SubscribeLocalEvent]
    private void OnDeliveredOrderMoved(Entity<KsCargoDeliveredOrderComponent> entity, ref MoveEvent args)
    {
        // Checked later, not here: a grid split moves the goods and their pallet to the new grid one at a time, so in
        //      between they would look apart.
        _ksMovedGoods.Add(entity.Owner);
    }

    /// <summary>
    ///     Lets go of goods that have left their pallet or telepad since the last update. The marker's shutdown refreshes the
    ///         station's consoles.
    /// </summary>
    private void KsUpdateCheckout()
    {
        if (_ksMovedGoods.Count == 0)
            return;

        _ksDrainedMovedGoods.Clear();
        _ksDrainedMovedGoods.AddRange(_ksMovedGoods);
        _ksMovedGoods.Clear();

        foreach (var goodsUid in _ksDrainedMovedGoods)
        {
            if (!TryComp<KsCargoDeliveredOrderComponent>(goodsUid, out var marker) || KsIsOnDeliveryPoint((goodsUid, marker)))
                continue;

            RemComp(goodsUid, marker);
        }
    }

    /// <summary>
    ///     Whether the goods are on the tile their pallet or telepad is on now, wherever that has gone (a grid split takes
    ///         both to a new grid). Picked up, put in a crate or moved off, they are not.
    /// </summary>
    private bool KsIsOnDeliveryPoint(Entity<KsCargoDeliveredOrderComponent> goods)
    {
        var deliveryPoint = goods.Comp.DeliveryPoint;
        if (TerminatingOrDeleted(deliveryPoint) || TerminatingOrDeleted(goods.Owner))
            return false;

        var goodsTransform = Transform(goods.Owner);
        var pointTransform = Transform(deliveryPoint);
        if (!pointTransform.Anchored || goodsTransform.ParentUid != pointTransform.ParentUid
            || !TryComp<MapGridComponent>(pointTransform.ParentUid, out var gridComponent))
        {
            return false;
        }

        var gridUid = pointTransform.ParentUid;
        return _ksMapSystem.CoordinatesToTile(gridUid, gridComponent, goodsTransform.Coordinates)
               == _ksMapSystem.CoordinatesToTile(gridUid, gridComponent, pointTransform.Coordinates);
    }

    [SubscribeLocalEvent]
    private void OnDeliveredOrderShutdown(Entity<KsCargoDeliveredOrderComponent> entity, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(entity.Comp.Station))
            return;

        UpdateOrders(entity.Comp.Station);
    }

    /// <summary>
    ///     The approved orders of <paramref name="station"/> whose goods wait on a buying pallet or a telepad, or are queued at
    ///         a telepad.
    /// </summary>
    /// <param name="station">The station.</param>
    /// <param name="account">Only the orders paid from this account; null for every account.</param>
    public List<KsCargoAwaitingOrder> GetAwaitingOrders(EntityUid station, ProtoId<CargoAccountPrototype>? account = null)
    {
        var found = new Dictionary<(int OrderId, KsCargoDeliveryPoint Where), (CargoOrderData Order, int OnPad, int Queued)>();

        void Add(CargoOrderData order, KsCargoDeliveryPoint where, int onPad, int queued)
        {
            if (account != null && order.Account != account)
                return;

            found.TryGetValue((order.OrderId, where), out var entry);
            found[(order.OrderId, where)] = (order, entry.OnPad + onPad, entry.Queued + queued);
        }

        var markers = AllEntityQuery<KsCargoDeliveredOrderComponent>();
        while (markers.MoveNext(out var markerUid, out var marker))
        {
            // A marker removed this tick is shut down but still stored.
            if (marker.Station != station || marker.LifeStage > ComponentLifeStage.Running)
                continue;

            // Moved off since the last update, and not yet let go of.
            if (!KsIsOnDeliveryPoint((markerUid, marker)))
                continue;

            Add(marker.Order, marker.AtTelepad ? KsCargoDeliveryPoint.Telepad : KsCargoDeliveryPoint.TradeStation, 1, 0);
        }

        var telepads = AllEntityQuery<CargoTelepadComponent>();
        while (telepads.MoveNext(out var telepadUid, out var telepadComponent))
        {
            if (telepadComponent.CurrentOrders.Count == 0 || _station.GetOwningStation(telepadUid) != station)
                continue;

            foreach (var queued in telepadComponent.CurrentOrders)
                Add(queued, KsCargoDeliveryPoint.Telepad, 0, 1);
        }

        var awaiting = new List<KsCargoAwaitingOrder>(found.Count);
        foreach (var ((_, where), (order, onPad, queued)) in found)
        {
            awaiting.Add(new KsCargoAwaitingOrder(
                order.OrderId, order.Product, order.Requester, order.Approver, order.Account, where, onPad, queued));
        }

        awaiting.Sort((left, right) => left.OrderId != right.OrderId
            ? left.OrderId.CompareTo(right.OrderId)
            : left.Where.CompareTo(right.Where));
        return awaiting;
    }
}
