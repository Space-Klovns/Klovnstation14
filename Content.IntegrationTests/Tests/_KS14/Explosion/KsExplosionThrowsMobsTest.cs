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

    private static readonly EntProtoId Human = "MobHuman";
    private readonly ProtoId<ExplosionPrototype> _explosion = "KsTestMobThrowExplosion";

    // Harmless, so that the mob is still whole to be measured.
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
";

    /// <summary>
    ///     How far east the mob ends up after a blast of this intensity one tile west of it, and whether it was marked
    ///         to slam on the way.
    /// </summary>
    private async Task<(float Distance, bool Marked)> Blast(float tileIntensity)
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
            mobUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var epicenter = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            // Small and steep: an explosion over a large area throws only at its rim.
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
        var middling = await Blast(4f);
        var strong = await Blast(16f);

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
