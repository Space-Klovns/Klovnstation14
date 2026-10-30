using Content.Client._KS14.PredictedSpawning;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Fluids;

/// <summary>
///     Predicts the spills of this client's own vapors (see <c>KsPredictedTileReactionSystem</c>). Onto a bare tile, a
///         placeholder puddle (<see cref="KsTilePlaceholderComponent"/>), handed over when the server's puddle for the
///         tile arrives. Into a puddle the server already sent, only the volume it is drawn at
///         (<see cref="KsPredictedPuddleTopUpComponent"/>).
/// </summary>
public sealed partial class KsPredictedSpillSystem : EntitySystem
{
    /// <summary>
    ///     How long a placeholder puddle or a drawn top-up waits for the server's spill. A round trip, with room to spare.
    /// </summary>
    public static readonly TimeSpan PredictionLifetime = TimeSpan.FromSeconds(1.5);

    private static readonly EntProtoId PuddlePrototype = "Puddle";

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;
    [Dependency] private SharedPuddleSystem _puddleSystem = default!;
    [Dependency] private KsTilePlaceholderSystem _tilePlaceholderSystem = default!;

    [Dependency] private EntityQuery<PuddleComponent> _puddleQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<AppearanceComponent> _appearanceQuery = default!;
    [Dependency] private EntityQuery<KsPredictedPuddleTopUpComponent> _topUpQuery = default!;

    /// <summary>
    ///     Predicts spilling <paramref name="solution"/> on a tile, as the server's <c>TrySpillAt</c> will.
    /// </summary>
    /// <param name="onlyIfPuddlePresent">Whether to spill only into a puddle already there, as <c>SpillIfPuddlePresent</c> does.</param>
    /// <returns>Whether it was spilled, so the reaction used it up.</returns>
    public bool TrySpill(TileRef tileRef, Solution solution, bool onlyIfPuddlePresent)
    {
        if (solution.Volume <= FixedPoint2.Zero ||
            tileRef.Tile.IsEmpty ||
            _turfSystem.IsSpace(tileRef) ||
            !_mapGridQuery.TryComp(tileRef.GridUid, out var mapGridComponent))
            return false;

        FindPuddles((tileRef.GridUid, mapGridComponent), tileRef.GridIndices, out var placeholderUid, out var serverPuddleUid);

        if (placeholderUid is { } existingPlaceholderUid)
        {
            AddToPuddle(existingPlaceholderUid, solution);
            _tilePlaceholderSystem.Refresh(existingPlaceholderUid, PredictionLifetime);
            return true;
        }

        if (serverPuddleUid is { } puddleUid)
        {
            AddTopUp(puddleUid, solution.Volume);
            return true;
        }

        if (onlyIfPuddlePresent)
            return false;

        var coordinates = _mapSystem.GridTileToLocal(tileRef.GridUid, mapGridComponent, tileRef.GridIndices);
        var newPlaceholderUid = _tilePlaceholderSystem.Spawn(PuddlePrototype, coordinates, PredictionLifetime);
        AddToPuddle(newPlaceholderUid, solution);
        return true;
    }

    /// <summary>
    ///     The volume a puddle is drawn at: the server's, plus what this client has spilled into it and the server's
    ///         state does not show yet. Both as <see cref="PuddleVisuals.CurrentVolume"/> has them, a fraction of the
    ///         puddle's overflow volume.
    /// </summary>
    public float GetDrawnVolume(EntityUid puddleUid, float serverVolume)
    {
        if (!_topUpQuery.TryComp(puddleUid, out var topUpComponent))
            return serverVolume;

        if (serverVolume >= topUpComponent.BaselineVolume + topUpComponent.PendingVolume * 0.5f ||
            _gameTiming.CurTime >= topUpComponent.ExpiryTime)
        {
            // Landed, or never will: the server's volume is the puddle's again.
            RemCompDeferred<KsPredictedPuddleTopUpComponent>(puddleUid);
            return serverVolume;
        }

        return serverVolume + topUpComponent.PendingVolume;
    }

    /// <summary>
    ///     The server's puddle has arrived on a tile, and takes it over from this client's placeholder.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPuddleStartup(Entity<PuddleComponent> entity, ref ComponentStartup args)
    {
        if (IsClientSide(entity))
            return;

        var transformComponent = Transform(entity);
        if (transformComponent.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return;

        var tile = _mapSystem.LocalToTile(gridUid, mapGridComponent, transformComponent.Coordinates);
        FindPuddles((gridUid, mapGridComponent), tile, out var placeholderUid, out _);
        if (placeholderUid is { } supersededUid)
            _tilePlaceholderSystem.Supersede(supersededUid);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // A top-up whose server spill never landed stops being drawn, even if no state changes the puddle meanwhile.
        var curTime = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<KsPredictedPuddleTopUpComponent>();
        while (enumerator.MoveNext(out var uid, out var topUpComponent))
        {
            if (curTime < topUpComponent.ExpiryTime)
                continue;

            RemCompDeferred<KsPredictedPuddleTopUpComponent>(uid);
            if (_appearanceQuery.TryComp(uid, out var appearanceComponent))
                _appearanceSystem.QueueUpdate(uid, appearanceComponent);
        }
    }

    /// <summary>
    ///     The tile's puddles on this client: this client's waiting placeholder, and the server's puddle.
    /// </summary>
    private void FindPuddles(Entity<MapGridComponent> grid, Vector2i tile, out EntityUid? placeholderUid, out EntityUid? serverPuddleUid)
    {
        placeholderUid = null;
        serverPuddleUid = null;

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
        while (anchored.MoveNext(out var anchoredUid))
        {
            if (!_puddleQuery.HasComp(anchoredUid))
                continue;

            if (_tilePlaceholderSystem.IsPlaceholder(anchoredUid.Value))
            {
                if (_tilePlaceholderSystem.IsWaiting(anchoredUid.Value))
                    placeholderUid = anchoredUid;
            }
            else
            {
                serverPuddleUid = anchoredUid;
            }
        }
    }

    private void AddToPuddle(EntityUid puddleUid, Solution solution)
    {
        if (!_puddleQuery.TryComp(puddleUid, out var puddleComponent) ||
            !_solutionContainerSystem.ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution))
            return;

        _solutionContainerSystem.TryAddSolution(puddleComponent.Solution.Value, solution);
    }

    private void AddTopUp(EntityUid puddleUid, FixedPoint2 volume)
    {
        if (!_puddleQuery.TryComp(puddleUid, out var puddleComponent))
            return;

        // As the drawn volume is: a fraction of what the puddle holds before it overflows.
        _solutionContainerSystem.ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution);
        var overflowVolume = (float)_puddleSystem.KsGetOverflowVolume(puddleComponent, solution);
        if (overflowVolume <= 0f)
            return;

        if (!_topUpQuery.TryComp(puddleUid, out var topUpComponent))
        {
            topUpComponent = AddComp<KsPredictedPuddleTopUpComponent>(puddleUid);
            topUpComponent.BaselineVolume = _appearanceSystem.TryGetData<float>(puddleUid, PuddleVisuals.CurrentVolume, out var serverVolume)
                ? serverVolume
                : 0f;
        }

        topUpComponent.PendingVolume += (float)volume / overflowVolume;
        topUpComponent.ExpiryTime = _gameTiming.CurTime + PredictionLifetime;

        if (_appearanceQuery.TryComp(puddleUid, out var appearanceComponent))
            _appearanceSystem.QueueUpdate(puddleUid, appearanceComponent);
    }
}
