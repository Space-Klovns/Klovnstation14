#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Movement;

/// <summary>
///     A weightless mob off any grid can push off something solid next to it, but not off something it would pass
///         straight through, such as a puddle's fixtures.
/// </summary>
[TestOf(typeof(SharedMoverController))]
public sealed class KsWeightlessPushOffTest : MovementTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsPushOffTestSolid
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.25,-0.25,0.25,0.25""
        layer:
        - HighImpassable
        hard: true

- type: entity
  id: KsPushOffTestSensor
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.25,-0.25,0.25,0.25""
        layer:
        - HighImpassable
        hard: false
";

    private readonly EntProtoId _solidPrototype = "KsPushOffTestSolid";
    private readonly EntProtoId _sensorPrototype = "KsPushOffTestSensor";

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestPushesOffOnlySolidThings(bool solid)
    {
        var spacePosition = new Vector2(100f, 100f);

        await Server.WaitPost(() =>
        {
            // Off the grid, in space, with nothing but the test entity within grabbing range.
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, spacePosition));
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(SPlayer, Vector2.Zero);
            SEntMan.SpawnEntity(solid ? _solidPrototype : _sensorPrototype,
                new EntityCoordinates(MapData.MapUid, spacePosition + new Vector2(0f, 1.2f)));
        });
        await RunTicks(5);

        var startPosition = Vector2.Zero;
        await Server.WaitPost(() => startPosition = Transform.GetWorldPosition(SPlayer));

        await Move(DirectionFlag.East, 1f);

        await Server.WaitAssertion(() =>
        {
            var travelled = Transform.GetWorldPosition(SPlayer).X - startPosition.X;
            if (solid)
                Assert.That(travelled, Is.GreaterThan(0.1f), "The player could not push off a solid thing beside them.");
            else
                Assert.That(travelled, Is.LessThan(0.01f), "The player pushed off a sensor fixture they would pass through.");
        });
    }
}
