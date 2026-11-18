#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Shared._KS14.Fluids.Evaporation;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using ServerPuddleSystem = Content.Server.Fluids.EntitySystems.PuddleSystem;
using ClientPuddleSystem = Content.Client.Fluids.PuddleSystem;

namespace Content.IntegrationTests.Tests._KS14.Fluids;

/// <summary>
///     While a minimum-evaporation rule is active, its reagents evaporate at least at its rate: puddles already lying
///         there start, they stop when it ends or goes away with the round, the highest of two rules wins, and the
///         client, which predicts evaporation, agrees.
/// </summary>
[TestFixture]
[TestOf(typeof(KsMinimumEvaporationSystem))]
public sealed class KsMinimumEvaporationRuleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestMinEvapStill
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  color: '#800000'

- type: reagent
  id: KsTestMinEvapFast
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  color: '#000080'
  evaporationSpeed: 3

- type: entity
  parent: BaseGameRule
  id: KsTestMinEvapRuleLow
  components:
  - type: KsMinimumEvaporationRule
    minimumRates:
      KsTestMinEvapStill: 0.5
      KsTestMinEvapFast: 1

- type: entity
  parent: BaseGameRule
  id: KsTestMinEvapRuleHigh
  components:
  - type: KsMinimumEvaporationRule
    minimumRates:
      KsTestMinEvapStill: 2
";

    // Instance fields: these exist only in the test prototypes above, and the YAML linter checks only static ones.
    private readonly ProtoId<ReagentPrototype> _stillReagent = "KsTestMinEvapStill";
    private readonly ProtoId<ReagentPrototype> _fastReagent = "KsTestMinEvapFast";
    private readonly EntProtoId _lowRule = "KsTestMinEvapRuleLow";
    private readonly EntProtoId _highRule = "KsTestMinEvapRuleHigh";

    private const float LowRate = 0.5f;
    private const float HighRate = 2f;
    private const float FastOwnRate = 3f;
    private const float SpillVolume = 20f;

    private int Ticks(float seconds)
        => (int)(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate);

    private EntityUid Spill(TileRef tile)
    {
        var spilled = new Solution();
        spilled.AddReagent(_stillReagent, FixedPoint2.New(SpillVolume));
        Assert.That(Pair.Server.System<ServerPuddleSystem>().TrySpillAt(tile, spilled, out var puddleUid, sound: false, tileReact: false));
        return puddleUid;
    }

    private Solution PuddleSolution(IEntityManager entityManager, EntityUid puddleUid)
    {
        var puddleComponent = entityManager.GetComponent<PuddleComponent>(puddleUid);
        Assert.That(entityManager.System<SharedSolutionContainerSystem>()
            .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution));
        return solution!;
    }

    private float Volume(EntityUid puddleUid)
        => (float)PuddleSolution(Pair.Server.EntMan, puddleUid).Volume;

    private float ServerRate(ProtoId<ReagentPrototype> reagent)
        => (float)Pair.Server.System<ServerPuddleSystem>().KsGetEvaporationSpeed(Pair.Server.ProtoMan.Index(reagent));

    [Test]
    public async Task ExistingPuddleEvaporatesOnlyWhileTheRuleRuns()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var gameTicker = server.System<GameTicker>();

        var puddleUid = EntityUid.Invalid;
        await server.WaitPost(() => puddleUid = Spill(testMap.Tile));
        await Pair.RunTicksSync(Ticks(3f));

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<EvaporationComponent>(puddleUid), Is.False, "The test reagent evaporates without a rule, so this proves nothing.");
            Assert.That(Volume(puddleUid), Is.EqualTo(SpillVolume), "The test reagent went somewhere without a rule, so this proves nothing.");
        });

        var ruleUid = EntityUid.Invalid;
        await server.WaitPost(() => Assert.That(gameTicker.StartGameRule(_lowRule, out ruleUid)));
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.HasComponent<EvaporationComponent>(puddleUid), Is.True, "A puddle already lying there did not start evaporating when the rule started."));

        await Pair.RunTicksSync(Ticks(4f));
        var volumeAtEnd = 0f;
        await server.WaitAssertion(() =>
        {
            volumeAtEnd = Volume(puddleUid);
            // Four seconds at the rule's rate, give or take an update either side.
            Assert.That(volumeAtEnd, Is.EqualTo(SpillVolume - 4f * LowRate).Within(LowRate * 1.01f), "The puddle did not evaporate at the rule's rate.");
            Assert.That(gameTicker.EndGameRule(ruleUid));
            Assert.That(server.EntMan.HasComponent<EvaporationComponent>(puddleUid), Is.False, "The puddle kept evaporating after the rule ended.");
        });

        await Pair.RunTicksSync(Ticks(3f));
        await server.WaitAssertion(() =>
            Assert.That(Volume(puddleUid), Is.EqualTo(volumeAtEnd), "The puddle lost volume after the rule ended."));
    }

    [Test]
    public async Task HighestRateWinsAndOwnFasterSpeedStays()
    {
        var server = Pair.Server;
        var gameTicker = server.System<GameTicker>();

        await server.WaitAssertion(() =>
            Assert.That(ServerRate(_stillReagent), Is.Zero, "The test reagent evaporates without a rule, so this proves nothing."));

        var lowRuleUid = EntityUid.Invalid;
        var highRuleUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            Assert.That(gameTicker.StartGameRule(_lowRule, out lowRuleUid));
            Assert.That(gameTicker.StartGameRule(_highRule, out highRuleUid));
        });

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServerRate(_stillReagent), Is.EqualTo(HighRate), "Two rules did not give the higher of their rates.");
                Assert.That(ServerRate(_fastReagent), Is.EqualTo(FastOwnRate), "A minimum slowed a reagent that is already faster.");
            }

            Assert.That(gameTicker.EndGameRule(highRuleUid));
            Assert.That(ServerRate(_stillReagent), Is.EqualTo(LowRate), "Ending the higher rule did not fall back to the other one.");

            // Round restart deletes rule entities without ending them.
            server.EntMan.DeleteEntity(lowRuleUid);
            Assert.That(ServerRate(_stillReagent), Is.Zero, "A rule deleted without ending left its rate behind.");
        });
    }

    [Test]
    public async Task ClientAgreesWithTheServer()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        var gameTicker = server.System<GameTicker>();

        var puddleUid = EntityUid.Invalid;
        var ruleUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            puddleUid = Spill(testMap.Tile);
            Assert.That(gameTicker.StartGameRule(_lowRule, out ruleUid));
        });
        await Pair.RunTicksSync(5);

        var clientPuddleUid = Pair.ToClientUid(puddleUid);
        await client.WaitAssertion(() =>
        {
            var clientPuddleSystem = client.System<ClientPuddleSystem>();
            var speeds = clientPuddleSystem.GetEvaporationSpeeds(PuddleSolution(client.EntMan, clientPuddleUid));
            Assert.That(speeds.TryGetValue(_stillReagent, out var clientRate), Is.True, "The client does not know the rule's rate, so it cannot predict the evaporation.");
            Assert.That((float)clientRate, Is.EqualTo(LowRate));
            Assert.That(clientPuddleSystem.CanFullyEvaporate(PuddleSolution(client.EntMan, clientPuddleUid)), Is.True);
        });

        await server.WaitPost(() => Assert.That(gameTicker.EndGameRule(ruleUid)));
        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientPuddleSystem>().GetEvaporationSpeeds(PuddleSolution(client.EntMan, clientPuddleUid)), Is.Empty,
                "The client kept the rule's rate after it ended."));
    }
}
