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
///         flame can light. Anything else (water, foam, bare floor) stops it.
/// </remarks>
public sealed partial class PuddleSystem
{
    [Dependency] private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;

    /// <summary>
    ///     <see cref="Content.Shared._KS14.CCVar.KsCCVars.AtmosFlameSpreadRate"/>.
    /// </summary>
    private float _flameSpreadRate;

    private static readonly Vector2i[] FlameSpreadOffsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    /// <summary>
    ///     The flame temperature reaching each tile from burning tiles nearby, per grid, worked out at the start of each
    ///         tile-effect update. Only grids with fires on them are in it.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, float>> _spreadFlames = new();

    private readonly Queue<(Vector2i Tile, int Depth)> _flameSpreadQueue = new();

    private void UpdateFlameSpread()
    {
        _spreadFlames.Clear();
        if (_flameSpreadRate <= 0f)
            return;

        var steps = Math.Max(1, (int)MathF.Round(_flameSpreadRate * (float)TileEffectUpdateInterval.TotalSeconds));

        foreach (var (gridUid, tilePuddles) in _gridPuddleCache)
        {
            if (!_chemicalFireGridQuery.TryComp(gridUid, out var chemicalFireGridComponent) ||
                chemicalFireGridComponent.Tiles.Count == 0)
                continue;

            var tileFlames = new Dictionary<Vector2i, float>();
            _spreadFlames[gridUid] = tileFlames;

            foreach (var (burningTile, tileFires) in chemicalFireGridComponent.Tiles)
            {
                var flameTemperature = 0f;
                foreach (var fire in tileFires.Fires.Values)
                    flameTemperature = MathF.Max(flameTemperature, fire.Comp.Temperature);

                if (flameTemperature <= 0f)
                    continue;

                SpreadFlameFrom(burningTile, flameTemperature, steps, tilePuddles, tileFlames);
            }
        }
    }

    private void SpreadFlameFrom(Vector2i burningTile, float flameTemperature, int steps,
        Dictionary<Vector2i, Entity<PuddleComponent>> tilePuddles, Dictionary<Vector2i, float> tileFlames)
    {
        _flameSpreadQueue.Clear();
        _flameSpreadQueue.Enqueue((burningTile, 0));

        while (_flameSpreadQueue.TryDequeue(out var entry))
        {
            if (entry.Depth >= steps)
                continue;

            foreach (var offset in FlameSpreadOffsets)
            {
                var tile = entry.Tile + offset;

                // Already reached by a flame at least as hot: whatever lies beyond it has been, too.
                if (tileFlames.TryGetValue(tile, out var reached) && reached >= flameTemperature)
                    continue;

                if (!tilePuddles.TryGetValue(tile, out var puddle) ||
                    !_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution) ||
                    !_tileEffectSystem.CanBeLitByFlame(solution, flameTemperature))
                    continue;

                tileFlames[tile] = flameTemperature;
                _flameSpreadQueue.Enqueue((tile, entry.Depth + 1));
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

        return tileFlames.TryGetValue(location.Indices, out var flameTemperature) ? flameTemperature : null;
    }
}
