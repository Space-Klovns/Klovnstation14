using System.Numerics;
using Robust.Shared.Map;

namespace Content.Shared._KS14.RCD;

/// <summary>
///     Keeps track of where an RCD may not be used, per map.
/// </summary>
public sealed partial class KsNoBuildZoneSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private EntityQuery<KsNoBuildZonesComponent> _zonesQuery = default!;

    /// <summary>
    ///     Replaces the no-build zones on <paramref name="mapUid"/>.
    /// </summary>
    public void SetZones(EntityUid mapUid, IEnumerable<KsNoBuildZone> zones)
    {
        var zonesComponent = EnsureComp<KsNoBuildZonesComponent>(mapUid);
        zonesComponent.Zones.Clear();
        zonesComponent.Zones.AddRange(zones);
        Dirty(mapUid, zonesComponent);
    }

    /// <summary>
    ///     Whether an RCD is forbidden at <paramref name="coordinates"/>.
    /// </summary>
    public bool IsForbidden(MapCoordinates coordinates)
    {
        if (_mapSystem.GetMapOrInvalid(coordinates.MapId) is not { Valid: true } mapUid ||
            !_zonesQuery.TryComp(mapUid, out var zonesComponent))
            return false;

        foreach (var zone in zonesComponent.Zones)
        {
            if (Vector2.DistanceSquared(zone.Center, coordinates.Position) <= zone.Radius * zone.Radius)
                return true;
        }

        return false;
    }
}
