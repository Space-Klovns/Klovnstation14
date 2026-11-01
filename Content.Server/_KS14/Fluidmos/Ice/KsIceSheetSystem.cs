using Content.Server._KS14.TileEffects;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Ice;
using Content.Shared._KS14.Temperature;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Fluidmos.Ice;

/// <summary>
///     Ice sheets: what freezes out of a tile's puddle, one sheet per tile. They exchange heat with the air (and the
///         puddle under them) in the puddle heat pass, melt back into the puddle above their reagents' melting points,
///         and sublime into the tile's gas in thin air.
/// </summary>
/// <remarks>
///     Sheets are cached per grid and tile, as puddles are, so nothing ever looks entities up per tick, and tiles without
///         ice cost nothing. Everything that spawns, spills or deletes is queued and done outside the walks.
/// </remarks>
public sealed partial class KsIceSheetSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private KsTileEffectAtmosSystem _tileEffectAtmosSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;

    private static readonly EntProtoId IceSheetPrototype = "KsIceSheet";

    /// <summary>
    ///     Volume, in units, below which a sheet is only frost: nobody slips on it.
    /// </summary>
    public const float SlipThresholdUnits = 2f;

    /// <summary>
    ///     Volume, in units, at which a sheet is drawn fully opaque.
    /// </summary>
    private const float OpaqueUnits = 30f;

    private const float MinimumAlpha = 0.25f;
    private const float MaximumAlpha = 0.85f;

    /// <summary>
    ///     Thermal conductance between a sheet and a puddle on the same tile, in W/K: high, as they are in contact.
    /// </summary>
    private const float PuddleConductance = 60f;

    /// <summary>
    ///     The share of a sheet's heat capacity each of its two contacts, the air and the puddle, works with. Both start
    ///         from the sheet's temperature and are applied together, so with the whole capacity in each, a thin sheet
    ///         between a colder air and puddle would be carried past both.
    /// </summary>
    private const float ContactCapacityShare = 0.5f;

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, Entity<KsIceSheetComponent>>> _gridIceCache = new();

    /// <summary>
    ///     Ice waiting to be added to its tile's sheet, drained in <see cref="Update"/>.
    /// </summary>
    private readonly List<(EntityUid GridUid, Vector2i Tile, Solution Ice)> _pendingIce = new();

    /// <summary>
    ///     Heat to put into solutions, gathered while walking the cache and applied after it.
    /// </summary>
    private readonly List<(Entity<SolutionComponent> Solution, float Heat)> _pendingHeat = new();

    private readonly List<Entity<KsIceSheetComponent>> _sheets = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingIce.Count == 0)
            return;

        var count = _pendingIce.Count;
        for (var i = 0; i < count; i++)
        {
            var (gridUid, tile, ice) = _pendingIce[i];
            AddIceNow(gridUid, tile, ice);
        }

        _pendingIce.RemoveRange(0, count);
    }

    /// <summary>
    ///     Queues frozen reagents for the sheet on the tile at <paramref name="coordinates"/>, made if there is none.
    /// </summary>
    public void AddIce(EntityCoordinates coordinates, Solution ice)
    {
        if (ice.Volume <= FixedPoint2.Zero ||
            _transformSystem.GetGrid(coordinates) is not { } gridUid ||
            !_gridQuery.TryComp(gridUid, out var gridComponent))
            return;

        _pendingIce.Add((gridUid, _mapSystem.TileIndicesFor(gridUid, gridComponent, coordinates), ice));
    }

    public bool TryGetIceSheet(EntityUid gridUid, Vector2i indices, out Entity<KsIceSheetComponent> sheet)
    {
        if (_gridIceCache.TryGetValue(gridUid, out var tiles) && tiles.TryGetValue(indices, out sheet))
            return true;

        sheet = default;
        return false;
    }

    /// <summary>
    ///     The sheet on a tile, spawned if it has none.
    /// </summary>
    public Entity<KsIceSheetComponent>? ResolveIceSheet(EntityUid gridUid, Vector2i indices)
    {
        if (TryGetIceSheet(gridUid, indices, out var sheet))
            return sheet;

        if (!_gridQuery.TryComp(gridUid, out var gridComponent))
            return null;

        var sheetUid = Spawn(IceSheetPrototype, _mapSystem.GridTileToLocal(gridUid, gridComponent, indices));
        return TryComp<KsIceSheetComponent>(sheetUid, out var sheetComponent) ? (sheetUid, sheetComponent) : null;
    }

    private void AddIceNow(EntityUid gridUid, Vector2i tile, Solution ice)
    {
        if (ResolveIceSheet(gridUid, tile) is not { } sheet ||
            !_solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out var solutionEntity, out _))
            return;

        // Merges the temperature by heat capacity.
        _solutionContainerSystem.TryAddSolution(solutionEntity.Value, ice);
        UpdateSheet(sheet);
    }

    [SubscribeLocalEvent]
    private void OnFrozen(ref KsTileEffectFreezeEvent args)
    {
        if (args.Units <= 0f)
            return;

        var ice = new Solution();
        ice.AddReagent(args.Reagent, FixedPoint2.New(args.Units));
        ice.Temperature = args.Temperature;
        _pendingIce.Add((args.GridUid, args.Tile, ice));
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsIceSheetComponent> entity, ref ComponentStartup args)
    {
        CacheSheet(entity);
        UpdateSheet(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsIceSheetComponent> entity, ref ComponentShutdown args)
    {
        UncacheSheet(entity);
    }

    [SubscribeLocalEvent]
    private void OnAnchorChanged(Entity<KsIceSheetComponent> entity, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            CacheSheet(entity);
        else
            UncacheSheet(entity);
    }

    private void CacheSheet(Entity<KsIceSheetComponent> entity)
    {
        var transformComponent = Transform(entity);
        if (!transformComponent.Anchored ||
            transformComponent.GridUid is not { } gridUid ||
            !_gridQuery.TryComp(gridUid, out var gridComponent))
            return;

        if (!_gridIceCache.TryGetValue(gridUid, out var tiles))
            _gridIceCache[gridUid] = tiles = new Dictionary<Vector2i, Entity<KsIceSheetComponent>>();

        tiles[_mapSystem.TileIndicesFor(gridUid, gridComponent, transformComponent.Coordinates)] = entity;
    }

    private void UncacheSheet(Entity<KsIceSheetComponent> entity)
    {
        foreach (var (gridUid, tiles) in _gridIceCache)
        {
            foreach (var (indices, cached) in tiles)
            {
                if (cached.Owner != entity.Owner)
                    continue;

                tiles.Remove(indices);
                if (tiles.Count == 0)
                    _gridIceCache.Remove(gridUid);

                return;
            }
        }
    }

    /// <summary>
    ///     One pass of the ice sheets' heat exchange and phase changes, from <see cref="PuddleSystem"/>'s air heat
    ///         exchange, after it has finished with the puddles.
    /// </summary>
    public void UpdateHeatExchange(float deltaTime, float conductance, float referenceVolume, float emissivity)
    {
        if (_gridIceCache.Count == 0 || deltaTime <= 0f)
            return;

        _pendingHeat.Clear();
        _sheets.Clear();

        foreach (var (gridUid, tiles) in _gridIceCache)
        {
            foreach (var (indices, sheet) in tiles)
            {
                _sheets.Add(sheet);
                ExchangeHeat(sheet, gridUid, indices, deltaTime, conductance, referenceVolume, emissivity);
            }
        }

        foreach (var (solutionEntity, heat) in _pendingHeat)
        {
            if (!TerminatingOrDeleted(solutionEntity.Owner))
                _solutionContainerSystem.AddThermalEnergy(solutionEntity, heat);
        }

        _pendingHeat.Clear();

        // The cache is no longer being walked, so melting may spill and empty sheets may go.
        foreach (var sheet in _sheets)
        {
            if (!TerminatingOrDeleted(sheet.Owner))
                StepPhases(sheet, deltaTime, referenceVolume);
        }

        _sheets.Clear();
    }

    private void ExchangeHeat(
        Entity<KsIceSheetComponent> sheet,
        EntityUid gridUid,
        Vector2i indices,
        float deltaTime,
        float conductance,
        float referenceVolume,
        float emissivity)
    {
        if (!_solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out var solutionEntity, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
            return;

        var iceCapacity = solution.GetHeatCapacity(ProtoMan);
        if (iceCapacity <= 0f)
            return;

        var iceTemperature = solution.Temperature;
        var iceHeat = 0f;

        if (_atmosphereSystem.GetTileMixture((gridUid, null, null), null, indices, excite: false) is { Immutable: false } mixture)
        {
            var airCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
            if (airCapacity >= Atmospherics.MinimumHeatCapacity)
            {
                var coverage = MathF.Min(1f, (float)solution.Volume / referenceVolume);
                var airConductance = conductance * coverage +
                                     KsThermalTransfer.RadiativeConductance(iceTemperature, mixture.Temperature, coverage, emissivity);
                var heat = KsThermalTransfer.ExchangeQuery(iceCapacity * ContactCapacityShare, iceTemperature, airCapacity,
                    mixture.Temperature, airConductance, deltaTime);
                mixture.Temperature -= heat / airCapacity;
                iceHeat += heat;
            }
        }

        // In contact with the puddle on the same tile.
        if (_puddleSystem.TryGetCachedPuddle(gridUid, indices, out var puddle) &&
            puddle.Comp.Solution is { } puddleSolution &&
            puddleSolution.Comp.Solution.Volume > FixedPoint2.Zero)
        {
            var puddleCapacity = puddleSolution.Comp.Solution.GetHeatCapacity(ProtoMan);
            if (puddleCapacity > 0f)
            {
                var heat = KsThermalTransfer.ExchangeQuery(iceCapacity * ContactCapacityShare, iceTemperature, puddleCapacity,
                    puddleSolution.Comp.Solution.Temperature, PuddleConductance, deltaTime);
                iceHeat += heat;
                _pendingHeat.Add((puddleSolution, -heat));
            }
        }

        if (iceHeat != 0f)
            _pendingHeat.Add((solutionEntity.Value, iceHeat));
    }

    /// <summary>
    ///     Melts what is above its melting point into the tile's puddle, and sublimes linked reagents into thin air.
    /// </summary>
    private void StepPhases(Entity<KsIceSheetComponent> sheet, float deltaTime, float referenceVolume)
    {
        if (!_solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out var solutionEntity, out var solution))
            return;

        var transformComponent = Transform(sheet);
        var coordinates = transformComponent.Coordinates;
        var melted = new Solution();
        var changed = false;

        for (var i = solution.Contents.Count - 1; i >= 0; i--)
        {
            var (reagent, quantity) = solution.Contents[i];
            if (!_propertiesSystem.TryGetProperties(reagent.Prototype, out var properties))
                continue;

            var heatCapacity = solution.GetHeatCapacity(ProtoMan);
            var temperature = solution.Temperature;
            var specificHeat = properties.SpecificHeat;

            if (properties.Freezable && properties.FusionHeat > 0f && temperature > properties.MeltingPoint)
            {
                // Melting u units takes their heat of fusion; at most enough to bring the sheet down to the melting point.
                var units = (float)FixedPoint2.New(MathF.Min((float)quantity,
                    heatCapacity * (temperature - properties.MeltingPoint) / properties.FusionHeat));
                if (units <= 0f)
                    continue;

                var removed = (float)solution.RemoveReagent(reagent, FixedPoint2.New(units));
                melted.AddReagent(reagent, FixedPoint2.New(removed));
                SetTemperatureAfterRemoving(solution, heatCapacity, temperature,
                    removed * (specificHeat * properties.MeltingPoint + properties.FusionHeat), removed * specificHeat);
                changed = true;
            }
            else if (properties.Linked && properties.Volatile && transformComponent.GridUid is { } gridUid)
            {
                var removed = Sublime(solution, reagent, (float)quantity, properties, gridUid, coordinates, deltaTime, referenceVolume);
                if (removed <= 0f)
                    continue;

                SetTemperatureAfterRemoving(solution, heatCapacity, temperature,
                    removed * (specificHeat * temperature + properties.LatentHeat + properties.FusionHeat), removed * specificHeat);
                changed = true;
            }
        }

        if (melted.Volume > FixedPoint2.Zero)
        {
            melted.Temperature = solution.Temperature;
            _puddleSystem.TrySpillAt(coordinates, melted, out _, sound: false);
        }

        if (!changed)
            return;

        _solutionContainerSystem.UpdateChemicals(solutionEntity.Value);

        if (solution.Volume <= FixedPoint2.Zero)
        {
            QueueDel(sheet);
            return;
        }

        UpdateSheet(sheet);
    }

    /// <summary>
    ///     Linked ice in thin air sublimes straight into the tile's gas, at the Hertz–Knudsen rate towards saturation.
    /// </summary>
    /// <returns>Units removed from the sheet.</returns>
    private float Sublime(
        Solution solution,
        ReagentId reagent,
        float available,
        KsFluidReagentProperties properties,
        EntityUid gridUid,
        EntityCoordinates coordinates,
        float deltaTime,
        float referenceVolume)
    {
        if (!_gridQuery.TryComp(gridUid, out var gridComponent))
            return 0f;

        var indices = _mapSystem.TileIndicesFor(gridUid, gridComponent, coordinates);
        if (_atmosphereSystem.GetTileMixture((gridUid, null, null), null, indices, excite: false) is not { Immutable: false } air ||
            air.Volume <= 0f || air.Temperature <= 0f)
            return 0f;

        var gas = (Gas)properties.GasIndex;
        var saturationPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, solution.Temperature);
        var partialPressure = air.GetMoles(gas) * Atmospherics.R * air.Temperature / air.Volume;
        if (saturationPressure <= partialPressure)
            return 0f;

        var coverage = MathF.Min(1f, (float)solution.Volume / referenceVolume);
        var flux = KsPhaseMath.HertzKnudsenFlux(KsFluidmos.PuddleHertzKnudsenAlpha, saturationPressure - partialPressure,
            properties.InverseSqrtTwoPiMR, solution.Temperature);
        var gapMoles = (saturationPressure - partialPressure) * air.Volume / (Atmospherics.R * air.Temperature);
        var moles = MathF.Min(flux * coverage * deltaTime, gapMoles);

        var units = (float)FixedPoint2.New(MathF.Min(moles / properties.MolesPerUnit, available));
        if (units <= 0f)
            return 0f;

        if (!_tileEffectAtmosSystem.TryAddGas(gridUid, indices, gas, units * properties.MolesPerUnit,
                sensibleEnergy: units * properties.SpecificHeat * solution.Temperature))
            return 0f;

        return (float)solution.RemoveReagent(reagent, FixedPoint2.New(units));
    }

    /// <summary>
    ///     Sets a solution's temperature after units left it carrying <paramref name="energyRemoved"/> joules.
    /// </summary>
    private static void SetTemperatureAfterRemoving(Solution solution, float heatCapacity, float temperature, float energyRemoved, float heatCapacityRemoved)
    {
        var remaining = heatCapacity - heatCapacityRemoved;
        if (remaining > 0f)
            solution.Temperature = MathF.Max((heatCapacity * temperature - energyRemoved) / remaining, Atmospherics.TCMB);
    }

    /// <summary>
    ///     Tints the sheet by its solution and makes it slippery once it is more than frost. Only touches physics when the
    ///         slipperiness actually changes.
    /// </summary>
    private void UpdateSheet(Entity<KsIceSheetComponent> sheet)
    {
        if (!_solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out _, out var solution))
            return;

        var volume = (float)solution.Volume;
        var color = solution.GetColor(ProtoMan);
        var alpha = MinimumAlpha + (MaximumAlpha - MinimumAlpha) * MathF.Min(1f, volume / OpaqueUnits);
        _appearanceSystem.SetData(sheet, KsIceSheetVisuals.Color, color.WithAlpha(alpha));

        var slippery = volume >= SlipThresholdUnits;
        if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(sheet, out var physicsComponent) &&
            physicsComponent.CanCollide != slippery)
            _physicsSystem.SetCanCollide(sheet, slippery, body: physicsComponent);
    }
}
