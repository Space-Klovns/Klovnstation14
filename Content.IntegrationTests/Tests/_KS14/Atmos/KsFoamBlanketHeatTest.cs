#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     A foam blanket holds heat: it is laid at the temperature of what laid it, conducts heat from the puddle under it
///         and on to the air over it, radiating as well, and insulates the puddle from the air.
/// </summary>
[TestOf(typeof(KsFoamBlanketSystem))]
public sealed class KsFoamBlanketHeatTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# A liquid that stays one: no boiling and no tile effects, so only the air takes its heat.
- type: reagent
  id: KsTestHeatProbeReagent
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  boilingPoint: 5000
  meltingPoint: 1 # Kelvin: never freezes
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _probeReagent = "KsTestHeatProbeReagent";

    private static readonly EntProtoId BlanketPrototype = "KsFoamBlanket";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    private const int TicksPerSecond = 30;

    /// <summary>
    ///     Below the foam's boiling point, so that nothing here boils and only conduction and radiation move heat.
    /// </summary>
    private const float WarmTemperature = 360f;

    private static readonly Vector2i CoveredTile = new(0, 0);
    private static readonly Vector2i BareTile = new(4, 0);

    private async Task<TestMapData> CreateBreathableTestMap()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = server.EntMan.System<SharedMapSystem>();
            for (var x = -1; x <= BareTile.X + 1; x++)
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    private TileRef TileRef(TestMapData testMap, Vector2i tile)
        => Pair.Server.EntMan.System<SharedMapSystem>().GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, tile);

    private void FillBlanket(TestMapData testMap, Vector2i tile, float? temperature = null)
    {
        var blanketSystem = Pair.Server.EntMan.System<KsFoamBlanketSystem>();
        for (var i = 0; i < 1000 && blanketSystem.Deploy(BlanketPrototype, TileRef(testMap, tile), 1f, playSound: false, temperature: temperature) > 0f; i++)
        {
        }
    }

    private EntityUid SpillWater(TestMapData testMap, Vector2i tile, float volume, float temperature)
        => Spill(testMap, tile, WaterReagent, volume, temperature);

    private EntityUid Spill(TestMapData testMap, Vector2i tile, ProtoId<ReagentPrototype> reagent, float volume, float temperature)
    {
        var spilled = new Solution(reagent.Id, FixedPoint2.New(volume)) { Temperature = temperature };
        Assert.That(Pair.Server.EntMan.System<PuddleSystem>().TrySpillAt(TileRef(testMap, tile), spilled, out var puddleUid, sound: false, tileReact: false),
            $"Puddle failed to spill on {tile}.");
        return puddleUid;
    }

    private float PuddleTemperature(EntityUid puddleUid)
    {
        var entityManager = Pair.Server.EntMan;
        var puddleComponent = entityManager.GetComponent<PuddleComponent>(puddleUid);
        Assert.That(entityManager.System<SharedSolutionContainerSystem>()
            .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution), "Puddle has no solution.");
        return solution!.Temperature;
    }

    private float BlanketTemperature(TestMapData testMap, Vector2i tile)
    {
        Assert.That(Pair.Server.EntMan.System<KsFoamBlanketSystem>().TryGetBlanket(testMap.Grid.Owner, tile, out var blanket), $"No blanket on {tile}.");
        return blanket.Comp.Heat.Temperature;
    }

    private float AirTemperature(TestMapData testMap, Vector2i tile)
        => Pair.Server.EntMan.System<AtmosphereSystem>().GetTileMixture((testMap.Grid.Owner, null, null), null, tile, excite: false)!.Temperature;

    /// <summary>
    ///     Foam is laid at the temperature of what laid it, and foam laid onto a blanket mixes in by how much of each
    ///         there is.
    /// </summary>
    [Test]
    public async Task TestFoamMixesInAtItsOwnTemperature()
    {
        var testMap = await CreateBreathableTestMap();
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var blanketSystem = server.EntMan.System<KsFoamBlanketSystem>();
            var tileRef = TileRef(testMap, BareTile);

            var first = blanketSystem.Deploy(BlanketPrototype, tileRef, 1f, playSound: false, temperature: 330f);
            Assert.That(BlanketTemperature(testMap, BareTile), Is.EqualTo(330f).Within(0.01f), "The foam was not laid at the temperature it came at.");

            var second = blanketSystem.Deploy(BlanketPrototype, tileRef, 1f, playSound: false, temperature: 290f);
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, BareTile, out var blanket));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(BlanketTemperature(testMap, BareTile), Is.EqualTo((330f * first + 290f * second) / (first + second)).Within(0.01f),
                    "Foam laid onto the blanket did not mix in by how much of each there was.");
                Assert.That(blanket.Comp.Heat.HeatCapacity, Is.EqualTo(blanket.Comp.Foam * blanket.Comp.SpecificHeat).Within(0.001f),
                    "The blanket's heat capacity is not its foam's.");
            }
        });
    }

    /// <summary>
    ///     A warm puddle under a blanket warms the foam, which passes the heat on to the air: the puddle cools, and the
    ///         foam ends between the two, without any of it boiling.
    /// </summary>
    [Test]
    public async Task TestBlanketCarriesPuddleHeatToTheAir()
    {
        var testMap = await CreateBreathableTestMap();
        var server = Pair.Server;
        EntityUid puddleUid = default;
        var foamBefore = 0f;
        var decay = 0f;

        await server.WaitPost(() =>
        {
            puddleUid = SpillWater(testMap, CoveredTile, 20f, WarmTemperature);
            FillBlanket(testMap, CoveredTile);
            Assert.That(server.EntMan.System<KsFoamBlanketSystem>().TryGetBlanket(testMap.Grid.Owner, CoveredTile, out var blanket));
            foamBefore = blanket.Comp.Foam;
            decay = blanket.Comp.DecayRate;
        });

        const int seconds = 10;
        await server.WaitRunTicks(TicksPerSecond * seconds);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.System<KsFoamBlanketSystem>().TryGetBlanket(testMap.Grid.Owner, CoveredTile, out var blanket));
            var blanketTemperature = blanket.Comp.Heat.Temperature;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(PuddleTemperature(puddleUid), Is.LessThan(WarmTemperature - 1f), "The blanket took no heat from the puddle under it.");
                Assert.That(blanketTemperature, Is.GreaterThan(AirTemperature(testMap, CoveredTile) + 1f), "The puddle did not warm the foam over it.");
                Assert.That(blanketTemperature, Is.LessThan(PuddleTemperature(puddleUid)), "The foam is hotter than the puddle that warmed it.");
                // Only draining: the puddle is below the boiling point, so nothing boiled.
                Assert.That(blanket.Comp.Foam, Is.EqualTo(foamBefore - decay * seconds).Within(decay * 2f), "Foam boiled off a puddle below its boiling point.");
            }
        });
    }

    /// <summary>
    ///     A blanket insulates the puddle under it: a covered warm puddle cools more slowly than a bare one, its heat
    ///         reaching the air only through the foam.
    /// </summary>
    [Test]
    public async Task TestBlanketInsulatesPuddle()
    {
        var testMap = await CreateBreathableTestMap();
        var server = Pair.Server;
        EntityUid coveredUid = default;
        EntityUid bareUid = default;

        await server.WaitPost(() =>
        {
            coveredUid = SpillWater(testMap, CoveredTile, 20f, WarmTemperature);
            bareUid = SpillWater(testMap, BareTile, 20f, WarmTemperature);
            FillBlanket(testMap, CoveredTile);
        });

        await server.WaitRunTicks(TicksPerSecond * 10);

        await server.WaitAssertion(() =>
        {
            var covered = PuddleTemperature(coveredUid);
            var bare = PuddleTemperature(bareUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(bare, Is.LessThan(WarmTemperature - 1f), "The bare puddle did not cool, so this proves nothing.");
                Assert.That(covered, Is.GreaterThan(bare), "The covered puddle cooled as fast as the bare one: the blanket does not insulate it.");
            }
        });
    }

    /// <summary>
    ///     A hot bare puddle radiates heat to the air on top of conducting it, by the emissivity cvar.
    /// </summary>
    [Test]
    public async Task TestHotPuddleRadiates()
    {
        var testMap = await CreateBreathableTestMap();
        var server = Pair.Server;
        var temperatures = new float[2];

        for (var run = 0; run < 2; run++)
        {
            await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleEmissivity, run == 0 ? 0f : 0.9f);

            // A fresh tile each run, so neither inherits the other's warmed air.
            var tile = new Vector2i(run * 4, 0);
            EntityUid puddleUid = default;
            await server.WaitPost(() => puddleUid = Spill(testMap, tile, _probeReagent, 20f, 900f));
            await server.WaitRunTicks(TicksPerSecond * 3);
            await server.WaitAssertion(() =>
            {
                temperatures[run] = PuddleTemperature(puddleUid);
                server.EntMan.DeleteEntity(puddleUid);
            });
        }

        Assert.That(temperatures[1], Is.LessThan(temperatures[0] - 10f),
            $"A 900 K puddle cooled no faster radiating ({temperatures[1]} K) than not ({temperatures[0]} K).");
    }

    /// <summary>
    ///     A warm blanket with nothing under it cools to the room air's temperature.
    /// </summary>
    [Test]
    public async Task TestBlanketCoolsInRoomAir()
    {
        var testMap = await CreateBreathableTestMap();
        var server = Pair.Server;

        await server.WaitPost(() => FillBlanket(testMap, BareTile, temperature: WarmTemperature));
        await server.WaitRunTicks(TicksPerSecond * 5);

        await server.WaitAssertion(() =>
        {
            var blanketTemperature = BlanketTemperature(testMap, BareTile);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(blanketTemperature, Is.LessThan(WarmTemperature - 1f), "The blanket did not cool in room air.");
                // Its capacity is tiny, so it reaches the air's temperature within a second or so, and never passes it.
                Assert.That(blanketTemperature, Is.EqualTo(AirTemperature(testMap, BareTile)).Within(1f), "The blanket did not settle at the air's temperature.");
            }
        });
    }

    /// <summary>
    ///     Foam laid hotter than its boiling point boils off what its heat over that point boils, at once, rather than
    ///         being cooled back down by the air while staying liquid.
    /// </summary>
    [Test]
    public async Task TestSuperheatedFoamBoilsOff()
    {
        var testMap = await CreateBreathableTestMap();
        var server = Pair.Server;
        var expectedBoiled = 0f;

        await server.WaitPost(() =>
        {
            Assert.That(server.ProtoMan.Index(BlanketPrototype).TryComp<KsFoamBlanketComponent>(out var blanketPrototype, server.EntMan.ComponentFactory));
            FillBlanket(testMap, BareTile, temperature: blanketPrototype!.BoilingPoint + 50f);
            // A control laid at room temperature on a tile with no puddle either, so both drain alike.
            FillBlanket(testMap, CoveredTile);

            Assert.That(server.EntMan.System<KsFoamBlanketSystem>().TryGetBlanket(testMap.Grid.Owner, BareTile, out var blanket));
            expectedBoiled = blanket.Comp.Foam * blanket.Comp.SpecificHeat * 50f / blanket.Comp.LatentHeat;
        });

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            var blanketSystem = server.EntMan.System<KsFoamBlanketSystem>();
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, BareTile, out var blanket));
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, CoveredTile, out var control));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(blanket.Comp.Heat.Temperature, Is.LessThanOrEqualTo(blanket.Comp.BoilingPoint + 0.01f), "The foam stayed above its boiling point.");
                Assert.That(control.Comp.Foam - blanket.Comp.Foam, Is.EqualTo(expectedBoiled).Within(expectedBoiled * 0.1f),
                    "Foam above its boiling point did not boil off its heat over that point.");
            }
        });
    }
}
