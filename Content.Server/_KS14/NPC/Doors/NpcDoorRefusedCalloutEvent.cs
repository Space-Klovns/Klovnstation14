using Content.Server._KS14.NPC.Squad;

namespace Content.Server._KS14.NPC.Doors;

/// <summary>
///     Raised on each of <paramref name="CallerUid"/>'s squadmates (<see cref="NpcSquadSystem.CallOut{TEvent}"/>):
///         <paramref name="DoorUid"/> would not open for it.
/// </summary>
/// <seealso cref="NpcDoorUserComponent.WarnsSquad"/>
[ByRefEvent]
public readonly record struct NpcDoorRefusedCalloutEvent(EntityUid CallerUid, EntityUid DoorUid);
