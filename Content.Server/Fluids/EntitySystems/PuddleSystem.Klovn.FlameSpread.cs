using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Fluids.Components;

namespace Content.Server.Fluids.EntitySystems;

/// <summary>
///     Flame running across a spill. A burning tile is an open flame to the puddles next to it, so anything an open flame
///         lights (<see cref="Content.Shared._KS14.TileEffects.Effects.CheckTemperatureTileEffect.LitByFlame"/>) catches
///         from its neighbour however cold it is, as fuel does, rather than waiting to be heated to its ignition point.
/// </summary>
/// <remarks>
///     Each tile-effect update, the flame reaches a few tiles out from every burning tile, through connected puddles a
///         flame can light. Anything else (water, foam, bare floor) stops it. How many tiles comes from the time since
///         the last update and the spread rate, with the fraction of a tile carried over, so how often puddles update
///         doesn't change how fast the flame moves.
/// </remarks>
public sealed partial class PuddleSystem
{
    [Dependency] private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;

    /// <summary>
    ///     <see cref="Content.Shared._KS14.CCVar.KsCCVars.AtmosFlameSpreadRate"/>.
    /// </summary>
    private float _flameSpreadRate;

    /// <summary>
    ///     Tiles the flame has earned but not yet moved, under one: the fraction left over from earlier updates.
    /// </summary>
    private float _flameSpreadProgress;

    private TimeSpan _lastFlameSpreadUpdate = TimeSpan.MinValue;

    private static readonly Vector2i[] FlameSpreadOffsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    /// <summary>
    ///     Hottest first, so that a flame reaching a tile already reached by a hotter one only has to go on from it if it
    ///         has more steps left.
    /// </summary>
    private static readonly Comparison<(Vector2i Tile, float Temperature)> HottestFirst =
        (a, b) => b.Temperature.CompareTo(a.Temperature);

    /// <summary>
    ///     The flame reaching each tile from burning tiles nearby, per grid, worked out at the start of each tile-effect
    ///         update. Only grids with fires on them are in it.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, SpreadFlame>> _spreadFlames = new();

    /// <summary>
    ///     The per-grid dictionaries of <see cref="_spreadFlames"/>, kept between updates rather than made anew for every
    ///         grid on every update.
    /// </summary>
    private readonly Stack<Dictionary<Vector2i, SpreadFlame>> _spreadFlamePool = new();

    private readonly List<(Vector2i Tile, float Temperature)> _burningTiles = new();
    private readonly Queue<(Vector2i Tile, int StepsLeft)> _flameSpreadQueue = new();

    /// <param name="Temperature">The hottest flame that reached the tile.</param>
    /// <param name="StepsLeft">
    ///     The most steps any flame at least as hot as the one now spreading had left on reaching it. Since flames spread
    ///         hottest first, every flame that reached it is at least as hot.
    /// </param>
    private record struct SpreadFlame(float Temperature, int StepsLeft);

    private void UpdateFlameSpread(TimeSpan curTime)
    {
        foreach (var tileFlames in _spreadFlames.Values)
        {
            tileFlames.Clear();
            _spreadFlamePool.Push(tileFlames);
        }

        _spreadFlames.Clear();

        // At most two intervals' worth, so a long pause (a paused or lagging server) doesn't send it across a whole spill
        var elapsed = _lastFlameSpreadUpdate == TimeSpan.MinValue
            ? _tileEffectUpdateInterval
            : curTime - _lastFlameSpreadUpdate;
        if (elapsed > _tileEffectUpdateInterval * 2)
            elapsed = _tileEffectUpdateInterval * 2;

        _lastFlameSpreadUpdate = curTime;

        if (_flameSpreadRate <= 0f)
        {
            _flameSpreadProgress = 0f;
            return;
        }

        _flameSpreadProgress += _flameSpreadRate * (float)elapsed.TotalSeconds;
        var steps = (int)MathF.Floor(_flameSpreadProgress);
        _flameSpreadProgress -= steps;

        foreach (var (gridUid, tilePuddles) in _gridPuddleCache)
        {
            if (!_chemicalFireGridQuery.TryComp(gridUid, out var chemicalFireGridComponent) ||
                chemicalFireGridComponent.Tiles.Count == 0)
                continue;

            _burningTiles.Clear();
            foreach (var (burningTile, tileFires) in chemicalFireGridComponent.Tiles)
            {
                var flameTemperature = 0f;
                foreach (var fire in tileFires.Fires.Values)
                    flameTemperature = MathF.Max(flameTemperature, fire.Comp.Temperature);

                if (flameTemperature > 0f)
                    _burningTiles.Add((burningTile, flameTemperature));
            }

            if (_burningTiles.Count == 0)
                continue;

            _burningTiles.Sort(HottestFirst);

            if (!_spreadFlamePool.TryPop(out var tileFlames))
                tileFlames = new Dictionary<Vector2i, SpreadFlame>();

            _spreadFlames[gridUid] = tileFlames;

            // An update that earned no whole tile moves the flame nowhere new, but burning tiles still light each other.
            //      Without that, a puddle the flame lit while still cold would go out before the flame next moved.
            if (steps == 0)
            {
                HoldFlamesBetweenBurningTiles(chemicalFireGridComponent, tilePuddles, tileFlames);
                continue;
            }

            foreach (var (burningTile, flameTemperature) in _burningTiles)
                SpreadFlameFrom(burningTile, flameTemperature, steps, tilePuddles, tileFlames);
        }
    }

    private void SpreadFlameFrom(Vector2i burningTile, float flameTemperature, int steps,
        Dictionary<Vector2i, Entity<PuddleComponent>> tilePuddles, Dictionary<Vector2i, SpreadFlame> tileFlames)
    {
        _flameSpreadQueue.Clear();
        _flameSpreadQueue.Enqueue((burningTile, steps));

        while (_flameSpreadQueue.TryDequeue(out var entry))
        {
            if (entry.StepsLeft <= 0)
                continue;

            var stepsLeft = entry.StepsLeft - 1;
            foreach (var offset in FlameSpreadOffsets)
            {
                var tile = entry.Tile + offset;

                // Already reached by a flame at least as hot (all earlier ones are) with as many steps left or more:
                //      whatever this one could reach from here, that one already has.
                var reached = tileFlames.TryGetValue(tile, out var spreadFlame);
                if (reached && spreadFlame.StepsLeft >= stepsLeft)
                    continue;

                // A flame at least as hot has lit it already, so it can be lit; otherwise check this one can.
                if (!reached &&
                    (!tilePuddles.TryGetValue(tile, out var puddle) ||
                     !_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution) ||
                     !_tileEffectSystem.CanBeLitByFlame(solution, flameTemperature)))
                    continue;

                tileFlames[tile] = new SpreadFlame(reached ? spreadFlame.Temperature : flameTemperature, stepsLeft);
                _flameSpreadQueue.Enqueue((tile, stepsLeft));
            }
        }
    }

    /// <summary>
    ///     Each burning tile lights the burning tiles next to it, and nothing else: what one step of spread does, minus
    ///         reaching any tile not already alight.
    /// </summary>
    private void HoldFlamesBetweenBurningTiles(ChemicalFireGridComponent chemicalFireGridComponent,
        Dictionary<Vector2i, Entity<PuddleComponent>> tilePuddles, Dictionary<Vector2i, SpreadFlame> tileFlames)
    {
        // Hottest first, so each tile keeps the hottest flame next to it
        foreach (var (burningTile, flameTemperature) in _burningTiles)
        {
            foreach (var offset in FlameSpreadOffsets)
            {
                var tile = burningTile + offset;
                if (tileFlames.ContainsKey(tile) ||
                    !chemicalFireGridComponent.Tiles.ContainsKey(tile) ||
                    !tilePuddles.TryGetValue(tile, out var puddle) ||
                    !_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution) ||
                    !_tileEffectSystem.CanBeLitByFlame(solution, flameTemperature))
                    continue;

                tileFlames[tile] = new SpreadFlame(flameTemperature, StepsLeft: 0);
            }
        }
    }

    /// <summary>
    ///     The temperature of flame reaching this puddle from a burning tile nearby, if any does.
    /// </summary>
    private float? GetSpreadFlameTemperature(EntityUid puddleUid)
    {
        if (!_puddleCacheLocations.TryGetValue(puddleUid, out var location) ||
            !_spreadFlames.TryGetValue(location.GridUid, out var tileFlames))
            return null;

        return tileFlames.TryGetValue(location.Indices, out var spreadFlame) ? spreadFlame.Temperature : null;
    }
}
