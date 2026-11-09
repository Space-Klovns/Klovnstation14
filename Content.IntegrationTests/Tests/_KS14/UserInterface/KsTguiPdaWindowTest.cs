#nullable enable
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Pda;
using Content.Client.PDA;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared.PDA.Ringer;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The PDA's BUI opens the tgui window under the tgui theme and the stock one otherwise, and the window's controls send
///         what the stock ones do: the flashlight, the ID and pen ejects, the ringtone, and running and closing a program.
/// </summary>
[TestOf(typeof(KsTguiPdaWindow))]
public sealed class KsTguiPdaWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly EntProtoId PdaProto = "PassengerPDA";
    private static readonly EntProtoId Human = "MobHuman";

    [TestCase(true)]
    [TestCase(false)]
    public async Task BuiPicksWindowByThemeAndEveryActionFires(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);

        var testMap = await Pair.CreateTestMap();
        EntityUid actor = default;
        EntityUid pda = default;
        await Server.WaitPost(() =>
        {
            actor = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            pda = SEntMan.SpawnEntity(PdaProto, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
        });
        await Pair.RunTicksSync(5);

        await Server.WaitPost(() => Server.System<UserInterfaceSystem>().TryOpenUi(pda, PdaUiKey.Key, actor));
        await Pair.RunTicksSync(10);

        PdaBoundUserInterface? bui = null;
        IKsPdaMenu? menu = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.System<Robust.Client.GameObjects.UserInterfaceSystem>()
                .TryGetOpenUi<PdaBoundUserInterface>(ToClientUid(pda), PdaUiKey.Key, out bui), "the PDA UI is open on the client");
            menu = (IKsPdaMenu?)typeof(PdaBoundUserInterface).GetField("_menu", Any)!.GetValue(bui);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu is KsTguiPdaWindow, Is.EqualTo(tgui));
            Assert.That(menu is PdaMenu, Is.EqualTo(!tgui));
        });

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<CartridgeLoaderComponent>(pda).PreinstalledPrograms, Is.Not.Empty, "precondition: the PDA has programs"));

        // The flashlight, the ID eject, the pen eject and the ringtone.
        await Client.WaitPost(() =>
        {
            Raise(menu!.FlashLightToggleButton, "OnToggled");
            Raise(menu.EjectIdButton, "OnPressed");
            Raise(menu.EjectPenButton, "OnPressed");
            Raise(menu.AccessRingtoneButton, "OnPressed");
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var component = SEntMan.GetComponent<PdaComponent>(pda);
            Assert.That(component.FlashlightOn, Is.True, "the flashlight toggled");
            Assert.That(component.ContainedId, Is.Null, "the ID was ejected");
            Assert.That(component.PenSlot.HasItem, Is.False, "the pen was ejected");
            Assert.That(Server.System<UserInterfaceSystem>().IsUiOpen(pda, RingerUiKey.Key, actor), Is.True, "the ringtone UI opened");
        });

        // Running a program from the list shows it in the window, closing it stops it.
        if (tgui)
        {
            await Client.WaitPost(() =>
            {
                var programList = (BoxContainer)typeof(KsTguiPdaWindow).GetField("_programList", Any)!.GetValue(menu)!;
                var row = (BoxContainer)programList.Children.First();
                var open = row.Children.OfType<KsTguiButton>().First();
                Raise(open, "OnPressed");
            });
            await Pair.RunTicksSync(10);

            await Server.WaitAssertion(() =>
                Assert.That(SEntMan.GetComponent<CartridgeLoaderComponent>(pda).ActiveProgram, Is.Not.Null, "the program started"));
            await Client.WaitAssertion(() =>
            {
                Assert.That(menu!.ProgramView.ChildCount, Is.GreaterThan(0), "the program's UI is hosted");
                Assert.That(menu.ProgramView.Visible, Is.True);
            });

            await Client.WaitPost(() =>
            {
                var close = (KsTguiButton)typeof(KsTguiPdaWindow).GetField("_closeProgramButton", Any)!.GetValue(menu)!;
                Raise(close, "OnPressed");
            });
            await Pair.RunTicksSync(10);

            await Server.WaitAssertion(() =>
                Assert.That(SEntMan.GetComponent<CartridgeLoaderComponent>(pda).ActiveProgram, Is.Null, "the program stopped"));
            await Client.WaitAssertion(() => Assert.That(menu!.ProgramView.Visible, Is.False));
        }

        await Client.WaitPost(() => bui!.Dispose());
    }

    private static void Raise(BaseButton button, string eventName)
    {
        var field = typeof(BaseButton).GetField(eventName, Any)!;
        ((Delegate)field.GetValue(button)!).DynamicInvoke([null]);
    }
}
