#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Grenades;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The mark an NPC's throw leaves on a grenade (<see cref="NpcThrownGrenadeComponent"/>) is about that throw. Picked
///         up - caught, or a dud taken off the floor - it is someone else's grenade: thrown again, it is not coming down
///         where the NPC threw it, and going off, it is not the NPC's blast to push in on.
/// </summary>
public sealed class KsThrownGrenadeMarkerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsThrownMarkerTestGrenade
  components:
  - type: Item
    size: Tiny
";

    [TestCase(true)]
    [TestCase(false)]
    public async Task TestPickingItUpClearsTheThrow(bool pickedUp)
    {
        var server = Pair.Server;
        var entManager = server.EntMan;
        var map = await Pair.CreateTestMap();
        EntityUid grenadeUid = default;

        await server.WaitPost(() =>
        {
            var mobUid = entManager.SpawnEntity("MobHuman", map.GridCoords);
            grenadeUid = entManager.SpawnEntity("KsThrownMarkerTestGrenade", map.GridCoords);
            entManager.EnsureComponent<NpcThrownGrenadeComponent>(grenadeUid);

            if (pickedUp)
                Assert.That(entManager.System<SharedHandsSystem>().TryPickupAnyHand(mobUid, grenadeUid), "the test needs it picked up");
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(entManager.HasComponent<NpcThrownGrenadeComponent>(grenadeUid), Is.EqualTo(!pickedUp)));
    }
}
