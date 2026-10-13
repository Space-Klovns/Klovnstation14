using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Chemistry.SolutionHeatExchange;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     <see cref="KsSolutionHeatExchangeSystem"/>: two solutions meeting at a shared temperature without
///         creating or destroying thermal energy.
/// </summary>
/// <remarks>
///     Vapor and smoke call <see cref="KsSolutionHeatExchangeSystem.TryEqualizeTemperature"/> against the
///         puddle on the tile they pass over. Most tests resolve the real puddle solution the same way the
///         vapor and smoke systems do and exchange heat with that directly; one drives a real vapor entity
///         across a puddle. The reagents are fixture-defined so that retuning shipped heat capacities cannot change what these tests mean.
/// </remarks>
[TestFixture]
[TestOf(typeof(KsSolutionHeatExchangeSystem))]
public sealed class SolutionHeatExchangeTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestHeatExchangeContainer
  components:
  - type: Solution
    id: beaker
    solution:
      maxVol: 1000

- type: reagent
  id: KsTestHeatReagentLight
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  specificHeat: 1

- type: reagent
  id: KsTestHeatReagentHeavy
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  specificHeat: 4
";

    private const string ContainerProto = "KsTestHeatExchangeContainer";
    private const string LightReagent = "KsTestHeatReagentLight";
    private const string HeavyReagent = "KsTestHeatReagentHeavy";

    private const float Tolerance = 0.01f;

    private static Entity<SolutionComponent> SpawnContainer(
        IEntityManager entityManager,
        SharedSolutionContainerSystem solutionContainerSystem,
        EntityCoordinates coordinates,
        string reagent,
        int volume,
        float temperature)
    {
        var container = entityManager.SpawnEntity(ContainerProto, coordinates);
        Assert.That(solutionContainerSystem.TryGetSolution(container, "beaker", out var solutionEntity, out _),
            "Test container has no beaker solution.");

        solutionContainerSystem.TryAddSolution(solutionEntity!.Value, new Solution(reagent, FixedPoint2.New(volume)));
        solutionContainerSystem.SetTemperature(solutionEntity.Value, temperature);
        return solutionEntity.Value;
    }

    [Test]
    public async Task TestEqualizeConservesThermalEnergy()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var protoMan = server.ProtoMan;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var heatExchangeSystem = entityManager.System<KsSolutionHeatExchangeSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hot = SpawnContainer(entityManager, solutionContainerSystem, testMap.GridCoords, HeavyReagent, 30, 600f);
            var cold = SpawnContainer(entityManager, solutionContainerSystem, testMap.GridCoords, LightReagent, 100, 280f);

            var hotCapacity = hot.Comp.Solution.GetHeatCapacity(protoMan);
            var coldCapacity = cold.Comp.Solution.GetHeatCapacity(protoMan);
            var energyBefore = hot.Comp.Solution.GetThermalEnergy(protoMan) + cold.Comp.Solution.GetThermalEnergy(protoMan);
            var expectedTemperature = energyBefore / (hotCapacity + coldCapacity);

            Assert.That(heatExchangeSystem.TryEqualizeTemperature(hot, cold), Is.True,
                "Equalising solutions at different temperatures reported no change.");

            var energyAfter = hot.Comp.Solution.GetThermalEnergy(protoMan) + cold.Comp.Solution.GetThermalEnergy(protoMan);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hot.Comp.Solution.Temperature, Is.EqualTo(cold.Comp.Solution.Temperature).Within(Tolerance),
                    "Solutions did not end at the same temperature.");

                Assert.That(hot.Comp.Solution.Temperature, Is.EqualTo(expectedTemperature).Within(Tolerance),
                    "Equilibrium was not the heat capacity weighted mean.");

                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 0.001f),
                    "Total thermal energy was not conserved.");
            }
        });
    }

    [Test]
    public async Task TestEqualizeAtEqualTemperatureDoesNothing()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var heatExchangeSystem = entityManager.System<KsSolutionHeatExchangeSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var first = SpawnContainer(entityManager, solutionContainerSystem, testMap.GridCoords, HeavyReagent, 30, 350f);
            var second = SpawnContainer(entityManager, solutionContainerSystem, testMap.GridCoords, LightReagent, 100, 350f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(heatExchangeSystem.TryEqualizeTemperature(first, second), Is.False,
                    "Equalising solutions already at the same temperature reported a change.");

                Assert.That(first.Comp.Solution.Temperature, Is.EqualTo(350f).Within(Tolerance));
                Assert.That(second.Comp.Solution.Temperature, Is.EqualTo(350f).Within(Tolerance));
            }
        });
    }

    /// <summary>
    ///     What vapor and smoke do to a puddle: a cold solution meets a hot puddle on a tile and drags it down
    ///         to a shared temperature.
    /// </summary>
    [Test]
    public async Task TestColdSolutionCoolsHotPuddle()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var protoMan = server.ProtoMan;
        var puddleSystem = entityManager.System<PuddleSystem>();
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var heatExchangeSystem = entityManager.System<KsSolutionHeatExchangeSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hotSpill = new Solution(HeavyReagent, FixedPoint2.New(20)) { Temperature = 600f };
            Assert.That(puddleSystem.TrySpillAt(testMap.Tile, hotSpill, out _, false), Is.True,
                "Failed to spill the hot puddle.");

            Assert.That(puddleSystem.TryGetCachedPuddle(testMap.Tile, out Entity<PuddleComponent> puddle), Is.True,
                "Spilled puddle is not in the tile cache.");

            Assert.That(solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName,
                    ref puddle.Comp.Solution, out var puddleSolution),
                "Could not resolve the puddle solution.");

            var puddleEntity = puddle.Comp.Solution!.Value;
            var puddleTemperatureBefore = puddleSolution!.Temperature;

            var cold = SpawnContainer(entityManager, solutionContainerSystem, testMap.GridCoords, LightReagent, 50, 280f);

            var energyBefore = puddleSolution.GetThermalEnergy(protoMan) + cold.Comp.Solution.GetThermalEnergy(protoMan);

            Assert.That(heatExchangeSystem.TryEqualizeTemperature(cold, puddleEntity), Is.True,
                "Cold solution did not exchange heat with the hot puddle.");

            var energyAfter = puddleSolution.GetThermalEnergy(protoMan) + cold.Comp.Solution.GetThermalEnergy(protoMan);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(puddleSolution.Temperature, Is.LessThan(puddleTemperatureBefore),
                    "Puddle did not cool.");

                Assert.That(puddleSolution.Temperature, Is.GreaterThan(280f),
                    "Puddle cooled past the cold solution temperature.");

                Assert.That(cold.Comp.Solution.Temperature, Is.EqualTo(puddleSolution.Temperature).Within(Tolerance),
                    "Cold solution and puddle did not reach a shared temperature.");

                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 0.001f),
                    "Total thermal energy was not conserved.");
            }
        });
    }

    /// <summary>
    ///     End to end: a real <c>Vapor</c> entity carrying a cold inert solution is thrown through the real
    ///         <see cref="VaporSystem"/> across a hot puddle, and the puddle must come out cooler.
    /// </summary>
    /// <remarks>
    ///     The reagents are fixture-defined and have no tile reactions, so <c>ReactionTile</c> neither
    ///         consumes the vapor nor touches the puddle. That lets the vapor keep its (now shared)
    ///         temperature after crossing the puddle, which is sampled every tick to check energy was
    ///         exchanged rather than created.
    /// </remarks>
    [Test]
    public async Task TestMovingVaporCoolsHotPuddle()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var protoMan = server.ProtoMan;
        var mapSystem = entityManager.System<SharedMapSystem>();
        var puddleSystem = entityManager.System<PuddleSystem>();
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var vaporSystem = entityManager.System<VaporSystem>();

        var testMap = await pair.CreateTestMap();
        var grid = testMap.Grid;

        EntityUid vapor = default;
        Entity<SolutionComponent> puddleSolution = default;
        float energyBefore = 0f;
        float expectedTemperature = 0f;
        const float initialPuddleTemperature = 600f;
        const float initialVaporTemperature = 280f;

        await server.WaitAssertion(() =>
        {
            // The puddle-air exchange would also cool the puddle if the grid had an atmosphere; this test needs
            //     the vapor-puddle exchange to be the only heat path.
            Assert.That(entityManager.HasComponent<GridAtmosphereComponent>(grid.Owner), Is.False,
                "Test grid has an atmosphere, so puddle-air heat exchange would skew the result.");

            // The test map only has the origin tile; lay a strip of floor for the vapor to fly over.
            for (var x = 1; x <= 4; x++)
                mapSystem.SetTile(grid.Owner, grid.Comp, new EntityCoordinates(grid, x, 0), testMap.Tile.Tile);

            // Puddle on tile (2, 0).
            var puddleTile = mapSystem.GetTileRef(grid.Owner, grid.Comp, new EntityCoordinates(grid, 2.5f, 0.5f));
            var hotSpill = new Solution(HeavyReagent, FixedPoint2.New(20)) { Temperature = initialPuddleTemperature };
            Assert.That(puddleSystem.TrySpillAt(puddleTile, hotSpill, out _, false, false), Is.True,
                "Failed to spill the hot puddle.");

            Assert.That(puddleSystem.TryGetCachedPuddle(puddleTile, out Entity<PuddleComponent> puddle), Is.True,
                "Spilled puddle is not in the tile cache.");

            Assert.That(solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName,
                    ref puddle.Comp.Solution, out _),
                "Could not resolve the puddle solution.");

            puddleSolution = puddle.Comp.Solution!.Value;

            // Cold vapor starts on tile (0, 0), puddle is two tiles down the line.
            var source = SpawnContainer(entityManager, solutionContainerSystem, testMap.GridCoords,
                LightReagent, 100, initialVaporTemperature);

            // The Vapor prototype gets its capacity (60u) from its SolutionSmall parent, same as in a real spray.
            vapor = entityManager.SpawnEntity("Vapor", new EntityCoordinates(grid, 0.5f, 0.5f));
            Assert.That(entityManager.GetComponent<SolutionComponent>(vapor).Solution.MaxVolume, Is.GreaterThanOrEqualTo(FixedPoint2.New(50)),
                "Vapor entity cannot hold the test volume.");
            Assert.That(vaporSystem.TryAddSolution(vapor, source, FixedPoint2.New(50)), Is.True,
                "Could not load the vapor entity.");

            Assert.That(entityManager.GetComponent<SolutionComponent>(vapor).Solution.Temperature,
                Is.EqualTo(initialVaporTemperature).Within(Tolerance),
                "Vapor did not inherit the source temperature.");

            var vaporSolution = entityManager.GetComponent<SolutionComponent>(vapor).Solution;
            var puddleCapacity = puddleSolution.Comp.Solution.GetHeatCapacity(protoMan);
            var vaporCapacity = vaporSolution.GetHeatCapacity(protoMan);
            energyBefore = puddleSolution.Comp.Solution.GetThermalEnergy(protoMan) + vaporSolution.GetThermalEnergy(protoMan);
            expectedTemperature = energyBefore / (puddleCapacity + vaporCapacity);

            // Same call SpraySystem makes: fly in +X to the far end of the strip.
            vaporSystem.Start(vapor,
                entityManager.GetComponent<TransformComponent>(vapor),
                new Vector2(4f, 0f),
                4f,
                new MapCoordinates(4.5f, 0.5f, testMap.MapId),
                2f);
        });

        // Sample the vapor temperature each tick until the despawn timer removes it.
        var lastVaporTemperature = initialVaporTemperature;
        var vaporGone = false;
        for (var i = 0; i < 300 && !vaporGone; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                if (!entityManager.EntityExists(vapor) || entityManager.IsQueuedForDeletion(vapor))
                {
                    vaporGone = true;
                    return;
                }

                lastVaporTemperature = entityManager.GetComponent<SolutionComponent>(vapor).Solution.Temperature;
            });
        }

        await server.WaitAssertion(() =>
        {
            var puddleTemperature = puddleSolution.Comp.Solution.Temperature;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vaporGone, Is.True, "Vapor never finished its flight.");

                Assert.That(puddleTemperature, Is.LessThan(initialPuddleTemperature - 50f),
                    "Puddle did not cool noticeably after cold vapor flew over it.");

                Assert.That(puddleTemperature, Is.GreaterThan(initialVaporTemperature),
                    "Puddle cooled past the vapor temperature.");

                // Vapor took the same temperature the puddle ended at (nothing else was on the path).
                Assert.That(lastVaporTemperature, Is.EqualTo(puddleTemperature).Within(1f),
                    "Vapor and puddle did not end at a shared temperature.");

                // One exchange between the two, so the heat capacity weighted mean is the answer.
                Assert.That(puddleTemperature, Is.EqualTo(expectedTemperature).Within(2f),
                    "Puddle did not reach the energy conserving equilibrium temperature.");
            }
        });
    }

    // A third scenario (a temperature-gated flammable puddle staying out after cooling) is intentionally
    //     not covered: it depends on puddle tile-effect tick timing and chemfire respawn rules, which are
    //     too fragile to assert on without being able to build and iterate.
}
