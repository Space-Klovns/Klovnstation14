#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Windows.Fax;
using Content.Client.Fax.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Fax;
using Content.Shared.UserInterface;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The fax machine's BUI opens the tgui window under the tgui theme and the stock one otherwise, and the tgui
///         window's buttons send what the stock window's do.
/// </summary>
[TestOf(typeof(KsTguiFaxWindow))]
public sealed class KsTguiFaxWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId FaxMachine = "FaxMachineCaptain";
    private static readonly EntProtoId Human = "MobHuman";

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheBuiOpensTheThemesWindow(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        EntityUid fax = default;
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            fax = SEntMan.SpawnEntity(FaxMachine, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(fax, FaxUiKey.Key, user);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var uiManager = Client.ResolveDependency<IUserInterfaceManager>();
            var windows = uiManager.WindowRoot.Children.ToList();
            Assert.That(windows.OfType<KsTguiFaxWindow>().Any(), Is.EqualTo(tgui));
            Assert.That(windows.OfType<FaxWindow>().Any(), Is.EqualTo(!tgui));
        });
    }

    /// <summary>
    ///     Fires a button's press as a click would, which a headless test has no mouse for.
    /// </summary>
    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Action<BaseButton.ButtonEventArgs>?)field.GetValue(button))?.Invoke(null!);
    }

    [Test]
    public async Task EveryActionFiresItsEvent()
    {
        KsTguiFaxWindow window = null!;
        var fired = new List<string>();

        await Client.WaitPost(() =>
        {
            window = new KsTguiFaxWindow();
            window.CopyButtonPressed += () => fired.Add("copy");
            window.SendButtonPressed += () => fired.Add("send");
            window.RefreshButtonPressed += () => fired.Add("refresh");
            window.FileButtonPressed += () => fired.Add("file");
            window.PeerSelected += address => fired.Add("peer:" + address);

            window.UpdateState(new FaxUiState("Fax", new Dictionary<string, string> { ["a"] = "Alpha", ["b"] = "Beta" },
                canSend: true, canCopy: true, isPaperInserted: true, destAddress: "a"));

            Press(window.CopyButton);
        });
        await Client.WaitPost(() =>
        {
            Assert.That(window.PeerButtons.Count(), Is.EqualTo(2));
            Assert.That(window.CopyButton.Disabled, Is.False);
            Assert.That(window.SendButton.Disabled, Is.False);

            Press(window.SendButton);
            Press(window.RefreshButton);
            Press(window.FileButton);
            Press(window.PeerButtons.Last());

            Assert.That(window.OfficePaper, Is.False);
            Press(window.PaperTypeButton);
            Assert.That(window.OfficePaper, Is.True);

            Assert.That(fired, Is.EqualTo(new[] { "copy", "send", "refresh", "file", "peer:b" }));
        });
    }
}
