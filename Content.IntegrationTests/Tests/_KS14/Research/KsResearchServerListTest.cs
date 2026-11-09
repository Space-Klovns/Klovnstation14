#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Research.UI;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Research.Systems;
using Content.Shared._KS14.Research.Components;
using Content.Shared.Input;
using Content.Shared.Lathe;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Client.UserInterface.Controls;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Research;

[TestFixture]
[TestOf(typeof(ResearchSystem))]
public sealed class KsResearchServerListTest : InteractionTest
{
    private EntityUid ToClientUid(EntityUid serverUid) => CEntMan.GetEntity(SEntMan.GetNetEntity(serverUid));

    private static readonly EntProtoId ServerPrototype = "ResearchAndDevelopmentServer";
    private static readonly EntProtoId LathePrototype = "Protolathe";
    private static readonly EntProtoId VesselPrototype = "MachineAnomalyVessel";

    private async Task<EntityUid> SpawnAsync(EntProtoId prototype, EntityCoordinates coordinates)
    {
        EntityUid spawned = default;
        await Server.WaitPost(() => spawned = SEntMan.SpawnEntity(prototype, coordinates));
        return spawned;
    }

    private async Task<EntityUid> SpawnPoweredLatheAsync(EntityCoordinates coordinates)
    {
        var latheUid = await SpawnAsync(LathePrototype, coordinates);
        await Server.WaitPost(() => Server.System<SharedPowerReceiverSystem>().SetNeedsPower(latheUid, false));
        return latheUid;
    }

    private async Task<KsResearchServerEntry[]> ClientListAsync(EntityUid serverSideClientUid)
    {
        KsResearchServerEntry[] list = [];
        await Client.WaitPost(() =>
        {
            list = Client.System<SharedResearchSystem>().GetServerList(ToClientUid(serverSideClientUid)).ToArray();
        });
        return list;
    }

    private ResearchClientServerSelectionMenu[] OpenMenus()
    {
        return Client.Resolve<IUserInterfaceManager>().WindowRoot.Children
            .OfType<ResearchClientServerSelectionMenu>()
            .ToArray();
    }

    private static ItemList ServersOf(ResearchClientServerSelectionMenu menu)
    {
        return menu.FindControl<ItemList>("Servers");
    }

    [Test]
    public async Task ClientKnowsTheServersOnItsMapAndTheirChanges()
    {
        var firstMap = MapData;
        var secondMap = await Pair.CreateTestMap();

        var firstServer = await SpawnAsync(ServerPrototype, firstMap.GridCoords);
        var secondServer = await SpawnAsync(ServerPrototype, firstMap.GridCoords);
        var otherMapServer = await SpawnAsync(ServerPrototype, secondMap.GridCoords);
        var firstLathe = await SpawnPoweredLatheAsync(firstMap.GridCoords);
        var secondLathe = await SpawnPoweredLatheAsync(secondMap.GridCoords);
        await RunTicks(5);

        var firstNet = SEntMan.GetNetEntity(firstServer);
        var secondNet = SEntMan.GetNetEntity(secondServer);
        var otherNet = SEntMan.GetNetEntity(otherMapServer);

        var firstList = await ClientListAsync(firstLathe);
        Assert.That(firstList.Select(entry => entry.Server), Is.EquivalentTo(new[] { firstNet, secondNet }),
            "the client lists the servers on its own map, and not those on another map");
        Assert.That(firstList.Select(entry => entry.Id).Distinct().Count(), Is.EqualTo(2), "the ids are the servers' unique ids");
        Assert.That((await ClientListAsync(secondLathe)).Select(entry => entry.Server), Is.EquivalentTo(new[] { otherNet }));

        // Renamed.
        await Server.WaitPost(() => Server.System<ResearchSystem>().SetServerName(firstServer, "RENAMED"));
        await RunTicks(5);
        Assert.That((await ClientListAsync(firstLathe)).Single(entry => entry.Server == firstNet).Name, Is.EqualTo("RENAMED"));

        // Added.
        var addedServer = await SpawnAsync(ServerPrototype, firstMap.GridCoords);
        await RunTicks(5);
        Assert.That((await ClientListAsync(firstLathe)).Select(entry => entry.Server),
            Is.EquivalentTo(new[] { firstNet, secondNet, SEntMan.GetNetEntity(addedServer) }));

        // Removed.
        await Server.WaitPost(() => SEntMan.DeleteEntity(secondServer));
        await RunTicks(5);
        Assert.That((await ClientListAsync(firstLathe)).Select(entry => entry.Server),
            Is.EquivalentTo(new[] { firstNet, SEntMan.GetNetEntity(addedServer) }));

        // Moved to another map.
        await Server.WaitPost(() => Server.System<TransformSystem>().SetCoordinates(firstServer, secondMap.GridCoords));
        await RunTicks(5);
        Assert.That((await ClientListAsync(firstLathe)).Select(entry => entry.Server),
            Is.EquivalentTo(new[] { SEntMan.GetNetEntity(addedServer) }), "a server that left the map is gone from its list");
        Assert.That((await ClientListAsync(secondLathe)).Select(entry => entry.Server), Is.EquivalentTo(new[] { otherNet, firstNet }));
    }

    [Test]
    public async Task ConsoleButtonOpensTheListBeforeTheServerHearsOfIt()
    {
        var map = MapData;
        await SpawnAsync(ServerPrototype, map.GridCoords);
        await SpawnAsync(ServerPrototype, map.GridCoords);
        await SpawnTarget(LathePrototype);
        var latheUid = STarget!.Value;
        await Server.WaitPost(() => Server.System<SharedPowerReceiverSystem>().SetNeedsPower(latheUid, false));
        await RunTicks(5);

        // The lathe window is open, as it is when its server list button is pressed.
        await Activate();
        await RunTicks(5);

        await Client.WaitPost(() =>
        {
            Assert.That(Client.System<Robust.Client.GameObjects.UserInterfaceSystem>()
                .TryGetOpenUi(ToClientUid(latheUid), LatheUiKey.Key, out var latheInterface), Is.True);
            latheInterface!.SendPredictedMessage(new ConsoleServerSelectionMessage());
        });

        // Only the client runs: the server has not seen the message.
        await Client.WaitRunTicks(3);

        Assert.That(Server.System<UserInterfaceSystem>().IsUiOpen(latheUid, ResearchClientUiKey.Key), Is.False,
            "the server has not run, so the window opened by prediction");
        var menus = OpenMenus();
        Assert.That(menus, Has.Length.EqualTo(1), "the client opened the window by itself");
        Assert.That(ServersOf(menus[0]).Count, Is.EqualTo(2), "the list was filled without a state from the server");

        // The window stays open once the server catches up, and the server agrees.
        await RunTicks(10);
        Assert.That(Server.System<UserInterfaceSystem>().IsUiOpen(latheUid, ResearchClientUiKey.Key), Is.True);
        Assert.That(OpenMenus(), Has.Length.EqualTo(1));
        Assert.That(ServersOf(OpenMenus()[0]).Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ListIsFilledAndShowsTheSelectionOnOpen()
    {
        var map = MapData;
        await SpawnAsync(ServerPrototype, map.GridCoords);
        await SpawnAsync(ServerPrototype, map.GridCoords);
        await SpawnTarget(VesselPrototype);
        var latheUid = STarget!.Value;
        var actorUid = SPlayer;
        await RunTicks(5);

        await Server.WaitPost(() => Server.System<SharedPowerReceiverSystem>().SetNeedsPower(latheUid, false));
        await RunTicks(5);

        // The player's activate key, run by the client alone: the server has not seen it.
        await SetKey(ContentKeyFunctions.ActivateItemInWorld, BoundKeyState.Down);
        await Client.WaitRunTicks(3);
        Assert.That(Server.System<UserInterfaceSystem>().IsUiOpen(latheUid, ResearchClientUiKey.Key), Is.False,
            "the server has not run, so the window opened by prediction");
        await SetKey(ContentKeyFunctions.ActivateItemInWorld, BoundKeyState.Up);
        await RunTicks(1);

        var servers = SEntMan.GetComponent<ResearchClientComponent>(latheUid);
        var connectedId = servers.ConnectedServerId;
        Assert.That(connectedId, Is.GreaterThanOrEqualTo(0), "the lathe registered with a server on startup");

        var menu = OpenMenus().Single();
        Assert.That(ServersOf(menu).Count, Is.EqualTo(2));
        var list = await ClientListAsync(latheUid);
        var selectedIds = Enumerable.Range(0, ServersOf(menu).Count)
            .Where(index => ServersOf(menu)[index].Selected)
            .Select(index => list[index].Id)
            .ToArray();
        Assert.That(selectedIds, Is.EqualTo(new[] { connectedId }), "the connected server is the selected one");

        // Choosing another server stays a message to the server, and the window follows its answer.
        var otherId = list.First(entry => entry.Id != connectedId).Id;
        await Client.WaitPost(() =>
        {
            Assert.That(Client.System<Robust.Client.GameObjects.UserInterfaceSystem>()
                .TryGetOpenUi(ToClientUid(latheUid), ResearchClientUiKey.Key, out var selectionInterface), Is.True);
            ((ResearchClientBoundUserInterface)selectionInterface!).SelectServer(otherId);
        });
        await RunTicks(10);

        Assert.That(SEntMan.GetComponent<ResearchClientComponent>(latheUid).ConnectedServerId, Is.EqualTo(otherId));
        Assert.That(CEntMan.GetComponent<ResearchClientComponent>(ToClientUid(latheUid)).ConnectedServerId, Is.EqualTo(otherId),
            "the client has the server's selection");
        var refreshed = OpenMenus().Single();
        var refreshedList = await ClientListAsync(latheUid);
        Assert.That(Enumerable.Range(0, ServersOf(refreshed).Count)
                .Where(index => ServersOf(refreshed)[index].Selected)
                .Select(index => refreshedList[index].Id),
            Is.EqualTo(new[] { otherId }), "the window shows the new selection");

        // A server built while the window is open appears in it.
        await SpawnAsync(ServerPrototype, map.GridCoords);
        await RunTicks(5);
        Assert.That(ServersOf(OpenMenus().Single()).Count, Is.EqualTo(3), "the open window follows the list");
    }
}
