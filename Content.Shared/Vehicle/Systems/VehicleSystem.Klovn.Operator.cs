using System.Diagnostics.CodeAnalysis;
using Content.Shared.Vehicle.Components;

namespace Content.Shared.Vehicle.Systems;

public sealed partial class VehicleSystem
{
    /// <summary>
    ///     Gets the vehicle an entity is operating, if any.
    /// </summary>
    public bool TryGetOperatedVehicle(EntityUid operatorUid, [NotNullWhen(true)] out Entity<VehicleComponent>? vehicle)
    {
        vehicle = null;
        if (!_operatorQuery.TryComp(operatorUid, out var operatorComponent) ||
            operatorComponent.Vehicle is not { } vehicleUid ||
            !_vehicleQuery.TryComp(vehicleUid, out var vehicleComponent) ||
            vehicleComponent.Operator != operatorUid)
        {
            return false;
        }

        vehicle = (vehicleUid, vehicleComponent);
        return true;
    }

    /// <summary>
    ///     The vehicle an entity is operating, or null.
    /// </summary>
    public EntityUid? GetOperatedVehicleOrNull(EntityUid operatorUid)
    {
        return TryGetOperatedVehicle(operatorUid, out var vehicle) ? vehicle.Value.Owner : null;
    }
}
