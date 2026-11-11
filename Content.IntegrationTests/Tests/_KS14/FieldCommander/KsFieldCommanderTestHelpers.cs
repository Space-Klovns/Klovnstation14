#nullable enable
using Content.Server.GameTicking;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Setup the field commander tests share. Each runs on the server's game thread, inside <c>WaitPost</c>.
/// </summary>
public static class KsFieldCommanderTestHelpers
{
    private static readonly EntProtoId ScenarioRule = "Scenario";

    /// <summary>
    ///     Lays the test map's floor tile over the square <paramref name="halfExtent"/> tiles either side of the origin.
    /// </summary>
    public static void FillFloor(RobustIntegrationTest.ServerIntegrationInstance server, TestMapData testMap, int halfExtent)
    {
        var mapSystem = server.System<SharedMapSystem>();
        for (var x = -halfExtent; x <= halfExtent; x++)
        {
            for (var y = -halfExtent; y <= halfExtent; y++)
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
        }
    }

    /// <summary>
    ///     Starts the scenario rule and then <paramref name="commanderRule"/>, as the field commander preset does.
    /// </summary>
    public static (EntityUid ScenarioUid, EntityUid CommanderUid) StartRules(RobustIntegrationTest.ServerIntegrationInstance server, EntProtoId commanderRule)
    {
        var ticker = server.System<GameTicker>();
        Assert.That(ticker.StartGameRule(ScenarioRule, out var scenarioUid), Is.True);
        Assert.That(ticker.StartGameRule(commanderRule, out var commanderUid), Is.True);
        return (scenarioUid, commanderUid);
    }
}
