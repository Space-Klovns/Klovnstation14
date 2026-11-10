#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The linear operations on fluid inside a <see cref="GasMixture"/>: splitting, merging, dividing and cloning conserve
///         every phase, reagent data survives the Solution boundary, and the cached totals and map serializer agree with
///         the entries.
/// </summary>
[TestOf(typeof(KsFluidMixture))]
public sealed class KsFluidMixtureTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";
    private static readonly ProtoId<GasPrototype> WaterVaporGas = "WaterVapor";
    private static readonly ProtoId<GasPrototype> AmmoniaGas = "Ammonia";
    private static readonly ProtoId<ReagentPrototype> AmmoniaReagent = "Ammonia";

    private const float Tolerance = 0.01f;

    private static GasMixture MakePipeMixture(float waterUnits, float ethanolUnits, float ethanolVaporMoles, float iceUnits)
    {
        var mixture = new GasMixture(200f) { Temperature = Atmospherics.T20C, FluidCapable = true };
        mixture.AdjustMoles(Gas.Nitrogen, 5f);
        mixture.Fluid = new KsFluidMixture();
        mixture.Fluid.SetAmount(new KsFluidReagentKey(WaterReagent, null), new KsFluidAmount(iceUnits, waterUnits, 0f));
        mixture.Fluid.SetAmount(new KsFluidReagentKey(EthanolReagent, null), new KsFluidAmount(0f, ethanolUnits, ethanolVaporMoles));
        return mixture;
    }

    [Test]
    public async Task SplitMergeAndCloneConserveEveryPhase()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            var source = MakePipeMixture(waterUnits: 40f, ethanolUnits: 10f, ethanolVaporMoles: 0.2f, iceUnits: 8f);
            var liquidBefore = source.Fluid!.LiquidUnits;
            var vaporBefore = source.Fluid.VaporMoles;
            var solidBefore = source.Fluid.SolidUnits;

            var removed = source.RemoveRatio(0.25f);
            var gasOnly = source.RemoveRatio(0.5f, includeLiquid: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(removed.FluidCapable, "A packet split off a fluid-capable mixture can't hold fluid.");
                Assert.That(removed.Fluid!.SolidUnits, Is.Zero, "Solids moved with a transfer.");
                Assert.That(removed.Fluid.LiquidUnits, Is.EqualTo(liquidBefore * 0.25f).Within(Tolerance));
                Assert.That(gasOnly.Fluid!.LiquidUnits, Is.Zero, "A gas-only draw took liquid.");
                Assert.That(source.Fluid!.SolidUnits, Is.EqualTo(solidBefore).Within(Tolerance), "Solids left the source.");
            }

            // Everything goes back.
            atmosphereSystem.Merge(source, removed);
            atmosphereSystem.Merge(source, gasOnly);

            var clone = source.Clone();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(source.Fluid!.LiquidUnits, Is.EqualTo(liquidBefore).Within(Tolerance));
                Assert.That(source.Fluid.VaporMoles, Is.EqualTo(vaporBefore).Within(Tolerance));
                Assert.That(source.Fluid.SolidUnits, Is.EqualTo(solidBefore).Within(Tolerance));
                Assert.That(clone, Is.EqualTo(source), "A clone differs from its source.");
                Assert.That(clone.Fluid, Is.Not.SameAs(source.Fluid), "A clone shares its source's fluid.");
            }
        });
    }

    /// <summary>
    ///     A copy made with the copy constructor, as unanchoring a broken pipe leaks one, can hold the fluid it copied: a
    ///         merge into it keeps that fluid rather than dropping it.
    /// </summary>
    [Test]
    public async Task ACopyHoldsTheFluidItCopied()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            var source = MakePipeMixture(waterUnits: 40f, ethanolUnits: 0f, ethanolVaporMoles: 0f, iceUnits: 0f);
            var copy = new GasMixture(source);
            var more = MakePipeMixture(waterUnits: 10f, ethanolUnits: 0f, ethanolVaporMoles: 0f, iceUnits: 0f);
            atmosphereSystem.Merge(copy, more);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(copy.FluidCapable, "A copy of a fluid-capable mixture can't hold fluid.");
                Assert.That(copy.Fluid!.LiquidUnits, Is.EqualTo(50f).Within(Tolerance), "Liquid merged into a copy was dropped.");
            }
        });
    }

    [Test]
    public async Task DividingAPipeNetConservesFluidAndSplitsSolidsByVolume()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            var source = MakePipeMixture(waterUnits: 30f, ethanolUnits: 6f, ethanolVaporMoles: 0.1f, iceUnits: 12f);
            var receivers = new List<GasMixture>
            {
                new(150f) { Temperature = Atmospherics.T20C, FluidCapable = true },
                new(50f) { Temperature = Atmospherics.T20C, FluidCapable = true },
            };

            atmosphereSystem.DivideInto(source, receivers);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(receivers[0].Fluid!.LiquidUnits + receivers[1].Fluid!.LiquidUnits,
                    Is.EqualTo(source.Fluid!.LiquidUnits).Within(Tolerance));
                Assert.That(receivers[0].Fluid!.VaporMoles + receivers[1].Fluid!.VaporMoles,
                    Is.EqualTo(source.Fluid.VaporMoles).Within(Tolerance));
                Assert.That(receivers[0].Fluid!.SolidUnits, Is.EqualTo(source.Fluid.SolidUnits * 0.75f).Within(Tolerance),
                    "Solids did not split by volume.");
            }
        });
    }

    [Test]
    public async Task ReagentDataSurvivesTheSolutionBoundary()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var solution = new Solution();
            solution.AddReagent(new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = "AAAA" } }), FixedPoint2.New(10));
            solution.AddReagent(new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = "CCCC" } }), FixedPoint2.New(5));

            var fluid = new KsFluidMixture();
            fluid.AddSolution(solution);

            Assert.That(fluid.Count, Is.EqualTo(2), "Blood with different DNA merged into one key.");

            var packet = fluid.RemoveRatio(0.5f);
            fluid.Add(packet);
            var result = fluid.TakeLiquidAsSolution(fluid.LiquidUnits);

            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)result.Volume, Is.EqualTo((float)solution.Volume).Within(0.02f));
                Assert.That((float)result.GetReagentQuantity(new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = "AAAA" } })),
                    Is.EqualTo(10f).Within(0.02f), "The first DNA's blood was lost.");
                Assert.That((float)result.GetReagentQuantity(new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = "CCCC" } })),
                    Is.EqualTo(5f).Within(0.02f), "The second DNA's blood was lost.");
            }
        });
    }

    [Test]
    public async Task CachedTotalsMatchARecount()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            var fluid = MakePipeMixture(waterUnits: 17f, ethanolUnits: 3f, ethanolVaporMoles: 0.05f, iceUnits: 2f).Fluid!;
            var other = MakePipeMixture(waterUnits: 5f, ethanolUnits: 9f, ethanolVaporMoles: 0.3f, iceUnits: 1f).Fluid!;

            // Merges and splits, with the heat capacity cached throughout so the merges carry it across.
            fluid.GetHeatCapacity(propertiesSystem);
            other.GetHeatCapacity(propertiesSystem);
            for (var i = 0; i < 20; i++)
            {
                fluid.Add(other, 0.1f);
                fluid.Add(fluid.RemoveRatio(0.3f));
                fluid.Multiply(0.99f);
            }

            var cachedHeatCapacity = fluid.GetHeatCapacity(propertiesSystem);

            var liquid = 0f;
            var vapor = 0f;
            var solid = 0f;
            foreach (var (_, amount) in fluid.Reagents)
            {
                liquid += amount.Liquid;
                vapor += amount.Vapor;
                solid += amount.Solid;
            }

            fluid.InvalidateHeatCapacity();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fluid.LiquidUnits, Is.EqualTo(liquid).Within(Tolerance));
                Assert.That(fluid.VaporMoles, Is.EqualTo(vapor).Within(Tolerance));
                Assert.That(fluid.SolidUnits, Is.EqualTo(solid).Within(Tolerance));
                Assert.That(cachedHeatCapacity, Is.EqualTo(fluid.GetHeatCapacity(propertiesSystem)).Within(Tolerance));
            }
        });
    }

    [Test]
    public async Task MapSerializerRoundTrips()
    {
        var server = Pair.Server;
        var serializationManager = server.ResolveDependency<ISerializationManager>();

        await server.WaitAssertion(() =>
        {
            var mixture = MakePipeMixture(waterUnits: 12f, ethanolUnits: 4f, ethanolVaporMoles: 0.07f, iceUnits: 3f);
            var blood = new Solution();
            blood.AddReagent(new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = "GGGG" } }), FixedPoint2.New(2));
            mixture.Fluid!.AddSolution(blood);

            var node = serializationManager.WriteValue(mixture, notNullableOverride: true);
            var read = serializationManager.Read<GasMixture>(node, notNullableOverride: true);
            var copy = serializationManager.CreateCopy(mixture, notNullableOverride: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(read.Fluid, Is.EqualTo(mixture.Fluid), "The fluid did not survive a map save.");
                Assert.That(copy.Fluid, Is.EqualTo(mixture.Fluid), "The fluid did not survive a serialization copy.");
            }
        });
    }

    [Test]
    public async Task HeatCapacityAndPressureIncludeTheFluid()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            var mixture = MakePipeMixture(waterUnits: 100f, ethanolUnits: 0f, ethanolVaporMoles: 0f, iceUnits: 0f);
            var dry = new GasMixture(200f) { Temperature = Atmospherics.T20C };
            dry.AdjustMoles(Gas.Nitrogen, 5f);

            var fluidHeatCapacity = mixture.Fluid!.GetHeatCapacity(propertiesSystem);
            var freeVolume = mixture.Volume - mixture.Fluid.CondensedUnits * KsFluidmos.LitersPerUnit;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false),
                    Is.EqualTo(atmosphereSystem.GetHeatCapacity(dry, applyScaling: false) + fluidHeatCapacity).Within(Tolerance));
                Assert.That(mixture.Pressure, Is.EqualTo(dry.Pressure * dry.Volume / freeVolume).Within(Tolerance),
                    "Liquid did not displace the gas's volume.");
                Assert.That(mixture.TotalMoles, Is.EqualTo(dry.TotalMoles), "TotalMoles counted fluid.");
            }

            // Overfill past the free-volume floor: the hydraulic term takes over.
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(WaterReagent, null), mixture.Volume / KsFluidmos.LitersPerUnit);
            Assert.That(mixture.Pressure, Is.GreaterThan(KsFluidmos.HydraulicStiffness), "An overfilled mixture has no hydraulic pressure.");
        });
    }

    [Test]
    public async Task HydraulicPressureIsCappedHoweverOverfull()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var mixture = MakePipeMixture(waterUnits: 0f, ethanolUnits: 0f, ethanolVaporMoles: 0f, iceUnits: 0f);
            var waterKey = new KsFluidReagentKey(WaterReagent, null);
            // Liquid filling the whole volume, and then as much again: 100% overfull.
            mixture.Fluid!.AddLiquid(waterKey, 2f * mixture.Volume / KsFluidmos.LitersPerUnit);

            // Past the floor, the gas sits in the floor's volume whatever the overfill.
            var gasPressure = mixture.TotalMoles * Atmospherics.R * mixture.Temperature
                              / (mixture.Volume * KsFluidmos.MinFreeVolumeFraction);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(float.IsFinite(mixture.Pressure), Is.True, "A 100% overfull mixture's pressure is not finite.");
                Assert.That(mixture.Pressure - gasPressure,
                    Is.LessThanOrEqualTo(KsFluidmos.MaxHydraulicPressure * (1f + Tolerance)),
                    "The hydraulic term was not capped.");
            }

            // An overfill whose uncapped term overflows a float still gives a finite pressure.
            mixture.Fluid.AddLiquid(waterKey, 3e38f);
            Assert.That(float.IsFinite(mixture.Pressure), Is.True, "An absurdly overfull mixture's pressure is not finite.");
        });
    }

    [Test]
    public async Task PropertyTableHasTheSeedLinksAndValidGasConstants()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(propertiesSystem.ByGas[(int)Gas.WaterVapor]?.Id, Is.EqualTo(WaterReagent));
                Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
                Assert.That(water!.MolesPerUnit, Is.EqualTo(KsPhaseMath.MolesPerUnit(server.ProtoMan.Index(WaterVaporGas).MolarMass)));
                Assert.That(water.LatentHeat, Is.EqualTo(KsPhaseMath.DefaultLatentHeat(water.BoilingPoint, water.SpecificHeat)).Within(Tolerance));

                // Ammonia is linked, and its gas's molar mass is corrected by a partial that keeps the rest of upstream's.
                var ammoniaGas = server.ProtoMan.Index(AmmoniaGas);
                Assert.That(propertiesSystem.ByGas[(int)Gas.Ammonia]?.Id, Is.EqualTo(AmmoniaReagent), "Ammonia isn't linked to its gas.");
                Assert.That(ammoniaGas.MolarMass, Is.EqualTo(17f), "The ammonia gas partial no longer sets its real molar mass.");
                Assert.That(ammoniaGas.Reagent, Is.EqualTo(AmmoniaReagent.Id), "The ammonia gas partial lost upstream's reagent.");
                Assert.That(ammoniaGas.GasOverlaySprite, Is.Not.Null, "The ammonia gas partial lost upstream's overlay.");
                Assert.That(propertiesSystem.TryGetProperties(AmmoniaReagent, out var ammonia));
                Assert.That(ammonia!.MolesPerUnit, Is.EqualTo(KsPhaseMath.MolesPerUnit(ammoniaGas.MolarMass)), "Linked ammonia doesn't take its gas's molar mass.");
                // Why it is linked: a pressure pump can compress it to condense at room temperature.
                Assert.That(KsPhaseMath.SaturationPressure(ammonia.VaporizationEntropy, ammonia.BoilingPoint, Atmospherics.T20C),
                    Is.LessThan(Atmospherics.MaxOutputPressure), "Ammonia no longer condenses within a pressure pump's reach at room temperature.");

                foreach (var gasPrototype in server.ProtoMan.EnumeratePrototypes<GasPrototype>())
                {
                    if (gasPrototype.KsVanDerWaalsA is not { } a || gasPrototype.KsVanDerWaalsB is not { } b)
                        continue;

                    Assert.That(a, Is.GreaterThanOrEqualTo(0f), $"{gasPrototype.ID} has a negative van der Waals a.");
                    Assert.That(b, Is.GreaterThan(0f), $"{gasPrototype.ID} has a non-positive van der Waals b.");
                    Assert.That(gasPrototype.HeatCapacityRatio, Is.GreaterThan(1f), $"{gasPrototype.ID} has γ <= 1.");
                    Assert.That(KsThrottling.InversionTemperature(a, b), Is.GreaterThan(KsThrottling.CriticalTemperature(a, b)),
                        $"{gasPrototype.ID} inverts below its critical temperature.");
                }
            }
        });
    }
}
