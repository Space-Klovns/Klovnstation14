using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chemistry.TileReactions;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     <see cref="ExtinguishTileReaction"/> boils water off and must account for the latent heat it absorbs,
///         rather than deleting thermal energy.
/// </summary>
[TestFixture]
[TestOf(typeof(ExtinguishTileReaction))]
public sealed class ExtinguishEnergyConservationTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseChemicalFire
  id: KsTestExtinguishEnergyFire
  components:
  - type: ChemicalFire
    color: '#FF9030'
    duration: 30
    temperature: 1000
    exposedVolume: 50
    heatPower: 0

- type: reagent
  parent: BaseDrink
  id: KsTestGaslessExtinguisher
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  tileReactions:
  - !type:ExtinguishTileReaction {}
";

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    // Deliberately NOT static: the YAML linter validates static prototype id fields against the real prototypes,
    //     and this one only exists in the test YAML above.
    private readonly ProtoId<ReagentPrototype> _gaslessExtinguisherReagent = "KsTestGaslessExtinguisher";

    private static readonly Vector2i Tile = Vector2i.Zero;

    [Test]
    public async Task TestExtinguishConservesEnergy()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        var protoMan = SProtoMan;

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = 1000f };
            mixture.SetMoles(Gas.Oxygen, 20f);
            mixture.SetMoles(Gas.Nitrogen, 80f);

            atmosphereSystem.SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var fire = chemicalFireSystem.SpawnChemicalFire("KsTestExtinguishEnergyFire", (gridUid, null), Tile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");
            Assert.That(atmosphereSystem.IsHotspotActive(gridUid, Tile), "Hotspot is not active.");

            var air = atmosphereSystem.GetTileMixture(gridUid, null, Tile, true);
            Assert.That(air, Is.Not.Null);

            var reaction = protoMan.Index(WaterReagent).TileReactions
                .OfType<ExtinguishTileReaction>().Single();

            var reactVolume = FixedPoint2.New(1.67f);
            var capacityBefore = atmosphereSystem.GetHeatCapacity(air!, applyScaling: true);
            var temperatureBefore = air!.Temperature;
            Assert.That(temperatureBefore, Is.GreaterThan(900f), "Test tile did not start hot.");

            var used = reaction.TileReact(testMap.Tile, protoMan.Index(WaterReagent), reactVolume, entityManager, null);

            var capacityAfter = atmosphereSystem.GetHeatCapacity(air, applyScaling: true);
            var energyBefore = capacityBefore * temperatureBefore;
            var energyAfter = capacityAfter * air.Temperature + (float) used * reaction.EnergyPerUnit;

            // Same target the reaction aims for (default coolingTemperature of 2), whatever HeatScale is.
            // The cooling floor is the reagent's boiling point.
            var waterBoilingPoint = protoMan.Index(WaterReagent).BoilingPoint;
            Assert.That(waterBoilingPoint, Is.Not.Null, "Water has no boiling point.");
            var boilingFloor = waterBoilingPoint!.Value;
            var target = MathF.Max(MathF.Min(temperatureBefore - 2000f, temperatureBefore / 2f), boilingFloor);
            // Boiled water adds heat capacity, so the air lands at or just under the target when it is reached.
            var targetFloor = capacityBefore * target / capacityAfter;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(used, Is.GreaterThan(FixedPoint2.Zero), "Reaction consumed no water.");
                Assert.That(used, Is.LessThanOrEqualTo(reactVolume));
                Assert.That(air.Temperature, Is.LessThan(temperatureBefore), "Air was not cooled.");

                if (used < reactVolume)
                {
                    // Enough water: the air should reach the target (to within the 0.01u rounding of the water used).
                    var roundingSlack = 0.01f * reaction.EnergyPerUnit / capacityAfter + 1f;
                    Assert.That(air.Temperature, Is.InRange(targetFloor - roundingSlack, target + roundingSlack),
                        "Air did not reach the target temperature.");
                }
                else
                {
                    Assert.That(air.Temperature, Is.GreaterThan(target), "Air overshot the target with all the water spent.");
                }

                // C_before*T_before = C_after*T_after + used*EnergyPerUnit; the consumed volume is quantised
                //     before the energy is computed, so this holds to float precision.
                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 1e-3f + 1f),
                    "Thermal energy was not conserved.");
            }
        });

        await AssertHotspotOutAfterTicks(server, atmosphereSystem, gridUid);
    }

    /// <summary>
    ///     The extinguished chemfire is only queued for deletion, so the hotspot is still reported until the next tick.
    /// </summary>
    private static async Task AssertHotspotOutAfterTicks(RobustIntegrationTest.ServerIntegrationInstance server, AtmosphereSystem atmosphereSystem, EntityUid gridUid)
    {
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(atmosphereSystem.IsHotspotActive(gridUid, Tile), Is.False, "Hotspot survived.");
        });
    }

    /// <summary>
    ///     A reagent with no matching gas (a drink, say) cannot boil off: it extinguishes the fire but neither
    ///         cools the air, adds moles, nor reports any volume as consumed.
    /// </summary>
    [Test]
    public async Task TestExtinguishWithoutGasOnlyExtinguishes()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        var protoMan = SProtoMan;

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = 1000f };
            mixture.SetMoles(Gas.Oxygen, 20f);
            mixture.SetMoles(Gas.Nitrogen, 80f);

            atmosphereSystem.SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var fire = chemicalFireSystem.SpawnChemicalFire("KsTestExtinguishEnergyFire", (gridUid, null), Tile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");
            Assert.That(atmosphereSystem.IsHotspotActive(gridUid, Tile), "Hotspot is not active.");

            var air = atmosphereSystem.GetTileMixture(gridUid, null, Tile, true);
            Assert.That(air, Is.Not.Null);

            var reagent = protoMan.Index(_gaslessExtinguisherReagent);
            var reaction = reagent.TileReactions.OfType<ExtinguishTileReaction>().Single();
            Assert.That(ExtinguishTileReaction.GetBoiledGas(atmosphereSystem, reagent), Is.Null,
                "Test reagent unexpectedly has a gas.");

            var temperatureBefore = air!.Temperature;
            var molesBefore = air.TotalMoles;
            var waterVaporBefore = air.GetMoles(Gas.WaterVapor);

            var used = reaction.TileReact(testMap.Tile, reagent, FixedPoint2.New(1.67f), entityManager, null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(used, Is.EqualTo(FixedPoint2.Zero), "A gasless reagent consumed volume.");
                Assert.That(air.Temperature, Is.EqualTo(temperatureBefore), "Air temperature changed.");
                Assert.That(air.TotalMoles, Is.EqualTo(molesBefore), "Air moles changed.");
                Assert.That(air.GetMoles(Gas.WaterVapor), Is.EqualTo(waterVaporBefore), "Vapor was added.");
            }
        });

        await AssertHotspotOutAfterTicks(server, atmosphereSystem, gridUid);
    }
}
