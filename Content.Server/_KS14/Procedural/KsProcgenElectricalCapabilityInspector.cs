using System.Collections.Immutable;
using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Shared._KS14.Procedural;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenElectricalDeviceRole : byte { HighVoltageCable, MediumVoltageCable, LowVoltageCable, Substation, Apc }

/// <summary>Inherited prototype facts only. Live node groups, mounting, delivered power and safety are unverified.</summary>
public sealed record KsProcgenElectricalDeviceDeclaration(string PrototypeId, KsProcgenElectricalDeviceRole Role,
    bool InitiallyAnchored, int? TransferRange, float? MaximumCharge, float? StartingCharge,
    float? MaximumSupply, float? MaximumChargeRate, float? SupplyRampRate, float? SupplyRampTolerance,
    float? Efficiency, bool? NetworkEnabled, bool? CanCharge, bool? CanDischarge, bool? MainBreakerEnabled,
    float? BreakerMaximumLoad, float? BreakerTripSeconds);

public sealed record KsProcgenElectricalCapabilityReport(IReadOnlyList<KsProcgenElectricalDeviceDeclaration> Devices)
{
    public bool ElectricalTopologyVerified => false;
    public bool ElectricalSupplyVerified => false;
}

/// <summary>Reads prototype components without spawning entities or changing node/provider state.</summary>
public static class KsProcgenElectricalCapabilityInspector
{
    public static bool TryInspect(IPrototypeManager prototypeManager, IComponentFactory componentFactory,
        KsProcgenElectricalProfile profile, out KsProcgenElectricalCapabilityReport? report, out KsProcgenIssue? issue)
    {
        report = null;
        issue = null;
        if (profile == null)
        {
            issue = new("InvalidElectricalInspection", "A validated electrical profile is required.");
            return false;
        }
        var choices = new List<(string Id, KsProcgenElectricalDeviceRole Role)>
        {
            (profile.HighVoltageCable, KsProcgenElectricalDeviceRole.HighVoltageCable),
            (profile.MediumVoltageCable, KsProcgenElectricalDeviceRole.MediumVoltageCable),
            (profile.LowVoltageCable, KsProcgenElectricalDeviceRole.LowVoltageCable),
        };
        choices.AddRange(profile.Substations.Where(choice => choice.Weight > 0f)
            .Select(choice => (choice.Prototype, KsProcgenElectricalDeviceRole.Substation)));
        choices.AddRange(profile.Apcs.Where(choice => choice.Weight > 0f)
            .Select(choice => (choice.Prototype, KsProcgenElectricalDeviceRole.Apc)));
        var declarations = new List<KsProcgenElectricalDeviceDeclaration>();
        foreach (var (id, role) in choices)
        {
            if (!TryInspectDevice(prototypeManager, componentFactory, id, role, out var declaration))
            {
                issue = new("ElectricalPrototypeCapabilityMismatch", $"Prototype {id} cannot fulfill the declared {role} role.");
                return false;
            }
            declarations.Add(declaration!);
        }
        report = new(declarations.ToImmutableArray());
        return true;
    }

    private static bool TryInspectDevice(IPrototypeManager prototypeManager, IComponentFactory componentFactory,
        string id, KsProcgenElectricalDeviceRole role, out KsProcgenElectricalDeviceDeclaration? declaration)
    {
        declaration = null;
        if (!prototypeManager.TryIndex<EntityPrototype>(id, out var prototype) || prototype.Abstract ||
            !prototype.TryComp<NodeContainerComponent>(out var nodes, componentFactory) || nodes.Nodes.Count is < 1 or > 64)
            return false;
        prototype.TryComp<TransformComponent>(out var transform, componentFactory);
        prototype.TryComp<ExtensionCableProviderComponent>(out var extension, componentFactory);
        prototype.TryComp<ApcPowerProviderComponent>(out var provider, componentFactory);
        bool Node(NodeGroupID group, bool cable, string? nodeId = null) => nodes.Nodes.Any(pair =>
            (nodeId == null || pair.Key == nodeId) && pair.Value.NodeGroupID == group &&
            (cable ? pair.Value is CableNode : pair.Value is CableDeviceNode { Enabled: true } and not CableTerminalNode));
        if (role is KsProcgenElectricalDeviceRole.HighVoltageCable or KsProcgenElectricalDeviceRole.MediumVoltageCable or
            KsProcgenElectricalDeviceRole.LowVoltageCable)
        {
            var group = role == KsProcgenElectricalDeviceRole.HighVoltageCable ? NodeGroupID.HVPower
                : role == KsProcgenElectricalDeviceRole.MediumVoltageCable ? NodeGroupID.MVPower : NodeGroupID.Apc;
            var cableType = role == KsProcgenElectricalDeviceRole.HighVoltageCable ? CableType.HighVoltage
                : role == KsProcgenElectricalDeviceRole.MediumVoltageCable ? CableType.MediumVoltage : CableType.Apc;
            if (!prototype.TryComp<CableComponent>(out var cable, componentFactory) || cable.CableType != cableType ||
                !Node(group, true) || transform?.Anchored != true || role == KsProcgenElectricalDeviceRole.LowVoltageCable &&
                (extension == null || !extension.Connectable || extension.TransferRange < 0 || provider == null ||
                 (NodeGroupID) provider.Voltage != NodeGroupID.Apc || !Node(group, true, nodeId: provider.NodeId)))
                return false;
            declaration = new(id, role, true, extension?.TransferRange,
                null, null, null, null, null, null, null, null, null, null, null, null, null);
            return true;
        }
        var input = role == KsProcgenElectricalDeviceRole.Substation ? NodeGroupID.HVPower : NodeGroupID.MVPower;
        var output = role == KsProcgenElectricalDeviceRole.Substation ? NodeGroupID.MVPower : NodeGroupID.Apc;
        if (!prototype.TryComp<BatteryComponent>(out var battery, componentFactory) ||
            !prototype.TryComp<PowerNetworkBatteryComponent>(out var networkBattery, componentFactory) ||
            !prototype.TryComp<BatteryChargerComponent>(out var charger, componentFactory) ||
            (NodeGroupID) charger.Voltage != input || !Node(input, false, nodeId: charger.NodeId) || !Node(output, false) ||
            !Positive(battery.MaxCharge) || !Nonnegative(battery.StartingCharge) || battery.StartingCharge > battery.MaxCharge ||
            !Positive(networkBattery.MaxSupply) || !Positive(networkBattery.MaxChargeRate) ||
            !Nonnegative(networkBattery.SupplyRampRate) || !Nonnegative(networkBattery.SupplyRampTolerance) ||
            !Positive(networkBattery.Efficiency) || networkBattery.Efficiency > 1f)
            return false;
        ApcComponent? apc = null;
        if (role == KsProcgenElectricalDeviceRole.Substation)
        {
            if (!prototype.TryComp<BatteryDischargerComponent>(out var discharger, componentFactory) ||
                (NodeGroupID) discharger.Voltage != output || !Node(output, false, nodeId: discharger.NodeId))
                return false;
        }
        else if (!prototype.TryComp<ApcComponent>(out apc, componentFactory) || provider == null ||
                 (NodeGroupID) apc.Voltage != output || !Node(output, false, nodeId: apc.NodeId) ||
                 (NodeGroupID) provider.Voltage != output || !Node(output, false, nodeId: provider.NodeId) ||
                 !Positive(apc.MaxLoad) || !double.IsFinite(apc.TripTime.TotalSeconds) || apc.TripTime.TotalSeconds <= 0.0 ||
                 apc.TripTime.TotalSeconds > (double) float.MaxValue ||
                 extension != null && (!extension.Connectable || extension.TransferRange < 0))
            return false;
        declaration = new(id, role, transform?.Anchored ?? false, extension?.TransferRange, battery.MaxCharge,
            battery.StartingCharge, networkBattery.MaxSupply, networkBattery.MaxChargeRate,
            networkBattery.SupplyRampRate, networkBattery.SupplyRampTolerance, networkBattery.Efficiency,
            networkBattery.Enabled, networkBattery.CanCharge, networkBattery.CanDischarge,
            apc?.MainBreakerEnabled, apc?.MaxLoad, apc == null ? null : (float) apc.TripTime.TotalSeconds);
        return true;
    }

    private static bool Positive(float value) => float.IsFinite(value) && value > 0f;
    private static bool Nonnegative(float value) => float.IsFinite(value) && value >= 0f;
}
