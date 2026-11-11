#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Supercritical extraction: a gas past its critical point, dense enough, dissolves condensed reagents by raising
///         their vapor pressure, lets them go again when the pressure drops, holds them past a cold wall, and is weak on
///         water. Also the analyzer's readouts of it.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsSupercriticalTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> DieselReagent = "KsDiesel";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> CarbonDioxideReagent = "CarbonDioxide";
    private static readonly KsFluidReagentKey DieselKey = new(DieselReagent, null);
    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);

    private const float NodeVolume = 200f;
    private const float StepSeconds = 0.5f;

    /// <summary>
    ///     Just past carbon dioxide's critical temperature of about 304 K.
    /// </summary>
    private const float ExtractionTemperature = 320f;

    /// <summary>
    ///     Dense enough to dissolve diesel a few hundred times over.
    /// </summary>
    private const float SupercriticalPressure = 20000f;

    /// <summary>
    ///     Past the critical temperature, but far too thin to dissolve anything.
    /// </summary>
    private const float ThinPressure = 4000f;

    private static GasMixture MakePipe(float temperature, Gas gas, float pressure)
    {
        var mixture = new GasMixture(NodeVolume) { Temperature = temperature, FluidCapable = true };
        mixture.AdjustMoles(gas, pressure * NodeVolume / (Atmospherics.R * temperature));
        return mixture;
    }

    private static void Step(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture)
    {
        if (mixture.Fluid != null)
            mixture.Fluid.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(StepSeconds);

        phaseSystem.UpdatePhases(mixture, holder: null);
    }

    private static KsFluidAmount Amount(GasMixture mixture, KsFluidReagentKey key)
    {
        return mixture.Fluid?.TryGetAmount(key, out var amount) == true ? amount : default;
    }

    [Test]
    public async Task CriticalPointAndDensityMath()
    {
        var server = Pair.Server;
        var gasThermoSystem = server.System<KsGasThermoSystem>();

        await server.WaitAssertion(() =>
        {
            var carbonDioxide = (int)Gas.CarbonDioxide;
            var a = gasThermoSystem.VanDerWaalsA[carbonDioxide];
            var b = gasThermoSystem.VanDerWaalsB[carbonDioxide];

            float Density(float pressure, float temperature) =>
                KsThrottling.ReducedDensity(a, b, pressure / (Atmospherics.R * temperature), temperature);

            var nitrogen = (int)Gas.Nitrogen;
            var nitrogenCanister = KsThrottling.ReducedDensity(
                gasThermoSystem.VanDerWaalsA[nitrogen],
                gasThermoSystem.VanDerWaalsB[nitrogen],
                Atmospherics.MaxOutputPressure / (Atmospherics.R * Atmospherics.T20C),
                Atmospherics.T20C);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gasThermoSystem.CriticalPressures[carbonDioxide], Is.EqualTo(7390f).Within(50f), "Carbon dioxide's critical pressure is off its van der Waals value.");
                Assert.That(gasThermoSystem.CriticalTemperatures[carbonDioxide], Is.EqualTo(304f).Within(1f), "Carbon dioxide's critical temperature is off its van der Waals value.");
                Assert.That(gasThermoSystem.CriticalTemperatures[(int)Gas.Frezon], Is.LessThan(float.PositiveInfinity), "An unlinked gas with constants has no critical temperature.");

                // Real carbon dioxide: about 1.6 times its critical density at 20 MPa and 320 K; this estimate may run a
                //     little high, but must be far denser than nitrogen at a canister's pressure.
                Assert.That(Density(SupercriticalPressure, ExtractionTemperature), Is.InRange(1.5f, 2.5f), "Dense supercritical carbon dioxide isn't dense.");
                Assert.That(Density(ThinPressure, ExtractionTemperature), Is.LessThan(1f), "Carbon dioxide below its critical pressure counts as dense.");
                Assert.That(nitrogenCanister, Is.LessThan(0.5f), "Nitrogen at a canister's pressure counts as a dense solvent.");

                Assert.That(KsPhaseMath.SupercriticalLogEnhancement(0.9f, affinity: 1f), Is.Zero, "A solvent below its critical density dissolves anything.");
                Assert.That(KsPhaseMath.SupercriticalLogEnhancement(2f, affinity: 1f), Is.GreaterThan(KsPhaseMath.SupercriticalLogEnhancement(1.5f, affinity: 1f)),
                    "A denser solvent doesn't dissolve more.");
                Assert.That(KsPhaseMath.SupercriticalLogEnhancement(1000f, affinity: 1f), Is.EqualTo(KsFluidmos.MaxSupercriticalLogEnhancement),
                    "The enhancement isn't capped.");
            }
        });
    }

    /// <summary>
    ///     Diesel barely evaporates at 320 K, but dense supercritical carbon dioxide dissolves hundreds of times more of
    ///         it than the same gas below its critical pressure.
    /// </summary>
    [Test]
    public async Task DenseSupercriticalCarbonDioxideDissolvesDiesel()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gasThermoSystem = server.System<KsGasThermoSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var dense = MakePipe(ExtractionTemperature, Gas.CarbonDioxide, SupercriticalPressure);
            var thin = MakePipe(ExtractionTemperature, Gas.CarbonDioxide, ThinPressure);
            foreach (var mixture in new[] { dense, thin })
            {
                mixture.Fluid = new KsFluidMixture();
                mixture.Fluid.AddLiquid(DieselKey, 50f);
            }

            for (var i = 0; i < 40; i++)
            {
                Step(phaseSystem, gameTiming, dense);
                Step(phaseSystem, gameTiming, thin);
            }

            var denseVapor = Amount(dense, DieselKey).Vapor;
            var thinVapor = Amount(thin, DieselKey).Vapor;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(thinVapor, Is.GreaterThan(0f), "Diesel didn't evaporate at all, so the comparison means nothing.");
                Assert.That(denseVapor, Is.GreaterThan(50f * thinVapor), "Dense supercritical carbon dioxide didn't dissolve diesel.");
                Assert.That(dense.Temperature, Is.GreaterThan(gasThermoSystem.CriticalTemperatures[(int)Gas.CarbonDioxide]), "Dissolving cooled the solvent below its critical temperature.");
            }
        });
    }

    /// <summary>
    ///     What dense carbon dioxide dissolved falls back out as liquid when the pressure drops.
    /// </summary>
    [Test]
    public async Task DroppingThePressureDropsTheExtractOut()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var loaded = MakePipe(ExtractionTemperature, Gas.CarbonDioxide, SupercriticalPressure);
            loaded.Fluid = new KsFluidMixture();
            loaded.Fluid.AddLiquid(DieselKey, 30f);
            for (var i = 0; i < 40; i++)
            {
                Step(phaseSystem, gameTiming, loaded);
            }

            var dissolved = Amount(loaded, DieselKey).Vapor;
            Assert.That(Amount(loaded, DieselKey).Liquid, Is.LessThan(1f), "The diesel never dissolved, so there is nothing to drop out.");

            // Through a valve into a collecting pipe at a tenth of the pressure.
            var collector = new GasMixture(NodeVolume) { Temperature = ExtractionTemperature, FluidCapable = true };
            collector.AdjustMoles(Gas.CarbonDioxide, loaded.GetMoles(Gas.CarbonDioxide) * 0.1f);
            collector.Fluid = new KsFluidMixture();
            collector.Fluid.SetAmount(DieselKey, new KsFluidAmount(solid: 0f, liquid: 0f, vapor: dissolved));

            for (var i = 0; i < 120; i++)
            {
                Step(phaseSystem, gameTiming, collector);
            }

            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(DieselReagent, out var diesel));
            var droppedOut = Amount(collector, DieselKey).Liquid * diesel!.MolesPerUnit;
            Assert.That(droppedOut, Is.GreaterThan(0.7f * dissolved), "Less than 70% of the extract fell out when the pressure dropped.");
        });
    }

    /// <summary>
    ///     Water is polar, so supercritical carbon dioxide raises its vapor pressure far less than diesel's.
    /// </summary>
    [Test]
    public async Task WaterResistsSupercriticalCarbonDioxide()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gasThermoSystem = server.System<KsGasThermoSystem>();
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            Assert.That(propertiesSystem.TryGetProperties(DieselReagent, out var diesel));

            var mixture = MakePipe(ExtractionTemperature, Gas.CarbonDioxide, SupercriticalPressure);
            var density = gasThermoSystem.ReducedSolventDensity(mixture);
            var waterFactor = MathF.Exp(KsPhaseMath.SupercriticalLogEnhancement(density, water!.SupercriticalAffinity));
            var dieselFactor = MathF.Exp(KsPhaseMath.SupercriticalLogEnhancement(density, diesel!.SupercriticalAffinity));

            // And in a pipe: the water vapor over liquid water stays within a few times its ordinary saturation.
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.AddLiquid(WaterKey, 200f);
            for (var i = 0; i < 40; i++)
            {
                Step(phaseSystem, gameTiming, mixture);
            }

            var saturationPressure = KsPhaseMath.SaturationPressure(water.VaporizationEntropy, water.BoilingPoint, mixture.Temperature);
            var ordinaryVapor = saturationPressure * mixture.FreeVolume / (Atmospherics.R * mixture.Temperature);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dieselFactor, Is.GreaterThan(100f), "Dense supercritical carbon dioxide barely raises diesel's vapor pressure.");
                Assert.That(waterFactor, Is.LessThan(10f), "Supercritical carbon dioxide dissolves water as well as anything.");
                Assert.That(mixture.GetMoles(Gas.WaterVapor), Is.LessThan(1.05f * waterFactor * ordinaryVapor), "Water dissolved past its enhanced equilibrium.");
                Assert.That(mixture.GetMoles(Gas.WaterVapor), Is.GreaterThan(1.5f * ordinaryVapor), "Supercritical carbon dioxide didn't raise water's vapor at all.");
            }
        });
    }

    /// <summary>
    ///     A cold radiator in a supercritical line doesn't strip out what the solvent carries, though it condenses the same
    ///         vapor from a thin gas.
    /// </summary>
    [Test]
    public async Task ACoolWallKeepsTheExtractInSolution()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();

        await server.WaitAssertion(() =>
        {
            GasMixture MakePacket()
            {
                var packet = new GasMixture(10f) { Temperature = ExtractionTemperature, FluidCapable = true };
                packet.AdjustMoles(Gas.CarbonDioxide, 10f);
                packet.Fluid = new KsFluidMixture();
                packet.Fluid.SetAmount(DieselKey, new KsFluidAmount(solid: 0f, liquid: 0f, vapor: 0.001f));
                return packet;
            }

            var wall = new KsWallHeatTransfer(Conductance: 100f, ConvectionTemperature: 250f, RadiativeCoefficient: 0f, RadiationTemperature: 0f);
            var dense = MakePacket();
            var thin = MakePacket();
            phaseSystem.ExchangeAtWall(dense, SupercriticalPressure, wall, deltaTime: 0.5f);
            phaseSystem.ExchangeAtWall(thin, 2000f, wall, deltaTime: 0.5f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Amount(dense, DieselKey).Liquid, Is.Zero, "A cold wall stripped dissolved diesel out of a dense supercritical line.");
                Assert.That(Amount(thin, DieselKey).Liquid, Is.GreaterThan(0f), "A cold wall didn't condense diesel out of a thin gas, so the comparison means nothing.");
            }
        });
    }

    /// <summary>
    ///     The analyzer's readouts: where each reagent boils at the pipe's pressure, which gas is past its critical
    ///         temperature, and how strong a solvent the gas is.
    /// </summary>
    [Test]
    public async Task AnalyzerShowsBoilingPointsAndSolventStrength()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();

        await server.WaitAssertion(() =>
        {
            var fluid = new KsFluidMixture();
            fluid.AddLiquid(WaterKey, 10f);
            fluid.AddLiquid(new KsFluidReagentKey(CarbonDioxideReagent, null), 10f);

            var atOneAtmosphere = KsFluidEntry.FromMixture(fluid, propertiesSystem, Atmospherics.OneAtmosphere, ExtractionTemperature)!;
            var atHalf = KsFluidEntry.FromMixture(fluid, propertiesSystem, 0.5f * Atmospherics.OneAtmosphere, ExtractionTemperature)!;
            var waterAtOne = Array.Find(atOneAtmosphere, entry => entry.Reagent == WaterReagent);
            var waterAtHalf = Array.Find(atHalf, entry => entry.Reagent == WaterReagent);
            var carbonDioxide = Array.Find(atOneAtmosphere, entry => entry.Reagent == CarbonDioxideReagent);

            var dense = MakePipe(ExtractionTemperature, Gas.CarbonDioxide, SupercriticalPressure);
            var air = MakePipe(Atmospherics.T20C, Gas.Nitrogen, Atmospherics.MaxOutputPressure);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(waterAtOne.BoilingPoint, Is.EqualTo(373.15f).Within(0.5f), "Water doesn't boil at 373 K at one atmosphere.");
                Assert.That(waterAtHalf.BoilingPoint, Is.LessThan(waterAtOne.BoilingPoint - 10f), "Water's boiling point didn't drop with the pressure.");
                Assert.That(waterAtOne.MeltingPoint, Is.EqualTo(273.15f).Within(0.5f), "Water's melting point is wrong.");
                Assert.That(waterAtOne.Supercritical, Is.False, "Water at 320 K is marked supercritical.");
                Assert.That(carbonDioxide.Supercritical, Is.True, "Carbon dioxide past its critical temperature isn't marked supercritical.");
                Assert.That(phaseSystem.SolventEnhancement(dense), Is.GreaterThan(100f), "The analyzer reads dense carbon dioxide as a weak solvent.");
                Assert.That(phaseSystem.SolventEnhancement(air), Is.EqualTo(1f), "The analyzer reads nitrogen in a canister as a solvent.");
            }
        });
    }

    /// <summary>
    ///     The guidebook calculator gives a reagent's own boiling point at one atmosphere, a lower one at lower pressure,
    ///         and a gas's critical point.
    /// </summary>
    [Test]
    public async Task GuidebookCalculatorMatchesThePipes()
    {
        var client = Pair.Client;

        await client.WaitAssertion(() =>
        {
            var calculator = new Content.Client._KS14.Guidebook.Controls.KsGuidePhaseCalculatorEmbed();
            Assert.That(calculator.TryParseTag(new Dictionary<string, string> { ["Reagents"] = "Water,CarbonDioxide" }, out _), "The calculator didn't parse its tag.");

            calculator.SetInputs(0, "101.325", "373.15");
            var water = calculator.ResultText;
            calculator.SetInputs(0, "50", "293.15");
            var waterAtHalf = calculator.ResultText;
            calculator.SetInputs(1, "101.325", "293.15");
            var carbonDioxide = calculator.ResultText;

            var defaultCalculator = new Content.Client._KS14.Guidebook.Controls.KsGuidePhaseCalculatorEmbed();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(water, Does.Contain("373.").Or.Contain("373,"), "Water doesn't boil at 373 K at one atmosphere.");
                Assert.That(water, Does.Contain("101.3").Or.Contain("101,3"), "Water's vapor pressure at its boiling point isn't one atmosphere.");
                Assert.That(waterAtHalf, Does.Not.Contain("373."), "Water's boiling point didn't move with the pressure.");
                Assert.That(carbonDioxide, Does.Contain("303.8"), "Carbon dioxide's critical point isn't shown.");
                Assert.That(defaultCalculator.TryParseTag(new Dictionary<string, string>(), out _), "The calculator with no reagents listed didn't parse.");
            }
        });
    }
}
