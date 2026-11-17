#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Both AT-4 launchers can be carried in the suit storage slot, not only on the back.
/// </summary>
[TestFixture]
public sealed class KsFieldCommanderAt4SuitStorageTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Armor = "ClothingOuterArmorBasic";
    private static readonly EntProtoId[] Launchers = ["KsWeaponLauncherRocketAt4", "KsWeaponLauncherRocketAt4FieldCommander"];

    [Test]
    public async Task At4FitsSuitStorage()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var inventorySystem = server.System<InventorySystem>();

        foreach (var launcherProto in Launchers)
        {
            var equipped = false;
            await server.WaitPost(() =>
            {
                var humanUid = SSpawnAtPosition(Human, new EntityCoordinates(testMap.MapUid, 0, 0));
                var armorUid = SSpawn(Armor);
                Assert.That(inventorySystem.TryEquip(humanUid, armorUid, "outerClothing", force: true), Is.True);

                var launcherUid = SSpawn(launcherProto);
                equipped = inventorySystem.TryEquip(humanUid, launcherUid, "suitstorage");
            });

            Assert.That(equipped, Is.True, $"{launcherProto} should fit in suit storage");
        }
    }
}
