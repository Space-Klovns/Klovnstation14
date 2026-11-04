#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The Fluidmos devices on the RPD, built instantly and at its cheapest atmos device cost, and on every pipe layer.
/// </summary>
[TestOf(typeof(AtmosPipeLayersComponent))]
public sealed class KsFluidmosRpdTests : GameTest
{
    private static readonly EntProtoId RpdPrototype = "RPD";
    private static readonly ProtoId<RCDPrototype> MolarMixerSetting = "MolarMixer";

    /// <summary>
    ///     Each device, its RPD setting, and the node that faces south when it is placed facing down.
    /// </summary>
    private static readonly (EntProtoId Device, ProtoId<RCDPrototype> Setting, string SouthNode)[] Devices =
    [
        ("KsFluidPort", "KsFluidPort", "pipe"),
        ("KsPhaseSeparator", "KsPhaseSeparator", "outlet"),
        ("KsExpansionValve", "KsExpansionValve", "outlet"),
    ];

    private static readonly EntProtoId[] StraightPipes = ["GasPipeStraight", "GasPipeStraightAlt1", "GasPipeStraightAlt2"];

    private static readonly AtmosPipeLayer[] Layers = [AtmosPipeLayer.Primary, AtmosPipeLayer.Secondary, AtmosPipeLayer.Tertiary];

    [Test]
    public async Task RpdBuildsEachDeviceInstantlyOnAnyLayer()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var factory = server.EntMan.ComponentFactory;
            Assert.That(server.ProtoMan.Index(RpdPrototype).TryGetComponent<RCDComponent>(out var rpd, factory), "The RPD has no RCD component.");
            var molarMixer = server.ProtoMan.Index(MolarMixerSetting);
            var pipeLayersSystem = server.System<Content.Shared.Atmos.EntitySystems.SharedAtmosPipeLayersSystem>();

            using (Assert.EnterMultipleScope())
            {
                foreach (var (device, settingId, _) in Devices)
                {
                    Assert.That(rpd!.AvailablePrototypes, Does.Contain(settingId), $"The RPD doesn't offer {settingId}.");
                    var setting = server.ProtoMan.Index(settingId);
                    Assert.That(setting.Prototype, Is.EqualTo(device.Id), $"{settingId} builds the wrong thing.");
                    Assert.That(setting.Cost, Is.EqualTo(molarMixer.Cost), $"{settingId} doesn't cost what a molar mixer does.");
                    Assert.That(setting.Delay, Is.Zero, $"{settingId} isn't built instantly.");
                    Assert.That(setting.HasLayers, $"{settingId} can't be built on another layer.");

                    Assert.That(server.ProtoMan.Index(device).TryGetComponent<AtmosPipeLayersComponent>(out var pipeLayers, factory),
                        $"{device} has no pipe layers.");
                    foreach (var layer in Layers)
                    {
                        Assert.That(pipeLayersSystem.TryGetAlternativePrototype(pipeLayers!, layer, out var layerPrototype),
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
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var placed = new List<(string Name, string SouthNode, EntityUid Device, EntityUid SameLayerPipe, EntityUid OtherLayerPipe)>();

        await server.WaitPost(() =>
        {
            var factory = server.EntMan.ComponentFactory;
            var pipeLayersSystem = server.System<Content.Shared.Atmos.EntitySystems.SharedAtmosPipeLayersSystem>();
            // One unbroken floor, so the grid doesn't split between the devices.
            for (var floorX = 0; floorX < 2 * Devices.Length * Layers.Length; floorX++)
            for (var floorY = -1; floorY <= 0; floorY++)
                mapSystem.SetTile(grid, new Vector2i(floorX, floorY), testMap.Tile.Tile);

            var x = 0;
            foreach (var (device, _, southNode) in Devices)
            {
                server.ProtoMan.Index(device).TryGetComponent<AtmosPipeLayersComponent>(out var pipeLayers, factory);
                for (var layerIndex = 0; layerIndex < Layers.Length; layerIndex++, x += 2)
                {
                    EntityCoordinates At(int y) => mapSystem.GridTileToLocal(grid, grid.Comp, new Vector2i(x, y));

                    Assert.That(pipeLayersSystem.TryGetAlternativePrototype(pipeLayers!, Layers[layerIndex], out var layerPrototype));
                    var deviceUid = server.EntMan.SpawnAtPosition(layerPrototype, At(0), Angle.Zero);
                    var sameLayerPipe = server.EntMan.SpawnAtPosition(StraightPipes[layerIndex], At(-1), Angle.Zero);
                    var otherLayerPipe = server.EntMan.SpawnAtPosition(StraightPipes[(layerIndex + 1) % Layers.Length], At(-1), Angle.Zero);
                    placed.Add((layerPrototype.Id, southNode, deviceUid, sameLayerPipe, otherLayerPipe));
                }
            }
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (name, southNode, deviceUid, sameLayerPipe, otherLayerPipe) in placed)
                {
                    Assert.That(nodeContainerSystem.TryGetNode(deviceUid, southNode, out PipeNode? deviceNode), $"{name} has no {southNode} node.");
                    Assert.That(nodeContainerSystem.TryGetNode(sameLayerPipe, "pipe", out PipeNode? sameNode));
                    Assert.That(nodeContainerSystem.TryGetNode(otherLayerPipe, "pipe", out PipeNode? otherNode));
                    Assert.That(deviceNode!.NodeGroup, Is.SameAs(sameNode!.NodeGroup), $"{name} didn't join the pipe on its own layer.");
                    Assert.That(deviceNode.NodeGroup, Is.Not.SameAs(otherNode!.NodeGroup), $"{name} joined a pipe on another layer.");
                }
            }
        });
    }
}
