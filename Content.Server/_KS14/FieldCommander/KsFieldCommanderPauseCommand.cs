using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Stops or restarts every running field commander's clock: <c>ks_fcpause</c> toggles, <c>on</c> and <c>off</c>
///         set it.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed partial class KsFieldCommanderPauseCommand : KsFieldCommanderCommandBase
{
    public override string Command => "ks_fcpause";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && args[0] is not ("on" or "off"))
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        // No argument toggles.
        bool? paused = args.Length == 1 ? args[0] == "on" : null;

        foreach (var rule in GetRules(shell))
        {
            var entity = (rule.Owner, rule.Comp1);
            var pause = paused ?? !FieldCommanderRuleSystem.IsPaused(rule.Comp1);
            FieldCommanderRuleSystem.SetPaused(entity, pause);
            shell.WriteLine(Loc.GetString(pause ? "cmd-ks_fcpause-paused" : "cmd-ks_fcpause-resumed"));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(new[] { new CompletionOption("on"), new CompletionOption("off") }, Loc.GetString("cmd-ks_fcpause-hint"))
            : CompletionResult.Empty;
    }
}
