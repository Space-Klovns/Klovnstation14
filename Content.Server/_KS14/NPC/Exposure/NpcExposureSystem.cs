using System.Numerics;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Exposure;

/// <summary>
///     How exposed a spot is to a threat - not just to where the threat stands, but to where it could be a few steps
///         from now. A spot just round a corner is out of a hostile's sight, and back in it the moment the hostile
///         takes a step; good cover stays hidden while it comes on. So the threat's approach is sampled as probes:
///         where it is, and floor it could walk to within a few steps, spread out. A spot's exposure is the share of
///         them that can see it.
/// </summary>
/// <remarks>
///     For on-demand use while planning (<c>TacticalPositionOperator</c>), not kept up to date: work out the probes
///         once, with <see cref="GetApproachProbes"/>, then score as many spots against them as needed.
/// </remarks>
public sealed partial class NpcExposureSystem : EntitySystem
{
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    private readonly Dictionary<Vector2i, int> _floodSteps = new();
    private readonly List<(Vector2i Tile, int Steps)> _floodTiles = new();

    /// <summary>
    ///     Fills <paramref name="probes"/> with where a threat at <paramref name="threatCoordinates"/> could see from:
    ///         where it is, and up to <paramref name="maxProbes"/> - 1 tiles of floor it could walk to within
    ///         <paramref name="reach"/> steps, through doors, as <paramref name="walkerUid"/> would walk, spread evenly
    ///         from near to far. Off a grid, only where it is.
    /// </summary>
    public void GetApproachProbes(EntityUid walkerUid,
        EntityCoordinates threatCoordinates,
        int reach,
        int maxProbes,
        List<MapCoordinates> probes)
    {
        probes.Clear();

        var threatMapCoordinates = _transformSystem.ToMapCoordinates(threatCoordinates);
        probes.Add(threatMapCoordinates);

        if (maxProbes <= 1 ||
            reach <= 0 ||
            _transformSystem.GetGrid(threatCoordinates) is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);
        var threatTile = _mapSystem.TileIndicesFor(grid, threatCoordinates);

        _floodSteps.Clear();
        _npcSquadCoverSystem.FloodTiles(walkerUid, grid, threatTile, reach, _floodSteps, stopAtDoors: false);
        _floodSteps.Remove(threatTile); // already in, at the threat's exact position

        _floodTiles.Clear();
        foreach (var (tile, steps) in _floodSteps)
        {
            _floodTiles.Add((tile, steps));
        }

        // Near to far, and in a fixed order within a step, so the same approach gives the same probes.
        _floodTiles.Sort((a, b) => a.Steps != b.Steps
            ? a.Steps.CompareTo(b.Steps)
            : a.Tile.X != b.Tile.X ? a.Tile.X.CompareTo(b.Tile.X) : a.Tile.Y.CompareTo(b.Tile.Y));

        var wanted = Math.Min(maxProbes - 1, _floodTiles.Count);
        var tileSize = (float) mapGridComponent.TileSize;

        for (var i = 0; i < wanted; i++)
        {
            // Evenly through the list, ending on the furthest.
            var index = (int) ((i + 1) * (long) _floodTiles.Count / wanted) - 1;
            var tile = _floodTiles[index].Tile;
            var center = new EntityCoordinates(gridUid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * tileSize);
            probes.Add(_transformSystem.ToMapCoordinates(center));
        }
    }

    /// <summary>
    ///     The share of <paramref name="probes"/>, from 0 to 1, with a clear line to <paramref name="position"/> within
    ///         <paramref name="range"/>. 0 with no probes.
    /// </summary>
    public float GetExposure(MapCoordinates position, List<MapCoordinates> probes, float range)
    {
        if (probes.Count == 0)
            return 0f;

        var seen = 0;
        foreach (var probe in probes)
        {
            if (_npcLineOfSightSystem.InLineOfSight(probe, position, range))
                seen++;
        }

        return seen / (float) probes.Count;
    }
}
