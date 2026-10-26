using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>
    /// Revalidate a complete private paused grid, using the normal tile/adjacency/mixture hooks.
    /// The caller owns the stage transaction and retires it on failure. This never processes other
    /// grids, advances simulation, replaces map atmosphere, or makes a stable-at-rest claim.
    /// </summary>
    internal string? KsPrepareProcgenGas(EntityUid gridUid, IReadOnlyList<Vector2i> terrainCells,
        out IReadOnlyList<KsProcgenAtmosCellState> states)
    {
        states = [];
        var issue = KsResolveProcgenGrid(gridUid, terrainCells, out var entity, createAtmosphere: true);
        if (issue != null) return issue;
        var atmosphere = entity.Comp1;
        var mapUid = entity.Comp4.MapUid;
        if (atmosphere.ProcessingPaused || atmosphere.CurrentRunInvalidatedTiles.Count != 0)
            return "OwnedAtmosProcessingInProgress";
        var halo = new HashSet<Vector2i>(terrainCells);
        foreach (var cell in terrainCells)
        for (var index = 0; index < Atmospherics.Directions; index++)
            halo.Add(cell.Offset((AtmosDirection) (1 << index)));
        TryComp(entity.Comp4.MapUid, out MapAtmosphereComponent? mapAtmosphere);
        // Populate every neighbor's data before either endpoint derives adjacency.
        foreach (var cell in halo.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
            UpdateTileData(entity, mapAtmosphere, GetOrNewTile(gridUid, atmosphere, cell, invalidateNew: false));
        foreach (var cell in halo.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            var tile = atmosphere.Tiles[cell];
            UpdateAdjacentTiles(entity, tile, activate: true);
            UpdateTileAir(entity, tile, GetVolumeForTiles(entity.Comp3));
            if (!KsProcgenFrameMatches(entity, mapUid)) return "OwnedAtmosFrameCallbackDrift";
            InvalidateVisuals(entity, tile);
            atmosphere.InvalidatedCoords.Remove(cell);
        }
        return KsReadProcgenGas(gridUid, terrainCells, out states);
    }

    internal string? KsWriteProcgenGas(EntityUid gridUid, IReadOnlyList<Vector2i> terrainCells,
        IReadOnlyList<KsProcgenAtmosWrite> writes, out IReadOnlyList<KsProcgenAtmosCellState> states)
    {
        states = [];
        var issue = KsResolveProcgenGrid(gridUid, terrainCells, out var entity);
        if (issue != null) return issue;
        if (writes == null || writes.Count > terrainCells.Count || writes.Any(write => write == null) ||
            writes.Select(write => write.Cell).Distinct().Count() != writes.Count)
            return "InvalidOwnedAtmosWrites";
        var allowed = terrainCells.ToHashSet();
        var mapUid = entity.Comp4.MapUid;
        var mixtures = new Dictionary<Vector2i, GasMixture>();
        foreach (var write in writes)
        {
            if (!allowed.Contains(write.Cell) || !entity.Comp1.Tiles.TryGetValue(write.Cell, out var tile) ||
                tile.NoGridTile || tile.MapAtmosphere || tile.Air is not { Immutable: false } air ||
                air.Volume != GetVolumeForTiles(entity.Comp3)) return "OwnedAtmosMixtureRequired";
            if (!KsProcgenAtmosphereMixturePlanner.TryPlan(write.Profile, air.Volume, out var planned, out var mixtureIssue))
                return mixtureIssue!.Code;
            var replacement = new GasMixture(volume: air.Volume) { Temperature = planned!.TemperatureKelvin };
            foreach (var gas in planned.Gases)
            {
                if (!Enum.TryParse<Gas>(gas.GasId, out var gasId) || !Enum.IsDefined(gasId)) return "UnknownOwnedAtmosGas";
                replacement.SetMoles(gasId, gas.Moles);
            }
            mixtures.Add(write.Cell, replacement);
        }
        // All numeric and ownership checks finish before the first write. Each tile receives an
        // independent mixture, including when old mixtures were aliased by an initializer.
        foreach (var (cell, mixture) in mixtures)
        {
            var tile = entity.Comp1.Tiles[cell];
            tile.Air = mixture;
            tile.AirArchived = null;
            tile.ArchivedCycle = 0;
            tile.LastShare = 0f;
            NotifyDeviceTileChanged((gridUid, entity.Comp1, entity.Comp3), cell);
            if (!KsProcgenFrameMatches(entity, mapUid)) return "OwnedAtmosFrameCallbackDrift";
            AddActiveTile(entity.Comp1, tile);
            InvalidateVisuals(entity, tile);
        }
        if (KsResolveProcgenGrid(gridUid, terrainCells, out entity) is { } changed) return changed;
        foreach (var (cell, mixture) in mixtures)
            if (!entity.Comp1.Tiles.TryGetValue(cell, out var tile) || !ReferenceEquals(tile.Air, mixture))
                return "OwnedAtmosWriteCallbackDrift";
        return KsReadProcgenGas(gridUid, terrainCells, out states);
    }

    private bool KsProcgenFrameMatches(Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> entity,
        EntityUid? mapUid) => mapUid != null &&
        TryComp<GridAtmosphereComponent>(entity.Owner, out var atmosphere) && ReferenceEquals(atmosphere, entity.Comp1) &&
        TryComp(entity.Owner, out TransformComponent? transform) && ReferenceEquals(transform, entity.Comp4) &&
        transform.MapUid == mapUid && transform.ParentUid == mapUid && _map.IsPaused((mapUid.Value, null));

    /// <summary>Read without creating, revalidating or refilling mixtures on repeated initialization.</summary>
    internal string? KsReadProcgenGas(EntityUid gridUid, IReadOnlyList<Vector2i> terrainCells,
        out IReadOnlyList<KsProcgenAtmosCellState> states)
    {
        states = [];
        if (KsResolveProcgenGrid(gridUid, terrainCells, out var entity) is { } issue) return issue;
        foreach (var cell in terrainCells)
        {
            if (!entity.Comp1.Tiles.TryGetValue(cell, out var tile)) return "OwnedAtmosMissingTile";
            var data = GetAirtightData(gridUid, entity.Comp3, cell);
            if (data.BlockedDirections != tile.AirtightData.BlockedDirections || data.NoAirWhenBlocked != tile.AirtightData.NoAirWhenBlocked)
                return "OwnedAtmosBoundaryDrift";
            var open = AtmosDirection.Invalid;
            for (var index = 0; index < Atmospherics.Directions; index++)
            {
                var direction = (AtmosDirection) (1 << index);
                var adjacent = GetAirtightData(gridUid, entity.Comp3, cell.Offset(direction));
                if ((data.BlockedDirections & direction) == 0 && (adjacent.BlockedDirections & direction.GetOpposite()) == 0)
                    open |= direction;
            }
            if (open != tile.AdjacentBits) return "OwnedAtmosBoundaryDrift";
        }
        return KsObserveProcgenGas(entity, terrainCells, out states);
    }

    private string? KsResolveProcgenGrid(EntityUid gridUid, IReadOnlyList<Vector2i> terrainCells,
        out Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> entity,
        bool createAtmosphere = false)
    {
        entity = default;
        if (terrainCells == null || terrainCells.Count is < 1 or > 16_384 ||
            terrainCells.Distinct().Count() != terrainCells.Count ||
            terrainCells.Any(cell => cell.X is < -1_000_000 or > 1_000_000 || cell.Y is < -1_000_000 or > 1_000_000))
            return "InvalidOwnedAtmosTerrain";
        if (!TryComp<MapGridComponent>(gridUid, out var grid) || grid.TileSize != 1 ||
            !TryComp(gridUid, out TransformComponent? transform) || transform.MapUid == null ||
            !TryComp<MapComponent>(transform.MapUid.Value, out var map) || !_map.IsPaused((transform.MapUid.Value, map)) ||
            !_map.IsInitialized(transform.MapUid.Value) || _map.GetFilledTileCount((gridUid, grid)) != terrainCells.Count)
            return "OwnedPausedAtmosGridRequired";
        foreach (var cell in terrainCells)
        {
            if (!_map.TryGetTile(grid, cell, out var tile) || tile.IsEmpty ||
                ((ContentTileDefinition) _tileDefinitionManager[tile.TypeId]).MapAtmosphere)
                return "OwnedAtmosTerrainMismatch";
        }
        // AutomaticAtmosSystem has a minimum mass threshold. Explicit owned 1x1/1x3 requests
        // still need a real grid atmosphere, created only after complete terrain/frame checks.
        if (createAtmosphere) EnsureComp<GridAtmosphereComponent>(gridUid);
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var atmosphere) ||
            !TryComp<GasTileOverlayComponent>(gridUid, out var overlay)) return "OwnedAtmosComponentsRequired";
        entity = (gridUid, atmosphere, overlay, grid, transform);
        return null;
    }

    private string? KsObserveProcgenGas(Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> entity,
        IReadOnlyList<Vector2i> terrainCells, out IReadOnlyList<KsProcgenAtmosCellState> states)
    {
        states = [];
        var observed = new List<KsProcgenAtmosCellState>();
        foreach (var cell in terrainCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            if (!entity.Comp1.Tiles.TryGetValue(cell, out var tile) || tile.NoGridTile || tile.MapAtmosphere)
                return "OwnedAtmosTerrainMismatch";
            var air = tile.Air;
            if (air?.Immutable == true) return "OwnedAtmosMixtureRequired";
            if (air != null && (!float.IsFinite(air.Volume) || air.Volume != GetVolumeForTiles(entity.Comp3) ||
                    !float.IsFinite(air.Temperature) || !float.IsFinite(air.Pressure))) return "InvalidOwnedAtmosState";
            var amounts = new List<KsProcgenGasAmount>();
            if (air != null)
            for (var index = 0; index < Atmospherics.TotalNumberOfGases; index++)
            {
                var moles = air.GetMoles(index);
                if (!float.IsFinite(moles) || moles < 0f) return "InvalidOwnedAtmosState";
                if (moles != 0f) amounts.Add(new(((Gas) index).ToString(), moles));
            }
            observed.Add(new(cell, air != null, air == null, tile.AdjacentBits, air?.Volume ?? 0f,
                air?.Temperature ?? 0f, air?.Pressure ?? 0f, Array.AsReadOnly(amounts.ToArray())));
        }
        states = Array.AsReadOnly(observed.ToArray());
        return null;
    }
}
