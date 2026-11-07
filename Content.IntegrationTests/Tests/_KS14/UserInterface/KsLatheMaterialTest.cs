#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lathe.UI;
using Content.Client._KS14.Tgui.Windows.Lathe;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Lathe;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.CCVar;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     Materials going into and out of a lathe, as the client sees them tick by tick, with the client predicting ahead of the
///         server as a real one does. What goes wrong here is only visible between states: everything agrees in the end.
/// </summary>
[TestOf(typeof(SharedMaterialStorageSystem))]
public sealed class KsLatheMaterialTest : InteractionTest
{
    private const string SheetPrototype = "SheetSteel";
    private const string LathePrototype = "Autolathe";
    private const string SiloPrototype = "MachineMaterialSilo";
    private const int Sheets = 5;
    private const int Stored = 100000;

    private static readonly ProtoId<MaterialPrototype> SteelMaterial = "Steel";

    private async Task<EntityUid> SpawnLathe(bool powered = true, int stored = 0, string prototype = LathePrototype)
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(CVars.NetPredictTickBias, 4));

        await SpawnTarget(prototype);
        var lathe = STarget!.Value;
        await Server.WaitPost(() =>
        {
            SEntMan.System<PowerReceiverSystem>().SetNeedsPower(lathe, !powered);
            if (stored > 0)
                Assert.That(SEntMan.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(lathe, SteelMaterial, stored), Is.True);
        });
        await RunTicks(30);
        return lathe;
    }

    private async Task Click()
    {
        await SetKey(EngineKeyFunctions.Use, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.Use, BoundKeyState.Up);
    }

    /// <summary>
    ///     What the client's windows show of the steel, and what's in the player's hand, on each of the next ticks.
    /// </summary>
    private async Task<(List<int> Steel, List<int> Held)> WatchSteel()
    {
        var steel = new List<int>();
        var held = new List<int>();
        for (var i = 0; i < 24; i++)
        {
            await Client.WaitPost(() =>
            {
                steel.Add(CEntMan.System<SharedMaterialStorageSystem>().GetMaterialAmount(CTarget!.Value, SteelMaterial));
                held.Add(CEntMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().EnumerateHeld(CEntMan.GetEntity(Player)).Count());
            });
            await RunTicks(1);
        }

        return (steel, held);
    }

    private Task Eject(int sheets)
    {
        return Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new EjectMaterialMessage(Target!.Value, SteelMaterial, sheets)));
    }

    private int SheetVolume()
    {
        return SEntMan.System<SharedMaterialStorageSystem>().GetSheetVolume(ProtoMan.Index(SteelMaterial));
    }

    /// <summary>
    ///     The sheets leave the hand on the click and the amount goes up on the click, and neither comes back. A predicted
    ///         delete leaves the click nothing to replay after the next state, so left alone the amount falls back until the
    ///         server's arrives: up, down, up, which was the lathe window's flicker. Deleting the sheets on the server alone
    ///         stops that, and leaves them in the hand for a round trip, which looks like no prediction at all.
    /// </summary>
    [TestCase(LathePrototype)]
    [TestCase(SiloPrototype)]
    public async Task InsertedMaterialDoesNotFallBack(string prototype)
    {
        await SpawnLathe(prototype: prototype);
        await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(30); // or the client has nothing in hand to predict with
        await Click();

        // A real client ticks on with nothing from the server yet, and its own prediction is all it has to go on.
        var earlySteel = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            await Client.WaitRunTicks(1);
            await Client.WaitPost(() => earlySteel.Add(CEntMan.System<SharedMaterialStorageSystem>().GetMaterialAmount(CTarget!.Value, SteelMaterial)));
        }

        var (series, held) = await WatchSteel();
        series.InsertRange(0, earlySteel);

        var inserted = Sheets * SheetVolume();
        Assert.That(series, Is.All.EqualTo(inserted), $"The client showed: {string.Join(", ", series)}");
        // The first sample is on the click's own tick, and a predicted delete lands as that tick ends.
        Assert.That(held.Skip(1), Is.All.EqualTo(0), $"The hand held: {string.Join(", ", held)}");
        Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.Null, "The server should have taken the sheets.");
    }

    [Test]
    public async Task UnpoweredLatheShowsNothingGoingIn()
    {
        await SpawnLathe(powered: false);
        await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(30); // or the client has nothing in hand to predict with
        await Click();

        var (series, _) = await WatchSteel();

        Assert.That(series, Is.All.EqualTo(0), $"The client showed: {string.Join(", ", series)}");
        Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.Not.Null, "The sheets should still be in hand.");
    }

    /// <summary>
    ///     The sheets are gone by the time the click replays, so it replays as an empty-handed click on the machine, which opens
    ///         its window, or closes an open one, until the next state undoes that. That's the window blinking.
    /// </summary>
    [TestCase(LathePrototype, false)]
    [TestCase(LathePrototype, true)]
    [TestCase(SiloPrototype, false)]
    [TestCase(SiloPrototype, true)]
    public async Task InsertingLeavesTheWindowAsItWas(string machine, bool windowOpen)
    {
        var machineUid = await SpawnLathe(prototype: machine);
        System.Enum key = machine == SiloPrototype ? Content.Shared.Materials.OreSilo.OreSiloUiKey.Key : LatheUiKey.Key;

        if (windowOpen)
        {
            await Server.WaitPost(() => SEntMan.System<SharedUserInterfaceSystem>().OpenUi(machineUid, key, SPlayer));
            await RunTicks(15);
        }

        await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(30);
        await Click();

        var clientUi = CEntMan.System<SharedUserInterfaceSystem>();
        var series = new List<bool>();
        for (var i = 0; i < 24; i++)
        {
            await Client.WaitPost(() => series.Add(clientUi.IsUiOpen(CTarget!.Value, key, CEntMan.GetEntity(Player))));
            await RunTicks(1);
        }

        Assert.That(series, Is.All.EqualTo(windowOpen), $"The window was open: {string.Join(", ", series)}");
    }

    /// <summary>
    ///     A real click while an insert's amounts are held has to be predicted as ever: the hold only stops the replay. The
    ///         server opens the window either way, so this looks at the client, on the click.
    /// </summary>
    [Test]
    public async Task ANewClickWhileHeldIsStillPredicted()
    {
        await SpawnLathe();
        await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(30);
        await Click();

        // Only on the client, which predicts ahead, so the server's state for the insert hasn't released the hold.
        await Client.WaitRunTicks(1);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<Content.Client.Materials.KsHeldMaterialAmountsComponent>(CTarget!.Value), Is.True,
                "The test needs the hold to be on for the second click.");
        });

        await Click();
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.System<SharedUserInterfaceSystem>().IsUiOpen(CTarget!.Value, LatheUiKey.Key, CEntMan.GetEntity(Player)), Is.True);
        });
    }

    /// <summary>
    ///     Measured in a real client with latency, where a frame runs between the insert and the next state; the headless client
    ///         here can't reproduce that ordering, so the decision is checked on its own.
    /// </summary>
    [Test]
    public void TheHoldWaitsForTheServerToReachTheInsert()
    {
        var inserted = new Robust.Shared.Timing.GameTick(100);
        var before = new Robust.Shared.Timing.GameTick(99);

        Assert.That(Content.Client.Materials.MaterialStorageSystem.KsCanRelease(before, inserted, stored: 500, held: 500), Is.False,
            "The client's own prediction already shows the insert, so it can't be what releases the hold.");
        Assert.That(Content.Client.Materials.MaterialStorageSystem.KsCanRelease(inserted, inserted, stored: 500, held: 500), Is.True);
        Assert.That(Content.Client.Materials.MaterialStorageSystem.KsCanRelease(inserted, inserted, stored: 0, held: 500), Is.False);
    }

    /// <summary>
    ///     An eject has to show through a hold left by an insert a moment before, or the amount looks stuck until it's released.
    /// </summary>
    [Test]
    public async Task EjectRightAfterAnInsertShowsAtOnce()
    {
        await SpawnLathe(stored: Stored);
        await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(30);
        await Click();
        await Eject(1);
        await RunTicks(1);

        await Client.WaitAssertion(() =>
        {
            Assert.That(
                CEntMan.System<SharedMaterialStorageSystem>().GetMaterialAmount(CTarget!.Value, SteelMaterial),
                Is.LessThan(Stored + Sheets * SheetVolume()),
                "The sheet taken out should come off what's shown.");
        });
    }

    /// <summary>
    ///     The amount has to change on the client's own tick, and hold, and the hand has to have a stack all the way through.
    /// </summary>
    [TestCase(LathePrototype)]
    [TestCase(SiloPrototype)]
    public async Task EjectIsPredictedIntoTheHand(string prototype)
    {
        await SpawnLathe(stored: Stored, prototype: prototype);
        await Eject(Sheets);

        var expected = Stored - Sheets * SheetVolume();
        var series = new List<int>();
        var heldCounts = new List<int>();
        for (var i = 0; i < 24; i++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                series.Add(CEntMan.GetComponent<MaterialStorageComponent>(CTarget!.Value).Storage.GetValueOrDefault(SteelMaterial));

                var clientHands = CEntMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
                var held = clientHands.EnumerateHeld(CEntMan.GetEntity(Player)).ToList();
                heldCounts.Add(held.Count == 1 && CEntMan.TryGetComponent(held[0], out StackComponent? stackComponent) ? stackComponent.Count : -1);
            });
        }

        Assert.That(series, Is.All.EqualTo(expected), $"The client showed: {string.Join(", ", series)}");
        Assert.That(heldCounts, Is.All.EqualTo(Sheets), $"The hand held: {string.Join(", ", heldCounts)}");

        await Server.WaitAssertion(() =>
        {
            var heldUid = HandSys.GetActiveItem((SPlayer, Hands));
            Assert.That(heldUid, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<StackComponent>(heldUid!.Value).Count, Is.EqualTo(Sheets));
            Assert.That(SEntMan.GetComponent<MaterialStorageComponent>(STarget!.Value).Storage[SteelMaterial], Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task EjectJoinsAStackInHand()
    {
        await SpawnLathe(stored: Stored);
        var heldNetEntity = await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(5);

        await Eject(Sheets);
        await RunTicks(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<StackComponent>(SEntMan.GetEntity(heldNetEntity)).Count, Is.EqualTo(Sheets * 2));
            Assert.That(HandSys.EnumerateHeld((SPlayer, Hands)).Count(), Is.EqualTo(1), "It should have joined the stack, not gone into the other hand.");
        });
    }

    /// <summary>
    ///     On the machine and not beside the player, and a second lot joins the first rather than piling up as separate stacks.
    /// </summary>
    [Test]
    public async Task EjectWithFullHandsLeavesTheSheetsOnTheLathe()
    {
        var lathe = await SpawnLathe(stored: Stored);

        await Server.WaitPost(() =>
        {
            foreach (var hand in Hands.Hands.Keys.ToList())
            {
                var filler = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
                Assert.That(HandSys.TryPickup(SPlayer, filler, hand), Is.True);
            }
        });
        await RunTicks(5);

        await Eject(Sheets);
        await RunTicks(15);
        await Eject(Sheets);
        await RunTicks(15);

        await Server.WaitAssertion(() =>
        {
            var latheCoordinates = SEntMan.GetComponent<TransformComponent>(lathe).Coordinates;
            var nearby = SEntMan.System<EntityLookupSystem>()
                .GetEntitiesInRange<StackComponent>(latheCoordinates, 0.1f)
                .ToList();

            Assert.That(nearby, Has.Count.EqualTo(1), "The sheets should be where the lathe is, in one stack.");
            Assert.That(nearby[0].Comp.Count, Is.EqualTo(Sheets * 2));
        });
    }

    /// <summary>
    ///     The message is the client's own, and nothing else keeps it to the machine in front of them.
    /// </summary>
    [Test]
    public async Task EjectFromOutOfReachDoesNothing()
    {
        var lathe = await SpawnLathe(stored: Stored);
        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetCoordinates(
            lathe,
            SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates.Offset(new System.Numerics.Vector2(20f, 0f))));
        await RunTicks(5);

        await Eject(Sheets);
        await RunTicks(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<MaterialStorageComponent>(lathe).Storage[SteelMaterial], Is.EqualTo(Stored));
            Assert.That(HandSys.EnumerateHeld((SPlayer, Hands)), Is.Empty);
        });
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (var child in root.Children)
        {
            if (child is T match)
                yield return match;

            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }

    /// <summary>
    ///     Every state repopulates the window, and a change of materials brings a state. Replacing the icons then means a new
    ///         entity for every row, each time, in both themes' windows.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task IconsSurviveAChangeOfMaterials(bool tgui)
    {
        var clientConfiguration = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfiguration.SetCVar(KsCCVars.TguiStyle, tgui));
        await RunTicks(2);

        var lathe = await SpawnLathe(stored: Stored);

        await Server.WaitPost(() =>
        {
            var latheSystem = SEntMan.System<LatheSystem>();
            var recipe = latheSystem.GetAvailableRecipes(lathe, SEntMan.GetComponent<LatheComponent>(lathe))
                .Select(id => ProtoMan.Index(id))
                .First(prototype => prototype.Result != null && latheSystem.CanProduce(lathe, prototype, 4, SEntMan.GetComponent<LatheComponent>(lathe)));

            Assert.That(latheSystem.TryAddToQueue(lathe, recipe, 2, SEntMan.GetComponent<LatheComponent>(lathe)), Is.True);
            Assert.That(latheSystem.TryAddToQueue(lathe, recipe, 2, SEntMan.GetComponent<LatheComponent>(lathe)), Is.True);
            latheSystem.TryStartProducing(lathe);

            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(lathe, LatheUiKey.Key, SPlayer);
        });
        await RunTicks(15);

        List<EntityPrototypeView> iconsBefore = null!;
        await Client.WaitPost(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.Single(control => control is KsTguiLatheWindow or LatheMenu);
            iconsBefore = Descendants<EntityPrototypeView>(window).ToList();
            Assert.That(iconsBefore, Has.Count.GreaterThan(2), "There should be designs, a queue and something printing to look at.");
        });

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(lathe, SteelMaterial, 500), Is.True);
        });
        await RunTicks(15);

        await Client.WaitAssertion(() =>
        {
            foreach (var icon in iconsBefore)
            {
                Assert.That(icon.IsInsideTree, Is.True, "A row's icon was replaced although its recipe didn't change.");
            }
        });

        await Client.WaitPost(() => clientConfiguration.SetCVar(KsCCVars.TguiStyle, true));
    }
}
