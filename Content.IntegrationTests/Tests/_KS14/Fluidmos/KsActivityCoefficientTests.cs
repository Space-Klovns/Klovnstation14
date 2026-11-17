#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Non-ideal mixing in pipes: Margules activity coefficients make ethanol and water an azeotrope that distillation
///         cannot pass, immiscible liquids each keep their own full vapor pressure, and a minimum-boiling mixture's
///         bubble point lies below both of its parts' boiling points.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsActivityCoefficientTests : GameTest
{
    // An oil that does not mix with water at all.
    [TestPrototypes]
    private const string Prototypes = @"
- type: ksFluidMixingFamily
  id: KsTestOilFamily
  immiscibleWith: [ KsAqueous ]

- type: reagent
  id: KsTestImmiscibleOil
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-oily
  boilingPoint: 450
  meltingPoint: 200
  fluidmos:
    molarMass: 100
    mixingFamily: KsTestOilFamily

- type: reagent
  id: KsTestActivityIce
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 273
  fluidmos:
    molarMass: 20
    fusionHeat: 300
    fusionEntropy: 2.65

- type: reagent
  id: KsTestActivitySolvent
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 50
  fluidmos:
    molarMass: 20

- type: ksFluidPairInteraction
  id: KsTestActivityPair
  first: KsTestActivityIce
  second: KsTestActivitySolvent
  margules: 2

- type: reagent
  id: KsTestWallFirst
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 360
  meltingPoint: 100
  fluidmos:
    molarMass: 40

- type: reagent
  id: KsTestWallSecond
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100
  fluidmos:
    molarMass: 40

- type: reagent
  id: KsTestWallIdealSecond
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100
  fluidmos:
    molarMass: 40

- type: ksFluidPairInteraction
  id: KsTestWallPair
  first: KsTestWallFirst
  second: KsTestWallSecond
  margules: 2
";

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> CarbonDioxideReagent = "CarbonDioxide";
    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey EthanolKey = new(EthanolReagent, null);

    // Instance field: this id exists only in the test prototypes above.
    private readonly ProtoId<ReagentPrototype> _oilReagent = "KsTestImmiscibleOil";
    private readonly ProtoId<ReagentPrototype> _freezingReagent = "KsTestActivityIce";
    private readonly ProtoId<ReagentPrototype> _freezingSolvent = "KsTestActivitySolvent";
    private readonly ProtoId<ReagentPrototype> _wallFirst = "KsTestWallFirst";
    private readonly ProtoId<ReagentPrototype> _wallSecond = "KsTestWallSecond";
    private readonly ProtoId<ReagentPrototype> _wallIdealSecond = "KsTestWallIdealSecond";

    private const float NodeVolume = 200f;

    private static GasMixture MakePipe(float temperature)
    {
        var mixture = new GasMixture(NodeVolume) { Temperature = temperature, FluidCapable = true, Fluid = new KsFluidMixture() };
        return mixture;
    }

    private static void AddHeadspace(GasMixture mixture)
    {
        mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * mixture.FreeVolume / (Atmospherics.R * mixture.Temperature));
    }

    /// <summary>
    ///     Steps until no vapor changes any more, so the liquid and its vapor are at equilibrium.
    /// </summary>
    private static void Settle(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture)
    {
        var lastVapor = -1f;
        for (var steps = 0; steps < 5000; steps++)
        {
            mixture.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
            phaseSystem.UpdatePhases(mixture, holder: null);
            var vapor = mixture.GetMoles(Gas.WaterVapor) + mixture.Fluid!.VaporMoles;
            if (MathF.Abs(vapor - lastVapor) <= 1e-7f * vapor)
                return;

            lastVapor = vapor;
        }
    }

    /// <summary>
    ///     Ethanol's mole fraction in the vapor, and in the liquid.
    /// </summary>
    private static (float Vapor, float Liquid) EthanolFractions(GasMixture mixture, KsFluidReagentProperties water, KsFluidReagentProperties ethanol)
    {
        mixture.Fluid!.TryGetAmount(WaterKey, out var waterAmount);
        mixture.Fluid.TryGetAmount(EthanolKey, out var ethanolAmount);
        var waterVapor = mixture.GetMoles(Gas.WaterVapor);
        var waterLiquid = waterAmount.Liquid * water.MolesPerUnit;
        var ethanolLiquid = ethanolAmount.Liquid * ethanol.MolesPerUnit;
        return (ethanolAmount.Vapor / (ethanolAmount.Vapor + waterVapor), ethanolLiquid / (ethanolLiquid + waterLiquid));
    }

    private static void Step(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture, int steps, float holdTemperature)
    {
        for (var i = 0; i < steps; i++)
        {
            mixture.Temperature = holdTemperature;
            mixture.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
            phaseSystem.UpdatePhases(mixture, holder: null);
        }
    }

    /// <summary>
    ///     Where a reagent freezes depends on the mixture as a whole, not on how much of it has frozen so far: between its
    ///         depressed melting point and its pure one, ice in the same mixture has to melt.
    /// </summary>
    [Test]
    public async Task FreezingDoesNotRaiseItsOwnFreezingPoint()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();
        var melting = server.ProtoMan.Index(_freezingReagent).MeltingPoint!.Value;

        await server.WaitAssertion(() =>
        {
            // Equal moles at the clamped Margules 2: ln γ = 0.5, so x γ = 0.82 and it melts about 7% lower.
            var between = melting / 1.03f;
            var mixture = MakePipe(between);
            mixture.Fluid!.AddLiquid(new KsFluidReagentKey(_freezingSolvent, null), 100f);
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(_freezingReagent, null), 5f);
            mixture.Fluid.SetAmount(new KsFluidReagentKey(_freezingReagent, null), new KsFluidAmount { Liquid = 5f, Solid = 95f });
            AddHeadspace(mixture);

            Step(phaseSystem, gameTiming, mixture, 400, between);

            mixture.Fluid.TryGetAmount(new KsFluidReagentKey(_freezingReagent, null), out var amount);
            Assert.That(amount.Solid, Is.Zero, "Ice stayed frozen in a mixture that melts below this temperature.");
        });
    }

    /// <summary>
    ///     A pure reagent below its triple point has no stable liquid, but one dissolved in something can have.
    /// </summary>
    [Test]
    public async Task ADissolvedReagentStaysLiquidBelowItsPureTriplePoint()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            const float temperature = 300f;
            var mixture = MakePipe(temperature);
            mixture.Fluid!.AddLiquid(new KsFluidReagentKey(_freezingSolvent, null), 100f);
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(CarbonDioxideReagent, null), 10f);
            AddHeadspace(mixture);

            Step(phaseSystem, gameTiming, mixture, 4, temperature);

            mixture.Fluid.TryGetAmount(new KsFluidReagentKey(CarbonDioxideReagent, null), out var amount);
            Assert.That(amount.Solid, Is.Zero, "A dissolved reagent froze at room temperature.");
        });
    }

    /// <summary>
    ///     However a liquid's reagents came to be in their order, each takes its fair part of a flash.
    /// </summary>
    [Test]
    public async Task AFlashDoesNotFavourWhicheverReagentIsFirst()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            GasMixture Flash(KsFluidReagentKey first, KsFluidReagentKey second)
            {
                var mixture = MakePipe(370f);
                mixture.Fluid!.AddLiquid(first, 500f);
                mixture.Fluid.AddLiquid(second, 500f);
                AddHeadspace(mixture);
                mixture.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
                phaseSystem.UpdatePhases(mixture, holder: null);
                return mixture;
            }

            var waterFirst = Flash(WaterKey, EthanolKey);
            var ethanolFirst = Flash(EthanolKey, WaterKey);
            waterFirst.Fluid!.TryGetAmount(EthanolKey, out var ethanolLast);
            ethanolFirst.Fluid!.TryGetAmount(EthanolKey, out var ethanolEarly);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ethanolEarly.Vapor, Is.GreaterThan(0f), "Nothing flashed, so this proved nothing.");
                Assert.That(ethanolLast.Vapor, Is.EqualTo(ethanolEarly.Vapor).Within(ethanolEarly.Vapor * 0.1f),
                    "How much ethanol flashed depends on the order of the mixture's reagents.");
            }
        });
    }

    /// <summary>
    ///     The same pair of reagents at the same fractions boils differently at a wall when they mix non-ideally.
    /// </summary>
    [Test]
    public async Task WallsUseActivitiesToo()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();

        await server.WaitAssertion(() =>
        {
            const float temperature = 350f;
            float Heat(ProtoId<ReagentPrototype> second)
            {
                var packet = MakePipe(temperature);
                // Some vapor already, as that's what sets the plateau the wall works against.
                packet.Fluid!.SetAmount(new KsFluidReagentKey(_wallFirst, null), new KsFluidAmount { Liquid = 50f, Vapor = 1f });
                packet.Fluid.AddLiquid(new KsFluidReagentKey(second, null), 50f);
                AddHeadspace(packet);
                // Little enough that the wall limits what boils, at a plateau set by how readily the mixture does.
                var wall = new KsWallHeatTransfer(100f, 380f, 0f, 0f);
                return phaseSystem.ExchangeAtWall(packet, 0.5f * Atmospherics.OneAtmosphere, wall, 0.5f);
            }

            var ideal = Heat(_wallIdealSecond);
            var nonIdeal = Heat(_wallSecond);
            Assert.That(nonIdeal, Is.Not.EqualTo(ideal).Within(MathF.Abs(ideal) * 0.05f + 0.001f),
                "A non-ideal mixture behaved as an ideal one at a wall.");
        });
    }

    [Test]
    public async Task EthanolWaterAzeotropeCannotBeDistilledPast()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            Assert.That(propertiesSystem.TryGetProperties(EthanolReagent, out var ethanol));

            // Equal moles: well below the azeotrope (about 89 mol% ethanol).
            var dilute = MakePipe(340f);
            dilute.Fluid!.AddLiquid(WaterKey, 100f * 18f / 46f);
            dilute.Fluid.AddLiquid(EthanolKey, 100f);
            AddHeadspace(dilute);

            // 98 mol% ethanol: past it.
            var strong = MakePipe(340f);
            strong.Fluid!.AddLiquid(WaterKey, 2f * 18f / 46f);
            strong.Fluid.AddLiquid(EthanolKey, 98f);
            AddHeadspace(strong);

            Settle(phaseSystem, gameTiming, dilute);
            Settle(phaseSystem, gameTiming, strong);

            var (diluteVapor, diluteLiquid) = EthanolFractions(dilute, water!, ethanol!);
            var (strongVapor, strongLiquid) = EthanolFractions(strong, water!, ethanol!);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(diluteVapor, Is.GreaterThan(diluteLiquid), "Below the azeotrope, the vapor isn't richer in ethanol than the liquid.");
                Assert.That(strongVapor, Is.LessThan(strongLiquid),
                    "Past the azeotrope, the vapor is still richer in ethanol than the liquid, so distillation could pass it.");
            }
        });
    }

    [Test]
    public async Task ImmiscibleLiquidsEachKeepTheirFullVaporPressure()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            Assert.That(propertiesSystem.TryGetProperties(_oilReagent, out var oil));
            Assert.That(propertiesSystem.Immiscible(water!, oil!), "The test oil and water aren't immiscible.");

            var mixture = MakePipe(350f);
            mixture.Fluid!.AddLiquid(WaterKey, 100f);
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(_oilReagent, null), 100f);
            AddHeadspace(mixture);
            Settle(phaseSystem, gameTiming, mixture);

            var temperature = mixture.Temperature;
            var waterPressure = mixture.GetMoles(Gas.WaterVapor) * Atmospherics.R * temperature / mixture.FreeVolume;
            var waterSaturation = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, temperature);

            // Mixed ideally, water would be about 85% of the liquid's moles and hold 85% of its vapor pressure.
            mixture.Fluid.TryGetAmount(WaterKey, out var waterAmount);
            mixture.Fluid.TryGetAmount(new KsFluidReagentKey(_oilReagent, null), out var oilAmount);
            var idealFraction = waterAmount.Liquid * water.MolesPerUnit /
                                (waterAmount.Liquid * water.MolesPerUnit + oilAmount.Liquid * oil!.MolesPerUnit);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(idealFraction, Is.LessThan(0.9f), "The test's oil is too little to tell immiscible from ideal.");
                Assert.That(waterPressure, Is.EqualTo(waterSaturation).Within(0.02f * waterSaturation),
                    "Water under an immiscible oil doesn't hold its full saturation pressure.");
            }
        });
    }

    [Test]
    public void MinimumBoilingMixtureBubblesBelowBothBoilingPoints()
    {
        // Two parts that each boil at 400 K, strongly non-ideal (A = 1.5 there): half and half, its activity coefficients
        //      are e^(A/4) each.
        const float boilingPoint = 400f;
        const float entropy = KsFluidmos.DefaultVaporizationEntropy;
        var gamma = MathF.Exp(1.5f / 4f);
        ReadOnlySpan<float> activities = [0.5f * gamma, 0.5f * gamma];
        ReadOnlySpan<float> entropies = [entropy, entropy];
        ReadOnlySpan<float> boilingPoints = [boilingPoint, boilingPoint];

        var bubble = KsPhaseMath.BubbleTemperature(activities, entropies, boilingPoints, Atmospherics.OneAtmosphere, 420f);
        var bubblePressure = 2f * 0.5f * gamma * KsPhaseMath.SaturationPressure(entropy, boilingPoint, bubble);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bubble, Is.LessThan(boilingPoint - 5f), "A minimum-boiling mixture doesn't boil below its parts.");
            Assert.That(bubblePressure, Is.EqualTo(Atmospherics.OneAtmosphere).Within(0.5f), "The bubble point isn't where the mixture's vapor pressure meets the pressure.");
        }
    }
}
