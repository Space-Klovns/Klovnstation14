using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Components;

namespace Content.Server.Atmos.Piping.EntitySystems;

/// <summary>
///     KS14: devices on a grid with no <see cref="GridAtmosphereComponent"/> (a map whose grids take its immutable
///         <see cref="MapAtmosphereComponent"/> air) never enter the grid loop, so they join <see cref="_joinedDevices"/>
///         like <see cref="AtmosDeviceComponent.JoinSystem"/> devices, and are updated in <see cref="Update"/> once per
///         full atmos cycle, against their map's air.
/// </summary>
public sealed partial class AtmosDeviceSystem
{
    /// <summary>
    ///     The update event for an off-grid device: the grid loop's dt, and the map the device is on, so it reads the map's
    ///         air rather than space.
    /// </summary>
    private AtmosDeviceUpdateEvent KsGetDeviceUpdateEvent(EntityUid device)
    {
        Entity<MapAtmosphereComponent?>? map = Transform(device).MapUid is { } mapUid ? (mapUid, null) : null;
        return new AtmosDeviceUpdateEvent(_atmosphereSystem.RealAtmosTime(), null, map);
    }
}
