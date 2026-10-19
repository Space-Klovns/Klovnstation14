#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Physics;

/// <summary>
///     Covers <see cref="KsConstantVelocitySystem"/>. Component fields are written directly throughout, which is
///         exactly what a VV edit does, so RA0002 is disabled for the file.
/// </summary>
#pragma warning disable RA0002
public sealed class KsConstantVelocityTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsConstantVelocityTestBody
  name: test body
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        density: 100
        hard: true
        mask:
        - Impassable
        layer:
        - Impassable

# A tall wall across the body's path, on the same layer, so the two genuinely collide.
- type: entity
  id: KsConstantVelocityTestWall
  name: test wall
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: -0.5,-5,0.5,5
        density: 100
        hard: true
        mask:
        - Impassable
        layer:
        - Impassable

# A wide plate, so a spinning round target cannot deflect it off to one side and let it slide past.
- type: entity
  id: KsConstantVelocityTestRammer
  name: test rammer
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: -0.5,-3,0.5,3
        density: 100
        hard: true
        mask:
        - Impassable
        layer:
        - Impassable
";

    // Far enough from the test map's grid to be in open space.
    private static readonly Vector2 SpaceOrigin = new(100f, 100f);

    private const float WallX = 3.5f;

    [Test]
    public async Task TestHoldsLinearAndAngularVelocity()
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();

        var map = await Pair.CreateTestMap();

        var bodyUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            bodyUid = entityManager.SpawnEntity("KsConstantVelocityTestBody", new MapCoordinates(SpaceOrigin, map.MapId));

            var constantVelocityComponent = entityManager.AddComponent<KsConstantVelocityComponent>(bodyUid);
            constantVelocityComponent.LinearVelocity = new Vector2(2f, -1f);
            constantVelocityComponent.AngularVelocity = 1.5f;
        });

        await Pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var physicsComponent = entityManager.GetComponent<PhysicsComponent>(bodyUid);
            Assert.Multiple(() =>
            {
                Assert.That(physicsComponent.LinearVelocity.X, Is.EqualTo(2f).Within(0.001f));
                Assert.That(physicsComponent.LinearVelocity.Y, Is.EqualTo(-1f).Within(0.001f));
                Assert.That(physicsComponent.AngularVelocity, Is.EqualTo(1.5f).Within(0.001f));
            });
        });
    }

    /// <summary>
    ///     With nothing enforced, the only thing keeping the body at speed is the damping being zeroed. The control
    ///         body proves friction would otherwise have slowed it.
    /// </summary>
    [Test]
    public async Task TestRemovesDragWithoutEnforcingVelocity()
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var physicsSystem = entityManager.System<SharedPhysicsSystem>();

        var map = await Pair.CreateTestMap();

        var bodyUid = EntityUid.Invalid;
        var controlUid = EntityUid.Invalid;
        var initialVelocity = new Vector2(4f, 0f);

        await server.WaitPost(() =>
        {
            bodyUid = entityManager.SpawnEntity("KsConstantVelocityTestBody", new MapCoordinates(SpaceOrigin, map.MapId));
            controlUid = entityManager.SpawnEntity("KsConstantVelocityTestBody", new MapCoordinates(SpaceOrigin + new Vector2(0f, 20f), map.MapId));

            var constantVelocityComponent = entityManager.AddComponent<KsConstantVelocityComponent>(bodyUid);
            constantVelocityComponent.EnforceLinearVelocity = false;
            constantVelocityComponent.EnforceAngularVelocity = false;

            physicsSystem.SetLinearVelocity(bodyUid, initialVelocity);
            physicsSystem.SetLinearVelocity(controlUid, initialVelocity);
        });

        await Pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var bodyPhysicsComponent = entityManager.GetComponent<PhysicsComponent>(bodyUid);
            var controlPhysicsComponent = entityManager.GetComponent<PhysicsComponent>(controlUid);
            Assert.Multiple(() =>
            {
                Assert.That(controlPhysicsComponent.LinearVelocity.X, Is.LessThan(initialVelocity.X * 0.95f),
                    "the control body should have been slowed by friction, or this test proves nothing");
                Assert.That(bodyPhysicsComponent.LinearVelocity.X, Is.EqualTo(initialVelocity.X).Within(0.001f));
                Assert.That(bodyPhysicsComponent.LinearDamping, Is.Zero);
                Assert.That(bodyPhysicsComponent.AngularDamping, Is.Zero);
            });
        });
    }

    [Test]
    public async Task TestMovesGrid()
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();
        var gridUid = map.Grid.Owner;
        var startPosition = Vector2.Zero;

        await server.WaitPost(() =>
        {
            startPosition = transformSystem.GetWorldPosition(gridUid);

            var constantVelocityComponent = entityManager.AddComponent<KsConstantVelocityComponent>(gridUid);
            constantVelocityComponent.LinearVelocity = new Vector2(3f, 0f);
            constantVelocityComponent.AngularVelocity = 0.5f;
        });

        await Pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var physicsComponent = entityManager.GetComponent<PhysicsComponent>(gridUid);
            var travelled = transformSystem.GetWorldPosition(gridUid) - startPosition;
            Assert.Multiple(() =>
            {
                Assert.That(physicsComponent.LinearVelocity.X, Is.EqualTo(3f).Within(0.001f));
                Assert.That(physicsComponent.AngularVelocity, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(travelled.X, Is.GreaterThan(5f), "the grid should have actually moved, not just held a velocity");
            });
        });
    }

    [Test]
    public async Task TestCanCollideFalsePassesThroughWall()
    {
        var bodyX = await RunIntoWall(canCollide: false);
        Assert.That(bodyX, Is.GreaterThan(WallX + 1f), "the body should have passed straight through the wall");
    }

    /// <summary>
    ///     The control for <see cref="TestCanCollideFalsePassesThroughWall"/>: the same setup, colliding, is stopped.
    /// </summary>
    [Test]
    public async Task TestCanCollideTrueIsBlockedByWall()
    {
        var bodyX = await RunIntoWall(canCollide: true);
        Assert.That(bodyX, Is.LessThan(WallX), "the body should have been stopped by the wall");
    }

    /// <summary>
    ///     Turning collision off while already pressed against the wall. The contact already exists by then, so this
    ///         only passes if the change tears existing contacts down rather than just filtering new ones.
    /// </summary>
    [Test]
    public async Task TestDisablingCollisionMidContactPassesThroughWall()
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();
        var bodyUid = await SpawnBodyAndWall(map, canCollide: true);

        await Pair.RunSeconds(2f);

        await server.WaitPost(() =>
        {
            Assert.That(transformSystem.GetWorldPosition(bodyUid).X - SpaceOrigin.X, Is.LessThan(WallX),
                "the body should be held against the wall before collision is turned off");

            entityManager.GetComponent<KsConstantVelocityComponent>(bodyUid).CanCollide = false;
        });

        await Pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(transformSystem.GetWorldPosition(bodyUid).X - SpaceOrigin.X, Is.GreaterThan(WallX + 1f),
                "the body should have passed through the wall once collision was turned off");
        });
    }

    /// <summary>
    ///     A locked body is rammed head-on and must neither move nor stop spinning. Linear enforcement is off, so the
    ///         lock is the only thing holding it.
    /// </summary>
    [Test]
    public async Task TestLockPositionHoldsAgainstImpact()
    {
        var (targetDrift, targetAngularVelocity, bulletX) = await RunImpact(lockPosition: true);
        Assert.Multiple(() =>
        {
            Assert.That(bulletX, Is.LessThan(SpaceOrigin.X),
                "the rammer should have been stopped by the target, or it went past and this test proves nothing");
            Assert.That(targetDrift, Is.LessThan(0.001f), "the locked target should not have moved");
            Assert.That(targetAngularVelocity, Is.EqualTo(ImpactSpin).Within(0.001f), "the locked target should still be spinning");
        });
    }

    /// <summary>
    ///     The control for <see cref="TestLockPositionHoldsAgainstImpact"/>: the same ram, unlocked, shoves the target away.
    /// </summary>
    [Test]
    public async Task TestUnlockedBodyIsKnockedByImpact()
    {
        var (targetDrift, _, _) = await RunImpact(lockPosition: false);
        Assert.That(targetDrift, Is.GreaterThan(0.1f), "the unlocked target should have been shoved away");
    }

    /// <summary>
    ///     A grid's centre of mass is not its origin, so this pins the pivot a locked body turns about: turning about the
    ///         origin would swing the centre of mass around it.
    /// </summary>
    [Test]
    public async Task TestLockPositionGridSpinsAboutCenterOfMass()
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();
        var gridUid = map.Grid.Owner;
        var startCenter = Vector2.Zero;
        var startRotation = Angle.Zero;

        await server.WaitPost(() =>
        {
            var physicsComponent = entityManager.GetComponent<PhysicsComponent>(gridUid);
            Assert.That(physicsComponent.LocalCenter.Length(), Is.GreaterThan(0.1f),
                "the grid's centre of mass should be off its origin, or this test proves nothing");

            startCenter = GetWorldCenterOfMass(entityManager, transformSystem, gridUid);
            startRotation = transformSystem.GetWorldRotation(gridUid);

            var constantVelocityComponent = entityManager.AddComponent<KsConstantVelocityComponent>(gridUid);
            constantVelocityComponent.LockPosition = true;
            constantVelocityComponent.AngularVelocity = 1f;
        });

        await Pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var turned = Math.Abs((transformSystem.GetWorldRotation(gridUid) - startRotation).Reduced().Theta);
            var drift = (GetWorldCenterOfMass(entityManager, transformSystem, gridUid) - startCenter).Length();
            Assert.Multiple(() =>
            {
                Assert.That(turned, Is.GreaterThan(1.0), "the grid should have turned");
                Assert.That(drift, Is.LessThan(0.001f), "the grid's centre of mass should not have moved");
            });
        });
    }

    /// <summary>
    ///     Locking swaps the body type out from under everything else, so unlocking has to give the original back.
    /// </summary>
    [Test]
    public async Task TestUnlockRestoresBodyType()
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();

        var map = await Pair.CreateTestMap();

        var bodyUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            bodyUid = entityManager.SpawnEntity("KsConstantVelocityTestBody", new MapCoordinates(SpaceOrigin, map.MapId));
            entityManager.AddComponent<KsConstantVelocityComponent>(bodyUid).LockPosition = true;
        });

        await Pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            Assert.That(entityManager.GetComponent<PhysicsComponent>(bodyUid).BodyType, Is.EqualTo(BodyType.Kinematic),
                "the locked body should be kinematic, or this test proves nothing");

            entityManager.GetComponent<KsConstantVelocityComponent>(bodyUid).LockPosition = false;
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.GetComponent<PhysicsComponent>(bodyUid).BodyType, Is.EqualTo(BodyType.Dynamic));
        });
    }

    private const float ImpactSpeed = 8f;
    private const float ImpactSpin = 2f;

    /// <summary>
    ///     Drives a rammer into a spinning target, with linear enforcement off on the target. The rammer holds its speed
    ///         (and zero rotation) through its own <see cref="KsConstantVelocityComponent"/>, since friction would stop a
    ///         plain body well short of the target.
    /// </summary>
    /// <returns>How far the target's centre moved, its final angular velocity, and the rammer's final X.</returns>
    private async Task<(float TargetDrift, float TargetAngularVelocity, float RammerX)> RunImpact(bool lockPosition)
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var targetUid = EntityUid.Invalid;
        var rammerUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            targetUid = entityManager.SpawnEntity("KsConstantVelocityTestBody", new MapCoordinates(SpaceOrigin, map.MapId));
            rammerUid = entityManager.SpawnEntity("KsConstantVelocityTestRammer", new MapCoordinates(SpaceOrigin - new Vector2(2f, 0f), map.MapId));

            var targetConstantVelocityComponent = entityManager.AddComponent<KsConstantVelocityComponent>(targetUid);
            targetConstantVelocityComponent.EnforceLinearVelocity = false;
            targetConstantVelocityComponent.AngularVelocity = ImpactSpin;
            targetConstantVelocityComponent.LockPosition = lockPosition;

            entityManager.AddComponent<KsConstantVelocityComponent>(rammerUid).LinearVelocity = new Vector2(ImpactSpeed, 0f);
        });

        await Pair.RunSeconds(1.5f);

        var result = (0f, 0f, 0f);
        await server.WaitPost(() =>
        {
            result = (
                (GetWorldCenterOfMass(entityManager, transformSystem, targetUid) - SpaceOrigin).Length(),
                entityManager.GetComponent<PhysicsComponent>(targetUid).AngularVelocity,
                transformSystem.GetWorldPosition(rammerUid).X);
        });

        return result;
    }

    private static Vector2 GetWorldCenterOfMass(IEntityManager entityManager, SharedTransformSystem transformSystem, EntityUid uid)
    {
        var (worldPosition, worldRotation) = transformSystem.GetWorldPositionRotation(uid);
        return worldPosition + worldRotation.RotateVec(entityManager.GetComponent<PhysicsComponent>(uid).LocalCenter);
    }

    /// <returns>The body's final X, relative to <see cref="SpaceOrigin"/>.</returns>
    private async Task<float> RunIntoWall(bool canCollide)
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();
        var bodyUid = await SpawnBodyAndWall(map, canCollide);

        await Pair.RunSeconds(2f);

        var bodyX = 0f;
        await server.WaitPost(() => bodyX = transformSystem.GetWorldPosition(bodyUid).X - SpaceOrigin.X);
        return bodyX;
    }

    /// <summary>
    ///     Spawns a body at <see cref="SpaceOrigin"/> heading +X at 5 m/s, into a wall centred <see cref="WallX"/>
    ///         ahead of it.
    /// </summary>
    private async Task<EntityUid> SpawnBodyAndWall(TestMapData map, bool canCollide)
    {
        var server = Pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();

        var bodyUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            entityManager.SpawnEntity("KsConstantVelocityTestWall", new MapCoordinates(SpaceOrigin + new Vector2(WallX, 0f), map.MapId));
            bodyUid = entityManager.SpawnEntity("KsConstantVelocityTestBody", new MapCoordinates(SpaceOrigin, map.MapId));

            var constantVelocityComponent = entityManager.AddComponent<KsConstantVelocityComponent>(bodyUid);
            constantVelocityComponent.LinearVelocity = new Vector2(5f, 0f);
            constantVelocityComponent.CanCollide = canCollide;
        });

        return bodyUid;
    }
}
#pragma warning restore RA0002
