using System.Linq;
using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server._KS14.Atmos.TileFire;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;

namespace Content.Server._KS14.Atmos.ChemicalFire;

/// <summary>
///     Server half of the chemfire system: atmospherics only exist server-side, so the actual tile ignition
///         lives here, mirroring the <see cref="Sparks.SparksSystem"/> split.
/// </summary>
public sealed partial class ChemicalFireSystem : SharedChemicalFireSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsFoamBlanketSystem _foamBlanketSystem = default!;
    [Dependency] private KsTileFireSystem _tileFireSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    /// <summary>
    ///     Reports a tile holding a chemfire as burning, even when there is no gas fire on it.
    /// </summary>
    /// <remarks>
    ///     Tile reactions gate on this before they do anything - <c>ExtinguishTileReaction</c> would otherwise
    ///         bail out and never reach <see cref="AtmosphereSystem.HotspotExtinguish"/>, leaving extinguishers
    ///         useless against a chemfire burning in an atmosphere with nothing flammable in it.
    ///     Deliberately runs even once the event is handled, since it only ever raises the answer.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnGridIsHotspotActive(Entity<ChemicalFireGridComponent> entity, ref AtmosphereSystem.IsHotspotActiveMethodEvent args)
    {
        if (args.Result || !entity.Comp.Tiles.ContainsKey(args.Tile))
            return;

        args.Result = true;
        args.Handled = true;
    }

    /// <summary>
    ///     Answers <see cref="KsTileFireSystem"/> with the chemfires burning a tile.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGridGetTileFireSources(Entity<ChemicalFireGridComponent> entity, ref KsGetTileFireSourcesEvent args)
    {
        if (args.AnySources || GetTileChemicalFires((entity.Owner, entity.Comp), args.Tile) is not { } tileData)
            return;

        foreach (var fire in tileData.Fires.Values)
        {
            if (fire.Owner == args.IgnoredSourceUid)
                continue;

            args.Report();
            return;
        }
    }

    /// <summary>
    ///     Douses the chemfires on a tile, which is how an extinguisher reaches them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGridExtinguishTileFireSources(Entity<ChemicalFireGridComponent> entity, ref KsExtinguishTileFireSourcesEvent args)
    {
        if (GetTileChemicalFires((entity.Owner, entity.Comp), args.Tile) is not { } tileData)
            return;

        // Copied, since a chemfire may deregister itself as it goes out.
        foreach (var fire in tileData.Fires.Values.ToArray())
        {
            if (!fire.Comp.Extinguishable)
                continue;

            ExtinguishChemicalFire(fire);
        }
    }

    /// <summary>
    ///     Tells everything standing on the tile that it is now on fire, the same way a hotspot does.
    /// </summary>
    /// <remarks>
    ///     Only the first of these; <see cref="OnHeatTile"/> repeats it for as long as the chemfire burns.
    ///     Done here as well so that a chemfire sets its tile alight the moment it appears, rather than
    ///         waiting out a whole <see cref="ChemicalFireComponent.HeatInterval"/> first.
    /// </remarks>
    protected override void AfterFireStartup(Entity<ChemicalFireComponent> entity)
    {
        if (entity.Comp.LocalGridUid is not { } gridUid)
            return;

        _tileFireSystem.RaiseTileFire(gridUid, entity.Comp.LocalTile, entity.Comp.Temperature, entity.Comp.ExposedVolume);
    }

    /// <summary>
    ///     The other half of <see cref="AfterFireStartup"/>: this chemfire has stopped burning the tile.
    /// </summary>
    /// <remarks>
    ///     Runs while the chemfire is still registered on its tile, so that
    ///         <see cref="KsTileFireSystem.RaiseTileExtinguish"/> can tell it apart from whatever else may
    ///         still be burning there - a tile is only announced as out once the last fire on it goes out.
    /// </remarks>
    protected override void BeforeFireShutdown(Entity<ChemicalFireComponent> entity)
    {
        if (entity.Comp.LocalGridUid is not { } gridUid)
            return;

        _tileFireSystem.RaiseTileExtinguish(gridUid, entity.Comp.LocalTile, entity.Owner);
    }

    /// <remarks>
    ///     <see cref="AtmosphereSystem.HotspotExpose"/> already no-ops unless the tile's mixture is both
    ///         oxidiser and fuel, which is exactly "ignite any fuel gases given an oxidiser is present" -
    ///         so no gas checks are needed here.
    ///     The tile is announced as burning on every heat tick rather than only at startup, matching how a
    ///         hotspot announces itself once per atmos cycle: whatever walks onto a burning tile has to catch
    ///         fire too, and a chemfire sharing a tile with a cooler fire has to keep setting the pace.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnHeatTile(Entity<ChemicalFireComponent> entity, ref ChemicalFireHeatTileEvent args)
    {
        // Cooled out before it can heat the air back up again.
        if (args.Mixture is { } mixture && IsTooCold(entity.Comp, mixture))
        {
            ExtinguishChemicalFire(entity);
            return;
        }

        HeatTileAir(entity, ref args);

        if (_foamBlanketSystem.ContestFire(entity, args.GridUid, args.Tile, args.Seconds))
            return;

        _tileFireSystem.RaiseTileFire(args.GridUid, args.Tile, entity.Comp.Temperature, entity.Comp.ExposedVolume);

        _atmosphereSystem.HotspotExpose(
            args.GridUid,
            args.Tile,
            entity.Comp.Temperature,
            entity.Comp.ExposedVolume,
            sparkSourceUid: entity.Owner,
            soh: true
        );
    }

    /// <summary>
    ///     Warms the air on the chemfire's tile directly, independently of whether there is anything on it to
    ///         set alight.
    /// </summary>
    /// <remarks>
    ///     Capped at <see cref="ChemicalFireComponent.Temperature"/>: a chemfire is a heat source at its own
    ///         temperature, not an unbounded energy pump, so a cool one must not be able to cook a room simply
    ///         by burning for long enough.
    /// </remarks>
    private void HeatTileAir(Entity<ChemicalFireComponent> entity, ref ChemicalFireHeatTileEvent args)
    {
        if (entity.Comp.HeatPower <= 0f)
            return;

        var energyBudget = entity.Comp.HeatPower * args.Seconds;

        // A foam blanket over the chemfire takes its share first, boiling away; then a puddle under it. Whatever
        //     neither can absorb falls through to the air.
        energyBudget -= _foamBlanketSystem.HeatBlanket(args.GridUid, args.Tile, energyBudget * Math.Clamp(entity.Comp.FoamHeatFraction, 0f, 1f));
        energyBudget -= HeatTilePuddle(entity, args.GridUid, args.Tile, energyBudget);

        if (energyBudget <= 0f || args.Mixture is not { } mixture || mixture.Immutable || mixture.Temperature >= entity.Comp.Temperature)
            return;

        var heatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (heatCapacity < Atmospherics.MinimumHeatCapacity)
            return;

        var energy = MathF.Min(
            energyBudget,
            (entity.Comp.Temperature - mixture.Temperature) * heatCapacity
        );

        _atmosphereSystem.AddHeat(mixture, energy);
    }

    /// <summary>
    ///     Pours <see cref="ChemicalFireComponent.PuddleHeatFraction"/> of <paramref name="energyBudget"/> into the
    ///         puddle on the tile, if there is one, never warming it past <see cref="ChemicalFireComponent.Temperature"/>.
    /// </summary>
    /// <returns>The energy actually given to the puddle, in joules; zero if there is no puddle or it took none.</returns>
    private float HeatTilePuddle(Entity<ChemicalFireComponent> entity, EntityUid gridUid, Vector2i tile, float energyBudget)
    {
        var fraction = Math.Clamp(entity.Comp.PuddleHeatFraction, 0f, 1f);
        if (fraction <= 0f || !_puddleSystem.TryGetCachedPuddle(gridUid, tile, out Entity<PuddleComponent> puddle))
            return 0f;

        if (!_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution) ||
            puddle.Comp.Solution is not { } solutionEntity ||
            solution.Temperature >= entity.Comp.Temperature)
            return 0f;

        var heatCapacity = solution.GetHeatCapacity(ProtoMan);
        if (heatCapacity <= 0f)
            return 0f;

        var energy = MathF.Min(
            energyBudget * fraction,
            (entity.Comp.Temperature - solution.Temperature) * heatCapacity
        );

        if (energy <= 0f)
            return 0f;

        _solutionContainerSystem.AddThermalEnergy(solutionEntity, energy);
        return energy;
    }

    protected override GasMixture? ResolveTileMixture(EntityUid gridUid, Vector2i tile, bool excite)
        => _atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite);
}
