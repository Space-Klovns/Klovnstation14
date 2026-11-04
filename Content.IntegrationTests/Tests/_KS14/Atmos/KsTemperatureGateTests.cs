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

    [Test]
    public async Task PassesOnlyBelowItsTarget()
    {
        var gateUid = await SpawnGate(enabled: true);
        var (coldMoved, coldFlowing) = await Run(gateUid, Target - 10f);
        var (hotMoved, hotFlowing) = await Run(gateUid, Target + 10f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coldMoved, Is.GreaterThan(0f), "A gate below its target stayed shut.");
            Assert.That(coldFlowing, "The gate let gas through without saying so.");
            Assert.That(hotMoved, Is.Zero, "A gate above its target let gas through.");
            Assert.That(hotFlowing, Is.False);
        }
    }

    [Test]
    public async Task InvertedPassesOnlyAboveItsTarget()
    {
        var gateUid = await SpawnGate(enabled: true, inverted: true);
        var (coldMoved, _) = await Run(gateUid, Target - 10f);
        var (hotMoved, _) = await Run(gateUid, Target + 10f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hotMoved, Is.GreaterThan(0f));
            Assert.That(coldMoved, Is.Zero);
        }
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task StaysShutUnlessOnAndPowered(bool enabled, bool powered)
    {
        var gateUid = await SpawnGate(enabled, powered: powered);
        var (moved, _) = await Run(gateUid, Target - 10f);
        Assert.That(moved, Is.Zero);
    }

    [Test]
    public async Task OnlyFlowsOneWay()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: true);

        await server.WaitAssertion(() =>
        {
            var (inlet, outlet) = Air(gateUid, Target - 10f);
            inlet.Clear();
            inlet.Temperature = Target - 10f;
            outlet.AdjustMoles(Gas.Nitrogen, 50f);

            Update(server.EntMan, gateUid);

            Assert.That(inlet.TotalMoles, Is.Zero, "Gas came back through the gate.");
        });
    }

    [Test]
    public async Task LetsLiquidThroughAsAPassiveGateDoes()
    {
        var server = Pair.Server;
        var gateUid = await SpawnGate(enabled: true);

        await server.WaitAssertion(() =>
        {
            var (inlet, outlet) = Air(gateUid, Target - 10f);
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
