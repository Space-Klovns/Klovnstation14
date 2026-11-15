#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.SalvageTablet;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.SalvageTablet;
using Content.Shared._KS14.SalvageTablet;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Access.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.SalvageTablet;

/// <summary>
///     What the client makes of the S.P.A.M.: its windows, and the tablet showing on an ore bag.
/// </summary>
[TestFixture]
[TestOf(typeof(KsSalvageTabletBoundUserInterface))]
[TestOf(typeof(KsLatheRequestPopup))]
public sealed class KsSalvageTabletUiTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId SalvageId = "SalvageIDCard";
    private static readonly EntProtoId TabletPrototype = "KsSalvageTablet";
    private static readonly EntProtoId OreBag = "OreBag";
    private const string BagSlot = "ks_salvage_tablet";
    private const string BagLayer = "tablet";
    private static readonly EntProtoId Silo = "MachineMaterialSilo";
    private static readonly ProtoId<MaterialPrototype> Steel = "Steel";

    private T[] OpenWindows<T>() where T : BaseWindow
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<T>()
            .Where(window => window.IsOpen)
            .ToArray();
    }

    /// <summary>
    ///     The order window is a window of its own. When the server closed the tablet - as it does whenever the tablet
    ///         leaves the active hand - it used to stay on screen, wired to an interface that was gone, and its order
    ///         button did nothing until the tablet was opened again.
    /// </summary>
    [Test]
    public async Task TheOrderWindowClosesWithTheTablet()
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var userUid = EntityUid.Invalid;
        var tabletUid = EntityUid.Invalid;

        await Server.WaitAssertion(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            userUid = SEntMan.SpawnEntity(Human, coordinates);
            tabletUid = SEntMan.SpawnEntity(TabletPrototype, coordinates);
            var cardUid = SEntMan.SpawnEntity(SalvageId, coordinates);
            Assert.That(handsSystem.TryPickupAnyHand(userUid, tabletUid), Is.True);
            Assert.That(handsSystem.TryPickupAnyHand(userUid, cardUid), Is.True);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, userUid);

            var tablet = new Entity<KsSalvageTabletComponent>(tabletUid, SEntMan.GetComponent<KsSalvageTabletComponent>(tabletUid));
            Assert.That(SEntMan.System<KsSalvageTabletSystem>().TryLogin(tablet, userUid), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(tabletUid, KsSalvageTabletUiKey.Key, userUid), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var windows = OpenWindows<KsSalvageTabletWindow>();
            Assert.That(windows, Has.Length.EqualTo(1), "precondition: the tablet's window is open");
            windows[0].ShowOrders();
            Assert.That(OpenWindows<KsSalvageTabletOrderWindow>(), Has.Length.EqualTo(1), "precondition: the order window is open");
        });

        await Server.WaitPost(() => SEntMan.System<SharedUserInterfaceSystem>().CloseUi(tabletUid, KsSalvageTabletUiKey.Key));
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(OpenWindows<KsSalvageTabletWindow>(), Is.Empty, "precondition: the server closed the tablet");
            Assert.That(OpenWindows<KsSalvageTabletOrderWindow>(), Is.Empty, "The order window must close with the tablet it orders through.");
        });
    }

    /// <summary>
    ///     A task's X, pressed as a player would: the card opens the task, and its X throws it away on the server.
    /// </summary>
    [Test]
    public async Task TheXOnATaskDropsIt()
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var userUid = EntityUid.Invalid;
        var tabletUid = EntityUid.Invalid;
        var cardUid = EntityUid.Invalid;

        await Server.WaitAssertion(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            userUid = SEntMan.SpawnEntity(Human, coordinates);
            tabletUid = SEntMan.SpawnEntity(TabletPrototype, coordinates);
            cardUid = SEntMan.SpawnEntity(SalvageId, coordinates);
            Assert.That(handsSystem.TryPickupAnyHand(userUid, tabletUid), Is.True);
            Assert.That(handsSystem.TryPickupAnyHand(userUid, cardUid), Is.True);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, userUid);

            var tablet = new Entity<KsSalvageTabletComponent>(tabletUid, SEntMan.GetComponent<KsSalvageTabletComponent>(tabletUid));
            Assert.That(SEntMan.System<KsSalvageTabletSystem>().TryLogin(tablet, userUid), Is.True);
            Assert.That(SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(tabletUid, KsSalvageTabletUiKey.Key, userUid), Is.True);
        });
        await Pair.RunTicksSync(10);

        KsSalvageTabletTaskCard? card = null;
        await Client.WaitAssertion(() =>
        {
            var windows = OpenWindows<KsSalvageTabletWindow>();
            Assert.That(windows, Has.Length.EqualTo(1), "precondition: the tablet's window is open");
            card = FindDescendant<KsSalvageTabletTaskCard>(windows[0]);
            Assert.That(card, Is.Not.Null, "precondition: the tablet shows a task");
        });
        await Press(card!);

        Button? dropButton = null;
        await Client.WaitAssertion(() =>
        {
            var taskWindows = OpenWindows<KsSalvageTabletTaskWindow>();
            Assert.That(taskWindows, Has.Length.EqualTo(1), "precondition: pressing the card opens the task");
            dropButton = taskWindows[0].FindControl<Button>("DropButton");
            Assert.That(dropButton.Disabled, Is.False, "precondition: nothing has been dropped yet");
        });
        await Press(dropButton!);
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<KsMinerAccountComponent>(cardUid).NextDropTime, Is.GreaterThan(TimeSpan.Zero),
                "Pressing the X must drop the task and start the cooldown.");
        });
    }

    private static T? FindDescendant<T>(Control control) where T : Control
    {
        foreach (var child in control.Children)
        {
            if (child is T found)
                return found;

            if (FindDescendant<T>(child) is { } deeper)
                return deeper;
        }

        return null;
    }

    private async Task Press(Control control)
    {
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, new ScreenCoordinates(control.GlobalPixelPosition, default),
                true, Vector2.Zero, Vector2.Zero);
            await Client.DoGuiEvent(control, args);
            await Pair.RunTicksSync(1);
        }
    }

    /// <summary>
    ///     The cart is where an order is put together: adding goes through it, and it is capped per product.
    /// </summary>
    [Test]
    public async Task TheCartHoldsWhatIsAdded()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new KsSalvageTabletOrderWindow();
            var productId = CProtoMan.EnumeratePrototypes<KsSalvageProductPrototype>().First().ID;
            window.UpdateState(points: 0, rank: 0, nextOrderTime: default, maxAmount: 2);

            window.ChangeAmount(productId, 1);
            window.ChangeAmount(productId, 1);
            window.ChangeAmount(productId, 1);
            Assert.That(window.Cart[productId], Is.EqualTo(2), "The cart must stop at the most of one product an order can hold.");

            window.ChangeAmount(productId, -2);
            Assert.That(window.Cart, Is.Empty, "A product taken down to none must leave the cart.");
        });
    }

    [Test]
    public async Task ATabletOnAnOreBagShows()
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var bagUid = EntityUid.Invalid;
        var tabletUid = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            bagUid = SEntMan.SpawnEntity(OreBag, coordinates);
            tabletUid = SEntMan.SpawnEntity(TabletPrototype, coordinates);
        });
        await Pair.RunTicksSync(10);

        bool LayerVisible()
        {
            var clientBagUid = ToClientUid(bagUid);
            var spriteComponent = CEntMan.GetComponent<SpriteComponent>(clientBagUid);
            return CEntMan.System<SpriteSystem>().LayerMapTryGet((clientBagUid, spriteComponent), BagLayer, out var layer, logMissing: false) &&
                   spriteComponent[layer].Visible;
        }

        await Client.WaitAssertion(() => Assert.That(LayerVisible(), Is.False, "A bag with no tablet must not show one."));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(bagUid, BagSlot, tabletUid, user: null), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() => Assert.That(LayerVisible(), Is.True, "A tablet attached to the bag must show on it."));
    }

    /// <summary>
    ///     The menu a right click on a lathe recipe opens. The click itself, and choosing the entry, need a mouse,
    ///         which a headless client has none of.
    /// </summary>
    [Test]
    public async Task TheLatheMenuAsksOnlyWhenTheRecipeIsShort()
    {
        await Client.WaitAssertion(() =>
        {
            var shortPopup = new KsLatheRequestPopup(canRequest: true, () => { });
            Assert.That(shortPopup.RequestButton.Disabled, Is.False, "A recipe short of materials must be able to ask for them.");

            var enoughPopup = new KsLatheRequestPopup(canRequest: false, () => { });
            Assert.That(enoughPopup.RequestButton.Disabled, Is.True, "A recipe that can be made has nothing to ask for.");
        });
    }

    private sealed class OpenTablet
    {
        public EntityUid UserUid;
        public EntityUid TabletUid;
        public EntityUid CardUid;
        public EntityUid SiloUid;
    }

    /// <summary>
    ///     A salvager holding a tablet logged in with their ID, open on their screen, beside a silo.
    /// </summary>
    private async Task<OpenTablet> OpenLoggedInTabletAsync()
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var scene = new OpenTablet();

        await Server.WaitAssertion(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            scene.UserUid = SEntMan.SpawnEntity(Human, coordinates);
            scene.TabletUid = SEntMan.SpawnEntity(TabletPrototype, coordinates);
            scene.CardUid = SEntMan.SpawnEntity(SalvageId, coordinates);
            scene.SiloUid = SEntMan.SpawnEntity(Silo, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            Assert.That(handsSystem.TryPickupAnyHand(scene.UserUid, scene.TabletUid), Is.True);
            Assert.That(handsSystem.TryPickupAnyHand(scene.UserUid, scene.CardUid), Is.True);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, scene.UserUid);

            var tablet = new Entity<KsSalvageTabletComponent>(scene.TabletUid, SEntMan.GetComponent<KsSalvageTabletComponent>(scene.TabletUid));
            Assert.That(SEntMan.System<KsSalvageTabletSystem>().TryLogin(tablet, scene.UserUid), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(scene.TabletUid, KsSalvageTabletUiKey.Key, scene.UserUid), Is.True);
        });
        await Pair.RunTicksSync(10);
        return scene;
    }

    private BoundUserInterfaceState? ServerState(OpenTablet scene)
    {
        return SEntMan.GetComponent<UserInterfaceComponent>(scene.TabletUid).States.GetValueOrDefault(KsSalvageTabletUiKey.Key);
    }

    private void SendFromClient(OpenTablet scene, BoundUserInterfaceMessage message)
    {
        var clientTabletUid = CEntMan.GetEntity(SEntMan.GetNetEntity(scene.TabletUid));
        Assert.That(CEntMan.System<UserInterfaceSystem>().TryGetOpenUi<KsSalvageTabletBoundUserInterface>(clientTabletUid, KsSalvageTabletUiKey.Key, out var boundUserInterface),
            Is.True, "precondition: the client has the tablet open");
        boundUserInterface!.SendMessage(message);
    }

    /// <summary>
    ///     The state carries end times, so the client counts down by itself: an open tablet is sent nothing while nothing
    ///         changes, and is sent a state as soon as something it shows does, here the silo.
    /// </summary>
    [Test]
    public async Task AnOpenTabletIsSentStateOnlyWhenItChanges()
    {
        var scene = await OpenLoggedInTabletAsync();
        BoundUserInterfaceState? before = null;
        await Server.WaitAssertion(() =>
        {
            before = ServerState(scene);
            Assert.That(before, Is.Not.Null, "precondition: the open tablet was sent a state");
        });

        await Pair.RunTicksSync(Server.Timing.TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerState(scene), Is.SameAs(before), "Time passing must not resend the tablet's state.");
            Assert.That(SEntMan.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(scene.SiloUid, Steel, 100), Is.True,
                "precondition: the silo takes steel");
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerState(scene), Is.Not.SameAs(before), "A change to the silo must reach the open tablet.");
        });
    }

    /// <summary>
    ///     A tablet left logged in must not let just anyone spend for its salvager: every message but logging in and out
    ///         takes salvage access, checked when it arrives.
    /// </summary>
    [Test]
    public async Task MessagesFromSomeoneWithoutAccessAreRefused()
    {
        var scene = await OpenLoggedInTabletAsync();
        var tasks = Array.Empty<ProtoId<KsSalvageTaskPrototype>>();
        await Server.WaitAssertion(() =>
        {
            tasks = SEntMan.GetComponent<KsMinerAccountComponent>(scene.CardUid).Tasks.Select(task => task.Task).ToArray();
            Assert.That(tasks, Has.Length.AtLeast(2), "precondition: the account has two tasks to track");
        });

        await Client.WaitPost(() => SendFromClient(scene, new KsSalvageTabletTrackTaskMessage(tasks[0])));
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var account = SEntMan.GetComponent<KsMinerAccountComponent>(scene.CardUid);
            Assert.That(account.Tasks.Single(task => task.Task == tasks[0]).Tracked, Is.True, "precondition: a salvager's message is taken");

            // The same card, with its access gone.
            SEntMan.GetComponent<AccessComponent>(scene.CardUid).Tags.Clear();
        });

        await Client.WaitPost(() => SendFromClient(scene, new KsSalvageTabletTrackTaskMessage(tasks[1])));
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var account = SEntMan.GetComponent<KsMinerAccountComponent>(scene.CardUid);
            Assert.That(account.Tasks.Single(task => task.Task == tasks[1]).Tracked, Is.False,
                "A message from someone without salvage access must be refused.");
        });
    }
}
