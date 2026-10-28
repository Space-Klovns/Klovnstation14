using System.Linq;
using Content.Server.Atmos.Components;
using System.Numerics;
using System.Threading;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Nodes;
using Content.Shared._KS14.Procedural;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Power.Components;
using Content.Shared.Power;
using Content.Shared.Wall;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Procedural;

/// <summary>Scoped native node construction and read-only provider inspection for a private owned grid.</summary>
public sealed partial class KsProcgenOwnedElectricalSystem : EntitySystem
{
    [Dependency] private KsProcgenOwnedStageSystem _stages = default!;
    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private NodeGroupSystem _nodes = default!;
    [Dependency] private PowerNetSystem _powerNetworks = default!;
    [Dependency] private IGameTiming _timing = default!;

    internal string? Inspect(KsProcgenOwnedStage stage, KsProcgenInitialElectricalReceipt? expected,
        bool rebuildIfNeeded, int maximumWork, out KsProcgenInitialElectricalReceipt? receipt, out int work,
        CancellationToken cancellationToken = default)
    {
        receipt = null; work = 0;
        if (stage.Electrical is not { } plan) return null;
        if (!_ownership.Owns(stage)) return "OwnedElectricalStageNotOwned";
        if (maximumWork is < 0 or > 1_000_000) return "OwnedElectricalWorkBudget";
        cancellationToken.ThrowIfCancellationRequested();
        var used = 0;
        bool Spend(int amount = 1)
        {
            if (amount < 0 || amount > maximumWork - used) return false;
            used += amount; return true;
        }
        string? Run(out KsProcgenInitialElectricalReceipt? prepared)
        {
            prepared = null;
            var issue = _stages.InspectScene(stage, Math.Min(stage.MaxSpawnedEntities, maximumWork - used), out var members, out var scanned);
            if (!Spend(scanned)) return "OwnedElectricalWorkBudget";
            if (issue != null) return issue;
            var authorityIssue = InspectPolicyAndDevices(stage, plan.Profile, plan.Devices, () => Spend());
            if (authorityIssue != null) return authorityIssue;
            var owned = members.ToHashSet();
            var bindings = new Dictionary<string, EntityUid>(StringComparer.Ordinal);
            var cableIds = new Dictionary<(Robust.Shared.Maths.Vector2i Cell, KsProcgenElectricalVoltage Voltage), EntityUid>();
            var infrastructure = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < stage.Scene.Entities.Count; index++)
            {
                if (!Spend()) return "OwnedElectricalWorkBudget";
                var intent = stage.Scene.Entities[index]; var uid = stage.Entities[index];
                if (intent.ElectricalMemberId != null && !bindings.TryAdd(intent.ElectricalMemberId, uid)) return "OwnedElectricalDuplicateBinding";
                if (intent.Kind != KsProcgenOwnedSceneEntityKind.Cable) continue;
                if (intent.Voltage is not { } voltage || !TryComp(uid, out CableComponent? cable) ||
                    !Transform(uid).Anchored || !cableIds.TryAdd((new((int) Math.Floor((double) intent.Position.X),
                        (int) Math.Floor((double) intent.Position.Y)), voltage), uid)) return "OwnedElectricalCableDrift";
                var cableType = voltage == KsProcgenElectricalVoltage.High ? CableType.HighVoltage :
                    voltage == KsProcgenElectricalVoltage.Medium ? CableType.MediumVoltage : CableType.Apc;
                if (cable.CableType != cableType) return "OwnedElectricalCableVoltageDrift";
            }
            for (var index = 0; index < stage.Scene.Assemblies.Count; index++)
            foreach (var binding in stage.Scene.Assemblies[index].Bindings)
            {
                if (!Spend()) return "OwnedElectricalWorkBudget";
                var key = stage.Scene.Assemblies[index].RegionId + "/" + binding.MemberId;
                if (!stage.Assemblies[index].TryGetValue(binding.MemberId, out var uid) || !bindings.TryAdd(key, uid))
                    return "OwnedElectricalMemberDrift";
                if (binding.Origin == KsProcgenRoomMemberOrigin.Infrastructure) infrastructure.Add(key);
            }
            var walls = stage.Scene.Entities.Select((intent, index) => (intent, uid: stage.Entities[index]))
                .Where(item => item.intent.Kind == KsProcgenOwnedSceneEntityKind.Wall)
                .ToDictionary(item => new Robust.Shared.Maths.Vector2i((int) Math.Floor((double) item.intent.Position.X),
                    (int) Math.Floor((double) item.intent.Position.Y)), item => item.uid);
            if (cableIds.Count != plan.Cables.Count || infrastructure.Count != plan.Devices.Count) return "OwnedElectricalReservationDrift";
            foreach (var device in plan.Devices)
            {
                if (!Spend()) return "OwnedElectricalWorkBudget";
                if (!infrastructure.Contains(device.Id) || !bindings.TryGetValue(device.Id, out var uid) ||
                    !Transform(uid).Anchored || Transform(uid).ParentUid != stage.GridUid) return "OwnedElectricalConverterDrift";
                if (HasComp<WallMountComponent>(uid) != device.WallMounted) return "OwnedElectricalConverterMountDrift";
                if (device.WallMounted && (!walls.TryGetValue(device.Cell, out var wall) ||
                        !Transform(wall).Anchored || Transform(wall).ParentUid != stage.GridUid))
                    return "OwnedElectricalWallMountSupportDrift";
            }
            if (plan.Service is { } service)
            {
                var layoutIssue = InspectServiceLayout(stage, service.AllowedCableCells, service.MediumVoltageTargets,
                    service.FloorDomain, service.Rooms, () => Spend());
                if (layoutIssue != null) return layoutIssue;
                string? serviceIssue = null;
                if (plan.Profile.Distribution is not { } distribution ||
                    !service.Validate(distribution, plan.Cables, plan.Devices, () => Spend(), out serviceIssue))
                    return serviceIssue ?? "OwnedElectricalServiceProfileDrift";
                foreach (var cable in plan.Cables.Where(cable => cable.Voltage == KsProcgenElectricalVoltage.Low))
                {
                    if (!Spend()) return "OwnedElectricalWorkBudget";
                    if (!TryComp(cableIds[(cable.Cell, cable.Voltage)], out ExtensionCableProviderComponent? provider) ||
                        provider.TransferRange != service.ProviderRange) return "OwnedElectricalServiceRangeDrift";
                }
                var structures = stage.Scene.Entities.Select((intent, index) => (intent, uid: stage.Entities[index]))
                    .Where(item => item.intent.Kind is KsProcgenOwnedSceneEntityKind.Wall or KsProcgenOwnedSceneEntityKind.Window)
                    .ToDictionary(item => new Robust.Shared.Maths.Vector2i((int) Math.Floor((double) item.intent.Position.X),
                        (int) Math.Floor((double) item.intent.Position.Y)), item => item.uid);
                var doors = stage.Scene.Entities.Where(intent => intent.Kind == KsProcgenOwnedSceneEntityKind.Door)
                    .Select(intent => new Robust.Shared.Maths.Vector2i((int) Math.Floor((double) intent.Position.X), (int) Math.Floor((double) intent.Position.Y))).ToHashSet();
                foreach (var room in service.Rooms)
                {
                    if (!Spend()) return "OwnedElectricalWorkBudget";
                    if (!doors.Contains(room.Reservation.Threshold)) return "OwnedElectricalUtilityEntranceDrift";
                    foreach (var cell in room.Reservation.EnclosureCells.Where(cell => cell != room.Reservation.Threshold))
                    {
                        if (!Spend()) return "OwnedElectricalWorkBudget";
                        if (!structures.TryGetValue(cell, out var structureUid) || !TryComp(structureUid, out AirtightComponent? airtight) || !airtight.AirBlocked)
                            return "OwnedElectricalUtilityEnclosureDrift";
                    }
                }
            }
            else if (plan.Profile.Distribution != null) return "OwnedElectricalServiceWitnessMissing";
            if (stage.InitializationPhase == KsProcgenStageInitializationPhase.Uninitialized)
                return expected == null ? null : "OwnedElectricalReceiptPhaseDrift";
            if (stage.InitializationPhase != KsProcgenStageInitializationPhase.Initialized) return "OwnedElectricalInitializationPhaseDrift";
            if (expected == null && !rebuildIfNeeded) return "OwnedElectricalReceiptMissing";
            if (expected != null && expected.PlanHash != plan.PlanHash) return "OwnedElectricalPlanDrift";
            var topology = expected == null
                ? _nodes.KsRebuildOwnedPowerNodes(stage.GridUid, members, maximumWork - used, cancellationToken: cancellationToken)
                : _nodes.KsInspectOwnedPowerNodes(stage.GridUid, members, maximumWork - used, cancellationToken: cancellationToken);
            if (!Spend(topology.Work)) return "OwnedElectricalWorkBudget";
            cancellationToken.ThrowIfCancellationRequested();
            if (topology.IssueCode != null) return topology.IssueCode;

            Node? FindNode(EntityUid uid, KsProcgenElectricalVoltage voltage, bool cable)
            {
                if (!TryComp(uid, out NodeContainerComponent? container)) return null;
                var group = voltage == KsProcgenElectricalVoltage.High ? NodeGroupID.HVPower :
                    voltage == KsProcgenElectricalVoltage.Medium ? NodeGroupID.MVPower : NodeGroupID.Apc;
                var candidates = container.Nodes.Values.Where(node => node.NodeGroupID == group &&
                    (cable ? node.GetType() == typeof(CableNode) : node.GetType() == typeof(CableDeviceNode))).ToArray();
                return candidates.Length == 1 && candidates[0].NodeGroup != null ? candidates[0] : null;
            }
            var mediumRoots = new HashSet<object>(); var lowRoots = new HashSet<object>();
            foreach (var device in plan.Devices)
            {
                if (!Spend()) return "OwnedElectricalWorkBudget";
                var uid = bindings[device.Id];
                foreach (var voltage in new[] { KsProcgenElectricalVoltage.Medium,
                             device.Substation ? KsProcgenElectricalVoltage.High : KsProcgenElectricalVoltage.Low })
                {
                    if (!Spend()) return "OwnedElectricalWorkBudget";
                    if (!cableIds.TryGetValue((device.Cell, voltage), out var cableUid) || FindNode(cableUid, voltage, true) is not { } cable ||
                        FindNode(uid, voltage, false) is not { } converter || !ReferenceEquals(cable.NodeGroup, converter.NodeGroup))
                        return "OwnedElectricalConverterWireMissing";
                    if (device.Substation && voltage == KsProcgenElectricalVoltage.Medium) mediumRoots.Add(converter.NodeGroup!);
                    if (!device.Substation && voltage == KsProcgenElectricalVoltage.Low) lowRoots.Add(converter.NodeGroup!);
                }
            }
            foreach (var cable in plan.Cables)
            {
                if (!Spend()) return "OwnedElectricalWorkBudget";
                var node = FindNode(cableIds[(cable.Cell, cable.Voltage)], cable.Voltage, true);
                if (node == null || cable.Voltage == KsProcgenElectricalVoltage.Medium && !mediumRoots.Contains(node.NodeGroup!) ||
                    cable.Voltage == KsProcgenElectricalVoltage.Low && !lowRoots.Contains(node.NodeGroup!)) return "OwnedElectricalCableNetworkDrift";
            }
            var actual = new List<KsProcgenElectricalConnectionState>();
            foreach (var consumer in plan.Consumers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Spend()) return "OwnedElectricalWorkBudget";
                if (!bindings.TryGetValue(consumer.ConsumerId, out var uid) || !TryComp(uid, out ExtensionCableReceiverComponent? receiver) ||
                    !TryComp(uid, out ApcPowerReceiverComponent? power) || receiver.Provider is not { } provider ||
                    !owned.Contains(provider.Owner) || !TryComp(provider.Owner, out ExtensionCableProviderComponent? extension) ||
                    !ReferenceEquals(extension, provider.Comp) || !TryComp(provider.Owner, out ApcPowerProviderComponent? source) ||
                    !ReferenceEquals(power.Provider, source) || !extension.Connectable || !receiver.Connectable ||
                    !Transform(uid).Anchored || !Transform(provider.Owner).Anchored || Transform(uid).ParentUid != stage.GridUid ||
                    Transform(provider.Owner).ParentUid != stage.GridUid || source.Net is not ApcNet net || !lowRoots.Contains(net) ||
                    !net.Providers.Contains(source) || !net.Apcs.Any() || !float.IsFinite(power.Load) || power.Load < 0f ||
                    !KsProcgenElectricalReach.WithinRange(Transform(provider.Owner).LocalPosition, Transform(uid).LocalPosition,
                        extension.TransferRange, receiver.ReceptionRange)) return "OwnedElectricalReceiverMembershipDrift";
                if (!Spend(extension.LinkedReceivers.Count + source.LinkedReceivers.Count)) return "OwnedElectricalWorkBudget";
                if (extension.LinkedReceivers.Count(item => item.Owner == uid && ReferenceEquals(item.Comp, receiver)) != 1 ||
                    source.LinkedReceivers.Count(item => ReferenceEquals(item, power)) != 1) return "OwnedElectricalReceiverBacklinkDrift";
                actual.Add(new(consumer.ConsumerId, uid, provider.Owner, power.Load, consumer.NominalWatts, consumer.PeakWatts,
                    extension.TransferRange, receiver.ReceptionRange));
            }
            var ownedPowerReceivers = new HashSet<ApcPowerReceiverComponent>();
            foreach (var uid in members)
            {
                if (!Spend()) return "OwnedElectricalWorkBudget";
                if (TryComp(uid, out ApcPowerReceiverComponent? power)) ownedPowerReceivers.Add(power);
                if (!TryComp(uid, out ExtensionCableProviderComponent? provider)) continue;
                foreach (var receiver in provider.LinkedReceivers)
                {
                    if (!Spend()) return "OwnedElectricalWorkBudget";
                    if (!owned.Contains(receiver.Owner) || !TryComp(receiver.Owner, out ExtensionCableReceiverComponent? current) ||
                        !ReferenceEquals(current, receiver.Comp) || current.Provider is not { } owner || owner.Owner != uid ||
                        !ReferenceEquals(owner.Comp, provider)) return "OwnedElectricalForeignReceiver";
                }
            }
            foreach (var uid in members)
            {
                if (!Spend()) return "OwnedElectricalWorkBudget";
                if (!TryComp(uid, out ApcPowerProviderComponent? provider)) continue;
                foreach (var receiver in provider.LinkedReceivers)
                {
                    if (!Spend()) return "OwnedElectricalWorkBudget";
                    if (!ownedPowerReceivers.Contains(receiver) || !ReferenceEquals(receiver.Provider, provider)) return "OwnedElectricalForeignPowerReceiver";
                }
            }
            if (expected != null && (expected.CableCount != cableIds.Count || expected.DeviceCount != plan.Devices.Count ||
                    !expected.Connections.SequenceEqual(actual.OrderBy(item => item.ConsumerId, StringComparer.Ordinal))))
                return "OwnedElectricalReceiptDrift";
            var powerLinks = expected == null
                ? _powerNetworks.KsReconnectOwnedPowerNetworks(stage.GridUid, members, maximumWork - used, cancellationToken: cancellationToken)
                : _powerNetworks.KsInspectOwnedPowerNetworks(stage.GridUid, members, maximumWork - used, cancellationToken: cancellationToken);
            if (!Spend(powerLinks.Work)) return "OwnedElectricalWorkBudget";
            cancellationToken.ThrowIfCancellationRequested();
            if (powerLinks.IssueCode != null) return powerLinks.IssueCode;
            if (expected != null) return null;
            prepared = new(plan.PlanHash, actual, cableIds.Count, plan.Devices.Count, used, _timing.CurTick, _timing.CurTime);
            return null;
        }
        var result = Run(out receipt); work = used; return result;
    }
}
