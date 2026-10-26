#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenNodeScopeTests
{
    [Test]
    public async Task ReadOnlyInspectionPreservesNetworksAndRejectsDriftWithoutRepair()
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var power = Pair.Server.System<PowerNetSystem>(); var fixture = Pair.Server.System<KsProcgenInsertionTestSystem>(); fixture.Reset();
            var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0), ("SubstationBasic", 1, 0));
            var foreign = Scene(("CableHV", 0, 0), ("CableHV", 1, 0));
            try
            {
                Assert.That(groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
                var nodes = scene.Members.SelectMany(Nodes).ToArray(); var original = nodes.Select(node => node.NodeGroup).ToArray();
                var netIds = nodes.Select(node => node.NetId).ToArray(); var countNetworks = power.GetStatistics().CountNetworks;
                var foreignNodes = foreign.Members.SelectMany(Nodes).ToArray(); var foreignGroups = foreignNodes.Select(node => node.NodeGroup).ToArray();
                var callbacks = 0; fixture.NodesRebuiltAction = _ => callbacks++;
                for (var attempt = 0; attempt < 2; attempt++)
                    Assert.That(groups.KsInspectOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
                Assert.That(callbacks, Is.Zero); Assert.That(power.GetStatistics().CountNetworks, Is.EqualTo(countNetworks));
                for (var index = 0; index < nodes.Length; index++)
                { Assert.That(nodes[index].NodeGroup, Is.SameAs(original[index])); Assert.That(nodes[index].NetId, Is.EqualTo(netIds[index])); }
                var device = (CableDeviceNode) One(scene.Members[2], NodeGroupID.HVPower); device.Enabled = false;
                Assert.That(groups.KsInspectOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.EqualTo("OwnedNodesTopologyDrift"));
                device.Enabled = true;
                Assert.That(groups.KsInspectOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
                Assert.That(power.GetStatistics().CountNetworks, Is.EqualTo(countNetworks)); Assert.That(callbacks, Is.Zero);
                for (var index = 0; index < nodes.Length; index++)
                {
                    Assert.That(nodes[index].NodeGroup, Is.SameAs(original[index])); Assert.That(nodes[index].NetId, Is.EqualTo(netIds[index]));
                    Assert.That(nodes[index].FlaggedForFlood, Is.False);
                }
                for (var index = 0; index < foreignNodes.Length; index++)
                { Assert.That(foreignNodes[index].NodeGroup, Is.SameAs(foreignGroups[index])); Assert.That(foreignNodes[index].FlaggedForFlood, Is.True); }
            }
            finally { fixture.Reset(); maps.DeleteMap(scene.Map); maps.DeleteMap(foreign.Map); }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ReadOnlyInspectionHonorsBudgetAndCancellationWithoutChangingGroups(bool cancelled)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0)); using var cancellation = new CancellationTokenSource();
            try
            {
                Assert.That(groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
                var node = Nodes(scene.Members[0]).Single(); var original = node.NodeGroup;
                if (cancelled) cancellation.Cancel();
                var result = groups.KsInspectOwnedPowerNodes(scene.Grid, scene.Members, cancelled ? 4096 : 0, cancellationToken: cancellation.Token);
                Assert.That(result.IssueCode, Is.EqualTo(cancelled ? "OwnedNodesCancelled" : "OwnedNodesWorkBudget"));
                Assert.That(node.NodeGroup, Is.SameAs(original)); Assert.That(node.FlaggedForFlood, Is.False);
            }
            finally { maps.DeleteMap(scene.Map); }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ForeignRemakeAndRemovalQueuesRemainPendingUntilTheOrdinaryUpdaterRuns(bool removal)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var foreign = Scene(("CableHV", 0, 0), ("CableHV", 1, 0), ("CableHV", 2, 0));
            groups.ForceUpdate(); var foreignNodes = foreign.Members.SelectMany(Nodes).ToArray();
            var oldGroup = (BaseNodeGroup) foreignNodes[0].NodeGroup!;
            if (removal) SEntMan.DeleteEntity(foreign.Members[1]); else groups.QueueRemakeGroup(oldGroup);
            var remaking = oldGroup.Remaking; var groupMembers = oldGroup.Nodes.ToArray();
            var owned = Scene(("CableHV", 0, 0), ("CableHV", 1, 0));
            try
            {
                var result = groups.KsRebuildOwnedPowerNodes(owned.Grid, owned.Members, 4096);
                Assert.That(oldGroup.Removed, Is.False); Assert.That(oldGroup.Remaking, Is.EqualTo(remaking));
                Assert.That(oldGroup.Nodes, Is.EquivalentTo(groupMembers));
                foreach (var node in foreignNodes) Assert.That(node.NodeGroup, Is.SameAs(oldGroup));
                Assert.That(result.IssueCode, Is.Null);
                groups.ForceUpdate(); Assert.That(oldGroup.Removed, Is.True);
                if (removal)
                {
                    Assert.That(foreignNodes[1].NodeGroup, Is.Null);
                    Assert.That(foreignNodes[0].NodeGroup, Is.Not.Null.And.Not.SameAs(foreignNodes[2].NodeGroup));
                }
                else Assert.That(foreignNodes.Select(node => node.NodeGroup).Distinct().Count(), Is.EqualTo(1));
            }
            finally { maps.DeleteMap(owned.Map); maps.DeleteMap(foreign.Map); }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task OwnedCallbackRequeuesAreRejectedAndPreservedForOrdinaryProcessing(bool remake)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var groups = Pair.Server.System<NodeGroupSystem>();
            var fixture = Pair.Server.System<KsProcgenInsertionTestSystem>(); fixture.Reset();
            var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0)); var callbacks = 0;
            var node = Nodes(scene.Members[0]).Single();
            fixture.NodesRebuiltAction = uid =>
            {
                if (uid != scene.Members[0]) return; callbacks++;
                if (remake) groups.QueueRemakeGroup((BaseNodeGroup) node.NodeGroup!); else groups.QueueReflood(node);
            };
            try
            {
                Assert.That(groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.EqualTo("OwnedNodesCallbackDrift"));
                fixture.Reset(); Assert.That(callbacks, Is.EqualTo(1)); var pending = node.NodeGroup;
                Assert.That(node.FlaggedForFlood, Is.True);
                Assert.That(groups.KsInspectOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.EqualTo("OwnedNodesPendingRebuild"));
                Assert.That(node.NodeGroup, Is.SameAs(pending)); Assert.That(node.FlaggedForFlood, Is.True);
                groups.ForceUpdate(); Assert.That(node.FlaggedForFlood, Is.False);
                Assert.That(node.NodeGroup, Is.Not.Null.And.Not.SameAs(pending));
                Assert.That(((BaseNodeGroup) pending!).Removed, Is.True);
                Assert.That(groups.KsInspectOwnedPowerNodes(scene.Grid, scene.Members, 4096).IssueCode, Is.Null);
            }
            finally { fixture.Reset(); maps.DeleteMap(scene.Map); }
        });
    }

    [TestCase("split")] [TestCase("unpause")] [TestCase("position")] [TestCase("map")]
    public async Task CallbackFrameChangesCannotProduceASuccessfulTopologyResult(string change)
    {
        await AssertOnServer(() =>
        {
            var maps = Pair.Server.System<SharedMapSystem>(); var transforms = Pair.Server.System<SharedTransformSystem>();
            var groups = Pair.Server.System<NodeGroupSystem>(); var fixture = Pair.Server.System<KsProcgenInsertionTestSystem>(); fixture.Reset();
            var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0));
            var otherMap = maps.CreateMap(out var otherMapId, runMapInit: true); maps.SetPaused(otherMapId, true);
            var callbacks = 0;
            fixture.NodesRebuiltAction = uid =>
            {
                if (uid != scene.Members[0]) return; callbacks++;
                switch (change)
                {
                    case "split": SEntMan.GetComponent<MapGridComponent>(scene.Grid).CanSplit = true; break;
                    case "unpause": maps.SetPaused(scene.Map, false); break;
                    case "position": transforms.SetLocalPosition(scene.Grid, new Vector2(2f, 0f)); break;
                    case "map": transforms.SetParent(scene.Grid, otherMap); break;
                }
            };
            try
            {
                var result = groups.KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 4096);
                Assert.That(callbacks, Is.EqualTo(1)); Assert.That(result.IssueCode, Is.EqualTo("OwnedNodesFrameDrift"));
            }
            finally { fixture.Reset(); maps.DeleteMap(scene.Map); maps.DeleteMap(otherMapId); }
        });
    }
}
