using Content.Server.Administration;
using Content.Shared._KS14.Perks;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Sets a defender's perk for the round, and swaps it onto the body they are in now.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed partial class KsFieldCommanderPerkCommand : KsFieldCommanderCommandBase
{
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override string Command => "ks_fcperk";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // ks_fcperk <player> <perk>
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError(Loc.GetString("shell-target-player-does-not-exist"));
            return;
        }

        if (!_prototypeManager.TryIndex<KsPerkPrototype>(args[1], out var perkPrototype))
        {
            shell.WriteError(Loc.GetString("cmd-ks_fcperk-unknown", ("perk", args[1])));
            return;
        }

        foreach (var rule in GetRules(shell))
        {
            FieldCommanderRuleSystem.SetChosenPerk((rule.Owner, rule.Comp1), session, perkPrototype.ID);
            shell.WriteLine(Loc.GetString("cmd-ks_fcperk-done",
                ("player", session.Name),
                ("perk", Loc.GetString(perkPrototype.Name))));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), Loc.GetString("cmd-ks_fcpoints-hint-player")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<KsPerkPrototype>(proto: _prototypeManager), Loc.GetString("cmd-ks_fcperk-hint-perk")),
            _ => CompletionResult.Empty,
        };
    }
}
