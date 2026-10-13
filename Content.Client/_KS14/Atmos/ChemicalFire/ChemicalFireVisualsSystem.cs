using System.Numerics;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Random.Helpers;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Atmos.ChemicalFire;

/// <summary>
///     Resolves a chemfire's sprite variation, animation phase and lateral <c>-west</c>/<c>-east</c>/<c>-full</c>
///         connection state into the RSI states <see cref="ChemicalFireOverlay"/> draws.
///     Recalculation is deferred through a dirty queue with a generation guard, exactly like
///         <see cref="Client.IconSmoothing.IconSmoothSystem"/>, so a tile changing hands only costs one
///         resmooth per affected chemfire per frame.
/// </summary>
public sealed partial class ChemicalFireVisualsSystem : EntitySystem
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;

    [Dependency] private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;
    [Dependency] private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    /// <summary>
    ///     How finely <see cref="ChemicalFireComponent.AnimationPhase"/> is divided. Prime, so the phase doesn't
    ///         repeat in step with the variation picked from the same seed.
    /// </summary>
    private const uint AnimationPhaseSteps = 997;

    private readonly Queue<EntityUid> _dirtyFires = new();
    private int _generation;

    /// <summary>
    ///     Eye rotation the chemfires were last resolved against. Connections are expressed in rendered space,
    ///         so turning the eye far enough to snap the flames onto a different cardinal invalidates every
    ///         chemfire at once.
    /// </summary>
    private Angle _lastEyeRotation = Angle.Zero;

    /// <summary>
    ///     Every tile a chemfire could possibly smooth into. Which two of these actually count is decided per
    ///         chemfire at resolve time, since that depends on how it ends up rotated on screen.
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
            UpdateVisuals(uid);
    }

    /// <summary>
    ///     The grid-local rotation a chemfire is drawn at: its own rotation, snapped so that it always faces the
    ///         nearest cardinal on screen, the way a <c>snapCardinals</c> sprite is.
    /// </summary>
    /// <param name="rotation">The chemfire's grid-local rotation.</param>
    /// <param name="localEyeRotation">The eye's rotation plus the grid's world rotation.</param>
    public static Angle GetDrawRotation(Angle rotation, Angle localEyeRotation)
    {
        var cardinal = (rotation + localEyeRotation)
            .Reduced()
            .FlipPositive()
            .RoundToCardinalAngle();

        return rotation - cardinal;
    }

    /// <summary>
    ///     Chemfires snap to cardinals, so which grid tiles read as left and right on screen moves with the eye.
    ///         Whenever the eye turns, every chemfire has to reconsider what it connects to.
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

    [SubscribeLocalEvent]
    private void OnAfterAutoHandleState(Entity<ChemicalFireComponent> entity, ref AfterAutoHandleStateEvent args)
        => _dirtyFires.Enqueue(entity.Owner);

    /// <remarks>
    ///     A tile changing affects the chemfires standing on it and the ones directly beside it, since those
    ///         are what the <c>-west</c>/<c>-east</c>/<c>-full</c> states describe.
    /// </remarks>
    [SubscribeLocalEvent]
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

    private void UpdateVisuals(EntityUid uid)
    {
        if (!_chemicalFireQuery.TryGetComponent(uid, out var fireComponent) ||
            !fireComponent.Running ||
            fireComponent.UpdateGeneration == _generation)
            return;

        fireComponent.UpdateGeneration = _generation;

        var fire = new Entity<ChemicalFireComponent>(uid, fireComponent);
        var tileSeed = GetTileSeed(fire);

        fireComponent.Variation = fireComponent.SpriteVariations <= 1
            ? 1
            : 1 + (int)(tileSeed % (uint)fireComponent.SpriteVariations);
        fireComponent.AnimationPhase = (float)(tileSeed % AnimationPhaseSteps) / AnimationPhaseSteps;
        fireComponent.Connection = GetConnection(fire);

        var stateSuffix = fireComponent.Variation + GetConnectionSuffix(fireComponent.Connection);
        fireComponent.UnderState = fireComponent.UnderStatePrefix + stateSuffix;
        fireComponent.OverState = fireComponent.OverStatePrefix + stateSuffix;
    }

    /// <summary>
    ///     A seed derived from the grid and tile, which the sprite variation and animation phase are picked
    ///         from, so every client (and the server, were it to care) agrees without any of it being networked.
    /// </summary>
    private uint GetTileSeed(Entity<ChemicalFireComponent> fire)
    {
        if (fire.Comp.LocalGridUid is not { } gridUid)
            return 0;

        return (uint)KsSharedRandomExtensions.HashCodeCombine(
            KsSharedRandomExtensions.GetNetId(gridUid, EntityManager),
            fire.Comp.LocalTile.X,
            fire.Comp.LocalTile.Y
        );
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

        var eastOffset = GetRenderedEastOffset(fire, gridUid);
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
    ///     The grid-local tile offset that a chemfire currently renders the east side of its flame towards.
    /// </summary>
    /// <remarks>
    ///     The <c>-west</c>/<c>-east</c>/<c>-full</c> states describe the flame as the player sees it, but
    ///         <see cref="GetDrawRotation"/> snaps the drawn orientation to the eye, so smoothing against raw
    ///         grid axes breaks as soon as the eye (or the grid) is turned. Using the same snapping as
    ///         <see cref="ChemicalFireOverlay"/> leaves the connection following what is actually on screen.
    /// </remarks>
    private Vector2i GetRenderedEastOffset(Entity<ChemicalFireComponent> fire, EntityUid gridUid)
    {
        var localEyeRotation = _eyeManager.CurrentEye.Rotation + _transformSystem.GetWorldRotation(gridUid);
        var drawRotation = GetDrawRotation(_transformQuery.GetComponent(fire.Owner).LocalRotation, localEyeRotation);

        // Rounded again because the flame is only snapped relative to the eye - a grid at some arbitrary
        //     angle still leaves a non-cardinal remainder here, which no tile offset could express.
        var direction = drawRotation.RoundToCardinalAngle().RotateVec(Vector2.UnitX);

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
