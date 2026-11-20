using System.Linq;
using Content.Server.Administration;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Lobby;
using Content.Shared.Administration;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server._KS14.Lobby;

/// <summary>
///     Shows or sets the lobby layout every client uses (<see cref="KsCCVars.LobbyLayout"/>). Clients switch live.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class KsLobbyLayoutCommand : LocalizedCommands
{
    [Dependency] private IConfigurationManager _configurationManager = default!;

    public override string Command => "lobbylayout";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            var layout = _configurationManager.GetCVar(KsCCVars.LobbyLayout);
            shell.WriteLine(Loc.GetString("cmd-lobbylayout-current",
                ("layout", layout), ("today", KsLobbyLayouts.Resolve(layout, DateTime.Now))));
            return;
        }

        if (args.Length != 1 || !KsLobbyLayouts.IsKnown(args[0]))
        {
            shell.WriteError(Loc.GetString("cmd-lobbylayout-unknown",
                ("layout", args[0]), ("layouts", string.Join(", ", KsLobbyLayouts.All))));
            return;
        }

        _configurationManager.SetCVar(KsCCVars.LobbyLayout, args[0]);
        shell.WriteLine(Loc.GetString("cmd-lobbylayout-set", ("layout", args[0])));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            KsLobbyLayouts.All.Select(layout => new CompletionOption(layout)),
            Loc.GetString("cmd-lobbylayout-arg-layout"));
    }
}
