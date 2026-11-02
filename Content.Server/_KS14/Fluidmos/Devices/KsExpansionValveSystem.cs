using Content.Server._KS14.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Devices;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     Runs <see cref="KsExpansionValveComponent"/>s and their windows.
/// </summary>
public sealed partial class KsExpansionValveSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private KsLiquidFlowSystem _liquidFlowSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsTransferThermoSystem _transferThermoSystem = default!;

    /// <summary>
    ///     Grams per mole of the vapor through a valve with no refrigerant to go by.
    /// </summary>
    private const float AirMolarMass = 29f;

    /// <summary>
    ///     Grams per liter of a liquid at the game's scale.
    /// </summary>
    private const float LiquidDensity = KsFluidmos.GramsPerUnit / KsFluidmos.LitersPerUnit;

    [SubscribeLocalEvent]
    private void OnDeviceUpdate(Entity<KsExpansionValveComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainerSystem.TryGetNodes(entity.Owner, entity.Comp.InletName, entity.Comp.OutletName, out PipeNode? inlet, out PipeNode? outlet))
            return;

        var refrigerant = MainLiquid(inlet.Air) ?? MainLiquid(outlet.Air) ?? MainLinkedGas(outlet.Air) ?? MainLinkedGas(inlet.Air) ?? MainVapor(inlet.Air);
        UpdateReadout(entity, inlet.Air, outlet.Air, refrigerant);

        if (entity.Comp.Automatic && entity.Comp.PressureSetpoint > 0f)
        {
            // Below the setpoint the evaporator is starved: open up. Above it, it boils too warm: close down.
            var error = (entity.Comp.PressureSetpoint - outlet.Air.Pressure) / entity.Comp.PressureSetpoint;
            entity.Comp.Opening = Math.Clamp(entity.Comp.Opening + entity.Comp.AutomaticGain * error * args.dt, 0f, 1f);
        }

        var conductance = entity.Comp.Conductance * entity.Comp.Opening;
        var pressureDifference = inlet.Air.Pressure - outlet.Air.Pressure;
        var liquidCapacity = conductance * MathF.Sqrt(MathF.Max(pressureDifference, 0f)) * args.dt;
        var moved = _liquidFlowSystem.Transfer(inlet.Air, outlet.Air, conductance, args.dt);

        // Liquid goes first, as from the bottom of a receiver; whatever of the orifice it leaves passes vapor. Without
        //      this, a condenser that hasn't yet delivered liquid to the valve stops the whole loop.
        if (liquidCapacity > 0f && moved < liquidCapacity)
            PassVapor(inlet.Air, outlet.Air, conductance * (1f - moved / liquidCapacity), refrigerant, args.dt);

        entity.Comp.FlowRate = args.dt > 0f ? moved / args.dt : 0f;

        if (_gameTiming.CurTime >= entity.Comp.NextUiUpdate)
        {
            entity.Comp.NextUiUpdate = _gameTiming.CurTime + entity.Comp.UiUpdateInterval;
            UpdateUi(entity);
        }
    }

    /// <summary>
    ///     The refrigerant's boiling point at the outlet, and the share of the liquid that flashes crossing the valve: the
    ///         energy-limited flash the outlet's phase step will do, <c>c (T - Tb(P_out)) / L</c>, computed here only for
    ///         display.
    /// </summary>
    private static void UpdateReadout(Entity<KsExpansionValveComponent> entity, GasMixture inlet, GasMixture outlet, KsFluidReagentProperties? refrigerant)
    {
        if (refrigerant is not { Volatile: true } || refrigerant.LatentHeat <= 0f)
        {
            entity.Comp.OutletBoilingPoint = float.NaN;
            entity.Comp.VaporFraction = float.NaN;
            return;
        }

        var outletBoilingPoint = KsPhaseMath.BoilingPointAt(refrigerant.VaporizationEntropy, refrigerant.BoilingPoint, outlet.Pressure);
        entity.Comp.OutletBoilingPoint = outletBoilingPoint;
        entity.Comp.VaporFraction = Math.Clamp(refrigerant.SpecificHeat * (inlet.Temperature - outletBoilingPoint) / refrigerant.LatentHeat, 0f, 1f);
    }

    /// <summary>
    ///     Throttles vapor across the orifice: the same conductance as for liquid, by mass, scaled by
    ///         <c>√(ρ_vapor / ρ_liquid)</c> as an orifice's mass flow goes with <c>√(ρ ΔP)</c>. Never past the pressures
    ///         meeting.
    /// </summary>
    /// <param name="conductance">The liquid conductance left for vapor, in units per second per √kPa.</param>
    /// <param name="refrigerant">Gives the vapor's molar mass; air's without one.</param>
    private void PassVapor(GasMixture inlet, GasMixture outlet, float conductance, KsFluidReagentProperties? refrigerant, float deltaTime)
    {
        var inletPressure = inlet.Pressure;
        var outletPressure = outlet.Pressure;
        var pressureDifference = inletPressure - outletPressure;
        // Unlinked vapor lives in the fluid, not the gas moles, and leaves with them by the same ratio.
        var inletVaporMoles = inlet.TotalMoles + (inlet.Fluid?.VaporMoles ?? 0f);
        if (pressureDifference <= 0f || inletVaporMoles <= 0f || inlet.Temperature <= 0f || outlet.Immutable)
            return;

        var molarMass = refrigerant is { MolesPerUnit: > 0f } ? KsFluidmos.GramsPerUnit / refrigerant.MolesPerUnit : AirMolarMass;
        var vaporDensity = inletPressure * molarMass / (Atmospherics.R * inlet.Temperature);
        var grams = conductance * KsFluidmos.GramsPerUnit * MathF.Sqrt(pressureDifference * vaporDensity / LiquidDensity) * deltaTime;

        // dP/dn for each side, as a radiator works out its own flow: moving more than this would reverse the difference.
        var pressurePerMole = Atmospherics.R * (inlet.Temperature / inlet.FreeVolume + MathF.Max(outlet.Temperature, Atmospherics.TCMB) / outlet.FreeVolume);
        var moles = MathF.Min(MathF.Min(grams / molarMass, inletVaporMoles), 0.5f * pressureDifference / pressurePerMole);
        if (moles <= 0f)
            return;

        var vapor = inlet.RemoveRatio(moles / inletVaporMoles,includeLiquid: false);
        _transferThermoSystem.Apply(vapor, inletPressure, outletPressure, doesWork: false);
        _atmosphereSystem.Merge(outlet, vapor);
    }

    /// <summary>
    ///     The linked gas a mixture holds the most of, for a valve that sees only vapor.
    /// </summary>
    private KsFluidReagentProperties? MainLinkedGas(GasMixture mixture)
    {
        KsFluidReagentProperties? main = null;
        var mainMoles = 0f;
        var byGas = _propertiesSystem.ByGas;
        for (var gas = 0; gas < byGas.Length; gas++)
        {
            var moles = mixture.GetMoles(gas);
            if (byGas[gas] is not { } properties || moles <= mainMoles)
                continue;

            main = properties;
            mainMoles = moles;
        }

        return main;
    }

    /// <summary>
    ///     The reagent a mixture holds the most liquid of.
    /// </summary>
    private KsFluidReagentProperties? MainLiquid(GasMixture mixture)
    {
        if (mixture.Fluid is not { } fluid)
            return null;

        KsFluidReagentProperties? main = null;
        var mainUnits = 0f;
        foreach (var (key, amount) in fluid.Reagents)
        {
            if (amount.Liquid <= mainUnits || !_propertiesSystem.TryGetProperties(key.Prototype, out var properties))
                continue;

            main = properties;
            mainUnits = amount.Liquid;
        }

        return main;
    }

    /// <summary>
    ///     The reagent a mixture holds the most unlinked vapor of, for a valve passing vapor no gas stands for.
    /// </summary>
    private KsFluidReagentProperties? MainVapor(GasMixture mixture)
    {
        if (mixture.Fluid is not { } fluid)
            return null;

        KsFluidReagentProperties? main = null;
        var mainMoles = 0f;
        foreach (var (key, amount) in fluid.Reagents)
        {
            if (amount.Vapor <= mainMoles || !_propertiesSystem.TryGetProperties(key.Prototype, out var properties))
                continue;

            main = properties;
            mainMoles = amount.Vapor;
        }

        return main;
    }

    // By-value BUI events: the engine has no Entity<T> handler for them (RA0054).
    [SubscribeLocalEvent]
    private void OnUiOpened(EntityUid uid, KsExpansionValveComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnSetOpening(EntityUid uid, KsExpansionValveComponent component, KsExpansionValveSetOpeningMessage args)
    {
        if (!float.IsFinite(args.Opening))
            return;

        component.Opening = Math.Clamp(args.Opening, 0f, 1f);
        UpdateUi((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnSetAutomatic(EntityUid uid, KsExpansionValveComponent component, KsExpansionValveSetAutomaticMessage args)
    {
        component.Automatic = args.Automatic;
        UpdateUi((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnSetPressure(EntityUid uid, KsExpansionValveComponent component, KsExpansionValveSetPressureMessage args)
    {
        if (!float.IsFinite(args.Pressure))
            return;

        component.PressureSetpoint = Math.Clamp(args.Pressure, 0f, component.MaxPressureSetpoint);
        UpdateUi((uid, component));
    }

    private float OutletPressure(Entity<KsExpansionValveComponent> entity)
    {
        return _nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.OutletName, out PipeNode? outlet) ? outlet.Air.Pressure : 0f;
    }

    private void UpdateUi(Entity<KsExpansionValveComponent> entity)
    {
        if (!_userInterfaceSystem.IsUiOpen(entity.Owner, KsExpansionValveUiKey.Key))
            return;

        _userInterfaceSystem.SetUiState(entity.Owner,
            KsExpansionValveUiKey.Key,
            new KsExpansionValveBoundUserInterfaceState(entity.Comp.Opening,
                entity.Comp.Automatic,
                entity.Comp.PressureSetpoint,
                entity.Comp.VaporFraction,
                OutletPressure(entity),
                entity.Comp.OutletBoilingPoint,
                entity.Comp.FlowRate));
    }
}
