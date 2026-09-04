using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;

namespace Content.Server._KS14.Atmos.ChemicalFire;

/// <summary>
///     Server half of the chemfire system: atmospherics only exist server-side, so the actual tile ignition
///         lives here, mirroring the <see cref="Sparks.SparksSystem"/> split.
/// </summary>
public sealed partial class ChemicalFireSystem : SharedChemicalFireSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChemicalFireComponent, ChemicalFireHeatTileEvent>(OnHeatTile);
        SubscribeLocalEvent<ChemicalFireComponent, TileExtinguishEvent>(OnTileExtinguish);
        SubscribeLocalEvent<ChemicalFireGridComponent, AtmosphereSystem.IsHotspotActiveMethodEvent>(OnGridIsHotspotActive);
    }

    /// <summary>
    ///     Reports a tile holding a chemfire as burning, even when there is no gas fire on it.
    /// </summary>
    /// <remarks>
    ///     Tile reactions gate on this before they do anything - <c>ExtinguishTileReaction</c> would otherwise
    ///         bail out and never reach <see cref="OnTileExtinguish"/>, leaving extinguishers useless against a
    ///         chemfire burning in an atmosphere with nothing flammable in it.
    ///     Deliberately runs even once the event is handled, since it only ever raises the answer.
    /// </remarks>
    private void OnGridIsHotspotActive(Entity<ChemicalFireGridComponent> entity, ref AtmosphereSystem.IsHotspotActiveMethodEvent args)
    {
        if (args.Result || !entity.Comp.Tiles.ContainsKey(args.Tile))
            return;

        args.Result = true;
        args.Handled = true;
    }

    /// <remarks>
    ///     Raised on every entity on the tile by <c>AtmosphereSystem.HotspotExtinguish</c>, which is where the
    ///         water an extinguisher sprays ends up.
    /// </remarks>
    private void OnTileExtinguish(Entity<ChemicalFireComponent> entity, ref TileExtinguishEvent args)
    {
        if (!entity.Comp.Extinguishable)
            return;

        ExtinguishChemicalFire(entity);
    }

    /// <remarks>
    ///     <see cref="AtmosphereSystem.HotspotExpose"/> already no-ops unless the tile's mixture is both
    ///         oxidiser and fuel, which is exactly "ignite any fuel gases given an oxidiser is present" -
    ///         so no gas checks are needed here.
    /// </remarks>
    private void OnHeatTile(Entity<ChemicalFireComponent> entity, ref ChemicalFireHeatTileEvent args)
    {
        _atmosphereSystem.HotspotExpose(
            args.GridUid,
            args.Tile,
            entity.Comp.Temperature,
            entity.Comp.ExposedVolume,
            sparkSourceUid: entity.Owner,
            soh: true
        );
    }
}
