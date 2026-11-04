#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Reactions;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Chemical reactions inside fluid-capable pipe mixtures: the temperature window, mass balance, the heat they take or
///         give, the idle gate, a partial-pressure gate, and linked reagents reacting from and into gas moles.
/// </summary>
[TestOf(typeof(KsFluidReactionSystem))]
public sealed class KsFluidReactionTests : GameTest
{
    // Effectively non-volatile and never freezing at the temperatures used, so only the reactions move anything.
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestReactionFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionExoFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionGatedFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionProduct
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: ksFluidReaction
  id: KsTestEndothermicReaction
  reactants:
    KsTestReactionFeed: { amount: 1, phase: Liquid }
  products:
    KsTestReactionProduct: { amount: 1, phase: Liquid }
  minTemp: 600
  rate: 0.5
  referenceTemperature: 700
  activationTemperature: 5000
  enthalpy: 50

- type: ksFluidReaction
  id: KsTestExothermicReaction
  reactants:
    KsTestReactionExoFeed: { amount: 1, phase: Liquid }
  products:
    KsTestReactionProduct: { amount: 1, phase: Liquid }
  minTemp: 600
  rate: 0.5
  referenceTemperature: 700
  enthalpy: -50

- type: ksFluidReaction
  id: KsTestOxygenGatedReaction
  reactants:
    KsTestReactionGatedFeed: { amount: 1, phase: Liquid }
    Oxygen: { amount: 1, phase: Vapor }
  products:
    Water: { amount: 2, phase: Vapor }
  minTemp: 600
  rate: 0.5
  referenceTemperature: 700
  minPartialPressure:
    Oxygen: 50
";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly ProtoId<ReagentPrototype> _feedReagent = "KsTestReactionFeed";
    private readonly ProtoId<ReagentPrototype> _exoFeedReagent = "KsTestReactionExoFeed";
    private readonly ProtoId<ReagentPrototype> _gatedFeedReagent = "KsTestReactionGatedFeed";
    private readonly ProtoId<ReagentPrototype> _productReagent = "KsTestReactionProduct";

    private const float NodeVolume = 200f;
    private const float StepSeconds = 0.5f;

    private static GasMixture MakePipe(float temperature)
    {
        var mixture = new GasMixture(NodeVolume) { Temperature = temperature, FluidCapable = true, Fluid = new KsFluidMixture() };
        mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * NodeVolume / (Atmospherics.R * temperature));
        return mixture;
    }

    /// <summary>
    ///     Runs one phase step, reactions included, as if <see cref="StepSeconds"/> had passed since the last.
    /// </summary>
    private static void Step(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture)
    {
        if (mixture.Fluid != null)
        {
            mixture.Fluid.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(StepSeconds);
            if (mixture.Fluid.LastReactionTime != null)
                mixture.Fluid.LastReactionTime = gameTiming.CurTime - TimeSpan.FromSeconds(StepSeconds);
        }

        phaseSystem.UpdatePhases(mixture, holder: null);
    }

    private static float Liquid(GasMixture mixture, ProtoId<ReagentPrototype> reagent)
    {
        return mixture.Fluid != null && mixture.Fluid.TryGetAmount(new KsFluidReagentKey(reagent, null), out var amount) ? amount.Liquid : 0f;
    }

    [Test]
    public async Task ConvertsAboveItsMinimumTemperatureOnlyAndBalancesMass()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var cold = MakePipe(500f);
            cold.Fluid!.AddLiquid(new KsFluidReagentKey(_feedReagent, null), 100f);

            var hot = MakePipe(700f);
            hot.Fluid!.AddLiquid(new KsFluidReagentKey(_feedReagent, null), 100f);

            for (var i = 0; i < 10; i++)
            {
                Step(phaseSystem, gameTiming, cold);
                Step(phaseSystem, gameTiming, hot);
            }

            var product = Liquid(hot, _productReagent);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(cold, _productReagent), Is.Zero, "The reaction ran below its minimum temperature.");
                Assert.That(cold.Fluid!.LastReactionTime, Is.Null, "A mixture too cold for any reaction still stepped reactions.");
                Assert.That(product, Is.GreaterThan(1f), "The reaction did not run above its minimum temperature.");
                Assert.That(Liquid(hot, _feedReagent) + product, Is.EqualTo(100f).Within(0.01f), "The reaction did not conserve mass.");
            }
        });
    }

    [Test]
    public async Task EndothermicCoolsExothermicHeatsAndEnergyIsConserved()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            const float startTemperature = 700f;
            const float enthalpy = 50f; // the test reactions' magnitude

            var endothermic = MakePipe(startTemperature);
            endothermic.Fluid!.AddLiquid(new KsFluidReagentKey(_feedReagent, null), 100f);
            var exothermic = MakePipe(startTemperature);
            exothermic.Fluid!.AddLiquid(new KsFluidReagentKey(_exoFeedReagent, null), 100f);

            var endothermicEnergy = atmosphereSystem.GetHeatCapacity(endothermic, applyScaling: false) * startTemperature;
            var exothermicEnergy = atmosphereSystem.GetHeatCapacity(exothermic, applyScaling: false) * startTemperature;

            Step(phaseSystem, gameTiming, endothermic);
            Step(phaseSystem, gameTiming, exothermic);

            var endothermicExtent = Liquid(endothermic, _productReagent);
            var exothermicExtent = Liquid(exothermic, _productReagent);
            var endothermicAfter = atmosphereSystem.GetHeatCapacity(endothermic, applyScaling: false) * endothermic.Temperature
                                   + enthalpy * endothermicExtent;
            var exothermicAfter = atmosphereSystem.GetHeatCapacity(exothermic, applyScaling: false) * exothermic.Temperature
                                  - enthalpy * exothermicExtent;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(endothermicExtent, Is.GreaterThan(0f));
                Assert.That(exothermicExtent, Is.GreaterThan(0f));
                Assert.That(endothermic.Temperature, Is.LessThan(startTemperature), "An endothermic reaction did not cool its mixture.");
                Assert.That(exothermic.Temperature, Is.GreaterThan(startTemperature), "An exothermic reaction did not heat its mixture.");
                Assert.That(endothermicAfter, Is.EqualTo(endothermicEnergy).Within(endothermicEnergy * 0.01f), "The endothermic reaction did not conserve energy.");
                Assert.That(exothermicAfter, Is.EqualTo(exothermicEnergy).Within(exothermicEnergy * 0.01f), "The exothermic reaction did not conserve energy.");
                Assert.That(endothermic.Temperature, Is.GreaterThanOrEqualTo(startTemperature - KsFluidReactionSystem.MaxReactionDeltaT - 0.01f),
                    "One reaction step moved the temperature past its cap.");
            }
        });
    }

    [Test]
    public async Task PartialPressureGateAndLinkedReagentsUseGasMoles()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            const float temperature = 700f;
            var starved = MakePipe(temperature);
            starved.Fluid!.AddLiquid(new KsFluidReagentKey(_gatedFeedReagent, null), 100f);
            // Some oxygen, but under the gate's 50 kPa.
            starved.AdjustMoles(Gas.Oxygen, 20f * NodeVolume / (Atmospherics.R * temperature));

            var fed = MakePipe(temperature);
            fed.Fluid!.AddLiquid(new KsFluidReagentKey(_gatedFeedReagent, null), 100f);
            fed.AdjustMoles(Gas.Oxygen, 200f * NodeVolume / (Atmospherics.R * temperature));

            var starvedOxygen = starved.GetMoles(Gas.Oxygen);
            var fedOxygen = fed.GetMoles(Gas.Oxygen);

            for (var i = 0; i < 4; i++)
            {
                Step(phaseSystem, gameTiming, starved);
                Step(phaseSystem, gameTiming, fed);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(starved.GetMoles(Gas.WaterVapor), Is.Zero, "The reaction ran under its minimum partial pressure.");
                Assert.That(starved.GetMoles(Gas.Oxygen), Is.EqualTo(starvedOxygen).Within(1e-4f));
                Assert.That(fed.GetMoles(Gas.Oxygen), Is.LessThan(fedOxygen), "A linked reactant was not taken from its gas moles.");
                Assert.That(fed.GetMoles(Gas.WaterVapor), Is.GreaterThan(0f), "A linked vapor product did not become its gas moles.");
                Assert.That(Liquid(fed, _gatedFeedReagent), Is.LessThan(100f));
            }
        });
    }
}
