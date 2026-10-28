using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.Procedural.Editor;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class KsProcgenThemeEditorCommand : LocalizedEntityCommands
{
    [Dependency] private EuiManager _euis = default!;
    public override string Command => "ksprocgen_theme";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player == null || args.Length > 1 || args.Length == 1 && args[0].Length > 64)
        { shell.WriteError(Loc.GetString("ks-procgen-theme-command-error")); return; }
        _euis.OpenEui(new KsProcgenThemeEditorEui(args.Length == 1 ? args[0] : null), shell.Player);
    }
}
