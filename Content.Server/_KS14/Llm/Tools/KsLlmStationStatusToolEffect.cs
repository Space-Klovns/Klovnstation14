using System.Text;
using Content.Server.AlertLevel;
using Content.Server.GameTicking;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Read-only: reports the sender station's name, round time, alert level, crew and evacuation state.
/// </summary>
public sealed partial class KsLlmStationStatusToolEffect : KsLlmToolEffect
{
    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        var entityManager = context.EntityManager;
        if (context.StationUid is not { } stationUid || !entityManager.EntityExists(stationUid))
            return KsLlmToolOutcome.Error("the sending fax does not belong to any station.");

        var stationSystem = entityManager.System<StationSystem>();
        var mobStateSystem = entityManager.System<MobStateSystem>();

        var aliveCrew = 0;
        var deadCrew = 0;
        var actorQuery = entityManager.EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (actorQuery.MoveNext(out var actorUid, out _, out var mobStateComponent, out var transformComponent))
        {
            if (stationSystem.GetOwningStation(actorUid, transformComponent) != stationUid)
                continue;

            if (mobStateSystem.IsDead(actorUid, mobStateComponent))
                deadCrew++;
            else
                aliveCrew++;
        }

        var roundDuration = entityManager.System<GameTicker>().RoundDuration();
        var alertLevel = entityManager.System<AlertLevelSystem>().GetLevel(stationUid);

        var builder = new StringBuilder();
        builder.AppendLine($"Station: {entityManager.GetComponent<MetaDataComponent>(stationUid).EntityName}");
        builder.AppendLine($"Shift time elapsed: {(int)roundDuration.TotalHours:D2}:{roundDuration.Minutes:D2}");
        builder.AppendLine($"Alert level: {(string.IsNullOrEmpty(alertLevel) ? "none" : alertLevel)}");
        builder.AppendLine($"Crew aboard (connected players): {aliveCrew} alive, {deadCrew} dead");
        builder.Append($"Evacuation: {DescribeEvacuation(entityManager)}");

        return KsLlmToolOutcome.Ok(builder.ToString());
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
