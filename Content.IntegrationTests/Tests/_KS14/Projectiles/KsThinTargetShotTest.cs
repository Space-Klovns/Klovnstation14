#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Light.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

/// <summary>
///     A round aimed at a wall light puts it out, and one aimed at someone standing there hits them, whatever the range. The
///         light's fixture is 0.15 of a tile deep and a round moves more than a tile a tick: without looking ahead
///         (<c>KsRayCollisionSystem</c>, which every round goes through) it stepped over it at some ranges, and on a wall it
///         stepped onto the light and the wall at once, and the wall could take the hit. Someone standing is hit as ever, and
///         a projectile shot again (an arrow pulled out) can hit what it hit the first time.
/// </summary>
public sealed class KsThinTargetShotTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsThinTargetTestShooter
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: CombatMode
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 185
        mask:
        - MobMask
        layer:
        - MobLayer

- type: entity
  id: KsThinTargetTestGun
  components:
  - type: Item
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
  - type: BallisticAmmoProvider
    proto: CartridgePistol
    capacity: 10

- type: entity
  id: KsThinTargetTestDart
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      projectile:
        shape:
          !type:PhysShapeAabb
          bounds: -0.1,-0.1,0.1,0.1
        hard: false
        mask:
        - Impassable
        - BulletImpassable
  - type: Projectile
    deleteOnCollide: false
    damage:
      types:
        Blunt: 5

- type: entity
  parent: Poweredlight
  id: KsThinTargetTestLight
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <param name="range">How far south of the light the shooter stands, in tiles: each lands the round's steps differently.</param>
    /// <param name="wall">Whether the light is on a wall, as on a station, or stands alone.</param>
    [Test]
    public async Task TestOneRoundPutsALightOut([Range(2, 9)] int range, [Values] bool wall)
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, gunUid = default, lightUid = default, gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -12), new Vector2i(3, 4)).Owner;

            // Its fixture is along the top of its tile, where the wall above it is.
            lightUid = SpawnAt(entManager, "KsThinTargetTestLight", gridUid, 0, 0);
            entManager.System<SharedPowerReceiverSystem>().SetNeedsPower(lightUid, false);

            if (wall)
                SpawnAt(entManager, "WallSolid", gridUid, 0, 1);

            shooterUid = SpawnAt(entManager, "KsThinTargetTestShooter", gridUid, 0, -range);
            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(shooterUid, "right", HandLocation.Right);
            gunUid = entManager.SpawnAtPosition("KsThinTargetTestGun", entManager.GetComponent<TransformComponent>(shooterUid).Coordinates);
            Assert.That(handsSystem.TryPickup(shooterUid, gunUid, "right"));
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<PoweredLightComponent>(lightUid).CurrentLit, "the test light should be lit");

            var gunSystem = entManager.System<GunSystem>();
            Assert.That(gunSystem.TryGetGun(shooterUid, out var gun));
            gunSystem.AttemptShoot(shooterUid,
                gun,
                new EntityCoordinates(gridUid, new Vector2(0.5f, 0.925f)), // the middle of the fixture
                lightUid);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<PoweredLightComponent>(lightUid).CurrentLit, Is.False, "one round should put it out"));
    }

    /// <param name="range">How far south of the mob the shooter stands, in tiles.</param>
    [Test]
    public async Task TestOneRoundHitsSomeoneStanding([Range(2, 9)] int range)
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, gunUid = default, mobUid = default, gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -12), new Vector2i(3, 4)).Owner;
            mobUid = SpawnAt(entManager, "MobHuman", gridUid, 0, 0);
            // The test map has no air: nothing but the round may hurt them, or the vacuum passes this test by itself.
            entManager.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(mobUid);
            entManager.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(mobUid);

            shooterUid = SpawnAt(entManager, "KsThinTargetTestShooter", gridUid, 0, -range);
            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(shooterUid, "right", HandLocation.Right);
            gunUid = entManager.SpawnAtPosition("KsThinTargetTestGun", entManager.GetComponent<TransformComponent>(shooterUid).Coordinates);
            Assert.That(handsSystem.TryPickup(shooterUid, gunUid, "right"));
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
#pragma warning disable CS0618 // a hit at all is what is asked, not how much
            Assert.That(entManager.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(mobUid).Float(), Is.Zero,
                "unhurt before the shot: whatever hurts them after is the round");
#pragma warning restore CS0618

            var gunSystem = entManager.System<GunSystem>();
            Assert.That(gunSystem.TryGetGun(shooterUid, out var gun));
            gunSystem.AttemptShoot(shooterUid, gun, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)), mobUid);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
#pragma warning disable CS0618 // a hit at all is what is asked, not how much
            Assert.That(entManager.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(mobUid).Float(), Is.GreaterThan(0f),
                "one round should hit them"));
#pragma warning restore CS0618
    }

    /// <summary>
    ///     A projectile that is not used up by a hit, shot at someone, stopped, and shot at them again, hits them both times:
    ///         what looking ahead had it hit is forgotten when it stops, as an arrow does in whoever it hit, and is not
    ///         passed through on the next shot.
    /// </summary>
    [Test]
    public async Task TestProjectileShotAgainHitsAgain()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, dartUid = default, mobUid = default, gridUid = default;
        var start = new Vector2(0.5f, -3.5f);
        var damageBefore = 0f;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -12), new Vector2i(3, 4)).Owner;
            mobUid = SpawnAt(entManager, "MobHuman", gridUid, 0, 0);
            // The test map has no air: nothing but the dart may hurt them.
            entManager.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(mobUid);
            entManager.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(mobUid);
            shooterUid = SpawnAt(entManager, "KsThinTargetTestShooter", gridUid, 0, -10);
            dartUid = entManager.SpawnAtPosition("KsThinTargetTestDart", new EntityCoordinates(gridUid, start));
        });

        await Pair.RunTicksSync(10);

        for (var shot = 1; shot <= 2; shot++)
        {
            await Server.WaitPost(() =>
            {
                var physicsSystem = entManager.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
                physicsSystem.SetLinearVelocity(dartUid, Vector2.Zero);
                entManager.System<SharedTransformSystem>().SetCoordinates(dartUid, new EntityCoordinates(gridUid, start));
                // As pulling an embedded one out does (SharedProjectileSystem).
                entManager.GetComponent<Content.Shared.Projectiles.ProjectileComponent>(dartUid).ProjectileSpent = false;
            });

            // At rest a moment, as one stuck in someone is.
            await Pair.RunTicksSync(5);

            await Server.WaitPost(() =>
                entManager.System<GunSystem>().ShootProjectile(dartUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: shooterUid));

            await Pair.RunTicksSync(30);

            var current = shot;
            await Server.WaitAssertion(() =>
            {
#pragma warning disable CS0618 // a hit at all is what is asked, not how much
                var damage = entManager.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(mobUid).Float();
#pragma warning restore CS0618
                Assert.That(damage, Is.GreaterThan(damageBefore), $"shot {current} should hit them");
                damageBefore = damage;
            });
        }
    }
}
