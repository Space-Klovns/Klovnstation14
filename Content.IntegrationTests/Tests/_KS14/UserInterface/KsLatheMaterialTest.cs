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
    ///     The amount of steel the client shows on each of the next ticks.
    /// </summary>
    private async Task<List<int>> WatchSteel()
    {
        var series = new List<int>();
        for (var i = 0; i < 24; i++)
        {
            await Client.WaitPost(() => series.Add(CEntMan.GetComponent<MaterialStorageComponent>(CTarget!.Value).Storage.GetValueOrDefault(SteelMaterial)));
            await RunTicks(1);
        }

        return series;
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
    ///     A predicted delete of the inserted stack leaves the click nothing to replay after the next state, so the amount
    ///         falls back to what it was until the server's arrives: up, down, up. That was the lathe window's flicker.
    /// </summary>
    [TestCase(LathePrototype)]
    [TestCase(SiloPrototype)]
    public async Task InsertedMaterialDoesNotFallBack(string prototype)
    {
        await SpawnLathe(prototype: prototype);
        await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(30); // or the client has nothing in hand to predict with
        await Click();

        var series = await WatchSteel();

        var inserted = Sheets * SheetVolume();
        Assert.That(series, Is.All.EqualTo(inserted), $"The client showed: {string.Join(", ", series)}");
        Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.Null, "The server should have taken the sheets.");
    }

    [Test]
    public async Task UnpoweredLatheShowsNothingGoingIn()
    {
        await SpawnLathe(powered: false);
        await PlaceInHands(SheetPrototype, Sheets);
        await RunTicks(30); // or the client has nothing in hand to predict with
        await Click();

        var series = await WatchSteel();

        Assert.That(series, Is.All.EqualTo(0), $"The client showed: {string.Join(", ", series)}");
        Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.Not.Null, "The sheets should still be in hand.");
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
