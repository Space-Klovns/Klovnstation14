using System.Threading.Tasks;
using Content.Client._KS14.Atmos.TemperatureGate;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Atmos.TemperatureGate;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     What the gate's window does with the state it's given.
/// </summary>
[TestOf(typeof(KsTemperatureGateWindow))]
public sealed class KsTemperatureGateWindowTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task AChangeToOneFieldLeavesWhatIsBeingTypedInTheOther()
    {
        var client = Pair.Client;
        await client.WaitAssertion(() =>
        {
            var gateUid = client.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var gate = client.EntMan.AddComponent<KsTemperatureGateComponent>(gateUid);
            var gateSystem = client.System<KsTemperatureGateSystem>();

            var window = new KsTemperatureGateWindow();
            window.UpdateState(gate);

            const float typed = 123f;
            window.SecondInput.Value = typed;

            gateSystem.SetTarget((gateUid, gate), 500f);
            window.UpdateState(gate);

            Assert.Multiple(() =>
            {
                Assert.That(window.FirstInput.Value, Is.EqualTo(500f), "The field that changed wasn't updated.");
                Assert.That(window.SecondInput.Value, Is.EqualTo(typed), "A change to the other field threw away what was typed.");
            });

            window.Dispose();
            client.EntMan.DeleteEntity(gateUid);
        });
    }

    /// <summary>
    ///     The way to swap the gate isn't in the window's controls, so the window has to say it.
    /// </summary>
    [Test]
    public async Task TheWindowSaysAMultitoolSwapsIt()
    {
        var client = Pair.Client;
        await client.WaitAssertion(() =>
        {
            var gateUid = client.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var gate = client.EntMan.AddComponent<KsTemperatureGateComponent>(gateUid);

            var window = new KsTemperatureGateWindow();
            window.UpdateState(gate);

            var hint = window.FindControl<Label>("InvertHintLabel");
            Assert.Multiple(() =>
            {
                Assert.That(hint.Visible, "The hint isn't shown.");
                Assert.That(hint.Text, Is.Not.Null.And.Not.Empty, "The hint is blank.");
            });

            window.Dispose();
            client.EntMan.DeleteEntity(gateUid);
        });
    }

    [Test]
    public async Task MaximumIsOnlyOfferedForARange()
    {
        var client = Pair.Client;
        await client.WaitAssertion(() =>
        {
            var gateUid = client.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var gate = client.EntMan.AddComponent<KsTemperatureGateComponent>(gateUid);
            var gateSystem = client.System<KsTemperatureGateSystem>();

            var window = new KsTemperatureGateWindow();
            window.UpdateState(gate);
            var maximumButton = window.FindControl<Button>("SetMaxButton");
            var inTolerance = maximumButton.Visible;

            gateSystem.SetMode((gateUid, gate), KsTemperatureGateMode.Range);
            window.UpdateState(gate);

            Assert.Multiple(() =>
            {
                Assert.That(inTolerance, Is.False, "A target at the top of the scale would never open, so it shouldn't be one press away.");
                Assert.That(maximumButton.Visible, "A range's top can't be set to the maximum.");
            });

            window.Dispose();
            client.EntMan.DeleteEntity(gateUid);
        });
    }
}
