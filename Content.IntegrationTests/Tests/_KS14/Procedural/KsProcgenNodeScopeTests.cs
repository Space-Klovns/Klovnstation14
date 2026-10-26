#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(NodeGroupSystem))]
public sealed partial class KsProcgenNodeScopeTests : KsProcgenGameTest
{
    [Test]
    public async Task OwnedVoltageNetworksRebuildWithoutConsumingForeignPendingWork()
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var owned = Scene(("CableHV", 0, 0), ("CableHV", 1, 0), ("CableHV", 2, 1),
                ("SubstationBasic", 1, 0), ("CableMV", 1, 0), ("CableMV", 2, 0),
                ("APCBasic", 2, 0), ("CableApcExtension", 2, 0), ("CableApcExtension", 3, 0));
            var foreign = Scene(("CableHV", 0, 0), ("CableHV", 1, 0));
            try
            {
                var foreignNodes = foreign.Members.SelectMany(Nodes).ToArray();
                var oldGroups = foreignNodes.Select(node => node.NodeGroup).ToArray();
                var oldFlood = foreignNodes.Select(node => node.FloodGen).ToArray();
                var oldUndirect = foreignNodes.Select(node => node.UndirectGen).ToArray();
                var oldReachable = foreignNodes.Select(node => node.ReachableNodes.ToArray()).ToArray();
                Assert.That(foreignNodes.All(node => node.FlaggedForFlood), Is.True);
                var result = groups.KsRebuildOwnedPowerNodes(owned.Grid, owned.Members, 4096);
                for (var index = 0; index < foreignNodes.Length; index++)
                {
                    Assert.That(foreignNodes[index].NodeGroup, Is.SameAs(oldGroups[index]));
                    Assert.That(foreignNodes[index].FloodGen, Is.EqualTo(oldFlood[index]));
                    Assert.That(foreignNodes[index].UndirectGen, Is.EqualTo(oldUndirect[index]));
                    Assert.That(foreignNodes[index].ReachableNodes, Is.EquivalentTo(oldReachable[index]));
                    Assert.That(foreignNodes[index].FlaggedForFlood, Is.True);
                }
                Assert.That(result.IssueCode, Is.Null); Assert.That(result.NodeCount, Is.EqualTo(11));
                var high = One(owned.Members[0], NodeGroupID.HVPower).NodeGroup;
                Assert.That(high, Is.Not.Null); Assert.That(One(owned.Members[1], NodeGroupID.HVPower).NodeGroup, Is.SameAs(high));
                Assert.That(One(owned.Members[2], NodeGroupID.HVPower).NodeGroup, Is.Not.SameAs(high));
                Assert.That(One(owned.Members[3], NodeGroupID.HVPower).NodeGroup, Is.SameAs(high));
                var medium = One(owned.Members[4], NodeGroupID.MVPower).NodeGroup;
                Assert.That(medium, Is.Not.Null.And.Not.SameAs(high));
                Assert.That(One(owned.Members[3], NodeGroupID.MVPower).NodeGroup, Is.SameAs(medium));
                Assert.That(One(owned.Members[6], NodeGroupID.MVPower).NodeGroup, Is.SameAs(medium));
                var low = One(owned.Members[7], NodeGroupID.Apc).NodeGroup;
                Assert.That(low, Is.Not.Null.And.Not.SameAs(medium));
                Assert.That(One(owned.Members[6], NodeGroupID.Apc).NodeGroup, Is.SameAs(low));
                Assert.That(One(owned.Members[8], NodeGroupID.Apc).NodeGroup, Is.SameAs(low));
                // Prove the foreign work still exists, rather than merely observing an untouched null group.
                groups.ForceUpdate();
                Assert.That(foreignNodes[0].NodeGroup, Is.Not.Null);
                Assert.That(foreignNodes[1].NodeGroup, Is.SameAs(foreignNodes[0].NodeGroup));
                Assert.That(foreignNodes.All(node => !node.FlaggedForFlood), Is.True);
            }
            finally { maps.DeleteMap(owned.Map); maps.DeleteMap(foreign.Map); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ConverterRequiresBothCableLayersOnItsOwnTile(bool missingInput)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var scene = Scene(("SubstationBasic", 1, 0), ("CableHV", missingInput ? 0 : 1, 0),
                ("CableMV", missingInput ? 1 : 0, 0));
            try
            {
                Assert.That(groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
                var high = One(scene.Members[0], NodeGroupID.HVPower).NodeGroup;
                var medium = One(scene.Members[0], NodeGroupID.MVPower).NodeGroup;
                Assert.That(ReferenceEquals(high, One(scene.Members[1], NodeGroupID.HVPower).NodeGroup), Is.EqualTo(!missingInput));
                Assert.That(ReferenceEquals(medium, One(scene.Members[2], NodeGroupID.MVPower).NodeGroup), Is.EqualTo(missingInput));
            }
            finally { maps.DeleteMap(scene.Map); }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task InsufficientWorkOrCancellationLeavesPendingNodesUntouched(bool cancelled)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0));
            using var cancellation = new CancellationTokenSource();
            if (cancelled) cancellation.Cancel();
            try
            {
                var nodes = scene.Members.SelectMany(Nodes).ToArray(); var previous = nodes.Select(node => node.NodeGroup).ToArray();
                var result = groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, cancelled ? 4096 : 0, cancellationToken: cancellation.Token);
                Assert.That(result.IssueCode, Is.EqualTo(cancelled ? "OwnedNodesCancelled" : "OwnedNodesWorkBudget"));
                for (var index = 0; index < nodes.Length; index++)
                { Assert.That(nodes[index].NodeGroup, Is.SameAs(previous[index])); Assert.That(nodes[index].FlaggedForFlood, Is.True); }
                Assert.That(groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
                Assert.That(nodes[0].NodeGroup, Is.Not.Null.And.SameAs(nodes[1].NodeGroup));
            }
            finally { maps.DeleteMap(scene.Map); }
        });
    }

    private IEnumerable<Node> Nodes(EntityUid uid) => SEntMan.GetComponent<NodeContainerComponent>(uid).Nodes.Values;
    [Test]
    public async Task ConnectionsOutsideTheDeclaredOwnedSetRejectBeforeAnyRebuild()
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0));
            try
            {
                var nodes = scene.Members.SelectMany(Nodes).ToArray(); var previous = nodes.Select(node => node.NodeGroup).ToArray();
                var result = groups.KsRebuildOwnedPowerNodes(scene.Grid, new[] { scene.Members[0] }, 4096);
                Assert.That(result.IssueCode, Is.AnyOf("OwnedNodesForeignConnection", "OwnedNodesForeignGroup"));
                for (var index = 0; index < nodes.Length; index++)
                { Assert.That(nodes[index].NodeGroup, Is.SameAs(previous[index])); Assert.That(nodes[index].FlaggedForFlood, Is.True); }
                Assert.That(groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
                Assert.That(nodes[0].NodeGroup, Is.Not.Null.And.SameAs(nodes[1].NodeGroup));
            }
            finally { maps.DeleteMap(scene.Map); }
        });
    }

    [TestCase("force")] [TestCase("disable")] [TestCase("cancel")] [TestCase("membership")]
    public async Task RebuildCallbacksCannotCertifyChangedOrReenteredOwnedNetworks(string fault)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var fixture = Pair.Server.System<KsProcgenInsertionTestSystem>(); fixture.Reset();
            var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0), ("SubstationBasic", 1, 0));
            var foreign = Scene(("CableHV", 0, 0), ("CableHV", 1, 0));
            var foreignNodes = foreign.Members.SelectMany(Nodes).ToArray(); var oldGroups = foreignNodes.Select(node => node.NodeGroup).ToArray();
            var container = SEntMan.GetComponent<NodeContainerComponent>(scene.Members[0]); var entries = container.Nodes.ToArray();
            using var cancellation = new CancellationTokenSource(); var callbacks = 0;
            fixture.NodesRebuiltAction = uid =>
            {
                if (uid != scene.Members[0]) return;
                callbacks++;
                switch (fault)
                {
                    case "force": groups.ForceUpdate(); break;
                    case "disable": ((Content.Server.Power.Nodes.CableDeviceNode) One(scene.Members[2], NodeGroupID.HVPower)).Enabled = false; break;
                    case "cancel": cancellation.Cancel(); break;
                    case "membership": container.Nodes.Clear(); break;
                }
            };
            try
            {
                var result = groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096, cancellationToken: cancellation.Token);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(result.IssueCode, Is.EqualTo(fault switch
                { "force" => "OwnedNodesCallbackDrift", "disable" => "OwnedNodesTopologyDrift", "cancel" => "OwnedNodesCancelled", _ => "OwnedNodesMemberDrift" }));
                for (var index = 0; index < foreignNodes.Length; index++)
                { Assert.That(foreignNodes[index].NodeGroup, Is.SameAs(oldGroups[index])); Assert.That(foreignNodes[index].FlaggedForFlood, Is.True); }
            }
            finally
            {
                fixture.Reset(); container.Nodes.Clear(); foreach (var entry in entries) container.Nodes.Add(entry.Key, entry.Value);
                maps.DeleteMap(scene.Map); maps.DeleteMap(foreign.Map);
            }
        });
    }

    private Node One(EntityUid uid, NodeGroupID voltage) => Nodes(uid).Single(node => node.NodeGroupID == voltage);
    private (MapId Map, EntityUid Grid, EntityUid[] Members) Scene(params (string Prototype, int X, int Y)[] entities)
    {
        var maps = Pair.Server.System<SharedMapSystem>(); var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
        maps.SetPaused(mapId, true); var grid = maps.CreateGridEntity(mapId); grid.Comp.CanSplit = false;
        var tiles = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        for (var x = -1; x <= 5; x++)
        for (var y = -1; y <= 2; y++) maps.SetTile(grid, new Vector2i(x, y), new Tile(tiles["FloorSteel"].TileId));
        var members = entities.Select(entity => SEntMan.SpawnEntity(entity.Prototype,
            new EntityCoordinates(grid.Owner, new Vector2((float) entity.X + 0.5f, (float) entity.Y + 0.5f)))).ToArray();
        maps.InitializeMap(mapId, unpause: false);
        return (mapId, grid.Owner, members);
    }
}
