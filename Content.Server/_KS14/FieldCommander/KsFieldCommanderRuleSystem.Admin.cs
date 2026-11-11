using System.Text;
using Content.Shared.GameTicking.Components;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Admin

    /// <summary>
    ///     Every field commander running now, for the admin commands.
    /// </summary>
    public List<Entity<KsFieldCommanderRuleComponent, GameRuleComponent>> GetActiveRules()
    {
        var rules = new List<Entity<KsFieldCommanderRuleComponent, GameRuleComponent>>();

        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out var gameRuleComponent))
            rules.Add((ruleUid, ruleComponent, gameRuleComponent));

        return rules;
    }

    /// <summary>
    ///     Finds a phase by its index or by its name, as an admin would type either.
    /// </summary>
    public bool TryFindPhase(KsFieldCommanderRuleComponent component, string phase, out int phaseIndex)
    {
        if (int.TryParse(phase, out phaseIndex))
            return phaseIndex >= 0 && phaseIndex < component.Phases.Count;

        for (phaseIndex = 0; phaseIndex < component.Phases.Count; phaseIndex++)
        {
            var phasePrototype = component.Phases[phaseIndex];
            if (string.Equals(phasePrototype.Name, phase, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Loc.GetString(phasePrototype.Name), phase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     A summary of where a field commander is, for admins.
    /// </summary>
    public string GetStatus(Entity<KsFieldCommanderRuleComponent, GameRuleComponent> rule)
    {
        var component = rule.Comp1;
        var curTime = _gameTiming.CurTime;
        var elapsed = GetElapsed(component, rule.Comp2, curTime);

        var battlefield = SurveyBattlefield(component);

        var nextPhaseIn = component.PhaseIndex + 1 < component.Phases.Count
            ? FormatSeconds(component.Phases[component.PhaseIndex + 1].Start - elapsed)
            : "-";

        var nextWaveIn = component.NextWaveTime == TimeSpan.MaxValue
            ? "-"
            : FormatSeconds(component.NextWaveTime - curTime);

        var uplink = component.UplinkUid == null
            ? Loc.GetString("cmd-ks_fcstatus-uplink-none")
            : component.UplinkDestroyedTime != null
                ? Loc.GetString("cmd-ks_fcstatus-uplink-destroyed")
                : component.UplinkBombardmentTime is { } bombardmentTime
                    ? Loc.GetString("cmd-ks_fcstatus-uplink-active", ("seconds", FormatSeconds(bombardmentTime - curTime)))
                    : Loc.GetString("cmd-ks_fcstatus-uplink-active", ("seconds", "-"));

        var builder = new StringBuilder();
        builder.AppendLine(Loc.GetString("cmd-ks_fcstatus-summary",
            ("rule", ToPrettyString(rule.Owner)),
            ("phase", GetPhaseName(component)),
            ("index", component.PhaseIndex),
            ("elapsed", FormatSeconds(elapsed)),
            ("nextPhase", nextPhaseIn),
            ("nextWave", nextWaveIn),
            ("adaptive", component.AdaptiveMultiplier.ToString("0.00")),
            ("alive", CountLivingUnits(rule.Owner, null)),
            ("cap", GetMaxAlive(component, battlefield.Defenders.Count)),
            ("defenders", battlefield.Defenders.Count),
            ("dead", battlefield.DeadDefenders),
            ("waves", component.WavesSent),
            ("uplink", uplink)));

        foreach (var (userId, points) in component.Points)
        {
            var playerName = _playerManager.TryGetSessionById(userId, out var session) ? session.Name : userId.ToString();
            builder.AppendLine(Loc.GetString("cmd-ks_fcstatus-points", ("player", playerName), ("points", points)));
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatSeconds(TimeSpan time)
    {
        return ((int)Math.Ceiling(time.TotalSeconds)).ToString();
    }

    #endregion
}
