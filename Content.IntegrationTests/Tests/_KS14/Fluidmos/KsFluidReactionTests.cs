#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Reactions;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Reactions;
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

- type: reagent
  id: KsTestReactionWindowFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionSteamFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionHeavyFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionGasFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionTraceFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestReactionTraceProduct
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: ksFluidReaction
  id: KsTestWindowReaction
  reactants:
    KsTestReactionWindowFeed: { amount: 1, phase: Liquid }
  products:
    KsTestReactionProduct: { amount: 1, phase: Liquid }
  minTemp: 600
  maxTemp: 800
  rate: 0.5
  referenceTemperature: 700

- type: ksFluidReaction
  id: KsTestSteamReaction
  catalystReagent: KsTestReactionProduct
  catalystReagentUnits: 5
  reactants:
    KsTestReactionSteamFeed: { amount: 1, phase: Liquid }
  products:
    KsTestReactionTraceProduct: { amount: 1, phase: Liquid }
  minTemp: 600
  rate: 0.5
  referenceTemperature: 700

- type: ksFluidReaction
  id: KsTestViolentReaction
  reactants:
    KsTestReactionHeavyFeed: { amount: 1, phase: Liquid }
  products:
    KsTestReactionProduct: { amount: 1, phase: Liquid }
  minTemp: 600
  rate: 5
  referenceTemperature: 700
  enthalpy: 50000

- type: ksFluidReaction
  id: KsTestGasMakingReaction
  reactants:
    KsTestReactionGasFeed: { amount: 1, phase: Liquid }
  products:
    Hydrogen: { amount: 1, phase: Vapor }
  minTemp: 600
  rate: 5
  referenceTemperature: 700

- type: ksFluidReaction
  id: KsTestTraceReaction
  reactants:
    KsTestReactionTraceFeed: { amount: 1, phase: Liquid }
  products:
    KsTestReactionProduct: { amount: 0.999, phase: Liquid }
    KsTestReactionTraceProduct: { amount: 0.001, phase: Liquid }
  minTemp: 600
  rate: 0.5
  referenceTemperature: 700
";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly ProtoId<ReagentPrototype> _feedReagent = "KsTestReactionFeed";
    private readonly ProtoId<ReagentPrototype> _exoFeedReagent = "KsTestReactionExoFeed";
    private readonly ProtoId<ReagentPrototype> _gatedFeedReagent = "KsTestReactionGatedFeed";
    private readonly ProtoId<ReagentPrototype> _productReagent = "KsTestReactionProduct";
    private readonly ProtoId<ReagentPrototype> _windowFeedReagent = "KsTestReactionWindowFeed";
    private readonly ProtoId<ReagentPrototype> _steamFeedReagent = "KsTestReactionSteamFeed";
    private readonly ProtoId<ReagentPrototype> _heavyFeedReagent = "KsTestReactionHeavyFeed";
    private readonly ProtoId<ReagentPrototype> _gasFeedReagent = "KsTestReactionGasFeed";
    private readonly ProtoId<ReagentPrototype> _traceFeedReagent = "KsTestReactionTraceFeed";
    private readonly ProtoId<ReagentPrototype> _traceProductReagent = "KsTestReactionTraceProduct";

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
                // Against the heat the reaction moved, which is far smaller than the mixture's whole energy.
                Assert.That(endothermicAfter, Is.EqualTo(endothermicEnergy).Within(enthalpy * endothermicExtent * 0.05f), "The endothermic reaction did not conserve energy.");
                Assert.That(exothermicAfter, Is.EqualTo(exothermicEnergy).Within(enthalpy * exothermicExtent * 0.05f), "The exothermic reaction did not conserve energy.");
            }
        });
    }

    /// <summary>
    ///     The first with far more heat than the cap allows, and the second with none, moving the temperature only by
    ///         what it does to the heat capacity.
    /// </summary>
    [Test]
    public async Task OneStepNeverMovesTheTemperaturePastTheCap()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            const float startTemperature = 700f;
            var violent = MakePipe(startTemperature);
            violent.Fluid!.AddLiquid(new KsFluidReagentKey(_heavyFeedReagent, null), 100f);
            var gasMaking = MakePipe(startTemperature);
            gasMaking.Fluid!.AddLiquid(new KsFluidReagentKey(_gasFeedReagent, null), 100f);

            Step(phaseSystem, gameTiming, violent);
            Step(phaseSystem, gameTiming, gasMaking);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(violent, _productReagent), Is.GreaterThan(0f));
                Assert.That(gasMaking.GetMoles(Gas.Hydrogen), Is.GreaterThan(0f));
                Assert.That(startTemperature - violent.Temperature, Is.LessThanOrEqualTo(KsFluidReactionSystem.MaxReactionDeltaT + 0.01f),
                    "A strongly endothermic step passed the cap.");
                Assert.That(MathF.Abs(startTemperature - gasMaking.Temperature), Is.LessThanOrEqualTo(KsFluidReactionSystem.MaxReactionDeltaT + 0.01f),
                    "A step that raises the heat capacity passed the cap.");
            }
        });
    }

    [Test]
    public async Task RateFollowsArrheniusAroundItsReferenceTemperature()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();
        var reaction = server.ProtoMan.Index<KsFluidReactionPrototype>("KsTestEndothermicReaction");

        await server.WaitAssertion(() =>
        {
            float Extent(float temperature)
            {
                var pipe = MakePipe(temperature);
                pipe.Fluid!.AddLiquid(new KsFluidReagentKey(_feedReagent, null), 100f);
                Step(phaseSystem, gameTiming, pipe);
                return Liquid(pipe, _productReagent);
            }

            float Expected(float temperature)
            {
                var rate = reaction.Rate * MathF.Exp(reaction.ActivationTemperature * (1f / reaction.ReferenceTemperature - 1f / temperature));
                return 100f * (1f - MathF.Exp(-rate * 0.5f));
            }

            var below = reaction.ReferenceTemperature - 50f;
            var above = reaction.ReferenceTemperature + 50f;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Extent(below), Is.EqualTo(Expected(below)).Within(Expected(below) * 0.05f), "Below its reference temperature it ran at the wrong rate.");
                Assert.That(Extent(above), Is.EqualTo(Expected(above)).Within(Expected(above) * 0.05f), "Above its reference temperature it ran at the wrong rate.");
            }
        });
    }

    [Test]
    public async Task StopsAboveItsMaximumTemperature()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var inside = MakePipe(700f);
            inside.Fluid!.AddLiquid(new KsFluidReagentKey(_windowFeedReagent, null), 100f);
            var above = MakePipe(900f);
            above.Fluid!.AddLiquid(new KsFluidReagentKey(_windowFeedReagent, null), 100f);

            Step(phaseSystem, gameTiming, inside);
            Step(phaseSystem, gameTiming, above);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(inside, _productReagent), Is.GreaterThan(0f), "It didn't run inside its window.");
                Assert.That(Liquid(above, _productReagent), Is.Zero, "It ran above its maximum temperature.");
            }
        });
    }

    [Test]
    public async Task NeedsItsCatalystReagentAndKeepsIt()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            const float catalystUnits = 10f;
            var without = MakePipe(700f);
            without.Fluid!.AddLiquid(new KsFluidReagentKey(_steamFeedReagent, null), 100f);
            var with = MakePipe(700f);
            with.Fluid!.AddLiquid(new KsFluidReagentKey(_steamFeedReagent, null), 100f);
            with.Fluid.AddLiquid(new KsFluidReagentKey(_productReagent, null), catalystUnits);

            Step(phaseSystem, gameTiming, without);
            Step(phaseSystem, gameTiming, with);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(without, _traceProductReagent), Is.Zero, "It ran without its catalyst reagent.");
                Assert.That(Liquid(with, _traceProductReagent), Is.GreaterThan(0f), "It didn't run with its catalyst reagent.");
                Assert.That(Liquid(with, _productReagent), Is.EqualTo(catalystUnits).Within(0.001f), "Its catalyst reagent was used up.");
            }
        });
    }

    /// <summary>
    ///     What a reaction might do is worked out once and kept, so a reagent arriving later has to refresh that.
    /// </summary>
    [Test]
    public async Task AReactantArrivingLaterStillReacts()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var pipe = MakePipe(700f);
            pipe.Fluid!.AddLiquid(new KsFluidReagentKey(_productReagent, null), 10f);
            Step(phaseSystem, gameTiming, pipe);

            pipe.Fluid.AddLiquid(new KsFluidReagentKey(_feedReagent, null), 100f);
            Step(phaseSystem, gameTiming, pipe);

            Assert.That(Liquid(pipe, _feedReagent), Is.LessThan(100f), "A reactant that wasn't there at first was never picked up.");
        });
    }

    /// <summary>
    ///     A prototype reload replaces the reactions a mixture has cached, and those have to go even if its reagents didn't
    ///         change.
    /// </summary>
    [Test]
    public async Task ReloadedReactionsReplaceCachedOnes()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var fluid = new KsFluidMixture();
            fluid.SetReactionCandidates([], float.PositiveInfinity, indexVersion: 1);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fluid.ReactionCandidatesStale(1), Is.False);
                Assert.That(fluid.ReactionCandidatesStale(2), "Cached reactions outlived a reload.");
            }
        });
    }

    /// <summary>
    ///     A table drops anything under its minimum, so a product too small to keep has to wait until it can be kept.
    /// </summary>
    [Test]
    public async Task ATraceOfProductIsKeptRatherThanLost()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            const float feed = 3f;
            var pipe = MakePipe(700f);
            pipe.Fluid!.AddLiquid(new KsFluidReagentKey(_traceFeedReagent, null), feed);

            for (var i = 0; i < 40; i++)
            {
                Step(phaseSystem, gameTiming, pipe);
            }

            var total = Liquid(pipe, _traceFeedReagent) + Liquid(pipe, _productReagent) + Liquid(pipe, _traceProductReagent);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(pipe, _traceProductReagent), Is.GreaterThan(0f), "The trace product never appeared.");
                // Feed's last dust goes under the table's minimum, as any reagent's does; the trace product would be 0.003.
                Assert.That(total, Is.EqualTo(feed).Within(KsFluidmos.MinLiquidUnits * 1.5f), "Mass was lost with the trace product.");
            }
        });
    }

    [Test]
    public async Task EqualMixturesHashTheSameWhateverOrderTheyFilledIn()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var forwards = new KsFluidMixture();
            forwards.AddLiquid(new KsFluidReagentKey(_feedReagent, null), 0.1f);
            forwards.AddLiquid(new KsFluidReagentKey(_productReagent, null), 1000f);
            forwards.AddLiquid(new KsFluidReagentKey(_traceFeedReagent, null), 0.2f);

            var backwards = new KsFluidMixture();
            backwards.AddLiquid(new KsFluidReagentKey(_traceFeedReagent, null), 0.2f);
            backwards.AddLiquid(new KsFluidReagentKey(_productReagent, null), 1000f);
            backwards.AddLiquid(new KsFluidReagentKey(_feedReagent, null), 0.1f);

            Assert.That(forwards, Is.EqualTo(backwards));
            Assert.That(forwards.GetHashCode(), Is.EqualTo(backwards.GetHashCode()));
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
