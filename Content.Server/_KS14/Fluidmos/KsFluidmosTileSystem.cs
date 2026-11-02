using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.TileEffects;
using Content.Shared.NodeContainer;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     Where fluid meets a tile. Tile mixtures never hold fluid: a tile's liquid is its puddle, and its unlinked vapor is
///         smoke. So every route from a device into a tile goes through <see cref="ReleaseToTile"/> first, and siphoning
///         devices draw puddles and smoke back in through <see cref="AbsorbFromTile"/>.
/// </summary>
public sealed partial class KsFluidmosTileSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private Ice.KsIceSheetSystem _iceSheetSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SmokeSystem _smokeSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<SmokeComponent> _smokeQuery = default!;

    private static readonly EntProtoId SmokePrototype = "Smoke";

    /// <summary>
    ///     How long released vapor hangs about as smoke, in seconds.
    /// </summary>
    private const float SmokeDuration = 10f;

    /// <summary>
    ///     Units of vapor per tile a puff of smoke spreads over.
    /// </summary>
    private const float SmokeUnitsPerTile = 5f;

    private const int MaxSmokeSpread = 20;

    /// <summary>
    ///     Spills queued from places that must not spawn, drained in <see cref="Update"/>.
    /// </summary>
    private readonly List<(EntityCoordinates Coordinates, Solution Liquid, Solution Vapor)> _pendingSpills = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingSpills.Count == 0)
            return;

        // Spilling can raise events that queue more; those wait for the next tick.
        var count = _pendingSpills.Count;
        for (var i = 0; i < count; i++)
        {
            var (coordinates, liquid, vapor) = _pendingSpills[i];
            Spill(coordinates, liquid, vapor);
        }

        _pendingSpills.RemoveRange(0, count);
    }

    /// <summary>
    ///     Detaches the fluid from a mixture a device is about to merge into its tile. The gas, linked vapor included, is
    ///         left for the caller to merge as usual; liquid becomes a puddle and unlinked vapor smoke, once the device
    ///         has buffered enough of either.
    /// </summary>
    public void ReleaseToTile(EntityUid deviceUid, GasMixture released)
    {
        if (released.Fluid is not { } fluid)
            return;

        released.Fluid = null;
        if (fluid.IsEmpty)
            return;

        var bufferComponent = EnsureComp<KsFluidmosTileBufferComponent>(deviceUid);
        if (bufferComponent.Remainder is { } remainder)
        {
            fluid.Add(remainder);
            bufferComponent.Remainder = null;
        }

        var solid = TakeAll(fluid, released.Temperature, bufferComponent.Liquid, bufferComponent.Vapor);
        _iceSheetSystem.AddIce(Transform(deviceUid).Coordinates, solid);

        // What fell short of a solution's 0.01u, kept for the next release.
        if (!fluid.IsEmpty)
            bufferComponent.Remainder = fluid;

        var spillLiquid = bufferComponent.Liquid.Volume >= FixedPoint2.New(KsFluidmos.SpillThresholdUnits);
        var spillVapor = bufferComponent.Vapor.Volume >= FixedPoint2.New(KsFluidmos.SmokeThresholdUnits);
        if (!spillLiquid && !spillVapor)
            return;

        var coordinates = Transform(deviceUid).Coordinates;
        Spill(coordinates,
            spillLiquid ? SplitAll(bufferComponent.Liquid) : new Solution(),
            spillVapor ? SplitAll(bufferComponent.Vapor) : new Solution());
    }

    /// <summary>
    ///     Spills a pipe's share of its nets' fluid where it stood, on the next update, when damage destroys it outright.
    ///         Its nets remove the same share from themselves when they drop its nodes. Breaking it (a lesser blow) leaks
    ///         that share through <c>AtmosUnsafeUnanchorSystem.LeakGas</c> instead. Only destruction spills: a pipe
    ///         deleted any other way, by an admin say, takes its fluid with it, as it takes its gas.
    /// </summary>
    /// <remarks>
    ///     A uid and component rather than an <see cref="Entity{T}"/>: <see cref="DestructionEventArgs"/> is raised by
    ///         value, which only that form of handler takes.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnNodeContainerDestroyed(EntityUid uid, NodeContainerComponent component, DestructionEventArgs args)
    {
        foreach (var node in component.Nodes.Values)
        {
            if (node is not PipeNode { NodeGroup: IPipeNet { Air: { Fluid: { } fluid } air } } pipe || air.Volume <= 0f)
                continue;

            var share = fluid.Clone();
            share.Multiply(MathF.Min(pipe.Volume / air.Volume, 1f));
            QueueSpill(Transform(uid).Coordinates, share, air.Temperature);
        }
    }

    /// <summary>
    ///     Spills a destroyed canister's fluid where it stood. Breaking one dumps its air through
    ///         <c>DumpCanisterBehavior</c>, but a hard enough blow skips that threshold and only destroys it, and only the
    ///         gas would otherwise reach the room. Only destruction spills: a canister deleted any other way, by an admin
    ///         say, takes its fluid with it, as it takes its gas. Portable scrubbers need nothing here, as their own
    ///         destruction handler already releases their fluid.
    /// </summary>
    /// <remarks>
    ///     A uid and component rather than an <see cref="Entity{T}"/>: <see cref="DestructionEventArgs"/> is raised by
    ///         value, which only that form of handler takes.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnCanisterDestroyed(EntityUid uid, Content.Shared.Atmos.Piping.Unary.Components.GasCanisterComponent component, DestructionEventArgs args)
    {
        SpillContainedFluid(uid, component.Air);
    }

    /// <summary>
    ///     Spills a destroyed gas tank's fluid, as <see cref="OnCanisterDestroyed"/> does a canister's.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGasTankDestroyed(EntityUid uid, Content.Shared.Atmos.Components.GasTankComponent component, DestructionEventArgs args)
    {
        SpillContainedFluid(uid, component.Air);
    }

    /// <summary>
    ///     Queues whatever fluid a broken container still holds to spill on its tile, carried or not. Nothing is spilled if
    ///         its grid or map is going too, as at round end.
    /// </summary>
    private void SpillContainedFluid(EntityUid containerUid, GasMixture? air)
    {
        if (air?.Fluid is not { IsEmpty: false } fluid)
            return;

        air.Fluid = null;
        var transformComponent = Transform(containerUid);
        if (TerminatingOrDeleted(transformComponent.MapUid) ||
            transformComponent.GridUid is { } gridUid && TerminatingOrDeleted(gridUid))
            return;

        QueueSpill(_transformSystem.GetMoverCoordinates(containerUid, transformComponent), fluid, air.Temperature);
    }

    /// <summary>
    ///     Queues a reagent condensed out of a tile's air to spill onto the tile's puddle on the next update, outside atmos
    ///         processing (<see cref="KsCondensationReaction"/>).
    /// </summary>
    public void QueueCondensate(EntityUid gridUid, Vector2i indices, ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype> reagent, float units, float temperature)
    {
        if (units <= 0f || !_gridQuery.TryComp(gridUid, out var gridComponent))
            return;

        var liquid = new Solution(reagent, FixedPoint2.New(units)) { Temperature = temperature };
        _pendingSpills.Add((_mapSystem.GridTileToLocal(gridUid, gridComponent, indices), liquid, new Solution()));
    }

    /// <summary>
    ///     Queues a mixture's fluid to spill at <paramref name="coordinates"/> on the next update, for callers that must
    ///         not spawn, such as a pipe net losing a deleted pipe.
    /// </summary>
    public void QueueSpill(EntityCoordinates coordinates, KsFluidMixture fluid, float temperature)
    {
        if (fluid.IsEmpty)
            return;

        var liquid = new Solution();
        var vapor = new Solution();
        var solid = TakeAll(fluid, temperature, liquid, vapor);
        _iceSheetSystem.AddIce(coordinates, solid);
        _pendingSpills.Add((coordinates, liquid, vapor));
    }

    /// <summary>
    ///     Draws a device's tile fluid into <paramref name="destination"/>: <paramref name="fraction"/> of the tile's
    ///         smoke when <paramref name="smoke"/>, and up to <paramref name="puddleUnits"/> of its puddle.
    /// </summary>
    public void AbsorbFromTile(EntityUid deviceUid, GasMixture destination, float fraction, bool smoke, float puddleUnits)
    {
        if (!destination.FluidCapable || fraction <= 0f && puddleUnits <= 0f)
            return;

        var transformComponent = Transform(deviceUid);
        if (transformComponent.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var gridComponent))
            return;

        var indices = _mapSystem.TileIndicesFor(gridUid, gridComponent, transformComponent.Coordinates);

        if (smoke && fraction > 0f && TryGetSmoke((gridUid, gridComponent), indices, out var smokeSolution))
        {
            var taken = _solutionContainerSystem.SplitSolution(smokeSolution,
                FixedPoint2.New((float)smokeSolution.Comp.Solution.Volume * MathF.Min(fraction, 1f)));
            InjectSolution(destination, taken);
        }

        if (puddleUnits > 0f &&
            _puddleSystem.TryGetCachedPuddle(gridUid, indices, out var puddle) &&
            puddle.Comp.Solution is { } puddleSolution)
        {
            var taken = _solutionContainerSystem.SplitSolution(puddleSolution, FixedPoint2.New(puddleUnits));
            InjectSolution(destination, taken);
        }
    }

    /// <summary>
    ///     Adds a solution to a mixture as liquid, merging the temperature by heat capacity.
    /// </summary>
    public void InjectSolution(GasMixture mixture, Solution solution)
    {
        if (solution.Volume <= FixedPoint2.Zero || mixture.Immutable)
            return;

        if (!mixture.FluidCapable)
        {
            Log.Error("Tried to inject a solution into a mixture that cannot hold fluid.");
            return;
        }

        var mixtureHeatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false);
        var solutionHeatCapacity = solution.GetHeatCapacity(ProtoMan);
        var totalHeatCapacity = mixtureHeatCapacity + solutionHeatCapacity;

        mixture.Fluid ??= new KsFluidMixture();
        mixture.Fluid.AddSolution(solution);

        if (totalHeatCapacity > 0f)
            mixture.Temperature = (mixtureHeatCapacity * mixture.Temperature + solutionHeatCapacity * solution.Temperature) / totalHeatCapacity;
    }

    /// <summary>
    ///     Vapor a puddle boiled off as smoke (<c>BoilTileEffect</c>'s smoke mode), held per tile until there is a puff's
    ///         worth: a transient work list, emptied as each tile reaches it.
    /// </summary>
    private readonly Dictionary<(EntityUid GridUid, Vector2i Tile), Solution> _boiledSmoke = new();

    [SubscribeLocalEvent]
    private void OnBoiledToSmoke(ref KsTileEffectSmokeEvent args)
    {
        if (args.Units <= 0f || !_gridQuery.TryComp(args.GridUid, out var gridComponent))
            return;

        var key = (args.GridUid, args.Tile);
        if (!_boiledSmoke.TryGetValue(key, out var vapor))
            _boiledSmoke[key] = vapor = new Solution();

        var boiled = new Solution();
        boiled.AddReagent(args.Reagent, FixedPoint2.New(args.Units));
        boiled.Temperature = args.Temperature;
        vapor.AddSolution(boiled, ProtoMan);
        args.Handled = true;

        if (vapor.Volume < FixedPoint2.New(KsFluidmos.SmokeThresholdUnits))
            return;

        _boiledSmoke.Remove(key);
        var coordinates = _mapSystem.GridTileToLocal(args.GridUid, gridComponent, args.Tile);
        _pendingSpills.Add((coordinates, new Solution(), vapor));
    }

    /// <summary>
    ///     Flushes a deleted device's buffer while it still stands somewhere: by its components' shutdown it has been
    ///         detached, and a spill at its coordinates then goes nowhere. <see cref="OnBufferShutdown"/> finds it empty.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBufferTerminating(Entity<KsFluidmosTileBufferComponent> entity, ref EntityTerminatingEvent args)
    {
        FlushBuffer(entity);
    }

    /// <summary>
    ///     Flushes the buffer of a device that keeps standing, with only its buffer removed.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBufferShutdown(Entity<KsFluidmosTileBufferComponent> entity, ref ComponentShutdown args)
    {
        FlushBuffer(entity);
    }

    private void FlushBuffer(Entity<KsFluidmosTileBufferComponent> entity)
    {
        // The sub-0.01u remainder joins the buffer, where it may tip a reagent over a solution's precision.
        if (entity.Comp.Remainder is { } remainder)
        {
            entity.Comp.Remainder = null;
            var solid = TakeAll(remainder, entity.Comp.Liquid.Temperature, entity.Comp.Liquid, entity.Comp.Vapor);
            if (solid.Volume > FixedPoint2.Zero)
                _iceSheetSystem.AddIce(Transform(entity).Coordinates, solid);
        }

        if (entity.Comp.Liquid.Volume <= FixedPoint2.Zero && entity.Comp.Vapor.Volume <= FixedPoint2.Zero)
            return;

        // Both can run mid-deletion, so the spill waits for the next update.
        _pendingSpills.Add((Transform(entity).Coordinates, SplitAll(entity.Comp.Liquid), SplitAll(entity.Comp.Vapor)));
    }

    /// <summary>
    ///     Moves every phase of <paramref name="fluid"/> into solutions at <paramref name="temperature"/>: liquid into
    ///         <paramref name="liquid"/>, unlinked vapor into <paramref name="vapor"/>, and the solid out as the result.
    /// </summary>
    /// <returns>The solid, for the tile's ice sheet.</returns>
    private Solution TakeAll(KsFluidMixture fluid, float temperature, Solution liquid, Solution vapor)
    {
        var condensed = fluid.TakeLiquidAsSolution(fluid.LiquidUnits);
        condensed.Temperature = temperature;
        liquid.AddSolution(condensed, ProtoMan);

        var solid = fluid.TakeSolidAsSolution();
        solid.Temperature = temperature;

        var evaporated = fluid.TakeVaporAsSolution(fluid.VaporMoles, _propertiesSystem);
        evaporated.Temperature = temperature;
        vapor.AddSolution(evaporated, ProtoMan);

        return solid;
    }

    private static Solution SplitAll(Solution solution)
    {
        var all = solution.SplitSolution(solution.Volume);
        all.Temperature = solution.Temperature;
        return all;
    }

    private void Spill(EntityCoordinates coordinates, Solution liquid, Solution vapor)
    {
        if (!coordinates.IsValid(EntityManager))
            return;

        if (liquid.Volume > FixedPoint2.Zero)
            _puddleSystem.TrySpillAt(coordinates, liquid, out _, sound: false);

        if (vapor.Volume > FixedPoint2.Zero)
            ReleaseSmoke(coordinates, vapor);
    }

    /// <summary>
    ///     Adds vapor to the smoke already on the tile, or starts a new puff.
    /// </summary>
    private void ReleaseSmoke(EntityCoordinates coordinates, Solution vapor)
    {
        var gridUid = _transformSystem.GetGrid(coordinates);
        if (gridUid != null && _gridQuery.TryComp(gridUid, out var gridComponent))
        {
            var indices = _mapSystem.TileIndicesFor(gridUid.Value, gridComponent, coordinates);
            if (TryGetSmoke((gridUid.Value, gridComponent), indices, out var smokeSolution) &&
                _solutionContainerSystem.TryAddSolution(smokeSolution, vapor))
                return;
        }

        var smokeUid = Spawn(SmokePrototype, _transformSystem.ToMapCoordinates(coordinates));
        var spread = Math.Clamp((int)MathF.Ceiling((float)vapor.Volume / SmokeUnitsPerTile), 1, MaxSmokeSpread);
        _smokeSystem.StartSmoke(smokeUid, vapor, SmokeDuration, spread);
    }

    private bool TryGetSmoke(Entity<MapGridComponent> grid, Vector2i indices, out Entity<SolutionComponent> smokeSolution)
    {
        var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(grid, grid.Comp, indices);
        while (enumerator.MoveNext(out var anchoredUid))
        {
            if (!_smokeQuery.HasComp(anchoredUid.Value) ||
                !_solutionContainerSystem.TryGetSolution(anchoredUid.Value, SmokeComponent.SolutionName, out var solutionEntity))
                continue;

            smokeSolution = solutionEntity.Value;
            return true;
        }

        smokeSolution = default;
        return false;
    }
}
