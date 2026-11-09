#nullable enable
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._KS14.Cargo;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Cargo;

/// <summary>
///     The cargo console's checkout lists the approved goods that still sit on a trade-station buying pallet or a telepad,
///         and what a telepad has queued. Goods that leave the tile, or are deleted, drop off the list.
/// </summary>
[TestOf(typeof(CargoSystem))]
public sealed class KsCargoCheckoutTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestCheckoutGoods
  name: checkout goods

- type: cargoProduct
  id: KsTestCheckoutProduct
  icon:
    sprite: Objects/Specific/Janitorial/spray_bottle.rsi
    state: cleaner
  product: KsTestCheckoutGoods
  cost: 10
  category: cargoproduct-category-name-service
  group: market
";

    // Deliberately NOT static: they only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<CargoProductPrototype> _product = "KsTestCheckoutProduct";
    private readonly EntProtoId _goods = "KsTestCheckoutGoods";

    private static readonly ProtoId<CargoAccountPrototype> Account = "Cargo";
    private static readonly EntProtoId PalletPrototype = "CargoPalletBuy";
    private static readonly EntProtoId TelepadPrototype = "CargoTelepad";

    private EntityUid _grid;
    private EntityUid _station;
    private Entity<StationDataComponent> _stationData;

    private async Task SetUpStation()
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            var mapSystem = Server.System<SharedMapSystem>();
            _grid = testMap.Grid.Owner;
            for (var x = 1; x < 4; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            _station = Server.EntMan.SpawnEntity(null, testMap.MapCoords);
            var stationData = Server.EntMan.AddComponent<StationDataComponent>(_station);
            Server.System<StationSystem>().AddGridToStation(_station, _grid, stationData: stationData);
            _stationData = (_station, stationData);

            var database = Server.EntMan.AddComponent<StationCargoOrderDatabaseComponent>(_station);
            database.Orders[Account] = new List<CargoOrderData>();
            Server.EntMan.AddComponent<StationBankAccountComponent>(_station);
            Server.EntMan.AddComponent<TradeStationComponent>(_grid);
        });
    }

    private EntityUid SpawnAnchored(EntProtoId prototype, int tileX)
    {
        var uid = Server.EntMan.SpawnEntity(prototype, new EntityCoordinates(_grid, tileX + 0.5f, 0.5f));
        Server.System<SharedTransformSystem>().AnchorEntity(uid);
        return uid;
    }

    private List<KsCargoAwaitingOrder> Awaiting()
    {
        return Server.System<CargoSystem>().GetAwaitingOrders(_station);
    }

    private EntityUid FindGoods()
    {
        var query = Server.EntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var metaData))
        {
            if (metaData.EntityPrototype?.ID == _goods.Id)
                return uid;
        }

        Assert.Fail("No goods were spawned.");
        return default;
    }

    [Test]
    public async Task TradePalletGoodsAreListedUntilTheyLeaveTheirTile()
    {
        await SetUpStation();
        await Server.WaitAssertion(() =>
        {
            var cargo = Server.System<CargoSystem>();
            var transformSystem = Server.System<SharedTransformSystem>();
            SpawnAnchored(PalletPrototype, tileX: 0);

            Assert.That(Awaiting(), Is.Empty, "Nothing was ordered yet.");

            var product = Server.ProtoMan.Index(_product);
            var database = Server.EntMan.GetComponent<StationCargoOrderDatabaseComponent>(_station);
            Assert.That(cargo.AddAndApproveOrder(_station, product, 1, "Orderer", "reason", "Approver", database, Account, _stationData),
                "The order was fulfilled at the free pallet.");

            var awaiting = Awaiting();
            Assert.That(awaiting, Has.Count.EqualTo(1));
            Assert.That(awaiting[0].Where, Is.EqualTo(KsCargoDeliveryPoint.TradeStation));
            Assert.That(awaiting[0].OnPad, Is.EqualTo(1));
            Assert.That(awaiting[0].Queued, Is.EqualTo(0));
            Assert.That(awaiting[0].Requester, Is.EqualTo("Orderer"));

            // Shifted about within the pallet's tile it still waits.
            var goodsUid = FindGoods();
            transformSystem.SetCoordinates(goodsUid, new EntityCoordinates(_grid, 0.6f, 0.4f));
            Assert.That(Awaiting(), Has.Count.EqualTo(1), "Still on the pallet's tile.");

            // Carried to the next tile it is picked up.
            transformSystem.SetCoordinates(goodsUid, new EntityCoordinates(_grid, 1.5f, 0.5f));
            Assert.That(Awaiting(), Is.Empty, "Left the pallet's tile.");
        });
    }

    [Test]
    public async Task DeletedGoodsLeaveTheList()
    {
        await SetUpStation();
        await Server.WaitAssertion(() =>
        {
            SpawnAnchored(PalletPrototype, tileX: 0);
            var product = Server.ProtoMan.Index(_product);
            var database = Server.EntMan.GetComponent<StationCargoOrderDatabaseComponent>(_station);
            Assert.That(Server.System<CargoSystem>().AddAndApproveOrder(_station, product, 1, "Orderer", "reason", "Approver", database, Account, _stationData));
            Assert.That(Awaiting(), Has.Count.EqualTo(1));

            Server.EntMan.DeleteEntity(FindGoods());
            Assert.That(Awaiting(), Is.Empty);
        });
    }

    [Test]
    public async Task TelepadQueueAndItsGoodsAreListed()
    {
        await SetUpStation();
        await Server.WaitAssertion(() =>
        {
            var telepadUid = SpawnAnchored(TelepadPrototype, tileX: 2);
            // The queue is the telepad system's to write, so the test reaches it by reflection.
            var queue = (List<CargoOrderData>)typeof(CargoTelepadComponent).GetField(nameof(CargoTelepadComponent.CurrentOrders))!
                .GetValue(Server.EntMan.GetComponent<CargoTelepadComponent>(telepadUid))!;

            var order = new CargoOrderData(7, _product, 3, "Orderer", "reason", Account);
            order.SetApproverData("Approver", "Approver");
            order.Approved = true;
            queue.Add(order);
            queue.Add(order);

            var awaiting = Awaiting();
            Assert.That(awaiting, Has.Count.EqualTo(1));
            Assert.That(awaiting[0].Where, Is.EqualTo(KsCargoDeliveryPoint.Telepad));
            Assert.That(awaiting[0].Queued, Is.EqualTo(2));
            Assert.That(awaiting[0].OnPad, Is.EqualTo(0));

            // One teleported in: FulfillOrder at the telepad, then it leaves the queue.
            var fulfill = typeof(CargoSystem).GetMethod("FulfillOrder", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That((bool)fulfill.Invoke(Server.System<CargoSystem>(),
                [order, Account, new EntityCoordinates(_grid, 2.5f, 0.5f), "PaperCargoInvoice"])!);
            queue.RemoveAt(0);

            awaiting = Awaiting();
            Assert.That(awaiting, Has.Count.EqualTo(1));
            Assert.That(awaiting[0].OnPad, Is.EqualTo(1), "The teleported goods wait on the telepad.");
            Assert.That(awaiting[0].Queued, Is.EqualTo(1), "One is still queued.");
            Assert.That(awaiting[0].Where, Is.EqualTo(KsCargoDeliveryPoint.Telepad));
        });
    }
}
