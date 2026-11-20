using Robust.Shared.Map;

namespace Content.Shared._KS14.MedicalBeam;

/// <summary>
///     Raised on a medical beam gun, on the server, when its beam is found crossing <see cref="OtherGunUid"/>'s. Both
///         beams have stopped already; the server sets off the gun's crossing explosion at <see cref="Crossing"/>.
/// </summary>
/// <param name="UserUid">Who held this gun's beam.</param>
/// <param name="OtherUserUid">Who held the other.</param>
[ByRefEvent]
public readonly record struct KsMedicalBeamsCrossedEvent(EntityUid OtherGunUid, EntityUid UserUid, EntityUid OtherUserUid, MapCoordinates Crossing);
