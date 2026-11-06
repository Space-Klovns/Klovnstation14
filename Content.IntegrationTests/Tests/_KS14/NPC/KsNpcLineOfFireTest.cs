#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Whether an NPC could hit something goes by what its gun's next shot collides with, not by what it can see or by what
///         would stop a hand. Glass is the case that matters: it stops a bullet, and lets through lasers and some bolts.
/// </summary>
public sealed class KsNpcLineOfFireTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Rifle = "KsLineOfFireTestRifle";
    private const string Laser = "KsLineOfFireTestLaser";
    private const string Bolter = "KsLineOfFireTestBolter";
    private const string Glass = "KsLineOfFireTestGlass";
    private const string Wall = "KsLineOfFireTestWall";

    /// <summary>
    ///     Longer than ranged combat takes to look again.
    /// </summary>
    private const int LookTicks = 30;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsLineOfFireTestBullet
  components:
  - type: Physics
  - type: Fixtures
    fixtures:
      projectile:
        shape: !type:PhysShapeCircle
          radius: 0.1
        mask:
        - Impassable
        - BulletImpassable

- type: entity
  id: KsLineOfFireTestCartridge
  components:
  - type: CartridgeAmmo
    proto: KsLineOfFireTestBullet

- type: entity
  id: KsLineOfFireTestEnergyBolt
  components:
  - type: Physics
  - type: Fixtures
    fixtures:
      projectile:
        shape: !type:PhysShapeCircle
          radius: 0.1
        mask:
        - Opaque

- type: entity
  id: KsLineOfFireTestBeam
  components:
  - type: HitscanBasicRaycast

- type: entity
  id: KsLineOfFireTestRifle
  components:
  - type: Item
  - type: Gun
  - type: BallisticAmmoProvider
    proto: KsLineOfFireTestCartridge
    capacity: 10

- type: entity
  id: KsLineOfFireTestLaser
  components:
  - type: Item
  - type: Gun
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
  - type: BatteryAmmoProvider
    proto: KsLineOfFireTestBeam

- type: entity
  id: KsLineOfFireTestBolter
  components:
  - type: Item
  - type: Gun
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
  - type: BatteryAmmoProvider
    proto: KsLineOfFireTestEnergyBolt

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsLineOfFireTestShooter
  components:
  - type: Hands
  - type: CombatMode

- type: entity
  parent: KsLineOfFireTestShooter
  id: KsLineOfFireTestWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier

- type: entity
  id: KsLineOfFireTestGlass
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: -0.5,-0.5,0.5,0.5
        layer:
        - GlassLayer

- type: entity
  id: KsLineOfFireTestWall
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: -0.5,-0.5,0.5,0.5
        layer:
        - WallLayer
";

    /// <summary>
    ///     What is fired, by way of what it is fired from: rounds in a cartridge, or a battery's beam or bolt.
    /// </summary>
    [TestCase(Rifle, CollisionGroup.Impassable | CollisionGroup.BulletImpassable)]
    [TestCase(Laser, CollisionGroup.Opaque)]
    [TestCase(Bolter, CollisionGroup.Opaque)]
    public async Task TestShotMaskIsWhatTheShotCollidesWith(string gun, CollisionGroup shotMask)
    {
        var (entManager, shooterUid, _) = await SetUp(gun);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(entManager.System<NpcLineOfFireSystem>().GetShotMask(shooterUid), Is.EqualTo(shotMask)));
    }

    /// <summary>
    ///     Glass stops a bullet and nothing else. A wall stops them all, and with nothing in the way, nothing stops any.
    /// </summary>
    [TestCase(Rifle, Glass, false)]
    [TestCase(Laser, Glass, true)]
    [TestCase(Bolter, Glass, true)]
    [TestCase(Rifle, Wall, false)]
    [TestCase(Laser, Wall, false)]
    [TestCase(Bolter, Wall, false)]
    [TestCase(Rifle, null, true)]
    public async Task TestWhatStopsAShot(string gun, string? barrier, bool clear)
    {
        var (entManager, shooterUid, targetUid) = await SetUp(gun, barrier);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(entManager.System<NpcLineOfFireSystem>().HasLineOfFire(shooterUid, targetUid, 16f), Is.EqualTo(clear)));
    }

    /// <summary>
    ///     A mob in the way is not a barrier: it may be gone by the time the shot gets there, and a squadmate is shot past.
    /// </summary>
    [Test]
    public async Task TestAMobInTheWayIsNoBarrier()
    {
        var (entManager, shooterUid, targetUid) = await SetUp(Rifle, "KsLineOfFireTestShooter");

        await Pair.Server.WaitAssertion(() =>
            Assert.That(entManager.System<NpcLineOfFireSystem>().HasLineOfFire(shooterUid, targetUid, 16f)));
    }

    /// <summary>
    ///     Live: the check that decides whether it fires. One that can shoot through the glass is told so, and one that cannot
    ///         is told that it cannot.
    /// </summary>
    [TestCase(Rifle, false)]
    [TestCase(Laser, true)]
    public async Task TestRangedCombatGoesByTheGun(string gun, bool inLineOfFire)
    {
        var (entManager, shooterUid, targetUid) = await SetUp(gun, Glass);

        await Pair.Server.WaitPost(() =>
        {
            var rangedComponent = entManager.EnsureComponent<NPCRangedCombatComponent>(shooterUid);
            rangedComponent.Target = targetUid;
        });

        await Pair.RunTicksSync(LookTicks);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<NPCRangedCombatComponent>(shooterUid).TargetInLOS, Is.EqualTo(inLineOfFire)));
    }

    /// <summary>
    ///     Live, for every NPC on the shared gun tasks and not only operatives: sealed off behind glass that stops its gun,
    ///         there is no shot to wait for and nowhere to walk to get one. With a gun the glass does not stop, it fires.
    /// </summary>
    [TestCase(Rifle, false)]
    [TestCase(Laser, true)]
    public async Task TestOrdinaryNpcBehindGlass(string gun, bool shoots)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, gunUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;

            for (var y = -6; y <= 6; y++)
            {
                SpawnAt(entManager, Glass, gridUid, 2, y);
            }

            npcUid = SpawnAt(entManager, "KsLineOfFireTestWalker", gridUid, 0, 0);
            SpawnAt(entManager, NanoTrasenMob, gridUid, 4, 0);

            handsSystem.AddHand(npcUid, "right", HandLocation.Right);
            gunUid = SpawnAt(entManager, gun, gridUid, 0, 0);
            Assert.That(handsSystem.TryPickup(npcUid, gunUid, "right"), "the test needs it holding the gun");
        });

        await Pair.RunTicksSync(90);

        var ammoAtFirst = 0;

        await server.WaitPost(() =>
        {
            ammoAtFirst = GetAmmo(entManager, gunUid);

            var htnComponent = entManager.GetComponent<HTNComponent>(npcUid);
            htnComponent.Blackboard.SetValue("VisionRadius", 17.5f);
            htnComponent.Blackboard.SetValue("AggroVisionRadius", 25f);
            htnComponent.Blackboard.SetValue("RangedRange", 9f);
            htnComponent.RootTask = new HTNCompoundTask { Task = "GunCombatCompound" };
            entManager.System<HTNSystem>().SetHTNEnabled((npcUid, htnComponent), true);
            entManager.System<NPCSystem>().WakeNPC(npcUid, htnComponent);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetAmmo(entManager, gunUid) < ammoAtFirst, Is.EqualTo(shoots));

            if (!shoots)
                Assert.That(entManager.HasComponent<NPCRangedCombatComponent>(npcUid), Is.False, "it is waiting on a shot it cannot take");
        });
    }

    private static int GetAmmo(IEntityManager entManager, EntityUid gunUid)
    {
        var ammoEvent = new GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ammoEvent);
        return ammoEvent.Count;
    }

    /// <summary>
    ///     A shooter at 0,0 holding <paramref name="gun"/>, a hostile four tiles along, and maybe something in between.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid ShooterUid, EntityUid TargetUid)> SetUp(string gun, string? barrier = null)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, targetUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;

            shooterUid = SpawnAt(entManager, "KsLineOfFireTestShooter", gridUid, 0, 0);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 4, 0);

            if (barrier != null)
                SpawnAt(entManager, barrier, gridUid, 2, 0);

            handsSystem.AddHand(shooterUid, "right", HandLocation.Right);
            var gunUid = SpawnAt(entManager, gun, gridUid, 0, 0);
            Assert.That(handsSystem.TryPickup(shooterUid, gunUid, "right"), "the test needs it holding the gun");
        });

        await Pair.RunTicksSync(2);

        return (entManager, shooterUid, targetUid);
    }
}
