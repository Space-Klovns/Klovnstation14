#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Pow3r;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenNodeScopeTests
{
    [Test]
    public async Task NativePowerReconnectOnlyConsumesOwnedQueuesAndNeverTicksPower()
    {
        await AssertOnServer(() =>
        {
            var power = Server.System<PowerNetSystem>();
            var maps = Server.System<SharedMapSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var owned = PowerScene();
            var foreign = PowerScene();
            try
            {
                var ownedBefore = PowerScalars(owned.Members);
                var foreignBefore = PowerScalars(foreign.Members);
                Assert.That(power.KsInspectOwnedPowerNetworks(foreign.Grid, foreign.Members, 65536).IssueCode,
                    Is.EqualTo("OwnedPowerReconnectPending"));
                var result = power.KsReconnectOwnedPowerNetworks(owned.Grid, owned.Members, 65536);
                Assert.That(result.IssueCode, Is.Null);
                Assert.That(result.NetworkCount, Is.EqualTo(4));
                Assert.That(result.ReconnectedCount, Is.EqualTo(3));
                var load = SEntMan.GetComponent<PowerConsumerComponent>(owned.Members[0]);
                Assert.That(load.NetworkLoad.LinkedNetwork, Is.EqualTo(load.Net!.NetworkNode.Id));
                Assert.That(load.Net.NetworkNode.Loads, Does.Contain(load.NetworkLoad.Id));
                var supplier = SEntMan.GetComponent<PowerSupplierComponent>(owned.Members[0]);
                Assert.That(supplier.NetworkSupply.LinkedNetwork, Is.EqualTo(supplier.Net!.NetworkNode.Id));
                Assert.That(supplier.Net.NetworkNode.Supplies, Does.Contain(supplier.NetworkSupply.Id));
                var receiver = SEntMan.GetComponent<ApcPowerReceiverComponent>(owned.Members.Last());
                Assert.That(receiver.Provider, Is.Not.Null);
                Assert.That(receiver.NetworkLoad.LinkedNetwork, Is.EqualTo(receiver.Provider!.Net!.NetworkNode.Id));
                Assert.That(PowerScalars(owned.Members), Is.EqualTo(ownedBefore));
                Assert.That(PowerScalars(foreign.Members), Is.EqualTo(foreignBefore));
                Assert.That(power.KsInspectOwnedPowerNetworks(owned.Grid, owned.Members, 65536).IssueCode, Is.Null);
                Assert.That(power.KsReconnectOwnedPowerNetworks(owned.Grid, owned.Members, 65536).ReconnectedCount, Is.Zero);
                Assert.That(power.KsInspectOwnedPowerNetworks(foreign.Grid, foreign.Members, 65536).IssueCode,
                    Is.EqualTo("OwnedPowerReconnectPending"));
                var foreignResult = power.KsReconnectOwnedPowerNetworks(foreign.Grid, foreign.Members, 65536);
                Assert.That(foreignResult.IssueCode, Is.Null);
                Assert.That(foreignResult.ReconnectedCount, Is.EqualTo(3), "foreign pending queues must still exist");
                Assert.That(PowerScalars(foreign.Members), Is.EqualTo(foreignBefore));
            }
            finally { maps.DeleteMap(owned.Map); maps.DeleteMap(foreign.Map); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task NativePowerReconnectReservesTheExactWorkAllowanceBeforeMutation(bool shortByOne)
    {
        await AssertOnServer(() =>
        {
            var power = Server.System<PowerNetSystem>();
            var maps = Server.System<SharedMapSystem>();
            var probe = PowerScene();
            int allowance;
            try
            {
                var measured = power.KsReconnectOwnedPowerNetworks(probe.Grid, probe.Members, 65536);
                Assert.That(measured.IssueCode, Is.Null);
                allowance = measured.Work;
            }
            finally { maps.DeleteMap(probe.Map); }
            var scene = PowerScene();
            try
            {
                var consumer = SEntMan.GetComponent<PowerConsumerComponent>(scene.Members[0]);
                Assert.That(consumer.NetworkLoad.LinkedNetwork, Is.EqualTo(default(PowerState.NodeId)));
                var result = power.KsReconnectOwnedPowerNetworks(scene.Grid, scene.Members, allowance - (shortByOne ? 1 : 0));
                if (shortByOne)
                {
                    Assert.That(result.IssueCode, Is.EqualTo("OwnedPowerWorkBudget"));
                    Assert.That(result.ReconnectedCount, Is.Zero);
                    Assert.That(consumer.NetworkLoad.LinkedNetwork, Is.EqualTo(default(PowerState.NodeId)));
                    Assert.That(power.KsInspectOwnedPowerNetworks(scene.Grid, scene.Members, 65536).IssueCode,
                        Is.EqualTo("OwnedPowerReconnectPending"));
                }
                else
                {
                    Assert.That(result.IssueCode, Is.Null);
                    Assert.That(result.Work, Is.EqualTo(allowance));
                    Assert.That(result.ReconnectedCount, Is.EqualTo(3));
                }
            }
            finally { maps.DeleteMap(scene.Map); }
        });
    }

    [Test]
    public async Task CancelledPowerReconnectLeavesLinksAndPendingWorkUntouched()
    {
        await AssertOnServer(() =>
        {
            var power = Server.System<PowerNetSystem>();
            var scene = PowerScene();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                var result = power.KsReconnectOwnedPowerNetworks(scene.Grid, scene.Members, 65536, cancellationToken: cancellation.Token);
                Assert.That(result.IssueCode, Is.EqualTo("OwnedPowerCancelled"));
                Assert.That(result.ReconnectedCount, Is.Zero);
                Assert.That(power.KsInspectOwnedPowerNetworks(scene.Grid, scene.Members, 65536).IssueCode,
                    Is.EqualTo("OwnedPowerReconnectPending"));
            }
            finally { Server.System<SharedMapSystem>().DeleteMap(scene.Map); }
        });
    }

    [TestCase("foreign-charger")] [TestCase("foreign-receiver")] [TestCase("foreign-list")]
    [TestCase("missing-load")] [TestCase("unpaused-load")] [TestCase("invalid-registration")]
    public async Task PowerReconnectRejectsUnownedOrUnreportedBindingsBeforeWriting(string defect)
    {
        await AssertOnServer(() =>
        {
            var power = Server.System<PowerNetSystem>();
            var maps = Server.System<SharedMapSystem>();
            var scene = PowerScene();
            var foreign = PowerScene();
            var consumer = SEntMan.GetComponent<PowerConsumerComponent>(scene.Members[0]);
            var originalId = consumer.NetworkLoad.Id;
            var network = (PowerNet) consumer.Net!;
            var foreignCharger = SEntMan.GetComponent<BatteryChargerComponent>(foreign.Members[3]);
            var foreignReceiver = SEntMan.GetComponent<ApcPowerReceiverComponent>(foreign.Members.Last());
            var provider = SEntMan.GetComponent<ApcPowerReceiverComponent>(scene.Members.Last()).Provider!;
            var foreignLoad = SEntMan.GetComponent<PowerConsumerComponent>(foreign.Members[0]).NetworkLoad;
            try
            {
                switch (defect)
                {
                    case "foreign-charger": network.Chargers.Add(foreignCharger); break;
                    case "foreign-receiver": provider.LinkedReceivers.Add(foreignReceiver); break;
                    case "foreign-list": network.NetworkNode.Loads.Add(foreignLoad.Id); break;
                    case "missing-load": network.Consumers.Remove(consumer); break;
                    case "unpaused-load": consumer.NetworkLoad.Paused = false; break;
                    case "invalid-registration": consumer.NetworkLoad.Id = new(originalId.Index, originalId.Generation + 1); break;
                }
                var result = power.KsReconnectOwnedPowerNetworks(scene.Grid, scene.Members, 65536);
                Assert.That(result.IssueCode, Is.EqualTo(defect == "foreign-list" ? "OwnedPowerForeignLink" : "OwnedPowerForeignMember"));
                Assert.That(result.ReconnectedCount, Is.Zero);
                Assert.That(consumer.NetworkLoad.LinkedNetwork, Is.EqualTo(default(PowerState.NodeId)));
                Assert.That(foreignLoad.LinkedNetwork, Is.EqualTo(default(PowerState.NodeId)));
            }
            finally
            {
                network.Chargers.Remove(foreignCharger);
                provider.LinkedReceivers.Remove(foreignReceiver);
                network.NetworkNode.Loads.Remove(foreignLoad.Id);
                if (!network.Consumers.Contains(consumer)) network.Consumers.Add(consumer);
                consumer.NetworkLoad.Paused = true;
                consumer.NetworkLoad.Id = originalId;
                maps.DeleteMap(scene.Map); maps.DeleteMap(foreign.Map);
            }
        });
    }

    [Test]
    public async Task ReadOnlyPowerInspectionDetectsBacklinkDriftWithoutRepair()
    {
        await AssertOnServer(() =>
        {
            var power = Server.System<PowerNetSystem>();
            var scene = PowerScene();
            try
            {
                Assert.That(power.KsReconnectOwnedPowerNetworks(scene.Grid, scene.Members, 65536).IssueCode, Is.Null);
                var consumer = SEntMan.GetComponent<PowerConsumerComponent>(scene.Members[0]);
                consumer.NetworkLoad.LinkedNetwork = default;
                var result = power.KsInspectOwnedPowerNetworks(scene.Grid, scene.Members, 65536);
                Assert.That(result.IssueCode, Is.EqualTo("OwnedPowerForeignMember"));
                Assert.That(result.ReconnectedCount, Is.Zero);
                Assert.That(consumer.NetworkLoad.LinkedNetwork, Is.EqualTo(default(PowerState.NodeId)));
            }
            finally { Server.System<SharedMapSystem>().DeleteMap(scene.Map); }
        });
    }

    private (MapId Map, EntityUid Grid, EntityUid[] Members) PowerScene()
    {
        var scene = Scene(("CableHV", 0, 0), ("CableHV", 1, 0), ("CableHV", 2, 1),
            ("SubstationBasic", 1, 0), ("CableMV", 1, 0), ("CableMV", 2, 0), ("APCBasic", 2, 0),
            ("CableApcExtension", 2, 0), ("CableApcExtension", 3, 0), ("KsProcgenFilteredComputerFixture", 3, 0));
        // Use normal component registration before topology construction; no prototype fixtures or private links.
        SEntMan.AddComponent<PowerConsumerComponent>(scene.Members[0]);
        SEntMan.AddComponent<PowerSupplierComponent>(scene.Members[0]);
        var maps = Server.System<SharedMapSystem>();
        maps.SetPaused(scene.Map, false);
        maps.SetPaused(scene.Map, true);
        Assert.That(Server.System<NodeGroupSystem>().KsRebuildOwnedPowerNodes(scene.Grid, scene.Members, 65536).IssueCode, Is.Null);
        return scene;
    }

    private Dictionary<object, Dictionary<string, object?>> PowerScalars(EntityUid[] members)
    {
        var states = new HashSet<object>();
        foreach (var uid in members)
        {
            if (SEntMan.TryGetComponent<PowerNetworkBatteryComponent>(uid, out var battery)) states.Add(battery.NetworkBattery);
            if (SEntMan.TryGetComponent<PowerConsumerComponent>(uid, out var consumer)) states.Add(consumer.NetworkLoad);
            if (SEntMan.TryGetComponent<PowerSupplierComponent>(uid, out var supplier)) states.Add(supplier.NetworkSupply);
            if (SEntMan.TryGetComponent<ApcPowerReceiverComponent>(uid, out var receiver)) states.Add(receiver.NetworkLoad);
        }
        foreach (var group in members.Where(uid => SEntMan.HasComponent<NodeContainerComponent>(uid)).SelectMany(Nodes)
                     .Select(node => node.NodeGroup).OfType<IBasePowerNet>())
            states.Add(group.NetworkNode);
        // Inspect all scalar runtime fields, including ramp/receiving/paused state. The explicitly
        // intended changes are network member lists and LinkedNetwork* backlinks, checked separately.
        return states.ToDictionary(state => state, state => state.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType.IsValueType && !field.Name.StartsWith("LinkedNetwork", StringComparison.Ordinal))
            .ToDictionary(field => field.Name, field => field.GetValue(state)));
    }
}
