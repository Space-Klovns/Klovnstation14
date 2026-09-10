using System.Diagnostics.CodeAnalysis;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.PipeNodeTeleporter;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.PipeNodeTeleporter;

/// <summary>
///     Links the node of a recipient entity to the nodes of every beacon in its device list, letting
///         gas or reagents flow between two piping networks that are not physically connected.
/// </summary>
public sealed partial class PipeNodeTeleporterSystem : EntitySystem
{
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PipeNodeTeleporterRecipientComponent, MapInitEvent>(OnRecipientMapInit);
        SubscribeLocalEvent<PipeNodeTeleporterRecipientComponent, DeviceListUpdateEvent>(OnRecipientDeviceListUpdate);

        SubscribeLocalEvent<PipeNodeTeleporterRecipientComponent, ComponentShutdown>(OnRecipientShutdown);
        SubscribeLocalEvent<PipeNodeTeleporterBeaconComponent, ComponentShutdown>(OnBeaconShutdown);
    }

    /// <summary>
    ///     Device lists survive mapping and saving, but <see cref="DeviceListUpdateEvent"/> is only raised on change,
    ///         so pre-linked teleporters have to establish their links themselves.
    /// </summary>
    private void OnRecipientMapInit(Entity<PipeNodeTeleporterRecipientComponent> entity, ref MapInitEvent args)
    {
        if (!TryComp<DeviceListComponent>(entity.Owner, out var deviceListComponent))
            return;

        UpdateLinks(entity, deviceListComponent.Devices);
    }

    private void OnRecipientDeviceListUpdate(Entity<PipeNodeTeleporterRecipientComponent> entity, ref DeviceListUpdateEvent args)
        => UpdateLinks(entity, args.Devices);

    /// <summary>
    ///     Links <paramref name="entity"/> to every beacon in <paramref name="devices"/>, and unlinks it from every
    ///         beacon it is linked to that is not in there.
    /// </summary>
    private void UpdateLinks(Entity<PipeNodeTeleporterRecipientComponent> entity, IEnumerable<EntityUid> devices)
    {
        if (!TryGetTeleporterNode(entity.Owner, entity.Comp.NodeName, out var recipientNode))
            return;

        var newBeaconUids = new HashSet<EntityUid>(devices);

        foreach (var oldUid in new List<EntityUid>(entity.Comp.LinkedBeaconUids))
        {
            if (newBeaconUids.Contains(oldUid))
                continue;

            Unlink(entity, recipientNode, oldUid);
        }

        foreach (var newUid in newBeaconUids)
        {
            if (entity.Comp.LinkedBeaconUids.Contains(newUid))
                continue;

            if (!TryComp<PipeNodeTeleporterBeaconComponent>(newUid, out var beaconComponent) ||
                !TryGetTeleporterNode(newUid, beaconComponent.NodeName, out var beaconNode))
                continue;

            if (!TrySetAlwaysReachable(recipientNode, beaconNode, reachable: true))
                continue;

            entity.Comp.LinkedBeaconUids.Add(newUid);
            beaconComponent.LinkedRecipientUids.Add(entity.Owner);

            UpdateConnectedVisuals(newUid, beaconComponent.LinkedRecipientUids.Count);
        }

        UpdateConnectedVisuals(entity.Owner, entity.Comp.LinkedBeaconUids.Count);
    }

    private void OnRecipientShutdown(Entity<PipeNodeTeleporterRecipientComponent> entity, ref ComponentShutdown args)
    {
        if (!TryGetTeleporterNode(entity.Owner, entity.Comp.NodeName, out var recipientNode))
            return;

        foreach (var beaconUid in new List<EntityUid>(entity.Comp.LinkedBeaconUids))
        {
            Unlink(entity, recipientNode, beaconUid);
        }
    }

    private void OnBeaconShutdown(Entity<PipeNodeTeleporterBeaconComponent> entity, ref ComponentShutdown args)
    {
        if (!TryGetTeleporterNode(entity.Owner, entity.Comp.NodeName, out var beaconNode))
            return;

        foreach (var recipientUid in new List<EntityUid>(entity.Comp.LinkedRecipientUids))
        {
            entity.Comp.LinkedRecipientUids.Remove(recipientUid);

            if (!TryComp<PipeNodeTeleporterRecipientComponent>(recipientUid, out var recipientComponent) ||
                !TryGetTeleporterNode(recipientUid, recipientComponent.NodeName, out var recipientNode))
                continue;

            TrySetAlwaysReachable(recipientNode, beaconNode, reachable: false);

            recipientComponent.LinkedBeaconUids.Remove(entity.Owner);

            UpdateConnectedVisuals(recipientUid, recipientComponent.LinkedBeaconUids.Count);
        }
    }

    /// <summary>
    ///     Severs the link between a recipient and one beacon, if there is one.
    /// </summary>
    private void Unlink(Entity<PipeNodeTeleporterRecipientComponent> entity, Node recipientNode, EntityUid beaconUid)
    {
        entity.Comp.LinkedBeaconUids.Remove(beaconUid);

        if (!TryComp<PipeNodeTeleporterBeaconComponent>(beaconUid, out var beaconComponent) ||
            !TryGetTeleporterNode(beaconUid, beaconComponent.NodeName, out var beaconNode))
            return;

        TrySetAlwaysReachable(recipientNode, beaconNode, reachable: false);

        beaconComponent.LinkedRecipientUids.Remove(entity.Owner);

        UpdateConnectedVisuals(beaconUid, beaconComponent.LinkedRecipientUids.Count);
    }

    private void UpdateConnectedVisuals(EntityUid uid, int linkCount)
    {
        if (TerminatingOrDeleted(uid))
            return;

        _appearanceSystem.SetData(uid, PipeNodeTeleporterVisuals.Connected, linkCount != 0);
    }

    /// <summary>
    ///     Gets a node by name without caring what kind of piping it belongs to - teleporters work on both
    ///         atmospherics pipes and plumbing ducts.
    /// </summary>
    private bool TryGetTeleporterNode(EntityUid uid, string nodeName, [NotNullWhen(true)] out Node? node)
        => _nodeContainerSystem.TryGetNode(uid, nodeName, out node);

    /// <summary>
    ///     Adds or removes an always-reachable link between two nodes of the same kind, in both directions.
    /// </summary>
    /// <returns>False if the two nodes cannot be linked to each other at all.</returns>
    private static bool TrySetAlwaysReachable(Node recipientNode, Node beaconNode, bool reachable)
    {
        switch (recipientNode, beaconNode)
        {
            case (PipeNode recipientPipeNode, PipeNode beaconPipeNode):
                if (reachable)
                {
                    recipientPipeNode.AddAlwaysReachable(beaconPipeNode);
                    beaconPipeNode.AddAlwaysReachable(recipientPipeNode);
                }
                else
                {
                    recipientPipeNode.RemoveAlwaysReachable(beaconPipeNode);
                    beaconPipeNode.RemoveAlwaysReachable(recipientPipeNode);
                }

                return true;

            case (PlumbingNode recipientPlumbingNode, PlumbingNode beaconPlumbingNode):
                if (reachable)
                {
                    recipientPlumbingNode.AddAlwaysReachable(beaconPlumbingNode);
                    beaconPlumbingNode.AddAlwaysReachable(recipientPlumbingNode);
                }
                else
                {
                    recipientPlumbingNode.RemoveAlwaysReachable(beaconPlumbingNode);
                    beaconPlumbingNode.RemoveAlwaysReachable(recipientPlumbingNode);
                }

                return true;

            default:
                return false;
        }
    }
}
