using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.SurveillanceCamera;
using Content.Shared.SurveillanceCamera;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.SurveillanceCamera;

/// <summary>
///     A camera monitor learns its subnets from the routers' replies to its ping.
/// </summary>
[TestFixture]
[TestOf(typeof(SurveillanceCameraRouterSystem))]
[TestOf(typeof(SurveillanceCameraMonitorSystem))]
public sealed class KsSurveillanceCameraSubnetTest : GameTest
{
    private readonly EntProtoId _routerPrototype = "KsTestSurveillanceCameraRouter";
    private readonly EntProtoId _monitorPrototype = "KsTestSurveillanceCameraMonitor";

    // Unpowered on purpose: power is not what is under test, and DeviceNetworkRequiresPower would need a grid of cables.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestSurveillanceCameraRouter
  components:
  - type: DeviceNetwork
    deviceNetId: Wired
    receiveFrequencyId: SurveillanceCamera
    transmitFrequencyId: SurveillanceCamera
  - type: WiredNetworkConnection
  - type: SurveillanceCameraRouter
    subnetFrequency: SurveillanceCameraEngineering

- type: entity
  id: KsTestSurveillanceCameraMonitor
  components:
  - type: DeviceNetwork
    deviceNetId: Wired
    receiveFrequencyId: SurveillanceCamera
    transmitFrequencyId: SurveillanceCamera
  - type: WiredNetworkConnection
  - type: SurveillanceCameraMonitor
";

    [Test]
    public async Task RefreshingSubnetsFindsTheRoutersSubnet()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        var routerUid = EntityUid.Invalid;
        var monitorUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            routerUid = server.EntMan.SpawnEntity(_routerPrototype, testMap.GridCoords);
            monitorUid = server.EntMan.SpawnEntity(_monitorPrototype, testMap.GridCoords);
        });
        await server.WaitRunTicks(2);

        // What the refresh button in the monitor's window sends.
        await server.WaitPost(() =>
        {
            var message = new SurveillanceCameraRefreshSubnetsMessage { UiKey = SurveillanceCameraMonitorUiKey.Key };
            server.EntMan.EventBus.RaiseLocalEvent(monitorUid, message);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var monitorComponent = server.EntMan.GetComponent<SurveillanceCameraMonitorComponent>(monitorUid);
            var routerComponent = server.EntMan.GetComponent<SurveillanceCameraRouterComponent>(routerUid);

            Assert.That(routerComponent.SubnetFrequencyId, Is.Not.Null);
            Assert.That(monitorComponent.KnownSubnets, Does.ContainKey(routerComponent.SubnetFrequencyId.Value.Id),
                "The monitor never learned the router's subnet from its ping reply.");
        });
    }
}
