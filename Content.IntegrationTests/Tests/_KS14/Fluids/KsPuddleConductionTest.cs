#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluids;

/// <summary>
///     Puddles on neighbouring tiles conduct heat into each other directly (<c>PuddleSystem.Klovn.Conduction.cs</c>).
/// </summary>
public sealed class KsPuddleConductionTest : GameTest
{
    private static readonly EntProtoId WallPrototype = "WallSolid";
    private const int TicksPerSecond = 30;
    private const float HotTemperature = 360f;
    private const float ColdTemperature = 280f;

    // Deliberately NOT static: it only exists in this test's YAML, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _inertReagent = "KsTestPuddleConductionInert";

    // No boiling or melting point, so nothing but heat exchange changes its temperature. A low specific heat makes a small
    //     puddle of it settle with whatever it touches within one exchange.
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestPuddleConductionInert
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  specificHeat: 0.2
";

    /// <summary>
    ///     Two touching puddles head for the same temperature, and the heat one loses is the heat the other gains. Two with bare
    ///         floor between them don't. There is no air on the grid, so nothing else moves heat between them.
    /// </summary>
    [Test]
    public async Task TestNeighbouringPuddlesShareHeat([Values(true, false)] bool adjacent)
    {
        await OverrideCVar(Side.Server, Content.Shared._KS14.CCVar.KsCCVars.AtmosPuddleEmissivity, 0f); // Only their shared edge moves heat here.
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var otherTile = adjacent ? new Vector2i(1, 0) : new Vector2i(2, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.HasComponent<GridAtmosphereComponent>(testMap.Grid.Owner), Is.False,
                "The grid has air, which would carry heat between the puddles too.");

            var mapSystem = entityManager.System<SharedMapSystem>();
            for (var x = 0; x <= 2; x++)
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
        });

        EntityUid hotUid = default;
        EntityUid coldUid = default;
        await server.WaitPost(() =>
        {
            var puddleSystem = entityManager.System<PuddleSystem>();
            var mapSystem = entityManager.System<SharedMapSystem>();
            puddleSystem.TrySpillAt(mapSystem.GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(0, 0)),
                new Solution(_inertReagent.Id, FixedPoint2.New(15)) { Temperature = HotTemperature }, out hotUid, sound: false);
            puddleSystem.TrySpillAt(mapSystem.GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, otherTile),
                new Solution(_inertReagent.Id, FixedPoint2.New(15)) { Temperature = ColdTemperature }, out coldUid, sound: false);
        });

        await server.WaitRunTicks(TicksPerSecond * 10);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutionContainerSystem.TryGetSolution(hotUid, "puddle", out _, out var hot), Is.True);
            Assert.That(solutionContainerSystem.TryGetSolution(coldUid, "puddle", out _, out var cold), Is.True);

            var initialDifference = HotTemperature - ColdTemperature;
            var difference = hot!.Temperature - cold!.Temperature;
            if (!adjacent)
            {
                Assert.That(difference, Is.EqualTo(initialDifference).Within(0.01f), "Puddles that don't touch should keep their heat.");
                return;
            }

            Assert.That(difference, Is.LessThan(initialDifference * 0.25f), "Touching puddles should have shared most of their heat.");
            Assert.That(difference, Is.GreaterThanOrEqualTo(0f), "Conduction should never carry heat past the shared temperature.");

            // Same volume of the same reagent, so the mean temperature is the conserved energy.
            Assert.That((hot.Temperature + cold.Temperature) / 2f, Is.EqualTo((HotTemperature + ColdTemperature) / 2f).Within(0.1f));
        });
    }

    /// <summary>
    ///     A puddle shares its capacity between the contacts it has, and a sealed tile has no air to count as one. So puddles
    ///         on sealed tiles settle with each other sooner than the same pair on open tiles.
    /// </summary>
    [Test]
    public async Task SealedPuddlesKeepNoCapacityBackForAir()
    {
        await OverrideCVar(Side.Server, Content.Shared._KS14.CCVar.KsCCVars.AtmosPuddleEmissivity, 0f); // So that the open tiles' air takes nothing itself.
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        var mapSystem = entityManager.System<SharedMapSystem>();
        var sealedTiles = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var openTiles = new[] { new Vector2i(4, 0), new Vector2i(5, 0) };

        await server.WaitPost(() =>
        {
            entityManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
            for (var x = 0; x <= 5; x++)
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);

            foreach (var tile in sealedTiles)
                entityManager.SpawnEntity(WallPrototype, mapSystem.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, tile));
        });
        await server.WaitRunTicks(TicksPerSecond * 5);

        var puddleUids = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var tile in sealedTiles)
            {
                Assert.That(entityManager.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, testMap.MapUid, tile), Is.Null,
                    "The wall's tile has air, so this proves nothing.");
            }

            foreach (var tiles in new[] { sealedTiles, openTiles })
            {
                for (var i = 0; i < tiles.Length; i++)
                {
                    var solution = new Solution(_inertReagent.Id, FixedPoint2.New(15)) { Temperature = i == 0 ? HotTemperature : ColdTemperature };
                    Assert.That(entityManager.System<PuddleSystem>().TrySpillAt(mapSystem.GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, tiles[i]), solution,
                        out var puddleUid, sound: false));
                    puddleUids.Add(puddleUid);
                }
            }
        });

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
            float Difference(int first)
            {
                Assert.That(solutionContainerSystem.TryGetSolution(puddleUids[first], "puddle", out _, out var hot), Is.True);
                Assert.That(solutionContainerSystem.TryGetSolution(puddleUids[first + 1], "puddle", out _, out var cold), Is.True);
                return hot!.Temperature - cold!.Temperature;
            }

            Assert.That(Difference(2), Is.GreaterThan(0.1f), "The open pair has already settled, so this proves nothing.");
            Assert.That(Difference(0), Is.LessThan(Difference(2) * 0.75f), "Puddles on sealed tiles held back capacity for air that isn't there.");
        });
    }

    /// <summary>
    ///     A small hot puddle between cold puddles, in cold air, cools towards them and stops there. Its exchanges with the
    ///         air and with its neighbours are each worked out from the same starting temperature and applied together, so
    ///         if each could take it all the way to its partner, together they would carry it far below both, past
    ///         absolute zero.
    /// </summary>
    [Test]
    public async Task TestPuddleBetweenColdNeighboursInColdAirStopsAtTheirTemperature()
    {
        const float startTemperature = 400f;
        const float surroundingTemperature = 120f;

        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        Vector2i[] neighbours = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

        await server.WaitPost(() =>
        {
            var mapSystem = entityManager.System<SharedMapSystem>();
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                    mapSystem.SetTile(gridUid, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);
        });

        await server.WaitRunTicks(TicksPerSecond);

        EntityUid hotUid = default;
        await server.WaitPost(() =>
        {
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var mixture = atmosphereSystem.GetTileMixture((gridUid, null, null), null, new Vector2i(x, y), excite: true)!;
                    mixture.Clear();
                    mixture.SetMoles(Gas.Nitrogen, Atmospherics.MolesCellStandard);
                    mixture.Temperature = surroundingTemperature;
                }
            }

            var puddleSystem = entityManager.System<PuddleSystem>();
            var mapSystem = entityManager.System<SharedMapSystem>();
            foreach (var neighbour in neighbours)
            {
                puddleSystem.TrySpillAt(mapSystem.GetTileRef(gridUid, testMap.Grid.Comp, neighbour),
                    new Solution(_inertReagent.Id, FixedPoint2.New(15)) { Temperature = surroundingTemperature }, out _, sound: false);
            }

            puddleSystem.TrySpillAt(mapSystem.GetTileRef(gridUid, testMap.Grid.Comp, Vector2i.Zero),
                new Solution(_inertReagent.Id, FixedPoint2.New(0.5f)) { Temperature = startTemperature }, out hotUid, sound: false);
        });

        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var coldest = float.MaxValue;
        for (var tick = 0; tick < TicksPerSecond * 3; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                Assert.That(solutionContainerSystem.TryGetSolution(hotUid, "puddle", out _, out var hot), Is.True);
                coldest = MathF.Min(coldest, hot!.Temperature);
            });
        }

        // The cold puddles and air warm a little as it cools, so it settles a little above where they started.
        Assert.That(coldest, Is.GreaterThanOrEqualTo(surroundingTemperature - 0.5f),
            $"The puddle fell to {coldest} K, below the {surroundingTemperature} K of everything it touches.");
        Assert.That(coldest, Is.LessThan(surroundingTemperature + 20f), "The puddle didn't cool, so this proves nothing.");
    }
}
