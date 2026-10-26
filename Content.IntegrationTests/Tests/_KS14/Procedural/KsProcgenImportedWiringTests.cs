#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Server.Power.NodeGroups;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Power;
using Content.Shared.SubFloor;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

/// <summary>Exercise the distributed native grid through ordinary loading and ticks, without procgen's node repair hooks.</summary>
public sealed class KsProcgenImportedWiringTests : KsProcgenGameTest
{
    [TestCase(false)] [TestCase(true)]
    public async Task InstalledShowcaseWiresSurviveImportUnpauseAndOrdinarySimulation(bool initiallyPaused)
    {
        var mapUid = EntityUid.Invalid; var gridUid = EntityUid.Invalid;
        var mapId = MapId.Nullspace;
        var before = new HashSet<EntityUid>();
        var original = new Dictionary<(Vector2i Cell, CableType Voltage), EntityUid>();
        try
        {
            await AssertOnServer(() =>
            {
                before = SEntMan.GetEntities().ToHashSet();
                using var input = Pair.Server.ResolveDependency<IResourceManager>()
                    .ContentFileRead(new ResPath("/Maps/_KS14/ProcgenShowcases/wired-office.yml"));
                using var reader = new StreamReader(input);
                Assert.That(Pair.Server.System<MapLoaderSystem>().TryLoadGrid(reader, "wired-office.yml", out var map, out var grid,
                    options: new DeserializationOptions { InitializeMaps = true, PauseMaps = initiallyPaused }), Is.True);
                mapUid = map!.Value.Owner; gridUid = grid!.Value.Owner;
                mapId = map.Value.Comp.MapId;
                original = Installed(gridUid);
                Assert.That(original.Count, Is.EqualTo(32), "The shipped showcase declares 32 installed segments, separate from loose supply coils.");
                Pair.Server.System<SharedMapSystem>().SetPaused(map.Value.Comp.MapId, false);
            });
            await RunTicksSync(30);
            await AssertOnServer(() =>
            {
                Assert.That(Installed(gridUid), Is.EquivalentTo(original), "Simulation must not cut, drop, move or replace any installed cable.");
                var converters = 0; var receivers = 0;
                foreach (var uid in SEntMan.GetEntities())
                {
                    if (!SEntMan.TryGetComponent<TransformComponent>(uid, out var transform) || transform.ParentUid != gridUid) continue;
                    if (SEntMan.TryGetComponent<NodeContainerComponent>(uid, out var container))
                    {
                        var nodes = container.Nodes.Values.OfType<CableDeviceNode>().ToArray();
                        if (nodes.Length == 2 && nodes.Any(node => node.NodeGroupID == NodeGroupID.MVPower) &&
                            nodes.Any(node => node.NodeGroupID is NodeGroupID.HVPower or NodeGroupID.Apc))
                        {
                            converters++;
                            var cell = Cell(transform);
                            foreach (var node in nodes)
                            {
                                var voltage = node.NodeGroupID == NodeGroupID.HVPower ? CableType.HighVoltage :
                                    node.NodeGroupID == NodeGroupID.MVPower ? CableType.MediumVoltage : CableType.Apc;
                                Assert.That(original.TryGetValue((cell, voltage), out var cableUid), Is.True, "Converter needs a wire under the same tile.");
                                var cableNode = SEntMan.GetComponent<NodeContainerComponent>(cableUid).Nodes.Values.Single(candidate => candidate.NodeGroupID == node.NodeGroupID);
                                Assert.That(node.NodeGroup, Is.Not.Null);
                                Assert.That(ReferenceEquals(node.NodeGroup, cableNode.NodeGroup), Is.True, "Native startup must connect the converter to its installed wire.");
                            }
                        }
                    }
                    if (!SEntMan.TryGetComponent<ExtensionCableReceiverComponent>(uid, out var receiver)) continue;
                    receivers++;
                    Assert.That(receiver.Provider.HasValue, Is.True, "Native startup must link the actual computer to LV wiring.");
                    var provider = receiver.Provider!.Value;
                    var source = SEntMan.GetComponent<ApcPowerProviderComponent>(provider.Owner);
                    var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(uid);
                    Assert.That(ReferenceEquals(power.Provider, source), Is.True);
                    Assert.That(source.Net, Is.TypeOf<ApcNet>());
                    var network = (ApcNet) source.Net!;
                    Assert.That(network.Apcs, Is.Not.Empty, "A nearby provider must belong to the installed APC network.");
                    Assert.That(network.Providers, Does.Contain(source));
                    Assert.That(provider.Comp.LinkedReceivers.Any(item => item.Owner == uid), Is.True);
                    Assert.That(source.LinkedReceivers, Does.Contain(power));
                    // APCs are also native proximity providers; a receiver need not select a cable entity.
                    Assert.That(original.Values.Any(cableUid =>
                        SEntMan.TryGetComponent<ApcPowerProviderComponent>(cableUid, out var cableSource) &&
                        ReferenceEquals(cableSource.Net, source.Net)), Is.True);
                }
                Assert.That(converters, Is.EqualTo(2)); Assert.That(receivers, Is.GreaterThanOrEqualTo(1));
            });
        }
        finally
        {
            await Pair.Server.WaitAssertion(() =>
            {
                if (mapUid != EntityUid.Invalid && SEntMan.EntityExists(mapUid)) Pair.Server.System<SharedMapSystem>().DeleteMap(mapId);
            });
        }
        await AssertOnServer(() => Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before)));
    }

    private Dictionary<(Vector2i Cell, CableType Voltage), EntityUid> Installed(EntityUid gridUid)
    {
        var maps = Pair.Server.System<SharedMapSystem>(); var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
        var result = new Dictionary<(Vector2i Cell, CableType Voltage), EntityUid>();
        var query = SEntMan.AllEntityQueryEnumerator<CableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var cable, out var transform))
        {
            if (transform.ParentUid != gridUid) continue;
            Assert.That(transform.Anchored, Is.True, "Wiring must be anchored, not dropped as loose items.");
            Assert.That(SEntMan.GetComponent<SubFloorHideComponent>(uid).IsUnderCover, Is.True,
                "Installed wires on finished floor must be underneath the tiles, not exposed on top.");
            var cell = Cell(transform); var anchored = new List<EntityUid>();
            maps.GetAnchoredEntities((gridUid, grid), cell, anchored);
            Assert.That(anchored, Does.Contain(uid), "Wiring must be registered in the physical anchor lattice.");
            Assert.That(result.TryAdd((cell, cable.CableType), uid), Is.True);
        }
        return result;
    }

    private static Vector2i Cell(TransformComponent transform) =>
        new((int) MathF.Floor(transform.LocalPosition.X), (int) MathF.Floor(transform.LocalPosition.Y));
}
