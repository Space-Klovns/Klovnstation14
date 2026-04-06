using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Sparks;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._KS14.Sparks;

public sealed class SparksSystem : SharedSparksSystem
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;

    public override void ExposeSpark(EntityCoordinates coordinates, float exposedTemperature, float exposedVolume)
    {
        if (coordinates.EntityId is not { Valid: true } uid)
            return;

        var uidTransform = Transform(uid);
        if (uidTransform.GridUid is not { } gridUid ||
            !_transformSystem.TryGetGridTilePosition((uid, uidTransform), out var tile))
            return;

        _atmosphereSystem.HotspotExpose(gridUid, tile, exposedTemperature, exposedVolume);
    }
}
