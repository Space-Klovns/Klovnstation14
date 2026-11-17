using Content.Server._KS14.NPC.Squad;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     Raised on each of <paramref name="CallerUid"/>'s squadmates (<see cref="NpcSquadSystem.CallOut{TEvent}"/>): the grenade
///         it threw at <paramref name="Coordinates"/> has gone off.
/// </summary>
/// <seealso cref="NpcGrenadeThrowerComponent.CallsOutBlasts"/>
[ByRefEvent]
public readonly record struct NpcGrenadeBlastCalloutEvent(EntityUid CallerUid, EntityCoordinates Coordinates);
