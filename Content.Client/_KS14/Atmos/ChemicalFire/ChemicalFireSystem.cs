using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.Atmos.ChemicalFire;

/// <summary>
///     Client half of the chemfire system. The real per-tile gas mixture atmos would compute is server-only,
///         but the client can still resolve a decent stand-in for a tile with no floor - there's no gas grid
///         to hold anything there anyway, so the tile's mixture is unambiguously the air of the map the grid is
///         on (<see cref="SharedAtmosphereSystem.GetMapMixture"/>), which is space if it has none. That is enough for
///         <see cref="CanSustain"/> to predict correctly for a chemfire spawned into vacuum or a
///         space-exposed room, which is the common case this whole feature exists for; a tile with a floor
///         still resolves to null here; visuals are handled by <see cref="ChemicalFireVisualsSystem"/> and
///         <see cref="ChemicalFireOverlay"/>.
/// </summary>
public sealed partial class ChemicalFireSystem : SharedChemicalFireSystem
{
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private SharedAtmosphereSystem _atmosphereSystem = default!;

    protected override GasMixture? ResolveTileMixture(EntityUid gridUid, Vector2i tile, bool excite)
    {
        var tileRef = _mapSystem.GetTileRef((gridUid, Comp<MapGridComponent>(gridUid)), tile);
        return tileRef.Tile.IsEmpty ? _atmosphereSystem.GetMapMixture(Transform(gridUid).MapUid) : null;
    }
}
