using Content.Server._KS14.NPC.Squad;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Toggles the NPC squad debug overlay for the invoking player. See <see cref="NpcSquadDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class SquadDebugCommand : LocalizedEntityCommands
{
    [Dependency] private NpcSquadDebugSystem _npcSquadDebugSystem = default!;

    public override string Command => "ks_squaddebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ks_squaddebug-no-session"));
            return;
        }

        shell.WriteLine(Loc.GetString(_npcSquadDebugSystem.Toggle(player)
            ? "cmd-ks_squaddebug-enabled"
            : "cmd-ks_squaddebug-disabled"));
    }
}
