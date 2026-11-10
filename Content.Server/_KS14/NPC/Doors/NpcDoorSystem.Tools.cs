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
    ///     Something <paramref name="npcUid"/> carries - in hand, worn, or in something worn - that would force
    ///         <paramref name="doorUid"/> open right now. Prying tools first, as they never run out; then access
    ///         breakers, with charges left and off cooldown.
    /// </summary>
    public bool TryGetBreachTool(EntityUid npcUid, EntityUid doorUid, out EntityUid toolUid, out NpcBreachMethod method)
    {
        toolUid = default;
        method = NpcBreachMethod.None;

        if (!_doorQuery.TryComp(doorUid, out var doorComponent) || doorComponent.State != DoorState.Closed)
            return false;

        _npcHandsSystem.GetCarried(npcUid, _carriedItems);

        // All the pryers are asked before any breaker is.
        if (TryFindCarried(npcUid, doorUid, NpcBreachMethod.Pry, out toolUid))
        {
            method = NpcBreachMethod.Pry;
            return true;
        }

        if (TryFindCarried(npcUid, doorUid, NpcBreachMethod.AccessBreaker, out toolUid))
        {
            method = NpcBreachMethod.AccessBreaker;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The first of the items in <see cref="_carriedItems"/> that would force <paramref name="doorUid"/> by
    ///         <paramref name="method"/>.
    /// </summary>
    private bool TryFindCarried(EntityUid npcUid, EntityUid doorUid, NpcBreachMethod method, out EntityUid toolUid)
    {
        foreach (var itemUid in _carriedItems)
        {
            if (!CanBreachWith(npcUid, doorUid, itemUid, method))
                continue;

            toolUid = itemUid;
            return true;
        }

        toolUid = default;
        return false;
    }

    private bool CanBreachWith(EntityUid npcUid, EntityUid doorUid, EntityUid itemUid, NpcBreachMethod method)
    {
        return method switch
        {
            NpcBreachMethod.Pry => CanPryWith(npcUid, doorUid, itemUid),
            NpcBreachMethod.AccessBreaker => CanAccessBreakWith(doorUid, itemUid),
            _ => false,
        };
    }

    /// <summary>
    ///     How <paramref name="toolUid"/> would force <paramref name="doorUid"/> for <paramref name="npcUid"/>: prying
    ///         if it can, else as an access breaker, else not at all.
    /// </summary>
    private NpcBreachMethod GetBreachMethod(EntityUid npcUid, EntityUid doorUid, EntityUid toolUid)
    {
        if (CanBreachWith(npcUid, doorUid, toolUid, NpcBreachMethod.Pry))
            return NpcBreachMethod.Pry;

        return CanBreachWith(npcUid, doorUid, toolUid, NpcBreachMethod.AccessBreaker)
            ? NpcBreachMethod.AccessBreaker
            : NpcBreachMethod.None;
    }

    /// <summary>
    ///     Whether <paramref name="itemUid"/> would pry <paramref name="doorUid"/> open for <paramref name="npcUid"/>,
    ///         asked the way the prying system asks: a door cancels the attempt if it is bolted (unless the tool forces),
    ///         welded, or a powered airlock and the tool cannot pry powered ones. A multi-tool counts if prying is one
    ///         of its modes; it is switched to it when used.
    /// </summary>
    public bool CanPryWith(EntityUid npcUid, EntityUid doorUid, EntityUid itemUid)
    {
        // Prying an open door shuts it.
        if (!_doorQuery.TryComp(doorUid, out var doorComponent) || doorComponent.State != DoorState.Closed)
            return false;

        if (!IsPryingTool(itemUid, out var pryingComponent))
            return false;

        var beforePryEvent = new BeforePryEvent(npcUid, pryingComponent.PryPowered, pryingComponent.Force, StrongPry: true);
        RaiseLocalEvent(doorUid, ref beforePryEvent);
        return !beforePryEvent.Cancelled;
    }

    /// <summary>
    ///     Whether <paramref name="itemUid"/> is an access breaker that would open <paramref name="doorUid"/> right now:
    ///         charges left, off cooldown, and the door a closed, powered, unbolted airlock it is not barred from.
    /// </summary>
    public bool CanAccessBreakWith(EntityUid doorUid, EntityUid itemUid)
    {
        if (!_emagQuery.TryComp(itemUid, out var emagComponent) ||
            (emagComponent.EmagType & EmagType.Access) == 0 ||
            _tagSystem.HasTag(doorUid, emagComponent.EmagImmuneTag) ||
            _sharedChargesSystem.IsEmpty((itemUid, (LimitedChargesComponent?) null)) ||
            _useDelaySystem.IsDelayed((itemUid, (UseDelayComponent?) null)))
            return false;

        return _airlockQuery.TryComp(doorUid, out var airlockComponent) &&
            airlockComponent.Powered &&
            _doorQuery.TryComp(doorUid, out var doorComponent) &&
            doorComponent.State == DoorState.Closed &&
            !(_doorBoltQuery.TryComp(doorUid, out var doorBoltComponent) && doorBoltComponent.BoltsDown);
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> carries something that pries and forces doors in its way with it, so a door only
    ///         prying gets through is a way through for its paths. Which door is left to <see cref="TryGetBreachTool"/>.
    /// </summary>
    public bool PriesDoorsInItsWay(EntityUid npcUid)
    {
        if (!BreachesWhenBlocked(npcUid))
            return false;

        _npcHandsSystem.GetCarried(npcUid, _carriedItems);

        foreach (var itemUid in _carriedItems)
        {
            if (IsPryingTool(itemUid, out _))
                return true;
        }

        return false;
    }

    private bool IsPryingTool(EntityUid itemUid, [NotNullWhen(true)] out PryingComponent? pryingComponent)
    {
        return _pryingQuery.TryComp(itemUid, out pryingComponent) &&
            (pryingComponent.Enabled || HasPryingMode(itemUid));
    }

    private bool HasPryingMode(EntityUid itemUid)
    {
        return TryGetPryingEntry(itemUid, out _, out _);
    }

    /// <summary>
    ///     The first mode of the multi-tool <paramref name="itemUid"/> that is prying, and its index in the tool's entries.
    /// </summary>
    private bool TryGetPryingEntry(EntityUid itemUid, [NotNullWhen(true)] out MultipleToolComponent? multipleToolComponent, out int index)
    {
        index = -1;

        if (!_multipleToolQuery.TryComp(itemUid, out multipleToolComponent))
            return false;

        for (var i = 0; i < multipleToolComponent.Entries.Length; i++)
        {
            if (!multipleToolComponent.Entries[i].Behavior.Contains(PryingQuality))
                continue;

            index = i;
            return true;
        }

        return false;
    }
}
