using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client._KS14.Projectiles.Diagnostics;

/// <summary>
///     <c>ks_shotdiag [net entity]</c>: prints what decides whether a shot hits the mob under the cursor (or the one
///         nearest it, or the entity given), as this client has it and as the server has it, with the differences
///         marked. For when your own bullets fly through someone the server's hit. The server's half needs the debug
///         admin flag.
/// </summary>
[AnyCommand]
public sealed partial class KsShotDiagnosticsCommand : LocalizedEntityCommands
{
    [Dependency] private KsShotDiagnosticsSystem _shotDiagnosticsSystem = default!;

    public override string Command => "ks_shotdiag";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("cmd-ks_shotdiag-help"));
            return;
        }

        if (args.Length == 1)
        {
            if (!NetEntity.TryParse(args[0], out var targetNetEntity) ||
                !EntityManager.TryGetEntity(targetNetEntity, out var givenUid))
            {
                shell.WriteError(Loc.GetString("cmd-ks_shotdiag-bad-target", ("target", args[0])));
                return;
            }

            _shotDiagnosticsSystem.Diagnose(givenUid.Value, clickedUid: null, clickLookedUp: false);
            return;
        }

        if (_shotDiagnosticsSystem.FindTarget(out var clickedUid) is not { } targetUid)
        {
            shell.WriteError(Loc.GetString("cmd-ks_shotdiag-no-target"));
            return;
        }

        _shotDiagnosticsSystem.Diagnose(targetUid, clickedUid, clickLookedUp: true);
    }
}
