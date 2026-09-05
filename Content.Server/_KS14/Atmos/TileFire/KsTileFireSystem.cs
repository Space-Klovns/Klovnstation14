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
///         added next - and everything standing on that tile has to be told about the fire exactly once,
///         whichever of them is doing the burning. Rather than have each source check for the others, every
///         source answers <see cref="KsGetTileFireSourceEvent"/> for itself and then routes its announcements
///         through here, where two rules are applied:
///     <list type="bullet">
///         <item>a fire is announced only by the strongest source burning the tile, so a weaker one burning
///             alongside it stays quiet;</item>
///         <item>an extinguishing is announced only once nothing else is burning the tile, so the tile reads as
///             out exactly when it is.</item>
///     </list>
///     Adding a new kind of tile fire therefore means answering one event and calling two methods - never
///         teaching the existing sources about it.
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

        SubscribeLocalEvent<GridAtmosphereComponent, KsGetTileFireSourceEvent>(OnGridGetTileFireSource);
    }

    /// <remarks>
    ///     Answered here rather than by atmospherics itself because a hotspot is tile data rather than an
    ///         entity, so it has nothing of its own to hang a subscription off. The grid stands in for it.
    /// </remarks>
    private void OnGridGetTileFireSource(Entity<GridAtmosphereComponent> entity, ref KsGetTileFireSourceEvent args)
    {
        if (entity.Owner == args.IgnoredSourceUid ||
            !_atmosphereSystem.HasGasHotspot((entity.Owner, entity.Comp), args.Tile))
            return;

        args.Report(KsTileFireSourcePriority.Hotspot);
    }

    /// <summary>
    ///     Tells everything standing on a tile that the tile is on fire, unless something is burning it harder
    ///         than <paramref name="sourceUid"/> is.
    /// </summary>
    /// <param name="sourceUid">
    ///     What is doing the burning; the grid itself, for a hotspot. Left out of the check, so a source
    ///         answering <see cref="KsGetTileFireSourceEvent"/> never outranks itself.
    /// </param>
    /// <returns>Whether the fire was announced.</returns>
    public bool RaiseTileFire(
        EntityUid gridUid,
        Vector2i tile,
        EntityUid sourceUid,
        KsTileFireSourcePriority priority,
        float temperature,
        float volume)
    {
        if (GetTileFireSourcePriority(gridUid, tile, sourceUid) > priority)
            return false;

        var tileFireEvent = new TileFireEvent(temperature, volume);

        foreach (var bystanderUid in GetTileEntities(gridUid, tile))
            RaiseLocalEvent(bystanderUid, ref tileFireEvent);

        return true;
    }

    /// <summary>
    ///     Tells everything standing on a tile that the fire on it is over, unless something else is still
    ///         burning it.
    /// </summary>
    /// <remarks>
    ///     Call this as the source stops burning, while it is still registered as a source: it is excluded from
    ///         the check by <paramref name="sourceUid"/>, so a source that has already deregistered would just
    ///         be asking whether it is the last one out twice over.
    /// </remarks>
    /// <returns>Whether the tile was announced as out.</returns>
    public bool RaiseTileExtinguish(EntityUid gridUid, Vector2i tile, EntityUid sourceUid)
    {
        if (GetTileFireSourcePriority(gridUid, tile, sourceUid) is not null)
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
    ///     The strongest thing burning a tile, ignoring one source, or null if nothing is burning it.
    /// </summary>
    public KsTileFireSourcePriority? GetTileFireSourcePriority(EntityUid gridUid, Vector2i tile, EntityUid ignoredSourceUid)
    {
        var getSourceEvent = new KsGetTileFireSourceEvent(tile, ignoredSourceUid);
        RaiseLocalEvent(gridUid, ref getSourceEvent);

        return getSourceEvent.HighestPriority;
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
