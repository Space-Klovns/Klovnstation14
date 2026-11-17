#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Hands;

/// <summary>
///     Taking something worn straight into a hand (<c>SharedHandsSystem.CanPickupToHand</c>). Upstream refused it always,
///         by asking the wrong entity whether it could be taken off. Allowed now, a pickup takes no time, so it must not
///         take off what has an unequip delay: that goes through <c>InventorySystem.TryUnequip</c> and its do-after.
/// </summary>
public sealed class KsWornPickupTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsWornPickupTestQuick
  components:
  - type: Item
    size: Tiny
  - type: Clothing
    slots: [ pocket ]

- type: entity
  id: KsWornPickupTestSlow
  components:
  - type: Item
    size: Tiny
  - type: Clothing
    slots: [ pocket ]
    unequipDelay: 2

# Slow to take off a head, like C4. In a pocket it is only carried, and comes straight out.
- type: entity
  id: KsWornPickupTestSlowHat
  components:
  - type: Item
    size: Tiny
  - type: Clothing
    slots: [ head ]
    unequipDelay: 2
";

    [TestCase("KsWornPickupTestQuick", true)]
    [TestCase("KsWornPickupTestSlow", false)]
    [TestCase("KsWornPickupTestSlowHat", true)]
    public async Task TestWornItemIntoHand(string item, bool allowed)
    {
        var server = Pair.Server;
        var entManager = server.EntMan;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mobUid = entManager.SpawnEntity("MobHuman", map.GridCoords);
            var itemUid = entManager.SpawnEntity(item, map.GridCoords);

            Assert.That(entManager.System<InventorySystem>().TryEquip(mobUid, itemUid, "pocket1", silent: true, force: true),
                "the test needs it in a pocket");

            var handsSystem = entManager.System<SharedHandsSystem>();
            var handsComponent = entManager.GetComponent<HandsComponent>(mobUid);
            Assert.That(handsComponent.ActiveHandId, Is.Not.Null, "the test needs a hand");

            Assert.That(handsSystem.CanPickupToHand(mobUid, itemUid, handsComponent.ActiveHandId!, handsComp: handsComponent), Is.EqualTo(allowed));
        });
    }
}
