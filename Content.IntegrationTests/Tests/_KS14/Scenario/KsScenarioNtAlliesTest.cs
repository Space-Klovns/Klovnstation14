#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Scenario;

/// <summary>
///     The Nanotrasen paramilitary NPCs fight alongside scenario Nanotrasen agents: what the agent's job makes of them
///         counts as friendly to the paramilitaries, so they never retaliate against the agents.
/// </summary>
[TestFixture]
public sealed class KsScenarioNtAlliesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    private static readonly ProtoId<JobPrototype> NanotrasenAgentJob = "ScenarioNt";

    // Exists only in the test prototypes below.
    private readonly EntProtoId _paramilitary = "KsTestNtParamilitary";
    private readonly EntProtoId _bareMob = "KsTestBareMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestNtParamilitary
  components:
  - type: NpcFactionMember
    factions:
    - KsNanoTrasenOperative

- type: entity
  id: KsTestBareMob
";

    [Test]
    public async Task ParamilitariesAreFriendlyToNanotrasenAgents()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var agentUid = server.EntMan.SpawnEntity(_bareMob, testMap.GridCoords);
            foreach (var special in server.ProtoMan.Index(NanotrasenAgentJob).Special)
                special.AfterEquip(agentUid);

            var paramilitaryUid = server.EntMan.SpawnEntity(_paramilitary, testMap.GridCoords);

            Assert.That(server.System<NpcFactionSystem>().IsEntityFriendly(paramilitaryUid, agentUid), Is.True,
                "a paramilitary must count a Nanotrasen agent as friendly, or it turns on them when hit");
        });
    }
}
