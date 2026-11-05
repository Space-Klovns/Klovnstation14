#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Grenades;
using Content.Server._KS14.NPC.Queries.Considerations;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Curves;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
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
    private const string PushingNpc = "KsGrenadeTestPushingNpc";
    private const string QuietNpc = "KsGrenadeTestQuietNpc";
    private const string Grenade = "KsGrenadeTestGrenade";
    private const string ShortGrenade = "KsGrenadeTestShortGrenade";
    private const string Weapon = "KsGrenadeTestWeapon";

    private const string WeaponHand = "right";

    /// <summary>
    ///     Long enough for squads to form.
    /// </summary>
    private const int SquadUpdateTicks = 90;

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
  parent: KsSquadTestMobSyndicate
  id: KsGrenadeTestPushingNpc
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: NpcGrenadeThrower
  - type: NpcGrenadePusher
    duration: 1

- type: entity
  parent: KsGrenadeTestPushingNpc
  id: KsGrenadeTestQuietNpc
  components:
  - type: NpcGrenadeThrower
    callsOutBlasts: false

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

- type: entity
  parent: KsGrenadeTestGrenade
  id: KsGrenadeTestShortGrenade
  components:
  - type: FlashOnTrigger
    keysIn:
    - timer
    range: 1
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
    ///     A grenade leaves from the thrower's feet. While in the air it counts from where it is coming down, so nobody runs
    ///         from their own throw, and whoever is under it knows. Once down it counts from where it really is, which is
    ///         what keeps one that fell short from being ignored.
    /// </summary>
    [Test]
    public async Task TestGrenadeInTheAirCountsFromWhereItIsComingDown()
    {
        var (entManager, npcUid, _, grenadeUid) = await SetUp(grenade: ShortGrenade);
        var danger = new TargetDangerRangeCon { Margin = 0f, Curve = new BoolCurve() };
        var blackboard = new NPCBlackboard();
        EntityUid underItUid = default;
        EntityCoordinates coordinates = default;

        await Pair.Server.WaitPost(() =>
        {
            danger.Initialise(entManager.EntitySysManager.DependencyCollection);
            var gridUid = entManager.GetComponent<TransformComponent>(npcUid).ParentUid;
            underItUid = SpawnAt(entManager, Npc, gridUid, 4, 0);
            coordinates = entManager.GetComponent<TransformComponent>(underItUid).Coordinates;
        });

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcGrenadeSystem>().TryThrow(npcUid, grenadeUid, coordinates), "the grenade should be thrown");

            Assert.Multiple(() =>
            {
                Assert.That(danger.GetScore(blackboard, npcUid, grenadeUid), Is.Zero, "the thrower is nowhere near where it is going");
                Assert.That(danger.GetScore(blackboard, underItUid, grenadeUid), Is.EqualTo(1f), "and whoever is under it should move");
            });
        });

        var flying = true;
        for (var i = 0; i < 300 && flying; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => flying = entManager.HasComponent<ThrownItemComponent>(grenadeUid));
        }

        await Pair.Server.WaitAssertion(() =>
        {
            var transformSystem = entManager.System<SharedTransformSystem>();
            var thrower = transformSystem.GetWorldPosition(npcUid);
            transformSystem.SetWorldPosition(grenadeUid, thrower);

            Assert.That(flying, Is.False, "it should have come down");
            Assert.That(danger.GetScore(blackboard, npcUid, grenadeUid), Is.EqualTo(1f), "one that is down counts from where it is, wherever it was meant to go");
        });
    }

    /// <summary>
    ///     Nobody goes anywhere while the fuse burns. When it goes off, the thrower goes in on where it threw it, and so do
    ///         the squadmates it tells that do that, until the time runs out.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestBlastSendsThemIn(bool tellsSquad)
    {
        var (entManager, npcUid, _, grenadeUid) = await SetUp(tellsSquad ? PushingNpc : QuietNpc);
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();
        EntityUid pusherUid = default;
        EntityUid bystanderUid = default;
        var coordinates = default(EntityCoordinates);

        await Pair.Server.WaitPost(() =>
        {
            var gridUid = entManager.GetComponent<TransformComponent>(npcUid).ParentUid;
            pusherUid = SpawnAt(entManager, PushingNpc, gridUid, 2, 0);
            bystanderUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 2);
            coordinates = new EntityCoordinates(gridUid, new Vector2(4.5f, 4.5f));
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().GetSquadOrSelf(pusherUid), Is.EqualTo(entManager.System<NpcSquadSystem>().GetSquadOrSelf(npcUid)),
                "they should be in one squad");
            Assert.That(grenadeSystem.TryThrow(npcUid, grenadeUid, coordinates), "the grenade should be thrown");
            Assert.That(grenadeSystem.TryGetPush(npcUid, out _), Is.False, "nobody goes in while the fuse burns");

            entManager.System<TriggerSystem>().Trigger(grenadeUid);

            Assert.Multiple(() =>
            {
                Assert.That(grenadeSystem.TryGetPush(npcUid, out var thrownAt) && thrownAt == coordinates, "the thrower goes in, on where it threw it");
                Assert.That(grenadeSystem.TryGetPush(pusherUid, out var told) && told == coordinates, Is.EqualTo(tellsSquad), "and so does a squadmate it told");
                Assert.That(grenadeSystem.TryGetPush(bystanderUid, out _), Is.False, "but not one that does not do that");
            });
        });

        var duration = entManager.GetComponent<NpcGrenadePusherComponent>(npcUid).Duration;
        await Pair.RunTicksSync((int) (duration.TotalSeconds * timing.TickRate) + 1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(grenadeSystem.TryGetPush(npcUid, out _), Is.False, "too long after the blast to be worth it"));
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
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid WeaponUid, EntityUid GrenadeUid)> SetUp(string npcPrototype = Npc, string grenade = Grenade)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, weaponUid = default, grenadeUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;
            npcUid = SpawnAt(entManager, npcPrototype, gridUid, 0, 0);
            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(npcUid, WeaponHand, HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            weaponUid = entManager.SpawnEntity(Weapon, coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, weaponUid, WeaponHand));
            handsSystem.TrySetActiveHand(npcUid, WeaponHand); // false if it already is
            Assert.That(entManager.System<SharedWieldableSystem>().TryWield((weaponUid, entManager.GetComponent<WieldableComponent>(weaponUid)), npcUid),
                "the weapon should be wielded");

            grenadeUid = entManager.SpawnEntity(grenade, coordinates);
            Assert.That(entManager.System<InventorySystem>().TryEquip(npcUid, grenadeUid, "pocket1", silent: true, force: true),
                "the grenade should be in a pocket");
        });

        // Wielding queues the deletion of what it replaces at the end of the tick.
        await Pair.RunTicksSync(1);

        return (entManager, npcUid, weaponUid, grenadeUid);
    }
}
