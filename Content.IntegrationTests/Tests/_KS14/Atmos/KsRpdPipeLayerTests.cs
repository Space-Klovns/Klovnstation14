#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.NodeContainer;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Every device the RPD builds in layer variants really exists on each layer, and joins only pipes on its own layer.
///         Walks the RPD's own settings, so a new device is covered by adding it to the RPD.
/// </summary>
[TestOf(typeof(SharedAtmosPipeLayersSystem))]
public sealed class KsRpdPipeLayerTests : GameTest
{
    private static readonly EntProtoId RpdPrototype = "RPD";

    private static readonly EntProtoId[] StraightPipes = ["GasPipeStraight", "GasPipeStraightAlt1", "GasPipeStraightAlt2"];

    private static readonly AtmosPipeLayer[] Layers = [AtmosPipeLayer.Primary, AtmosPipeLayer.Secondary, AtmosPipeLayer.Tertiary];

    private List<EntProtoId> LayeredDevices()
    {
        var server = Pair.Server;
        var factory = server.EntMan.ComponentFactory;
        Assert.That(server.ProtoMan.Index(RpdPrototype).TryGetComponent<RCDComponent>(out var rpd, factory), "The RPD has no RCD component.");

        // Some settings take a layer but name no variants, and the RPD then builds them on the primary one, so only those
        //     that name them have anything to prove.
        var devices = rpd!.AvailablePrototypes
            .Select(setting => server.ProtoMan.Index(setting))
            .Where(setting => setting.HasLayers && setting.Prototype != null)
            .Select(setting => new EntProtoId(setting.Prototype!))
            .Distinct()
            .Where(device => server.ProtoMan.Index(device).TryGetComponent<AtmosPipeLayersComponent>(out var pipeLayers, factory) &&
                             pipeLayers.AlternativePrototypes.Count > 0)
            .ToList();

        Assert.That(devices, Is.Not.Empty, "The RPD offers nothing on layers.");
        return devices;
    }

    [Test]
    public async Task EachLayeredDeviceHasAPrototypeOnEveryLayer()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var factory = server.EntMan.ComponentFactory;
            var pipeLayersSystem = server.System<SharedAtmosPipeLayersSystem>();

            using (Assert.EnterMultipleScope())
            {
                foreach (var device in LayeredDevices())
                {
                    Assert.That(server.ProtoMan.Index(device).TryGetComponent<AtmosPipeLayersComponent>(out var pipeLayers, factory), $"{device} has no pipe layers.");
                    if (pipeLayers == null)
                        continue;

                    foreach (var layer in Layers)
                    {
                        Assert.That(pipeLayersSystem.TryGetAlternativePrototype(pipeLayers, layer, out var layerPrototype),
                            $"{device} has no prototype for its {layer} layer, so the RPD builds it on the primary one.");
                        Assert.That(server.ProtoMan.Index(layerPrototype).TryGetComponent<AtmosPipeLayersComponent>(out var layerPipeLayers, factory));
                        Assert.That(layerPipeLayers!.CurrentPipeLayer, Is.EqualTo(layer), $"{layerPrototype} isn't on the {layer} layer.");
                    }
                }
            }
        });
    }

    /// <summary>
    ///     A device on a layer joins the pipe on its own layer and not one on another, sharing its tile.
    /// </summary>
    [Test]
    public async Task EachLayerConnectsOnlyToItsOwnLayer()
    {
        var server = Pair.Server;
        var mapSystem = server.System<SharedMapSystem>();
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var placed = new List<(string Name, EntityUid Device, EntityUid SameLayerPipe, EntityUid OtherLayerPipe)>();

        await server.WaitPost(() =>
        {
            var factory = server.EntMan.ComponentFactory;
            var pipeLayersSystem = server.System<SharedAtmosPipeLayersSystem>();
            var devices = LayeredDevices();

            // One unbroken floor, so the grid doesn't split between the devices.
            for (var floorX = 0; floorX < 2 * devices.Count * Layers.Length; floorX++)
            for (var floorY = -1; floorY <= 0; floorY++)
                mapSystem.SetTile(grid, new Vector2i(floorX, floorY), testMap.Tile.Tile);

            var x = 0;
            foreach (var device in devices)
            {
                if (!server.ProtoMan.Index(device).TryGetComponent<AtmosPipeLayersComponent>(out var pipeLayers, factory))
                    continue;

                for (var layerIndex = 0; layerIndex < Layers.Length; layerIndex++, x += 2)
                {
                    EntityCoordinates At(int y) => mapSystem.GridTileToLocal(grid, grid.Comp, new Vector2i(x, y));

                    if (!pipeLayersSystem.TryGetAlternativePrototype(pipeLayers, Layers[layerIndex], out var layerPrototype))
                        continue;

                    var deviceUid = server.EntMan.SpawnAtPosition(layerPrototype, At(0), Angle.Zero);
                    var sameLayerPipe = server.EntMan.SpawnAtPosition(StraightPipes[layerIndex], At(-1), Angle.Zero);
                    var otherLayerPipe = server.EntMan.SpawnAtPosition(StraightPipes[(layerIndex + 1) % Layers.Length], At(-1), Angle.Zero);
                    placed.Add((layerPrototype.Id, deviceUid, sameLayerPipe, otherLayerPipe));
                }
            }
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var nodeContainerSystem = server.System<NodeContainerSystem>();
            using (Assert.EnterMultipleScope())
            {
                foreach (var (name, deviceUid, sameLayerPipe, otherLayerPipe) in placed)
                {
                    // A device with nothing facing south has nothing to prove against the pipes laid there.
                    var deviceNode = server.EntMan.GetComponent<NodeContainerComponent>(deviceUid).Nodes.Values
                        .OfType<PipeNode>()
                        .FirstOrDefault(node => (node.CurrentPipeDirection & PipeDirection.South) != 0);
                    if (deviceNode == null)
                        continue;

                    Assert.That(nodeContainerSystem.TryGetNode(sameLayerPipe, "pipe", out PipeNode? sameNode));
                    Assert.That(nodeContainerSystem.TryGetNode(otherLayerPipe, "pipe", out PipeNode? otherNode));
                    Assert.That(deviceNode.NodeGroup, Is.SameAs(sameNode!.NodeGroup), $"{name} didn't join the pipe on its own layer.");
                    Assert.That(deviceNode.NodeGroup, Is.Not.SameAs(otherNode!.NodeGroup), $"{name} joined a pipe on another layer.");
                }
            }
        });
    }
}
