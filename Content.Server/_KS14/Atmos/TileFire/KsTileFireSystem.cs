using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;

namespace Content.Server._KS14.Atmos.TileFire;

/// <summary>
///     The single place <see cref="TileFireEvent"/> and <see cref="TileExtinguishEvent"/> are raised from, and
///         the only thing that decides when they may be raised.
/// </summary>
/// <remarks>
///     Several unrelated things can burn the same tile - an atmospherics hotspot, a chemfire, whatever gets
///         added next - and each of them burns whatever is standing there in its own right, so a fire is
///         announced whenever any of them says so.
///     Ending a fire is the asymmetric half: the tile has only stopped burning once the last source stops, so
///         an extinguishing is announced only when nothing else answers
///         <see cref="KsGetTileFireSourcesEvent"/> for the tile. A chemfire burning out over a gas fire would
///         otherwise report the tile as out while it was still alight, and the gas fire would report it out
///         again later - one fire ending must not read as two.
///     Every source answers that event for itself and routes its announcements through here, so adding a new
///         kind of tile fire means answering one event and calling two methods - never teaching the existing
///         sources about it.
/// </remarks>
public sealed partial class KsTileFireSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;

    /// <summary>Scratch set for the entities standing on a tile.</summary>
    private readonly HashSet<EntityUid> _tileEntities = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridAtmosphereComponent, KsGetTileFireSourcesEvent>(OnGridGetTileFireSources);
    }

    /// <remarks>
    ///     Answered here rather than by atmospherics itself because a hotspot is tile data rather than an
    ///         entity, so it has nothing of its own to hang a subscription off. The grid stands in for it.
    /// </remarks>
    private void OnGridGetTileFireSources(Entity<GridAtmosphereComponent> entity, ref KsGetTileFireSourcesEvent args)
    {
        if (args.AnySources ||
            entity.Owner == args.IgnoredSourceUid ||
            !_atmosphereSystem.HasGasHotspot((entity.Owner, entity.Comp), args.Tile))
            return;

        args.Report();
    }

    /// <summary>
    ///     Tells everything standing on a tile that the tile is on fire.
    /// </summary>
    public void RaiseTileFire(EntityUid gridUid, Vector2i tile, float temperature, float volume)
    {
        var tileFireEvent = new TileFireEvent(temperature, volume);

        foreach (var bystanderUid in GetTileEntities(gridUid, tile))
            RaiseLocalEvent(bystanderUid, ref tileFireEvent);
    }

    /// <summary>
    ///     Tells everything standing on a tile that the fire on it is over, unless something else is still
    ///         burning it.
    /// </summary>
    /// <param name="sourceUid">
    ///     What has stopped burning; the grid itself, for a hotspot. Left out of the check, so a source never
    ///         reads its own claim on the tile as a reason to stay quiet.
    /// </param>
    /// <remarks>
    ///     Call this as the source stops burning: anything else holding the tile has to still be able to answer
    ///         for itself when it does.
    /// </remarks>
    /// <returns>Whether the tile was announced as out.</returns>
    public bool RaiseTileExtinguish(EntityUid gridUid, Vector2i tile, EntityUid sourceUid)
    {
        if (HasTileFireSources(gridUid, tile, sourceUid))
            return false;

        var tileExtinguishEvent = new TileExtinguishEvent();

        foreach (var bystanderUid in GetTileEntities(gridUid, tile))
            RaiseLocalEvent(bystanderUid, tileExtinguishEvent);

        return true;
    }

    /// <summary>
    ///     Puts out everything burning a tile, and announces the tile as out if nothing keeps burning it.
    /// </summary>
    /// <remarks>
    ///     Sources that end immediately drop out of the check below and are covered by this call; ones that
    ///         take until the end of the tick to die are still burning as far as it is concerned, and announce
    ///         themselves once they actually go out.
    /// </remarks>
    public void ExtinguishTile(EntityUid gridUid, Vector2i tile, EntityUid sourceUid)
    {
        var extinguishSourcesEvent = new KsExtinguishTileFireSourcesEvent(tile);
        RaiseLocalEvent(gridUid, ref extinguishSourcesEvent);

        RaiseTileExtinguish(gridUid, tile, sourceUid);
    }

    /// <summary>
    ///     Whether anything other than <paramref name="ignoredSourceUid"/> is burning a tile.
    /// </summary>
    public bool HasTileFireSources(EntityUid gridUid, Vector2i tile, EntityUid ignoredSourceUid)
    {
        var getSourcesEvent = new KsGetTileFireSourcesEvent(tile, ignoredSourceUid);
        RaiseLocalEvent(gridUid, ref getSourcesEvent);

        return getSourcesEvent.AnySources;
    }

    /// <remarks>
    ///     Unenlarged, matching how a hotspot picks out what it is burning.
    ///     A grid on its way out takes its broadphase with it before its children shut down, so a fire dying
    ///         along with its grid has nothing to look the tile up with and nobody left on it to tell.
    /// </remarks>
    private HashSet<EntityUid> GetTileEntities(EntityUid gridUid, Vector2i tile)
    {
        _tileEntities.Clear();

        if (TerminatingOrDeleted(gridUid))
            return _tileEntities;

        _entityLookupSystem.GetLocalEntitiesIntersecting(gridUid, tile, _tileEntities, enlargement: 0f);

        return _tileEntities;
    }
}
