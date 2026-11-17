#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Grenades;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A cooked grenade that leaves the hand some way other than the throw (a stun drops what is held, a shove knocks it
///         out) is no longer the throw the NPC meant: it is not coming down where that was aimed, nobody goes in on that
///         spot when it goes off, and the thrower's hands are put back as they were. See <c>NpcGrenadeSystem.EndCook</c>.
/// </summary>
public sealed class KsNpcGrenadeCookDroppedTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Cook = "KsCookDroppedTestCook";
    private const string Frag = "KsCookDroppedTestFrag";
    private const string Weapon = "KsCookDroppedTestWeapon";

    private const string WeaponHand = "right";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCookDroppedTestCook
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: MobState
  - type: NpcGrenadeThrower
    cooks: true
    cookMargin: 0.45
    cookSpread: 0

- type: entity
  id: KsCookDroppedTestWeapon
  components:
  - type: Item
    size: Large
  - type: Wieldable

# A frag that never really explodes: it deletes itself instead.
- type: entity
  parent: BaseItem
  id: KsCookDroppedTestFrag
  components:
  - type: Item
    size: Small
  - type: TimerTrigger
    delay: 3
  - type: Explosive
    explosionType: Default
    maxIntensity: 1
    intensitySlope: 1
    totalIntensity: 1
  - type: DeleteOnTrigger
    keysIn:
    - timer
";

    /// <summary>
    ///     A hold cut short while the thrower is still inside the wait between throws (it threw something a moment ago)
    ///         throws the grenade all the same, rather than drop it lit at its own feet.
    /// </summary>
    [Test]
    public async Task TestCookCutShortInsideTheThrowCooldownIsStillThrown()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var timing = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, grenadeUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -4), new Vector2i(12, 10)).Owner;
            npcUid = SpawnAt(entManager, Cook, gridUid, -3, 0);
            handsSystem.AddHand(npcUid, WeaponHand, HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            grenadeUid = entManager.SpawnAtPosition(Frag, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(entManager.System<InventorySystem>().TryEquip(npcUid, grenadeUid, "pocket1", silent: true, force: true),
                "the grenade should be in a pocket");
        });

        await Pair.RunTicksSync(1);

        await Server.WaitAssertion(() =>
        {
            var aim = entManager.GetComponent<TransformComponent>(npcUid).Coordinates.Offset(new Vector2(6f, 0f));
            Assert.That(grenadeSystem.TryBeginThrow(npcUid, grenadeUid, aim), Is.EqualTo(NpcGrenadeThrowResult.Cooking));

            // As if it had only just thrown something else.
            entManager.GetComponent<HandsComponent>(npcUid).NextThrowTime = timing.CurTime + System.TimeSpan.FromHours(1);
            grenadeSystem.EndCook(npcUid);

            Assert.Multiple(() =>
            {
                Assert.That(handsSystem.IsHolding(npcUid, grenadeUid), Is.False, "the grenade should be out of the hand");
                Assert.That(entManager.HasComponent<Content.Shared.Throwing.ThrownItemComponent>(grenadeUid),
                    "thrown, not dropped at its feet: ending a hold does not wait for the throw cooldown");
            });
        });
    }

    [Test]
    public async Task TestGrenadeDroppedWhileCookedIsNoLongerTheThrow()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, weaponUid = default, grenadeUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -4), new Vector2i(12, 10)).Owner;
            npcUid = SpawnAt(entManager, Cook, gridUid, -3, 0);
            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;

            handsSystem.AddHand(npcUid, WeaponHand, HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            weaponUid = entManager.SpawnAtPosition(Weapon, coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, weaponUid, WeaponHand));
            handsSystem.TrySetActiveHand(npcUid, WeaponHand);
            Assert.That(entManager.System<SharedWieldableSystem>().TryWield((weaponUid, entManager.GetComponent<WieldableComponent>(weaponUid)), npcUid),
                "the weapon should be wielded to begin with");

            grenadeUid = entManager.SpawnAtPosition(Frag, coordinates);
            Assert.That(entManager.System<InventorySystem>().TryEquip(npcUid, grenadeUid, "pocket1", silent: true, force: true),
                "the grenade should be in a pocket");
        });

        await Pair.RunTicksSync(1);

        await Server.WaitAssertion(() =>
        {
            var aim = entManager.GetComponent<TransformComponent>(npcUid).Coordinates.Offset(new Vector2(6f, 0f));
            Assert.That(grenadeSystem.TryBeginThrow(npcUid, grenadeUid, aim), Is.EqualTo(NpcGrenadeThrowResult.Cooking));
            Assert.That(grenadeSystem.TryGetLanding(grenadeUid, out _), "held to be thrown, it is coming down where it is aimed");
            Assert.That(handsSystem.GetActiveItem(npcUid), Is.EqualTo(grenadeUid), "the grenade is in the active hand while it is held");
        });

        await Pair.RunTicksSync(3);

        // Out of the hand, and not by the throw: what a stun does to anything held.
        await Server.WaitAssertion(() =>
        {
            grenadeSystem.RenewCook(npcUid);
            Assert.That(handsSystem.TryDrop(npcUid, grenadeUid, checkActionBlocker: false), "the grenade should drop");
        });

        // The system sees it gone from the hand on its next update.
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(grenadeSystem.IsCooking(npcUid), Is.False, "the hold should be over");
                Assert.That(entManager.HasComponent<NpcThrownGrenadeComponent>(grenadeUid), Is.False,
                    "dropped, it is not the throw the NPC made: nobody goes in on where that was aimed when it goes off");
                Assert.That(grenadeSystem.TryGetLanding(grenadeUid, out _), Is.False, "nor is it coming down there");
                Assert.That(handsSystem.GetActiveItem(npcUid), Is.EqualTo(weaponUid), "the weapon should be back in the active hand");
                Assert.That(entManager.GetComponent<WieldableComponent>(weaponUid).Wielded, "and wielded again");
            });
        });
    }
}
