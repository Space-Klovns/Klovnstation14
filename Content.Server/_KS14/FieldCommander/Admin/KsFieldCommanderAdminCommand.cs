using Content.Server.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.FieldCommander.Admin;

/// <summary>
///     Opens the field commander admin window.
/// </summary>
[AdminCommand(KsFieldCommanderAdminEui.RequiredFlags)]
public sealed partial class KsFieldCommanderAdminCommand : LocalizedEntityCommands
{
    [Dependency] private KsFieldCommanderAdminEuiSystem _adminEuiSystem = default!;

    public override string Command => "ks_fcadmin";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _adminEuiSystem.OpenEui(player);
    }
}
