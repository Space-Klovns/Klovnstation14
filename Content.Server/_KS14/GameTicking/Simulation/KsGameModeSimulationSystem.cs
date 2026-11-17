using System.Linq;
using Content.Server.GameTicking.Presets;
using Content.Server.StationEvents;
using Content.Server.StationEvents.Components;
using Content.Shared._KS14.EntityTable;
using Content.Shared.Destructible.Thresholds;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.Conditions;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking.Rules;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.GameTicking.Simulation;

/// <summary>
///     Simulates many rounds of a game preset without running them, and counts which rules its event schedulers and
///         dynamic rules would start. It extends <see cref="StationEventCommand"/>'s <c>simulate</c>, which handles a
///         single <see cref="BasicStationEventSchedulerComponent"/>: here each simulated round keeps its own history, so
///         <see cref="StationEventComponent.MaxOccurrences"/>, <see cref="StationEventComponent.ReoccurrenceDelay"/> and
///         a still-running event are respected, the first event waits for the scheduler's opening delay, and a
///         <see cref="DynamicRuleComponent"/> spends its budget as <see cref="Content.Server.GameTicking.Rules.DynamicRuleSystem"/> does.
/// </summary>
/// <remarks>
///     Round end is not simulated (a simulated round just stops), so <see cref="StationEventComponent.OccursDuringRoundEnd"/>
///         is ignored. Rules of any other kind in the preset (antag rules, ramping schedulers, sub-gamemodes) are not
///         simulated and are listed in <see cref="KsGameModeSimulationResult.UnsimulatedRules"/>.
/// </remarks>
public sealed partial class KsGameModeSimulationSystem : EntitySystem
{
    [Dependency] private EventManagerSystem _eventManagerSystem = default!;
    [Dependency] private EntityTableSystem _entityTableSystem = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <inheritdoc cref="SimulateRules"/>
    public KsGameModeSimulationResult SimulatePreset(
        GamePresetPrototype preset,
        int rounds,
        int playerCount,
        float roundEndMeanMinutes,
        float roundEndStdDevMinutes)
    {
        return SimulateRules(preset.Rules, rounds, playerCount, roundEndMeanMinutes, roundEndStdDevMinutes);
    }

    /// <summary>
    ///     Simulates <paramref name="rounds"/> rounds that run <paramref name="ruleIds"/> at <paramref name="playerCount"/>
    ///         players, each lasting a normally distributed number of minutes.
    /// </summary>
    public KsGameModeSimulationResult SimulateRules(
        IReadOnlyList<EntProtoId> ruleIds,
        int rounds,
        int playerCount,
        float roundEndMeanMinutes,
        float roundEndStdDevMinutes)
    {
        var result = new KsGameModeSimulationResult();
        var sourcePrototypes = new List<EntityPrototype>();

        foreach (var ruleId in ruleIds)
        {
            var rulePrototype = ProtoMan.Index(ruleId);
            var isScheduler = rulePrototype.HasComp<BasicStationEventSchedulerComponent>(Factory);
            var isDynamic = rulePrototype.HasComp<DynamicRuleComponent>(Factory);
            if (!isScheduler && !isDynamic)
            {
                result.UnsimulatedRules.Add(ruleId);
                continue;
            }

            // A rule that needs more players than this cancels itself at round start.
            if (rulePrototype.TryComp<GameRuleComponent>(out var gameRuleComponent, Factory)
                && playerCount < gameRuleComponent.MinPlayers)
            {
                result.CancelledRules.Add(ruleId);
                continue;
            }

            sourcePrototypes.Add(rulePrototype);
            result.OccurrencesBySource[ruleId] = new Dictionary<string, int>();
        }

        var round = new SimulatedRound();
        var ranThisRound = new HashSet<string>();
        for (var i = 0; i < rounds; i++)
        {
            var roundLength = TimeSpan.FromMinutes(_random.NextGaussian(roundEndMeanMinutes, roundEndStdDevMinutes));
            if (roundLength <= TimeSpan.Zero)
                continue;

            result.Rounds++;
            result.SimulatedTime += roundLength;
            round.Reset();
            ranThisRound.Clear();

            var sources = sourcePrototypes.Select(CreateSource).ToList();
            while (true)
            {
                var source = sources.MinBy(s => s.NextTime);
                if (source == null || source.NextTime > roundLength)
                    break;

                var currentTime = source.NextTime;
                foreach (var startedRuleId in source.Fire(this, round, currentTime, playerCount))
                {
                    result.Record(source.RuleId, startedRuleId);
                    ranThisRound.Add(startedRuleId);
                }
            }

            foreach (var ruleId in ranThisRound)
            {
                result.RoundsWithOccurrence.TryGetValue(ruleId, out var count);
                result.RoundsWithOccurrence[ruleId] = count + 1;
            }
        }

        return result;
    }

    private SimulatedSource CreateSource(EntityPrototype rulePrototype)
    {
        if (rulePrototype.TryComp<BasicStationEventSchedulerComponent>(out var schedulerComponent, Factory))
        {
            // As BasicStationEventSchedulerSystem.Started.
            var firstEvent = _random.NextFloat(schedulerComponent.MinimumTimeUntilFirstEvent,
                schedulerComponent.MinimumTimeUntilFirstEvent + schedulerComponent.MaximumSpanUntilFirstEvent);

            return new SimulatedScheduler(rulePrototype.ID, schedulerComponent, TimeSpan.FromSeconds(firstEvent));
        }

        rulePrototype.TryComp<DynamicRuleComponent>(out var dynamicRuleComponent, Factory);

        // As DynamicRuleSystem.Added and Started: it executes the moment the round starts.
        var budget = (float)_random.Next(dynamicRuleComponent!.StartingBudgetMin, dynamicRuleComponent.StartingBudgetMax);
        return new SimulatedDynamicRule(rulePrototype.ID, dynamicRuleComponent, budget);
    }

    /// <summary>
    ///     Records that <paramref name="ruleId"/> was added at <paramref name="addedTime"/>, as
    ///         <see cref="Content.Server.GameTicking.GameTicker.AddGameRule"/> would: it counts as run once its delay is
    ///         over, and stays active for its duration.
    /// </summary>
    private void StartRule(SimulatedRound round, EntProtoId ruleId, TimeSpan addedTime)
    {
        var rulePrototype = ProtoMan.Index(ruleId);

        var startTime = addedTime;
        if (rulePrototype.TryComp<GameRuleComponent>(out var gameRuleComponent, Factory)
            && gameRuleComponent.Delay is { } delay)
        {
            startTime += TimeSpan.FromSeconds(delay.Next(_random));
        }

        round.PreviousRules.Add((startTime, ruleId));

        if (!rulePrototype.TryComp<StationEventComponent>(out var stationEventComponent, Factory))
            return;

        // As StationEventSystem.Added: a null duration lasts until something ends it, which here is the round.
        TimeSpan endTime;
        if (stationEventComponent.Duration is not { } duration)
            endTime = TimeSpan.MaxValue;
        else if (stationEventComponent.MaxDuration is { } maxDuration)
            endTime = startTime + _random.Next(duration, maxDuration);
        else
            endTime = startTime + duration;

        round.ActiveUntil[ruleId] = endTime;
    }

    private sealed class SimulatedRound
    {
        public readonly List<(TimeSpan Time, string RuleId)> PreviousRules = new();
        public readonly Dictionary<string, TimeSpan> ActiveUntil = new();

        public void Reset()
        {
            PreviousRules.Clear();
            ActiveUntil.Clear();
        }
    }

    private abstract class SimulatedSource(string ruleId)
    {
        public readonly string RuleId = ruleId;
        public TimeSpan NextTime;

        /// <summary>
        ///     Runs this source at <paramref name="currentTime"/>, moves <see cref="NextTime"/> on, and returns the rules it started.
        /// </summary>
        public abstract List<EntProtoId> Fire(
            KsGameModeSimulationSystem system,
            SimulatedRound round,
            TimeSpan currentTime,
            int playerCount);
    }

    /// <summary>
    ///     As <see cref="BasicStationEventSchedulerSystem.Update"/>.
    /// </summary>
    private sealed class SimulatedScheduler : SimulatedSource
    {
        private readonly BasicStationEventSchedulerComponent _schedulerComponent;

        // A copy, as StationEventCommand.Simulate takes one: the component's own may only be used by its system.
        private readonly MinMax _eventTiming;

        public SimulatedScheduler(string ruleId, BasicStationEventSchedulerComponent schedulerComponent, TimeSpan firstEvent)
            : base(ruleId)
        {
            _schedulerComponent = schedulerComponent;
            _eventTiming = schedulerComponent.MinMaxEventTiming;
            NextTime = firstEvent;
        }

        public override List<EntProtoId> Fire(
            KsGameModeSimulationSystem system,
            SimulatedRound round,
            TimeSpan currentTime,
            int playerCount)
        {
            NextTime = currentTime + TimeSpan.FromSeconds(_eventTiming.Next(system._random));

            var limitedEvents = system._eventManagerSystem.KsBuildSimulatedEvents(_schedulerComponent.ScheduledGameRules,
                currentTime,
                playerCount,
                round.PreviousRules,
                round.ActiveUntil);

            // FindEvent warns when there is nothing to pick, which in a simulation is an ordinary outcome.
            if (limitedEvents.Count == 0 || system._eventManagerSystem.FindEvent(limitedEvents) is not { } eventId)
                return [];

            system.StartRule(round, eventId, currentTime);
            return new List<EntProtoId> { eventId };
        }
    }

    /// <summary>
    ///     As <see cref="Content.Server.GameTicking.Rules.DynamicRuleSystem"/>'s <c>Execute</c>.
    /// </summary>
    private sealed class SimulatedDynamicRule : SimulatedSource
    {
        private readonly DynamicRuleComponent _dynamicRuleComponent;
        private float _budget;
        private TimeSpan _lastBudgetUpdate = TimeSpan.Zero;

        public SimulatedDynamicRule(string ruleId, DynamicRuleComponent dynamicRuleComponent, float budget)
            : base(ruleId)
        {
            _dynamicRuleComponent = dynamicRuleComponent;
            _budget = budget;
            NextTime = TimeSpan.Zero;
        }

        public override List<EntProtoId> Fire(
            KsGameModeSimulationSystem system,
            SimulatedRound round,
            TimeSpan currentTime,
            int playerCount)
        {
            NextTime = currentTime + system._random.Next(_dynamicRuleComponent.MinRuleInterval, _dynamicRuleComponent.MaxRuleInterval);

            _budget += (float)(currentTime - _lastBudgetUpdate).TotalSeconds * _dynamicRuleComponent.BudgetPerSecond;
            _lastBudgetUpdate = currentTime;

            var context = new EntityTableContext(new Dictionary<string, object>
            {
                { HasBudgetCondition.BudgetContextKey, _budget },
                { KsSimulationContextKeys.PlayerCount, playerCount },
                { KsSimulationContextKeys.RoundDuration, currentTime },
                { KsSimulationContextKeys.PreviousGameRules, (IReadOnlyList<(TimeSpan, string)>)round.PreviousRules },
            });

            // Picked before any of them start, as the real rule spends its budget only after the whole table has rolled.
            var startedRules = system._entityTableSystem.GetSpawns(_dynamicRuleComponent.Table, ctx: context).ToList();
            foreach (var ruleId in startedRules)
            {
                system.StartRule(round, ruleId, currentTime);

                if (system.ProtoMan.Index(ruleId).TryComp<DynamicRuleCostComponent>(out var costComponent, system.Factory))
                    _budget -= costComponent.Cost;
            }

            return startedRules;
        }
    }
}

/// <summary>
///     What <see cref="KsGameModeSimulationSystem"/> counted over its simulated rounds.
/// </summary>
public sealed class KsGameModeSimulationResult
{
    /// <summary>
    ///     How many rounds were simulated. A drawn round length of zero or less is not a round.
    /// </summary>
    public int Rounds;

    /// <summary>
    ///     The combined length of every simulated round.
    /// </summary>
    public TimeSpan SimulatedTime;

    /// <summary>
    ///     For each scheduler or dynamic rule in the preset, how often it started each rule.
    /// </summary>
    public readonly Dictionary<string, Dictionary<string, int>> OccurrencesBySource = new();

    /// <summary>
    ///     For each rule started, how many simulated rounds it started in at least once.
    /// </summary>
    public readonly Dictionary<string, int> RoundsWithOccurrence = new();

    /// <summary>
    ///     Rules in the preset that are neither a basic event scheduler nor a dynamic rule, and so are not simulated.
    /// </summary>
    public readonly List<string> UnsimulatedRules = new();

    /// <summary>
    ///     Schedulers and dynamic rules that need more players than were simulated, and so would cancel themselves.
    /// </summary>
    public readonly List<string> CancelledRules = new();

    /// <summary>
    ///     How many times <paramref name="ruleId"/> started, from every source.
    /// </summary>
    public int Occurrences(string ruleId)
    {
        var total = 0;
        foreach (var occurrences in OccurrencesBySource.Values)
        {
            if (occurrences.TryGetValue(ruleId, out var count))
                total += count;
        }

        return total;
    }

    /// <summary>
    ///     The share of simulated rounds in which <paramref name="ruleId"/> started at least once.
    /// </summary>
    public double RoundShare(string ruleId)
    {
        if (Rounds == 0)
            return 0;

        return RoundsWithOccurrence.TryGetValue(ruleId, out var count) ? (double)count / Rounds : 0;
    }

    /// <summary>
    ///     How many rules <paramref name="sourceRuleId"/> started per hour of simulated round.
    /// </summary>
    public double PerHour(string sourceRuleId)
    {
        if (SimulatedTime <= TimeSpan.Zero || !OccurrencesBySource.TryGetValue(sourceRuleId, out var occurrences))
            return 0;

        return (double)occurrences.Values.Sum() / SimulatedTime.TotalHours;
    }

    /// <summary>
    ///     Every rule started by any source.
    /// </summary>
    public IEnumerable<string> StartedRules()
    {
        return OccurrencesBySource.Values.SelectMany(occurrences => occurrences.Keys).Distinct();
    }

    internal void Record(string sourceRuleId, string startedRuleId)
    {
        var occurrences = OccurrencesBySource[sourceRuleId];
        occurrences.TryGetValue(startedRuleId, out var count);
        occurrences[startedRuleId] = count + 1;
    }
}
