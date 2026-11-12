#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.SPAM;
using Content.Server._KS14.SupplyPod;
using Content.Server.Lathe;
using Content.Shared._KS14.SPAM;
using Content.Shared._KS14.SupplyPod;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.SPAM;

/// <summary>
///     Mining points: earned at the smelter, spent on orders. And what a lathe asks the salvagers for.
/// </summary>
[TestFixture]
[TestOf(typeof(KsSPAMSmelterSystem))]
[TestOf(typeof(KsMaterialRequestSystem))]
public sealed class KsSPAMEconomyTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId SalvageId = "SalvageIDCard";
    private static readonly EntProtoId TabletPrototype = "KsSPAM";
    private static readonly EntProtoId IronOre = "SteelOre1";
    private static readonly EntProtoId OreProcessor = "OreProcessor";
    private static readonly EntProtoId Silo = "MachineMaterialSilo";
    private static readonly EntProtoId Autolathe = "Autolathe";
    private readonly EntProtoId _smelter = "KsTestSPAMSmelter";
    private readonly ProtoId<KsSPAMProductPrototype> _product = "KsTestSPAMProduct";
    private readonly ProtoId<KsSPAMProductPrototype> _lockedProduct = "KsTestSPAMLockedProduct";
    private const string ProductEntity = "KsTestSPAMProductItem";
    private const int PointsPerIronOre = 3;
    private const int ProductCost = 50;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestSPAMSmelter
  components:
  - type: MaterialStorage
    whitelist:
      tags:
      - Ore
  - type: KsSPAMSmelter
    pointsPerUnit:
      SteelOre: 3

- type: entity
  parent: BaseItem
  id: KsTestSPAMProductItem

- type: ksSPAMProduct
  id: KsTestSPAMProduct
  entity: KsTestSPAMProductItem
  cost: 50
  category: ks-spam-category-tools

- type: ksSPAMProduct
  id: KsTestSPAMLockedProduct
  entity: KsTestSPAMProductItem
  cost: 1
  category: ks-spam-category-tools
  minRank: 99
";

    private sealed class Scene
    {
        public EntityUid UserUid;
        public EntityUid TabletUid;
        public EntityUid CardUid;
        public EntityCoordinates Coordinates;
    }

    /// <summary>
    ///     A salvager logged in to a tablet they hold.
    /// </summary>
    private async Task<Scene> SetUpAsync()
    {
        var testMap = await Pair.CreateTestMap();
        var scene = new Scene { Coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f) };
        await Server.WaitAssertion(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            scene.UserUid = SEntMan.SpawnEntity(Human, scene.Coordinates);
            scene.CardUid = SEntMan.SpawnEntity(SalvageId, scene.Coordinates);
            scene.TabletUid = SEntMan.SpawnEntity(TabletPrototype, scene.Coordinates);
            Assert.That(handsSystem.TryPickupAnyHand(scene.UserUid, scene.TabletUid), Is.True);
            Assert.That(handsSystem.TryPickupAnyHand(scene.UserUid, scene.CardUid), Is.True);
            Assert.That(SEntMan.System<KsSPAMSystem>().TryLogin(Tablet(scene), scene.UserUid), Is.True);
        });
        await Pair.RunTicksSync(5);
        return scene;
    }

    private Entity<KsSPAMComponent> Tablet(Scene scene) => (scene.TabletUid, SEntMan.GetComponent<KsSPAMComponent>(scene.TabletUid));

    private Entity<KsMinerAccountComponent> Account(Scene scene) => (scene.CardUid, SEntMan.GetComponent<KsMinerAccountComponent>(scene.CardUid));

    [Test]
    public async Task SmeltedOrePaysPointsToWhoeverClaims()
    {
        var scene = await SetUpAsync();

        await Server.WaitAssertion(() =>
        {
            var smelterUid = SEntMan.SpawnEntity(_smelter, scene.Coordinates);
            var oreUid = SEntMan.SpawnEntity(IronOre, scene.Coordinates);
            Assert.That(SEntMan.System<SharedMaterialStorageSystem>().TryInsertMaterialEntity(scene.UserUid, oreUid, smelterUid), Is.True);
            Assert.That(Account(scene).Comp.Points, Is.Zero, "Points must wait on the machine until claimed.");

            // Swiping the tablet claims them.
            SEntMan.System<SharedInteractionSystem>().InteractUsing(scene.UserUid, scene.TabletUid, smelterUid, scene.Coordinates);
            Assert.That(Account(scene).Comp.Points, Is.EqualTo(PointsPerIronOre), "Claiming must pay the ore's points.");
            Assert.That(Account(scene).Comp.Reputation, Is.Zero, "Smelting must not pay reputation.");

            SEntMan.System<SharedInteractionSystem>().InteractUsing(scene.UserUid, scene.TabletUid, smelterUid, scene.Coordinates);
            Assert.That(Account(scene).Comp.Points, Is.EqualTo(PointsPerIronOre), "Points must be claimed once.");
        });
    }

    /// <summary>
    ///     Ore that could be taken back out of a processor could be put in again for more points.
    /// </summary>
    [Test]
    public async Task OreProcessorsPayPointsAndKeepTheirOre()
    {
        var scene = await SetUpAsync();

        await Server.WaitAssertion(() =>
        {
            var processorUid = SEntMan.SpawnEntity(OreProcessor, scene.Coordinates);
            Assert.That(SEntMan.HasComponent<KsSPAMSmelterComponent>(processorUid), Is.True, "The ore processor must pay mining points.");

            var materialStorageComponent = SEntMan.GetComponent<MaterialStorageComponent>(processorUid);
            Assert.That(materialStorageComponent.CanEjectStoredMaterials, Is.False, "Ore must not be ejected from a processor.");
            Assert.That(materialStorageComponent.DropOnDeconstruct, Is.False, "Ore must not drop from a deconstructed processor.");
        });
    }

    [Test]
    public async Task OrdersSpendPointsAndArriveByPod()
    {
        var scene = await SetUpAsync();

        await Server.WaitAssertion(() =>
        {
            var spamSystem = SEntMan.System<KsSPAMSystem>();
            var cart = new Dictionary<ProtoId<KsSPAMProductPrototype>, int> { [_product] = 2 };

            Assert.That(spamSystem.TryOrder(Tablet(scene), scene.UserUid, cart), Is.False, "An order the account can't pay for must be refused.");

            spamSystem.AddPoints(Account(scene), ProductCost * 2 + 5);
            var lockedCart = new Dictionary<ProtoId<KsSPAMProductPrototype>, int> { [_lockedProduct] = 1 };
            Assert.That(spamSystem.TryOrder(Tablet(scene), scene.UserUid, lockedCart), Is.False, "A product above the account's rank must be refused.");
            Assert.That(Account(scene).Comp.Points, Is.EqualTo(ProductCost * 2 + 5), "A refused order must cost nothing.");

            Assert.That(spamSystem.TryOrder(Tablet(scene), scene.UserUid, cart), Is.True);
            Assert.That(Account(scene).Comp.Points, Is.EqualTo(5), "An order must cost its price.");

            var containerSystem = SEntMan.System<SharedContainerSystem>();
            var delivered = SEntMan.EntityQuery<MetaDataComponent>()
                .Where(meta => meta.EntityPrototype?.ID == ProductEntity)
                .Select(meta => meta.Owner)
                .ToList();
            Assert.That(delivered, Has.Count.EqualTo(2), "Everything ordered must be sent.");
            foreach (var deliveredUid in delivered)
            {
                Assert.That(containerSystem.TryGetContainingContainer(deliveredUid, out var container) &&
                            SEntMan.HasComponent<SupplyPodComponent>(container.Owner),
                    Is.True,
                    "An order must arrive inside a supply pod.");
            }

            spamSystem.AddPoints(Account(scene), ProductCost * 2);
            Assert.That(spamSystem.TryOrder(Tablet(scene), scene.UserUid, cart), Is.False, "A second order must wait for the cooldown.");
            Assert.That(Account(scene).Comp.Points, Is.EqualTo(ProductCost * 2 + 5), "An order refused for the cooldown must cost nothing.");
        });
    }

    [Test]
    public async Task ALatheAsksForWhatItIsShortOf()
    {
        var scene = await SetUpAsync();

        await Server.WaitAssertion(() =>
        {
            var requestSystem = SEntMan.System<KsMaterialRequestSystem>();
            var materialStorageSystem = SEntMan.System<SharedMaterialStorageSystem>();
            var siloUid = SEntMan.SpawnEntity(Silo, scene.Coordinates);
            var latheUid = SEntMan.SpawnEntity(Autolathe, scene.Coordinates);
            var latheComponent = SEntMan.GetComponent<LatheComponent>(latheUid);
            var recipeId = SEntMan.System<LatheSystem>().GetAvailableRecipes(latheUid, latheComponent).First();
            var materialId = SProtoMan.Index(recipeId).Materials.Keys.First();

            Assert.That(requestSystem.TryRequest((latheUid, latheComponent), recipeId), Is.True, "An empty lathe is short of everything.");
            Assert.That(requestSystem.TryRequest((latheUid, latheComponent), recipeId), Is.False, "Asking twice for the same thing must say nothing new.");

            var report = new List<KsSPAMSiloMaterial>();
            requestSystem.GetSiloReport(report);
            Assert.That(report.Any(material => material.Material == materialId && material.Requested > 0), Is.True,
                "The tablet must be told what was asked for.");

            Assert.That(materialStorageSystem.TryChangeMaterialAmount(siloUid, materialId, 100000), Is.True);
            report.Clear();
            requestSystem.GetSiloReport(report);
            Assert.That(report.Any(material => material.Requested > 0 && material.Material == materialId), Is.False,
                "A request the silo has met must be dropped.");

            // The tablet lists what salvagers bring even when the silo has never held any.
            report.Clear();
            requestSystem.GetSiloReport(report, alwaysShown: Tablet(scene).Comp.ShownMaterials);
            Assert.That(Tablet(scene).Comp.ShownMaterials, Is.Not.Empty);
            foreach (var shownId in Tablet(scene).Comp.ShownMaterials)
            {
                Assert.That(report.Count(material => material.Material == shownId), Is.EqualTo(1), $"{shownId} must be listed once.");
            }

            Assert.That(report.Any(material => material.Amount == 0 && material.Requested == 0), Is.True,
                "A material the silo has none of must be listed at zero.");
        });
    }
}
