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
    ///     Whether <paramref name="npcUid"/>, or its squad, has found <paramref name="doorUid"/> will not open for it
    ///         lately.
    /// </summary>
    public bool IsNoGo(EntityUid npcUid, EntityUid doorUid)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent) &&
            NpcExpiringMap.IsLive(doorUserComponent.NoGoDoors, doorUid, _gameTiming.CurTime);
    }

    /// <summary>
    ///     <paramref name="npcUid"/> tried <paramref name="doorUid"/>, believing it could open it, and it would not. It
    ///         and everyone in its squad remember the door as a no-go, and it says so. A door it already knew to be
    ///         one is not reported again.
    /// </summary>
    public void ReportRefused(EntityUid npcUid, EntityUid doorUid)
    {
        if (IsNoGo(npcUid, doorUid))
            return;

        Remember(npcUid, doorUid);

        // Remember made sure it has one.
        var doorUserComponent = _doorUserQuery.Comp(npcUid);
        if (doorUserComponent.WarnsSquad)
        {
            var ev = new NpcDoorRefusedCalloutEvent(npcUid, doorUid);
            _npcSquadSystem.CallOut(npcUid, ref ev);
        }

        doorUserComponent.RefusedAt = _gameTiming.CurTime;
        _npcSensorSystem.RequestReplan(npcUid);
    }

    /// <summary>
    ///     A squadmate was refused by a door: this one remembers it as a no-go too. On the squad member, not the door
    ///         user component, which a squadmate that never met a door has not got yet.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDoorRefusedCallout(Entity<NpcSquadMemberComponent> entity, ref NpcDoorRefusedCalloutEvent args)
    {
        Remember(entity.Owner, args.DoorUid);
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> was refused by a door within <paramref name="within"/> and has not said
    ///         so yet.
    /// </summary>
    public bool HasPendingRefusal(EntityUid npcUid, TimeSpan within)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent) &&
            NpcCallouts.IsPending(doorUserComponent.RefusedAt, within, _gameTiming.CurTime);
    }

    /// <summary>
    ///     <paramref name="npcUid"/> has said it was refused.
    /// </summary>
    public void ClearPendingRefusal(EntityUid npcUid)
    {
        if (_doorUserQuery.TryComp(npcUid, out var doorUserComponent))
            doorUserComponent.RefusedAt = null;
    }

    /// <summary>
    ///     <paramref name="npcUid"/> walked up to <paramref name="doorUid"/> and could not get through: not by hand, and
    ///         with nothing to force it. Its paths go round the door for a while (see <see cref="GetBlockedDoors"/>),
    ///         rather than straight back into it.
    /// </summary>
    public void ReportBlocked(EntityUid npcUid, EntityUid doorUid)
    {
        var doorUserComponent = EnsureComp<NpcDoorUserComponent>(npcUid);
        Remember(doorUserComponent.BlockedDoors, doorUid, _gameTiming.CurTime, doorUserComponent.BlockedForgetAfter);
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/>'s paths go round <paramref name="doorUid"/> for now. See
    ///         <see cref="ReportBlocked"/>.
    /// </summary>
    public bool IsBlocked(EntityUid npcUid, EntityUid doorUid)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent) &&
            NpcExpiringMap.IsLive(doorUserComponent.BlockedDoors, doorUid, _gameTiming.CurTime);
    }

    /// <summary>
    ///     Adds the doors <paramref name="npcUid"/>'s paths go round for now to <paramref name="doorUids"/>. See
    ///         <see cref="ReportBlocked"/>.
    /// </summary>
    public void GetBlockedDoors(EntityUid npcUid, List<EntityUid> doorUids)
    {
        if (!_doorUserQuery.TryComp(npcUid, out var doorUserComponent))
            return;

        var now = _gameTiming.CurTime;
        AddRemembered(doorUserComponent.BlockedDoors, now, doorUids);

        // And the ones it is going round rather than forcing.
        AddRemembered(doorUserComponent.DetourDoors, now, doorUids);
    }

    private void AddRemembered(Dictionary<EntityUid, TimeSpan> doors, TimeSpan now, List<EntityUid> doorUids)
    {
        foreach (var (doorUid, expiresAt) in doors)
        {
            // Open now, by whatever means: nothing to go round, and a squad that forced it has to be able to go through.
            if (expiresAt > now &&
                !TerminatingOrDeleted(doorUid) &&
                (!_doorQuery.TryComp(doorUid, out var doorComponent) || doorComponent.State is not (DoorState.Open or DoorState.Opening)))
                doorUids.Add(doorUid);
        }
    }

    private static bool AnyRemembered(Dictionary<EntityUid, TimeSpan> doors, TimeSpan now)
    {
        foreach (var expiresAt in doors.Values)
        {
            if (expiresAt > now)
                return true;
        }

        return false;
    }

    private void Remember(EntityUid npcUid, EntityUid doorUid)
    {
        var doorUserComponent = EnsureComp<NpcDoorUserComponent>(npcUid);
        Remember(doorUserComponent.NoGoDoors, doorUid, _gameTiming.CurTime, doorUserComponent.ForgetAfter);
    }

    /// <summary>
    ///     Remembers <paramref name="doorUid"/> in <paramref name="doors"/> for <paramref name="forgetAfter"/>, dropping
    ///         the ones already forgotten. Pruned here, when another is added, not on a timer: nothing else needs them
    ///         gone.
    /// </summary>
    private static void Remember(Dictionary<EntityUid, TimeSpan> doors, EntityUid doorUid, TimeSpan now, TimeSpan forgetAfter)
    {
        NpcExpiringMap.PruneExpired(doors, now);
        doors[doorUid] = now + forgetAfter;
    }
}
