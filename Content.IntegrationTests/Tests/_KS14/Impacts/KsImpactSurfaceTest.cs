#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Impacts;
using Content.Shared._KS14.Particles;
using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Impacts;

/// <summary>
///     TGMC's bullet impacts: a bullet hitting a surface puffs smoke and throws that surface's debris back at the
///         shooter, once, on every client and only there; energy throws no debris and mobs get none of it.
/// </summary>
[TestOf(typeof(KsImpactSurfaceSystem))]
public sealed class KsImpactSurfaceTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestGun = "KsImpactTestGun";
    private const string TestWall = "KsImpactTestWall";

    private readonly EntProtoId _testWall = TestWall;
    private readonly EntProtoId _testMob = "KsImpactTestMob";
    private readonly EntProtoId _testShelf = "KsImpactTestShelf";
    private readonly EntProtoId _testBook = "KsImpactTestBook";
    private readonly ProtoId<KsParticlesPrototype> _testSmoke = "KsImpactTestSmoke";
    private readonly ProtoId<KsParticlesPrototype> _testDebris = "KsImpactTestDebris";
    private readonly ProtoId<KsImpactMaterialPrototype> _testMaterial = "KsImpactTestMaterial";
    private readonly ProtoId<KsImpactMaterialPrototype> _testPaper = "KsImpactTestPaper";
    private static readonly ProtoId<DamageTypePrototype> PiercingDamage = "Piercing";
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: ksParticles
  id: KsImpactTestSmoke
  burst: 1
  count: 1
  spawning: 0
  oneShot: true
  lifespan: 5

- type: ksParticles
  parent: KsImpactTestSmoke
  id: KsImpactTestDebris

- type: ksParticles
  parent: KsImpactTestSmoke
  id: KsImpactTestPaperDebris

- type: ksImpactMaterial
  id: KsImpactTestMaterial
  smoke: KsImpactTestSmoke
  debris: KsImpactTestDebris

- type: ksImpactMaterial
  id: KsImpactTestPaper
  smoke: KsImpactTestSmoke
  debris: KsImpactTestPaperDebris

- type: entity
  id: {TestGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 10
    count: 10

- type: entity
  id: {TestWall}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        hard: true
        layer:
        - WallLayer
        mask:
        - FullTileMask
  - type: Damageable
  - type: KsImpactSurface
    material: KsImpactTestMaterial

- type: entity
  id: KsImpactTestMob
  components:
  - type: MobState
  - type: Damageable

- type: entity
  id: KsImpactTestShelf
  components:
  - type: Storage
    grid:
    - 0,0,1,1
  - type: ContainerContainer
    containers:
      storagebase: !type:Container
  - type: KsImpactSurface
    material: KsImpactTestMaterial
  - type: KsImpactSurfaceStorageFill
    material: KsImpactTestPaper

- type: entity
  id: KsImpactTestBook
  components:
  - type: Item
    size: Tiny
";

    private List<(EntityUid Uid, ProtoId<KsParticlesPrototype> Prototype)> ClientEmitters(IEnumerable<ProtoId<KsParticlesPrototype>> prototypes)
    {
        var wanted = prototypes.ToHashSet();
        var emitters = new List<(EntityUid, ProtoId<KsParticlesPrototype>)>();
        var query = CEntMan.AllEntityQueryEnumerator<KsParticlesComponent>();
        while (query.MoveNext(out var uid, out var particlesComponent))
        {
            if (particlesComponent.Prototype is { } prototype && wanted.Contains(prototype))
                emitters.Add((uid, prototype));
        }

        return emitters;
    }

    private int ServerEmitterCount()
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<KsParticlesComponent>();
        while (query.MoveNext(out _, out var particlesComponent))
        {
            if (particlesComponent.Prototype == _testSmoke || particlesComponent.Prototype == _testDebris)
                count++;
        }

        return count;
    }

    /// <summary>
    ///     Where a client emitter sends its particles: its -y, turned by its parent and its own rotation.
    /// </summary>
    private Vector2 ClientTravel(EntityUid emitterUid)
    {
        var transformSystem = CEntMan.System<SharedTransformSystem>();
        var parentUid = CEntMan.GetComponent<TransformComponent>(emitterUid).ParentUid;
        var parentRotation = parentUid.IsValid() ? transformSystem.GetWorldRotation(parentUid) : Angle.Zero;
        var rotation = parentRotation + CEntMan.GetComponent<KsParticlesComponent>(emitterUid).Rotation;
        return rotation.RotateVec(new Vector2(0f, -1f));
    }

    /// <summary>
    ///     Off the 1x1 test grid, so the bullet flies through space.
    /// </summary>
    private MapCoordinates SpaceCoordinates(float x) => new(MapData.MapCoords.Position + new Vector2(x, 20f), MapData.MapId);

    /// <summary>
    ///     The shooter's own bullet hitting a wall plays the wall's smoke and debris once, client-side only, flying back
    ///         at the shooter; nothing is spawned on the server for it.
    /// </summary>
    [Test]
    public async Task ShootersHitThrowsDebrisBackOnce()
    {
        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            SEntMan.SpawnEntity(_testWall, new MapCoordinates(shooterPosition + new Vector2(6f, 0f), MapData.MapId));
        });
        await RunTicks(5);

        var gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown
        await Shoot(gunNetEntity, shooterPosition + new Vector2(40f, 0f));

        var seen = new Dictionary<EntityUid, (ProtoId<KsParticlesPrototype> Prototype, Vector2 Travel, bool ClientSide)>();
        var serverEmitters = 0;
        for (var tick = 0; tick < 60; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                foreach (var (uid, prototype) in ClientEmitters([_testSmoke, _testDebris]))
                    seen.TryAdd(uid, (prototype, ClientTravel(uid), CEntMan.IsClientSide(uid)));
            });
            await Server.WaitPost(() => serverEmitters = Math.Max(serverEmitters, ServerEmitterCount()));
        }

        Assert.Multiple(() =>
        {
            Assert.That(seen.Values.Count(e => e.Prototype == _testSmoke), Is.EqualTo(1), "the hit did not puff exactly one smoke");
            Assert.That(seen.Values.Count(e => e.Prototype == _testDebris), Is.EqualTo(1), "the hit did not throw exactly one burst of debris");
            Assert.That(seen.Values.All(e => e.ClientSide), "an impact emitter came from the server rather than being played here");
            Assert.That(serverEmitters, Is.Zero, "the server spawned an impact emitter; they are client-side only");
            foreach (var (prototype, travel, _) in seen.Values)
                Assert.That(Vector2.Dot(travel, new Vector2(-1f, 0f)), Is.GreaterThan(0.99f), $"{prototype} flies {travel}, not back at the shooter");
        });
    }

    /// <summary>
    ///     A shot nobody here predicted (an NPC's) still plays its impact on this client, once, from the server's relayed
    ///         hit.
    /// </summary>
    [Test]
    public async Task ObserverSeesTheImpactOnce()
    {
        EntityUid gunUid = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity(_testWall, SpaceCoordinates(6f));
            gunUid = SEntMan.SpawnEntity(TestGun, SpaceCoordinates(0f));
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            var gunSystem = SEntMan.System<SharedGunSystem>();
            var gun = new Entity<GunComponent>(gunUid, SEntMan.GetComponent<GunComponent>(gunUid));
            var target = SEntMan.System<SharedTransformSystem>().ToCoordinates(SpaceCoordinates(40f));
            using (SEntMan.System<KsSharedPredictedSpawnSystem>().BeginUserPrediction(null))
                Assert.That(gunSystem.AttemptShoot(gunUid, gun, target), "the server's gun did not fire");
        });

        var seen = new Dictionary<EntityUid, ProtoId<KsParticlesPrototype>>();
        for (var tick = 0; tick < 60; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                foreach (var (uid, prototype) in ClientEmitters([_testSmoke, _testDebris]))
                    seen.TryAdd(uid, prototype);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(seen.Values.Count(p => p == _testSmoke), Is.EqualTo(1), "the server's hit did not puff exactly one smoke here");
            Assert.That(seen.Values.Count(p => p == _testDebris), Is.EqualTo(1), "the server's hit did not throw exactly one burst of debris here");
        });
    }

    /// <summary>
    ///     Energy puffs smoke but throws no debris, a mob gets neither, and something with no surface still smokes.
    /// </summary>
    [Test]
    public async Task EnergyAndMobsThrowNoDebris()
    {
        NetEntity wallNetEntity = default, mobNetEntity = default;
        await Server.WaitPost(() =>
        {
            wallNetEntity = SEntMan.GetNetEntity(SEntMan.SpawnEntity(_testWall, SpaceCoordinates(6f)));
            mobNetEntity = SEntMan.GetNetEntity(SEntMan.SpawnEntity(_testMob, SpaceCoordinates(-6f)));
        });
        await RunTicks(5);

        var piercing = new DamageSpecifier();
        piercing.DamageDict[PiercingDamage] = 10;
        var heat = new DamageSpecifier();
        heat.DamageDict[HeatDamage] = 10;

        await Client.WaitAssertion(() =>
        {
            var impactSurfaceSystem = CEntMan.System<KsImpactSurfaceSystem>();
            var coordinates = new EntityCoordinates(MapData.CMapUid, Vector2.Zero);

            impactSurfaceSystem.PlayImpact(CEntMan.GetEntity(wallNetEntity), coordinates, new Vector2(1f, 0f), heat, heat);
            Assert.That(ClientEmitters([_testSmoke]), Has.Count.EqualTo(1), "an energy hit puffed no smoke");
            Assert.That(ClientEmitters([_testDebris]), Is.Empty, "an energy hit threw debris");

            impactSurfaceSystem.PlayImpact(CEntMan.GetEntity(mobNetEntity), coordinates, new Vector2(1f, 0f), piercing, piercing);
            Assert.That(ClientEmitters([_testSmoke, _testDebris]), Has.Count.EqualTo(1), "a hit on a mob spawned impact particles");
        });
    }

    /// <summary>
    ///     A shelf's chance of paper is how full it is, worked out as books go in and out on both sides: empty, it is
    ///         always the shelf's own material; full, always paper.
    /// </summary>
    [Test]
    public async Task ShelfPaperChanceFollowsItsFill()
    {
        EntityUid shelfUid = default;
        var bookUids = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            shelfUid = SEntMan.SpawnEntity(_testShelf, SpaceCoordinates(0f));
            for (var i = 0; i < 4; i++)
                bookUids.Add(SEntMan.SpawnEntity(_testBook, SpaceCoordinates(0f)));
        });
        await RunTicks(5);

        var shelfNetEntity = SEntMan.GetNetEntity(shelfUid);
        float ServerFill() => SEntMan.GetComponent<KsImpactSurfaceStorageFillComponent>(shelfUid).FillFraction;
        float ClientFill() => CEntMan.GetComponent<KsImpactSurfaceStorageFillComponent>(CEntMan.GetEntity(shelfNetEntity)).FillFraction;

        await Server.WaitAssertion(() => Assert.That(ServerFill(), Is.Zero, "an empty shelf has a chance of paper"));
        await Client.WaitAssertion(() =>
        {
            var impactSurfaceSystem = CEntMan.System<KsImpactSurfaceSystem>();
            for (var i = 0; i < 20; i++)
                Assert.That(impactSurfaceSystem.PickMaterial(CEntMan.GetEntity(shelfNetEntity)), Is.EqualTo(_testMaterial), "an empty shelf threw paper");
        });

        await Server.WaitPost(() =>
        {
            var storageSystem = SEntMan.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
            Assert.That(storageSystem.Insert(shelfUid, bookUids[0], out _, playSound: false), "the first book did not go in");
            Assert.That(storageSystem.Insert(shelfUid, bookUids[1], out _, playSound: false), "the second book did not go in");
        });
        await RunTicks(5);
        await Server.WaitAssertion(() => Assert.That(ServerFill(), Is.EqualTo(0.5f).Within(0.001f), "two of four slots full is not half"));
        await Client.WaitAssertion(() => Assert.That(ClientFill(), Is.EqualTo(0.5f).Within(0.001f), "the client's shelf did not follow its contents"));

        await Server.WaitPost(() =>
        {
            var storageSystem = SEntMan.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
            Assert.That(storageSystem.Insert(shelfUid, bookUids[2], out _, playSound: false));
            Assert.That(storageSystem.Insert(shelfUid, bookUids[3], out _, playSound: false));
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(ClientFill(), Is.EqualTo(1f).Within(0.001f));
            var impactSurfaceSystem = CEntMan.System<KsImpactSurfaceSystem>();
            for (var i = 0; i < 20; i++)
                Assert.That(impactSurfaceSystem.PickMaterial(CEntMan.GetEntity(shelfNetEntity)), Is.EqualTo(_testPaper), "a full shelf threw wood");
        });

        await Server.WaitPost(() =>
        {
            var containerSystem = SEntMan.System<Robust.Shared.Containers.SharedContainerSystem>();
            foreach (var bookUid in bookUids)
                containerSystem.RemoveEntity(shelfUid, bookUid);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() => Assert.That(ServerFill(), Is.Zero, "emptying the shelf left a chance of paper"));
        await Client.WaitAssertion(() => Assert.That(ClientFill(), Is.Zero, "emptying the client's shelf left a chance of paper"));
    }

    private async Task Shoot(NetEntity gunNetEntity, Vector2 worldTarget)
    {
        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, worldTarget));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = targetCoordinates,
            });
        });
    }
}
