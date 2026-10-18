using Content.Shared.Atmos;

namespace Content.Shared._KS14.TileEffects;

/// <summary>
///     Raised by <see cref="Effects.BoilTileEffect"/> to put boiled reagent into a tile's air, which only the server
///         can reach. Whoever adds it sets <see cref="Handled"/>; an unhandled event means nothing boiled.
/// </summary>
/// <param name="Units">Reagent units boiled, treated as grams.</param>
/// <param name="SensibleEnergy">Thermal energy the boiled units carry into the air, in joules.</param>
[ByRefEvent]
public record struct KsTileEffectBoilEvent(EntityUid GridUid, Vector2i Tile, Gas Gas, float Units, float SensibleEnergy)
{
    public bool Handled;
}
