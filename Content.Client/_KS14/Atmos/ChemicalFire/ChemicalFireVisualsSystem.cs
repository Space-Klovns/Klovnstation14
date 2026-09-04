using System.Numerics;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Random.Helpers;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
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
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;

    private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;
    private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;
    private EntityQuery<SpriteComponent> _spriteQuery = default!;
    private EntityQuery<TransformComponent> _transformQuery = default!;

    private readonly Queue<EntityUid> _dirtyFires = new();
    private int _generation;

    /// <summary>
    ///     Eye rotation the chemfires were last resolved against. Connections are expressed in rendered space,
    ///         so turning the eye far enough to snap the sprites onto a different cardinal invalidates every
    ///         chemfire at once.
    /// </summary>
    private Angle _lastEyeRotation = Angle.Zero;

    /// <summary>
    ///     Every tile a chemfire could possibly smooth into. Which two of these actually count is decided per
    ///         chemfire at resolve time, since that depends on how its sprite ends up rotated on screen.
    /// </summary>
    private static readonly Vector2i[] NeighbourOffsets =
    [
        new Vector2i(-1, 0),
        new Vector2i(1, 0),
        new Vector2i(0, -1),
        new Vector2i(0, 1),
    ];

    public override void Initialize()
    {
        base.Initialize();

        _chemicalFireQuery = GetEntityQuery<ChemicalFireComponent>();
        _chemicalFireGridQuery = GetEntityQuery<ChemicalFireGridComponent>();
        _spriteQuery = GetEntityQuery<SpriteComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<ChemicalFireComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
        SubscribeLocalEvent<ChemicalFireTileChangedEvent>(OnTileChanged);

        UpdatesOutsidePrediction = true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        DirtyOnEyeRotated();

        if (_dirtyFires.Count == 0)
            return;

        ++_generation;

        while (_dirtyFires.TryDequeue(out var uid))
            UpdateSprite(uid);
    }

    /// <summary>
    ///     Chemfire sprites snap to cardinals, so which grid tiles read as left and right on screen moves with
    ///         the eye. Whenever the eye turns, every chemfire has to reconsider what it connects to.
    /// </summary>
    private void DirtyOnEyeRotated()
    {
        var eyeRotation = _eyeManager.CurrentEye.Rotation;
        if (eyeRotation.EqualsApprox(_lastEyeRotation))
            return;

        _lastEyeRotation = eyeRotation;

        var query = AllEntityQuery<ChemicalFireComponent>();
        while (query.MoveNext(out var uid, out _))
            _dirtyFires.Enqueue(uid);
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
        foreach (var offset in NeighbourOffsets)
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
        fireComponent.Connection = GetConnection(fire, (uid, spriteComponent));

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
    private ChemicalFireConnection GetConnection(Entity<ChemicalFireComponent> fire, Entity<SpriteComponent> sprite)
    {
        if (fire.Comp.LocalGridUid is not { } gridUid ||
            !_chemicalFireGridQuery.TryGetComponent(gridUid, out var gridComponent))
            return ChemicalFireConnection.None;

        var eastOffset = GetRenderedEastOffset(fire, sprite, gridUid);
        var connectionKey = _chemicalFireSystem.GetConnectionKey(fire);
        var connection = ChemicalFireConnection.None;

        if (HasNeighbour(gridComponent, fire.Comp.LocalTile - eastOffset, connectionKey))
            connection |= ChemicalFireConnection.West;

        if (HasNeighbour(gridComponent, fire.Comp.LocalTile + eastOffset, connectionKey))
            connection |= ChemicalFireConnection.East;

        return connection;
    }

    private static bool HasNeighbour(ChemicalFireGridComponent gridComponent, Vector2i tile, string connectionKey)
        => gridComponent.Tiles.TryGetValue(tile, out var tileData) && tileData.Fires.ContainsKey(connectionKey);

    /// <summary>
    ///     The grid-local tile offset that a chemfire currently renders the east side of its sprite towards.
    /// </summary>
    /// <remarks>
    ///     The <c>-west</c>/<c>-east</c>/<c>-full</c> states describe the flame as the player sees it, but
    ///         <see cref="SpriteComponent.SnapCardinals"/> means the drawn orientation is
    ///         <c>spriteRotation - cardinal</c> rather than the entity's true rotation, so smoothing against raw
    ///         grid axes breaks as soon as the eye (or the grid) is turned. This replicates the snapping maths
    ///         the renderer and <see cref="ChemicalFireOverlay"/> both use, leaving the connection following
    ///         what is actually on screen.
    /// </remarks>
    private Vector2i GetRenderedEastOffset(Entity<ChemicalFireComponent> fire, Entity<SpriteComponent> sprite, EntityUid gridUid)
    {
        var localEyeRotation = _eyeManager.CurrentEye.Rotation + _transformSystem.GetWorldRotation(gridUid);
        var spriteRotation = _transformQuery.GetComponent(fire.Owner).LocalRotation + sprite.Comp.Rotation;

        var cardinal = (spriteRotation + localEyeRotation)
            .Reduced()
            .FlipPositive()
            .RoundToCardinalAngle();

        // Rounded again because the sprite is only snapped relative to the eye - a grid at some arbitrary
        //     angle still leaves a non-cardinal remainder here, which no tile offset could express.
        var direction = (spriteRotation - cardinal).RoundToCardinalAngle().RotateVec(Vector2.UnitX);

        return new Vector2i((int)MathF.Round(direction.X), (int)MathF.Round(direction.Y));
    }

    private static string GetConnectionSuffix(ChemicalFireConnection connection) => connection switch
    {
        ChemicalFireConnection.West => "-west",
        ChemicalFireConnection.East => "-east",
        ChemicalFireConnection.Full => "-full",
        _ => string.Empty,
    };
}
