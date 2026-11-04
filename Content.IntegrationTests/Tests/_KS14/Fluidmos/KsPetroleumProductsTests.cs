#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Power.Generator;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Fluids;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     What the refinery is for: refined fuels run a generator, and spilled products set into walls, asphalt and candles.
/// </summary>
[TestOf(typeof(ChemicalFuelGeneratorAdapterComponent))]
public sealed class KsPetroleumProductsTests : GameTest
{
    private static readonly EntProtoId GeneratorPrototype = "PortableGeneratorJrPacman";
    private static readonly EntProtoId WallPrototype = "WallPlastic";
    private static readonly EntProtoId CandlePrototype = "Candle";
    private static readonly EntProtoId BeakerPrototype = "Beaker";

    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";
    private static readonly ProtoId<ReagentPrototype> GasolineReagent = "KsGasoline";
    private static readonly ProtoId<ReagentPrototype> PolyethyleneReagent = "KsPolyethylene";
    private static readonly ProtoId<ReagentPrototype> BitumenReagent = "KsBitumen";
    private static readonly ProtoId<ReagentPrototype> WaxReagent = "KsParaffinWax";
    private static readonly ProtoId<ReagentPrototype> NapalmReagent = "Napalm";

    private const string AsphaltTile = "FloorAsphalt";

    /// <summary>
    ///     How many of a prototype are on <paramref name="testMap"/>, the only map these tests build on.
    /// </summary>
    private int CountOnMap(TestMapData testMap, EntProtoId prototype)
    {
        var server = Pair.Server;
        var count = 0;
        var query = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var metaDataComponent, out var transformComponent))
        {
            if (metaDataComponent.EntityPrototype?.ID == prototype.Id && transformComponent.MapID == testMap.MapId)
                count++;
        }

        return count;
    }

    private static Solution Spilled(ProtoId<ReagentPrototype> reagent, float units)
    {
        var solution = new Solution();
        solution.AddReagent(reagent, FixedPoint2.New(units));
        return solution;
    }

    [Test]
    public async Task GeneratorTakesRefinedFuelAndStillTakesWeldingFuel()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.ProtoMan.Index(GeneratorPrototype).TryComp<ChemicalFuelGeneratorAdapterComponent>(out var adapter, server.EntMan.ComponentFactory));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(adapter!.Reagents, Does.ContainKey(WeldingFuelReagent), "The partial replaced upstream's fuel instead of adding to it.");
                Assert.That(adapter.Reagents, Does.ContainKey(GasolineReagent), "The partial didn't add gasoline.");
                Assert.That(adapter.Reagents[GasolineReagent], Is.GreaterThan(adapter.Reagents[WeldingFuelReagent]), "Refined fuel isn't worth the refining.");
            }
        });
    }

    [Test]
    public async Task SpilledPolyethyleneSetsIntoOneWallPerTile()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var puddleSystem = server.System<SharedPuddleSystem>();

        await server.WaitAssertion(() =>
        {
            var solution = Spilled(PolyethyleneReagent, 120f);
            puddleSystem.DoTileReactions(testMap.Tile, solution);
            Assert.That(CountOnMap(testMap, WallPrototype), Is.EqualTo(1), "Spilled polyethylene didn't set into a wall.");
            Assert.That((float)solution.Volume, Is.EqualTo(70f).Within(0.01f), "The wall didn't use the reagent up.");

            puddleSystem.DoTileReactions(testMap.Tile, solution);
            Assert.That(CountOnMap(testMap, WallPrototype), Is.EqualTo(1), "A second wall grew on the first.");
            Assert.That((float)solution.Volume, Is.EqualTo(70f).Within(0.01f), "A wall that wasn't built used the reagent up.");
        });
    }

    [Test]
    public async Task SpilledBitumenPavesTheFloorOnce()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var puddleSystem = server.System<SharedPuddleSystem>();
        var turfSystem = server.System<TurfSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var solution = Spilled(BitumenReagent, 50f);
            puddleSystem.DoTileReactions(testMap.Tile, solution);

            var tile = mapSystem.GetTileRef(testMap.Grid, testMap.Tile.GridIndices);
            Assert.That(turfSystem.GetContentTileDefinition(tile).ID, Is.EqualTo(AsphaltTile), "Spilled bitumen didn't pave the floor.");
            Assert.That((float)solution.Volume, Is.EqualTo(30f).Within(0.01f));

            puddleSystem.DoTileReactions(tile, solution);
            Assert.That((float)solution.Volume, Is.EqualTo(30f).Within(0.01f), "Bitumen was used up on tile that was already asphalt.");
        });
    }

    [Test]
    public async Task SpilledWaxSetsIntoACandle()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var puddleSystem = server.System<SharedPuddleSystem>();

        await server.WaitAssertion(() =>
        {
            puddleSystem.DoTileReactions(testMap.Tile, Spilled(WaxReagent, 30f));
            Assert.That(CountOnMap(testMap, CandlePrototype), Is.EqualTo(1), "Spilled wax didn't set into a candle.");
        });
    }

    [Test]
    public async Task GasolineAndPolyethyleneMakeNapalmInABeaker()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var beakerUid = server.EntMan.SpawnEntity(BeakerPrototype, testMap.GridCoords);
            Assert.That(solutionSystem.TryGetSolution(beakerUid, "beaker", out var beakerEntity, out var beaker));
            solutionSystem.TryAddReagent(beakerEntity!.Value, GasolineReagent, FixedPoint2.New(20), out _);
            solutionSystem.TryAddReagent(beakerEntity.Value, PolyethyleneReagent, FixedPoint2.New(10), out _);

            Assert.That((float)beaker!.GetTotalPrototypeQuantity(NapalmReagent), Is.EqualTo(30f).Within(0.01f), "Gasoline and polyethylene didn't make napalm.");
        });
    }
}
