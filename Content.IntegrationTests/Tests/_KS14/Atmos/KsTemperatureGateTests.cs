#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._KS14.Atmos.TemperatureGate;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.Atmos.TemperatureGate;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     What the gate lets through. <see cref="KsTemperatureGatePredictionTests"/> covers its controls.
/// </summary>
[TestOf(typeof(KsTemperatureGateSystem))]
public sealed class KsTemperatureGateTests : GameTest
{
    private static readonly EntProtoId GatePrototype = "KsTemperatureGate";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    private const float Target = 300f;

    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";

    private async Task<EntityUid> SpawnGate(bool enabled, bool inverted = false, bool powered = true)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid gateUid = default;
        await server.WaitPost(() =>
        {
            gateUid = server.EntMan.SpawnEntity(GatePrototype, testMap.GridCoords);
            server.System<PowerReceiverSystem>().SetNeedsPower(gateUid, !powered);
            var gate = server.EntMan.GetComponent<KsTemperatureGateComponent>(gateUid);
            var gateSystem = server.System<KsTemperatureGateSystem>();
            gateSystem.SetTarget((gateUid, gate), Target);
            gateSystem.SetEnabled((gateUid, gate), enabled);
            if (inverted)
                typeof(KsTemperatureGateComponent).GetField(nameof(KsTemperatureGateComponent.Inverted))!.SetValue(gate, true);
        });
        await server.WaitRunTicks(5);
        return gateUid;
    }

    private (GasMixture Inlet, GasMixture Outlet) Air(EntityUid gateUid, float inletTemperature)
    {
        var nodeSystem = Pair.Server.System<NodeContainerSystem>();
        Assert.That(nodeSystem.TryGetNodes(gateUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
        inlet!.Air.Clear();
        outlet!.Air.Clear();
        inlet.Air.Temperature = inletTemperature;
        outlet.Air.Temperature = Atmospherics.T20C;
        inlet.Air.AdjustMoles(Gas.Nitrogen, 50f);
        return (inlet.Air, outlet.Air);
    }

    private static void Update(IEntityManager entityManager, EntityUid gateUid)
    {
        var ev = new AtmosDeviceUpdateEvent(0.5f, null, null);
        entityManager.EventBus.RaiseLocalEvent(gateUid, ref ev);
    }

    private async Task<(float Moved, bool Flowing)> Run(EntityUid gateUid, float inletTemperature)
    {
        var server = Pair.Server;
        var moved = 0f;
        var flowing = false;
        await server.WaitAssertion(() =>
        {
            var (_, outlet) = Air(gateUid, inletTemperature);
            Update(server.EntMan, gateUid);
            moved = outlet.TotalMoles;
            flowing = server.EntMan.GetComponent<KsTemperatureGateComponent>(gateUid).Flowing;
        });
        return (moved, flowing);
    }

    /// <summary>
    ///     Steps the inlet through temperatures on one gate.
    /// </summary>
    private async Task AssertPasses(EntityUid gateUid, params (float Temperature, bool Passes)[] steps)
    {
        foreach (var (temperature, passes) in steps)
        {
            var (moved, flowing) = await Run(gateUid, temperature);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(moved > 0f, Is.EqualTo(passes), $"At {temperature} K.");
                Assert.That(flowing, Is.EqualTo(passes), $"Its flow isn't what it shows at {temperature} K.");
            }
        }
    }

    private async Task Configure(EntityUid gateUid, Action<KsTemperatureGateSystem, Entity<KsTemperatureGateComponent>> configure)
    {
        var server = Pair.Server;
        await server.WaitPost(() => configure(server.System<KsTemperatureGateSystem>(), (gateUid, server.EntMan.GetComponent<KsTemperatureGateComponent>(gateUid))));
    }

    /// <summary>
    ///     Both modes set the same band, around <see cref="Target"/>, so they have to agree.
    /// </summary>
    [TestCase(KsTemperatureGateMode.Tolerance, false)]
    [TestCase(KsTemperatureGateMode.Tolerance, true)]
    [TestCase(KsTemperatureGateMode.Range, false)]
    [TestCase(KsTemperatureGateMode.Range, true)]
    public async Task OpenInsideItsBandOrOutsideWhenInverted(KsTemperatureGateMode mode, bool inverted)
    {
        const float halfWidth = 10f;
        var gateUid = await SpawnGate(enabled: true, inverted: inverted);
        await Configure(gateUid, (gateSystem, gate) =>
        {
            gateSystem.SetMode(gate, mode);
            gateSystem.SetTolerance(gate, halfWidth);
            gateSystem.SetRange(gate, Target - halfWidth, Target + halfWidth);
        });

        await AssertPasses(gateUid,
            (Target - halfWidth - 1f, inverted),
            (Target - halfWidth + 1f, !inverted),
            (Target + halfWidth - 1f, !inverted),
            (Target + halfWidth + 1f, inverted));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task StaysShutUnlessOnAndPowered(bool enabled, bool powered)
    {
        var gateUid = await SpawnGate(enabled, powered: powered);
        var (moved, _) = await Run(gateUid, Target);
        Assert.That(moved, Is.Zero);
    }

    [Test]
    public async Task OnlyFlowsOneWay()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: true);

        await server.WaitAssertion(() =>
        {
            const float inletMoles = 1f;
            var (inlet, outlet) = Air(gateUid, Target);
            inlet.Clear();
            inlet.Temperature = Target;
            inlet.SetMoles(Gas.Nitrogen, inletMoles);
            outlet.Clear();
            outlet.Temperature = Target;
            outlet.SetMoles(Gas.Nitrogen, 50f);

            Update(server.EntMan, gateUid);

            Assert.That(inlet.TotalMoles, Is.EqualTo(inletMoles), "Gas crossed the gate uphill.");
        });
    }

    /// <summary>
    ///     Vapor counts towards pressure but not towards gas moles, so an inlet holding only that used to give no valid
    ///         fraction, and lost it all.
    /// </summary>
    [Test]
    public async Task CarriesVaporWithNoGasAndLosesNone()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: true);

        await server.WaitAssertion(() =>
        {
            const float vapor = 5f;
            var (inlet, outlet) = Air(gateUid, Target);
            inlet.Clear();
            outlet.Clear();
            inlet.Temperature = Target;
            outlet.Temperature = Target;
            inlet.FluidCapable = true;
            outlet.FluidCapable = true;
            var key = new KsFluidReagentKey(EthanolReagent, null);
            inlet.Fluid = new KsFluidMixture();
            inlet.Fluid.SetAmount(key, new KsFluidAmount { Vapor = vapor });

            Update(server.EntMan, gateUid);

            float Vapor(GasMixture air) => air.Fluid != null && air.Fluid.TryGetAmount(key, out var amount) ? amount.Vapor : 0f;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Vapor(outlet), Is.GreaterThan(0f), "Vapor didn't cross the gate.");
                Assert.That(Vapor(inlet) + Vapor(outlet), Is.EqualTo(vapor).Within(0.001f), "Vapor was lost crossing the gate.");
            }
        });
    }

    [Test]
    public async Task IgnoresAModeThatDoesNotExist()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: true);
        await Configure(gateUid, (gateSystem, gate) => gateSystem.SetMode(gate, (KsTemperatureGateMode)200));

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<KsTemperatureGateComponent>(gateUid).Mode, Is.EqualTo(KsTemperatureGateMode.Tolerance));
        });
    }

    [Test]
    public async Task CannotBeTurnedOnOffItsPipes()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: false);
        await server.WaitPost(() => server.System<SharedTransformSystem>().Unanchor(gateUid));
        await Configure(gateUid, (gateSystem, gate) => gateSystem.SetEnabled(gate, true));

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<KsTemperatureGateComponent>(gateUid).Enabled, Is.False);
        });
    }

    /// <summary>
    ///     These are read-only to VV, so <c>vvwrite</c> only lands through a registered path, which goes through the setter
    ///         and so gets clamped.
    /// </summary>
    [Test]
    public async Task VvWritesGoThroughTheSetters()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: true);
        var consoleHost = server.ResolveDependency<Robust.Shared.Console.IConsoleHost>();
        var name = nameof(KsTemperatureGateComponent).Replace("Component", string.Empty);

        await server.WaitPost(() =>
        {
            consoleHost.ExecuteCommand($"vvwrite /entity/{gateUid.Id}/{name}/{nameof(KsTemperatureGateComponent.Tolerance)} -5");
            consoleHost.ExecuteCommand($"vvwrite /entity/{gateUid.Id}/{name}/{nameof(KsTemperatureGateComponent.RangeMinimum)} 900");
        });

        await server.WaitAssertion(() =>
        {
            var gate = server.EntMan.GetComponent<KsTemperatureGateComponent>(gateUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(gate.Tolerance, Is.Zero, "vvwrite went past the setter's clamp.");
                Assert.That(gate.RangeMinimum, Is.LessThanOrEqualTo(gate.RangeMaximum), "vvwrite left the range upside down.");
                Assert.That(gate.RangeMaximum, Is.EqualTo(900f), "vvwrite didn't reach the range.");
            }
        });
    }

    [Test]
    public async Task LetsLiquidThroughAsAPassiveGateDoes()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: true);

        await server.WaitAssertion(() =>
        {
            var (inlet, outlet) = Air(gateUid, Target);
            inlet.FluidCapable = true;
            outlet.FluidCapable = true;
            inlet.Fluid = new KsFluidMixture();
            var key = new KsFluidReagentKey(WaterReagent, null);
            inlet.Fluid.AddLiquid(key, 500f);

            Update(server.EntMan, gateUid);

            Assert.That(outlet.Fluid, Is.Not.Null, "No liquid crossed the gate.");
            Assert.That(outlet.Fluid!.LiquidUnits, Is.GreaterThan(0f), "No liquid crossed the gate.");
        });
    }
}

/// <summary>
///     The gate's controls, from a client. Each is checked before the server has seen it, or after, as stated.
/// </summary>
[TestOf(typeof(KsSharedTemperatureGateSystem))]
public sealed class KsTemperatureGatePredictionTests : InteractionTest
{
    private const string GatePrototype = "KsTemperatureGate";

    private async Task OpenGate()
    {
        await SpawnTarget(GatePrototype);
        await Server.WaitPost(() => SEntMan.System<PowerReceiverSystem>().SetNeedsPower(STarget!.Value, false));
        await RunTicks(5);
        await Activate();
        await RunTicks(5);
    }

    [Test]
    public async Task TogglingAndSettingThroughTheWindowArePredicted()
    {
        await OpenGate();
        var clientGateUid = CTarget!.Value;
        var clientGate = CEntMan.GetComponent<KsTemperatureGateComponent>(clientGateUid);

        Assert.That(clientGate.Enabled, Is.False);
        await Client.WaitPost(() =>
        {
            Assert.That(CEntMan.System<SharedUserInterfaceSystem>().TryGetOpenUi(clientGateUid, KsTemperatureGateUiKey.Key, out var boundUserInterface),
                "The gate's window didn't open.");
            boundUserInterface!.SendPredictedMessage(new KsTemperatureGateToggleStatusMessage(true));
            boundUserInterface.SendPredictedMessage(new KsTemperatureGateChangeTargetMessage(1000f));
        });

        // Only the client has run, so this can't be the server's state.
        await Client.WaitRunTicks(2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientGate.Enabled, "Turning the gate on wasn't predicted.");
            Assert.That(clientGate.TargetTemperature, Is.EqualTo(1000f), "Setting the target wasn't predicted.");
        }

        await RunTicks(15);
        var serverGate = SEntMan.GetComponent<KsTemperatureGateComponent>(STarget!.Value);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverGate.Enabled, "The server never got the gate turned on.");
            Assert.That(serverGate.TargetTemperature, Is.EqualTo(1000f));
            Assert.That(clientGate.Enabled, "The server's state took the prediction back.");
        }
    }

    [Test]
    public async Task TargetIsClampedAtBothEnds()
    {
        await OpenGate();
        var serverGate = SEntMan.GetComponent<KsTemperatureGateComponent>(STarget!.Value);

        await SendBui(KsTemperatureGateUiKey.Key, new KsTemperatureGateChangeTargetMessage(float.PositiveInfinity));
        Assert.That(serverGate.TargetTemperature, Is.EqualTo(serverGate.MaxTemperature));

        await SendBui(KsTemperatureGateUiKey.Key, new KsTemperatureGateChangeTargetMessage(float.NegativeInfinity));
        Assert.That(serverGate.TargetTemperature, Is.EqualTo(serverGate.MinTemperature));
    }

    [Test]
    public async Task ModeAndRangeChangesArePredictedAndRangeCannotInvert()
    {
        await OpenGate();
        var clientGate = CEntMan.GetComponent<KsTemperatureGateComponent>(CTarget!.Value);

        await Client.WaitPost(() =>
        {
            Assert.That(CEntMan.System<SharedUserInterfaceSystem>().TryGetOpenUi(CTarget!.Value, KsTemperatureGateUiKey.Key, out var boundUserInterface));
            boundUserInterface!.SendPredictedMessage(new KsTemperatureGateChangeModeMessage(KsTemperatureGateMode.Range));
            boundUserInterface.SendPredictedMessage(new KsTemperatureGateChangeRangeMessage(500f, 200f));
        });

        await Client.WaitRunTicks(2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientGate.Mode, Is.EqualTo(KsTemperatureGateMode.Range), "Changing mode wasn't predicted.");
            Assert.That(clientGate.RangeMinimum, Is.EqualTo(200f), "Setting the range wasn't predicted, or its ends stayed swapped.");
            Assert.That(clientGate.RangeMaximum, Is.EqualTo(500f));
        }

        await RunTicks(15);
        Assert.That(SEntMan.GetComponent<KsTemperatureGateComponent>(STarget!.Value).Mode, Is.EqualTo(KsTemperatureGateMode.Range));
    }

    [Test]
    public async Task MultitoolInverts()
    {
        await SpawnTarget(GatePrototype);
        var clientGate = CEntMan.GetComponent<KsTemperatureGateComponent>(CTarget!.Value);
        Assert.That(clientGate.Inverted, Is.False);

        await InteractUsing("Multitool");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SEntMan.GetComponent<KsTemperatureGateComponent>(STarget!.Value).Inverted, "A multitool didn't invert the gate.");
            Assert.That(clientGate.Inverted, "The client doesn't know the gate is inverted.");
        }
    }

    [Test]
    public async Task CannotBeUnanchoredWhileRunning()
    {
        await OpenGate();
        await SendBui(KsTemperatureGateUiKey.Key, new KsTemperatureGateToggleStatusMessage(true));

        await Server.WaitAssertion(() =>
        {
            var attempt = new UnanchorAttemptEvent(SPlayer, STarget!.Value);
            SEntMan.EventBus.RaiseLocalEvent(STarget!.Value, attempt);
            Assert.That(attempt.Cancelled, "A running gate could be unwrenched.");
        });

        await SendBui(KsTemperatureGateUiKey.Key, new KsTemperatureGateToggleStatusMessage(false));

        await Server.WaitAssertion(() =>
        {
            var attempt = new UnanchorAttemptEvent(SPlayer, STarget!.Value);
            SEntMan.EventBus.RaiseLocalEvent(STarget!.Value, attempt);
            Assert.That(attempt.Cancelled, Is.False, "A gate that is off couldn't be unwrenched.");
        });
    }
}
