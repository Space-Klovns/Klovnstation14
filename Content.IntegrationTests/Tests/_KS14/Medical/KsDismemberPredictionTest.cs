#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Klovnmed;
using Content.Shared.Body;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Medical;

/// <summary>
///     Which body part a dismemberment takes is rolled on the client for prediction and again on the server.
///         The roll indexes a list of the body's parts, so it only agrees if both sides build that list in the
///         same order, and <see cref="BodyComponent.RecursiveChildUids"/> is not networked, so the client's order
///         is whatever order its entity states were applied in.
/// </summary>
[TestFixture]
[TestOf(typeof(OrganSearchSystem))]
public sealed class KsDismemberPredictionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId HumanProto = "MobHuman";

    private const BodyPartType Limbs = BodyPartType.Arm | BodyPartType.Hand | BodyPartType.Leg | BodyPartType.Foot;

    private const uint FirstTick = 1000;
    private const int Rolls = 40;

    /// <summary>
    ///     The net ids of the part chosen on each of <see cref="Rolls"/> consecutive ticks.
    /// </summary>
    private List<int> RollOnEachTick(IEntityManager entityManager, IGameTiming timing, EntityUid bodyUid)
    {
        var searchSystem = entityManager.System<OrganSearchSystem>();
        var bodyComponent = entityManager.GetComponent<BodyComponent>(bodyUid);
        var originalTick = timing.CurTick;
        var chosen = new List<int>();

        try
        {
            for (var tick = FirstTick; tick < FirstTick + Rolls; tick++)
            {
                timing.CurTick = new GameTick(tick);
                Assert.That(searchSystem.TryGetRandomBodyPartOfType((bodyUid, bodyComponent), Limbs, out var partUid), Is.True);
                chosen.Add(entityManager.GetNetEntity(partUid!.Value).Id);
            }
        }
        finally
        {
            timing.CurTick = originalTick;
        }

        return chosen;
    }

    [Test]
    public async Task TestClientAndServerChooseTheSamePartWhateverOrderTheClientListsThem()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();

        EntityUid serverBody = default;
        await server.WaitPost(() => serverBody = SEntMan.SpawnEntity(HumanProto, testMap.GridCoords));
        await Pair.RunTicksSync(10);

        var clientBody = Pair.ToClientUid(serverBody);
        var serverTiming = server.ResolveDependency<IGameTiming>();
        var clientTiming = client.ResolveDependency<IGameTiming>();

        var serverRolls = new List<int>();
        await server.WaitPost(() =>
        {
            var bodyComponent = SEntMan.GetComponent<BodyComponent>(serverBody);
            var serverChildren = bodyComponent.RecursiveChildUids;
            Assert.That(serverChildren, Has.Count.GreaterThan(4), "The body has no parts to choose from.");
            serverRolls = RollOnEachTick(SEntMan, serverTiming, serverBody);
        });

        // Both orders: how the client happens to have them, and the reverse, which a different order of state
        //      application could just as well have produced.
        var clientRolls = new List<int>();
        var reversedClientRolls = new List<int>();
        await client.WaitPost(() =>
        {
            var bodyComponent = CEntMan.GetComponent<BodyComponent>(clientBody);
            clientRolls = RollOnEachTick(CEntMan, clientTiming, clientBody);

            var children = bodyComponent.RecursiveChildUids;
            children.Reverse();
            reversedClientRolls = RollOnEachTick(CEntMan, clientTiming, clientBody);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverRolls.Distinct().Count(), Is.GreaterThan(1), "The roll always lands on one part, so this proves nothing.");
            Assert.That(clientRolls, Is.EqualTo(serverRolls), "The client chose different parts to the server.");
            Assert.That(reversedClientRolls, Is.EqualTo(serverRolls), "Listing the parts in another order changed which one was chosen.");
        }
    }
}
