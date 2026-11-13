using Content.Shared.Atmos.Components;

namespace Content.Server.Atmos.Piping.EntitySystems;

public sealed partial class AtmosDeviceSystem
{
    /// <summary>
    ///     KS14: the map atmosphere of the map <paramref name="device"/> is on, for devices updated outside any grid's atmosphere,
    ///         so they read the map's air rather than space.
    /// </summary>
    private Entity<MapAtmosphereComponent?>? KsGetDeviceMap(EntityUid device)
    {
        return Transform(device).MapUid is { } mapUid ? new Entity<MapAtmosphereComponent?>(mapUid, null) : null;
    }
}
