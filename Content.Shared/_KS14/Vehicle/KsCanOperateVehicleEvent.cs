namespace Content.Shared._KS14.Vehicle;

/// <summary>
///     Raised on an entity to ask whether it may operate a vehicle, from
///         <see cref="Content.Shared.Vehicle.Systems.VehicleSystem.CanOperate"/>. Raised whenever the operator's
///         movement is re-evaluated, so handlers must be pure.
/// </summary>
/// <param name="Vehicle">The vehicle to be operated.</param>
[ByRefEvent]
public record struct KsCanOperateVehicleEvent(EntityUid Vehicle, bool Cancelled = false);
