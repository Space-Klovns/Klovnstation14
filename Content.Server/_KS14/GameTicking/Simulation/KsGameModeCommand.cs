using System.Linq;
using Content.Server.Administration;
using Content.Server.GameTicking.Presets;
using Content.Shared.Administration;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;

namespace Content.Server._KS14.GameTicking.Simulation;

/// <summary>
///     <c>ksgamemode simulate Survival 1000 30 60 15</c>: simulates a preset's event schedulers and dynamic rules over
///         many rounds. See <see cref="KsGameModeSimulationSystem"/>. Like <c>stationevent simulate</c>, it is best run
///         on a local server, though it reads nothing from the current round.
/// </summary>
[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed partial class KsGameModeCommand : ToolshedCommand
{
    private KsGameModeSimulationSystem? _simulationSystem;

    [CommandImplementation("simulate")]
    public IEnumerable<string> Simulate(
        [CommandArgument] ProtoId<GamePresetPrototype> presetId,
        [CommandArgument] int rounds,
        [CommandArgument] int playerCount,
        [CommandArgument] float roundEndMeanMinutes,
        [CommandArgument] float roundEndStdDevMinutes)
    {
        _simulationSystem ??= GetSys<KsGameModeSimulationSystem>();

        var preset = IoCManager.Resolve<IPrototypeManager>().Index(presetId);
        var result = _simulationSystem.SimulatePreset(preset, rounds, playerCount, roundEndMeanMinutes, roundEndStdDevMinutes);

        yield return $"{result.Rounds} rounds, {result.SimulatedTime.TotalHours:F1} hours, {playerCount} players";

        foreach (var (sourceId, occurrences) in result.OccurrencesBySource)
        {
            yield return $"{sourceId}: {result.PerHour(sourceId):F2} per hour";

            foreach (var (ruleId, count) in occurrences.OrderByDescending(pair => pair.Value))
            {
                yield return $"    {ruleId}: {(double)count / result.Rounds:F3} per round, in {result.RoundShare(ruleId):P1} of rounds";
            }
        }

        if (result.CancelledRules.Count > 0)
            yield return $"Too few players for: {string.Join(", ", result.CancelledRules)}";

        if (result.UnsimulatedRules.Count > 0)
            yield return $"Not simulated: {string.Join(", ", result.UnsimulatedRules)}";
    }
}
