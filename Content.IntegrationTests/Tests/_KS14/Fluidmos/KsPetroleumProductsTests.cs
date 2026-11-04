#nullable enable
using Content.Server.Chemistry.TileReactions;
using System.Linq;
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
    ///     Far more than any of these reactions costs, so that what they use is free to change.
    /// </summary>
    private const float Spill = 1000f;

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

    /// <summary>
    ///     No machine can pick a cut clean, so a tank holding any of the group's reagents has to run.
    /// </summary>
    [Test]
    public async Task NoPetroleumCutCloggsTheGenerator()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var generatorUid = server.EntMan.SpawnEntity(GeneratorPrototype, testMap.GridCoords);
            var adapter = server.EntMan.GetComponent<ChemicalFuelGeneratorAdapterComponent>(generatorUid);
            Assert.That(solutionSystem.TryGetSolution(generatorUid, adapter.SolutionName, out var tankEntity, out _));

            var cut = new Solution();
            foreach (var reagent in server.ProtoMan.EnumeratePrototypes<ReagentPrototype>())
            {
                if (reagent.KsInseparableGroup == server.ProtoMan.Index(GasolineReagent).KsInseparableGroup)
                    cut.AddReagent(reagent.ID, FixedPoint2.New(1));
            }

            Assert.That(solutionSystem.TryAddSolution(tankEntity!.Value, cut), "The cut didn't fit in the tank.");

            var ev = new GeneratorGetCloggedEvent();
            server.EntMan.EventBus.RaiseLocalEvent(generatorUid, ref ev);
            Assert.That(ev.Clogged, Is.False, "Part of a petroleum cut clogs the generator.");
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
            var solution = Spilled(PolyethyleneReagent, Spill);
            puddleSystem.DoTileReactions(testMap.Tile, solution);
            Assert.That(CountOnMap(testMap, WallPrototype), Is.EqualTo(1), "Spilled polyethylene didn't set into a wall.");
            var afterWall = (float)solution.Volume;
            Assert.That(afterWall, Is.LessThan(Spill), "The wall didn't use the reagent up.");

            puddleSystem.DoTileReactions(testMap.Tile, solution);
            Assert.That(CountOnMap(testMap, WallPrototype), Is.EqualTo(1), "A second wall grew on the first.");
            Assert.That((float)solution.Volume, Is.EqualTo(afterWall).Within(0.01f), "A wall that wasn't built used the reagent up.");
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
            var solution = Spilled(BitumenReagent, Spill);
            puddleSystem.DoTileReactions(testMap.Tile, solution);

            var tile = mapSystem.GetTileRef(testMap.Grid, testMap.Tile.GridIndices);
            Assert.That(turfSystem.GetContentTileDefinition(tile).ID, Is.EqualTo(AsphaltTile), "Spilled bitumen didn't pave the floor.");
            var afterPaving = (float)solution.Volume;
            Assert.That(afterPaving, Is.LessThan(Spill), "Paving didn't use the bitumen up.");

            puddleSystem.DoTileReactions(tile, solution);
            Assert.That((float)solution.Volume, Is.EqualTo(afterPaving).Within(0.01f), "Bitumen was used up on tile that was already asphalt.");
        });
    }

    [Test]
    public async Task BitumenLeavesIndestructibleFloorsAlone()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var puddleSystem = server.System<SharedPuddleSystem>();
        var turfSystem = server.System<TurfSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var indestructible = server.ProtoMan.EnumeratePrototypes<ContentTileDefinition>().First(tile => tile.Indestructible && !tile.MapAtmosphere);
            mapSystem.SetTile(testMap.Grid, testMap.Tile.GridIndices, new Tile(indestructible.TileId));

            var tile = mapSystem.GetTileRef(testMap.Grid, testMap.Tile.GridIndices);
            puddleSystem.DoTileReactions(tile, Spilled(BitumenReagent, Spill));

            tile = mapSystem.GetTileRef(testMap.Grid, testMap.Tile.GridIndices);
            Assert.That(turfSystem.GetContentTileDefinition(tile).ID, Is.EqualTo(indestructible.ID), "Bitumen replaced an indestructible floor.");
        });
    }

    [Test]
    public async Task SpilledWaxStopsPilingUpCandles()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var puddleSystem = server.System<SharedPuddleSystem>();

        await server.WaitAssertion(() =>
        {
            var reaction = server.ProtoMan.Index(WaxReagent).TileReactions!.OfType<CreateEntityTileReaction>().Single();
            for (var i = 0; i < reaction.MaxOnTile * 3; i++)
            {
                puddleSystem.DoTileReactions(testMap.Tile, Spilled(WaxReagent, Spill));
            }

            Assert.That(CountOnMap(testMap, CandlePrototype), Is.EqualTo(reaction.MaxOnTile), "Wax piled up candles past its limit.");
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
            puddleSystem.DoTileReactions(testMap.Tile, Spilled(WaxReagent, Spill));
            Assert.That(CountOnMap(testMap, CandlePrototype), Is.GreaterThan(0), "Spilled wax didn't set into a candle.");
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

            Assert.That((float)beaker!.GetTotalPrototypeQuantity(NapalmReagent), Is.GreaterThan(0f), "Gasoline and polyethylene didn't make napalm.");
        });
    }
}
