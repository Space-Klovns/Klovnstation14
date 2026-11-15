#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Explosion;

/// <summary>
///     An explosion with <see cref="ExplosionPrototype.KsSpared"/> leaves what it lists undamaged, except what
///         <see cref="ExplosionPrototype.KsNotSpared"/> lists, and hurts everything else as usual. An explosion without
///         them spares nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(ExplosionSystem))]
public sealed class KsExplosionSparingTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly ProtoId<ExplosionPrototype> _sparingExplosion = "KsTestSparingExplosion";
    private readonly ProtoId<ExplosionPrototype> _plainExplosion = "KsTestPlainExplosion";
    private readonly EntProtoId _mob = "KsTestSparingMob";
    private readonly EntProtoId _wall = "KsTestSparingWall";
    private readonly EntProtoId _exception = "KsTestSparingException";

    [TestPrototypes]
    private const string Prototypes = @"
- type: explosion
  id: KsTestPlainExplosion
  damagePerIntensity:
    types:
      Blunt: 10
  tileBreakChance: [ 0 ]
  tileBreakIntensity: [ 0 ]
  texturePath: /Textures/Effects/fire.rsi
  fireStates: 3

- type: explosion
  id: KsTestSparingExplosion
  damagePerIntensity:
    types:
      Blunt: 10
  tileBreakChance: [ 0 ]
  tileBreakIntensity: [ 0 ]
  texturePath: /Textures/Effects/fire.rsi
  fireStates: 3
  ksSpared:
    tags:
    - Wall
  ksNotSpared:
    components:
    - SolutionContainerManager

- type: entity
  id: KsTestSparingMob
  components:
  - type: MobState
  - type: Damageable
    damageContainer: Biological
  - type: Injurable

- type: entity
  id: KsTestSparingWall
  components:
  - type: Tag
    tags:
    - Wall
  - type: Damageable
  - type: Injurable
    damageContainer: Inorganic

- type: entity
  parent: KsTestSparingWall
  id: KsTestSparingException
  components:
  - type: SolutionContainerManager
";

    [Test]
    public async Task SparedThingsAreLeftAlone([Values] bool sparing)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid mobUid = default;
        EntityUid wallUid = default;
        EntityUid exceptionUid = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            for (var x = -6; x <= 6; x++)
            {
                for (var y = -6; y <= 6; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            mobUid = server.EntMan.SpawnAtPosition(_mob, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            wallUid = server.EntMan.SpawnAtPosition(_wall, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            exceptionUid = server.EntMan.SpawnAtPosition(_exception, new EntityCoordinates(testMap.Grid, -0.5f, 0.5f));

            var epicenter = server.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.System<ExplosionSystem>().QueueExplosion(epicenter, sparing ? _sparingExplosion : _plainExplosion,
                totalIntensity: 40f, slope: 10f, maxTileIntensity: 20f, cause: null);
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var damageableSystem = server.System<DamageableSystem>();
            Assert.That((float)damageableSystem.GetTotalDamage(mobUid), Is.GreaterThan(0f), "everything not spared is hurt");
            Assert.That((float)damageableSystem.GetTotalDamage(exceptionUid), Is.GreaterThan(0f), "an exception to the spared list is hurt");

            if (sparing)
                Assert.That((float)damageableSystem.GetTotalDamage(wallUid), Is.Zero, "a spared wall should be untouched");
            else
                Assert.That((float)damageableSystem.GetTotalDamage(wallUid), Is.GreaterThan(0f), "an explosion that spares nothing hurts walls");
        });
    }
}
