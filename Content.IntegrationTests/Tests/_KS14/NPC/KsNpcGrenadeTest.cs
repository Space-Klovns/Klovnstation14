#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Grenades;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Trigger.Components;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcGrenadeSystem"/>: an NPC throwing a grenade it carries, with its weapon back in hand afterwards.
///         Whether and where it throws one is the operative HTN's, and getting things out and back is
///         <see cref="KsNpcHandsTest"/>'s.
/// </summary>
public sealed class KsNpcGrenadeTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Npc = "KsGrenadeTestNpc";
    private const string Grenade = "KsGrenadeTestGrenade";
    private const string Weapon = "KsGrenadeTestWeapon";

    private const string WeaponHand = "right";

    /// <summary>
    ///     A grenade with a fuse far longer than any test.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsGrenadeTestNpc
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: MobState

- type: entity
  id: KsGrenadeTestWeapon
  components:
  - type: Item
    size: Large
  - type: Wieldable

- type: entity
  parent: GrenadeFlashBang
  id: KsGrenadeTestGrenade
  components:
  - type: TimerTrigger
    delay: 1000
";

    [Test]
    public async Task TestThrowsAndLightsIt()
    {
        var (entManager, npcUid, weaponUid, grenadeUid) = await SetUp();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var start = Vector2.Zero;

        await Pair.Server.WaitAssertion(() =>
        {
            start = transformSystem.GetWorldPosition(npcUid);
            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates.Offset(new Vector2(4f, 0f));

            Assert.That(grenadeSystem.TryThrow(npcUid, grenadeUid, coordinates), "the grenade should be thrown");

            Assert.Multiple(() =>
            {
                Assert.That(entManager.HasComponent<ActiveTimerTriggerComponent>(grenadeUid), "its fuse should be lit");
                Assert.That(handsSystem.IsHolding(npcUid, grenadeUid), Is.False, "it should have left the hand");
                Assert.That(handsSystem.GetActiveItem(npcUid), Is.EqualTo(weaponUid), "the weapon should be back in hand");
                Assert.That(entManager.GetComponent<WieldableComponent>(weaponUid).Wielded, "and wielded again");
            });
        });

        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(Vector2.Distance(transformSystem.GetWorldPosition(grenadeUid), start), Is.GreaterThan(1f),
                "it should have gone the way it was thrown"));
    }

    /// <summary>
    ///     An NPC that cannot throw right now, as anyone just after a throw, is left holding nothing live: a lit grenade
    ///         would otherwise go back in its pocket and go off there.
    /// </summary>
    [Test]
    public async Task TestNoThrowLeavesItUnlit()
    {
        var (entManager, npcUid, weaponUid, grenadeUid) = await SetUp();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.GetComponent<HandsComponent>(npcUid).NextThrowTime = timing.CurTime + System.TimeSpan.FromHours(1);

            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates.Offset(new Vector2(4f, 0f));

            Assert.That(grenadeSystem.TryThrow(npcUid, grenadeUid, coordinates), Is.False);

            Assert.Multiple(() =>
            {
                Assert.That(entManager.HasComponent<ActiveTimerTriggerComponent>(grenadeUid), Is.False, "its fuse should not be lit");
                Assert.That(handsSystem.IsHolding(npcUid, grenadeUid), Is.False, "it should have gone back");
                Assert.That(entManager.System<InventorySystem>().TryGetSlotEntity(npcUid, "pocket1", out var pocketUid) && pocketUid == grenadeUid, "to its pocket");
                Assert.That(handsSystem.GetActiveItem(npcUid), Is.EqualTo(weaponUid), "the weapon should be back in hand");
            });
        });
    }

    /// <summary>
    ///     An NPC with a weapon wielded in both hands, and a grenade in its pocket.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid WeaponUid, EntityUid GrenadeUid)> SetUp()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, weaponUid = default, grenadeUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;
            npcUid = SpawnAt(entManager, Npc, gridUid, 0, 0);
            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(npcUid, WeaponHand, HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            weaponUid = entManager.SpawnEntity(Weapon, coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, weaponUid, WeaponHand));
            handsSystem.TrySetActiveHand(npcUid, WeaponHand); // false if it already is
            Assert.That(entManager.System<SharedWieldableSystem>().TryWield((weaponUid, entManager.GetComponent<WieldableComponent>(weaponUid)), npcUid),
                "the weapon should be wielded");

            grenadeUid = entManager.SpawnEntity(Grenade, coordinates);
            Assert.That(entManager.System<InventorySystem>().TryEquip(npcUid, grenadeUid, "pocket1", silent: true, force: true),
                "the grenade should be in a pocket");
        });

        // Wielding queues the deletion of what it replaces at the end of the tick.
        await Pair.RunTicksSync(1);

        return (entManager, npcUid, weaponUid, grenadeUid);
    }
}
