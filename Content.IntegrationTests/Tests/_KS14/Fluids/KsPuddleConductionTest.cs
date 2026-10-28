#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Fluids.EntitySystems;
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
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private const int TicksPerSecond = 30;
    private const float HotTemperature = 360f;
    private const float ColdTemperature = 280f;

    /// <summary>
    ///     Two touching puddles head for the same temperature, and the heat one loses is the heat the other gains. Two with bare
    ///         floor between them don't. There is no air on the grid, so nothing else moves heat between them.
    /// </summary>
    [Test]
    public async Task TestNeighbouringPuddlesShareHeat([Values(true, false)] bool adjacent)
    {
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
                new Solution(WaterReagent.Id, FixedPoint2.New(15)) { Temperature = HotTemperature }, out hotUid, sound: false);
            puddleSystem.TrySpillAt(mapSystem.GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, otherTile),
                new Solution(WaterReagent.Id, FixedPoint2.New(15)) { Temperature = ColdTemperature }, out coldUid, sound: false);
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
}
