#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.McqDialogue;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Whitelist;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.McqDialogue;

/// <summary>
///     Dialogues close themselves when what their <see cref="McqDialogueSettings"/> need stops being true: the engine's
///         range check for every dialogue, and <see cref="McqDialogueHeldItemComponent"/> for one that needs an item
///         kept in hand.
/// </summary>
[TestFixture]
[TestOf(typeof(McqDialogueSystem))]
[TestOf(typeof(McqDialogueHeldItemSystem))]
public sealed class KsMcqDialogueTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private readonly EntProtoId _key = "KsTestDialogueKey";
    private readonly EntProtoId _notAKey = "KsTestDialogueNotAKey";
    private static readonly EntProtoId Human = "MobHuman";

    private const string OptionId = "a";
    private static readonly LocId OptionLoc = "ks-mcqdialogue-default-prompt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestDialogueKey
  components:
  - type: Item
  - type: KsDiscoveringScanner

- type: entity
  id: KsTestDialogueNotAKey
  components:
  - type: Item
";

    private EntityUid _grid;

    private static McqDialogueSettings HeldKey => new()
    {
        HeldItemWhitelist = new EntityWhitelist { Components = ["KsDiscoveringScanner"] },
    };

    private async Task SetUpMapAsync()
    {
        var testMap = await Pair.CreateTestMap();
        _grid = testMap.Grid;
    }

    private async Task RunSecondsAsync(float seconds)
    {
        var tickRate = (float)Server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)(tickRate * seconds));
    }

    /// <summary>
    ///     A human a metre from a source, holding <paramref name="itemPrototype"/> if given, and asked something about
    ///         the source with <paramref name="settings"/>.
    /// </summary>
    private async Task<(EntityUid User, EntityUid? Item, EntityUid Dialogue)> StartDialogueAsync(
        McqDialogueSettings? settings,
        EntProtoId? itemPrototype)
    {
        EntityUid user = default;
        EntityUid? item = null;
        EntityUid dialogue = default;
        await Server.WaitPost(() =>
        {
            var source = SEntMan.SpawnEntity(null, new EntityCoordinates(_grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(_grid, new Vector2(0.5f, 1.5f)));
            if (itemPrototype is { } prototype)
            {
                item = SEntMan.SpawnEntity(prototype, new EntityCoordinates(_grid, new Vector2(0.5f, 1.5f)));
                Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, item.Value), "precondition: the user holds the item");
            }

            dialogue = SEntMan.System<McqDialogueSystem>().StartDialogue(source, user,
                new[] { new McqDialogueData(OptionLoc, OptionId) },
                settings: settings)!.Value;
        });

        await Pair.RunTicksSync(2);
        return (user, item, dialogue);
    }

    private bool IsOpen(EntityUid dialogue) => !SEntMan.Deleted(dialogue) && !SEntMan.IsQueuedForDeletion(dialogue);

    [Test]
    public async Task HeldItemDialogueStaysOpenWhileTheItemIsHeld()
    {
        await SetUpMapAsync();
        var (_, item, dialogue) = await StartDialogueAsync(HeldKey, _key);
        await RunSecondsAsync(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsOpen(dialogue), "a dialogue whose user keeps the item stays open");
            Assert.That(SEntMan.GetComponent<McqDialogueHeldItemComponent>(dialogue).HeldUid, Is.EqualTo(item), "it recorded the held item when it started");
        });
    }

    [Test]
    public async Task HeldItemDialogueClosesWhenTheItemLeavesTheHand()
    {
        await SetUpMapAsync();
        var (user, item, dialogue) = await StartDialogueAsync(HeldKey, _key);

        await Server.WaitPost(() => SEntMan.System<SharedHandsSystem>().TryDrop(user, item!.Value));
        await RunSecondsAsync(0.5f);

        await Server.WaitAssertion(() => Assert.That(IsOpen(dialogue), Is.False, "dropping the item closes the dialogue"));
    }

    /// <summary>
    ///     The item has to pass the whitelist when the dialogue opens, and there has to be one.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task HeldItemDialogueClosesStraightAwayWithoutTheRightItem(bool holdingSomething)
    {
        await SetUpMapAsync();
        var (_, _, dialogue) = await StartDialogueAsync(HeldKey, holdingSomething ? _notAKey : (EntProtoId?)null);
        await RunSecondsAsync(0.5f);

        await Server.WaitAssertion(() => Assert.That(IsOpen(dialogue), Is.False,
            holdingSomething ? "an item the whitelist refuses closes the dialogue" : "an empty hand closes the dialogue"));
    }

    /// <summary>
    ///     The distance limit every dialogue has, from the engine's range check on its UI: 2 m by default, or
    ///         <see cref="McqDialogueSettings.Range"/>.
    /// </summary>
    [TestCase(null, 0.5f, true)]
    [TestCase(null, 3f, false)]
    [TestCase(5f, 3f, true)]
    [TestCase(5f, 7f, false)]
    public async Task DialogueClosesWhenTheUserWalksOff(float? range, float distanceFromSource, bool staysOpen)
    {
        await SetUpMapAsync();
        var (user, _, dialogue) = await StartDialogueAsync(new McqDialogueSettings { Range = range }, null);

        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetCoordinates(user,
            new EntityCoordinates(_grid, new Vector2(0.5f, 0.5f + distanceFromSource))));
        await RunSecondsAsync(0.5f);

        await Server.WaitAssertion(() => Assert.That(IsOpen(dialogue), Is.EqualTo(staysOpen),
            staysOpen ? "the control: a user nearby keeps it open" : "walking off closes the dialogue"));
    }

    /// <summary>
    ///     Asking a user about the same source again while their dialogue is open does nothing. Another user still gets
    ///         their own, and the first user can be asked again once theirs has closed.
    /// </summary>
    [Test]
    public async Task OneOpenDialoguePerUserAndSource()
    {
        await SetUpMapAsync();
        var (user, _, dialogue) = await StartDialogueAsync(null, null);
        var options = new[] { new McqDialogueData(OptionLoc, OptionId) };

        await Server.WaitAssertion(() =>
        {
            var dialogueSystem = SEntMan.System<McqDialogueSystem>();
            var source = SEntMan.GetComponent<ActiveMcqDialogueComponent>(dialogue).Source;

            Assert.That(dialogueSystem.StartDialogue(source, user, options), Is.Null, "a second one for the same user is refused");

            var otherUser = SEntMan.SpawnEntity(Human, new EntityCoordinates(_grid, new Vector2(1.5f, 0.5f)));
            Assert.That(dialogueSystem.StartDialogue(source, otherUser, options), Is.Not.Null, "the control: another user gets their own");

            dialogueSystem.CloseDialogue(dialogue);
            Assert.That(dialogueSystem.StartDialogue(source, user, options), Is.Not.Null, "once it's closed, the user can be asked again");
        });
    }

    /// <summary>
    ///     An option id comes from the client, so one the dialogue didn't offer is ignored rather than handed on.
    /// </summary>
    [TestCase(OptionId, false)]
    [TestCase("not-offered", true)]
    public async Task OnlyOfferedOptionsAreAnswers(string answerId, bool staysOpen)
    {
        await SetUpMapAsync();
        var (user, _, dialogue) = await StartDialogueAsync(null, null);

        await Server.WaitPost(() =>
        {
            var message = new McqDialogueDataSelectedMessage(answerId) { Actor = user, UiKey = McqDialogueUiKey.Key };
            SEntMan.EventBus.RaiseLocalEvent(dialogue, message);
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() => Assert.That(IsOpen(dialogue), Is.EqualTo(staysOpen),
            staysOpen ? "an option it didn't offer is ignored" : "the control: an offered option answers and closes it"));
    }
}
