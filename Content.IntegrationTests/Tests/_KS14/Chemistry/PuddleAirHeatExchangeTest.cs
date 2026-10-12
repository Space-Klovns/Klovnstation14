using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     Conductive heat exchange between a puddle and the air on its tile must move energy, not create it.
/// </summary>
[TestFixture]
[TestOf(typeof(PuddleSystem))]
public sealed class PuddleAirHeatExchangeTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestPuddleAirHeatReagent
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  specificHeat: 4
";

    private const int TicksPerSecond = 30;
    private const string Reagent = "KsTestPuddleAirHeatReagent";

    [Test]
    public async Task TestHotPuddleWarmsAirAndConservesEnergy()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var protoMan = server.ProtoMan;
        var puddleSystem = entityManager.System<PuddleSystem>();
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();

        var testMap = await pair.CreateTestMap();

        GasMixture air = null;
        Entity<SolutionComponent> puddleSolution = default;
        float energyBefore = 0f;
        float solutionTemperatureBefore = 0f;
        float airTemperatureBefore = 0f;

        // A real grid atmosphere: map atmospheres are immutable and deliberately skipped by the exchange.
        await server.WaitPost(() => entityManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner));

        // Let atmos process once so the tile is given a mutable mixture.
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            // Pause the simulation so the tile air is only ever changed by the puddle.
            atmosphereSystem.SetAtmosphereSimulation((testMap.Grid.Owner, entityManager.GetComponent<GridAtmosphereComponent>(testMap.Grid.Owner)), false);

            var hotSpill = new Solution(Reagent, FixedPoint2.New(20)) { Temperature = 600f };
            Assert.That(puddleSystem.TrySpillAt(testMap.Tile, hotSpill, out _, false), Is.True,
                "Failed to spill the hot puddle.");

            Assert.That(puddleSystem.TryGetCachedPuddle(testMap.Tile, out Entity<PuddleComponent> puddle), Is.True,
                "Spilled puddle is not in the tile cache.");

            Assert.That(solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName,
                    ref puddle.Comp.Solution, out _),
                "Could not resolve the puddle solution.");

            puddleSolution = puddle.Comp.Solution!.Value;

            air = atmosphereSystem.GetTileMixture(new Entity<TransformComponent>(puddle.Owner, null), excite: false);
            Assert.That(air, Is.Not.Null, "Puddle tile has no air.");
            Assert.That(air!.Immutable, Is.False, "Puddle tile air is immutable.");
            air.Temperature = Atmospherics.T20C;
            air.SetMoles(Gas.Oxygen, 20f);

            solutionTemperatureBefore = puddleSolution.Comp.Solution.Temperature;
            airTemperatureBefore = air.Temperature;
            energyBefore = Energy(atmosphereSystem, protoMan, air, puddleSolution.Comp.Solution);
        });

        // Throttled to once a second; give it several passes.
        await server.WaitRunTicks(TicksPerSecond * 5);

        await server.WaitAssertion(() =>
        {
            var solution = puddleSolution.Comp.Solution;
            var energyAfter = Energy(atmosphereSystem, protoMan, air!, solution);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(solution.Temperature, Is.LessThan(solutionTemperatureBefore), "Hot puddle did not cool.");
                Assert.That(air!.Temperature, Is.GreaterThan(airTemperatureBefore), "Air did not warm.");
                Assert.That(air.Temperature, Is.LessThanOrEqualTo(solution.Temperature + 0.01f),
                    "Exchange overshot equilibrium.");
                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 0.001f),
                    "Total thermal energy was not conserved.");
            }
        });
    }

    private static float Energy(AtmosphereSystem atmosphereSystem, Robust.Shared.Prototypes.IPrototypeManager protoMan, GasMixture air, Solution solution)
        => atmosphereSystem.GetHeatCapacity(air, applyScaling: true) * air.Temperature + solution.GetThermalEnergy(protoMan);
}
