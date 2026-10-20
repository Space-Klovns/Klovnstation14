#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Klovnmed;
using Content.Shared.Body;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Medical;

/// <summary>
///     Which body part a dismemberment takes is rolled on the client for prediction and again on the server.
///         The roll indexes a list of the body's parts, so it only agrees if both sides build that list in the
///         same order. <see cref="BodyComponent.RecursiveChildUids"/> is not networked: the client rebuilds it in
///         whatever order its entity states are applied, so it is kept sorted by net id on both sides.
/// </summary>
[TestFixture]
[TestOf(typeof(OrganSearchSystem)), TestOf(typeof(BodyHierarchySystem))]
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

    /// <summary>
    ///     An organ spawned before the body, so its net id is lower than every part the body came with, and inserted
    ///         after: appended, it would sit out of order at the end of the list.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestLooseOrgan
  components:
  - type: Organ
";

    private readonly EntProtoId _looseOrganProto = "KsTestLooseOrgan";

    private List<int> NetIds(IEntityManager entityManager, EntityUid bodyUid)
    {
        return entityManager.GetComponent<BodyComponent>(bodyUid).RecursiveChildUids
            .Select(uid => entityManager.GetNetEntity(uid).Id)
            .ToList();
    }

    [Test]
    public async Task TestClientAndServerListAndChooseThePartsInTheSameOrder()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();

        EntityUid serverBody = default;
        EntityUid looseOrgan = default;
        await server.WaitPost(() =>
        {
            looseOrgan = SEntMan.SpawnEntity(_looseOrganProto, testMap.GridCoords);
            serverBody = SEntMan.SpawnEntity(HumanProto, testMap.GridCoords);
        });
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var container = SEntMan.System<SharedContainerSystem>().GetContainer(serverBody, BodyHierarchySystem.ConstContainerId);
            Assert.That(SEntMan.System<SharedContainerSystem>().Insert(looseOrgan, container), Is.True, "precondition: the organ went in");
        });
        await Pair.RunTicksSync(10);

        var clientBody = Pair.ToClientUid(serverBody);
        var serverTiming = server.ResolveDependency<IGameTiming>();
        var clientTiming = client.ResolveDependency<IGameTiming>();

        var serverIds = new List<int>();
        var serverRolls = new List<int>();
        await server.WaitPost(() =>
        {
            serverIds = NetIds(SEntMan, serverBody);
            serverRolls = RollOnEachTick(SEntMan, serverTiming, serverBody);
        });

        var clientIds = new List<int>();
        var clientRolls = new List<int>();
        await client.WaitPost(() =>
        {
            clientIds = NetIds(CEntMan, clientBody);
            clientRolls = RollOnEachTick(CEntMan, clientTiming, clientBody);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverIds, Has.Count.GreaterThan(4), "The body has no parts to choose from.");
            Assert.That(serverIds, Does.Contain(SEntMan.GetNetEntity(looseOrgan).Id), "The loose organ is not in the body.");
            Assert.That(serverIds, Is.Ordered, "The server's list is not in net id order.");
            Assert.That(clientIds, Is.EqualTo(serverIds), "The client lists the body's parts in a different order to the server.");
            Assert.That(serverRolls.Distinct().Count(), Is.GreaterThan(1), "The roll always lands on one part, so this proves nothing.");
            Assert.That(clientRolls, Is.EqualTo(serverRolls), "The client chose different parts to the server.");
        }
    }
}
