using Content.Server.Administration;
using Content.Server._KS14.NPC.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.NPC.Commands;

/// <summary>
/// Toggles the tactical position debug overlay (candidate scores, chosen spot, live claims) for the
/// invoking player. See <see cref="NpcTacticalPositionDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class TacticalPositionDebugCommand : LocalizedEntityCommands
{
    [Dependency] private NpcTacticalPositionDebugSystem _npcTacticalPositionDebugSystem = default!;

    public override string Command => "ks_tacticalposdebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ks_tacticalposdebug-no-session"));
            return;
        }

        var enabled = _npcTacticalPositionDebugSystem.Toggle(player);
        shell.WriteLine(Loc.GetString(enabled
            ? "cmd-ks_tacticalposdebug-enabled"
            : "cmd-ks_tacticalposdebug-disabled"));
    }
}
