using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.GameTicking.Simulation;
using Content.Server._KS14.StationEvents.Components;
using Content.Server.GameTicking.Presets;
using Content.Server.StationEvents.Components;
using Content.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.GameRules;

/// <summary>
///     Content tests of the game presets' event rates, run through <see cref="KsGameModeSimulationSystem"/>: each one states
///         a design limit and checks it over many simulated rounds, so that a retune that keeps to the design passes.
///         The last test checks the simulator itself.
/// </summary>
[TestOf(typeof(KsGameModeSimulationSystem))]
public sealed class KsGameModeSimulationTests : GameTest
{
    private const int Rounds = 2000;

    // Instance fields: the YAML linter validates static id fields, and game presets are a server-only kind it doesn't know.
    private readonly ProtoId<GamePresetPrototype> _skeletonCrewPreset = "KsSkeletonCrew";
    private readonly ProtoId<GamePresetPrototype> _traitorPreset = "Traitor";
    private static readonly ProtoId<WeightedRandomPrototype> SecretWeights = "Secret";
    private static readonly EntProtoId SkeletonCrewEventScheduler = "KsSkeletonCrewEventScheduler";
    private static readonly EntProtoId PowerGridCheckEvent = "PowerGridCheck";
    private static readonly EntProtoId RandomCritterHordeEvent = "RandomCritterHorde";
    private static readonly EntProtoId SpaceDust = "MeteorSpaceDust";

    private readonly EntProtoId _onceScheduler = "KsSimulationTestOnceScheduler";
    private readonly EntProtoId _onceEvent = "KsSimulationTestOnceEvent";
    private readonly EntProtoId _delayedEvent = "KsSimulationTestDelayedEvent";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseGameRule
  id: KsSimulationTestOnceEvent
  components:
  - type: StationEvent
    earliestStart: 0
    maxOccurrences: 1
    reoccurrenceDelay: 0

- type: entity
  parent: BaseGameRule
  id: KsSimulationTestDelayedEvent
  components:
  - type: StationEvent
    earliestStart: 0
    reoccurrenceDelay: 20

- type: entity
  parent: BaseGameRule
  id: KsSimulationTestOnceScheduler
  components:
  - type: BasicStationEventScheduler
    minimumTimeUntilFirstEvent: 10
    maximumSpanUntilFirstEvent: 0
    minMaxEventTiming:
      min: 10
      max: 10
    scheduledGameRules: !type:AllSelector
      children:
      - id: KsSimulationTestOnceEvent
      - id: KsSimulationTestDelayedEvent
";

    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     Skeleton Crew's main scheduler keeps a small crew busy: an event at least every four minutes on average, at
    ///         every population it is played at. Its filler events, which change little, don't count towards that.
    /// </summary>
    [Test]
    [TestCase(3)]
    [TestCase(12)]
    [TestCase(25)]
    public async Task SkeletonCrewIsBusy(int playerCount)
    {
        const double minimumEventsPerHour = 15;

        await Server.WaitAssertion(() =>
        {
            var result = Simulate(_skeletonCrewPreset, playerCount, roundEndMeanMinutes: 45f, roundEndStdDevMinutes: 10f);
            var mainEvents = result.OccurrencesBySource[SkeletonCrewEventScheduler];

            Assert.Multiple(() =>
            {
                Assert.That(result.CancelledRules, Is.Empty, $"Rules that can't run at {playerCount} players.");
                Assert.That(result.PerHour(SkeletonCrewEventScheduler), Is.GreaterThanOrEqualTo(minimumEventsPerHour),
                    $"The main scheduler runs too few events at {playerCount} players.");

                foreach (var ruleId in mainEvents.Keys)
                {
                    Assert.That(IsFiller(ruleId), Is.False,
                        $"{ruleId} is filler, and belongs on the fluff scheduler, not the main one.");
                }
            });
        });
    }

    /// <summary>
    ///     Nothing Skeleton Crew starts damages the station on a large scale: no meteor heavier than space dust, no
    ///         immovable rod, no nuke calibration, and no power grid check.
    /// </summary>
    [Test]
    [TestCase(3)]
    [TestCase(25)]
    public async Task SkeletonCrewDoesNotWreckTheStation(int playerCount)
    {
        await Server.WaitAssertion(() =>
        {
            var result = Simulate(_skeletonCrewPreset, playerCount, roundEndMeanMinutes: 45f, roundEndStdDevMinutes: 10f);

            Assert.Multiple(() =>
            {
                foreach (var ruleId in result.StartedRules())
                {
                    var rulePrototype = SProtoMan.Index<EntityPrototype>(ruleId);

                    Assert.That(rulePrototype.HasComp<ImmovableRodRuleComponent>(SEntMan.ComponentFactory), Is.False, ruleId);
                    Assert.That(rulePrototype.HasComp<NukeCalibrationRuleComponent>(SEntMan.ComponentFactory), Is.False, ruleId);
                    Assert.That(IsOrInherits(ruleId, PowerGridCheckEvent), Is.False, ruleId);

                    if (rulePrototype.TryComp<MeteorSwarmComponent>(out var meteorSwarmComponent, SEntMan.ComponentFactory))
                    {
                        foreach (var meteorId in meteorSwarmComponent.Meteors.Keys)
                        {
                            Assert.That(meteorId, Is.EqualTo(SpaceDust),
                                $"{ruleId} throws {meteorId}, which is heavier than space dust.");
                        }
                    }
                }
            });
        });
    }

    /// <summary>
    ///     The nuke calibration happens about once every few rounds on a populated station, and never on a small one.
    /// </summary>
    [Test]
    public async Task NukeCalibrationHappensEveryFewRounds()
    {
        await Server.WaitAssertion(() =>
        {
            var populated = Simulate(_traitorPreset, playerCount: 30, roundEndMeanMinutes: 60f, roundEndStdDevMinutes: 15f);
            var small = Simulate(_traitorPreset, playerCount: 15, roundEndMeanMinutes: 60f, roundEndStdDevMinutes: 15f);

            var populatedShare = NukeCalibrationShare(populated);
            Assert.Multiple(() =>
            {
                Assert.That(populatedShare, Is.InRange(0.2, 0.45),
                    "The nuke calibration should run in about one round in three at 30 players.");
                Assert.That(NukeCalibrationShare(small), Is.Zero, "The nuke calibration ran on a 15 player station.");
            });
        });
    }

    /// <summary>
    ///     The power grid check is admin-only: no preset that can be voted for, or picked by Secret, starts it.
    /// </summary>
    [Test]
    public async Task PowerGridCheckIsAdminOnly()
    {
        await Server.WaitAssertion(() =>
        {
            var presetIds = SProtoMan.EnumeratePrototypes<GamePresetPrototype>()
                .Where(preset => preset.ShowInVote)
                .Select(preset => preset.ID)
                .Concat(SProtoMan.Index(SecretWeights).Weights.Keys)
                .Distinct()
                .ToList();

            Assert.That(presetIds, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var presetId in presetIds)
                {
                    var preset = SProtoMan.Index<GamePresetPrototype>(presetId);
                    var playerCount = preset.MaxPlayers ?? 40;
                    var result = SEntMan.System<KsGameModeSimulationSystem>()
                        .SimulatePreset(preset, rounds: 300, playerCount, roundEndMeanMinutes: 60f, roundEndStdDevMinutes: 15f);

                    foreach (var ruleId in result.StartedRules())
                    {
                        Assert.That(IsOrInherits(ruleId, PowerGridCheckEvent), Is.False,
                            $"{presetId} started {ruleId}, a power grid check.");
                    }
                }
            });
        });
    }

    /// <summary>
    ///     Each simulated round keeps its own history: an event with <see cref="StationEventComponent.MaxOccurrences"/>
    ///         of 1 runs at most once a round, but again in the next, and one with a
    ///         <see cref="StationEventComponent.ReoccurrenceDelay"/> waits that long between runs.
    ///         <c>stationevent simulate</c> respects neither.
    /// </summary>
    [Test]
    public async Task SimulatedRoundsKeepTheirOwnHistory()
    {
        const int rounds = 200;
        const float roundMinutes = 60f;
        const int reoccurrenceDelayMinutes = 20;

        await Server.WaitAssertion(() =>
        {
            var result = SEntMan.System<KsGameModeSimulationSystem>()
                .SimulateRules([_onceScheduler], rounds, playerCount: 1, roundMinutes, roundEndStdDevMinutes: 0f);
            var occurrences = result.OccurrencesBySource[_onceScheduler];

            // The first run is at the first event, 10 seconds in, and each later one at least 20 minutes after the last:
            //      three fit in 60 minutes, where an unchecked scheduler would run it on every one of its 360 events.
            var maximumDelayedRuns = (int)((roundMinutes - 1f) / reoccurrenceDelayMinutes) + 1;

            Assert.Multiple(() =>
            {
                Assert.That(result.Rounds, Is.EqualTo(rounds));
                Assert.That(occurrences[_onceEvent], Is.EqualTo(rounds), "The once-a-round event should run once in each round.");
                Assert.That(result.RoundShare(_onceEvent), Is.EqualTo(1d));
                Assert.That(occurrences[_delayedEvent], Is.LessThanOrEqualTo(rounds * maximumDelayedRuns),
                    "The delayed event ran more often than its reoccurrence delay allows.");
                Assert.That(occurrences[_delayedEvent], Is.GreaterThan(rounds),
                    "The delayed event should run more than once a round.");
            });
        });
    }

    private KsGameModeSimulationResult Simulate(
        ProtoId<GamePresetPrototype> presetId,
        int playerCount,
        float roundEndMeanMinutes,
        float roundEndStdDevMinutes)
    {
        return SEntMan.System<KsGameModeSimulationSystem>()
            .SimulatePreset(SProtoMan.Index(presetId), Rounds, playerCount, roundEndMeanMinutes, roundEndStdDevMinutes);
    }

    private double NukeCalibrationShare(KsGameModeSimulationResult result)
    {
        return result.StartedRules()
            .Where(ruleId => SProtoMan.Index<EntityPrototype>(ruleId)
                .HasComp<NukeCalibrationRuleComponent>(SEntMan.ComponentFactory))
            .Select(result.RoundShare)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>
    ///     Mass hallucinations, cargo gifts, or the horde of snails and roaches: events that change little on their own.
    /// </summary>
    private bool IsFiller(string ruleId)
    {
        var rulePrototype = SProtoMan.Index<EntityPrototype>(ruleId);
        return rulePrototype.HasComp<MassHallucinationsRuleComponent>(SEntMan.ComponentFactory)
            || rulePrototype.HasComp<CargoGiftsRuleComponent>(SEntMan.ComponentFactory)
            || IsOrInherits(ruleId, RandomCritterHordeEvent);
    }

    private bool IsOrInherits(string ruleId, EntProtoId ancestorId)
    {
        return SProtoMan.EnumerateParents<EntityPrototype>(ruleId, includeSelf: true)
            .Any(prototype => prototype.ID == ancestorId);
    }
}
