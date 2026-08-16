using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Sensors;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     Regression coverage for the datalink machines' player-facing knobs.
///         The transmitter and receiver are deliberately non-networked (see
///         <see cref="KsDatalinkTransmitterComponent"/>), so their BUI handlers
///         must never <c>Dirty</c> the component: that trips the "dirtied a
///         non-networked component" debug assert the instant a player toggles or
///         tunes the machine. Every BUI message is driven through the real
///         client-&gt;server path, so a reintroduced <c>Dirty</c> call crashes here
///         exactly as it would in game.
/// </summary>
public sealed class KsDatalinkInteractionTest : InteractionTest
{
    private const string Transmitter = "KsMachineDatalinkTransmitter";
    private const string Receiver = "KsMachineDatalinkReceiver";

    [Test]
    public async Task TestTransmitterBuiMessages()
    {
        await SpawnTarget(Transmitter);
        var comp = SEntMan.GetComponent<KsDatalinkTransmitterComponent>(STarget!.Value);

        Assert.That(comp.Enabled, Is.True, "transmitter should start enabled");

        await Activate();
        Assert.That(IsUiOpen(KsDatalinkTransmitterUiKey.Key), "transmitter BUI failed to open");

        // Each message reproduces a distinct handler that previously Dirty'd the
        // non-networked component; sending them must mutate state without crashing.
        await SendBui(KsDatalinkTransmitterUiKey.Key, new KsDatalinkToggleMessage());
        await SendBui(KsDatalinkTransmitterUiKey.Key, new KsDatalinkSetFrequencyMessage(2500));
        await SendBui(KsDatalinkTransmitterUiKey.Key, new KsDatalinkSetPowerMessage(0.25f));

        Assert.Multiple(() =>
        {
            Assert.That(comp.Enabled, Is.False, "toggle message did not flip Enabled");
            Assert.That(comp.Frequency, Is.EqualTo(2500), "set-frequency message did not land");
            Assert.That(comp.PowerFraction, Is.EqualTo(0.25f).Within(0.0001f), "set-power message did not land");
        });
    }

    [Test]
    public async Task TestReceiverBuiMessages()
    {
        await SpawnTarget(Receiver);
        var comp = SEntMan.GetComponent<KsDatalinkReceiverComponent>(STarget!.Value);

        Assert.That(comp.Enabled, Is.True, "receiver should start enabled");

        await Activate();
        Assert.That(IsUiOpen(KsDatalinkReceiverUiKey.Key), "receiver BUI failed to open");

        await SendBui(KsDatalinkReceiverUiKey.Key, new KsDatalinkToggleMessage());
        await SendBui(KsDatalinkReceiverUiKey.Key, new KsDatalinkSetFrequencyMessage(2600));

        Assert.Multiple(() =>
        {
            Assert.That(comp.Enabled, Is.False, "toggle message did not flip Enabled");
            Assert.That(comp.Frequency, Is.EqualTo(2600), "set-frequency message did not land");
        });
    }
}
