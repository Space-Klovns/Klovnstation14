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
    ///     Whether <paramref name="npcUid"/> is forcing a door right now.
    /// </summary>
    public bool IsBreaching(EntityUid npcUid)
    {
        return _breachingQuery.HasComp(npcUid);
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> is forcing a door steering found in its way (<see cref="TryBreachBlockingDoor"/>),
    ///         rather than one it was sent to force. That breach ends with the move (<see cref="StopSteeringBreach"/>),
    ///         so whatever would end the move part way - a new order - should wait for it.
    /// </summary>
    public bool IsBreachingOnTheWay(EntityUid npcUid)
    {
        return _breachingQuery.TryComp(npcUid, out var breachingComponent) && breachingComponent.FromSteering;
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> is forcing <paramref name="doorUid"/> right now.
    /// </summary>
    public bool IsBreaching(EntityUid npcUid, EntityUid doorUid)
    {
        return _breachingQuery.TryComp(npcUid, out var breachingComponent) && breachingComponent.DoorUid == doorUid;
    }

    /// <summary>
    ///     Steering's way through a door in its way that will not open for it: if <paramref name="npcUid"/> does that
    ///         (<see cref="NpcDoorUserComponent.BreachWhenBlocked"/>) and carries something that forces the door, it
    ///         does. The pathfinder only sends it through a door it cannot open when that is the way there - any other
    ///         is longer by more than the door costs, or there is none. Returns whether it started.
    /// </summary>
    /// <remarks>
    ///     Only while getting there is what the NPC is doing (see <see cref="IsMovingInForeground"/>). Movement carried
    ///         on in the background - a retreat while it reloads, closing in while it shoots - comes with plan tasks
    ///         using its hands at the same time, and taking a tool out under them ends with the tool on the floor: a
    ///         reload's drop, or the gun branch finding no gun in hand, throws it away.
    /// </remarks>
    public bool TryBreachBlockingDoor(EntityUid npcUid, EntityUid doorUid)
    {
        return CanForceFromSteering(npcUid, doorUid, out var toolUid) &&
            TryStartBreach(npcUid, doorUid, toolUid, DefaultBreachTimeout, fromSteering: true);
    }

    /// <summary>
    ///     Whether steering may force <paramref name="doorUid"/> for <paramref name="npcUid"/> on its own, and with what:
    ///         it does that at all, the move is its task at hand, and it carries something that forces the door.
    /// </summary>
    private bool CanForceFromSteering(EntityUid npcUid, EntityUid doorUid, out EntityUid toolUid)
    {
        toolUid = default;

        return BreachesWhenBlocked(npcUid) &&
            IsMovingInForeground(npcUid) &&
            TryGetBreachTool(npcUid, doorUid, out toolUid, out _);
    }

    private bool BreachesWhenBlocked(EntityUid npcUid)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent)
            ? doorUserComponent.BreachWhenBlocked
            : NpcDoorUserComponent.DefaultBreachWhenBlocked;
    }

    /// <summary>
    ///     Whether moving is what <paramref name="npcUid"/> is doing right now, rather than something it carries on with
    ///         in the background while later tasks run. See <see cref="NpcBackgroundMoveComponent"/>.
    /// </summary>
    public bool IsMovingInForeground(EntityUid npcUid)
    {
        return !_backgroundMoveQuery.HasComp(npcUid);
    }

    /// <summary>
    ///     Starts <paramref name="npcUid"/> forcing <paramref name="doorUid"/> with <paramref name="toolUid"/>: the
    ///         weapon in hand is unwielded, and the tool taken from wherever it is carried into a free hand and used.
    ///         The breach then runs on its own (see <see cref="Update"/>) and ends when the door opens or the tool fails.
    ///         An access breaker also gets <paramref name="timeout"/>, having no end of its own; prying takes as long as
    ///         the tool takes on that door, and giving up part way wastes it. However it ends, the tool goes back where it came from and
    ///         the weapon comes back out, wielded if it was. Returns whether it started, or was already under way on
    ///         this door.
    /// </summary>
    public bool TryStartBreach(EntityUid npcUid, EntityUid doorUid, EntityUid toolUid, TimeSpan timeout, bool fromSteering)
    {
        if (_breachingQuery.TryComp(npcUid, out var existingComponent))
            return existingComponent.DoorUid == doorUid;

        var method = GetBreachMethod(npcUid, doorUid, toolUid);

        // Checked first rather than found out by trying: steering asks every tick it is stuck, and putting things down
        //      and picking them up again each time would be a racket.
        if (method == NpcBreachMethod.None || !_npcHandsSystem.CanTakeOut(npcUid, toolUid))
            return false;

        var breachingComponent = AddComp<NpcBreachingComponent>(npcUid);
        breachingComponent.DoorUid = doorUid;
        breachingComponent.ToolUid = toolUid;
        breachingComponent.Method = method;
        breachingComponent.FromSteering = fromSteering;
        breachingComponent.GiveUpAt = _gameTiming.CurTime + timeout;

        var tookOut = _npcHandsSystem.TryTakeOut(npcUid, toolUid, out var takenOut);
        breachingComponent.TakenOut = takenOut;

        if (!tookOut || !UseTool((npcUid, breachingComponent)))
        {
            StopBreach(npcUid);
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Ends <paramref name="npcUid"/>'s breach, if it has one, however far it got: the tool goes back, the weapon
    ///         comes back out.
    /// </summary>
    public void StopBreach(EntityUid npcUid)
    {
        if (!_breachingQuery.TryComp(npcUid, out var breachingComponent))
            return;

        if (breachingComponent.TakenOut is { } takenOut)
            _npcHandsSystem.PutBack(npcUid, takenOut);

        RemComp<NpcBreachingComponent>(npcUid);
    }

    /// <summary>
    ///     Steering has stopped for <paramref name="npcUid"/>: a breach it started on the way ends with it, at once. Left
    ///         to <see cref="Update"/>, whatever the NPC does next - a plan that replaced the one it was walking for -
    ///         would start with the tool still in its hand, and could drop it.
    /// </summary>
    public void StopSteeringBreach(EntityUid npcUid)
    {
        if (_breachingQuery.TryComp(npcUid, out var breachingComponent) && breachingComponent.FromSteering)
            StopBreach(npcUid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        // Gathered and stopped after, rather than removed deferred: an order or steering may start another breach on
        //      the same NPC this tick, and a deferred removal would take the new one with it.
        _endedBreaches.Clear();
        var breachingEnumerator = EntityQueryEnumerator<NpcBreachingComponent>();
        while (breachingEnumerator.MoveNext(out var npcUid, out var breachingComponent))
        {
            if (!IsBreachUnderway((npcUid, breachingComponent), now))
                _endedBreaches.Add(npcUid);
        }

        foreach (var npcUid in _endedBreaches)
        {
            StopBreach(npcUid);
        }
    }

    private bool IsBreachUnderway(Entity<NpcBreachingComponent> breaching, TimeSpan now)
    {
        var breachingComponent = breaching.Comp;

        // Open: done. Gone: nothing to do.
        if (!_doorQuery.TryComp(breachingComponent.DoorUid, out var doorComponent) ||
            doorComponent.State is DoorState.Open or DoorState.Opening)
            return false;

        // Steering has moved on: so does it.
        if (breachingComponent.FromSteering && !_steeringQuery.HasComp(breaching))
            return false;

        // Prying is a do-after: once it has ended with the door still shut, it failed. An access breaker sets the door
        //      going straight away.
        return breachingComponent.Method == NpcBreachMethod.Pry
            ? _doAfterSystem.GetStatus(breachingComponent.DoAfterId) == DoAfterStatus.Running
            : doorComponent.State == DoorState.Emagging && now <= breachingComponent.GiveUpAt;
    }

    private bool UseTool(Entity<NpcBreachingComponent> breaching)
    {
        var toolUid = breaching.Comp.ToolUid;
        var doorUid = breaching.Comp.DoorUid;

        if (breaching.Comp.Method == NpcBreachMethod.AccessBreaker)
            return _emagSystem.TryEmagEffect(toolUid, breaching, doorUid);

        SwitchToPrying(breaching, toolUid);
        var started = _pryingSystem.TryPry(doorUid, breaching, out var doAfterId, toolUid);
        breaching.Comp.DoAfterId = doAfterId;
        return started;
    }

    /// <summary>
    ///     A multi-tool - jaws of life, say - is put in its prying mode first.
    /// </summary>
    private void SwitchToPrying(EntityUid npcUid, EntityUid toolUid)
    {
        if (!TryGetPryingEntry(toolUid, out var multipleToolComponent, out var index))
            return;

        if (multipleToolComponent.CurrentEntry != (uint) index)
        {
            multipleToolComponent.CurrentEntry = (uint) index;
            _toolSystem.SetMultipleTool(toolUid, multipleToolComponent, playSound: true, user: npcUid);
        }
    }
}
