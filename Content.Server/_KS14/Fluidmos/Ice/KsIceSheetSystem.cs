using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server._KS14.TileEffects;
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
using Content.Shared.Fluids.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Fluidmos.Ice;

/// <summary>
///     Ice sheets: what freezes out of a tile's puddle, one sheet per tile. They exchange heat with the air (and the
///         puddle under them) in the puddle heat pass, melt back into the puddle above their reagents' melting points,
///         sublime into the tile's gas in thin air, take in whatever of their puddle is supercooled, and waste away in
///         vacuum or under a map's air (<see cref="KsFluidDissipationSystem"/>).
/// </summary>
/// <remarks>
///     Sheets are cached per grid and tile, as puddles are, so nothing ever looks entities up per tick, and tiles without
///         ice cost nothing. Everything that spawns, spills or deletes is queued and done outside the walks.
/// </remarks>
public sealed partial class KsIceSheetSystem : EntitySystem
{
    [Dependency] private Content.Server._KS14.Atmos.KsAirHeatExchangeSystem _airHeatExchangeSystem = default!;
    [Dependency] private KsFluidDissipationSystem _fluidDissipationSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private KsTileEffectAtmosSystem _tileEffectAtmosSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private KsFoamBlanketSystem _foamBlanketSystem = default!;

    private static readonly EntProtoId IceSheetPrototype = "KsIceSheet";

    /// <summary>
    ///     The puddle a sheet is drawn as, whose overflow volume sets how full a sheet looks.
    /// </summary>
    private static readonly EntProtoId PuddlePrototype = "Puddle";

    /// <summary>
    ///     Volume, in units, below which a sheet is only frost: nobody slips on it.
    /// </summary>
    public const float SlipThresholdUnits = 2f;

    /// <summary>
    ///     Volume, in units, at which a sheet reaches <see cref="MaximumAlpha"/>.
    /// </summary>
    private const float OpaqueUnits = 30f;

    // Ice is clear: even a thick sheet shows the floor through it, tinted by what froze.
    private const float MinimumAlpha = 0.15f;
    private const float MaximumAlpha = 0.55f;

    /// <summary>
    ///     Thermal conductance between a sheet and a puddle on the same tile, in W/K: high, as they are in contact.
    /// </summary>
    private const float PuddleConductance = 60f;


    private readonly Dictionary<EntityUid, Dictionary<Vector2i, Entity<KsIceSheetComponent>>> _gridIceCache = new();

    /// <summary>
    ///     Ice waiting to be added to its tile's sheet, drained in <see cref="Update"/>.
    /// </summary>
    private readonly List<(EntityUid GridUid, Vector2i Tile, Solution Ice)> _pendingIce = new();

    /// <summary>
    ///     Heat to put into solutions, gathered while walking the cache and applied after it.
    /// </summary>
    private readonly List<(Entity<SolutionComponent> Solution, float Heat)> _pendingHeat = new();

    /// <summary>
    ///     The sheets walked this pass, where they lie, and how many units each is to lose to dissipation.
    /// </summary>
    private readonly List<(Entity<KsIceSheetComponent> Sheet, EntityUid GridUid, Vector2i Indices, float DissipatedUnits)> _sheets = new();

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
    public void UpdateHeatExchange(float deltaTime, float conductance, float referenceVolume)
    {
        if (_gridIceCache.Count == 0 || deltaTime <= 0f)
            return;

        _pendingHeat.Clear();
        _sheets.Clear();

        foreach (var (gridUid, tiles) in _gridIceCache)
        {
            foreach (var (indices, sheet) in tiles)
            {
                var dissipationRate = ExchangeHeat(sheet, gridUid, indices, deltaTime, conductance, referenceVolume);
                _sheets.Add((sheet, gridUid, indices, dissipationRate * deltaTime));
            }
        }

        foreach (var (solutionEntity, heat) in _pendingHeat)
        {
            if (!TerminatingOrDeleted(solutionEntity.Owner))
                _solutionContainerSystem.AddThermalEnergy(solutionEntity, heat);
        }

        _pendingHeat.Clear();

        // The cache is no longer being walked, so melting may spill and empty sheets may go.
        foreach (var (sheet, gridUid, indices, dissipatedUnits) in _sheets)
        {
            if (!TerminatingOrDeleted(sheet.Owner))
                StepPhases(sheet, gridUid, indices, deltaTime, referenceVolume, dissipatedUnits);
        }

        _sheets.Clear();
    }

    /// <returns>
    ///     Units per second the sheet dissipates where it lies (<see cref="KsFluidDissipationSystem.GetRate"/>), from the
    ///         same read of its tile's air.
    /// </returns>
    private float ExchangeHeat(
        Entity<KsIceSheetComponent> sheet,
        EntityUid gridUid,
        Vector2i indices,
        float deltaTime,
        float conductance,
        float referenceVolume)
    {
        if (!_solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out var solutionEntity, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
            return 0f;

        var iceCapacity = solution.GetHeatCapacity(ProtoMan);
        if (iceCapacity <= 0f)
            return 0f;

        var iceTemperature = solution.Temperature;
        var iceHeat = 0f;

        // Its air and its puddle each work with a share of its capacity, split between those it has: both start from the
        //     sheet's temperature and are applied together, so with the whole capacity in each, a thin sheet between a
        //     colder air and puddle would be carried past both.
        var hasPuddle = _puddleSystem.TryGetCachedPuddle(gridUid, indices, out var puddle) &&
                        puddle.Comp.Solution is { } contactSolution &&
                        contactSolution.Comp.Solution.Volume > FixedPoint2.Zero;
        var contacts = (_airHeatExchangeSystem.HasAir(gridUid, indices) ? 1 : 0) + (hasPuddle ? 1 : 0);
        var contactCapacity = iceCapacity / Math.Max(contacts, 1);

        var coverage = MathF.Min(1f, (float)solution.Volume / referenceVolume);
        iceHeat += _airHeatExchangeSystem.ExchangeWithTile(gridUid,
            indices,
            contactCapacity,
            iceTemperature,
            conductance * coverage,
            radiatingArea: coverage,
            deltaTime,
            out var mixture);

        // In contact with the puddle on the same tile.
        if (hasPuddle && puddle.Comp.Solution is { } puddleSolution)
        {
            var puddleCapacity = puddleSolution.Comp.Solution.GetHeatCapacity(ProtoMan);
            if (puddleCapacity > 0f)
            {
                var heat = KsThermalTransfer.ExchangeQuery(contactCapacity, iceTemperature, puddleCapacity,
                    puddleSolution.Comp.Solution.Temperature, PuddleConductance, deltaTime);
                iceHeat += heat;
                _pendingHeat.Add((puddleSolution, -heat));
            }
        }

        if (iceHeat != 0f)
            _pendingHeat.Add((solutionEntity.Value, iceHeat));

        // Nothing dissipates from under a foam blanket, as for the puddle beside it.
        if (_foamBlanketSystem.TryGetBlanket(gridUid, indices, out _))
            return 0f;

        return _fluidDissipationSystem.GetRate(mixture);
    }

    /// <summary>
    ///     Melts what is above its melting point into the tile's puddle, sublimes linked reagents into thin air, freezes
    ///         what of the puddle is supercooled onto the sheet, and dissipates <paramref name="dissipatedUnits"/>.
    /// </summary>
    private void StepPhases(
        Entity<KsIceSheetComponent> sheet,
        EntityUid gridUid,
        Vector2i indices,
        float deltaTime,
        float referenceVolume,
        float dissipatedUnits)
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

                // All of it melted: the melt holds the whole sheet's heat, less the fusion it took, which melting only as
                //     much as that pays for keeps at or above the melting point. The sheet's temperature is what it takes.
                if (solution.Volume <= FixedPoint2.Zero && removed > 0f)
                    solution.Temperature = (heatCapacity * temperature - removed * properties.FusionHeat) / (removed * specificHeat);

                changed = true;
            }
            else if (properties.Linked && properties.Volatile)
            {
                var removed = Sublime(solution, reagent, (float)quantity, properties, gridUid, indices, deltaTime, referenceVolume,
                    heatCapacity);
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
        // Not when it has just melted into the puddle, which that left above the melting point.
        else if (FreezeSupercooledPuddle(solution, gridUid, indices))
        {
            changed = true;
        }

        if (changed)
            _solutionContainerSystem.UpdateChemicals(solutionEntity.Value);

        if (dissipatedUnits > 0f && solution.Volume > FixedPoint2.Zero)
        {
            // Updates the chemicals itself.
            _fluidDissipationSystem.Dissipate(solutionEntity.Value, gridUid, indices, dissipatedUnits);
            changed = true;
        }

        if (!changed)
            return;

        if (solution.Volume <= FixedPoint2.Zero)
        {
            // Out of the cache now rather than at shutdown, at the end of the tick: ice that freezes on this tile before
            //     then would otherwise go into this sheet and be deleted with it.
            UncacheSheet(sheet);
            QueueDel(sheet);
            return;
        }

        UpdateSheet(sheet);
    }

    /// <summary>
    ///     Linked ice in thin air sublimes straight into the tile's gas, at the Hertz–Knudsen rate towards saturation, as
    ///         far as its own heat pays for. On a tile with no air of its own (space, or an airless grid) it sublimes into
    ///         vacuum, and the gas is lost to it, as anything vented there is.
    /// </summary>
    /// <param name="heatCapacity">The sheet's heat capacity, which pays the sublimation heat.</param>
    /// <returns>Units removed from the sheet.</returns>
    private float Sublime(
        Solution solution,
        ReagentId reagent,
        float available,
        KsFluidReagentProperties properties,
        EntityUid gridUid,
        Vector2i indices,
        float deltaTime,
        float referenceVolume,
        float heatCapacity)
    {
        // A sealed tile has nowhere for the gas to go. Anything else takes it, and a map's air or vacuum simply loses it.
        if (_tileEffectAtmosSystem.GetTileAir(gridUid, indices) is not { } tileMixture)
            return 0f;

        var air = tileMixture is { Volume: > 0f, Temperature: > 0f } ? tileMixture : null;

        var gas = (Gas)properties.GasIndex;
        var temperature = solution.Temperature;
        var saturationPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature);
        var partialPressure = air != null ? air.GetMoles(gas) * Atmospherics.R * air.Temperature / air.Volume : 0f;
        if (saturationPressure <= partialPressure)
            return 0f;

        var coverage = MathF.Min(1f, (float)solution.Volume / referenceVolume);
        var flux = KsPhaseMath.HertzKnudsenFlux(KsFluidmos.PuddleHertzKnudsenAlpha, saturationPressure - partialPressure,
            properties.InverseSqrtTwoPiMR, temperature);
        var moles = flux * coverage * deltaTime;
        if (air != null)
            moles = MathF.Min(moles, (saturationPressure - partialPressure) * air.Volume / (Atmospherics.R * air.Temperature));

        // The sublimation heat comes out of the sheet, so no more goes than cools it to where its vapor pressure meets
        //     the air's, or the floor of the curve in vacuum.
        var sublimationHeat = properties.LatentHeat + properties.FusionHeat;
        var excessTemperature = temperature - KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, partialPressure);
        if (excessTemperature <= 0f)
            return 0f;

        var heatLimit = excessTemperature * heatCapacity / (sublimationHeat + excessTemperature * properties.SpecificHeat);
        var units = (float)FixedPoint2.New(MathF.Min(MathF.Min(moles / properties.MolesPerUnit, available), heatLimit));
        if (units <= 0f)
            return 0f;

        if (!_tileEffectAtmosSystem.TryAddGas(gridUid, indices, gas, units * properties.MolesPerUnit,
                sensibleEnergy: units * properties.SpecificHeat * temperature))
            return 0f;

        return (float)solution.RemoveReagent(reagent, FixedPoint2.New(units));
    }

    /// <summary>
    ///     How full a puddle of what the sheet holds would be, as <c>PuddleVisuals.CurrentVolume</c> measures it, so the
    ///         sheet looks like the puddle it froze out of.
    /// </summary>
    private float GetFill(Solution solution)
    {
        if (!ProtoMan.Index(PuddlePrototype).TryComp<PuddleComponent>(out var puddleComponent, Factory))
            return 1f;

        return (float)(solution.Volume / _puddleSystem.KsGetOverflowVolume(puddleComponent, solution));
    }

    /// <summary>
    ///     Freezes onto the sheet whatever of the tile's puddle is below its melting point and already frozen in the sheet,
    ///         as far as the puddle's heat deficit pays for, as <c>FreezeTileEffect</c> freezes it: the freezing warms what
    ///         is left to the melting point, and the ice leaves at it.
    /// </summary>
    /// <remarks>
    ///     Ice seeds the freezing of liquid that touches it. Air too thin for a reagent to freeze on its own boils it
    ///         instead (<c>FreezeTileEffect</c>), cooling it below its melting point, and a spaced room would otherwise keep
    ///         that supercooled puddle beside the ice it froze earlier for ever. This way a tile seldom holds one reagent as
    ///         both. Only tiles with ice get here, so puddles without any cost nothing.
    /// </remarks>
    /// <returns>Whether anything froze into <paramref name="ice"/>.</returns>
    private bool FreezeSupercooledPuddle(Solution ice, EntityUid gridUid, Vector2i indices)
    {
        if (ice.Volume <= FixedPoint2.Zero ||
            !_puddleSystem.TryGetCachedPuddle(gridUid, indices, out var puddle) ||
            !_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var puddleSolution) ||
            puddle.Comp.Solution is not { } puddleSolutionEntity ||
            puddleSolution.Volume <= FixedPoint2.Zero)
            return false;

        var froze = false;
        for (var i = puddleSolution.Contents.Count - 1; i >= 0; i--)
        {
            var (reagent, quantity) = puddleSolution.Contents[i];
            if (!ice.ContainsPrototype(reagent.Prototype) ||
                !_propertiesSystem.TryGetProperties(reagent.Prototype, out var properties) ||
                !properties.Freezable ||
                properties.FusionHeat <= 0f)
                continue;

            var meltingPoint = properties.MeltingPoint;
            var temperature = puddleSolution.Temperature;
            if (temperature >= meltingPoint)
                continue;

            // Freezing u units at the melting point leaves (C - u c) T' + u c Tm = C T + u Lf; at T' = Tm, u = C (Tm - T) / Lf.
            var heatCapacity = puddleSolution.GetHeatCapacity(ProtoMan);
            var units = (float)FixedPoint2.New(MathF.Min((float)quantity, heatCapacity * (meltingPoint - temperature) / properties.FusionHeat));
            if (units <= 0f)
                continue;

            var removed = (float)puddleSolution.RemoveReagent(reagent, FixedPoint2.New(units));
            if (removed <= 0f)
                continue;

            // The ice leaves at the melting point. If nothing is left, the ice holds all the heat, which freezing all of it
            //     raised no further than the melting point.
            var specificHeat = properties.SpecificHeat;
            var remainingHeatCapacity = heatCapacity - removed * specificHeat;
            var frozen = new Solution();
            frozen.AddReagent(reagent, FixedPoint2.New(removed));
            if (remainingHeatCapacity > 0f && puddleSolution.Volume > FixedPoint2.Zero)
            {
                frozen.Temperature = meltingPoint;
                puddleSolution.Temperature = (heatCapacity * temperature + removed * properties.FusionHeat - removed * specificHeat * meltingPoint)
                                             / remainingHeatCapacity;
            }
            else
            {
                frozen.Temperature = (heatCapacity * temperature + removed * properties.FusionHeat) / (removed * specificHeat);
            }

            // Merges the temperature by heat capacity.
            ice.AddSolution(frozen, ProtoMan);
            froze = true;
        }

        if (froze)
            _solutionContainerSystem.UpdateChemicals(puddleSolutionEntity);

        return froze;
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
    ///     Tints the sheet by its solution, shapes it as a puddle of as much, and makes it slippery once it is more than
    ///         frost. Only touches physics when the slipperiness actually changes.
    /// </summary>
    private void UpdateSheet(Entity<KsIceSheetComponent> sheet)
    {
        if (!_solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out _, out var solution))
            return;

        var volume = (float)solution.Volume;
        var color = solution.GetColor(ProtoMan);
        var alpha = MinimumAlpha + (MaximumAlpha - MinimumAlpha) * MathF.Min(1f, volume / OpaqueUnits);
        _appearanceSystem.SetData(sheet, KsIceSheetVisuals.Color, color.WithAlpha(alpha));
        _appearanceSystem.SetData(sheet, KsIceSheetVisuals.Fill, GetFill(solution));

        var slippery = volume >= SlipThresholdUnits;
        if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(sheet, out var physicsComponent) &&
            physicsComponent.CanCollide != slippery)
            _physicsSystem.SetCanCollide(sheet, slippery, body: physicsComponent);
    }
}
