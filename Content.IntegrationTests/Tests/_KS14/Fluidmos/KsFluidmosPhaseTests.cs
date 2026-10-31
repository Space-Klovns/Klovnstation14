#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The phase step inside fluid-capable mixtures: Hertz–Knudsen evaporation towards Raoult's-law equilibrium, flashing
///         above the boiling point at the mixture's pressure, condensation of linked gases, freezing plateaus, sublimation,
///         the critical-temperature cap, and energy conservation throughout.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsFluidmosPhaseTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> NitrogenReagent = "Nitrogen";

    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey NitrogenKey = new(NitrogenReagent, null);

    private static readonly ProtoId<ReagentPrototype> PlasmaReagent = "Plasma";
    private static readonly ProtoId<ReagentPrototype> TritiumReagent = "Tritium";
    private static readonly KsFluidReagentKey PlasmaKey = new(PlasmaReagent, null);
    private static readonly KsFluidReagentKey TritiumKey = new(TritiumReagent, null);

    private static readonly ProtoId<ReagentPrototype> AmmoniaReagent = "Ammonia";
    private static readonly KsFluidReagentKey AmmoniaKey = new(AmmoniaReagent, null);

    /// <summary>
    ///     A pipe node's volume, which the Hertz–Knudsen tuning targets.
    /// </summary>
    private const float NodeVolume = 200f;

    private const float StepSeconds = 0.5f;

    private static GasMixture MakePipe(float temperature)
    {
        return new GasMixture(NodeVolume) { Temperature = temperature, FluidCapable = true };
    }

    /// <summary>
    ///     Fills the gas left over by the liquid with nitrogen at one atmosphere, so the liquid evaporates at its surface
    ///         rather than flashing, as it would into vacuum.
    /// </summary>
    private static void AddHeadspace(GasMixture mixture, float pressure = Atmospherics.OneAtmosphere)
    {
        mixture.AdjustMoles(Gas.Nitrogen, pressure * mixture.FreeVolume / (Atmospherics.R * mixture.Temperature));
    }

    /// <summary>
    ///     Runs one phase step as if <paramref name="seconds"/> had passed since the last.
    /// </summary>
    private static void Step(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture, float seconds = StepSeconds)
    {
        if (mixture.Fluid != null)
            mixture.Fluid.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(seconds);

        phaseSystem.UpdatePhases(mixture, holder: null);
    }

    private static float EquilibriumVaporMoles(KsFluidReagentProperties properties, GasMixture mixture)
    {
        var saturationPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, mixture.Temperature);
        return saturationPressure * mixture.FreeVolume / (Atmospherics.R * mixture.Temperature);
    }

    [Test]
    public async Task WaterEvaporatesToSaturationWithinTheTunedTimeAndCools()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            const float startTemperature = 300f;
            var mixture = MakePipe(startTemperature);
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.AddLiquid(WaterKey, 100f);
            AddHeadspace(mixture);

            var energyBefore = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature;

            // The tuning: most of the way to saturation in a few seconds, between two and five (about three now). The
            //      early bound keeps a much faster alpha from passing too.
            var earlyFraction = 0f;
            for (var elapsed = 0f; elapsed < 5f; elapsed += StepSeconds)
            {
                Step(phaseSystem, gameTiming, mixture);
                if (elapsed + StepSeconds <= 2f)
                    earlyFraction = mixture.GetMoles(Gas.WaterVapor) / EquilibriumVaporMoles(water!, mixture);
            }

            var vapor = mixture.GetMoles(Gas.WaterVapor);
            var equilibrium = EquilibriumVaporMoles(water!, mixture);
            var evaporatedUnits = vapor / water!.MolesPerUnit;
            var energyAfter = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature
                              + evaporatedUnits * water.LatentHeat;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vapor, Is.GreaterThanOrEqualTo(0.9f * equilibrium), "Water came nowhere near saturation in five seconds.");
                Assert.That(earlyFraction, Is.LessThan(0.9f), "Water came most of the way to saturation within two seconds: alpha is far above its tuning.");
                Assert.That(vapor, Is.LessThanOrEqualTo(1.01f * equilibrium), "Evaporation overshot equilibrium.");
                Assert.That(mixture.Temperature, Is.LessThan(startTemperature), "Evaporation did not cool the water.");
                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 0.0005f), "Evaporation created or destroyed energy.");
            }
        });
    }

    [Test]
    public async Task WaterVaporCondensesInAColdDryPipe()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            const float startTemperature = 285f;
            var mixture = MakePipe(startTemperature);
            mixture.AdjustMoles(Gas.Nitrogen, 8f);
            mixture.AdjustMoles(Gas.WaterVapor, 2f);

            for (var i = 0; i < 40; i++)
            {
                Step(phaseSystem, gameTiming, mixture);
            }

            Assert.That(mixture.Fluid, Is.Not.Null, "Supersaturated steam in a dry pipe never condensed.");
            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var amount));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(amount.Liquid, Is.GreaterThan(0f));
                Assert.That(mixture.GetMoles(Gas.WaterVapor), Is.EqualTo(EquilibriumVaporMoles(water!, mixture)).Within(5f).Percent,
                    "The steam did not settle at saturation.");
                Assert.That(mixture.Temperature, Is.GreaterThan(startTemperature), "Condensing released no latent heat.");
            }
        });
    }

    [Test]
    public async Task PressureSuppressesFlashing()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            // Above water's boiling point at one atmosphere.
            var hotTemperature = water!.BoilingPoint + 20f;

            // Enough liquid that the open pipe's flash, limited by the liquid's own heat, dwarfs what evaporates through the
            //      surface in one step, which is all the pressurised pipe may do.
            const float waterUnits = 2000f;
            var open = MakePipe(hotTemperature);
            open.Fluid = new KsFluidMixture();
            open.Fluid.AddLiquid(WaterKey, waterUnits);

            var pressurised = MakePipe(hotTemperature);
            pressurised.Fluid = new KsFluidMixture();
            pressurised.Fluid.AddLiquid(WaterKey, waterUnits);
            AddHeadspace(pressurised, pressure: 1000f);

            var flashPoint = KsPhaseMath.BoilingPointAt(water.VaporizationEntropy, water.BoilingPoint, pressurised.Pressure);
            Assert.That(flashPoint, Is.GreaterThan(hotTemperature), "The test's pressure doesn't raise the boiling point enough.");

            Step(phaseSystem, gameTiming, open);
            Step(phaseSystem, gameTiming, pressurised);

            Assert.That(pressurised.GetMoles(Gas.WaterVapor), Is.LessThan(open.GetMoles(Gas.WaterVapor) * 0.5f),
                "Pressurised water boiled as fast as water free to flash.");
        });
    }

    /// <summary>
    ///     Warm liquid let into an empty net, as through an expansion valve, flashes until what it boiled has raised the
    ///         net to the liquid's saturation pressure: it lands at <c>Tb(P)</c>, with as much vapor as its own sensible
    ///         heat paid for.
    /// </summary>
    [Test]
    public async Task LiquidEnteringALowPressureNetFlashesToItsBoilingPoint()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(AmmoniaReagent, out var ammonia));
            // Room-temperature liquid, far above its boiling point at the pressure an empty net offers.
            var startTemperature = Atmospherics.T20C;
            Assert.That(startTemperature, Is.GreaterThan(ammonia!.BoilingPoint), "Ammonia wouldn't flash at room temperature.");

            // Enough that the flash, at a few percent of it, fills the net long before the liquid is gone.
            const float liquidUnits = 4000f;
            var mixture = MakePipe(startTemperature);
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.AddLiquid(AmmoniaKey, liquidUnits);
            var startHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false);

            for (var i = 0; i < 40; i++)
                Step(phaseSystem, gameTiming, mixture);

            var landing = KsPhaseMath.BoilingPointAt(ammonia.VaporizationEntropy, ammonia.BoilingPoint, mixture.Pressure);
            // What the liquid's heat pays for, landing there: C0 T0 = (C_l (u0 - v) + C_v v) T + L v.
            var expectedVaporUnits = (startHeatCapacity * startTemperature - ammonia.SpecificHeat * liquidUnits * mixture.Temperature) /
                                     (ammonia.LatentHeat + (ammonia.VaporHeatCapacityPerUnit - ammonia.SpecificHeat) * mixture.Temperature);
            var vaporUnits = mixture.GetMoles(Gas.Ammonia) / ammonia.MolesPerUnit;
            Assert.That(mixture.Fluid!.TryGetAmount(AmmoniaKey, out var left));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mixture.Temperature, Is.LessThan(startTemperature - 10f), "The liquid didn't chill by flashing.");
                Assert.That(mixture.Temperature, Is.EqualTo(landing).Within(0.5f), "The flashed liquid didn't land at its boiling point at the net's pressure.");
                Assert.That(left.Liquid, Is.GreaterThan(0.5f * liquidUnits), "The flash boiled off more than the liquid's heat could pay for.");
                Assert.That(vaporUnits, Is.EqualTo(expectedVaporUnits).Within(0.01f * expectedVaporUnits),
                    "The flash fraction isn't what the energy balance gives.");
            }
        });
    }

    [Test]
    public async Task WaterFreezesAtAPlateauAndMeltsBack()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var coldTemperature = water!.MeltingPoint - 10f;
            var mixture = MakePipe(coldTemperature);
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.AddLiquid(WaterKey, 100f);
            AddHeadspace(mixture);

            var energyBefore = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature;
            Step(phaseSystem, gameTiming, mixture);

            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var frozen));
            // A little evaporates in the same step, taking its own latent heat.
            var evaporatedUnits = mixture.GetMoles(Gas.WaterVapor) / water.MolesPerUnit;
            var energyAfter = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature
                              + evaporatedUnits * water.LatentHeat
                              - frozen.Solid * water.FusionHeat;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mixture.Temperature, Is.EqualTo(water.MeltingPoint).Within(0.01f), "Freezing water did not plateau at its melting point.");
                Assert.That(frozen.Solid, Is.GreaterThan(0f), "Water below its melting point did not freeze.");
                Assert.That(frozen.Liquid, Is.GreaterThan(0f), "Water froze past what its sensible heat paid for.");
                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 0.0005f), "The heat of fusion was not accounted for.");
            }

            // Warm it back up: the ice melts, taking the same heat.
            mixture.Temperature = water.MeltingPoint + 5f;
            Step(phaseSystem, gameTiming, mixture);
            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var melted));
            Assert.That(melted.Solid, Is.LessThan(frozen.Solid), "Warm ice did not melt.");
        });
    }

    [Test]
    public async Task IceInVacuumSublimes()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var mixture = MakePipe(water!.MeltingPoint - 10f);
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.SetAmount(WaterKey, new KsFluidAmount(10f, 0f, 0f));

            for (var i = 0; i < 10; i++)
            {
                Step(phaseSystem, gameTiming, mixture);
            }

            Assert.That(mixture.GetMoles(Gas.WaterVapor), Is.GreaterThan(0f), "Ice in vacuum did not sublime.");
            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var amount));
            Assert.That(amount.Liquid, Is.Zero, "Subliming ice melted instead.");
        });
    }

    [Test]
    public async Task LinkedGasesNeverCondenseAboveTheirCriticalTemperature()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(NitrogenReagent, out var nitrogen));
            Assert.That(nitrogen!.CriticalTemperature, Is.LessThan(float.PositiveInfinity), "Nitrogen has no critical temperature.");

            // Far past what its vapor-pressure curve would hold, but above its critical temperature.
            var supercritical = MakePipe(nitrogen.CriticalTemperature + 5f);
            // Already carrying fluid, so the phase step itself decides, not the dry-pipe check in front of it.
            supercritical.Fluid = new KsFluidMixture();
            supercritical.Fluid.AddLiquid(WaterKey, 1f);
            supercritical.AdjustMoles(Gas.Nitrogen, 50000f * NodeVolume / (Atmospherics.R * supercritical.Temperature));

            // The same just below its boiling point.
            var cold = MakePipe(nitrogen.BoilingPoint - 2f);
            cold.AdjustMoles(Gas.Nitrogen, 500f * NodeVolume / (Atmospherics.R * cold.Temperature));

            for (var i = 0; i < 10; i++)
            {
                Step(phaseSystem, gameTiming, supercritical);
                Step(phaseSystem, gameTiming, cold);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(supercritical.Fluid?.TryGetAmount(NitrogenKey, out _) == true, Is.False,
                    "Nitrogen condensed above its critical temperature.");
                Assert.That(cold.Fluid?.TryGetAmount(NitrogenKey, out var liquid) == true && liquid.Liquid > 0f,
                    "Nitrogen below its boiling point never condensed.");
            }
        });
    }

    [Test]
    public async Task PlasmaIsALiquidAndTritiumVaporises()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(PlasmaReagent, out var plasma));
            Assert.That(propertiesSystem.TryGetProperties(TritiumReagent, out var tritium));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(plasma!.Linked, Is.False, "Plasma is linked, so it would condense out of the station's air.");
                Assert.That(tritium!.Linked, Is.False, "Tritium is linked.");
                Assert.That(plasma.Volatile, "Liquid plasma never vaporises.");
                Assert.That(tritium.Volatile, "Liquid tritium never vaporises.");
            }

            // Room temperature: plasma evaporates only up to its vapor pressure and stays mostly liquid.
            const float plasmaUnits = 1000f;
            var room = MakePipe(Atmospherics.T20C);
            room.Fluid = new KsFluidMixture();
            room.Fluid.AddLiquid(PlasmaKey, plasmaUnits);
            AddHeadspace(room);

            // Past its boiling point at one atmosphere: it boils, down to the boiling point.
            var hotTemperature = plasma.BoilingPoint + 20f;
            var hot = MakePipe(hotTemperature);
            hot.Fluid = new KsFluidMixture();
            hot.Fluid.AddLiquid(PlasmaKey, plasmaUnits);
            AddHeadspace(hot);

            var cryogenic = MakePipe(Atmospherics.T20C);
            cryogenic.Fluid = new KsFluidMixture();
            cryogenic.Fluid.AddLiquid(TritiumKey, 10f);
            AddHeadspace(cryogenic);

            for (var i = 0; i < 20; i++)
            {
                Step(phaseSystem, gameTiming, room);
                Step(phaseSystem, gameTiming, hot);
                Step(phaseSystem, gameTiming, cryogenic);
            }

            Assert.That(room.Fluid!.TryGetAmount(PlasmaKey, out var roomPlasma));
            Assert.That(hot.Fluid!.TryGetAmount(PlasmaKey, out var hotPlasma));
            cryogenic.Fluid!.TryGetAmount(TritiumKey, out var cryogenicTritium);
            var roomBoiledUnits = room.Fluid.VaporMoles / plasma.MolesPerUnit;
            var hotBoiledUnits = hot.Fluid.VaporMoles / plasma.MolesPerUnit;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(roomPlasma.Liquid, Is.GreaterThan(0.8f * plasmaUnits), "Plasma did not stay liquid at room temperature.");
                Assert.That(hotBoiledUnits, Is.GreaterThan(roomBoiledUnits), "Plasma past its boiling point did not boil.");
                Assert.That(hotPlasma.Vapor, Is.GreaterThan(0f), "Boiling plasma made no vapor to release as smoke.");
                Assert.That(hot.Temperature, Is.LessThan(hotTemperature), "Boiling plasma did not cool.");
                Assert.That(cryogenicTritium.Liquid, Is.LessThan(0.01f), "Liquid tritium at room temperature did not vaporise.");
                Assert.That(cryogenicTritium.Vapor, Is.GreaterThan(0f), "Tritium vaporised into nothing.");

                foreach (var mixture in new[] { room, hot, cryogenic })
                {
                    Assert.That(mixture.GetMoles(Gas.Plasma), Is.Zero, "Plasma reagent made plasma gas.");
                    Assert.That(mixture.GetMoles(Gas.Tritium), Is.Zero, "Tritium reagent made tritium gas.");
                }
            }
        });
    }

    [Test]
    public async Task OverfilledNetsStopPumps()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            var inlet = MakePipe(Atmospherics.T20C);
            inlet.Fluid = new KsFluidMixture();
            inlet.Fluid.AddLiquid(WaterKey, 100f);
            inlet.AdjustMoles(Gas.Nitrogen, 5f);

            var outlet = MakePipe(Atmospherics.T20C);
            outlet.Fluid = new KsFluidMixture();
            outlet.Fluid.AddLiquid(WaterKey, NodeVolume / KsFluidmos.LitersPerUnit);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(outlet.Pressure, Is.GreaterThan(Atmospherics.MaxOutputPressure), "A liquid-filled net has no hydraulic pressure.");
                Assert.That(atmosphereSystem.PumpGasTo(inlet, outlet, Atmospherics.MaxOutputPressure), Is.False,
                    "A pump kept feeding a liquid-filled net.");
            }
        });
    }

    /// <summary>
    ///     Ice takes up room as liquid does: a net packed with it has the same hydraulic pressure, and pumps stop.
    /// </summary>
    [Test]
    public async Task IcePackedNetsStopPumps()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var inlet = MakePipe(Atmospherics.T20C);
            inlet.AdjustMoles(Gas.Nitrogen, 5f);

            var outlet = MakePipe(water!.MeltingPoint - 10f);
            outlet.Fluid = new KsFluidMixture();
            outlet.Fluid.SetAmount(WaterKey, new KsFluidAmount(NodeVolume / KsFluidmos.LitersPerUnit, 0f, 0f));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(outlet.Pressure, Is.GreaterThan(Atmospherics.MaxOutputPressure), "An ice-packed net has no hydraulic pressure.");
                Assert.That(atmosphereSystem.PumpGasTo(inlet, outlet, Atmospherics.MaxOutputPressure), Is.False,
                    "A pump kept feeding an ice-packed net.");
            }
        });
    }

    /// <summary>
    ///     A pump drawing from a slurry takes its liquid, and leaves the ice behind.
    /// </summary>
    [Test]
    public async Task PumpDrawingFromASlurryMovesOnlyLiquid()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            const float iceUnits = 100f;
            var inlet = MakePipe(water!.MeltingPoint);
            inlet.Fluid = new KsFluidMixture();
            inlet.Fluid.SetAmount(WaterKey, new KsFluidAmount(iceUnits, 100f, 0f));
            AddHeadspace(inlet);

            var outlet = MakePipe(water.MeltingPoint);
            Assert.That(atmosphereSystem.PumpGasTo(inlet, outlet, Atmospherics.MaxOutputPressure), "The pump moved nothing.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(outlet.Fluid?.LiquidUnits ?? 0f, Is.GreaterThan(0f), "The pump left the slurry's liquid behind.");
                Assert.That(outlet.Fluid?.SolidUnits ?? 0f, Is.Zero, "The pump moved ice.");
                Assert.That(inlet.Fluid!.SolidUnits, Is.EqualTo(iceUnits).Within(0.01f), "The slurry lost its ice.");
            }
        });
    }
}
