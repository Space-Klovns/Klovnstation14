#nullable enable
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Windows.Materials;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.CCVar;
using Content.Shared.Materials.OreSilo;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The silo window's machine rows are selected exactly when the machine is linked, however fast they are clicked (#451).
/// </summary>
[TestOf(typeof(KsTguiOreSiloWindow))]
public sealed class KsOreSiloLinkedRowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId SiloProto = "MachineMaterialSilo";
    private static readonly EntProtoId LatheProto = "Autolathe";
    private static readonly EntProtoId Human = "MobHuman";

    private EntityUid _silo;
    private EntityUid _latheA;
    private EntityUid _latheB;

    [Test]
    public async Task RowsFollowTheLinksWhenClickedFast()
    {
        await OpenSilo(farB: false);

        var netA = SEntMan.GetNetEntity(_latheA);
        var netB = SEntMan.GetNetEntity(_latheB);

        // Presses a tick or less apart, so each lands while the server's answer to the one before is still on its way.
        var presses = new[] { netA, netB, netA, netA, netB, netA, netB, netB, netA };
        foreach (var press in presses)
        {
            await Client.WaitPost(() => Press(Window().ClientButtons[press]));
            await Pair.RunTicksSync(1);
        }

        await Pair.RunTicksSync(20);

        var linkedA = false;
        var linkedB = false;
        await Server.WaitPost(() =>
        {
            linkedA = SEntMan.GetComponent<OreSiloClientComponent>(_latheA).Silo == _silo;
            linkedB = SEntMan.GetComponent<OreSiloClientComponent>(_latheB).Silo == _silo;
        });

        await Client.WaitAssertion(() =>
        {
            var window = Window();
            Assert.That(window.ClientButtons[netA].Selected, Is.EqualTo(linkedA), "the first machine's row against the server's link");
            Assert.That(window.ClientButtons[netB].Selected, Is.EqualTo(linkedB), "the second machine's row against the server's link");
            window.Close();
        });
    }

    /// <summary>
    ///     A silo with two machines in reach and its window open. The second machine is out of the player's sight, as a
    ///         machine across the station is, when <paramref name="farB"/>.
    /// </summary>
    private async Task OpenSilo(bool farB)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        if (farB)
            await OverrideCVar(Side.Server, CVars.NetPVS, true);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            _silo = SEntMan.SpawnEntity(SiloProto, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            var user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            SEntMan.System<PowerReceiverSystem>().SetNeedsPower(_silo, false);
            _latheA = SEntMan.SpawnEntity(LatheProto, new EntityCoordinates(testMap.Grid, new Vector2(2.5f, 0.5f)));
            _latheB = SEntMan.SpawnEntity(LatheProto, new EntityCoordinates(testMap.Grid, new Vector2(farB ? 300.5f : -1.5f, 0.5f)));
        });
        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var actor = Server.PlayerMan.GetSessionById(ServerSession!.UserId).AttachedEntity!.Value;
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(_silo, OreSiloUiKey.Key, actor);
        });
        await Pair.RunTicksSync(15);
    }

    /// <summary>
    ///     A machine the player can't see, which the client knows nothing of beyond its id, shows as linked once linked.
    /// </summary>
    [Test]
    public async Task AMachineOutOfSightShowsLinked()
    {
        await OpenSilo(farB: true);

        var netB = SEntMan.GetNetEntity(_latheB);
        await Client.WaitPost(() =>
        {
            Assert.That(CEntMan.TryGetEntity(netB, out var clientB) && CEntMan.HasComponent<OreSiloClientComponent>(clientB),
                Is.False, "the far machine is out of the player's sight, as the test means it to be");
            Press(Window().ClientButtons[netB]);
        });
        await Pair.RunTicksSync(20);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<OreSiloClientComponent>(_latheB).Silo, Is.EqualTo(_silo), "pressing the row linked the machine"));
        await Client.WaitAssertion(() =>
        {
            Assert.That(Window().ClientButtons[netB].Selected, Is.True, "the far machine is linked, but its row isn't selected");
            Window().Close();
        });
    }

    private KsTguiOreSiloWindow Window()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiOreSiloWindow>().Single();
    }

    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((System.Action<BaseButton.ButtonEventArgs>?) field.GetValue(button))?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }
}
