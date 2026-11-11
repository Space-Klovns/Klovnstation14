#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Scenario.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Scenario;

/// <summary>
///     A member's side reaches the client, which predicts what depends on it, such as being refused its own
///         objective (<see cref="Content.Shared._KS14.Scenario.Systems.ScenarioObjectiveGuardSystem"/>).
/// </summary>
[TestFixture]
[TestOf(typeof(ScenarioFactionMemberComponent))]
public sealed class KsScenarioFactionNetworkTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly ProtoId<ScenarioFactionPrototype> Syndicate = "Syndicate";

    // Exists only in the test prototypes below.
    private readonly EntProtoId _member = "KsTestScenarioNetworkedMember";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestScenarioNetworkedMember
";

    [Test]
    public async Task ClientKnowsTheMembersSide()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        EntityUid memberUid = default;

        // Added at runtime, as a job gives it to a player, so the client can't simply read it off the prototype.
        await server.WaitPost(() =>
        {
            memberUid = server.EntMan.SpawnEntity(_member, testMap.GridCoords);
            var memberComponent = server.EntMan.AddComponent<ScenarioFactionMemberComponent>(memberUid);
            memberComponent.Id = Syndicate;
            server.EntMan.Dirty(memberUid, memberComponent);
        });
        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var clientMemberUid = Pair.ToClientUid(memberUid);
            Assert.That(client.EntMan.GetComponent<ScenarioFactionMemberComponent>(clientMemberUid).Id, Is.EqualTo(Syndicate),
                "the client should know which side a member is on");
        });
    }
}
