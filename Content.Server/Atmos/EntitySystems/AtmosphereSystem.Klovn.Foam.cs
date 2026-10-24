using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Shared.Atmos;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>
    ///     Foam blankets keep gas fires from starting under them, and a gas fire next to one boils it away.
    /// </summary>
    [Dependency] private KsFoamBlanketSystem _ksFoamBlanketSystem = default!;

    /// <summary>
    ///     Whether a foam blanket on the tile keeps a gas fire from starting there, which it only does while the air is
    ///         cooler than the foam's boiling point. See <see cref="KsFoamBlanketSystem.SmothersIgnition"/>.
    /// </summary>
    private bool KsIsSmotheredByFoam(TileAtmosphere tile)
        => _ksFoamBlanketSystem.SmothersIgnition(tile.GridIndex, tile.GridIndices, tile.Air);

    /// <summary>
    ///     A full tile fire radiating into a neighbour heats the blanket there, if there is one, which boils it off
    ///         with heat from the burning air. See <see cref="KsFoamBlanketSystem.ExposeToRadiatedFire"/>.
    /// </summary>
    private void KsRadiateIntoFoam(TileAtmosphere burningTile, TileAtmosphere otherTile)
    {
        if (burningTile.Air is not { } burningAir)
            return;

        _ksFoamBlanketSystem.ExposeToRadiatedFire(otherTile.GridIndex, otherTile.GridIndices, burningTile.GridIndices, burningAir.Temperature);
    }
}
