#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.Portal;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Teleportation.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._KS14.Portal;

/// <summary>
///     Seamless crossing of view portals: an entity whose centre crosses a doorway ends up the same distance
///         past the linked doorway, with its velocity turned by the doorways' relative rotation, and is not
///         sent back by the doorway it arrives at.
/// </summary>
[TestFixture]
public sealed class KsPortalTravelTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestTravelMob
  components:
  - type: Physics
    bodyType: Dynamic
    fixedRotation: true
    bodyStatus: InAir
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.25
        density: 10
        hard: true
        layer:
        - MidImpassable
        mask:
        - Impassable
- type: entity
  id: KsTestTravelMover
  components:
  - type: Physics
    bodyType: KinematicController
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.25
        density: 10
        hard: true
        layer:
        - MidImpassable
        mask:
        - Impassable
";

    private const string PortalProto = "KsViewPortal";
    private const float Speed = 4f;

    private static readonly Vector2 FirstPosition = Vector2.Zero;
    private static readonly Vector2 SecondPosition = new(30f, 5f);

    private async Task<(EntityUid First, EntityUid Second, MapId MapId)> SpawnLinkedPortalsAsync(Angle secondRotation)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            var transformSystem = server.EntMan.System<TransformSystem>();
            first = server.EntMan.SpawnEntity(PortalProto, new MapCoordinates(FirstPosition, testMap.MapId));
            second = server.EntMan.SpawnEntity(PortalProto, new MapCoordinates(SecondPosition, testMap.MapId));
            transformSystem.SetWorldRotation(second, secondRotation);

            Assert.That(server.EntMan.System<LinkedEntitySystem>().TryLink(first, second), Is.True);
        });

        return (first, second, testMap.MapId);
    }

    private async Task<EntityUid> SpawnMobAsync(Vector2 position, Vector2 velocity, MapId mapId)
    {
        var server = Pair.Server;
        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("KsTestTravelMob", new MapCoordinates(position, mapId));
            server.EntMan.System<SharedPhysicsSystem>().SetLinearVelocity(mob, velocity);
        });

        return mob;
    }

    private async Task SetVelocityAsync(EntityUid mob, Vector2 velocity)
    {
        var server = Pair.Server;
        await server.WaitPost(() => server.EntMan.System<SharedPhysicsSystem>().SetLinearVelocity(mob, velocity));
    }

    private (Vector2 Position, Vector2 Velocity) Sample(EntityUid mob)
    {
        var entityManager = Pair.Server.EntMan;
        return (
            entityManager.System<TransformSystem>().GetWorldPosition(mob),
            entityManager.GetComponent<PhysicsComponent>(mob).LinearVelocity);
    }

    private Vector2 InFrameOf(EntityUid portal, Vector2 worldPoint)
    {
        var (position, rotation) = Pair.Server.EntMan.System<TransformSystem>().GetWorldPositionRotation(portal);
        return KsPortalTransform.ToPortalFrame(worldPoint, position, rotation);
    }

    [Test]
    public async Task TestCrossingLandsPastLinkedPortalWithSameOffsetAndDoesNotReturn()
    {
        var (first, second, mapId) = await SpawnLinkedPortalsAsync(Angle.Zero);

        // 0.4 to the side of centre, one unit before the doorway, walking straight at it.
        var mob = await SpawnMobAsync(new Vector2(0.4f, -1f), new Vector2(0f, Speed), mapId);

        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            var (position, velocity) = Sample(mob);
            var local = InFrameOf(second, position);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(local.X, Is.EqualTo(0.4f).Within(0.05f), $"Lateral offset not kept; the mob is at {position}.");
                Assert.That(local.Y, Is.GreaterThan(0.5f).And.LessThan(6f), $"The mob is not beyond the linked doorway; it is at {position}.");
                Assert.That(velocity.Y, Is.EqualTo(Speed).Within(0.4f));
            }
        });

        // Turn it round: it has to come back through the doorway it arrived at, to the first one.
        await SetVelocityAsync(mob, new Vector2(0f, -Speed));
        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            var (position, _) = Sample(mob);
            var local = InFrameOf(first, position);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(local.X, Is.EqualTo(0.4f).Within(0.05f), $"The way back lost the offset; the mob is at {position}.");
                Assert.That(local.Y, Is.LessThan(-0.5f).And.GreaterThan(-6f), $"The mob did not come back through the first doorway; it is at {position}.");
            }
        });
    }

    [Test]
    public async Task TestWideDoorwayNominatesTravellersAlongItsWholeWidth()
    {
        var (first, second, mapId) = await SpawnLinkedPortalsAsync(Angle.Zero);
        var server = Pair.Server;

        await server.WaitPost(() =>
        {
            var travelSystem = server.EntMan.System<KsPortalTravelSystem>();
            foreach (var portal in new[] { first, second })
            {
                var view = server.EntMan.GetComponent<KsPortalViewComponent>(portal);
                view.Width = 20f;
                travelSystem.RefreshSensor((portal, view));
            }
        });

        await server.WaitAssertion(() =>
        {
            // The sensor follows the width: the doorway plus the margin to either end.
            var fixture = server.EntMan.System<FixtureSystem>().GetFixtureOrNull(first, KsPortalTravelSystem.SensorFixtureId);
            Assert.That(fixture, Is.Not.Null);
            var extent = fixture!.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
            Assert.That(extent.Right, Is.EqualTo(11.5f).Within(0.05f), "sensor not resized to the width");
        });

        // 8.6 to the side: far outside the prototype's default sensor, near the end of the 20 wide doorway.
        var mob = await SpawnMobAsync(new Vector2(8.6f, -1f), new Vector2(0f, Speed), mapId);

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var (position, _) = Sample(mob);
            var local = InFrameOf(second, position);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(local.X, Is.EqualTo(8.6f).Within(0.05f), $"Lateral offset not kept; the mob is at {position}.");
                Assert.That(local.Y, Is.GreaterThan(0.5f).And.LessThan(6f), $"The mob did not cross the wide doorway; it is at {position}.");
            }
        });
    }

    [Test]
    public async Task TestRotatedDestinationTurnsPositionVelocityAndFacing()
    {
        var (_, second, mapId) = await SpawnLinkedPortalsAsync(new Angle(MathF.PI / 2f));

        var mob = await SpawnMobAsync(new Vector2(0.4f, -1f), new Vector2(0f, Speed), mapId);

        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            var (position, velocity) = Sample(mob);
            var local = InFrameOf(second, position);
            var rotation = Pair.Server.EntMan.System<TransformSystem>().GetWorldRotation(mob);

            using (Assert.EnterMultipleScope())
            {
                // In the destination's own frame everything looks exactly as it did in the source's.
                Assert.That(local.X, Is.EqualTo(0.4f).Within(0.05f), $"Lateral offset not kept; the mob is at {position}.");
                Assert.That(local.Y, Is.GreaterThan(0.5f).And.LessThan(6f), $"The mob is not beyond the linked doorway; it is at {position}.");

                // The destination is turned a quarter turn, so the world velocity is too.
                Assert.That(velocity.X, Is.EqualTo(-Speed).Within(0.4f));
                Assert.That(velocity.Y, Is.EqualTo(0f).Within(0.05f));

                Assert.That(Angle.ShortestDistance(rotation, new Angle(MathF.PI / 2f)).Theta, Is.EqualTo(0f).Within(0.01f));
            }
        });
    }

    [Test]
    public async Task TestCrossingToAnotherMap()
    {
        var server = Pair.Server;
        var firstMap = await Pair.CreateTestMap();
        var secondMap = await Pair.CreateTestMap();

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            first = server.EntMan.SpawnEntity(PortalProto, new MapCoordinates(FirstPosition, firstMap.MapId));
            second = server.EntMan.SpawnEntity(PortalProto, new MapCoordinates(SecondPosition, secondMap.MapId));
            Assert.That(server.EntMan.System<LinkedEntitySystem>().TryLink(first, second), Is.True);
        });

        var mob = await SpawnMobAsync(new Vector2(0.4f, -1f), new Vector2(0f, Speed), firstMap.MapId);

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var coordinates = server.EntMan.System<TransformSystem>().GetMapCoordinates(mob);
            var local = InFrameOf(second, coordinates.Position);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(coordinates.MapId, Is.EqualTo(secondMap.MapId));
                Assert.That(local.X, Is.EqualTo(0.4f).Within(0.05f));
                Assert.That(local.Y, Is.GreaterThan(0.5f));
            }
        });
    }

    [Test]
    public async Task TestApproachingWithoutCrossingDoesNotTravel()
    {
        var (first, _, mapId) = await SpawnLinkedPortalsAsync(Angle.Zero);

        var mob = await SpawnMobAsync(new Vector2(0.4f, -1f), new Vector2(0f, Speed), mapId);

        // Close to the line, then stopped well short of it...
        await Pair.RunTicksSync(3);
        await SetVelocityAsync(mob, Vector2.Zero);
        await Pair.RunTicksSync(20);

        await Pair.Server.WaitAssertion(() =>
        {
            var local = InFrameOf(first, Sample(mob).Position);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(local.Y, Is.LessThan(0f), "The mob should still be before the doorway.");
                Assert.That(local.Y, Is.GreaterThan(-1f), "The mob should have moved up to the doorway.");
                Assert.That(local.X, Is.EqualTo(0.4f).Within(0.01f));
            }
        });

        // ...then turned back: still never crossed.
        await SetVelocityAsync(mob, new Vector2(0f, -Speed));
        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
        {
            var local = InFrameOf(first, Sample(mob).Position);
            Assert.That(local.Y, Is.LessThan(-1f), "The mob should have walked back away from the doorway.");
        });
    }

    [Test]
    public async Task TestPassingBesideTheDoorwayDoesNotTravel()
    {
        var (first, _, mapId) = await SpawnLinkedPortalsAsync(Angle.Zero);

        // The doorway is one unit long and centred, so this goes past its end, near enough for the sensor to
        //      be tracking it.
        var mob = await SpawnMobAsync(new Vector2(1.3f, -1f), new Vector2(0f, Speed), mapId);

        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            var local = InFrameOf(first, Sample(mob).Position);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(local.X, Is.EqualTo(1.3f).Within(0.05f), "The mob was moved sideways.");
                Assert.That(local.Y, Is.GreaterThan(1f), "The mob should have walked straight on past the doorway's end.");
            }
        });
    }

    [Test]
    public async Task TestControlledMoverKeepsMovingTheSameWayOnScreenAndTurnsItsCamera()
    {
        var server = Pair.Server;
        var (_, second, mapId) = await SpawnLinkedPortalsAsync(new Angle(MathF.PI / 2f));

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("KsTestTravelMover", new MapCoordinates(new Vector2(0.4f, -1f), mapId));
            var mover = new Entity<InputMoverComponent>(mob, server.EntMan.GetComponent<InputMoverComponent>(mob));
            server.EntMan.System<SharedMoverController>().SetVelocityDirection(mover, Direction.North, subTick: 0, enabled: true);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            var moverSystem = server.EntMan.System<SharedMoverController>();
            var moverComponent = server.EntMan.GetComponent<InputMoverComponent>(mob);
            var local = InFrameOf(second, Sample(mob).Position);

            using (Assert.EnterMultipleScope())
            {
                // The view turned with the crossing, so that the destination looks the same as it did through the doorway.
                Assert.That(Angle.ShortestDistance(moverSystem.GetParentGridAngle(moverComponent), new Angle(MathF.PI / 2f)).Theta,
                    Is.EqualTo(0f).Within(0.01f), "The camera's parent angle did not turn with the crossing.");

                // And holding up still goes the way it did on screen: straight on through the destination, not sideways.
                Assert.That(local.X, Is.EqualTo(0.4f).Within(0.1f), "Holding up no longer moves the way it did before the crossing.");
                Assert.That(local.Y, Is.GreaterThan(0.5f));
            }
        });
    }
}
