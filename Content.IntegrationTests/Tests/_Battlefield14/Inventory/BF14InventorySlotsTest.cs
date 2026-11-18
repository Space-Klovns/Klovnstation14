#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Battlefield14.Inventory;

/// <summary>
///     The Mono and BF14 slots on the humanoid templates: each takes only its own clothing, and the webbing and helmet
///         slots need what they are worn on.
/// </summary>
[TestFixture]
public sealed class BF14InventorySlotsTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: BF14TestSlotOuter
  components:
  - type: Item
  - type: Clothing
    slots: [ OUTERCLOTHING ]

- type: entity
  id: BF14TestSlotHead
  components:
  - type: Item
  - type: Clothing
    slots: [ HEAD ]

- type: entity
  id: BF14TestSlotWebbing
  components:
  - type: Item
  - type: Clothing
    slots: [ WEBBING ]

- type: entity
  id: BF14TestSlotHelmetCover
  components:
  - type: Item
  - type: Clothing
    slots: [ HELMETCOVER ]

- type: entity
  id: BF14TestSlotArmband
  components:
  - type: Item
  - type: Clothing
    slots: [ ARMBANDLEFT ]
";

    private static readonly EntProtoId Human = "MobHuman";
    private readonly EntProtoId _outer = "BF14TestSlotOuter";
    private readonly EntProtoId _head = "BF14TestSlotHead";
    private readonly EntProtoId _webbing = "BF14TestSlotWebbing";
    private readonly EntProtoId _helmetCover = "BF14TestSlotHelmetCover";
    private readonly EntProtoId _armband = "BF14TestSlotArmband";

    private static readonly string[] NewSlots =
        ["balaclava", "leftarmband", "rightarmband", "helmetcover", "helmetattachment", "suitstorage2", "webbing"];

    [Test]
    public async Task HumanHasEveryNewSlot()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var inventorySystem = server.System<InventorySystem>();

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(testMap.MapUid, 0, 0);
            var humanUid = SSpawnAtPosition(Human, origin);
            foreach (var slot in NewSlots)
            {
                Assert.That(inventorySystem.HasSlot(humanUid, slot), Is.True, $"MobHuman has no {slot} slot");
            }
        });
    }

    [Test]
    public async Task WebbingNeedsOuterClothing()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var inventorySystem = server.System<InventorySystem>();

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(testMap.MapUid, 0, 0);
            var humanUid = SSpawnAtPosition(Human, origin);
            var webbingUid = SSpawnAtPosition(_webbing, origin);
            Assert.That(inventorySystem.TryEquip(humanUid, webbingUid, "webbing", silent: true), Is.False,
                "webbing went on with no outer clothing");

            Assert.That(inventorySystem.TryEquip(humanUid, SSpawnAtPosition(_outer, origin), "outerClothing", silent: true, force: true), Is.True);
            Assert.That(inventorySystem.TryEquip(humanUid, webbingUid, "webbing", silent: true), Is.True,
                "webbing did not go on over outer clothing");
        });
    }

    [Test]
    public async Task HelmetCoverNeedsHead()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var inventorySystem = server.System<InventorySystem>();

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(testMap.MapUid, 0, 0);
            var humanUid = SSpawnAtPosition(Human, origin);
            var coverUid = SSpawnAtPosition(_helmetCover, origin);
            Assert.That(inventorySystem.TryEquip(humanUid, coverUid, "helmetcover", silent: true), Is.False,
                "helmet cover went on with no helmet");

            Assert.That(inventorySystem.TryEquip(humanUid, SSpawnAtPosition(_head, origin), "head", silent: true, force: true), Is.True);
            Assert.That(inventorySystem.TryEquip(humanUid, coverUid, "helmetcover", silent: true), Is.True,
                "helmet cover did not go on over a helmet");
        });
    }

    [Test]
    public async Task SlotsTakeOnlyTheirOwnClothing()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var inventorySystem = server.System<InventorySystem>();

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(testMap.MapUid, 0, 0);
            var humanUid = SSpawnAtPosition(Human, origin);
            var armbandUid = SSpawnAtPosition(_armband, origin);
            Assert.That(inventorySystem.TryEquip(humanUid, armbandUid, "rightarmband", silent: true), Is.False,
                "a left armband went on the right arm");
            Assert.That(inventorySystem.TryEquip(humanUid, armbandUid, "leftarmband", silent: true), Is.True);
        });
    }
}
