#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.PipeNodeTeleporter;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.PipeNodeTeleporter;

/// <summary>
///     Regression tests for chemical beacons/recipients doing nothing when linked.
///     The teleporter system used to look its nodes up as <see cref="Content.Server.NodeContainer.Nodes.PipeNode"/>s,
///         but every teleporter prototype in the game is plumbing, whose nodes are
///         <see cref="Content.Server._Starlight.Plumbing.Nodes.PlumbingNode"/>s. The lookup always failed, so linking
///         a beacon to a recipient silently did nothing and no reagents ever moved.
/// </summary>
[TestFixture]
[TestOf(typeof(PipeNodeTeleporterSystem))]
public sealed class PipeNodeTeleporterTest : GameTest
{
    private const string BeaconProto = "KsPlumbingTeleporterBeacon";
    private const string RecipientProto = "KsPlumbingTeleporterRecipient";

    /// <summary>
    ///     Source of reagents, sitting next to the beacon.
    /// </summary>
    private const string InputProto = "PlumbingInput";

    /// <summary>
    ///     Puller of reagents, sitting next to the recipient, on the far side of the map.
    /// </summary>
    private const string OutputProto = "PlumbingOutput";

    private const string BeaconNodeName = "inlet";
    private const string RecipientNodeName = "outlet";

    /// <summary>
    ///     Linking a beacon to a recipient must merge the two plumbing networks, and unlinking must split them again.
    /// </summary>
    [Test]
    public async Task TestLinkingMergesPlumbingNetworks()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.EntMan;
        var deviceListSystem = entityMan.System<DeviceListSystem>();

        var (beacon, recipient, _, _) = await SpawnSetup();

        await server.WaitAssertion(() =>
        {
            Assert.That(SameNetwork(entityMan, beacon, recipient), Is.False,
                "Beacon and recipient shared a network before they were ever linked");
        });

        await server.WaitAssertion(() => deviceListSystem.UpdateDeviceList(recipient, [beacon]));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SameNetwork(entityMan, beacon, recipient),
                    "Linking a beacon to a recipient did not merge their plumbing networks");

                Assert.That(entityMan.GetComponent<PipeNodeTeleporterRecipientComponent>(recipient).LinkedBeaconUids,
                    Does.Contain(beacon));
                Assert.That(entityMan.GetComponent<PipeNodeTeleporterBeaconComponent>(beacon).LinkedRecipientUids,
                    Does.Contain(recipient));
            });
        });

        await server.WaitAssertion(() => deviceListSystem.UpdateDeviceList(recipient, []));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SameNetwork(entityMan, beacon, recipient), Is.False,
                    "Unlinking a beacon from a recipient did not split their plumbing networks");

                Assert.That(entityMan.GetComponent<PipeNodeTeleporterRecipientComponent>(recipient).LinkedBeaconUids,
                    Is.Empty);
                Assert.That(entityMan.GetComponent<PipeNodeTeleporterBeaconComponent>(beacon).LinkedRecipientUids,
                    Is.Empty);
            });
        });
    }

    /// <summary>
    ///     The player-facing symptom: reagents poured in next to a beacon must come out next to the recipient.
    /// </summary>
    [Test]
    public async Task TestReagentsFlowAcrossLink()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.EntMan;
        var deviceListSystem = entityMan.System<DeviceListSystem>();
        var solutionSystem = entityMan.System<SharedSolutionContainerSystem>();

        var (beacon, recipient, input, output) = await SpawnSetup();

        await server.WaitAssertion(() =>
        {
            Assert.That(solutionSystem.TryGetSolution(input, "input", out var inputSolutionEntity, out _));
            solutionSystem.TryAddReagent(inputSolutionEntity!.Value, "Water", FixedPoint2.New(50), out var accepted);

            Assert.That(accepted, Is.GreaterThan(FixedPoint2.Zero), "Could not fill the plumbing input with water");
        });

        await server.WaitAssertion(() => deviceListSystem.UpdateDeviceList(recipient, [beacon]));

        // Plumbing devices only pull every couple of seconds, so give it a while.
        await server.WaitRunTicks(600);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutionSystem.TryGetSolution(output, "output", out _, out var outputSolution));
            Assert.That(outputSolution!.Volume, Is.GreaterThan(FixedPoint2.Zero),
                "No reagents made it across the teleporter link");
        });
    }

    /// <summary>
    ///     Teleporters have no range limit and are not bound to a grid, so a beacon on one grid must still feed a
    ///         recipient sitting on a completely different one.
    /// </summary>
    [Test]
    public async Task TestReagentsFlowAcrossGrids()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.EntMan;
        var deviceListSystem = entityMan.System<DeviceListSystem>();
        var solutionSystem = entityMan.System<SharedSolutionContainerSystem>();

        var (beacon, recipient, input, output) = await SpawnSetup(separateGrids: true);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityMan.GetComponent<TransformComponent>(beacon).GridUid,
                Is.Not.EqualTo(entityMan.GetComponent<TransformComponent>(recipient).GridUid),
                "The two ends were supposed to end up on separate grids");

            Assert.That(solutionSystem.TryGetSolution(input, "input", out var inputSolutionEntity, out _));
            solutionSystem.TryAddReagent(inputSolutionEntity!.Value, "Water", FixedPoint2.New(50), out var accepted);

            Assert.That(accepted, Is.GreaterThan(FixedPoint2.Zero), "Could not fill the plumbing input with water");
        });

        await server.WaitAssertion(() => deviceListSystem.UpdateDeviceList(recipient, [beacon]));

        await server.WaitRunTicks(600);

        await server.WaitAssertion(() =>
        {
            Assert.That(SameNetwork(entityMan, beacon, recipient),
                "Linking across grids did not merge the two plumbing networks");

            Assert.That(solutionSystem.TryGetSolution(output, "output", out _, out var outputSolution));
            Assert.That(outputSolution!.Volume, Is.GreaterThan(FixedPoint2.Zero),
                "No reagents made it across a cross-grid teleporter link");
        });
    }

    /// <summary>
    ///     Deleting a beacon must not leave the recipient linked to it.
    /// </summary>
    [Test]
    public async Task TestDeletingBeaconUnlinksRecipient()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.EntMan;
        var deviceListSystem = entityMan.System<DeviceListSystem>();

        var (beacon, recipient, _, _) = await SpawnSetup();

        await server.WaitAssertion(() => deviceListSystem.UpdateDeviceList(recipient, [beacon]));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() => entityMan.DeleteEntity(beacon));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityMan.GetComponent<PipeNodeTeleporterRecipientComponent>(recipient).LinkedBeaconUids,
                Is.Empty, "Recipient stayed linked to a deleted beacon");
        });
    }

    /// <summary>
    ///     Spawns a beacon with a reagent source next to it, and - far enough away that nothing connects by
    ///         accident - a recipient with a reagent sink next to it.
    /// </summary>
    private async Task<(EntityUid Beacon, EntityUid Recipient, EntityUid Input, EntityUid Output)> SpawnSetup(bool separateGrids = false)
    {
        var pair = Pair;
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entityMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSys = entityMan.System<SharedMapSystem>();

        EntityUid beacon = default;
        EntityUid recipient = default;
        EntityUid input = default;
        EntityUid output = default;

        await server.WaitAssertion(() =>
        {
            Entity<MapGridComponent> beaconGrid = mapMan.CreateGridEntity(testMap.MapId);
            var recipientGrid = separateGrids ? mapMan.CreateGridEntity(testMap.MapId) : beaconGrid;

            for (var x = 0; x <= 5; ++x)
            {
                for (var y = 0; y <= 5; ++y)
                {
                    mapSys.SetTile(beaconGrid, beaconGrid, new Vector2i(x, y), new Tile(1));

                    if (separateGrids)
                        mapSys.SetTile(recipientGrid, recipientGrid, new Vector2i(x, y), new Tile(1));
                }
            }

            // Beacon end: the input feeds the beacon from the tile to its south.
            input = entityMan.SpawnEntity(InputProto, new EntityCoordinates(beaconGrid, 0.5f, 0.5f));
            beacon = entityMan.SpawnEntity(BeaconProto, new EntityCoordinates(beaconGrid, 0.5f, 1.5f));

            // Recipient end, five tiles away - or on a whole other grid: the output pulls from the recipient on
            //     the tile to its north.
            output = entityMan.SpawnEntity(OutputProto, new EntityCoordinates(recipientGrid, 4.5f, 3.5f));
            recipient = entityMan.SpawnEntity(RecipientProto, new EntityCoordinates(recipientGrid, 4.5f, 4.5f));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // Everything has to be anchored, otherwise nothing forms a network in the first place.
                foreach (var uid in new[] { input, beacon, output, recipient })
                {
                    Assert.That(entityMan.GetComponent<TransformComponent>(uid).Anchored,
                        $"{entityMan.ToPrettyString(uid)} did not anchor");
                }

                Assert.That(entityMan.HasComponent<DeviceListComponent>(recipient),
                    "Recipient has no device list to link beacons through");

                // Sanity check that each end formed a local network of its own.
                Assert.That(SameNetwork(entityMan, beacon, input), "Beacon did not connect to its input");
                Assert.That(SameNetwork(entityMan, recipient, output), "Recipient did not connect to its output");
            });
        });

        return (beacon, recipient, input, output);
    }

    /// <summary>
    ///     Whether the teleporter node of <paramref name="uid"/> ended up in the same node group as any node of
    ///         <paramref name="otherUid"/>.
    /// </summary>
    private static bool SameNetwork(IEntityManager entityMan, EntityUid uid, EntityUid otherUid)
    {
        var nodeContainerSystem = entityMan.System<NodeContainerSystem>();

        var nodeName = entityMan.HasComponent<PipeNodeTeleporterBeaconComponent>(uid)
            ? BeaconNodeName
            : RecipientNodeName;

        if (!nodeContainerSystem.TryGetNode<Node>(uid, nodeName, out var node) || node.NodeGroup == null)
            return false;

        if (!entityMan.TryGetComponent<NodeContainerComponent>(otherUid, out var otherNodeContainerComponent))
            return false;

        return otherNodeContainerComponent.Nodes.Values.Any(otherNode => otherNode.NodeGroup == node.NodeGroup);
    }
}
