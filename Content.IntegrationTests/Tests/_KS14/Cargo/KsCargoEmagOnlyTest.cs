#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Emag.Systems;
using Content.Shared.Station.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Cargo;

/// <summary>
///     Emag-only cargo products are offered by an emagged order console alone, under the contraband category.
/// </summary>
[TestFixture]
[TestOf(typeof(CargoSystem))]
public sealed class KsCargoEmagOnlyTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestEmagOnlyGoods

- type: cargoProduct
  id: KsTestEmagOnlyProduct
  icon:
    sprite: Objects/Specific/Janitorial/spray_bottle.rsi
    state: cleaner
  product: KsTestEmagOnlyGoods
  cost: 10
  category: cargoproduct-category-name-contraband
  group: market
  ksEmagOnly: true
";

    private readonly ProtoId<CargoProductPrototype> _emagOnlyProduct = "KsTestEmagOnlyProduct";

    private static readonly ProtoId<CargoAccountPrototype> Account = "Cargo";
    private static readonly EntProtoId Console = "ComputerCargoOrders";
    private static readonly EntProtoId Emag = "EmagUnlimited";
    private const string ContrabandCategory = "cargoproduct-category-name-contraband";

    [Test]
    public async Task OnlyAnEmaggedConsoleOffersEmagOnlyProducts()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var stationUid = SEntMan.SpawnEntity(null, testMap.MapCoords);
            var stationData = SEntMan.AddComponent<StationDataComponent>(stationUid);
            Server.System<StationSystem>().AddGridToStation(stationUid, testMap.Grid.Owner, stationData: stationData);
            SEntMan.AddComponent<StationCargoOrderDatabaseComponent>(stationUid).Orders[Account] = new List<CargoOrderData>();
            SEntMan.AddComponent<StationBankAccountComponent>(stationUid);

            var consoleUid = SEntMan.SpawnEntity(Console, testMap.GridCoords);
            var console = (consoleUid, SEntMan.GetComponent<CargoOrderConsoleComponent>(consoleUid));
            var cargoSystem = Server.System<CargoSystem>();

            Assert.That(cargoSystem.GetAvailableProducts(console), Is.Not.Empty, "The console must offer the ordinary catalogue.");
            Assert.That(cargoSystem.GetAvailableProducts(console), Does.Not.Contain(_emagOnlyProduct),
                "An emag-only product must not be offered before the console is emagged.");

            var userUid = SEntMan.SpawnEntity(null, testMap.GridCoords);
            var emagUid = SEntMan.SpawnEntity(Emag, testMap.GridCoords);
            Assert.That(Server.System<EmagSystem>().TryEmagEffect(emagUid, userUid, consoleUid), Is.True);

            Assert.That(cargoSystem.GetAvailableProducts(console), Does.Contain(_emagOnlyProduct),
                "An emagged console must offer emag-only products.");
        });
    }

    /// <summary>
    ///     The cargo windows build their tabs from product categories, so this is what gives emag-only products a
    ///         Contraband tab of their own.
    /// </summary>
    [Test]
    public async Task EveryEmagOnlyProductIsContraband()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypeManager = Server.ResolveDependency<IPrototypeManager>();
            Assert.Multiple(() =>
            {
                foreach (var product in prototypeManager.EnumeratePrototypes<CargoProductPrototype>())
                {
                    if (product.KsEmagOnly)
                        Assert.That(product.Category, Is.EqualTo(ContrabandCategory), $"Emag-only product {product.ID} must be in the contraband category.");
                }
            });
        });
    }
}
