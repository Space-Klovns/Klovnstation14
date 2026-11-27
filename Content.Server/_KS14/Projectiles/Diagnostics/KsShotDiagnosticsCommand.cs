using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.Projectiles.Diagnostics;

/// <summary>
///     <c>ks_shotdiag [net entity]</c>: has the caller's client print what decides whether a shot hits the mob under its
///         cursor (or the one nearest it, or the entity given), as that client has it and as the server has it, with the
///         differences marked. For when your own bullets fly through someone the server's hit.
/// </summary>
/// <remarks>
///     A server command although the client does the looking and the printing: what a client has about a mob (its
///         state, its damage, whether it can be hit) is more than a player is shown, and only a server command can be
///         gated by <see cref="AdminCommandAttribute"/>. The client gathers its half when told to from here.
/// </remarks>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class KsShotDiagnosticsCommand : LocalizedEntityCommands
{
    [Dependency] private KsShotDiagnosticsSystem _shotDiagnosticsSystem = default!;

    public override string Command => "ks_shotdiag";

    // Looked up by name: the base's ids are built from the command's, whose underscore no locale id may have.
    public override string Description => Loc.GetString("ks-shotdiag-command-desc");
    public override string Help => Loc.GetString("ks-shotdiag-command-help");

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } session)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("ks-shotdiag-command-help"));
            return;
        }

        NetEntity? targetNetEntity = null;
        if (args.Length == 1)
        {
            if (!NetEntity.TryParse(args[0], out var given) || !EntityManager.TryGetEntity(given, out _))
            {
                shell.WriteError(Loc.GetString("ks-shotdiag-command-bad-target", ("target", args[0])));
                return;
            }

            targetNetEntity = given;
        }

        _shotDiagnosticsSystem.Start(session, targetNetEntity);
    }
}
