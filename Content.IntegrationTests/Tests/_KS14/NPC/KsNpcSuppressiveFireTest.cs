#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Suppression;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcSuppressiveFireSystem"/>: an NPC firing at a spot, with no target entity to shoot at.
/// </summary>
public sealed class KsNpcSuppressiveFireTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Npc = "KsSuppressionTestNpc";
    private const string Gun = "KsSuppressionTestGun";

    private const string GunHand = "right";

    /// <summary>
    ///     A gun that fires fast, and whose ammo the test can count.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsSuppressionTestNpc
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: MobState
  - type: CombatMode

- type: entity
  id: KsSuppressionTestGun
  components:
  - type: Item
  - type: Gun
    fireRate: 20
    selectedMode: FullAuto
    availableModes:
    - FullAuto
  - type: BasicEntityAmmoProvider
    proto: BulletPistol
    capacity: 1000
    count: 1000
";

    [Test]
    public async Task TestFiresAtTheSpotUntilItsTimeIsUp()
    {
        var (entManager, npcUid, gunUid) = await SetUp();
        var suppressiveFireSystem = entManager.System<NpcSuppressiveFireSystem>();
        var combatModeSystem = entManager.System<SharedCombatModeSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();

        var duration = System.TimeSpan.FromSeconds(0.5);
        var ammoAtStart = 0;

        await Pair.Server.WaitPost(() =>
        {
            ammoAtStart = GetAmmo(entManager, gunUid);
            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates.Offset(new Vector2(4f, 0f));
            suppressiveFireSystem.Start(npcUid, coordinates, duration);
        });

        await Pair.RunTicksSync((int) (duration.TotalSeconds * timing.TickRate / 2));

        var ammoPartWay = 0;

        await Pair.Server.WaitAssertion(() =>
        {
            ammoPartWay = GetAmmo(entManager, gunUid);

            Assert.Multiple(() =>
            {
                Assert.That(suppressiveFireSystem.IsFiring(npcUid), "it should still be firing");
                Assert.That(ammoPartWay, Is.LessThan(ammoAtStart), "it should have fired with no target at all");
            });
        });

        await Pair.RunTicksSync((int) (duration.TotalSeconds * timing.TickRate) + 2);

        var ammoAtEnd = 0;

        await Pair.Server.WaitAssertion(() =>
        {
            ammoAtEnd = GetAmmo(entManager, gunUid);

            Assert.Multiple(() =>
            {
                Assert.That(suppressiveFireSystem.IsFiring(npcUid), Is.False, "its time is up");
                Assert.That(combatModeSystem.IsInCombatMode(npcUid), Is.False, "and it should be out of combat mode again");
            });
        });

        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(GetAmmo(entManager, gunUid), Is.EqualTo(ammoAtEnd), "nothing more should be fired"));
    }

    private static int GetAmmo(IEntityManager entManager, EntityUid gunUid)
    {
        var ev = new GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ev);
        return ev.Count;
    }

    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid GunUid)> SetUp()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, gunUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;
            npcUid = SpawnAt(entManager, Npc, gridUid, 0, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(npcUid, GunHand, HandLocation.Right);

            gunUid = entManager.SpawnEntity(Gun, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, gunUid, GunHand));
            handsSystem.TrySetActiveHand(npcUid, GunHand); // false if it already is
        });

        return (entManager, npcUid, gunUid);
    }
}
