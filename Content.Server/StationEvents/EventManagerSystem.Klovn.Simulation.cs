using Content.Server.StationEvents.Components;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents;

/// <summary>
///     The parts of event selection that a simulated round needs to run without <see cref="Content.Server.GameTicking.GameTicker"/>: the same checks
///         as <see cref="CanRun"/>, read from the simulated round's own history.
/// </summary>
public sealed partial class EventManagerSystem
{
    /// <summary>
    ///     The events in <paramref name="limitedEventsTable"/> that could run at <paramref name="currentTime"/> in a
    ///         simulated round, as <see cref="TryBuildLimitedEvents(EntityTableSelector, out Dictionary{EntityPrototype, StationEventComponent}, TimeSpan?, int?)"/>
    ///         builds them for a real one.
    /// </summary>
    /// <param name="previousRules">Every rule the simulated round has added so far, with the time it was added.</param>
    /// <param name="activeUntil">When each rule the simulated round has added stops being active.</param>
    public Dictionary<EntityPrototype, StationEventComponent> KsBuildSimulatedEvents(
        EntityTableSelector limitedEventsTable,
        TimeSpan currentTime,
        int playerCount,
        IReadOnlyList<(TimeSpan Time, string RuleId)> previousRules,
        IReadOnlyDictionary<string, TimeSpan> activeUntil)
    {
        var limitedEvents = new Dictionary<EntityPrototype, StationEventComponent>();

        foreach (var eventId in _entityTable.GetSpawns(limitedEventsTable))
        {
            if (GameTicker.IsIgnored(eventId))
                continue;

            if (!ProtoMan.Resolve(eventId, out var eventPrototype) || eventPrototype.Abstract)
                continue;

            if (limitedEvents.ContainsKey(eventPrototype))
                continue;

            if (!eventPrototype.TryComp<StationEventComponent>(out var stationEvent, EntityManager.ComponentFactory))
                continue;

            if (!KsCanRunSimulated(eventPrototype, stationEvent, playerCount, currentTime, previousRules, activeUntil))
                continue;

            limitedEvents.Add(eventPrototype, stationEvent);
        }

        return limitedEvents;
    }

    /// <summary>
    ///     <see cref="CanRun"/>, against a simulated round's history rather than <see cref="Content.Server.GameTicking.GameTicker"/>'s.
    ///         Round end is not simulated: a simulated round simply stops.
    /// </summary>
    public static bool KsCanRunSimulated(
        EntityPrototype prototype,
        StationEventComponent stationEvent,
        int playerCount,
        TimeSpan currentTime,
        IReadOnlyList<(TimeSpan Time, string RuleId)> previousRules,
        IReadOnlyDictionary<string, TimeSpan> activeUntil)
    {
        if (activeUntil.TryGetValue(prototype.ID, out var endTime) && currentTime < endTime)
            return false;

        var occurrences = 0;
        var lastRun = TimeSpan.Zero;
        foreach (var (time, ruleId) in previousRules)
        {
            if (ruleId != prototype.ID)
                continue;

            occurrences++;
            lastRun = time;
        }

        if (stationEvent.MaxOccurrences is { } maxOccurrences && occurrences >= maxOccurrences)
            return false;

        if (playerCount < stationEvent.MinimumPlayers)
            return false;

        if (currentTime != TimeSpan.Zero && currentTime.TotalMinutes < stationEvent.EarliestStart)
            return false;

        if (lastRun != TimeSpan.Zero && currentTime.TotalMinutes < stationEvent.ReoccurrenceDelay + lastRun.TotalMinutes)
            return false;

        return true;
    }
}
