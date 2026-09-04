using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Random.Helpers;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client._KS14.Atmos.ChemicalFire;

/// <summary>
///     Resolves a chemfire's sprite variation and its lateral <c>-west</c>/<c>-east</c>/<c>-full</c>
///         connection state, and pushes the result onto the entity's <c>under</c> sprite layer.
///     Recalculation is deferred through a dirty queue with a generation guard, exactly like
///         <see cref="Client.IconSmoothing.IconSmoothSystem"/>, so a tile changing hands only costs one
///         resmooth per affected chemfire per frame.
/// </summary>
public sealed partial class ChemicalFireVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;

    private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;
    private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;
    private EntityQuery<SpriteComponent> _spriteQuery = default!;

    private readonly Queue<EntityUid> _dirtyFires = new();
    private int _generation;

    /// <summary>
    ///     Lateral neighbours a chemfire can smooth into, and the connection they contribute.
    /// </summary>
    private static readonly (Vector2i Offset, ChemicalFireConnection Connection)[] LateralNeighbours =
    [
        (new Vector2i(-1, 0), ChemicalFireConnection.West),
        (new Vector2i(1, 0), ChemicalFireConnection.East),
    ];

    public override void Initialize()
    {
        base.Initialize();

        _chemicalFireQuery = GetEntityQuery<ChemicalFireComponent>();
        _chemicalFireGridQuery = GetEntityQuery<ChemicalFireGridComponent>();
        _spriteQuery = GetEntityQuery<SpriteComponent>();

        SubscribeLocalEvent<ChemicalFireComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
        SubscribeLocalEvent<ChemicalFireTileChangedEvent>(OnTileChanged);

        UpdatesOutsidePrediction = true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_dirtyFires.Count == 0)
            return;

        ++_generation;

        while (_dirtyFires.TryDequeue(out var uid))
            UpdateSprite(uid);
    }

    private void OnAfterAutoHandleState(Entity<ChemicalFireComponent> entity, ref AfterAutoHandleStateEvent args)
        => _dirtyFires.Enqueue(entity.Owner);

    /// <remarks>
    ///     A tile changing affects the chemfires standing on it and the ones directly beside it, since those
    ///         are what the <c>-west</c>/<c>-east</c>/<c>-full</c> states describe.
    /// </remarks>
    private void OnTileChanged(ref ChemicalFireTileChangedEvent args)
    {
        if (!_chemicalFireGridQuery.TryGetComponent(args.GridUid, out var gridComponent))
            return;

        DirtyTile(gridComponent, args.Tile);
        foreach (var (offset, _) in LateralNeighbours)
            DirtyTile(gridComponent, args.Tile + offset);
    }

    private void DirtyTile(ChemicalFireGridComponent gridComponent, Vector2i tile)
    {
        if (!gridComponent.Tiles.TryGetValue(tile, out var tileData))
            return;

        foreach (var fire in tileData.Fires.Values)
            _dirtyFires.Enqueue(fire.Owner);
    }

    private void UpdateSprite(EntityUid uid)
    {
        if (!_chemicalFireQuery.TryGetComponent(uid, out var fireComponent) ||
            !fireComponent.Running ||
            fireComponent.UpdateGeneration == _generation ||
            !_spriteQuery.TryGetComponent(uid, out var spriteComponent))
            return;

        fireComponent.UpdateGeneration = _generation;

        var fire = new Entity<ChemicalFireComponent>(uid, fireComponent);
        fireComponent.Variation = GetVariation(fire);
        fireComponent.Connection = GetConnection(fire);

        var stateSuffix = fireComponent.Variation + GetConnectionSuffix(fireComponent.Connection);
        fireComponent.OverState = fireComponent.OverStatePrefix + stateSuffix;

        if (!_spriteSystem.LayerMapTryGet((uid, spriteComponent), ChemicalFireVisualLayers.Under, out var layerIndex, false))
            return;

        _spriteSystem.LayerSetRsiState((uid, spriteComponent), layerIndex, fireComponent.UnderStatePrefix + stateSuffix);
        _spriteSystem.LayerSetColor((uid, spriteComponent), layerIndex, fireComponent.Color);
    }

    /// <summary>
    ///     Picks the sprite variation deterministically from the grid and tile, so every client (and the
    ///         server, were it to care) agrees without any of it being networked.
    /// </summary>
    private int GetVariation(Entity<ChemicalFireComponent> fire)
    {
        if (fire.Comp.SpriteVariations <= 1 ||
            fire.Comp.LocalGridUid is not { } gridUid)
            return 1;

        var seed = KsSharedRandomExtensions.HashCodeCombine(
            KsSharedRandomExtensions.GetNetId(gridUid, EntityManager),
            fire.Comp.LocalTile.X,
            fire.Comp.LocalTile.Y
        );

        return 1 + (int)((uint)seed % (uint)fire.Comp.SpriteVariations);
    }

    /// <remarks>
    ///     Neighbour tiles are keyed by connection key in the grid cache, so this is two dictionary lookups
    ///         rather than an entity lookup.
    /// </remarks>
    private ChemicalFireConnection GetConnection(Entity<ChemicalFireComponent> fire)
    {
        if (fire.Comp.LocalGridUid is not { } gridUid ||
            !_chemicalFireGridQuery.TryGetComponent(gridUid, out var gridComponent))
            return ChemicalFireConnection.None;

        var connectionKey = _chemicalFireSystem.GetConnectionKey(fire);
        var connection = ChemicalFireConnection.None;

        foreach (var (offset, neighbourConnection) in LateralNeighbours)
        {
            if (!gridComponent.Tiles.TryGetValue(fire.Comp.LocalTile + offset, out var tileData) ||
                !tileData.Fires.ContainsKey(connectionKey))
                continue;

            connection |= neighbourConnection;
        }

        return connection;
    }

    private static string GetConnectionSuffix(ChemicalFireConnection connection) => connection switch
    {
        ChemicalFireConnection.West => "-west",
        ChemicalFireConnection.East => "-east",
        ChemicalFireConnection.Full => "-full",
        _ => string.Empty,
    };
}
