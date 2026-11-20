#nullable enable
using Content.Client._KS14.FieldCommander;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A dead defender watching as a ghost still sees their requisition and the wave timer.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderGhostHudTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcGhostHudRule";
    private readonly EntProtoId _defender = "KsTestFcGhostHudDefender";
    private readonly EntProtoId _ghost = "KsTestFcGhostHudGhost";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcGhostHudDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcGhostHudGhost

- type: entity
  parent: BaseGameRule
  id: KsTestFcGhostHudRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 1000
";

    [Test]
    public async Task GhostSeesItsPointsAndTheWaveTimer()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        const int points = 123;

        await server.WaitPost(() =>
        {
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            var body = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, body);
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            server.System<KsFieldCommanderRuleSystem>().SetPoints((ruleUid, component), Pair.Player!, points);

            // Dead and watching.
            var ghost = server.EntMan.SpawnAtPosition(_ghost, new EntityCoordinates(testMap.Grid, 1.5f, 1.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, ghost);
        });

        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var pointsSystem = client.System<KsFieldCommanderPointsSystem>();
            Assert.That(pointsSystem.TryGetLocalPoints(out var shown), Is.True, "a ghost should be shown its requisition");
            Assert.That(shown, Is.EqualTo(points));
            Assert.That(pointsSystem.Control?.WaveText, Is.Not.Null, "a ghost should be shown the wave timer");
        });
    }
}
