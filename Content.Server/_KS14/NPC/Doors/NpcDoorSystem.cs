using Content.Server._KS14.NPC.Hands;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC.Components;
using Content.Server._KS14.NPC.Components;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Inventory;
using Content.Shared.Prying.Components;
using Content.Shared.Prying.Systems;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Doors;

/// <summary>
///     What NPCs believe about doors, and what they can force them with.
///     <list type="bullet">
///         <item><see cref="GetDoorAccess"/>: whether an NPC thinks it can open a door by hand. It goes by what the door
///             shows - its access list as it was built (what examining it shows), its bolts, a weld, whether it is
///             powered - not by what the door would actually do. So a door whose access was changed after it was built
///             can fool it.</item>
///         <item><see cref="TryGetBreachTool"/>: whether it carries something that forces the door - a prying tool,
///             or an access breaker - checked with the same rules the game uses.</item>
///         <item><see cref="ReportRefused"/>: a door that would not open for an NPC that believed it would. The door
///             becomes a no-go for it and for its squad, for a while, and it says so.</item>
///         <item><see cref="TryStartBreach"/>: forcing a door - tool out, used, and put back with the weapon back in
///             hand however it ends. For a <c>Breach</c> order, and for steering when the way it is going runs through
///             a door that will not open for it (<see cref="TryBreachBlockingDoor"/>).</item>
///     </list>
///     The queries are pure reads, safe from an HTN operator's <c>Plan</c> and from squad tactics.
/// </summary>
public sealed partial class NpcDoorSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private EmagSystem _emagSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private PryingSystem _pryingSystem = default!;
    [Dependency] private SharedChargesSystem _sharedChargesSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;

    [Dependency] private EntityQuery<AirlockComponent> _airlockQuery = default!;
    [Dependency] private EntityQuery<DoorBoltComponent> _doorBoltQuery = default!;
    [Dependency] private EntityQuery<DoorComponent> _doorQuery = default!;
    [Dependency] private EntityQuery<EmagComponent> _emagQuery = default!;
    [Dependency] private EntityQuery<NpcBackgroundMoveComponent> _backgroundMoveQuery = default!;
    [Dependency] private EntityQuery<MultipleToolComponent> _multipleToolQuery = default!;
    [Dependency] private EntityQuery<NpcBreachingComponent> _breachingQuery = default!;
    [Dependency] private EntityQuery<NpcDoorUserComponent> _doorUserQuery = default!;
    [Dependency] private EntityQuery<NPCSteeringComponent> _steeringQuery = default!;
    [Dependency] private EntityQuery<PryingComponent> _pryingQuery = default!;
    [Dependency] private EntityQuery<StorageComponent> _storageQuery = default!;

    private static readonly ProtoId<ToolQualityPrototype> PryingQuality = "Prying";

    /// <summary>
    ///     How long a breach steering starts (<see cref="TryBreachBlockingDoor"/>) is given before it is given up on.
    /// </summary>
    public static readonly TimeSpan DefaultBreachTimeout = TimeSpan.FromSeconds(10);

    private readonly List<EntityUid> _carriedItems = new();
    private readonly List<EntityUid> _endedBreaches = new();

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
            !(doorComponent.ClickOpen || doorComponent.BumpOpen) ||
            doorComponent.State == DoorState.Welded ||
            _doorBoltQuery.TryComp(doorUid, out var doorBoltComponent) && doorBoltComponent.BoltsDown ||
            _airlockQuery.TryComp(doorUid, out var airlockComponent) && !airlockComponent.Powered)
            return NpcDoorAccess.Locked;

        return !RequiresAccess(doorUid) || HasAdvertisedAccess(npcUid, doorUid)
            ? NpcDoorAccess.Openable
            : NpcDoorAccess.Locked;
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
            (readerEntity.Value.Comp.AccessListsOriginal ?? readerEntity.Value.Comp.AccessLists).Count > 0;
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/>'s access meets what <paramref name="doorUid"/> was built to ask for - one
    ///         of its original access lists, all of it - whatever it has been changed to since.
    /// </summary>
    public bool HasAdvertisedAccess(EntityUid npcUid, EntityUid doorUid)
    {
        if (!_accessReaderSystem.GetMainAccessReader(doorUid, out var readerEntity))
            return true;

        var lists = readerEntity.Value.Comp.AccessListsOriginal ?? readerEntity.Value.Comp.AccessLists;
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

        GetCarriedItems(npcUid);

        foreach (var itemUid in _carriedItems)
        {
            if (!CanPryWith(npcUid, doorUid, itemUid))
                continue;

            toolUid = itemUid;
            method = NpcBreachMethod.Pry;
            return true;
        }

        foreach (var itemUid in _carriedItems)
        {
            if (!CanAccessBreakWith(doorUid, itemUid))
                continue;

            toolUid = itemUid;
            method = NpcBreachMethod.AccessBreaker;
            return true;
        }

        return false;
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

        if (!_pryingQuery.TryComp(itemUid, out var pryingComponent))
            return false;

        if (!pryingComponent.Enabled && !HasPryingMode(itemUid))
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

    private bool HasPryingMode(EntityUid itemUid)
    {
        if (!_multipleToolQuery.TryComp(itemUid, out var multipleToolComponent))
            return false;

        foreach (var entry in multipleToolComponent.Entries)
        {
            if (entry.Behavior.Contains(PryingQuality))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Fills <see cref="_carriedItems"/> with what <paramref name="npcUid"/> holds or wears, and what is in the
    ///         things it wears - a belt's contents, a backpack's.
    /// </summary>
    private void GetCarriedItems(EntityUid npcUid)
    {
        _carriedItems.Clear();

        foreach (var itemUid in _inventorySystem.GetHandOrInventoryEntities(npcUid))
        {
            _carriedItems.Add(itemUid);

            if (!_storageQuery.TryComp(itemUid, out var storageComponent))
                continue;

            foreach (var storedUid in storageComponent.Container.ContainedEntities)
            {
                _carriedItems.Add(storedUid);
            }
        }
    }

    #region No-go doors

    /// <summary>
    ///     Whether <paramref name="npcUid"/>, or its squad, has found <paramref name="doorUid"/> will not open for it
    ///         lately.
    /// </summary>
    public bool IsNoGo(EntityUid npcUid, EntityUid doorUid)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent) &&
            doorUserComponent.NoGoDoors.TryGetValue(doorUid, out var expiresAt) &&
            expiresAt > _gameTiming.CurTime;
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

        if (_npcSquadSystem.TryGetSquad(npcUid, out var squadEntity))
        {
            foreach (var memberUid in squadEntity.Value.Comp.Members)
            {
                if (memberUid != npcUid)
                    Remember(memberUid, doorUid);
            }
        }

        EnsureComp<NpcDoorUserComponent>(npcUid).RefusedAt = _gameTiming.CurTime;
        _npcSensorSystem.RequestReplan(npcUid);
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> was refused by a door within <paramref name="within"/> and has not said
    ///         so yet.
    /// </summary>
    public bool HasPendingRefusal(EntityUid npcUid, TimeSpan within)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent) &&
            doorUserComponent.RefusedAt is { } refusedAt &&
            _gameTiming.CurTime - refusedAt <= within;
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
        var now = _gameTiming.CurTime;
        Prune(doorUserComponent.BlockedDoors, now);
        doorUserComponent.BlockedDoors[doorUid] = now + doorUserComponent.BlockedForgetAfter;
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/>'s paths go round <paramref name="doorUid"/> for now. See
    ///         <see cref="ReportBlocked"/>.
    /// </summary>
    public bool IsBlocked(EntityUid npcUid, EntityUid doorUid)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent) &&
            doorUserComponent.BlockedDoors.TryGetValue(doorUid, out var expiresAt) &&
            expiresAt > _gameTiming.CurTime;
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
        foreach (var (doorUid, expiresAt) in doorUserComponent.BlockedDoors)
        {
            if (expiresAt > now && !TerminatingOrDeleted(doorUid))
                doorUids.Add(doorUid);
        }

        // And the ones it is going round rather than forcing.
        foreach (var (doorUid, expiresAt) in doorUserComponent.DetourDoors)
        {
            if (expiresAt > now && !TerminatingOrDeleted(doorUid))
                doorUids.Add(doorUid);
        }
    }

    /// <summary>
    ///     Steering's first answer to a door in its way that is shut to <paramref name="npcUid"/> and that it could
    ///         force: go round it instead, if there is a way round. Forcing a door is loud and spends an access breaker's
    ///         charges, and the pathfinder sends an NPC through any door needing access whenever that saves enough
    ///         walking - it does not know who has the access - so without this, every shortcut through maintenance got
    ///         forced. Returns whether it is now going round: its paths avoid the door (see
    ///         <see cref="GetBlockedDoors"/>), and steering drops its path for one that does. If there is none, steering
    ///         says so with <see cref="GiveUpDetours"/>, the door becomes the only way, and the next time it is in the
    ///         way it is forced.
    /// </summary>
    public bool TryDetourAroundDoor(EntityUid npcUid, EntityUid doorUid)
    {
        if (!CanForceFromSteering(npcUid, doorUid, out _))
            return false;

        var doorUserComponent = EnsureComp<NpcDoorUserComponent>(npcUid);
        var now = _gameTiming.CurTime;

        if (IsRemembered(doorUserComponent.ForceableDoors, doorUid, now) ||
            IsRemembered(doorUserComponent.DetourDoors, doorUid, now))
            return false;

        Prune(doorUserComponent.DetourDoors, now);
        doorUserComponent.DetourDoors[doorUid] = now + doorUserComponent.DetourForgetAfter;
        return true;
    }

    /// <summary>
    ///     Steering found no path for <paramref name="npcUid"/>. If it was going round doors it could force, they are
    ///         the only way: it stops going round them and forces them instead. Returns whether it was, in which case
    ///         steering asks again, through them.
    /// </summary>
    public bool GiveUpDetours(EntityUid npcUid)
    {
        if (!_doorUserQuery.TryComp(npcUid, out var doorUserComponent) || doorUserComponent.DetourDoors.Count == 0)
            return false;

        var now = _gameTiming.CurTime;
        var gaveUp = false;
        Prune(doorUserComponent.ForceableDoors, now);

        foreach (var (doorUid, expiresAt) in doorUserComponent.DetourDoors)
        {
            if (expiresAt <= now)
                continue;

            doorUserComponent.ForceableDoors[doorUid] = now + doorUserComponent.DetourForgetAfter;
            gaveUp = true;
        }

        doorUserComponent.DetourDoors.Clear();
        return gaveUp;
    }

    private static bool IsRemembered(Dictionary<EntityUid, TimeSpan> doors, EntityUid doorUid, TimeSpan now)
    {
        return doors.TryGetValue(doorUid, out var expiresAt) && expiresAt > now;
    }

    private void Remember(EntityUid npcUid, EntityUid doorUid)
    {
        var doorUserComponent = EnsureComp<NpcDoorUserComponent>(npcUid);
        var now = _gameTiming.CurTime;
        Prune(doorUserComponent.NoGoDoors, now);
        doorUserComponent.NoGoDoors[doorUid] = now + doorUserComponent.ForgetAfter;
    }

    /// <summary>
    ///     Drops the doors no longer remembered. Here, when another is added, not on a timer: nothing else needs them
    ///         gone. Removing during enumeration is allowed for a Dictionary.
    /// </summary>
    private static void Prune(Dictionary<EntityUid, TimeSpan> doors, TimeSpan now)
    {
        foreach (var (doorUid, expiresAt) in doors)
        {
            if (expiresAt <= now)
                doors.Remove(doorUid);
        }
    }

    #endregion

    #region Forcing doors

    /// <summary>
    ///     Whether <paramref name="npcUid"/> is forcing a door right now.
    /// </summary>
    public bool IsBreaching(EntityUid npcUid)
    {
        return _breachingQuery.HasComp(npcUid);
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
        var breachWhenBlocked = _doorUserQuery.TryComp(npcUid, out var doorUserComponent)
            ? doorUserComponent.BreachWhenBlocked
            : NpcDoorUserComponent.DefaultBreachWhenBlocked;

        return breachWhenBlocked &&
            IsMovingInForeground(npcUid) &&
            TryGetBreachTool(npcUid, doorUid, out toolUid, out _);
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
    ///         The breach then runs on its own (see <see cref="Update"/>) and ends when the door opens, when the tool
    ///         fails, or after <paramref name="timeout"/>. However it ends, the tool goes back where it came from and
    ///         the weapon comes back out, wielded if it was. Returns whether it started, or was already under way on
    ///         this door.
    /// </summary>
    public bool TryStartBreach(EntityUid npcUid, EntityUid doorUid, EntityUid toolUid, TimeSpan timeout, bool fromSteering)
    {
        if (_breachingQuery.TryComp(npcUid, out var existingComponent))
            return existingComponent.DoorUid == doorUid;

        var method = CanPryWith(npcUid, doorUid, toolUid) ? NpcBreachMethod.Pry
            : CanAccessBreakWith(doorUid, toolUid) ? NpcBreachMethod.AccessBreaker
            : NpcBreachMethod.None;

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

        if (!tookOut || !UseTool(npcUid, breachingComponent))
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
            if (!IsBreachUnderway(npcUid, breachingComponent, now))
                _endedBreaches.Add(npcUid);
        }

        foreach (var npcUid in _endedBreaches)
        {
            StopBreach(npcUid);
        }
    }

    private bool IsBreachUnderway(EntityUid npcUid, NpcBreachingComponent breachingComponent, TimeSpan now)
    {
        // Open: done. Gone: nothing to do.
        if (!_doorQuery.TryComp(breachingComponent.DoorUid, out var doorComponent) ||
            doorComponent.State is DoorState.Open or DoorState.Opening ||
            now > breachingComponent.GiveUpAt)
            return false;

        // Steering has moved on: so does it.
        if (breachingComponent.FromSteering && !_steeringQuery.HasComp(npcUid))
            return false;

        // Prying is a do-after: once it has ended with the door still shut, it failed. An access breaker sets the door
        //      going straight away.
        return breachingComponent.Method == NpcBreachMethod.Pry
            ? _doAfterSystem.GetStatus(breachingComponent.DoAfterId) == DoAfterStatus.Running
            : doorComponent.State == DoorState.Emagging;
    }

    private bool UseTool(EntityUid npcUid, NpcBreachingComponent breachingComponent)
    {
        var toolUid = breachingComponent.ToolUid;
        var doorUid = breachingComponent.DoorUid;

        if (breachingComponent.Method == NpcBreachMethod.AccessBreaker)
            return _emagSystem.TryEmagEffect(toolUid, npcUid, doorUid);

        SwitchToPrying(npcUid, toolUid);
        var started = _pryingSystem.TryPry(doorUid, npcUid, out var doAfterId, toolUid);
        breachingComponent.DoAfterId = doAfterId;
        return started;
    }

    /// <summary>
    ///     A multi-tool - jaws of life, say - is put in its prying mode first.
    /// </summary>
    private void SwitchToPrying(EntityUid npcUid, EntityUid toolUid)
    {
        if (!_multipleToolQuery.TryComp(toolUid, out var multipleToolComponent))
            return;

        for (var i = 0; i < multipleToolComponent.Entries.Length; i++)
        {
            if (!multipleToolComponent.Entries[i].Behavior.Contains(PryingQuality))
                continue;

            if (multipleToolComponent.CurrentEntry != (uint) i)
            {
                multipleToolComponent.CurrentEntry = (uint) i;
                _toolSystem.SetMultipleTool(toolUid, multipleToolComponent, playSound: true, user: npcUid);
            }

            return;
        }
    }

    #endregion
}
