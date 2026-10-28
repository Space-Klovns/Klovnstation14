#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.McqDialogue;
using Content.Client._KS14.McqDialogue.UI;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.McqDialogue;
using Content.Shared._KS14.OreVent;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.McqDialogue;

/// <summary>
///     A dialogue started from predicted code shows its window on the user's client at once, before the server's dialogue
///         exists, and that dialogue's UI takes the window over when it arrives. Driven through an ore vent's question,
///         which is asked from a predicted interaction, using <see cref="Tests._KS14.OreVent.KsOreVentTest"/>'s
///         prototypes.
/// </summary>
[TestFixture]
[TestOf(typeof(McqDialoguePredictionSystem))]
public sealed class KsMcqDialoguePredictionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly EntProtoId _vent = "KsTestOreVent";
    private readonly EntProtoId _scanner = "KsTestOreVentScanner";
    private static readonly EntProtoId Human = "MobHuman";

    private EntityUid _grid;

    /// <summary>
    ///     A vent, and the player's human next to it with a scanner in hand.
    /// </summary>
    private async Task<(EntityUid Vent, EntityUid User, EntityUid Scanner)> SetUpAsync()
    {
        var testMap = await Pair.CreateTestMap();
        _grid = testMap.Grid;

        EntityUid vent = default;
        EntityUid user = default;
        EntityUid scanner = default;
        await Server.WaitPost(() =>
        {
            vent = SEntMan.SpawnEntity(_vent, new EntityCoordinates(_grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(_grid, new Vector2(0.5f, 1.5f)));
            scanner = SEntMan.SpawnEntity(_scanner, new EntityCoordinates(_grid, new Vector2(0.5f, 1.5f)));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, scanner), "precondition: the user holds the scanner");
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
        });

        await Pair.RunTicksSync(10);
        return (vent, user, scanner);
    }

    private async Task RunSecondsAsync(float seconds)
    {
        var tickRate = (float)Server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)(tickRate * seconds));
    }

    private void ClientScan(EntityUid vent, EntityUid user, EntityUid scanner)
    {
        CEntMan.System<SharedInteractionSystem>().InteractUsing(ToClientUid(user), ToClientUid(scanner), ToClientUid(vent),
            new EntityCoordinates(ToClientUid(_grid), new Vector2(0.5f, 0.5f)), checkCanInteract: false, checkCanUse: false);
    }

    private async Task ServerScanAsync(EntityUid vent, EntityUid user, EntityUid scanner)
    {
        await Server.WaitPost(() => SEntMan.System<SharedInteractionSystem>().InteractUsing(user, scanner, vent,
            new EntityCoordinates(_grid, new Vector2(0.5f, 0.5f)), checkCanInteract: false, checkCanUse: false));
    }

    private McqDialogueWindow[] OpenWindows()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<McqDialogueWindow>()
            .Where(window => window.IsOpen)
            .ToArray();
    }

    [Test]
    public async Task TheWindowOpensBeforeTheServerAnswersAndIsTakenOver()
    {
        var (vent, user, scanner) = await SetUpAsync();

        McqDialogueWindow? predictedWindow = null;
        await Client.WaitPost(() =>
        {
            ClientScan(vent, user, scanner);

            // No tick has run, so nothing can have come from the server.
            Assert.That(OpenWindows(), Has.Length.EqualTo(1), "the window opens as soon as the client scans");
            predictedWindow = OpenWindows()[0];
        });

        await ServerScanAsync(vent, user, scanner);
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<McqDialoguePredictedComponent>(ToClientUid(vent)), Is.False, "the server's dialogue took it over");
            Assert.That(OpenWindows(), Is.EqualTo(new[] { predictedWindow }), "and kept that window, rather than opening a second one");
        });
    }

    [Test]
    public async Task AnAnswerGivenBeforeTheServerAnswersIsSentOnceItDoes()
    {
        var (vent, user, scanner) = await SetUpAsync();

        await Client.WaitPost(() =>
        {
            ClientScan(vent, user, scanner);
            CEntMan.System<McqDialoguePredictionSystem>().Choose(ToClientUid(vent), "yes");
            Assert.That(OpenWindows(), Is.Empty, "answering closes the window");
        });

        await ServerScanAsync(vent, user, scanner);
        await RunSecondsAsync(1f);

        await Server.WaitAssertion(() =>
        {
            var ventComponent = SEntMan.GetComponent<OreVentComponent>(vent);
            Assert.That(ventComponent.DoingClearing || ventComponent.BeingTapped || ventComponent.Tapped, "the early yes reached the server");
        });

        await Client.WaitAssertion(() => Assert.That(OpenWindows(), Is.Empty, "and no window reopened for it"));
    }

    [Test]
    public async Task AWindowClosedBeforeTheServerAnswersClosesItsDialogue()
    {
        var (vent, user, scanner) = await SetUpAsync();

        await Client.WaitPost(() =>
        {
            ClientScan(vent, user, scanner);
            OpenWindows()[0].Close();
        });

        await ServerScanAsync(vent, user, scanner);
        await RunSecondsAsync(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<McqDialogueSourceComponent>(vent), Is.False, "the server's dialogue was closed too");
            Assert.That(SEntMan.GetComponent<OreVentComponent>(vent).DoingClearing, Is.False, "and nothing started");
        });

        await Client.WaitAssertion(() => Assert.That(OpenWindows(), Is.Empty, "and no window reopened for it"));
    }

    /// <summary>
    ///     Answering through the dialogue's UI is predicted: what the answer starts - here, the excavation's do-after -
    ///         is already running on the client before the server has handled the answer.
    /// </summary>
    [Test]
    public async Task AnsweringIsPredicted()
    {
        var (vent, user, scanner) = await SetUpAsync();

        await ServerScanAsync(vent, user, scanner);
        await Pair.RunTicksSync(10);

        await Client.WaitPost(() =>
        {
            EntityUid? dialogueUid = null;
            var dialogueEnumerator = CEntMan.EntityQueryEnumerator<ActiveMcqDialogueComponent>();
            while (dialogueEnumerator.MoveNext(out var uid, out var dialogueComponent))
            {
                if (dialogueComponent.Source == ToClientUid(vent))
                    dialogueUid = uid;
            }

            Assert.That(dialogueUid, Is.Not.Null, "precondition: the client has the server's dialogue");
            Assert.That(CEntMan.System<SharedUserInterfaceSystem>().TryGetOpenUi(dialogueUid!.Value, McqDialogueUiKey.Key, out var bui),
                "precondition: and its UI is open");

            // As the option's button does.
            bui!.SendPredictedMessage(new McqDialogueDataSelectedMessage("yes"));
        });

        // The client alone, so the server can't have handled the answer yet.
        await Client.WaitRunTicks(2);

        await Client.WaitAssertion(() => Assert.That(CEntMan.GetComponent<DoAfterComponent>(ToClientUid(user)).DoAfters, Is.Not.Empty,
            "the client started the excavation itself"));
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DoAfterComponent>(user).DoAfters, Is.Empty,
            "precondition: before the server did"));

        await RunSecondsAsync(1f);
        await Server.WaitAssertion(() =>
        {
            var ventComponent = SEntMan.GetComponent<OreVentComponent>(vent);
            Assert.That(ventComponent.DoingClearing || ventComponent.BeingTapped || ventComponent.Tapped, "and the server followed");
        });
    }

    [Test]
    public async Task AWindowTheServerNeverOpensClosesAfterTheTimeout()
    {
        var (vent, user, scanner) = await SetUpAsync();

        await Client.WaitPost(() => ClientScan(vent, user, scanner));

        var timeout = (float)McqDialoguePredictionSystem.PredictionTimeout.TotalSeconds;
        await RunSecondsAsync(timeout / 2f);
        await Client.WaitAssertion(() => Assert.That(OpenWindows(), Has.Length.EqualTo(1), "the control: it waits for the server for a while"));

        await RunSecondsAsync(timeout / 2f + 0.5f);
        await Client.WaitAssertion(() =>
        {
            Assert.That(OpenWindows(), Is.Empty, "then gives up");
            Assert.That(CEntMan.HasComponent<McqDialoguePredictedComponent>(ToClientUid(vent)), Is.False, "and forgets it");
        });
    }
}
