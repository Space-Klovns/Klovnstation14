using System.Linq;
using System.Threading;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Pow3r;
using Content.Shared.NodeContainer;

namespace Content.Server.Power.EntitySystems;

/// <summary>Scoped native reconnection only; no solver tick, battery sync or operating-power claim.</summary>
internal sealed record KsOwnedPowerReconnectResult(string? IssueCode, int Work, int NetworkCount, int ReconnectedCount);

public sealed partial class PowerNetSystem
{
    [Dependency] private NodeGroupSystem _ksNodeGroups = default!;
    private sealed class KsPowerNetworkPlan(BaseNodeGroup group, PowerState.Network network, bool pending)
    {
        public readonly BaseNodeGroup Group = group;
        public readonly PowerState.Network Network = network;
        public readonly bool Pending = pending;
        public readonly HashSet<PowerState.NodeId> Loads = new();
        public readonly HashSet<PowerState.NodeId> Supplies = new();
        public readonly HashSet<PowerState.NodeId> BatteryLoads = new();
        public readonly HashSet<PowerState.NodeId> BatterySupplies = new();
    }

    internal KsOwnedPowerReconnectResult KsReconnectOwnedPowerNetworks(EntityUid gridUid,
        IReadOnlyList<EntityUid> members, int maximumWork, CancellationToken cancellationToken = default) =>
        KsProcessOwnedPowerNetworks(gridUid, members, maximumWork, reconnect: true, cancellationToken);

    internal KsOwnedPowerReconnectResult KsInspectOwnedPowerNetworks(EntityUid gridUid,
        IReadOnlyList<EntityUid> members, int maximumWork, CancellationToken cancellationToken = default) =>
        KsProcessOwnedPowerNetworks(gridUid, members, maximumWork, reconnect: false, cancellationToken);

    /// <summary>
    /// The caller supplies its complete inspected scene and retires it on post-mutation failure.
    /// Native reconnect methods raise no events or callbacks. All validation/work reservations precede
    /// the first write; only pending networks belonging wholly to this paused grid are reconnected.
    /// </summary>
    private KsOwnedPowerReconnectResult KsProcessOwnedPowerNetworks(EntityUid gridUid,
        IReadOnlyList<EntityUid> members, int maximumWork, bool reconnect, CancellationToken cancellationToken)
    {
        var work = 0;
        var reconnected = 0;
        var budgetExceeded = false;
        var plans = new Dictionary<BaseNodeGroup, KsPowerNetworkPlan>();
        KsOwnedPowerReconnectResult Result(string? code) => new(code, work, plans.Count, reconnected);
        bool Spend(int amount = 1)
        {
            if (amount < 0 || amount > maximumWork - work) { budgetExceeded = true; return false; }
            work += amount;
            return true;
        }
        if (members == null || maximumWork is < 0 or > 1_000_000 || members.Count > 4096)
            return Result("InvalidOwnedPowerBudget");
        if (cancellationToken.IsCancellationRequested) return Result("OwnedPowerCancelled");
        var topology = _ksNodeGroups.KsInspectOwnedPowerNodes(gridUid, members,
            maximumWork, cancellationToken: cancellationToken);
        if (!Spend(topology.Work)) return Result("OwnedPowerWorkBudget");
        if (topology.IssueCode != null) return Result(topology.IssueCode);
        var owners = new HashSet<EntityUid>(members);
        var loads = new Dictionary<PowerState.Load, PowerState.Network>();
        var supplies = new Dictionary<PowerState.Supply, PowerState.Network>();
        var charging = new Dictionary<PowerState.Battery, PowerState.Network>();
        var discharging = new Dictionary<PowerState.Battery, PowerState.Network>();
        var providers = new HashSet<ApcPowerProviderComponent>();

        bool Owns<T>(T component) where T : Component =>
            component != null && owners.Contains(component.Owner) && !EntityManager.IsQueuedForDeletion(component.Owner) &&
            TryComp<T>(component.Owner, out var current) && ReferenceEquals(current, component) &&
            Transform(component.Owner).GridUid == gridUid && MetaData(component.Owner).EntityPaused;
        bool Load(KsPowerNetworkPlan plan, PowerState.Load load) =>
            load.Paused && KsRegistered(_powerState.Loads, load.Id, load) &&
            (load.LinkedNetwork == plan.Network.Id || plan.Pending && load.LinkedNetwork == default) &&
            plan.Loads.Add(load.Id) && loads.TryAdd(load, plan.Network);
        bool Supply(KsPowerNetworkPlan plan, PowerState.Supply supply) =>
            supply.Paused && KsRegistered(_powerState.Supplies, supply.Id, supply) &&
            (supply.LinkedNetwork == plan.Network.Id || plan.Pending && supply.LinkedNetwork == default) &&
            plan.Supplies.Add(supply.Id) && supplies.TryAdd(supply, plan.Network);
        bool Battery(KsPowerNetworkPlan plan, EntityUid uid, bool charge)
        {
            if (!TryComp<PowerNetworkBatteryComponent>(uid, out var component) || !Owns(component)) return false;
            var battery = component.NetworkBattery;
            if (!battery.Paused || !KsRegistered(_powerState.Batteries, battery.Id, battery)) return false;
            var link = charge ? battery.LinkedNetworkCharging : battery.LinkedNetworkDischarging;
            if (link != plan.Network.Id && !(plan.Pending && link == default)) return false;
            return charge ? plan.BatteryLoads.Add(battery.Id) && charging.TryAdd(battery, plan.Network)
                : plan.BatterySupplies.Add(battery.Id) && discharging.TryAdd(battery, plan.Network);
        }
        bool BaseMembers<T>(BasePowerNet<T> group, KsPowerNetworkPlan plan) where T : IBasePowerNet
        {
            foreach (var consumer in group.Consumers)
                if (!Spend() || !Owns(consumer) || !ReferenceEquals(consumer.Net, group) || !Load(plan, consumer.NetworkLoad)) return false;
            foreach (var supplier in group.Suppliers)
                if (!Spend() || !Owns(supplier) || !ReferenceEquals(supplier.Net, group) || !Supply(plan, supplier.NetworkSupply)) return false;
            return true;
        }
        foreach (var uid in members)
        {
            if (cancellationToken.IsCancellationRequested) return Result("OwnedPowerCancelled");
            if (!Spend()) return Result("OwnedPowerWorkBudget");
            if (!TryComp<NodeContainerComponent>(uid, out var container)) continue;
            foreach (var node in container.Nodes.Values)
            {
                if (!Spend()) return Result("OwnedPowerWorkBudget");
                if (node.NodeGroup is not BaseNodeGroup group || plans.ContainsKey(group)) continue;
                var network = group switch { PowerNet power => power.NetworkNode, ApcNet apc => apc.NetworkNode, _ => null };
                if (network == null || !KsRegistered(_powerState.Networks, network.Id, network)) return Result("OwnedPowerNetworkDrift");
                var pending = group is PowerNet powerGroup ? _powerNetReconnectQueue.Contains(powerGroup)
                    : _apcNetReconnectQueue.Contains((ApcNet) group);
                plans.Add(group, new(group, network, pending));
            }
        }
        foreach (var plan in plans.Values)
        {
            if (cancellationToken.IsCancellationRequested) return Result("OwnedPowerCancelled");
            if (!Spend()) return Result("OwnedPowerWorkBudget");
            // Bound mutable native lists before inspecting their members.
            var count = plan.Group switch
            {
                PowerNet powerLists => (long) powerLists.Consumers.Count + powerLists.Suppliers.Count + powerLists.Chargers.Count + powerLists.Dischargers.Count,
                ApcNet apcLists => (long) apcLists.Consumers.Count + apcLists.Suppliers.Count + apcLists.Apcs.Count + apcLists.Providers.Count,
                _ => long.MaxValue,
            };
            if (count > maximumWork - work) return Result("OwnedPowerWorkBudget");
            bool valid;
            if (plan.Group is PowerNet power)
            {
                valid = BaseMembers(power, plan);
                if (!valid) return Result(budgetExceeded ? "OwnedPowerWorkBudget" : "OwnedPowerForeignMember");
                foreach (var charger in power.Chargers)
                    if (!Spend() || !Owns(charger) || !ReferenceEquals(charger.Net, power) || !Battery(plan, charger.Owner, charge: true))
                        return Result(budgetExceeded ? "OwnedPowerWorkBudget" : "OwnedPowerForeignMember");
                foreach (var discharger in power.Dischargers)
                    if (!Spend() || !Owns(discharger) || !ReferenceEquals(discharger.Net, power) || !Battery(plan, discharger.Owner, charge: false))
                        return Result(budgetExceeded ? "OwnedPowerWorkBudget" : "OwnedPowerForeignMember");
            }
            else
            {
                var apc = (ApcNet) plan.Group;
                valid = BaseMembers(apc, plan);
                if (!valid) return Result(budgetExceeded ? "OwnedPowerWorkBudget" : "OwnedPowerForeignMember");
                foreach (var battery in apc.Apcs)
                    if (!Spend() || !Owns(battery) || !ReferenceEquals(battery.Net, apc) || !Battery(plan, battery.Owner, charge: false))
                        return Result(budgetExceeded ? "OwnedPowerWorkBudget" : "OwnedPowerForeignMember");
                foreach (var provider in apc.Providers)
                {
                    if (!Spend()) return Result("OwnedPowerWorkBudget");
                    if (!Owns(provider) || !ReferenceEquals(provider.Net, apc) || !providers.Add(provider)) return Result("OwnedPowerForeignMember");
                    foreach (var receiver in provider.LinkedReceivers)
                    {
                        if (!Spend()) return Result("OwnedPowerWorkBudget");
                        if (!Owns(receiver) || !ReferenceEquals(receiver.Provider, provider) || !Load(plan, receiver.NetworkLoad))
                            return Result("OwnedPowerForeignMember");
                    }
                }
            }
            if (plan.Pending && !reconnect) return Result("OwnedPowerReconnectPending");
            if (plan.Pending && _powerState.GroupedNets != null) return Result("OwnedPowerCacheDrift");
            if (plan.Network.Loads == null || plan.Network.Supplies == null ||
                plan.Network.BatteryLoads == null || plan.Network.BatterySupplies == null)
                return Result("OwnedPowerLinkDrift");
            // A stale foreign list must not be silently repaired into an apparently isolated scene.
            foreach (var (current, expected) in KsPowerLists(plan))
            {
                foreach (var id in current)
                {
                    if (!Spend()) return Result("OwnedPowerWorkBudget");
                    if (!expected.Contains(id)) return Result("OwnedPowerForeignLink");
                }
                if (!plan.Pending && (current.Count != expected.Count || !expected.SetEquals(current)))
                    return Result("OwnedPowerLinkDrift");
            }
        }
        // Verify the reverse direction as well: a component omitted from a native group list
        // must not smuggle an external network/backlink into the declared complete scene.
        bool MatchesNetwork<T>(IBasePowerNet? network, Dictionary<T, PowerState.Network> bindings, T state) where T : class =>
            network == null ? !bindings.ContainsKey(state) :
                network is BaseNodeGroup group && plans.ContainsKey(group) &&
                bindings.TryGetValue(state, out var expected) && ReferenceEquals(network.NetworkNode, expected);
        foreach (var uid in members)
        {
            if (cancellationToken.IsCancellationRequested) return Result("OwnedPowerCancelled");
            if (!Spend(amount: 8)) return Result("OwnedPowerWorkBudget");
            if (TryComp<PowerConsumerComponent>(uid, out var consumer) &&
                (!Owns(consumer) || !consumer.NetworkLoad.Paused || !KsRegistered(_powerState.Loads, consumer.NetworkLoad.Id, consumer.NetworkLoad) ||
                 !MatchesNetwork(consumer.Net, loads, consumer.NetworkLoad) ||
                 consumer.Net == null && consumer.NetworkLoad.LinkedNetwork != default)) return Result("OwnedPowerForeignMember");
            if (TryComp<PowerSupplierComponent>(uid, out var supplier) &&
                (!Owns(supplier) || !supplier.NetworkSupply.Paused || !KsRegistered(_powerState.Supplies, supplier.NetworkSupply.Id, supplier.NetworkSupply) ||
                 !MatchesNetwork(supplier.Net, supplies, supplier.NetworkSupply) ||
                 supplier.Net == null && supplier.NetworkSupply.LinkedNetwork != default)) return Result("OwnedPowerForeignMember");
            if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver) &&
                (!Owns(receiver) || !receiver.NetworkLoad.Paused || !KsRegistered(_powerState.Loads, receiver.NetworkLoad.Id, receiver.NetworkLoad) ||
                 receiver.Provider != null && (!Owns(receiver.Provider) || !providers.Contains(receiver.Provider)) ||
                 !MatchesNetwork(receiver.Provider?.Net, loads, receiver.NetworkLoad) ||
                 receiver.Provider == null && receiver.NetworkLoad.LinkedNetwork != default)) return Result("OwnedPowerForeignMember");
            if (TryComp<ApcPowerProviderComponent>(uid, out var provider) &&
                (!Owns(provider) || provider.Net != null &&
                    (provider.Net is not ApcNet providerNetwork || !plans.ContainsKey(providerNetwork) || !providers.Contains(provider)) ||
                 provider.Net == null && provider.LinkedReceivers.Count != 0)) return Result("OwnedPowerForeignMember");
            if (TryComp<PowerNetworkBatteryComponent>(uid, out var battery))
            {
                if (!Owns(battery) || !battery.NetworkBattery.Paused || !KsRegistered(_powerState.Batteries, battery.NetworkBattery.Id, battery.NetworkBattery))
                    return Result("OwnedPowerForeignMember");
                var chargeNetwork = TryComp<BatteryChargerComponent>(uid, out var charger) ? charger.Net : null;
                var dischargeNetwork = TryComp<BatteryDischargerComponent>(uid, out var discharger) ? discharger.Net : null;
                var apcNetwork = TryComp<ApcComponent>(uid, out var apc) ? apc.Net : null;
                if (dischargeNetwork != null && apcNetwork != null) return Result("OwnedPowerCapabilityUnsupported");
                if (!MatchesNetwork(chargeNetwork, charging, battery.NetworkBattery) ||
                    !MatchesNetwork((IBasePowerNet?) dischargeNetwork ?? apcNetwork, discharging, battery.NetworkBattery) ||
                    chargeNetwork == null && battery.NetworkBattery.LinkedNetworkCharging != default ||
                    dischargeNetwork == null && apcNetwork == null && battery.NetworkBattery.LinkedNetworkDischarging != default)
                    return Result("OwnedPowerForeignMember");
            }
            else if (TryComp<BatteryChargerComponent>(uid, out var orphanCharger) && orphanCharger.Net != null ||
                     TryComp<BatteryDischargerComponent>(uid, out var orphanDischarger) && orphanDischarger.Net != null ||
                     TryComp<ApcComponent>(uid, out var orphanApc) && orphanApc.Net != null)
                return Result("OwnedPowerForeignMember");
        }
        bool LinksMatch() => loads.All(pair => pair.Key.LinkedNetwork == pair.Value.Id) &&
            supplies.All(pair => pair.Key.LinkedNetwork == pair.Value.Id) &&
            charging.All(pair => pair.Key.LinkedNetworkCharging == pair.Value.Id) &&
            discharging.All(pair => pair.Key.LinkedNetworkDischarging == pair.Value.Id);
        // Reserve native list clears/inserts, backlinks and the final independent comparison before writes.
        long writes = 0;
        foreach (var plan in plans.Values)
        {
            var entries = (long) plan.Loads.Count + plan.Supplies.Count + plan.BatteryLoads.Count + plan.BatterySupplies.Count;
            writes += 4 + entries * (plan.Pending ? 4 : 2);
        }
        if (writes > maximumWork - work || !Spend((int) writes)) return Result("OwnedPowerWorkBudget");
        if (cancellationToken.IsCancellationRequested) return Result("OwnedPowerCancelled");
        // No callbacks/yields occur between this point and the final comparison.
        foreach (var plan in plans.Values.Where(plan => plan.Pending))
        {
            if (plan.Group is PowerNet power)
            {
                DoReconnectPowerNet(power);
                _powerNetReconnectQueue.Remove(power);
            }
            else
            {
                var apc = (ApcNet) plan.Group;
                DoReconnectApcNet(apc);
                _apcNetReconnectQueue.Remove(apc);
            }
            reconnected++;
        }
        if (!LinksMatch() || plans.Values.Any(plan => KsPowerLists(plan).Any(pair =>
                pair.Current.Count != pair.Expected.Count || !pair.Expected.SetEquals(pair.Current))))
            return Result("OwnedPowerLinkDrift");
        return Result(cancellationToken.IsCancellationRequested ? "OwnedPowerCancelled" : null);
    }

    private static IEnumerable<(List<PowerState.NodeId> Current, HashSet<PowerState.NodeId> Expected)> KsPowerLists(KsPowerNetworkPlan plan)
    {
        yield return (plan.Network.Loads, plan.Loads);
        yield return (plan.Network.Supplies, plan.Supplies);
        yield return (plan.Network.BatteryLoads, plan.BatteryLoads);
        yield return (plan.Network.BatterySupplies, plan.BatterySupplies);
    }

    private static bool KsRegistered<T>(PowerState.GenIdStorage<T> storage, PowerState.NodeId id, T expected) where T : class
    {
        if (id.Generation == 0 || id.Index < 0) return false;
        try { return ReferenceEquals(storage[id], expected); }
        catch (Exception exception) when (exception is KeyNotFoundException or IndexOutOfRangeException) { return false; }
    }
}
