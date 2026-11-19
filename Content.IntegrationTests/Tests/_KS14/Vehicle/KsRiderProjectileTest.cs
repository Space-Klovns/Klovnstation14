#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.Vehicle;
using Content.Shared.Buckle;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Vehicle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     A round spawns at its shooter, so a rider's round starts inside the vehicle they ride. It passes through that
///         vehicle (<see cref="KsRiderProjectileSystem"/>), and only that one.
/// </summary>
[TestOf(typeof(KsRiderProjectileSystem))]
public sealed class KsRiderProjectileTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private readonly EntProtoId _vehicle = "KsRiderProjectileTestVehicle";
    private readonly EntProtoId _shooter = "KsRiderProjectileTestShooter";
    private static readonly EntProtoId Rider = "MobHuman";
    private static readonly EntProtoId Bullet = "BulletPistol";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsRiderProjectileTestVehicle
  components:
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.4
        density: 100
        mask:
        - MobMask
        layer:
        - BulletImpassable
  - type: Damageable
  - type: Strap
  - type: Vehicle
    transferDamage: false
  - type: StrapVehicle

- type: entity
  id: KsRiderProjectileTestShooter
";

    [Test]
    public async Task TestARidersRoundPassesThroughTheirVehicle()
    {
        var entManager = Server.EntMan;
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, vehicleUid = default, riderUid = default, bulletUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-3, -3), new Vector2i(3, 8)).Owner;
            vehicleUid = SpawnAt(entManager, _vehicle, gridUid, 0, 0);
            riderUid = SpawnAt(entManager, Rider, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<SharedBuckleSystem>().TryBuckle(riderUid, riderUid, vehicleUid), Is.True);
            Assert.That(entManager.GetComponent<VehicleComponent>(vehicleUid).Operator, Is.EqualTo(riderUid),
                "buckling in should make the rider the operator");

            bulletUid = entManager.SpawnAtPosition(Bullet, entManager.GetComponent<TransformComponent>(riderUid).Coordinates);
            entManager.System<GunSystem>().ShootProjectile(bulletUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: riderUid);
        });

        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsDamaged(entManager, vehicleUid), Is.False,
                "the rider's round should pass through the vehicle they ride");
            Assert.That(entManager.Deleted(bulletUid) ||
                        entManager.GetComponent<TransformComponent>(bulletUid).LocalPosition.Y > 2f, Is.True,
                "and fly on, not stop inside it");
        });
    }

    [Test]
    public async Task TestSomeoneElsesRoundStillHitsTheVehicle()
    {
        var entManager = Server.EntMan;
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, vehicleUid = default, riderUid = default, shooterUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-3, -8), new Vector2i(3, 3)).Owner;
            vehicleUid = SpawnAt(entManager, _vehicle, gridUid, 0, 0);
            riderUid = SpawnAt(entManager, Rider, gridUid, 0, 0);
            shooterUid = SpawnAt(entManager, _shooter, gridUid, 0, -6);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<SharedBuckleSystem>().TryBuckle(riderUid, riderUid, vehicleUid), Is.True);

            var bulletUid = entManager.SpawnAtPosition(Bullet, new EntityCoordinates(gridUid, new Vector2(0.5f, -3.5f)));
            entManager.System<GunSystem>().ShootProjectile(bulletUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: shooterUid);
        });

        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            // The rider is buckled on top and never in the way: the vehicle is what the round meets.
            Assert.That(IsDamaged(entManager, vehicleUid), Is.True,
                "a round from anyone else should still hit the vehicle");
        });
    }

    private static bool IsDamaged(IEntityManager entManager, EntityUid uid)
    {
        var damageable = entManager.GetComponent<DamageableComponent>(uid);
        return entManager.System<DamageableSystem>().GetPositiveDamage((uid, damageable)).AnyPositive();
    }
}
