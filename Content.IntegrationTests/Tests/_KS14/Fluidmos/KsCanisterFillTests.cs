#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Canisters of a Fluidmos-linked gas are filled at saturation: vapor at its saturation pressure at the fill
///         temperature, the rest as liquid. Otherwise the gas condenses on the first update and its latent heat cooks the
///         canister (2769 mol of water vapor at 293 K self-heats to about 490 K).
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsCanisterFillTests : GameTest
{
    /// <summary>
    ///     Most a canister may drift from its fill temperature while it sits.
    /// </summary>
    private const float Tolerance = 3f;

    private const float SettleSeconds = 30f;

    private static readonly EntProtoId WaterVaporCanister = "WaterVaporCanister";
    private static readonly EntProtoId OxygenCanister = "OxygenCanister";

    /// <summary>
    ///     Every canister prototype holding a linked gas, with its fill.
    /// </summary>
    private List<(EntityPrototype Prototype, GasMixture Air)> LinkedGasCanisters()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var canisters = new List<(EntityPrototype, GasMixture)>();

        foreach (var prototype in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract ||
                !prototype.TryGetComponent<GasCanisterComponent>(out var canister, server.EntMan.ComponentFactory) ||
                canister.Air == null)
                continue;

            // A premix (the max-cap canister) heats by burning, which is not what this is about.
            if (server.System<AtmosphereSystem>().IsMixtureIgnitable(canister.Air))
                continue;

            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            {
                if (propertiesSystem.ByGas[i] == null || canister.Air.GetMoles(i) <= 0f)
                    continue;

                canisters.Add((prototype, canister.Air));
                break;
            }
        }

        return canisters;
    }

    [Test]
    public async Task LinkedGasCanistersHoldTheirFillTemperature()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var spawned = new List<(EntityUid Uid, string Id, float FillTemperature)>();

        await server.WaitPost(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            foreach (var (prototype, air) in LinkedGasCanisters())
            {
                spawned.Add((server.EntMan.SpawnEntity(prototype.ID, testMap.GridCoords), prototype.ID, air.Temperature));
            }
        });

        Assert.That(spawned, Is.Not.Empty, "No canister holds a linked gas, so this test checks nothing.");
        var ticks = (int)(SettleSeconds * server.ResolveDependency<IGameTiming>().TickRate);
        await server.WaitRunTicks(ticks);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (uid, id, fillTemperature) in spawned)
                {
                    if (!server.EntMan.TryGetComponent<GasCanisterComponent>(uid, out var canister))
                    {
                        Assert.Fail($"{id} was destroyed while it sat: it is not filled at saturation.");
                        continue;
                    }

                    var temperature = canister.Air.Temperature;
                    Assert.That(temperature, Is.EqualTo(fillTemperature).Within(Tolerance),
                        $"{id} drifted from its fill temperature: it is not filled at saturation.");
                }
            }
        });
    }

    [Test]
    public async Task SaturatedFillsKeepUpstreamsAmountAndCanister()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.ProtoMan.Index(WaterVaporCanister).TryGetComponent<GasCanisterComponent>(out var water, server.EntMan.ComponentFactory));
            Assert.That(server.ProtoMan.Index(OxygenCanister).TryGetComponent<GasCanisterComponent>(out var oxygen, server.EntMan.ComponentFactory));
            Assert.That(propertiesSystem.TryGetProperties(propertiesSystem.ByGas[(int)Gas.WaterVapor]!.Id, out var waterProperties));

            var air = water!.Air;
            var vapor = air.GetMoles(Gas.WaterVapor);
            var liquidMoles = (air.Fluid?.LiquidUnits ?? 0f) * waterProperties!.MolesPerUnit;
            var saturated = KsPhaseMath.SaturationPressure(waterProperties.VaporizationEntropy, waterProperties.BoilingPoint, air.Temperature)
                            * air.FreeVolume / (Atmospherics.R * air.Temperature);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vapor, Is.EqualTo(saturated).Within(saturated * 0.01f), "The water vapor canister's vapor isn't at saturation.");
                Assert.That(liquidMoles, Is.GreaterThan(0f), "The water vapor canister holds no liquid.");
                // Upstream fills every canister with the same amount; the partial only splits it between phases.
                Assert.That(vapor + liquidMoles, Is.EqualTo(oxygen!.Air.TotalMoles).Within(oxygen.Air.TotalMoles * 0.001f),
                    "The saturated fill changed the canister's amount.");
                Assert.That(air.Volume, Is.EqualTo(oxygen.Air.Volume), "The partial lost upstream's volume.");
                Assert.That(air.Temperature, Is.EqualTo(oxygen.Air.Temperature), "The partial lost upstream's fill temperature.");
            }
        });
    }
}
