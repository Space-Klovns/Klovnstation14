#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Construction;
using Content.Shared._KS14.LearnedRecipes;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.LearnedRecipes;

[TestFixture]
[TestOf(typeof(KsLearnedRecipesSystem))]
[TestOf(typeof(KsRecipeGranterSystem))]
public sealed class KsLearnedRecipesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, DummyTicker = false, Dirty = true };

    private readonly ProtoId<ConstructionPrototype> _learnedRecipe = "KsTestLearnedRecipe";
    private readonly ProtoId<ConstructionPrototype> _freeRecipe = "KsTestFreeRecipe";
    private readonly EntProtoId _result = "KsTestLearnedResult";
    private readonly EntProtoId _book = "KsTestGranterBook";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Steel = "SheetSteel1";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestLearnedResult
  components:
  - type: Item
  - type: Construction
    graph: KsTestLearnedGraph
    node: done

- type: constructionGraph
  id: KsTestLearnedGraph
  start: start
  graph:
  - node: start
    edges:
    - to: done
      steps:
      - material: Steel
        amount: 1
  - node: done
    entity: KsTestLearnedResult

- type: construction
  id: KsTestLearnedRecipe
  graph: KsTestLearnedGraph
  startNode: start
  targetNode: done
  category: construction-category-weapons
  objectType: Item
  ksMustBeLearned: true

- type: construction
  id: KsTestFreeRecipe
  graph: KsTestLearnedGraph
  startNode: start
  targetNode: done
  category: construction-category-weapons
  objectType: Item

- type: entity
  parent: KsBaseRecipeGranterBook
  id: KsTestGranterBook
  components:
  - type: KsRecipeGranter
    recipes:
    - KsTestLearnedRecipe
    pagesToMastery: 1
    readTime: 0.1
";

    private async Task<EntityUid> SpawnPlayerMobAsync()
    {
        var testMap = await Pair.CreateTestMap();
        var mobUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mobUid = SEntMan.SpawnEntity(Human, testMap.GridCoords);
            var mindSystem = SEntMan.System<SharedMindSystem>();
            mindSystem.TransferTo(mindSystem.CreateMind(ServerSession!.UserId), mobUid);
        });
        await Pair.RunTicksSync(10);
        return mobUid;
    }

    /// <summary>
    ///     A learned recipe is hidden until taught, and what the mind learns reaches the owning client.
    /// </summary>
    [Test]
    public async Task TeachingUnlocksTheRecipeOnBothSides()
    {
        var mobUid = await SpawnPlayerMobAsync();
        var serverLearned = SEntMan.System<KsLearnedRecipesSystem>();
        var prototypeManager = Server.ResolveDependency<IPrototypeManager>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(serverLearned.CanUse(mobUid, prototypeManager.Index(_freeRecipe)), Is.True,
                "A recipe that needn't be learned is always usable.");
            Assert.That(serverLearned.CanUse(mobUid, prototypeManager.Index(_learnedRecipe)), Is.False,
                "An unlearned recipe must not be usable.");

            Assert.That(SEntMan.System<SharedMindSystem>().TryGetMind(mobUid, out var mindId, out _));
            serverLearned.Teach(mindId, _learnedRecipe);
            Assert.That(serverLearned.CanUse(mobUid, prototypeManager.Index(_learnedRecipe)), Is.True);
        });

        await Pair.RunTicksSync(10);

        var clientMobUid = ToClientUid(mobUid);
        await Client.WaitAssertion(() =>
        {
            var clientLearned = CEntMan.System<KsLearnedRecipesSystem>();
            Assert.That(clientLearned.Knows(clientMobUid, _learnedRecipe), Is.True,
                "The client must see what its mind learned, so the crafting menu can show it.");
        });
    }

    /// <summary>
    ///     The server refuses an unlearned recipe even when the client asks for it by id.
    /// </summary>
    [Test]
    public async Task ServerRefusesAnUnlearnedRecipe()
    {
        var mobUid = await SpawnPlayerMobAsync();
        var constructionSystem = SEntMan.System<ConstructionSystem>();
        Task<bool>? attempt = null;

        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity(Steel, SEntMan.GetComponent<TransformComponent>(mobUid).Coordinates);
            attempt = constructionSystem.TryStartItemConstruction(_learnedRecipe, mobUid);
        });
        await Pair.RunTicksSync(30);

        Assert.That(attempt!.IsCompleted, Is.True);
        Assert.That(await attempt, Is.False, "An unlearned recipe must be refused.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == _result.Id), Is.False);
        });

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<SharedMindSystem>().TryGetMind(mobUid, out var mindId, out _));
            SEntMan.System<KsLearnedRecipesSystem>().Teach(mindId, _learnedRecipe);
            attempt = constructionSystem.TryStartItemConstruction(_learnedRecipe, mobUid);
        });
        await Pair.RunTicksSync(30);

        Assert.That(attempt!.IsCompleted, Is.True);
        Assert.That(await attempt, Is.True, "Once learned, the recipe must be craftable.");
    }

    /// <summary>
    ///     Reading the book through teaches its recipes; reading it again crumbles it.
    /// </summary>
    [Test]
    public async Task ReadingTheBookTeachesThenCrumbles()
    {
        var mobUid = await SpawnPlayerMobAsync();
        var bookUid = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            bookUid = SEntMan.SpawnEntity(_book, SEntMan.GetComponent<TransformComponent>(mobUid).Coordinates);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(mobUid, bookUid));
            SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(mobUid, bookUid);
        });

        // Two reads of 0.1 s each, with room to spare.
        var tickRate = (float)Server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)(1f * tickRate));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<KsLearnedRecipesSystem>().Knows(mobUid, _learnedRecipe), Is.True,
                "Reading the book through must teach its recipe.");
            Assert.That(SEntMan.GetComponent<KsRecipeGranterComponent>(bookUid).Uses, Is.EqualTo(0));

            SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(mobUid, bookUid, checkUseDelay: false);
        });
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.Deleted(bookUid), Is.True, "A used-up book must crumble when read again."));
    }
}
