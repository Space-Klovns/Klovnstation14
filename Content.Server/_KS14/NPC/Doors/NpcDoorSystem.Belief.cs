using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._KS14.NPC.Hands;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Inventory;
using Content.Shared.NPC;
using Content.Shared.Prying.Components;
using Content.Shared.Prying.Systems;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Doors;

public sealed partial class NpcDoorSystem
{
    /// <summary>
    ///     What <paramref name="npcUid"/> believes about getting through <paramref name="doorUid"/> by hand. See
    ///         <see cref="NpcDoorAccess"/>.
    /// </summary>
    public NpcDoorAccess GetDoorAccess(EntityUid npcUid, EntityUid doorUid)
    {
        if (!_doorQuery.TryComp(doorUid, out var doorComponent) ||
            doorComponent.State is DoorState.Open or DoorState.Opening)
            return NpcDoorAccess.Open;

        if (IsNoGo(npcUid, doorUid) ||
            !CanBeOpenedByHand(doorComponent) ||
            doorComponent.State == DoorState.Welded ||
            IsBolted(doorUid) ||
            IsUnpoweredAirlock(doorUid))
            return NpcDoorAccess.Locked;

        return !RequiresAccess(doorUid) || HasAdvertisedAccess(npcUid, doorUid)
            ? NpcDoorAccess.Openable
            : NpcDoorAccess.Locked;
    }

    private static bool CanBeOpenedByHand(DoorComponent doorComponent)
    {
        return doorComponent.ClickOpen || doorComponent.BumpOpen;
    }

    private bool IsBolted(EntityUid doorUid)
    {
        return _doorBoltQuery.TryComp(doorUid, out var doorBoltComponent) && doorBoltComponent.BoltsDown;
    }

    private bool IsUnpoweredAirlock(EntityUid doorUid)
    {
        return _airlockQuery.TryComp(doorUid, out var airlockComponent) && !airlockComponent.Powered;
    }

    /// <summary>
    ///     The access lists <paramref name="reader"/> was built with, whatever it has been changed to since.
    /// </summary>
    private static List<HashSet<ProtoId<AccessLevelPrototype>>> GetAdvertisedLists(AccessReaderComponent reader)
    {
        return reader.AccessListsOriginal ?? reader.AccessLists;
    }

    /// <summary>
    ///     Whether <paramref name="doorUid"/> asks for access at all, as far as anyone can tell by looking: its reader is
    ///         on and lists access, and it is not on emergency access, which lets everyone through.
    /// </summary>
    public bool RequiresAccess(EntityUid doorUid)
    {
        if (_airlockQuery.TryComp(doorUid, out var airlockComponent) && airlockComponent.EmergencyAccess)
            return false;

        return _accessReaderSystem.GetMainAccessReader(doorUid, out var readerEntity) &&
            readerEntity.Value.Comp.Enabled &&
            GetAdvertisedLists(readerEntity.Value.Comp).Count > 0;
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/>'s access meets what <paramref name="doorUid"/> was built to ask for - one
    ///         of its original access lists, all of it - whatever it has been changed to since.
    /// </summary>
    public bool HasAdvertisedAccess(EntityUid npcUid, EntityUid doorUid)
    {
        if (!_accessReaderSystem.GetMainAccessReader(doorUid, out var readerEntity))
            return true;

        var lists = GetAdvertisedLists(readerEntity.Value.Comp);
        if (lists.Count == 0)
            return true;

        var tags = _accessReaderSystem.FindAccessTags(npcUid);
        foreach (var list in lists)
        {
            var satisfied = true;
            foreach (var tag in list)
            {
                if (tags.Contains(tag))
                    continue;

                satisfied = false;
                break;
            }

            if (satisfied)
                return true;
        }

        return false;
    }
}
