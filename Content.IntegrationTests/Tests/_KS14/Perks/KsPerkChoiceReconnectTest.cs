#nullable enable
using System.Linq;
using Content.Client._KS14.Perks.UI;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared._KS14.Perks;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Perks;

/// <summary>
///     The perk picker can't be closed, so a player who leaves their body while it is open (a lost connection, a
///         restarted client, ghosting) must find it open again on coming back, or they never get a perk.
/// </summary>
/// <remarks>
///     A real round (no dummy ticker), as reconnecting writes the player to it; and a mind, so the game puts the player
///         back in their body.
/// </remarks>
[TestFixture]
[TestOf(typeof(SharedKsPerkChoiceSystem))]
public sealed class KsPerkChoiceReconnectTest : GameTest
{
    public override PoolSettings PoolSettings => new() { DummyTicker = false, Connected = true, Dirty = true };

    // Ids from KsPerkTest's test prototypes.
    private readonly EntProtoId _body = "KsTestPerkBody";
    private readonly ProtoId<KsPerkPrototype> _perkA = "KsTestPerkA";
    private readonly ProtoId<KsPerkPrototype> _perkB = "KsTestPerkB";

    /// <summary>
    ///     Spawns a body with the player's mind in it and opens the picker on it.
    /// </summary>
    private async Task<EntityUid> OpenPickerOnMindedBody()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var mindSystem = server.System<MindSystem>();
        EntityUid bodyUid = default;

        await server.WaitPost(() =>
        {
            var session = server.PlayerMan.Sessions.Single();
            bodyUid = server.EntMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            var mindUid = mindSystem.GetMind(session.UserId) ?? mindSystem.CreateMind(session.UserId);
            mindSystem.TransferTo(mindUid, bodyUid);
            Assert.That(server.System<SharedKsPerkChoiceSystem>().RequestChoice(bodyUid, new[] { _perkA, _perkB }), Is.True);
        });

        await Pair.RunTicksSync(5);
        await AssertPickerOpen(bodyUid, "before leaving");
        return bodyUid;
    }

    private async Task AssertPickerOpen(EntityUid bodyUid, string when)
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.PlayerMan.Sessions.Single().AttachedEntity, Is.EqualTo(bodyUid), $"the player should be in their body {when}");
            Assert.That(server.System<SharedKsPerkChoiceSystem>().IsChoosing(bodyUid), Is.True, $"the choice should be pending {when}");
            Assert.That(server.System<SharedUserInterfaceSystem>().IsUiOpen(bodyUid, KsPerkChoiceUiKey.Key, bodyUid), Is.True,
                $"the picker isn't open on the server {when}");
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.System<SharedUserInterfaceSystem>().TryGetOpenUi(ToClientUid(bodyUid), KsPerkChoiceUiKey.Key, out var bui), Is.True,
                $"the picker isn't open on the client {when}");
            Assert.That(bui, Is.InstanceOf<KsPerkChoiceBoundUserInterface>());
        });
    }

    private async Task PickAndAssertGiven(EntityUid bodyUid, ProtoId<KsPerkPrototype> perk)
    {
        var server = Pair.Server;
        await Client.WaitPost(() =>
        {
            Assert.That(Client.System<SharedUserInterfaceSystem>().TryGetOpenUi(ToClientUid(bodyUid), KsPerkChoiceUiKey.Key, out var bui), Is.True);
            bui!.SendMessage(new KsPerkChoiceSelectMessage(perk));
        });
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedKsPerkChoiceSystem>().IsChoosing(bodyUid), Is.False, "picking should end the choice");
            Assert.That(server.System<SharedKsPerkSystem>().HasPerk(bodyUid, perk), Is.True, "the picked perk should be given");
        });
    }

    [Test]
    public async Task PickerIsOpenAgainAfterReconnecting()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var clientNetManager = client.ResolveDependency<IClientNetManager>();
        var bodyUid = await OpenPickerOnMindedBody();
        var session = server.PlayerMan.Sessions.Single();
        var userName = session.Name;

        await client.WaitPost(() => clientNetManager.ClientDisconnect("testing"));
        await Pair.RunTicksSync(5);
        Assert.That(session.Status, Is.EqualTo(SessionStatus.Disconnected));

        // Everything the client knew is gone, as after a restart.
        await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());
        client.SetConnectTarget(server);
        await client.WaitPost(() => clientNetManager.ClientConnect(null!, 0, userName));
        await Pair.RunTicksSync(10);

        Assert.That(server.PlayerMan.Sessions.Single().Status, Is.EqualTo(SessionStatus.InGame));
        await AssertPickerOpen(bodyUid, "after reconnecting");
        await PickAndAssertGiven(bodyUid, _perkA);
    }

    /// <summary>
    ///     Out to a ghost and back: visiting it (a ghost that can return) or moved into it and put back in the body
    ///         (revived).
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task PickerIsOpenAgainAfterGhostingAndReturning(bool visit)
    {
        var server = Pair.Server;
        var mindSystem = server.System<MindSystem>();
        var bodyUid = await OpenPickerOnMindedBody();
        EntityUid mindUid = default;
        EntityUid ghostUid = default;

        await server.WaitPost(() =>
        {
            mindUid = mindSystem.GetMind(server.PlayerMan.Sessions.Single().UserId)!.Value;
            ghostUid = server.EntMan.SpawnEntity(GameTicker.ObserverPrototypeName, MapCoordinates.Nullspace);
            if (visit)
                mindSystem.Visit(mindUid, ghostUid);
            else
                mindSystem.TransferTo(mindUid, ghostUid);
        });
        await Pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(server.PlayerMan.Sessions.Single().AttachedEntity, Is.EqualTo(ghostUid), "the player should be ghosting"));

        await server.WaitPost(() =>
        {
            if (visit)
                mindSystem.UnVisit(mindUid);
            else
                mindSystem.TransferTo(mindUid, bodyUid);
        });
        await Pair.RunTicksSync(5);

        await AssertPickerOpen(bodyUid, "after returning to the body");
        await PickAndAssertGiven(bodyUid, _perkB);
    }
}
