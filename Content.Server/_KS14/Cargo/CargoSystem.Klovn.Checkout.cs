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
            marker.Grid = spawn.EntityId;
            marker.Tile = tile;
            marker.AtTelepad = atTelepad;
            return;
        }
    }

    [SubscribeLocalEvent]
    private void OnDeliveredOrderMoved(Entity<KsCargoDeliveredOrderComponent> entity, ref MoveEvent args)
    {
        var transformComponent = args.Component;
        // TryComp: the grid may be on its way out, taking the goods with it.
        if (transformComponent.ParentUid == entity.Comp.Grid &&
            TryComp<MapGridComponent>(entity.Comp.Grid, out var gridComponent) &&
            _ksMapSystem.CoordinatesToTile(entity.Comp.Grid, gridComponent, transformComponent.Coordinates) == entity.Comp.Tile)
        {
            return;
        }

        // Off its tile, or picked up or crated: no longer waiting. The shutdown refreshes the consoles.
        RemCompDeferred<KsCargoDeliveredOrderComponent>(entity.Owner);
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
        while (markers.MoveNext(out var marker))
        {
            // A marker removed this tick is shut down but still stored.
            if (marker.Station != station || marker.LifeStage > ComponentLifeStage.Running)
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
