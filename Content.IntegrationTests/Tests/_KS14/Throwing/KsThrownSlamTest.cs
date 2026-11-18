#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Throwing;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Throwing;

/// <summary>
///     <see cref="KsThrownSlamSystem"/> by itself, with nobody behind the throw, as a blast would use it: someone
///         thrown and marked slams into the first thing they hit, and someone thrown without the mark doesn't.
/// </summary>
[TestFixture]
[TestOf(typeof(KsThrownSlamSystem))]
public sealed class KsThrownSlamTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<KsThrownSlamPrototype> Slam = "KsThrownSlam";
    private readonly EntProtoId _wall = "KsThrownSlamTestWall";

    /// <summary>
    ///     Where what they are thrown at stands; the thrown starts three tiles west of it.
    /// </summary>
    private const float ObstacleX = 3.5f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsThrownSlamTestWall
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        mask:
        - FullTileMask
        layer:
        - WallLayer
";

    /// <param name="marked">Whether the thrown is marked to slam. Unmarked, the wall only stops them.</param>
    /// <param name="intoPerson">Whether a person stands where they are thrown, rather than a wall.</param>
    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task SourcelessThrowSlams(bool marked, bool intoPerson)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid thrownUid = default;
        EntityUid obstacleUid = default;
        var timing = Server.ResolveDependency<IGameTiming>();

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 9; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            thrownUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            obstacleUid = SEntMan.SpawnEntity(intoPerson ? Human : _wall, new EntityCoordinates(testMap.Grid, ObstacleX, 0.5f));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            SEntMan.System<ThrowingSystem>().TryThrow(thrownUid, new Vector2(7f, 0f), 10f);
            if (marked)
                SEntMan.System<KsThrownSlamSystem>().MarkThrown(thrownUid, Slam);
        });

        await Pair.RunTicksSync((int)(1.5 * timing.TickRate));

        await Server.WaitAssertion(() =>
        {
            var slam = SProtoMan.Index(Slam);
            var damage = SEntMan.System<DamageableSystem>().GetAllDamage(thrownUid).GetTotal();
            var knockedDown = SEntMan.HasComponent<KnockedDownComponent>(thrownUid);
            var impact = slam.ImpactDamage!;

            if (!marked)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(knockedDown, Is.False, "Someone thrown without the mark must not be slammed.");
                    Assert.That(damage, Is.LessThan(impact.GetTotal()), "Someone thrown without the mark must not take the slam's damage.");
                });
                return;
            }

            var transformSystem = SEntMan.System<SharedTransformSystem>();
            var past = transformSystem.GetWorldPosition(thrownUid).X - transformSystem.GetWorldPosition(obstacleUid).X;
            Assert.Multiple(() =>
            {
                Assert.That(knockedDown, "Someone thrown and marked must go down against what they hit.");
                Assert.That(damage, Is.GreaterThanOrEqualTo(impact.GetTotal()), "The slam must hurt them.");
                Assert.That(SEntMan.HasComponent<KsThrownSlamComponent>(thrownUid), Is.False, "The mark must go with the first slam.");
                Assert.That(past, Is.LessThan(0.75f), "The slam must stop them where they hit.");
                Assert.That(SEntMan.GetComponent<PhysicsComponent>(thrownUid).LinearVelocity.Length(), Is.LessThan(0.1f),
                    "They must be at rest after the slam.");
                if (intoPerson)
                {
                    Assert.That(SEntMan.HasComponent<KnockedDownComponent>(obstacleUid), "Whoever they hit must go down too.");
                    Assert.That(transformSystem.GetWorldPosition(obstacleUid).X, Is.GreaterThan(ObstacleX + 0.25f),
                        "Whoever they hit must be pushed on by the momentum they brought.");
                }
            });
        });
    }
}
