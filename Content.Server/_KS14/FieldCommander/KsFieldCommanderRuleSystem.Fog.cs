using Content.Shared._KS14.RCD;
using Robust.Shared.Map;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Fog

    /// <summary>
    ///     Brings the fog down around the defenders' base, if the rule has one.
    /// </summary>
    private void RaiseFog(KsFieldCommanderRuleComponent component)
    {
        if (component.FogRange <= 0f)
            return;

        var objectives = new List<MapCoordinates>();
        CollectDefenderObjectives(component, objectives);
        if (objectives.Count == 0 || _mapSystem.GetMapOrInvalid(objectives[0].MapId) is not { Valid: true } mapUid)
            return;

        component.FogMapUid = mapUid;
        component.FogOrigin = new MapCoordinates(GetBaseCentre(component, objectives), objectives[0].MapId);
        component.FogCurrentRange = component.FogRange;
        _fogSystem.SetFog(mapUid, component.FogOrigin.Position, component.FogCurrentRange);
    }

    /// <summary>
    ///     Lifts the fog far enough to take in <paramref name="coordinates"/>, with room to fight around it.
    /// </summary>
    private void LiftFogTo(KsFieldCommanderRuleComponent component, MapCoordinates coordinates)
    {
        if (component.FogMapUid is not { } mapUid || coordinates.MapId != component.FogOrigin.MapId)
            return;

        var range = (coordinates.Position - component.FogOrigin.Position).Length() + component.FogUplinkMargin;
        if (range <= component.FogCurrentRange)
            return;

        component.FogCurrentRange = range;
        _fogSystem.SetFog(mapUid, component.FogOrigin.Position, range);
    }

    /// <summary>
    ///     Whether <paramref name="coordinates"/> lies at least <paramref name="margin"/> beyond the fog's edge. Always
    ///         true with no fog.
    /// </summary>
    private static bool IsBeyondFog(KsFieldCommanderRuleComponent component, MapCoordinates coordinates, float margin)
    {
        if (component.FogMapUid == null || coordinates.MapId != component.FogOrigin.MapId)
            return true;

        var distance = component.FogCurrentRange + margin;
        return (coordinates.Position - component.FogOrigin.Position).LengthSquared() >= distance * distance;
    }

    /// <summary>
    ///     The spawn areas are for coming back to, not for fortifying: nobody may use an RCD in them.
    /// </summary>
    private void ForbidBuildingInSafeZones(KsFieldCommanderRuleComponent component)
    {
        var zonesByMap = new Dictionary<MapId, List<KsNoBuildZone>>();
        foreach (var safeZone in component.SafeZones)
        {
            if (!zonesByMap.TryGetValue(safeZone.Center.MapId, out var zones))
                zonesByMap[safeZone.Center.MapId] = zones = [];

            zones.Add(new KsNoBuildZone(safeZone.Center.Position, safeZone.Radius));
        }

        foreach (var (mapId, zones) in zonesByMap)
        {
            if (_mapSystem.GetMapOrInvalid(mapId) is { Valid: true } mapUid)
                _noBuildZoneSystem.SetZones(mapUid, zones);
        }
    }

    #endregion
}
