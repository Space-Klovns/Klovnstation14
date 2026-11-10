using System.Text;
using Content.Server.AlertLevel;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.GameTicking;
using Content.Server.Medical.CrewMonitoring;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Read-only: reports the sender station's name, round time, alert level, crew vitals and evacuation state.
/// </summary>
/// <remarks>
///     Crew figures come from the station's crew monitoring server - the same suit sensor data its crew
///         monitoring consoles show - not from anything Central Command could not plausibly know. Sensors that
///         are off, or not reaching a working server, simply are not counted.
/// </remarks>
public sealed partial class KsLlmStationStatusToolEffect : KsLlmToolEffect
{
    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        var entityManager = context.EntityManager;
        if (context.StationUid is not { } stationUid || !entityManager.EntityExists(stationUid))
            return KsLlmToolOutcome.Error("the sending fax does not belong to any station.");

        var roundDuration = entityManager.System<GameTicker>().RoundDuration();
        var alertLevel = entityManager.System<AlertLevelSystem>().GetLevel(stationUid);

        var builder = new StringBuilder();
        builder.AppendLine($"Station: {entityManager.GetComponent<MetaDataComponent>(stationUid).EntityName}");
        builder.AppendLine($"Shift time elapsed: {(int)roundDuration.TotalHours:D2}:{roundDuration.Minutes:D2}");
        builder.AppendLine($"Alert level: {(string.IsNullOrEmpty(alertLevel) ? "none" : alertLevel)}");
        builder.AppendLine($"Crew monitoring: {DescribeCrewMonitoring(entityManager, stationUid)}");
        builder.Append($"Evacuation: {DescribeEvacuation(entityManager)}");

        return KsLlmToolOutcome.Ok(builder.ToString());
    }

    private static string DescribeCrewMonitoring(IEntityManager entityManager, EntityUid stationUid)
    {
        var stationSystem = entityManager.System<StationSystem>();
        var singletonServerSystem = entityManager.System<SingletonDeviceNetServerSystem>();

        // Read-only on purpose: SingletonDeviceNetServerSystem's own lookup connects servers as a side effect.
        var serverQuery = entityManager.EntityQueryEnumerator<CrewMonitoringServerComponent, SingletonDeviceNetServerComponent>();
        while (serverQuery.MoveNext(out var serverUid, out var crewMonitoringServerComponent, out var singletonServerComponent))
        {
            if (!singletonServerComponent.Available
                || !singletonServerSystem.IsActiveServer(serverUid, singletonServerComponent)
                || stationSystem.GetOwningStation(serverUid) != stationUid)
                continue;

            var alive = 0;
            var critical = 0;
            var dead = 0;
            foreach (var sensorStatus in crewMonitoringServerComponent.SensorStatus.Values)
            {
                if (!sensorStatus.IsAlive)
                    dead++;
                else if (sensorStatus.DamagePercentage >= 1f)
                    critical++;
                else
                    alive++;
            }

            var reporting = crewMonitoringServerComponent.SensorStatus.Count;
            return reporting == 0
                ? "online, but no suit sensors are reporting"
                : $"{reporting} suit sensors reporting: {alive} alive, {critical} in critical condition, {dead} dead. Crew with sensors off are not counted.";
        }

        return "no data - the station's crew monitoring server is offline or missing";
    }

    private static string DescribeEvacuation(IEntityManager entityManager)
    {
        if (entityManager.System<EmergencyShuttleSystem>().EmergencyShuttleArrived)
            return "the emergency shuttle is docked at the station";

        var roundEndSystem = entityManager.System<RoundEndSystem>();
        if (!roundEndSystem.IsRoundEndRequested())
            return "not called";

        return roundEndSystem.ShuttleTimeLeft is { } timeLeft && timeLeft > TimeSpan.Zero
            ? $"emergency shuttle called, arriving in {(int)timeLeft.TotalMinutes} minutes"
            : "emergency shuttle called";
    }
}
