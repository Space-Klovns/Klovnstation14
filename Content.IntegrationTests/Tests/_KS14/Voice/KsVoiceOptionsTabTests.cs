using System.Threading.Tasks;
using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     The voice part of the Klovnstation 14 options tab follows cvar changes while it's open, and stops following them
///         once it's closed.
/// </summary>
[TestOf(typeof(Ks14Tab))]
public sealed class KsVoiceOptionsTabTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task TabFollowsVoiceCVarsWhileOpen()
    {
        await OverrideCVar(Side.Server, KsCCVars.VoiceEnabled, false);
        await Pair.RunTicksSync(5);

        var configuration = Client.ResolveDependency<IConfigurationManager>();
        var userInterface = Client.ResolveDependency<IUserInterfaceManager>();
        Ks14Tab tab = null;

        var hiddenOnOpen = false;
        await Client.WaitPost(() =>
        {
            tab = new Ks14Tab();
            userInterface.RootControl.AddChild(tab);
            hiddenOnOpen = !tab.FindControl<Control>("VoiceSection").Visible;
        });

        // The server switches voice on while the tab is open.
        await OverrideCVar(Side.Server, KsCCVars.VoiceEnabled, true);
        await Pair.RunTicksSync(5);

        var shownLive = false;
        var volumeLive = 0f;
        await Client.WaitPost(() =>
        {
            shownLive = tab.FindControl<Control>("VoiceSection").Visible;
            configuration.SetCVar(KsCCVars.VoiceVolume, 0.5f);
            volumeLive = tab.FindControl<OptionSlider>("SliderVoiceVolume").Slider.Value;
        });

        // Once closed, it lets go of the configuration manager.
        await Client.WaitPost(() => userInterface.RootControl.RemoveChild(tab));
        await OverrideCVar(Side.Server, KsCCVars.VoiceEnabled, false);
        await Pair.RunTicksSync(5);

        var frozenAfterClose = false;
        await Client.WaitPost(() =>
        {
            frozenAfterClose = tab.FindControl<Control>("VoiceSection").Visible;
            configuration.SetCVar(KsCCVars.VoiceVolume, KsCCVars.VoiceVolume.DefaultValue);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hiddenOnOpen, Is.True, "voice settings are hidden while the server has voice off");
            Assert.That(shownLive, Is.True, "turning voice on shows them without reopening the menu");
            Assert.That(volumeLive, Is.EqualTo(0.5f).Within(0.001f), "a volume change made elsewhere shows on the slider");
            Assert.That(frozenAfterClose, Is.True, "a closed tab no longer reacts: it has unsubscribed");
        });
    }
}
