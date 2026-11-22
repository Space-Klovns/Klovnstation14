#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Trigger.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Trigger;

/// <summary>
///     A round with <c>KsChemicalFireOnTrigger</c> (the flamer tank's) leaves a chemical fire where it lands, out to its
///         radius and no further, and only when it goes off.
/// </summary>
[TestFixture]
[TestOf(typeof(KsChemicalFireOnTriggerSystem))]
public sealed class KsChemicalFireOnTriggerTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // The chemical fire the round lights, as a flamer's would be: what is under test is the round.
    private static readonly EntProtoId ChemicalFire = "ChemicalFire";

    // A real wall, for the round to land against: what is under test is the round.
    private static readonly EntProtoId Wall = "WallSolid";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _round = "KsTestChemicalFireRound";
    private readonly EntProtoId _wideRound = "KsTestChemicalFireWideRound";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestChemicalFireRound
  components:
  - type: KsChemicalFireOnTrigger
    fire: ChemicalFire
    radius: 1

- type: entity
  id: KsTestChemicalFireWideRound
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.05
        density: 1
        hard: false
  - type: KsChemicalFireOnTrigger
    fire: ChemicalFire
    radius: 3
";

    [Test]
    public async Task TheRoundLightsWhereItLands()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        EntityUid roundUid = default;

        await server.WaitPost(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            entMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = entMan.System<SharedMapSystem>();
            for (var x = -5; x <= 5; x++)
            {
                for (var y = -5; y <= 5; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            roundUid = entMan.SpawnEntity(_round, new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));
        });

        // For the air to settle on the new floor: a chemical fire needs some to burn in.
        await Pair.RunTicksSync(60);

        var chemicalFireSystem = entMan.System<SharedChemicalFireSystem>();
        await server.WaitAssertion(() =>
        {
            Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, Vector2i.Zero, ChemicalFire.Id, out _), Is.False,
                "nothing should burn before the round goes off");

            entMan.System<TriggerSystem>().Trigger(roundUid, key: TriggerSystem.DefaultTriggerKey);
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            foreach (var tile in new[] { Vector2i.Zero, new Vector2i(1, 0), new Vector2i(0, -1) })
            {
                Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, tile, ChemicalFire.Id, out _), Is.True,
                    $"the round should leave a chemical fire on {tile}");
            }

            Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, new Vector2i(3, 0), ChemicalFire.Id, out _), Is.False,
                "and none past its radius");
        });
    }

    /// <summary>
    ///     A round landing against a wall one tile thick lights its own side and not the far one, though the far one
    ///         is in its radius: whether it went off in front of the wall or inside the wall's tile, coming from in front, or
    ///         stopped there with nothing but where it is in the tile to go by.
    /// </summary>
    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public async Task TheRoundDoesNotLightThroughAWall(bool insideTheWall, bool moving)
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        EntityUid roundUid = default;
        const int wallX = 1;

        await server.WaitPost(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            entMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = entMan.System<SharedMapSystem>();
            for (var x = -6; x <= 6; x++)
            {
                for (var y = -6; y <= 6; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            // Wall to wall across the grid, so that nothing goes round it within the radius.
            for (var y = -6; y <= 6; y++)
                entMan.SpawnAtPosition(Wall, new EntityCoordinates(testMap.Grid.Owner, wallX + 0.5f, y + 0.5f));

            var position = insideTheWall ? new System.Numerics.Vector2(wallX + 0.3f, 0.5f) : new System.Numerics.Vector2(wallX - 0.1f, 0.5f);
            roundUid = entMan.SpawnEntity(_wideRound, new EntityCoordinates(testMap.Grid.Owner, position));
        });

        await Pair.RunTicksSync(60);

        var chemicalFireSystem = entMan.System<SharedChemicalFireSystem>();
        await server.WaitAssertion(() =>
        {
            // Travelling at the wall from the west.
            var physicsSystem = entMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            var physicsComponent = entMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(roundUid);
            if (moving)
            {
                physicsSystem.SetLinearVelocity(roundUid, new System.Numerics.Vector2(20f, 0f), body: physicsComponent);
                Assert.That(physicsComponent.LinearVelocity.X, Is.GreaterThan(0f), "precondition: the round is moving");
            }

            entMan.System<TriggerSystem>().Trigger(roundUid, key: TriggerSystem.DefaultTriggerKey);
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, new Vector2i(wallX - 1, 0), ChemicalFire.Id, out _), Is.True,
                "the round should light the tile it came from");
            Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, new Vector2i(wallX - 2, 1), ChemicalFire.Id, out _), Is.True,
                "and round about it on its own side");

            for (var x = wallX + 1; x <= wallX + 2; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, new Vector2i(x, y), ChemicalFire.Id, out _), Is.False,
                        $"the round should not light {new Vector2i(x, y)}, behind the wall");
                }
            }
        });
    }
}
