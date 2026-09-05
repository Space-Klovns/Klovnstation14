using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Robust.Shared.Map;

namespace Content.Server._KS14.Atmos.ChemicalFire;

/// <summary>
///     Server half of the chemfire system: atmospherics only exist server-side, so the actual tile ignition
///         lives here, mirroring the <see cref="Sparks.SparksSystem"/> split.
/// </summary>
public sealed partial class ChemicalFireSystem : SharedChemicalFireSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;

    [Dependency] private EntityQuery<ChemicalFireComponent> _fireQuery = default!;

    /// <summary>Scratch set for the entities standing on a chemfire's tile.</summary>
    private readonly HashSet<EntityUid> _tileEntities = [];

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

    /// <summary>
    ///     Tells everything standing on the tile that it is now on fire, the same way a hotspot does.
    /// </summary>
    protected override void AfterFireStartup(Entity<ChemicalFireComponent> entity)
    {
        if (!TryGetUnburningTile(entity, out var gridUid, out var tile))
            return;

        var tileFireEvent = new TileFireEvent(entity.Comp.Temperature, entity.Comp.ExposedVolume);

        foreach (var bystanderUid in GetTileBystanders(gridUid, tile))
            RaiseLocalEvent(bystanderUid, ref tileFireEvent);
    }

    /// <summary>
    ///     The other half of <see cref="AfterFireStartup"/>: the tile has stopped burning.
    /// </summary>
    protected override void BeforeFireShutdown(Entity<ChemicalFireComponent> entity)
    {
        if (!TryGetUnburningTile(entity, out var gridUid, out var tile))
            return;

        // Another chemfire is still holding the tile, so it has not stopped burning at all.
        if (GetTileChemicalFires((gridUid, null), tile) is { } tileData)
        {
            foreach (var otherFire in tileData.Fires.Values)
            {
                if (otherFire.Owner != entity.Owner)
                    return;
            }
        }

        var tileExtinguishEvent = new TileExtinguishEvent();

        foreach (var bystanderUid in GetTileBystanders(gridUid, tile))
            RaiseLocalEvent(bystanderUid, tileExtinguishEvent);
    }

    /// <summary>
    ///     Resolves the chemfire's tile, unless atmospherics is already burning a gas fire on it.
    /// </summary>
    /// <remarks>
    ///     A hotspot raises both of these events itself, every cycle it burns for, so a chemfire sitting in one
    ///         has nothing to announce.
    ///     The check matters most on the way out: a chemfire that lit a gas fire and then burned out would
    ///         otherwise raise <see cref="TileExtinguishEvent"/> while the hotspot it started is still going,
    ///         and the hotspot would raise it a second time once it too went out. One fire ending must not read
    ///         as two.
    /// </remarks>
    private bool TryGetUnburningTile(Entity<ChemicalFireComponent> entity, out EntityUid gridUid, out Vector2i tile)
    {
        gridUid = default;
        tile = entity.Comp.LocalTile;

        if (entity.Comp.LocalGridUid is not { } fireGridUid)
            return false;

        gridUid = fireGridUid;

        return !_atmosphereSystem.HasGasHotspot((gridUid, null), tile);
    }

    /// <summary>
    ///     Everything standing on a tile that a fire on it should be announced to.
    /// </summary>
    /// <remarks>
    ///     Unenlarged, matching how a hotspot picks out what it is burning. Chemfires are dropped: they are the
    ///         fire rather than something caught in it, and one handing another a
    ///         <see cref="TileExtinguishEvent"/> would put it out.
    /// </remarks>
    private HashSet<EntityUid> GetTileBystanders(EntityUid gridUid, Vector2i tile)
    {
        _tileEntities.Clear();
        _entityLookupSystem.GetLocalEntitiesIntersecting(gridUid, tile, _tileEntities, enlargement: 0f);
        _tileEntities.RemoveWhere(uid => _fireQuery.HasComponent(uid));

        return _tileEntities;
    }

    /// <remarks>
    ///     <see cref="AtmosphereSystem.HotspotExpose"/> already no-ops unless the tile's mixture is both
    ///         oxidiser and fuel, which is exactly "ignite any fuel gases given an oxidiser is present" -
    ///         so no gas checks are needed here.
    /// </remarks>
    private void OnHeatTile(Entity<ChemicalFireComponent> entity, ref ChemicalFireHeatTileEvent args)
    {
        HeatTileAir(entity, ref args);

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

        var mixture = _atmosphereSystem.GetTileMixture((args.GridUid, null, null), null, args.Tile, excite: true);
        if (mixture is null || mixture.Immutable || mixture.Temperature >= entity.Comp.Temperature)
            return;

        var heatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (heatCapacity < Atmospherics.MinimumHeatCapacity)
            return;

        var energy = MathF.Min(
            entity.Comp.HeatPower * args.Seconds,
            (entity.Comp.Temperature - mixture.Temperature) * heatCapacity
        );

        _atmosphereSystem.AddHeat(mixture, energy);
    }
}
