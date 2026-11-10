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
    private const string Friend = "KsSuppressionTestFriend";
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
  - type: NpcPerception
  - type: NpcFactionMember
    factions:
    - Syndicate

- type: entity
  id: KsSuppressionTestFriend
  components:
  - type: MobState
  - type: NpcFactionMember
    factions:
    - Syndicate

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

    /// <summary>
    ///     A friend about it and the spot, which is where a hostile went. Round a friend in the way, anywhere the hostile could
    ///         be does as well as the spot, as a squad holding a room keeps its lines off each other; with no hostile, only the
    ///         spot itself does, as for a window to break. With nowhere clear, or the friend on top of it, it holds fire. A
    ///         friend just beside or behind it stops nothing: a round only starts inside one that is.
    /// </summary>
    [TestCase(2.5f, 0f, true, true)] // in the way, room to fire round it
    [TestCase(2.5f, 0f, false, false)] // in the way, and it has to be the very spot
    [TestCase(0.1f, 0f, true, false)] // on top of it: a round from there starts inside the friend, whichever way it goes
    [TestCase(0f, 0.55f, false, true)] // just beside
    [TestCase(-0.8f, 0f, false, true)] // just behind
    [TestCase(0f, 3f, false, true)] // control: off to the side
    public async Task TestFiresRoundAFriendOrNotAtAll(float friendX, float friendY, bool hostile, bool fires)
    {
        var (entManager, npcUid, gunUid) = await SetUp();
        var suppressiveFireSystem = entManager.System<NpcSuppressiveFireSystem>();
        var ammoAtStart = 0;

        await Pair.Server.WaitPost(() =>
        {
            var npcCoordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;
            entManager.SpawnEntity(Friend, npcCoordinates.Offset(new Vector2(friendX, friendY)));
            ammoAtStart = GetAmmo(entManager, gunUid);

            var spot = npcCoordinates.Offset(new Vector2(4f, 0f));
            suppressiveFireSystem.Start(npcUid, spot, System.TimeSpan.FromSeconds(0.5), hostile ? ReportHostile(entManager, npcUid, spot) : null);
        });

        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(suppressiveFireSystem.IsFiring(npcUid), "the test needs it still at it");

            if (fires)
                Assert.That(GetAmmo(entManager, gunUid), Is.LessThan(ammoAtStart), "it should fire, clear of its friend");
            else
                Assert.That(GetAmmo(entManager, gunUid), Is.EqualTo(ammoAtStart), "it should hold fire, with its friend in every way");
        });
    }

    /// <summary>
    ///     The spot just round a corner, friends in the way of it and of the open side: a round into the corner wall, beside
    ///         where the hostile went, keeps its head down as well, and is taken. It used to have to reach as far as the spot.
    /// </summary>
    [Test]
    public async Task TestARoundIntoTheCornerBesideTheSpotCounts()
    {
        var (entManager, npcUid, gunUid) = await SetUp();
        var suppressiveFireSystem = entManager.System<NpcSuppressiveFireSystem>();
        var ammoAtStart = 0;

        await Pair.Server.WaitPost(() =>
        {
            var npcCoordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;
            var gridUid = npcCoordinates.EntityId;

            for (var y = 1; y <= 4; y++)
            {
                SpawnAt(entManager, "WallSolid", gridUid, 4, y);
            }

            entManager.SpawnEntity(Friend, npcCoordinates.Offset(new Vector2(3f, 0f)));
            entManager.SpawnEntity(Friend, npcCoordinates.Offset(new Vector2(3f, -0.8f)));
            ammoAtStart = GetAmmo(entManager, gunUid);

            var spot = npcCoordinates.Offset(new Vector2(4f, 0f));
            suppressiveFireSystem.Start(npcUid, spot, System.TimeSpan.FromSeconds(0.5), ReportHostile(entManager, npcUid, spot));
        });

        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(GetAmmo(entManager, gunUid), Is.LessThan(ammoAtStart), "it should fire into the corner"));
    }

    /// <summary>
    ///     A hostile, out of sight, that a squadmate just called out at <paramref name="spot"/>.
    /// </summary>
    private static EntityUid ReportHostile(IEntityManager entManager, EntityUid npcUid, EntityCoordinates spot)
    {
        var hostileUid = entManager.SpawnEntity(NanoTrasenMob, spot.Offset(new Vector2(0f, -4f)));
        var ev = new Content.Server._KS14.NPC.Perception.NpcContactCalloutEvent(npcUid, hostileUid, spot);
        entManager.EventBus.RaiseLocalEvent(npcUid, ref ev);
        return hostileUid;
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
