#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Explosion;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Gravity;
using Content.Shared._KS14.Throwing;
using Content.Shared.Explosion;
using Content.Shared.Gravity;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Explosion;

/// <summary>
///     An explosion sends a mob with <see cref="KsExplosionThrownComponent"/> flying away from it, marked to slam
///         into what it hits, and further the stronger the blast is where the mob stands. A blast too weak leaves it
///         where it is.
/// </summary>
[TestFixture]
[TestOf(typeof(ExplosionSystem))]
public sealed class KsExplosionThrowsMobsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private readonly EntProtoId _mob = "KsTestBlastThrownMob";
    private readonly ProtoId<ExplosionPrototype> _explosion = "KsTestMobThrowExplosion";

    // The explosion is harmless, so that the mob is still whole to be measured. The mob's caps are out of reach of
    // any blast here, so that distance keeps growing with force.
    [TestPrototypes]
    private const string Prototypes = @"
- type: explosion
  id: KsTestMobThrowExplosion
  damagePerIntensity:
    types:
      Blunt: 0.01
  tileBreakChance: [ 0 ]
  tileBreakIntensity: [ 0 ]
  texturePath: /Textures/Effects/fire.rsi
  fireStates: 3

- type: entity
  parent: MobHuman
  id: KsTestBlastThrownMob
  components:
  - type: KsExplosionThrown
    minThrowForce: 15
    speedPerForce: 0.4
    maxSpeed: 100
    distancePerForce: 0.15
    maxDistance: 100
    knockdownTime: 2
";

    /// <summary>
    ///     <c>ExplosionSystem</c>'s throw force on a tile is this times the square root of the intensity there.
    /// </summary>
    private const float ThrowForcePerRootIntensity = 10f;

    /// <summary>
    ///     How far east the mob ends up after a blast one tile west of it, and whether it was marked to slam on the way.
    /// </summary>
    /// <param name="forceOverThreshold">The blast's throw force on the mob's tile, as a multiple of the weakest
    ///     that lifts the mob.</param>
    private async Task<(float Distance, bool Marked)> Blast(float forceOverThreshold)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid mobUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -4; x <= 16; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
                }
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);
            mobUid = SEntMan.SpawnEntity(_mob, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var throwForce = SEntMan.GetComponent<KsExplosionThrownComponent>(mobUid).MinThrowForce * forceOverThreshold;
            var rootIntensity = throwForce / ThrowForcePerRootIntensity;
            var tileIntensity = rootIntensity * rootIntensity;

            var epicenter = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            // Small and steep: an explosion over a large area throws only at its rim. One tile out, a slope of half
            // the wanted intensity under a cap of one and a half times it leaves just that intensity.
            SEntMan.System<ExplosionSystem>().QueueExplosion(epicenter, _explosion, totalIntensity: tileIntensity * 12f,
                slope: tileIntensity / 2f, maxTileIntensity: tileIntensity * 1.5f, cause: null);
        });

        var marked = false;
        for (var tick = 0; tick < 150; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() => marked |= SEntMan.HasComponent<KsThrownSlamComponent>(mobUid));
        }

        var distance = 0f;
        await Server.WaitPost(() => distance = SEntMan.System<SharedTransformSystem>().GetWorldPosition(mobUid).X - 1.5f);
        return (distance, marked);
    }

    [Test]
    public async Task StrongerBlastThrowsFurther()
    {
        var weak = await Blast(0.5f);
        var middling = await Blast(1.5f);
        var strong = await Blast(3f);

        Assert.Multiple(() =>
        {
            Assert.That(weak.Marked, Is.False, "A blast too weak must not throw the mob.");
            Assert.That(weak.Distance, Is.LessThan(0.5f), "A blast too weak must leave the mob where it is.");
            Assert.That(middling.Marked, "A thrown mob must be marked to slam into what it hits.");
            Assert.That(middling.Distance, Is.GreaterThan(1f), "A blast must throw the mob away from it.");
            Assert.That(strong.Distance, Is.GreaterThan(middling.Distance + 1f), "A stronger blast must throw the mob further.");
        });
    }
}
