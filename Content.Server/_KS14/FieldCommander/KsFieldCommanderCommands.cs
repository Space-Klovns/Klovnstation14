using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Shared plumbing for the field commander debug commands: each acts on every field commander running.
/// </summary>
public abstract partial class KsFieldCommanderCommandBase : LocalizedEntityCommands
{
    [Dependency] protected KsFieldCommanderRuleSystem FieldCommanderRuleSystem = default!;

    /// <summary>
    ///     The running field commanders, or none with an error written, so the command can stop.
    /// </summary>
    protected List<Entity<KsFieldCommanderRuleComponent, GameRuleComponent>> GetRules(IConsoleShell shell)
    {
        var rules = FieldCommanderRuleSystem.GetActiveRules();
        if (rules.Count == 0)
            shell.WriteError(Loc.GetString("cmd-ks_fieldcommander-no-rule"));

        return rules;
    }
}

[AdminCommand(AdminFlags.Round)]
public sealed partial class KsFieldCommanderPhaseCommand : KsFieldCommanderCommandBase
{
    public override string Command => "ks_fcphase";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        foreach (var rule in GetRules(shell))
        {
            if (!FieldCommanderRuleSystem.TryFindPhase(rule.Comp1, args[0], out var phaseIndex))
            {
                shell.WriteError(Loc.GetString("cmd-ks_fcphase-unknown", ("phase", args[0])));
                continue;
            }

            FieldCommanderRuleSystem.SetPhase(rule, phaseIndex);
            shell.WriteLine(Loc.GetString("cmd-ks_fcphase-set",
                ("phase", Loc.GetString(rule.Comp1.Phases[phaseIndex].Name)),
                ("index", phaseIndex)));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = new List<CompletionOption>();
        foreach (var rule in FieldCommanderRuleSystem.GetActiveRules())
        {
            for (var i = 0; i < rule.Comp1.Phases.Count; i++)
                options.Add(new CompletionOption(i.ToString(), Loc.GetString(rule.Comp1.Phases[i].Name)));
        }

        return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-ks_fcphase-hint"));
    }
}

[AdminCommand(AdminFlags.Round)]
public sealed partial class KsFieldCommanderWaveCommand : KsFieldCommanderCommandBase
{
    public override string Command => "ks_fcwave";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        foreach (var rule in GetRules(shell))
        {
            FieldCommanderRuleSystem.ForceWave((rule.Owner, rule.Comp1));
            shell.WriteLine(Loc.GetString("cmd-ks_fcwave-sent", ("waves", rule.Comp1.WavesSent)));
        }
    }
}

[AdminCommand(AdminFlags.Round)]
public sealed partial class KsFieldCommanderUplinkCommand : KsFieldCommanderCommandBase
{
    public override string Command => "ks_fcuplink";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        foreach (var rule in GetRules(shell))
        {
            if (rule.Comp1.UplinkUid != null)
            {
                shell.WriteError(Loc.GetString("cmd-ks_fcuplink-already"));
                continue;
            }

            FieldCommanderRuleSystem.ForceUplink((rule.Owner, rule.Comp1));
            shell.WriteLine(Loc.GetString(rule.Comp1.UplinkUid != null
                ? "cmd-ks_fcuplink-deployed"
                : "cmd-ks_fcuplink-failed"));
        }
    }
}

[AdminCommand(AdminFlags.Round)]
public sealed partial class KsFieldCommanderPointsCommand : KsFieldCommanderCommandBase
{
    [Dependency] private ISharedPlayerManager _playerManager = default!;

    public override string Command => "ks_fcpoints";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // ks_fcpoints <player> <amount> [set]
        if (args.Length is < 2 or > 3)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError(Loc.GetString("shell-target-player-does-not-exist"));
            return;
        }

        if (!int.TryParse(args[1], out var amount))
        {
            shell.WriteError(Loc.GetString("shell-argument-must-be-number"));
            return;
        }

        var set = args.Length == 3 && args[2] == "set";

        foreach (var rule in GetRules(shell))
        {
            var entity = (rule.Owner, rule.Comp1);
            if (set)
                FieldCommanderRuleSystem.SetPoints(entity, session, amount);
            else
                FieldCommanderRuleSystem.AdminAddPoints(entity, session, amount);

            shell.WriteLine(Loc.GetString("cmd-ks_fcpoints-done",
                ("player", session.Name),
                ("points", FieldCommanderRuleSystem.GetPoints(rule.Comp1, session.UserId))));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), Loc.GetString("cmd-ks_fcpoints-hint-player")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-ks_fcpoints-hint-amount")),
            3 => CompletionResult.FromHintOptions(new[] { new CompletionOption("set") }, Loc.GetString("cmd-ks_fcpoints-hint-set")),
            _ => CompletionResult.Empty,
        };
    }
}

[AdminCommand(AdminFlags.Round)]
public sealed partial class KsFieldCommanderStatusCommand : KsFieldCommanderCommandBase
{
    public override string Command => "ks_fcstatus";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        foreach (var rule in GetRules(shell))
            shell.WriteLine(FieldCommanderRuleSystem.GetStatus(rule));
    }
}
