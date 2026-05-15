using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.PipeNodeTeleporter;
using Content.Shared.DeviceNetwork.Systems;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.PipeNodeTeleporter;

public sealed partial class PipeNodeTeleporterSystem : EntitySystem
{
    [Dependency] private readonly NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PipeNodeTeleporterRecipientComponent, DeviceListUpdateEvent>(OnRecipientDeviceListUpdate);

        SubscribeLocalEvent<PipeNodeTeleporterRecipientComponent, ComponentShutdown>(OnRecipientShutdown);
        SubscribeLocalEvent<PipeNodeTeleporterBeaconComponent, ComponentShutdown>(OnBeaconShutdown);
    }

    private void OnRecipientDeviceListUpdate(Entity<PipeNodeTeleporterRecipientComponent> entity, ref DeviceListUpdateEvent args)
    {
        if (!_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.NodeName, out PipeNode? recipientNode))
            return;

        foreach (var oldUid in args.OldDevices)
        {
            if (!TryComp<PipeNodeTeleporterBeaconComponent>(oldUid, out var beaconComponent) ||
                !_nodeContainerSystem.TryGetNode(oldUid, beaconComponent.NodeName, out PipeNode? beaconNode))
                continue;

            if (!entity.Comp.LinkedBeaconUids.Remove(oldUid) ||
                !beaconComponent.LinkedRecipientUids.Remove(entity))
                continue;

            recipientNode.RemoveAlwaysReachable(beaconNode);
            _appearanceSystem.SetData(oldUid, PipeNodeTeleporterVisuals.Connected, beaconComponent.LinkedRecipientUids.Count != 0);
        }

        foreach (var newUid in args.Devices)
        {
            if (!TryComp<PipeNodeTeleporterBeaconComponent>(newUid, out var beaconComponent) ||
                !_nodeContainerSystem.TryGetNode(newUid, beaconComponent.NodeName, out PipeNode? beaconNode))
                continue;

            if (!entity.Comp.LinkedBeaconUids.Add(newUid) ||
                !beaconComponent.LinkedRecipientUids.Add(entity))
                continue;

            recipientNode.AddAlwaysReachable(beaconNode);
            _appearanceSystem.SetData(newUid, PipeNodeTeleporterVisuals.Connected, beaconComponent.LinkedRecipientUids.Count != 0);
        }

        _appearanceSystem.SetData(entity.Owner, PipeNodeTeleporterVisuals.Connected, entity.Comp.LinkedBeaconUids.Count != 0);
    }

    private void OnRecipientShutdown(Entity<PipeNodeTeleporterRecipientComponent> entity, ref ComponentShutdown args)
    {
        if (!_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.NodeName, out PipeNode? recipientNode))
            return;

        foreach (var uid in entity.Comp.LinkedBeaconUids)
        {
            if (!TryComp<PipeNodeTeleporterBeaconComponent>(uid, out var beaconComponent) ||
                !_nodeContainerSystem.TryGetNode(uid, beaconComponent.NodeName, out PipeNode? beaconNode))
                continue;

            if (!beaconComponent.LinkedRecipientUids.Remove(entity))
                continue;

            recipientNode.RemoveAlwaysReachable(beaconNode);
            _appearanceSystem.SetData(uid, PipeNodeTeleporterVisuals.Connected, beaconComponent.LinkedRecipientUids.Count != 0);
        }
    }

    private void OnBeaconShutdown(Entity<PipeNodeTeleporterBeaconComponent> entity, ref ComponentShutdown args)
    {
        if (!_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.NodeName, out PipeNode? beaconNode))
            return;

        foreach (var uid in entity.Comp.LinkedRecipientUids)
        {
            if (!TryComp<PipeNodeTeleporterRecipientComponent>(uid, out var recipientComponent) ||
                !_nodeContainerSystem.TryGetNode(uid, recipientComponent.NodeName, out PipeNode? recipientNode))
                continue;

            if (!recipientComponent.LinkedBeaconUids.Remove(entity))
                continue;

            recipientNode.RemoveAlwaysReachable(beaconNode);
            _appearanceSystem.SetData(uid, PipeNodeTeleporterVisuals.Connected, recipientComponent.LinkedBeaconUids.Count != 0);
        }
    }
}
