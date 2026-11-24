#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Cuffs;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.Vehicle;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Vehicle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     KS14's changes to the upstream vehicle system: who may drive, what a rider's shots hit, and what damage reaches
///         the operator. Everything here runs on this file's own prototypes.
/// </summary>
[TestOf(typeof(KsRiderProjectileSystem))]
[TestOf(typeof(KsVehicleRestraintSystem))]
public sealed class KsVehicleTest : GameTest
{
    private readonly EntProtoId _rider = "KsVehicleTestRider";
    private readonly EntProtoId _vehicle = "KsVehicleTestVehicle";
    private readonly EntProtoId _bullet = "KsVehicleTestBullet";
    private readonly EntProtoId _handcuffs = "KsVehicleTestHandcuffs";
    private readonly ProtoId<DamageTypePrototype> _takenDamage = "KsVehicleTestTaken";
    private readonly ProtoId<DamageTypePrototype> _ignoredDamage = "KsVehicleTestIgnored";

    [TestPrototypes]
    private const string Prototypes = @"
- type: damageType
  id: KsVehicleTestTaken
  name: damage-type-blunt

- type: damageType
  id: KsVehicleTestIgnored
  name: damage-type-radiation

- type: damageContainer
  id: KsVehicleTestContainer
  supportedTypes:
  - KsVehicleTestTaken

- type: entity
  id: KsVehicleTestRider
  components:
  - type: Buckle
  - type: Hands
    hands:
      hand_right:
        location: Right
      hand_left:
        location: Left
    sortedHands:
    - hand_right
    - hand_left
  - type: ComplexInteraction
  - type: Cuffable
  - type: StandingState
  - type: Eye
  - type: InputMover
  - type: MobMover
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        density: 100
        mask:
        - MobMask
        layer:
        - MobLayer
  - type: Damageable
  - type: Injurable

# Needs no hands, as an office chair.
- type: entity
  id: KsVehicleTestVehicle
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
  - type: Injurable
    damageContainer: KsVehicleTestContainer
  - type: Strap
  - type: Vehicle
    requiresHands: false
  - type: StrapVehicle

- type: entity
  id: KsVehicleTestBullet
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      projectile:
        shape:
          !type:PhysShapeCircle
          radius: 0.1
        hard: false
        mask:
        - BulletImpassable
  - type: Projectile
    damage:
      types:
        KsVehicleTestTaken: 10

- type: entity
  id: KsVehicleTestHandcuffs
  components:
  - type: Handcuff
";

    private async Task<(EntityUid Vehicle, EntityUid Rider)> SpawnRiderOnVehicle(bool buckle = true)
    {
        var entManager = Server.EntMan;
        var map = await Pair.CreateTestMap();
        EntityUid vehicleUid = default, riderUid = default;

        await Server.WaitPost(() =>
        {
            vehicleUid = entManager.SpawnEntity(_vehicle, map.MapCoords);
            riderUid = entManager.SpawnEntity(_rider, map.MapCoords);
        });

        await Pair.RunTicksSync(5);

        if (buckle)
        {
            await Server.WaitAssertion(() =>
            {
                Assert.That(entManager.System<SharedBuckleSystem>().TryBuckle(riderUid, riderUid, vehicleUid), Is.True);
                Assert.That(entManager.GetComponent<VehicleComponent>(vehicleUid).Operator, Is.EqualTo(riderUid),
                    "buckling in should make the rider the operator");
            });
        }

        return (vehicleUid, riderUid);
    }

    private bool IsDamaged(EntityUid uid, ProtoId<DamageTypePrototype> type)
    {
        var damageable = Server.EntMan.GetComponent<DamageableComponent>(uid);
        var damage = Server.EntMan.System<DamageableSystem>().GetPositiveDamage((uid, damageable));
        return damage.DamageDict.TryGetValue(type, out var amount) && amount > FixedPoint2.Zero;
    }

    [Test]
    public async Task TestARidersRoundPassesThroughTheirVehicle()
    {
        var (vehicleUid, riderUid) = await SpawnRiderOnVehicle();
        EntityUid bulletUid = default;

        await Server.WaitPost(() =>
        {
            var entManager = Server.EntMan;
            bulletUid = entManager.SpawnAtPosition(_bullet, entManager.GetComponent<TransformComponent>(riderUid).Coordinates);
            entManager.System<GunSystem>().ShootProjectile(bulletUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: riderUid);
        });

        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsDamaged(vehicleUid, _takenDamage), Is.False,
                "the rider's round should pass through the vehicle they ride");
            Assert.That(Server.EntMan.Deleted(bulletUid) ||
                        Server.EntMan.GetComponent<TransformComponent>(bulletUid).LocalPosition.Y > 2f, Is.True,
                "and fly on, not stop inside it");
        });
    }

    [Test]
    public async Task TestSomeoneElsesRoundHitsTheVehicle()
    {
        // Empty, as a rider sitting on it would take the round first.
        var map = await Pair.CreateTestMap();
        EntityUid vehicleUid = default;
        await Server.WaitPost(() => vehicleUid = Server.EntMan.SpawnEntity(_vehicle, map.MapCoords));
        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var entManager = Server.EntMan;
            var vehicleCoordinates = entManager.GetComponent<TransformComponent>(vehicleUid).Coordinates;
            var bulletUid = entManager.SpawnAtPosition(_bullet, vehicleCoordinates.Offset(new Vector2(0f, -3f)));
            entManager.System<GunSystem>().ShootProjectile(bulletUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null);
        });

        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsDamaged(vehicleUid, _takenDamage), Is.True, "a round from anyone else should still hit the vehicle");
        });
    }

    /// <summary>
    ///     Off one vehicle and onto another in the same tick: the first one's operator component is still stored,
    ///         pending removal, when the second one wants it.
    /// </summary>
    [Test]
    public async Task TestSwitchingVehiclesInOneTickKeepsTheRiderDriving()
    {
        var (firstVehicleUid, riderUid) = await SpawnRiderOnVehicle(buckle: false);
        var entManager = Server.EntMan;
        EntityUid secondVehicleUid = default;

        await Server.WaitAssertion(() =>
        {
            secondVehicleUid = entManager.SpawnAtPosition(_vehicle, entManager.GetComponent<TransformComponent>(riderUid).Coordinates);
            var buckleSystem = entManager.System<SharedBuckleSystem>();
            Assert.That(buckleSystem.TryBuckle(riderUid, riderUid, firstVehicleUid), Is.True);
            // Unbuckle rather than TryUnbuckle: the buckle cooldown refuses getting straight back off.
            buckleSystem.Unbuckle(riderUid, riderUid);
            Assert.That(buckleSystem.TryBuckle(riderUid, riderUid, secondVehicleUid), Is.True);
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<VehicleComponent>(secondVehicleUid).Operator, Is.EqualTo(riderUid));
            Assert.That(entManager.TryGetComponent<VehicleOperatorComponent>(riderUid, out var operatorComponent), Is.True,
                "the rider should still be an operator once the tick's removals are done");
            Assert.That(operatorComponent!.Vehicle, Is.EqualTo(secondVehicleUid));
            Assert.That(entManager.GetComponent<RelayInputMoverComponent>(riderUid).RelayEntity, Is.EqualTo(secondVehicleUid));
        });
    }

    [Test]
    public async Task TestACuffedEntityCannotDriveAHandsFreeVehicle()
    {
        var (vehicleUid, riderUid) = await SpawnRiderOnVehicle(buckle: false);
        var entManager = Server.EntMan;

        await Server.WaitAssertion(() =>
        {
            var cuffsUid = entManager.SpawnAtPosition(_handcuffs, entManager.GetComponent<TransformComponent>(riderUid).Coordinates);
            Assert.That(entManager.System<CuffableSystem>().TryAddNewCuffs(riderUid, riderUid, cuffsUid), Is.True);

            // Buckled in by someone else, as a prisoner strapped to a chair.
            Assert.That(entManager.System<SharedBuckleSystem>().TryBuckle(riderUid, null, vehicleUid), Is.True);
            Assert.That(entManager.GetComponent<VehicleComponent>(vehicleUid).Operator, Is.Null,
                "a cuffed entity shouldn't become the operator");
            Assert.That(entManager.GetComponent<InputMoverComponent>(riderUid).CanMove, Is.False,
                "and the buckle should hold them still");
        });
    }

    [Test]
    public async Task TestCuffingTheOperatorStopsThemDriving()
    {
        var (vehicleUid, riderUid) = await SpawnRiderOnVehicle();
        var entManager = Server.EntMan;

        await Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<InputMoverComponent>(riderUid).CanMove, Is.True,
                "the operator should be able to drive before being cuffed");

            var cuffsUid = entManager.SpawnAtPosition(_handcuffs, entManager.GetComponent<TransformComponent>(riderUid).Coordinates);
            Assert.That(entManager.System<CuffableSystem>().TryAddNewCuffs(riderUid, riderUid, cuffsUid), Is.True);

            Assert.That(entManager.GetComponent<InputMoverComponent>(riderUid).CanMove, Is.False,
                "a cuffed operator shouldn't steer the vehicle");
            Assert.That(entManager.GetComponent<VehicleComponent>(vehicleUid).Operator, Is.EqualTo(riderUid));
        });
    }

    [Test]
    public async Task TestOnlyDamageTheVehicleTakesReachesTheOperator()
    {
        var (vehicleUid, riderUid) = await SpawnRiderOnVehicle();

        await Server.WaitAssertion(() =>
        {
            var damageableSystem = Server.EntMan.System<DamageableSystem>();
            damageableSystem.TryChangeDamage(vehicleUid, new DamageSpecifier(Server.ProtoMan.Index(_ignoredDamage), 20));
            Assert.That(IsDamaged(riderUid, _ignoredDamage), Is.False,
                "damage the vehicle's container doesn't take shouldn't reach the operator");

            damageableSystem.TryChangeDamage(vehicleUid, new DamageSpecifier(Server.ProtoMan.Index(_takenDamage), 20));
            Assert.That(IsDamaged(vehicleUid, _takenDamage), Is.True);
            Assert.That(IsDamaged(riderUid, _takenDamage), Is.True, "damage the vehicle takes should reach the operator");
        });
    }
}
