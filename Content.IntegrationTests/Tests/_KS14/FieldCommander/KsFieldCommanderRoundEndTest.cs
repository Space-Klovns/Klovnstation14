#nullable enable
using System.Linq;
using Content.Client.RoundEnd;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     When the last defender dies, the round ends, and the summary opens on the client, every round.
/// </summary>
[TestFixture]
public sealed class KsFieldCommanderRoundEndTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, DummyTicker = false, Dirty = true };

    private static readonly EntProtoId ScenarioRule = "KsScenarioFieldCommander";
    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _commanderRule = "KsTestFieldCommanderRoundEndRule";
    private readonly EntProtoId _defender = "KsTestFieldCommanderRoundEndDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderRoundEndDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  parent: BaseGameRule
  id: KsTestFieldCommanderRoundEndRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 1000
";

    private bool IsSummaryOpen()
    {
        return Pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<RoundEndSummaryWindow>()
            .Any(window => window.IsOpen);
    }

    private async Task LoseRound()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid defenderUid = default;

        await server.WaitPost(() =>
        {
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            defenderUid = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, defenderUid);

            KsFieldCommanderTestHelpers.StartRules(server, _commanderRule);
        });

        await Pair.RunTicksSync(5);

        // The only defender dies: the commander wins by decimation.
        await server.WaitPost(() => server.System<MobStateSystem>().ChangeMobState(defenderUid, MobState.Dead));
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(server.System<GameTicker>().RunLevel, Is.EqualTo(GameRunLevel.PostRound), "the last defender dying should end the round"));
    }

    [Test]
    public async Task SummaryOpensWhenTheLastDefenderDiesEveryRound()
    {
        var server = Pair.Server;

        for (var round = 0; round < 2; round++)
        {
            await LoseRound();

            await Pair.Client.WaitAssertion(() =>
                Assert.That(IsSummaryOpen(), Is.True, $"the round end summary should open in round {round + 1}"));

            await server.WaitPost(() => server.System<GameTicker>().RestartRound());
            await Pair.RunTicksSync(30);
            await Pair.RunUntilSynced();
        }
    }
}
