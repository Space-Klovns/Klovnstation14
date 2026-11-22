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
    ///     Forbids every RCD on <paramref name="mapUid"/>, telling whoever tries <paramref name="message"/>, or with
    ///         null allows them again. The zones stay as they are.
    /// </summary>
    public void SetBlocked(EntityUid mapUid, LocId? message)
    {
        // Nothing to allow again on a map that never had anything forbidden.
        if (message == null && !_zonesQuery.HasComp(mapUid))
            return;

        var zonesComponent = EnsureComp<KsNoBuildZonesComponent>(mapUid);
        if (zonesComponent.BlockedMessage == message)
            return;

        zonesComponent.BlockedMessage = message;
        Dirty(mapUid, zonesComponent);
    }

    /// <summary>
    ///     Whether every RCD on the map is forbidden (see <see cref="SetBlocked"/>), and what to tell whoever tries.
    /// </summary>
    public bool IsBlocked(MapId mapId, out LocId message)
    {
        message = default;
        if (_mapSystem.GetMapOrInvalid(mapId) is not { Valid: true } mapUid ||
            !_zonesQuery.TryComp(mapUid, out var zonesComponent) ||
            zonesComponent.BlockedMessage is not { } blockedMessage)
            return false;

        message = blockedMessage;
        return true;
    }

    /// <summary>
    ///     Whether an RCD is forbidden at <paramref name="coordinates"/> for being in a zone.
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
