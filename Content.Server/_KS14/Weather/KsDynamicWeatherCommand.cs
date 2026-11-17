using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.Weather;

/// <summary>
///     Shows every running dynamic weather's state, or changes them all to a state now.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed partial class KsDynamicWeatherCommand : LocalizedEntityCommands
{
    [Dependency] private KsDynamicWeatherRuleSystem _dynamicWeatherRuleSystem = default!;

    public override string Command => "ks_weather";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        var rules = _dynamicWeatherRuleSystem.GetActiveRules();
        if (rules.Count == 0)
        {
            shell.WriteError(Loc.GetString("cmd-ks_weather-no-rule"));
            return;
        }

        foreach (var rule in rules)
        {
            if (args.Length == 1 && !_dynamicWeatherRuleSystem.TrySetState(rule, args[0]))
            {
                shell.WriteError(Loc.GetString("cmd-ks_weather-unknown", ("state", args[0])));
                continue;
            }

            var current = rule.Comp.StateIndex >= 0 ? rule.Comp.States[rule.Comp.StateIndex].Name : "-";
            shell.WriteLine(Loc.GetString("cmd-ks_weather-status",
                ("rule", EntityManager.ToPrettyString(rule.Owner)),
                ("state", current),
                ("states", string.Join(", ", rule.Comp.States.Select(state => state.Name)))));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var names = _dynamicWeatherRuleSystem.GetActiveRules()
            .SelectMany(rule => rule.Comp.States)
            .Select(state => state.Name)
            .Distinct();
        return CompletionResult.FromHintOptions(names, Loc.GetString("cmd-ks_weather-hint"));
    }
}
