using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.Atmos.ChemicalFire;

/// <summary>
///     Client half of the chemfire system. The real per-tile gas mixture atmos would compute is server-only,
///         but the client can still resolve a decent stand-in for a tile with no floor - there's no gas grid
///         to hold anything there anyway, so the tile's mixture is unambiguously either the map's own
///         atmosphere (<see cref="MapAtmosphereComponent"/>) or raw space. That is enough for
///         <see cref="CanSustain"/> to predict correctly for a chemfire spawned into vacuum or a
///         space-exposed room, which is the common case this whole feature exists for; a tile with a floor
///         still resolves to null here; visuals are handled by <see cref="ChemicalFireVisualsSystem"/> and
///         <see cref="ChemicalFireOverlay"/>.
/// </summary>
public sealed partial class ChemicalFireSystem : SharedChemicalFireSystem
{
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private EntityQuery<MapAtmosphereComponent> _mapAtmosphereQuery = default!;

    protected override GasMixture? ResolveTileMixture(EntityUid gridUid, Vector2i tile, bool excite)
    {
        var tileRef = _mapSystem.GetTileRef((gridUid, Comp<MapGridComponent>(gridUid)), tile);
        if (tileRef.Tile.IsEmpty)
        {
            if (!_mapAtmosphereQuery.TryGetComponent(gridUid, out var mapAtmosphereComponent))
                return GasMixture.SpaceGas;
            else
                return mapAtmosphereComponent.Mixture;
        }

        return null;
    }
}
