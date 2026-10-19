using Content.IntegrationTests.Fixtures;
using Content.Shared.Emp;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Emp;

/// <summary>
///     An EMP pulse that isn't predicted, such as an EMP grenade landing, is the server's alone. The client would
///         otherwise run it again on every re-predicted tick, its sound included.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedEmpSystem))]
public sealed class KsEmpPredictionTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestEmpBattery
  components:
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly EntProtoId _batteryPrototype = "KsTestEmpBattery";

    private const float Drain = 100f;

    /// <summary>
    ///     The client leaves an unpredicted pulse alone, and still runs a predicted one: the control, which shows a
    ///         pulse there would reach the battery at all.
    /// </summary>
    [Test]
    public async Task ClientLeavesAnUnpredictedPulseToTheServer()
    {
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        await Pair.RunTicksSync(10);

        var clientGridUid = ToClientUid(testMap.Grid.Owner);

        await client.WaitAssertion(() =>
        {
            var empSystem = client.EntMan.System<SharedEmpSystem>();
            var batterySystem = client.EntMan.System<SharedBatterySystem>();
            var coordinates = new EntityCoordinates(clientGridUid, 0.5f, 0.5f);
            var batteryUid = client.EntMan.SpawnAttachedTo(_batteryPrototype, coordinates);

            empSystem.EmpPulse(coordinates, 1f, Drain, TimeSpan.FromSeconds(1d), predicted: false);
            var chargeAfterUnpredicted = batterySystem.GetCharge(batteryUid);

            empSystem.EmpPulse(coordinates, 1f, Drain, TimeSpan.FromSeconds(1d), predicted: true);
            var chargeAfterPredicted = batterySystem.GetCharge(batteryUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(chargeAfterUnpredicted, Is.EqualTo(1000f).Within(0.01f),
                    "The client ran an unpredicted pulse.");
                Assert.That(chargeAfterPredicted, Is.EqualTo(1000f - Drain).Within(0.01f),
                    "A predicted pulse didn't reach the battery either, so the unpredicted case proves nothing.");
            }

            client.EntMan.DeleteEntity(batteryUid);
        });
    }
}
